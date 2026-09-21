using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Coding.Evidence;
using HospitalAi.Application.Coding.Preprocessing;
using HospitalAi.Application.Coding.Rules;
using HospitalAi.Application.Coding.Scoring;
using HospitalAi.Application.Common;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Contracts.Models;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker.Tracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HospitalAi.Worker.Pipeline;

/// <summary>
/// 召回合并后的候选：保留各通道原始分与融合分，缺失通道为 null。
/// </summary>
internal sealed record MergedCandidate(
    string CodeSystem,
    string Code,
    string Title,
    decimal? ExactScore,
    decimal? Bm25Score,
    decimal FusedScore);

/// <summary>
/// 模型解释校验结果：Validated 表示通过 Schema 校验，Supported 表示解释能被证据支持。
/// </summary>
internal sealed record ModelExplanationCheck(bool Validated, bool Supported);

/// <summary>
/// 模型解释的一次调用结论。OutputInvalid 与 Available 必须分开：
/// 模型接通但输出始终不合法时禁止按"模型不可用"处理，否则会掩盖质量问题。
/// </summary>
internal sealed record ModelExplanationOutcome(
    ModelExplanationCheck? Check,
    bool OutputInvalid);

/// <summary>
/// V2.2-Lite 推荐流水线执行器。
/// 步骤：QUALITY_GATE → DOCUMENT_VERSION → CHUNK → FACT → EVIDENCE →
/// EXACT_RETRIEVAL → BM25_RETRIEVAL → RULE → SCORE → POLICY → PERSIST。
/// 所有步骤写入 pipeline_trace_step（stage / status / duration / 版本 / token），
/// 软失效旧推荐而不是物理删除；已确认 Final Coding 不被自动重跑覆盖。
/// </summary>
public sealed class V22LiteCodingPipelineRunner : ICodingTaskPipelineRunner, ICodingTaskPipelineCapabilities
{
    private static readonly string[] SupportedVersions = [PipelineVersions.Lite, PipelineVersions.Full];

    private readonly HospitalAiDbContext _dbContext;
    private readonly IExactCodingKnowledgeSearch _exactSearch;
    private readonly ICodingKnowledgeSearch _bm25Search;
    private readonly IModelGateway _modelGateway;
    private readonly IModelRouter _modelRouter;
    private readonly IJsonSchemaValidator _schemaValidator;
    private readonly IOptions<V22PipelineOptions> _options;
    private readonly ILogger<V22LiteCodingPipelineRunner> _logger;

    public V22LiteCodingPipelineRunner(
        HospitalAiDbContext dbContext,
        IExactCodingKnowledgeSearch exactSearch,
        ICodingKnowledgeSearch bm25Search,
        IModelGateway modelGateway,
        IModelRouter modelRouter,
        IJsonSchemaValidator schemaValidator,
        IOptions<V22PipelineOptions> options,
        ILogger<V22LiteCodingPipelineRunner> logger)
    {
        _dbContext = dbContext;
        _exactSearch = exactSearch;
        _bm25Search = bm25Search;
        _modelGateway = modelGateway;
        _modelRouter = modelRouter;
        _schemaValidator = schemaValidator;
        _options = options;
        _logger = logger;
    }

    public Task RunAsync(
        CodingTaskCreatedMessage message,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(message, diagnosisInputIds: null, cancellationToken);
    }

    /// <summary>
    /// Lite 阶段承接 V2.2 家族的 Lite 与 Full：语义召回与重排由模型网关是否启用来决定，
    /// 而不是由版本号分裂出另一套执行路径。
    /// </summary>
    public IReadOnlyCollection<string> SupportedPipelineVersions => SupportedVersions;

    /// <summary>
    /// 执行流水线。diagnosisInputIds 为空时处理任务的全部诊断输入；
    /// API 可指定单个诊断输入触发增量推荐。
    /// </summary>
    public async Task ExecuteAsync(
        CodingTaskCreatedMessage message,
        IReadOnlyList<Guid>? diagnosisInputIds,
        CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        var task = await _dbContext.CodingTasks
            .Include(item => item.Visit)
            .ThenInclude(visit => visit.Patient)
            .SingleOrDefaultAsync(
                item => item.Id == message.TaskId && item.HospitalId == message.HospitalId,
                cancellationToken)
            ?? throw new InvalidOperationException("编码任务不存在。");

        task.CodingStage = CodingStage.Processing;
        task.UpdatedAt = startedAt;

        var qualityIssues = new List<QualityIssueRecord>();
        var degradedFlags = new HashSet<string>(StringComparer.Ordinal);
        var recorder = new TraceRecorder(_dbContext, task, message.TraceId, runId);

        // Step 1：Data Quality Gate。
        var documents = await LoadCurrentDocumentsAsync(task, cancellationToken);
        var gateFailures = new List<string>();
        if (documents.Count == 0)
        {
            gateFailures.Add("就诊下没有可用的当前生效文书。");
        }

        foreach (var document in documents)
        {
            if (string.IsNullOrWhiteSpace(document.ContentReference))
            {
                gateFailures.Add($"文书 {document.DocumentType} 内容为空。");
            }
        }

        var duplicateHashes = documents
            .GroupBy(item => item.ContentHash)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateHashes.Count > 0)
        {
            gateFailures.Add("存在重复 content_hash 文书。");
        }

        if (task.Visit.DischargeAt is { } dischargeAt && dischargeAt < task.Visit.AdmissionAt)
        {
            gateFailures.Add("入院时间早于出院时间，文书时间不合法。");
        }

        if (gateFailures.Count > 0)
        {
            foreach (var failure in gateFailures)
            {
                qualityIssues.Add(CreateQualityIssue(
                    QualityIssueType.GranularityInsufficient, "HIGH", failure, task));
            }

            recorder.Record("QUALITY_GATE", "HUMAN_REQUIRED", null, startedAt);
            task.CodingStage = CodingStage.HumanRequired;
            task.Status = CodingTaskStatus.PendingReview;
            task.ErrorCode = string.Join("; ", gateFailures);
            task.UpdatedAt = DateTimeOffset.UtcNow;
            _dbContext.QualityIssues.AddRange(qualityIssues);
            await recorder.FlushAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        recorder.Record("QUALITY_GATE", "SUCCESS", null, startedAt);

        // Step 2 / 3：文档版本与 Chunk，写入 document_section（保留可复用的历史切片）。
        var sectionMap = await UpsertChunksAsync(task, documents, now: startedAt, cancellationToken);
        // 首页文书的入院 / 出院诊断段落字符区间，证据链按位置隔离用。
        var diagnosisSectionRanges = documents
            .Where(document => !string.IsNullOrEmpty(document.ContentReference))
            .ToDictionary(
                document => document.Id,
                document => ComputeDiagnosisSectionRanges(document.ContentReference));
        recorder.Record("DOCUMENT_VERSION", "SUCCESS", null, startedAt);
        recorder.Record("CHUNK", "SUCCESS", null, startedAt);

        // Step 4：Fact 与 Evidence 构建。
        var facts = new List<ClinicalFactRecord>();
        var evidences = new List<ClinicalEvidenceRecord>();
        var factEvidenceLinks = new List<ClinicalFactEvidenceRecord>();
        var documentTypes = documents.ToDictionary(item => item.Id, item => item.DocumentType);
        foreach (var document in documents)
        {
            var chunks = sectionMap[document.Id];
            foreach (var fact in ClinicalFactExtractor.Extract(chunks, document.Id, cancellationToken))
            {
                var chunk = chunks.FirstOrDefault(item =>
                    item.StartPosition <= fact.SourceStart && item.EndPosition >= fact.SourceStart)
                    ?? chunks.First();

                var factRecord = new ClinicalFactRecord
                {
                    Id = Guid.NewGuid(),
                    HospitalId = task.HospitalId,
                    VisitId = task.VisitId,
                    CodingTaskId = task.Id,
                    PipelineRunId = runId,
                    FactType = fact.FactType,
                    FactName = fact.FactName,
                    NormalizedValue = fact.NormalizedValue,
                    OriginalValue = fact.OriginalValue,
                    Negation = fact.Negation,
                    Certainty = fact.Certainty,
                    Temporality = fact.Temporality,
                    Status = "ACTIVE",
                    Confidence = fact.Confidence,
                    SourceDocumentId = fact.SourceDocumentId,
                    SourceSectionId = fact.SourceSectionId,
                    SourceStart = fact.SourceStart,
                    SourceEnd = fact.SourceEnd,
                    ExtractorVersion = fact.ExtractorVersion,
                    CreatedAt = startedAt,
                    UpdatedAt = startedAt
                };
                facts.Add(factRecord);

                var absoluteSpan = new TextChunk(
                    chunk.SectionId,
                    fact.SourceStart,
                    fact.SourceEnd,
                    fact.OriginalValue,
                    chunk.ContentHash);
                var evidence = EvidenceBuilder.Build(
                    fact,
                    absoluteSpan,
                    documentTypes.TryGetValue(document.Id, out var type) ? type : string.Empty,
                    document.Id,
                    fact.Temporality);
                var evidenceRecord = new ClinicalEvidenceRecord
                {
                    Id = Guid.NewGuid(),
                    HospitalId = task.HospitalId,
                    VisitId = task.VisitId,
                    CodingTaskId = task.Id,
                    PipelineRunId = runId,
                    EvidenceType = evidence.EvidenceType,
                    SourceType = evidence.SourceType,
                    DocumentId = evidence.DocumentId,
                    SectionId = evidence.SectionId,
                    OriginalText = evidence.OriginalText,
                    StartPosition = evidence.StartPosition,
                    EndPosition = evidence.EndPosition,
                    EvidenceLevel = evidence.Level,
                    SourceReliability = evidence.SourceReliability,
                    TemporalValidity = evidence.TemporalValidity,
                    TextCompleteness = evidence.TextCompleteness,
                    EvidenceScore = evidence.EvidenceScore,
                    Status = "ACTIVE",
                    CreatedAt = startedAt,
                    UpdatedAt = startedAt
                };
                evidences.Add(evidenceRecord);
                factEvidenceLinks.Add(new ClinicalFactEvidenceRecord
                {
                    Id = Guid.NewGuid(),
                    HospitalId = task.HospitalId,
                    FactId = factRecord.Id,
                    EvidenceId = evidenceRecord.Id,
                    CreatedAt = startedAt,
                    UpdatedAt = startedAt
                });
            }
        }

        _dbContext.ClinicalFacts.AddRange(facts);
        _dbContext.ClinicalEvidences.AddRange(evidences);
        _dbContext.ClinicalFactEvidences.AddRange(factEvidenceLinks);
        recorder.Record("FACT", "SUCCESS", null, startedAt);
        recorder.Record("EVIDENCE", "SUCCESS", null, startedAt);

        // 诊断输入：结构化输入优先；文档自动诊断 / 手术按原文去重复用。
        var autoInputs = facts
            .Where(item => (item.FactType == ClinicalFactType.Diagnosis || item.FactType == ClinicalFactType.Procedure)
                && !item.Negation
                // 疑似诊断不做自动编码输入（"考虑手足口病待排"），编码规范不允许按疑似出码。
                && item.Certainty is not (FactCertainty.RuledOut or FactCertainty.Suspected))
            .ToList();
        var createdInputIds = new List<Guid>();
        var diagnosisInputs = await ResolveDiagnosisInputsAsync(
            task, autoInputs, runId, startedAt, createdInputIds, cancellationToken);

        // 增量推荐必须同时覆盖文档自动解析出的输入（诊断 / 手术条目）：
        // 调用方传的往往是首页结构化输入 id，自动输入不在其中，
        // 只按请求 id 选择会让手术页签永远是 0。
        var selectedInputs = diagnosisInputIds is null || diagnosisInputIds.Count == 0
            ? diagnosisInputs
            : diagnosisInputs
                .Where(item => diagnosisInputIds.Contains(item.Id)
                    || createdInputIds.Contains(item.Id)
                    || item.SourceType == "DOCUMENT_AUTO"
                    || item.SourceType == "PROCEDURE_AUTO"
                    // 首页入院诊断段导入的结构化输入：调用方请求里通常只带出院 FRONT_PAGE id，
                    // 不纳入的话"入院诊断=出院诊断"的任务入院页签永远是 0。
                    || item.SourceType == "ADMISSION_PAGE")
                .ToList();

        // 本次运行会额外纳入新建的文档自动输入（手术条目），selectedInputs 多于请求数是预期；
        // 校验口径改为“请求的输入都在本任务内”，而不是数量相等。
        if (diagnosisInputIds is { Count: > 0 }
            && !diagnosisInputIds.All(item => selectedInputs.Any(selected => selected.Id == item)))
        {
            throw new ValidationException("指定的诊断输入不属于当前编码任务。");
        }

        var now = DateTimeOffset.UtcNow;
        if (selectedInputs.Count == 0)
        {
            task.CodingStage = qualityIssues.Count > 0 ? CodingStage.HumanRequired : CodingStage.Ready;
            task.Status = CodingTaskStatus.PendingReview;
            task.ErrorCode = "NO_DIAGNOSIS_INPUT";
            task.UpdatedAt = now;
            _dbContext.QualityIssues.AddRange(qualityIssues);
            recorder.Record("POLICY", "NO_SAFE_RECOMMENDATION", "NO_DIAGNOSIS_INPUT", startedAt);
            await recorder.FlushAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // Step 5：Exact Retrieval。
        var exactResults = new Dictionary<Guid, IReadOnlyList<CodingKnowledgeHit>>();
        foreach (var input in selectedInputs)
        {
            exactResults[input.Id] = await _exactSearch.SearchAsync(
                new ExactCodingKnowledgeQuery(
                    task.HospitalId,
                    input.NormalizedText ?? input.OriginalText,
                    settings.CodingVersion),
                cancellationToken);
        }

        // Step 6：BM25（ES 不可用时标记降级；未命中即 NO_SAFE_RECOMMENDATION，不回退 SQL 全量扫描）。
        var bm25Available = _bm25Search.IsAvailable;
        var bm25Results = new Dictionary<Guid, IReadOnlyList<CodingKnowledgeHit>>();
        if (bm25Available)
        {
            foreach (var input in selectedInputs)
            {
                bm25Results[input.Id] = await _bm25Search.SearchAsync(
                    new CodingKnowledgeSearchQuery(
                        task.HospitalId,
                        input.NormalizedText ?? input.OriginalText,
                        settings.CodingVersion,
                        settings.KnowledgeVersion,
                        settings.DocumentVersion,
                        ContentHash: null),
                    cancellationToken);
            }
        }
        else
        {
            degradedFlags.Add("DEGRADED.NO_BM25");
        }

        // Step 7：向量检索（Lite 阶段未接入，禁止伪向量 / 随机向量 / 伪造 SemanticScore）。
        degradedFlags.Add("DEGRADED.NO_VECTOR");

        // Step 8：规则引擎。
        var rules = settings.RuleEngineEnabled
            ? await LoadRulesAsync(task, settings, cancellationToken)
            : new List<RuleDefinition>();

        // Step 9：模型解释与一致性校验口径。
        var modelRoute = await _modelRouter.ResolveAsync(settings.ExplanationModelCode, cancellationToken);
        var modelOutcome = modelRoute.Available
            ? await GetValidatedExplanationAsync(selectedInputs, settings, cancellationToken)
            : new ModelExplanationOutcome(null, OutputInvalid: false);
        var modelExplanation = modelOutcome.Check;
        var modelExplanationValid = modelExplanation?.Validated ?? false;
        var explanationSupported = modelExplanation?.Supported ?? false;
        var modelVersion = modelExplanationValid ? settings.ExplanationModelCode : null;
        var promptVersion = modelExplanationValid ? settings.PromptVersion : null;
        if (!modelExplanationValid)
        {
            degradedFlags.Add("DEGRADED.NO_MODEL");
        }

        // 模型有响应但 Schema 重试后仍不合法：记录 LLM_OUTPUT_INVALID，
        // 该次输出不得进入任何评分口径。
        if (modelOutcome.OutputInvalid)
        {
            degradedFlags.Add("DEGRADED.LLM_OUTPUT_INVALID");
            qualityIssues.Add(CreateQualityIssue(
                QualityIssueType.LlmOutputInvalid,
                "HIGH",
                $"模型 {settings.ExplanationModelCode} 输出经 2 次重试仍未通过 Schema 校验，本次解释不参与评分。",
                task));
        }

        // Step 9 / 10 / 11：评分、Policy、持久化（软失效旧推荐）。
        await MarkPreviousRecommendationsStaleAsync(task, selectedInputs, now, cancellationToken);
        var existingVersions = await LoadRecommendationVersionsAsync(task, cancellationToken);
        var scoringOptions = new ScoringOptions();
        var candidates = new List<CodingCandidateRecord>();
        var scoreRecords = new List<RecommendationScoreRecord>();
        var recommendations = new List<CodingRecommendationRecord>();
        var recommendationEvidences = new List<RecommendationEvidenceRecord>();

        foreach (var input in selectedInputs)
        {
            var merged = MergeHits(exactResults[input.Id], bm25Results.GetValueOrDefault(input.Id) ?? []);
            var inputName = input.NormalizedText ?? input.OriginalText;
            // 类型隔离：手术输入只收 ICD-9 手术码、诊断输入只收 ICD-10 诊断码，
            // 否则 "腰椎间盘突出" 的诊断页会出现 "17.92650 腰椎间盘突出推拿治疗" 这类手术码。
            var procedureInput = input.SourceType.Contains("PROCEDURE", StringComparison.Ordinal);
            merged = merged
                .Where(candidate => IsProcedureCodeSystem(candidate.CodeSystem) == procedureInput)
                // ICD 星号复合码（"2型糖尿病性肩关节周围炎 E11.600X015+M14.2*"）要求病历中有糖尿病主诊断；
                // 输入未提糖尿病时这类候选即医生眼中的"糖尿病污染"，连精确分一并剔除。
                .Where(candidate => !IsUnjustifiedDiabetesCompound(candidate.Title, inputName))
                // 部位冲突："腰椎退行性病变" 召回 "颈椎/胸椎退行性病变"（字符覆盖 6/7、连续片段都挡不住），
                // 标题出现的部位/限定词必须是输入已声明的，否则剔除。
                .Where(candidate => !HasBodyPartConflict(candidate.Title, inputName))
                .ToList();
            var inputKey = NormalizeMatchKey(inputName);
            if (inputKey.Length >= 2)
            {
                // ICD 标题常为 "修饰语+核心词" 拼接，单字覆盖率挡不住 "甲周炎" 匹配 "肩周炎"。
                // 要求标题对输入的"连续片段覆盖率"（输入被共享子串覆盖的字符比例）≥60%：
                // "神经根型腰椎病" 与 "腰椎间盘突出伴神经根病" 共享 神经根+腰椎 = 5/7=0.71 通过，
                // 而 "电针治疗" 与 "芒针治疗" 只有 针+治疗 = 3/4=0.5 剔除；
                // 跨词二元组（根型/型腰）不参与打分，避免语序不同的正确 ICD 标题被误杀。
                // MaxSegment≥3 剔除 "甲周炎"↔"肩周炎"（仅共享 周炎=2/3）；
                // 但输入被多段完整覆盖（比例=1）时放行，否则 "子宫肌瘤"↔"子宫腺肌瘤"
                // 这类 "子宫+肌瘤" 两段拼接的标准标题会被误杀。
                merged = merged
                    .Where(candidate => candidate.ExactScore is not null
                        || TitleCoversInput(candidate.Title, inputName))
                    .Select(candidate => (candidate, Contiguous: ContiguousCover(candidate.Title, inputName)))
                    .Where(item => item.candidate.ExactScore is not null
                        || (item.Contiguous.Ratio >= 0.6m
                            && (inputKey.Length < 3
                                || item.Contiguous.MaxSegment >= 3
                                || item.Contiguous.Ratio >= 1.0m)))
                    .OrderByDescending(item => item.Contiguous.Ratio)
                    .ThenByDescending(item => item.candidate.FusedScore)
                    .Select(item => item.candidate)
                    .ToList();
            }

            var evidenceForInput = evidences
                .Where(evidence => MatchesInput(evidence, input))
                .OrderByDescending(evidence => evidence.EvidenceScore)
                .ToList();
            var evidenceScore = evidenceForInput.Count > 0 ? evidenceForInput[0].EvidenceScore : 0m;
            decimal? ComputeLlmScore() => modelExplanationValid
                ? LlmConsistencyScorer.Compute(new LlmConsistencyInput(
                    ModelAvailable: true,
                    OutputValidated: true,
                    EvidenceReferencesValid: evidenceForInput.Count > 0,
                    CandidateExistsInRetrieval: true,
                    ExplanationSupportedByEvidence: explanationSupported,
                    NegationOrTemporalViolation: false))
                : null;

            var scored = new List<(MergedCandidate Candidate, decimal Final, RuleEvaluationResult Rule, ScoreComputation Score)>();
            foreach (var candidate in merged.Take(settings.MaxCandidatesPerInput))
            {
                var ruleResult = RuleEngine.Evaluate(rules, new RuleEvaluationContext(
                    CandidateCode: candidate.Code,
                    CandidateCodeSystem: candidate.CodeSystem,
                    CandidateGranularity: DetermineGranularity(candidate.Code),
                    FactNegation: null,
                    FactCertainty: null,
                    FactTemporality: null,
                    FactFactType: nameof(ClinicalFactType.Diagnosis),
                    EvidenceLevel: null,
                    EvidenceScore: evidenceScore,
                    DocumentDocumentType: null));

                if (ruleResult.Blocked)
                {
                    qualityIssues.Add(CreateQualityIssue(
                        QualityIssueType.RuleViolation,
                        "HIGH",
                        $"候选 {candidate.Code} 命中阻断规则："
                            + string.Join(",", ruleResult.Matches.Select(item => item.RuleCode)),
                        task,
                        input.Id,
                        currentCode: candidate.Code));
                    continue;
                }

                var computed = SevenDimensionScorer.Compute(scoringOptions, new CandidateScoreInput(
                    ExactScore: candidate.ExactScore,
                    SemanticScore: null,
                    RetrievalScore: candidate.FusedScore,
                    RerankScore: null,
                    RuleScore: ruleResult.RuleScore,
                    EvidenceScore: evidenceScore,
                    LlmScore: ComputeLlmScore()));

                scored.Add((candidate, computed.FinalScore, ruleResult, computed));
            }

            var ranked = scored
                .OrderByDescending(item => item.Final)
                .Take(settings.MaxCandidatesPerInput)
                .ToList();
            var margin = ranked.Count >= 2 ? ranked[0].Final - ranked[1].Final : 0m;

            var policy = RecommendationPolicy.Evaluate(
                scoringOptions,
                new PolicyInput(
                    HasEvidence: evidenceForInput.Count > 0,
                    RuleBlocked: false,
                    GranularityInsufficient: ranked.Count > 0
                        && DetermineGranularity(ranked[0].Candidate.Code) == "GENERIC",
                    ModelAvailable: modelExplanation != null,
                    ModelOutputValidated: modelExplanationValid,
                    UnresolvedConflict: false,
                    QualityGateFailed: false,
                    DegradedFlags: degradedFlags.ToList()),
                finalScore: ranked.Count > 0 ? ranked[0].Final : 0m,
                evidenceScore: evidenceScore,
                margin: margin,
                rulePass: ranked.Count == 0 || ranked[0].Rule.RuleScore > 0);

            if (policy.Outcome == "NO_SAFE_RECOMMENDATION" && evidenceForInput.Count == 0)
            {
                qualityIssues.Add(CreateQualityIssue(
                    QualityIssueType.EvidenceInsufficient,
                    "HIGH",
                    "诊断输入缺少有效证据支撑，不生成安全推荐。",
                    task,
                    input.Id));
            }

            if (policy.Outcome == "NEED_REVIEW"
                && policy.Reason.Contains("粒度", StringComparison.Ordinal))
            {
                qualityIssues.Add(CreateQualityIssue(
                    QualityIssueType.GranularityInsufficient,
                    "MEDIUM",
                    policy.Reason,
                    task,
                    input.Id,
                    factId: null));
            }

            var version = NextVersion(existingVersions, input.Id);
            existingVersions[input.Id] = version;
            var rank = 1;
            foreach (var item in ranked)
            {
                var candidateId = Guid.NewGuid();
                candidates.Add(new CodingCandidateRecord
                {
                    Id = candidateId,
                    HospitalId = task.HospitalId,
                    CodingTaskId = task.Id,
                    DiagnosisInputId = input.Id,
                    PipelineRunId = runId,
                    CodeSystem = item.Candidate.CodeSystem,
                    Code = item.Candidate.Code,
                    Title = item.Candidate.Title,
                    RecallSource = "COMBINED",
                    ExactScore = item.Candidate.ExactScore,
                    Bm25Score = item.Candidate.Bm25Score,
                    VectorScore = null,
                    RerankScore = null,
                    RuleScore = item.Rule.RuleScore,
                    EvidenceScore = evidenceScore,
                    FinalScore = item.Final,
                    Rank = rank,
                    Status = "ACTIVE",
                    CreatedAt = now,
                    UpdatedAt = now
                });

                var recommendationId = Guid.NewGuid();
                scoreRecords.Add(new RecommendationScoreRecord
                {
                    Id = Guid.NewGuid(),
                    HospitalId = task.HospitalId,
                    RecommendationId = recommendationId,
                    ExactScore = item.Candidate.ExactScore,
                    SemanticScore = null,
                    RetrievalScore = item.Candidate.FusedScore,
                    RerankScore = null,
                    RuleScore = item.Rule.RuleScore,
                    EvidenceScore = evidenceScore,
                    LlmScore = ComputeLlmScore(),
                    MarginScore = rank == 1 ? margin : null,
                    ScoreProfile = item.Score.ScoreProfile,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                var outcomeForItem = rank == 1 ? policy.Outcome : "NEED_REVIEW";
                recommendations.Add(new CodingRecommendationRecord
                {
                    Id = recommendationId,
                    HospitalId = task.HospitalId,
                    CodingTaskId = task.Id,
                    DiagnosisInputId = input.Id,
                    PipelineVersion = PipelineVersions.Lite,
                    PipelineRunId = runId,
                    RecommendationVersion = version,
                    // 前端按 DIAGNOSIS / PROCEDURE 全大写口径分类；
                    // ICD-9-CM-3 是手术操作编码体系，其余按诊断处理（与 Legacy 口径一致）。
                    RecommendationType = IsProcedureCodeSystem(item.Candidate.CodeSystem)
                        ? "PROCEDURE"
                        : "DIAGNOSIS",
                    CodeSystemCode = item.Candidate.CodeSystem,
                    Code = item.Candidate.Code,
                    Title = item.Candidate.Title,
                    Rank = rank,
                    RecallScore = item.Candidate.FusedScore,
                    RuleScore = item.Rule.RuleScore,
                    ConfidenceScore = item.Final,
                    ReviewStatus = "PENDING_REVIEW",
                    Outcome = outcomeForItem,
                    LifecycleStatus = "ACTIVE",
                    EvidenceSufficiency = evidenceScore,
                    RiskLevel = policy.RiskLevel,
                    Reason = policy.Reason,
                    ModelVersion = modelVersion,
                    PromptVersion = promptVersion,
                    KnowledgeVersion = settings.KnowledgeVersion,
                    RuleVersion = settings.RuleVersion,
                    CodingVersion = settings.CodingVersion,
                    IsReadOnly = false,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                // 证据链只保留与推荐相关的证据：命中候选标题或医生诊断原文；
                // 不做无关兜底，避免整段病历里其他诊断的证据混入。
                var matchedTitle = item.Candidate.Title ?? string.Empty;
                var inputText = input.OriginalText ?? input.NormalizedText ?? string.Empty;
                var relevantEvidences = evidenceForInput
                    .Where(evidence => EvidenceMatchesTitle(evidence.OriginalText, matchedTitle)
                        || EvidenceMatchesTitle(evidence.OriginalText, inputText))
                    .OrderByDescending(evidence => evidence.EvidenceScore)
                    .ToList();
                // 段落隔离：入院诊断推荐只引用【入院诊断】段内的证据、出院（FRONT_PAGE）推荐
                // 只引用【出院诊断】段内的证据，否则重名诊断两条证据都挂上。
                // 按字符位置判定：并列条目切分后不带段落标签，靠标签文本会漏。
                // 文书无该段落或过滤为空时回退全部相关证据。
                var sectionEvidences = input.SourceType switch
                {
                    "ADMISSION_PAGE" or "FRONT_PAGE" => relevantEvidences
                        .Where(evidence => InDiagnosisSection(
                            evidence,
                            input.SourceType == "ADMISSION_PAGE",
                            diagnosisSectionRanges))
                        .ToList(),
                    _ => relevantEvidences,
                };
                var evidencesForCode = (sectionEvidences.Count > 0 ? sectionEvidences : relevantEvidences)
                    .Take(3)
                    .ToList();
                foreach (var evidence in evidencesForCode)
                {
                    var matched = EvidenceMatchesTitle(evidence.OriginalText, matchedTitle);
                    recommendationEvidences.Add(new RecommendationEvidenceRecord
                    {
                        Id = Guid.NewGuid(),
                        HospitalId = task.HospitalId,
                        CodingRecommendationId = recommendationId,
                        DocumentSectionId = evidence.SectionId,
                        PipelineRunId = runId,
                        EvidenceLevel = evidence.EvidenceLevel.ToString(),
                        SourceType = evidence.SourceType,
                        SourceText = evidence.OriginalText,
                        MatchText = matched ? matchedTitle : string.Empty,
                        Score = evidence.EvidenceScore,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }

                rank++;
            }

            _logger.LogInformation(
                "V2.2-Lite 推荐完成，TaskId={TaskId}, InputId={InputId}, Candidates={CandidateCount}, Outcome={Outcome}",
                task.Id,
                input.Id,
                ranked.Count,
                policy.Outcome);
        }

        _dbContext.CodingCandidates.AddRange(candidates);
        _dbContext.RecommendationScores.AddRange(scoreRecords);
        _dbContext.CodingRecommendations.AddRange(recommendations);
        _dbContext.RecommendationEvidences.AddRange(recommendationEvidences);
        _dbContext.QualityIssues.AddRange(qualityIssues);

        recorder.Record("EXACT_RETRIEVAL", "SUCCESS", null, startedAt);
        recorder.Record(
            "BM25_RETRIEVAL",
            bm25Available ? "SUCCESS" : "DEGRADED",
            bm25Available ? null : "DEGRADED.NO_BM25",
            startedAt);
        recorder.Record("RULE", "SUCCESS", null, startedAt);
        recorder.Record(
            "SCORE",
            modelExplanationValid ? "SUCCESS" : "DEGRADED",
            modelExplanationValid ? null : "DEGRADED.NO_MODEL",
            startedAt);
        recorder.Record("POLICY", "SUCCESS", null, startedAt);
        recorder.Record("PERSIST", "SUCCESS", null, startedAt);

        task.CodingStage = recommendations.Count > 0
            ? CodingStage.AiRecommending
            : CodingStage.HumanRequired;
        task.Status = CodingTaskStatus.PendingReview;
        task.CompletedAt = DateTimeOffset.UtcNow;
        task.UpdatedAt = task.CompletedAt.Value;
        await recorder.FlushAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool MatchesInput(ClinicalEvidenceRecord evidence, CodingDiagnosisInputRecord input)
    {
        return (evidence.DocumentId is { } documentId && input.SourceDocumentId == documentId)
            || (evidence.SectionId is { } sectionId && input.SourceSectionId == sectionId);
    }

    /// <summary>
    /// 首页文书【入院诊断】/【出院诊断】段的字符区间（半开）。缺少段落标签返回 null。
    /// </summary>
    private static (int AdmissionStart, int AdmissionEnd, int DischargeStart, int DischargeEnd)?
        ComputeDiagnosisSectionRanges(string content)
    {
        var admission = content.IndexOf("【入院诊断】", StringComparison.Ordinal);
        var discharge = content.IndexOf("【出院诊断】", StringComparison.Ordinal);
        if (discharge < 0)
        {
            return null;
        }

        var procedure = content.IndexOf("【手术操作】", StringComparison.Ordinal);
        var dischargeEnd = procedure > discharge ? procedure : content.Length;
        var admissionEnd = admission >= 0 && admission < discharge ? discharge : admission;
        return (admission, admissionEnd, discharge, dischargeEnd);
    }

    private static bool InDiagnosisSection(
        ClinicalEvidenceRecord evidence,
        bool admissionSection,
        IReadOnlyDictionary<Guid, (int AdmissionStart, int AdmissionEnd, int DischargeStart, int DischargeEnd)?> ranges)
    {
        if (evidence.DocumentId is not { } documentId
            || !ranges.TryGetValue(documentId, out var range)
            || range is null)
        {
            return false;
        }

        var start = evidence.StartPosition;
        return admissionSection
            ? range.Value.AdmissionStart >= 0
                && start >= range.Value.AdmissionStart
                && start < range.Value.AdmissionEnd
            : start >= range.Value.DischargeStart && start < range.Value.DischargeEnd;
    }

    private static bool EvidenceMatchesTitle(string? evidenceText, string title)
    {
        return title.Length >= 2
            && !string.IsNullOrWhiteSpace(evidenceText)
            && evidenceText.Contains(title, StringComparison.Ordinal);
    }

    /// <summary>
    /// 候选标题对输入名的字符覆盖率须达到 60% 才算相关。
    /// BM25 只按词频打分，"神经根型腰椎病" 会召回 "1型糖尿病伴神经根神经病变"（覆盖率约 0.43），
    /// 这类条目医生一眼就是"错误推荐"，宁缺毋滥。
    /// </summary>
    private static bool TitleCoversInput(string? title, string? inputText)
    {
        var normalizedTitle = NormalizeMatchKey(title);
        var normalizedInput = NormalizeMatchKey(inputText);
        if (normalizedTitle.Length == 0 || normalizedInput.Length == 0)
        {
            return false;
        }

        var titleCharacters = normalizedTitle.Distinct().ToHashSet();
        var inputCharacters = normalizedInput.Distinct().ToArray();
        var covered = inputCharacters.Count(character => titleCharacters.Contains(character));
        return (decimal)covered / inputCharacters.Length >= 0.6m;
    }

    /// <summary>
    /// 部位 / 特殊人群限定词。候选标题声明了输入没有的部位（"颈椎退行性病变" vs 输入 "腰椎退行性病变"、
    /// "角膜针刺术" vs 输入 "针刺"、"妊娠合并X" vs 普通输入）即视为冲突剔除；
    /// 标题不含任何限定词（"脊椎退行性病变"）则放行。
    /// </summary>
    /// <summary>
    /// 标题对输入的连续片段覆盖率：反复取输入与标题的最长公共子串，标记输入中已覆盖位置，
    /// 直到公共子串长度 &lt;2 为止，返回被覆盖的输入字符比例。
    /// 二元组命中率会因 ICD 标题语序与输入不同（"神经根型" 在标题里是 "伴神经根病"）被跨词片段拖低，
    /// 连续片段覆盖只认真正共享的词，既保住正确候选又挡住 "芒针治疗" 这类换字近码。
    /// </summary>
    private static (decimal Ratio, int MaxSegment) ContiguousCover(string? title, string? inputText)
    {
        var inputKey = NormalizeMatchKey(inputText);
        var titleKey = NormalizeMatchKey(title);
        if (inputKey.Length == 0)
        {
            return (0m, 0);
        }

        if (inputKey.Length == 1)
        {
            return titleKey.Contains(inputKey, StringComparison.Ordinal) ? (1m, 1) : (0m, 0);
        }

        var covered = new bool[inputKey.Length];
        var coveredCount = 0;
        var maxSegment = 0;
        var currentTitle = titleKey;
        while (true)
        {
            var (start, length) = LongestCommonSubstring(inputKey, currentTitle);
            if (length < 2)
            {
                break;
            }

            maxSegment = Math.Max(maxSegment, length);
            for (var index = start; index < start + length; index++)
            {
                if (!covered[index])
                {
                    covered[index] = true;
                    coveredCount++;
                }
            }

            // 从标题中去掉该片段，避免同一子串反复贡献。
            currentTitle = currentTitle.Remove(
                currentTitle.IndexOf(inputKey.AsSpan(start, length)), length);
        }

        return (coveredCount / (decimal)inputKey.Length, maxSegment);
    }

    private static (int Start, int Length) LongestCommonSubstring(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return (0, 0);
        }

        var previous = new int[right.Length + 1];
        var bestStart = 0;
        var bestLength = 0;
        for (var i = left.Length - 1; i >= 0; i--)
        {
            var currentRow = new int[right.Length + 1];
            for (var j = right.Length - 1; j >= 0; j--)
            {
                if (left[i] == right[j])
                {
                    currentRow[j] = previous[j + 1] + 1;
                    if (currentRow[j] > bestLength)
                    {
                        bestLength = currentRow[j];
                        bestStart = i;
                    }
                }
            }

            previous = currentRow;
        }

        return (bestStart, bestLength);
    }

    /// <summary>
    /// 部位 / 特殊人群限定词。候选标题声明了输入没有的部位（"颈椎退行性病变" vs 输入 "腰椎退行性病变"、
    /// "角膜针刺术" vs 输入 "针刺"、"妊娠合并X" vs 普通输入）即视为冲突剔除；
    /// 标题不含任何限定词（"脊椎退行性病变"）则放行。
    /// </summary>
    private static readonly string[] BodyQualifiers =
        ["颈", "胸", "腰", "骶", "尾", "肩", "肘", "腕", "髋", "膝", "踝", "足", "手",
            "头", "腹", "耳", "鼻", "牙", "眼", "角膜", "妊娠"];

    private static bool HasBodyPartConflict(string? title, string? inputText)
    {
        var titleQualifiers = ExtractQualifiers(title);
        return titleQualifiers.Count > 0
            && !titleQualifiers.IsSubsetOf(ExtractQualifiers(inputText));
    }

    private static HashSet<string> ExtractQualifiers(string? text)
    {
        var normalized = NormalizeMatchKey(text);
        return BodyQualifiers
            .Where(word => normalized.Contains(word, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsUnjustifiedDiabetesCompound(string? title, string? inputText)
    {
        var titleKey = NormalizeMatchKey(title);
        if (!titleKey.Contains("糖尿病", StringComparison.Ordinal))
        {
            return false;
        }

        var inputKey = NormalizeMatchKey(inputText);
        return !inputKey.Contains("糖尿病", StringComparison.Ordinal);
    }

    private static string NormalizeMatchKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return new string(text
            .Where(character => !char.IsPunctuation(character) && !char.IsWhiteSpace(character))
            .ToArray())
            .ToLowerInvariant();
    }

    private static bool IsProcedureCodeSystem(string? codeSystem)
    {
        return codeSystem is not null
            && codeSystem.Contains("ICD-9", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetermineGranularity(string code)
    {
        return code.Length >= 3 ? "SPECIFIC" : "GENERIC";
    }

    private async Task<ModelExplanationOutcome> GetValidatedExplanationAsync(
        IReadOnlyList<CodingDiagnosisInputRecord> inputs,
        V22PipelineOptions settings,
        CancellationToken cancellationToken)
    {
        // LlmScore 一致性校验口径：模型输出必须通过 Schema 校验，最多重试 2 次。
        const string schemaJson = """
            {
              "type": "object",
              "required": ["explanation", "supported"],
              "properties": {
                "explanation": { "type": "string" },
                "supported": { "type": "boolean" }
              },
              "additionalProperties": false
            }
            """;

        var request = new ModelGenerationRequest(
            settings.ExplanationModelCode,
            $"诊断输入数量：{inputs.Count}。请判断推荐解释能否被证据支持。",
            SystemPrompt: "你是编码推荐一致性校验器，只输出 JSON。",
            ResponseSchemaJson: schemaJson);

        var invalid = false;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var result = await _modelGateway.GenerateAsync(request, cancellationToken);
            if (!result.Success || result.ContentJson is null)
            {
                _logger.LogWarning(
                    "模型解释请求失败，ModelCode={ModelCode}, Attempt={Attempt}, ErrorCode={ErrorCode}",
                    result.ModelCode,
                    attempt,
                    result.ErrorCode);
                continue;
            }

            var validation = _schemaValidator.Validate(schemaJson, result.ContentJson);
            if (!validation.IsValid)
            {
                // 只记录错误条数：AI 响应内容不得进入普通应用日志。
                _logger.LogWarning(
                    "模型输出未通过 Schema 校验，ModelCode={ModelCode}, Attempt={Attempt}, ErrorCount={ErrorCount}",
                    result.ModelCode,
                    attempt,
                    validation.Errors.Count);
                invalid = true;
                continue;
            }

            return new ModelExplanationOutcome(
                new ModelExplanationCheck(
                    Validated: true,
                    Supported: ReadSupportedFlag(result.ContentJson)),
                OutputInvalid: false);
        }

        // 重试耗尽：区分"根本没响应"与"响应了但始终不合法"。
        return new ModelExplanationOutcome(null, OutputInvalid: invalid);
    }

    /// <summary>
    /// 读取模型输出中的 supported 字段。Schema 已校验通过，解析异常按“无法被证据支持”处理。
    /// </summary>
    private static bool ReadSupportedFlag(string contentJson)
    {
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            return document.RootElement.TryGetProperty("supported", out var element)
                && element.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static QualityIssueRecord CreateQualityIssue(
        QualityIssueType type,
        string riskLevel,
        string description,
        CodingTaskRecord task,
        Guid? diagnosisInputId = null,
        Guid? factId = null,
        string? currentCode = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new QualityIssueRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = task.HospitalId,
            VisitId = task.VisitId,
            CodingTaskId = task.Id,
            DiagnosisInputId = diagnosisInputId,
            IssueType = type,
            RiskLevel = riskLevel,
            Description = description,
            FactId = factId,
            // 问题涉及的编码必须落到结构化列：人工复核要能按编码筛选问题，
            // 只写在描述里就无法查询和统计。
            CurrentCode = currentCode,
            Status = "OPEN",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private async Task<List<MedicalDocumentRecord>> LoadCurrentDocumentsAsync(
        CodingTaskRecord task,
        CancellationToken cancellationToken)
    {
        return await _dbContext.MedicalDocuments
            .AsNoTracking()
            .Where(item => item.HospitalId == task.HospitalId
                && item.VisitId == task.VisitId
                && item.IsCurrent
                && item.DocumentStatus == "ACTIVE")
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, List<TextChunk>>> UpsertChunksAsync(
        CodingTaskRecord task,
        IReadOnlyList<MedicalDocumentRecord> documents,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var documentIds = documents.Select(item => item.Id).ToList();
        var existing = (await _dbContext.DocumentSections
            .Where(item => item.HospitalId == task.HospitalId
                && documentIds.Contains(item.MedicalDocumentId))
            .ToListAsync(cancellationToken))
            .GroupBy(item => item.MedicalDocumentId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var result = new Dictionary<Guid, List<TextChunk>>();
        foreach (var document in documents)
        {
            var chunks = DocumentChunker.Chunk(document.ContentReference);
            existing.TryGetValue(document.Id, out var currentSections);
            var byHash = currentSections?.ToDictionary(item => item.ContentHash, item => item)
                ?? [];
            var documentChunks = new List<TextChunk>();
            foreach (var chunk in chunks)
            {
                if (byHash.TryGetValue(chunk.ContentHash, out var section))
                {
                    section.IndexStatus = "INDEXED";
                    section.UpdatedAt = now;
                    documentChunks.Add(chunk with { SectionId = section.Id });
                    continue;
                }

                var created = new DocumentSectionRecord
                {
                    Id = Guid.NewGuid(),
                    HospitalId = task.HospitalId,
                    VisitId = task.VisitId,
                    MedicalDocumentId = document.Id,
                    SectionType = "CHUNK",
                    Title = document.DocumentType,
                    Content = chunk.Text,
                    Sequence = documentChunks.Count + 1,
                    StartPosition = chunk.StartPosition,
                    EndPosition = chunk.EndPosition,
                    TokenCount = EstimateTokens(chunk.Text),
                    ContentHash = chunk.ContentHash,
                    EmbeddingStatus = "UNAVAILABLE",
                    IndexStatus = "INDEXED",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _dbContext.DocumentSections.Add(created);
                documentChunks.Add(chunk with { SectionId = created.Id });
            }

            result[document.Id] = documentChunks;
        }

        return result;
    }

    private async Task<List<CodingDiagnosisInputRecord>> ResolveDiagnosisInputsAsync(
        CodingTaskRecord task,
        IReadOnlyList<ClinicalFactRecord> diagnosisFacts,
        Guid runId,
        DateTimeOffset now,
        List<Guid> createdIds,
        CancellationToken cancellationToken)
    {
        // 去重只看 ACTIVE 输入：被治理停用的污染输入不得阻止干净条目重建。
        var existing = (await _dbContext.CodingDiagnosisInputs
            .Where(item => item.HospitalId == task.HospitalId && item.CodingTaskId == task.Id)
            .ToListAsync(cancellationToken))
            .Where(item => item.Status == "ACTIVE")
            .ToList();

        // 归一化后可能多条输入撞到同一个键（历史脏数据），取第一条而不是直接抛异常。
        // 同键优先 FRONT_PAGE：入院 / 出院重名时两条输入并存是预期，
        // 出院段抽取出的事实必须挂到出院输入，否则推荐会串进入院页签。
        var byText = existing
            .GroupBy(item => NormalizeMatchKey(item.OriginalText), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(item => item.SourceType == "FRONT_PAGE" ? 0 : 1)
                    .ThenBy(item => item.DiagnosisOrder)
                    .First(),
                StringComparer.Ordinal);
        var order = existing.Count > 0
            ? existing.Max(item => item.DiagnosisOrder) + 1
            : 1;

        foreach (var fact in diagnosisFacts.OrderBy(item => item.SourceStart))
        {
            // 医生已在结构化输入里写过的诊断，文档自动条目不得再建一条：
            // 按标点归一化后比较，否则 "2型糖尿病" 与 "2型糖尿病（E11900）" 会各建一个输入，推荐翻倍。
            if (byText.ContainsKey(NormalizeMatchKey(fact.FactName)))
            {
                continue;
            }

            var record = new CodingDiagnosisInputRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = task.HospitalId,
                VisitId = task.VisitId,
                CodingTaskId = task.Id,
                SourceType = fact.FactType == ClinicalFactType.Procedure
                    ? "PROCEDURE_AUTO"
                    : "DOCUMENT_AUTO",
                OriginalText = fact.FactName,
                NormalizedText = fact.FactName,
                // 主诊断只能来自诊断事实；手术条目不能抢占主诊断位。
                IsPrincipal = fact.FactType != ClinicalFactType.Procedure
                    && !existing.Any(item => item.IsPrincipal),
                DiagnosisOrder = order,
                SourceDocumentId = fact.SourceDocumentId,
                SourceSectionId = fact.SourceSectionId,
                SourceStart = fact.SourceStart,
                SourceEnd = fact.SourceEnd,
                Status = "ACTIVE",
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.CodingDiagnosisInputs.Add(record);
            existing.Add(record);
            byText[NormalizeMatchKey(record.OriginalText)] = record;
            createdIds.Add(record.Id);
            order++;
        }

        return existing.Where(item => item.Status == "ACTIVE").ToList();
    }

    private async Task<List<RuleDefinition>> LoadRulesAsync(
        CodingTaskRecord task,
        V22PipelineOptions settings,
        CancellationToken cancellationToken)
    {
        var records = await _dbContext.CodingRules
            .AsNoTracking()
            .Where(item => item.HospitalId == task.HospitalId
                && item.IsEnabled
                && item.RuleVersion == settings.RuleVersion)
            .ToListAsync(cancellationToken);

        return records
            .Select(item => new RuleDefinition(
                item.RuleCode,
                item.RuleVersion,
                item.Priority,
                item.RuleGroup,
                item.Severity,
                item.Message,
                item.Blocking,
                item.IsBuiltin,
                item.ConditionJson,
                item.ActionJson))
            .ToList();
    }

    private async Task MarkPreviousRecommendationsStaleAsync(
        CodingTaskRecord task,
        IReadOnlyList<CodingDiagnosisInputRecord> inputs,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // 软失效：旧推荐标记 STALE，旧证据 / Candidate / Score 保留供审计回放。
        // 已人工确认并进入 Final Coding 的推荐不参与软失效，禁止自动重跑覆盖。
        var inputIds = inputs.Select(item => item.Id).ToList();
        var confirmedRecommendationIds = await _dbContext.FinalCodingResults
            .Where(item => item.HospitalId == task.HospitalId
                && item.CodingTaskId == task.Id
                && item.SourceRecommendationId.HasValue)
            .Select(item => item.SourceRecommendationId!.Value)
            .ToListAsync(cancellationToken);
        var previous = await _dbContext.CodingRecommendations
            .Where(item => item.HospitalId == task.HospitalId
                && item.CodingTaskId == task.Id
                && item.DiagnosisInputId.HasValue
                && inputIds.Contains(item.DiagnosisInputId.Value)
                && item.LifecycleStatus == "ACTIVE"
                && !item.IsReadOnly
                && !confirmedRecommendationIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        foreach (var recommendation in previous)
        {
            recommendation.LifecycleStatus = "STALE";
            recommendation.UpdatedAt = now;
        }
    }

    private async Task<Dictionary<Guid, string>> LoadRecommendationVersionsAsync(
        CodingTaskRecord task,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, string>();
        var existing = await _dbContext.CodingRecommendations
            .Where(item => item.HospitalId == task.HospitalId
                && item.CodingTaskId == task.Id
                && item.DiagnosisInputId.HasValue
                && item.RecommendationVersion != null)
            .Select(item => new { item.DiagnosisInputId, item.RecommendationVersion })
            .ToListAsync(cancellationToken);

        foreach (var item in existing)
        {
            if (item.DiagnosisInputId is null || item.RecommendationVersion is null)
            {
                continue;
            }

            if (!result.TryGetValue(item.DiagnosisInputId.Value, out var current)
                || CompareVersions(item.RecommendationVersion, current) > 0)
            {
                result[item.DiagnosisInputId.Value] = item.RecommendationVersion;
            }
        }

        return result;
    }

    private static string NextVersion(Dictionary<Guid, string> versions, Guid inputId)
    {
        if (!versions.TryGetValue(inputId, out var current))
        {
            return "r-1";
        }

        var separator = current.LastIndexOf('-');
        return separator >= 0 && int.TryParse(current.AsSpan(separator + 1), out var index)
            ? $"r-{index + 1}"
            : "r-1";
    }

    private static int CompareVersions(string left, string right)
    {
        var leftIndex = ParseVersionIndex(left);
        var rightIndex = ParseVersionIndex(right);
        return leftIndex.CompareTo(rightIndex);
    }

    private static int ParseVersionIndex(string value)
    {
        var separator = value.LastIndexOf('-');
        return separator >= 0 && int.TryParse(value.AsSpan(separator + 1), out var index)
            ? index
            : 0;
    }

    private static IReadOnlyList<MergedCandidate> MergeHits(
        IReadOnlyList<CodingKnowledgeHit> exact,
        IReadOnlyList<CodingKnowledgeHit> bm25)
    {
        var merged = new Dictionary<string, MergedCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in exact)
        {
            var score = ToScore(hit.Score);
            merged[$"{hit.CodeSystemCode}:{hit.Code}"] = new MergedCandidate(
                hit.CodeSystemCode, hit.Code, hit.Title, score, null, score);
        }

        foreach (var hit in bm25)
        {
            var score = ToScore(hit.Score);
            var key = $"{hit.CodeSystemCode}:{hit.Code}";
            if (merged.TryGetValue(key, out var existing))
            {
                merged[key] = existing with
                {
                    Bm25Score = score,
                    FusedScore = Math.Max(existing.FusedScore, score)
                };
                continue;
            }

            merged[key] = new MergedCandidate(
                hit.CodeSystemCode, hit.Code, hit.Title, null, score, score);
        }

        // 同名条目只保留分数最高的一条：ICD 库里 "腰椎退行性病变" 有 M48.901~904 等多条扩展码，
        // 全部端给医生就是用户看到的"重复推荐"。
        return merged.Values
            .OrderByDescending(item => item.FusedScore)
            .GroupBy(item => NormalizeMatchKey(item.Title), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(item => item.FusedScore)
            .ToList();
    }

    /// <summary>
    /// 检索分数统一按 decimal(5,4) 口径落库，避免浮点误差影响排序稳定性。
    /// </summary>
    private static decimal ToScore(double score)
    {
        return Math.Round((decimal)score, 4);
    }

    private static int EstimateTokens(string text)
    {
        // 粗略 token 近似：CJK 字符按 1 token，其余按 4 字符 1 token。
        var cjk = text.Count(character => character >= 0x2E80);
        var other = text.Length - cjk;
        return Math.Max(1, cjk + (other + 3) / 4);
    }

    /// <summary>
    /// Trace 步骤记录器。根 Trace 一个任务一条，按 trace_id 定位；
    /// 每个 stage 写一条 pipeline_trace_step，携带 run_id、版本与耗时。
    /// </summary>
    private sealed class TraceRecorder(
        HospitalAiDbContext dbContext,
        CodingTaskRecord task,
        string traceId,
        Guid runId)
    {
        private readonly List<PipelineTraceStepRecord> _steps = [];

        public void Record(
            string stage,
            string status,
            string? errorCode,
            DateTimeOffset startedAt,
            string? modelVersion = null,
            int? inputTokens = null,
            int? outputTokens = null)
        {
            var now = DateTimeOffset.UtcNow;
            _steps.Add(new PipelineTraceStepRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = task.HospitalId,
                PipelineTraceId = Guid.Empty,
                PipelineRunId = runId,
                Stage = stage,
                StepName = stage.ToLowerInvariant(),
                Status = status,
                StartedAt = startedAt,
                CompletedAt = now,
                DurationMs = (long)(now - startedAt).TotalMilliseconds,
                ErrorCode = errorCode,
                ModelVersion = modelVersion,
                KnowledgeVersion = null,
                RuleVersion = null,
                PromptVersion = null,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            var trace = await dbContext.PipelineTraces
                .FirstOrDefaultAsync(
                    item => item.HospitalId == task.HospitalId
                        && item.CodingTaskId == task.Id
                        && item.TraceId == traceId,
                    cancellationToken)
                ?? dbContext.PipelineTraces.Local.FirstOrDefault(
                    item => item.HospitalId == task.HospitalId
                        && item.CodingTaskId == task.Id
                        && item.TraceId == traceId);

            if (trace is null)
            {
                // 每次运行可携带独立的 X-Trace-Id：任务创建时的根 Trace 匹配不到时，
                // 为本次运行新建 Trace 头，避免步骤外键指向不存在的 id。
                var now = DateTimeOffset.UtcNow;
                trace = new PipelineTraceRecord
                {
                    Id = Guid.NewGuid(),
                    HospitalId = task.HospitalId,
                    CodingTaskId = task.Id,
                    TraceId = traceId,
                    Status = "RUNNING",
                    StartedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                dbContext.PipelineTraces.Add(trace);
            }

            var traceRecordId = trace.Id;
            foreach (var step in _steps)
            {
                step.PipelineTraceId = traceRecordId;
            }

            if (trace is not null)
            {
                trace.Status = _steps.Count > 0 ? _steps[^1].Status : trace.Status;
                trace.UpdatedAt = DateTimeOffset.UtcNow;
            }

            dbContext.PipelineTraceSteps.AddRange(_steps);
            _steps.Clear();
        }
    }
}
