using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Coding.Cm3Index;
using HospitalAi.Application.Coding.Evidence;
using HospitalAi.Application.Coding.IcdIndex;
using HospitalAi.Application.Coding.Matching;
using HospitalAi.Application.Coding.Preprocessing;
using HospitalAi.Application.Coding.Rules;
using HospitalAi.Application.Coding.Scoring;
using HospitalAi.Application.Common;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Contracts.Models;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.Elasticsearch;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker.Tracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HospitalAi.Worker.Pipeline;

/// <summary>
/// 召回合并后的候选：保留各通道原始分与融合分，缺失通道为 null。
/// RecallChannel：DICTIONARY=Exact/BM25 字典直匹配（手术通道）；ICD_INDEX=诊断索引两段式；
/// CM3_INDEX=手术索引两段式（索引侧字段与 ICD_INDEX 同一套语义，直接复用）。
/// </summary>
internal sealed record MergedCandidate(
    string CodeSystem,
    string Code,
    string Title,
    decimal? ExactScore,
    decimal? Bm25Score,
    decimal FusedScore,
    string RecallChannel = "DICTIONARY",
    string? IndexEvidencePath = null,
    bool IsIndexFallback = false,
    string? IndexNote = null,
    decimal IndexTitleDepth = 0m,
    decimal? VectorScore = null,
    decimal? RerankScore = null,
    /// <summary>索引同族展开批次内的名次（0 = 族内最优），注入 ExactScore 以抗重排噪声。</summary>
    int FamilyRank = 0);

/// <summary>
/// 模型一致性校验的调用结论形态。四种状态必须分开：
/// 折叠成"模型不可用"会掩盖模型侧真实的质量问题（验证文档 §模型输出不合法不得当作不可用）。
/// </summary>
internal enum ExplanationState
{
    /// <summary>模型路由不可用，未发起调用。</summary>
    NotRequested,

    /// <summary>调用了但没有任何响应（网络/服务错误）。</summary>
    Unavailable,

    /// <summary>有响应但重试后仍未通过 Schema 校验。</summary>
    OutputInvalid,

    /// <summary>通过 Schema 校验，字段可进入判据换算。</summary>
    Validated
}

/// <summary>
/// 单个候选的模型自述。四个业务判据不在这里算，
/// 由 LlmConsistencyJudge 用检索池与证据原文复核后换算。
/// </summary>
internal sealed record CandidateExplanation(
    ExplanationState State,
    string? EchoedCode = null,
    bool Supported = false,
    IReadOnlyList<int>? CitedEvidenceIndexes = null,
    bool ModelReportsViolation = false);

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
    private readonly IVectorSearch _vectorSearch;
    private readonly IReranker _reranker;
    private readonly IModelGateway _modelGateway;
    private readonly IModelRouter _modelRouter;
    private readonly IJsonSchemaValidator _schemaValidator;
    private readonly IIcdIndexSource _indexSource;
    private readonly ITumorIndexSource _tumorIndexSource;
    private readonly ICm3IndexSource _cm3IndexSource;
    private readonly IIcdDictionarySource _dictionarySource;
    private readonly IIcdTermSynonymSource _termSynonymSource;
    private readonly IOptions<V22PipelineOptions> _options;
    private readonly ILogger<V22LiteCodingPipelineRunner> _logger;

    public V22LiteCodingPipelineRunner(
        HospitalAiDbContext dbContext,
        IExactCodingKnowledgeSearch exactSearch,
        ICodingKnowledgeSearch bm25Search,
        IVectorSearch vectorSearch,
        IReranker reranker,
        IModelGateway modelGateway,
        IModelRouter modelRouter,
        IJsonSchemaValidator schemaValidator,
        IIcdIndexSource indexSource,
        ITumorIndexSource tumorIndexSource,
        ICm3IndexSource cm3IndexSource,
        IIcdDictionarySource dictionarySource,
        IIcdTermSynonymSource termSynonymSource,
        IOptions<V22PipelineOptions> options,
        ILogger<V22LiteCodingPipelineRunner> logger)
    {
        _dbContext = dbContext;
        _exactSearch = exactSearch;
        _bm25Search = bm25Search;
        _vectorSearch = vectorSearch;
        _reranker = reranker;
        _modelGateway = modelGateway;
        _modelRouter = modelRouter;
        _schemaValidator = schemaValidator;
        _indexSource = indexSource;
        _tumorIndexSource = tumorIndexSource;
        _cm3IndexSource = cm3IndexSource;
        _dictionarySource = dictionarySource;
        _termSynonymSource = termSynonymSource;
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
    /// 手术输入判据（SourceType 含 PROCEDURE：文档解析的手术条目与首页手术录入）。
    /// 诊断索引与 CM3 索引按互补的两半边筛输入，这个口径在本文件用了 6 处，
    /// 留成 6 份字面量比较迟早分叉。
    /// </summary>
    private static bool IsProcedureInput(CodingDiagnosisInputRecord input)
        => input.SourceType.Contains("PROCEDURE", StringComparison.Ordinal);

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

        // 通道分流：诊断输入走 ICD 索引两段式，手术输入走 CM3 索引两段式（Step 6.7）。
        // 两条索引链路都失败时，手术侧仍退回今天的 Exact/BM25 字典通道，诊断侧不回退（决策 2）。
        var procedureInputs = selectedInputs.Where(IsProcedureInput).ToList();

        // Step 5：Exact Retrieval（仅手术通道）。
        var exactResults = new Dictionary<Guid, IReadOnlyList<CodingKnowledgeHit>>();
        foreach (var input in procedureInputs)
        {
            exactResults[input.Id] = await _exactSearch.SearchAsync(
                new ExactCodingKnowledgeQuery(
                    task.HospitalId,
                    input.NormalizedText ?? input.OriginalText,
                    settings.CodingVersion),
                cancellationToken);
        }

        // Step 6：BM25（ES 不可用/检索失败都标记降级；未命中即 NO_SAFE_RECOMMENDATION，不回退 SQL 全量扫描）。
        var bm25Available = _bm25Search.IsAvailable;
        var bm25Degraded = false;
        var bm25Results = new Dictionary<Guid, IReadOnlyList<CodingKnowledgeHit>>();
        if (bm25Available)
        {
            foreach (var input in procedureInputs)
            {
                try
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
                catch (ElasticsearchUnavailableException exception)
                {
                    // ES 启用但检索失败：显式降级，不得混入"知识库无命中"口径；已成功的输入保留其结果。
                    bm25Degraded = true;
                    degradedFlags.Add("DEGRADED.NO_BM25");
                    _logger.LogWarning(
                        exception,
                        "ES BM25 检索失败，本次运行手术通道降级。TaskId={TaskId}",
                        task.Id);
                    break;
                }
            }
        }
        else if (procedureInputs.Count > 0)
        {
            bm25Degraded = true;
            degradedFlags.Add("DEGRADED.NO_BM25");
        }

        // Step 6.5：诊断侧 ICD 索引两段式（主导词召回 → 逐级下钻 → 索引编码 7 格式解析 → 字典展开）。
        var indexStartedAt = DateTimeOffset.UtcNow;
        var indexResults = new Dictionary<Guid, IReadOnlyList<DiagnosisIndexRecommendation>>();
        var indexAttempted = settings.IcdIndexEnabled
            && selectedInputs.Any(input => !IsProcedureInput(input));
        var indexSucceeded = false;
        if (indexAttempted)
        {
            try
            {
                var indexSnapshot = _indexSource.GetSnapshot(cancellationToken);
                // 肿瘤部位表可选：表为空/加载失败时按无肿瘤链路继续（裸形态学码照旧丢弃）。
                TumorIndexSnapshot? tumorSnapshot = null;
                try
                {
                    tumorSnapshot = _tumorIndexSource.GetSnapshot(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "肿瘤索引快照加载失败，本次按无部位链路执行");
                }
                var diagnosisDictionary = _dictionarySource.GetDictionary(
                    task.HospitalId, settings.IndexDiagnosisCodeSystem, cancellationToken);
                var termSynonyms = _termSynonymSource.GetSnapshot(cancellationToken);
                var resolver = new IcdIndexDiagnosisResolver(
                    settings.LeadWordTopK, settings.MaxCandidatesPerInput, settings.IndexDiagnosisCodeSystem);
                foreach (var input in selectedInputs)
                {
                    if (IsProcedureInput(input))
                    {
                        continue;
                    }

                    var inputName = input.NormalizedText ?? input.OriginalText;
                    IReadOnlyList<string>? aliasTexts = input.NormalizedText is { } normalized
                        && normalized != input.OriginalText
                        ? [input.OriginalText]
                        : null;
                    indexResults[input.Id] = resolver.Resolve(
                        indexSnapshot, diagnosisDictionary, inputName, aliasTexts,
                        termSynonyms, tumorSnapshot, cancellationToken);
                }

                indexSucceeded = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 快照加载失败按无推荐处理（不回退字典直匹配），降级标记进 Trace 与 Policy。
                indexResults.Clear();
                degradedFlags.Add("DEGRADED.NO_ICD_INDEX");
                _logger.LogWarning(ex, "ICD 索引快照加载失败，本次运行诊断输入不召回。TaskId={TaskId}", task.Id);
            }
        }

        // Step 6.6：向量语义召回（WP-B3-1，决策 2：CPU 小模型先行）。
        // 语义候选独立于索引两段式：索引未命中（如口语简称"心梗"）时由向量兜底，
        // 索引命中时仅补充候选池，排序仍让位于索引整标题贴合（IndexTitleDepth）。
        var vectorSucceeded = false;
        var vectorResults = new Dictionary<Guid, IReadOnlyList<VectorSearchHit>>();
        var diagnosisInputCount = selectedInputs.Count(item => !IsProcedureInput(item));
        if (diagnosisInputCount > 0)
        {
            if (settings.VectorEnabled && _vectorSearch.IsAvailable)
            {
                try
                {
                    foreach (var input in selectedInputs)
                    {
                        if (IsProcedureInput(input))
                        {
                            continue;
                        }

                        var hits = await _vectorSearch.SearchAsync(
                            task.HospitalId,
                            settings.IndexDiagnosisCodeSystem,
                            input.NormalizedText ?? input.OriginalText,
                            settings.VectorTopK,
                            cancellationToken);
                        vectorResults[input.Id] = hits
                            .Where(hit => hit.Score >= settings.VectorMinScore)
                            .ToList();
                    }

                    vectorSucceeded = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    vectorResults.Clear();
                    _logger.LogWarning(
                        ex,
                        "向量语义检索失败，本次运行诊断输入无向量候选。TaskId={TaskId}",
                        task.Id);
                }
            }

            if (!vectorSucceeded)
            {
                // 显式降级：向量通道缺失不得伪装成"语义无相关"。
                degradedFlags.Add("DEGRADED.NO_VECTOR");
            }
        }

        // Step 6.7：手术侧 CM3 索引两段式（主导词召回 → 子树内修饰词下钻 → 索引码前缀展开）。
        // 与诊断侧同一套机制，词面判据按 cm3_index 实测形态另建（指针只有 ―见/―另见/―见亚目 三种）。
        // 与诊断侧不同的降级口径：字典通道本来就服务手术输入，所以这里失败等于回到今天的行为，
        // 属纯增量、不构成回归；诊断侧的"索引失败即不召回"不能照搬到这边。
        var cm3StartedAt = DateTimeOffset.UtcNow;
        var cm3Results = new Dictionary<Guid, IReadOnlyList<ProcedureIndexRecommendation>>();
        var cm3Attempted = settings.Cm3IndexEnabled && procedureInputs.Count > 0;
        var cm3Succeeded = false;
        if (cm3Attempted)
        {
            try
            {
                var cm3Snapshot = _cm3IndexSource.GetSnapshot(cancellationToken);
                var procedureDictionary = _dictionarySource.GetDictionary(
                    task.HospitalId, settings.IndexProcedureCodeSystem, cancellationToken);
                var cm3Synonyms = _termSynonymSource.GetSnapshot(cancellationToken);
                var cm3Resolver = new Cm3ProcedureIndexResolver(
                    settings.ProcedureLeadWordTopK, settings.MaxCandidatesPerInput, settings.Cm3TitleDirectEnabled);
                foreach (var input in procedureInputs)
                {
                    var inputName = input.NormalizedText ?? input.OriginalText;
                    cm3Results[input.Id] = cm3Resolver.Resolve(
                        cm3Snapshot, procedureDictionary, inputName, cm3Synonyms, cancellationToken);
                }

                cm3Succeeded = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                cm3Results.Clear();
                degradedFlags.Add("DEGRADED.NO_CM3_INDEX");
                _logger.LogWarning(
                    ex,
                    "CM3 索引快照加载失败，本次运行手术输入退回字典通道。TaskId={TaskId}",
                    task.Id);
            }
        }

        // Step 8：规则引擎。
        var rules = settings.RuleEngineEnabled
            ? await LoadRulesAsync(task, settings, cancellationToken)
            : new List<RuleDefinition>();

        // Step 9：模型一致性校验。校验器要判断的是"这一条候选有没有证据支持"，
        // 所以请求只能在候选与证据都齐备后按"逐输入×逐候选"发出（每输入至多 MaxCandidatesPerInput 次），
        // 每次调用都带诊断原文、候选编码与证据原文——判据必须由这些内容决定，不能是任务级常数。
        var modelRoute = await _modelRouter.ResolveAsync(settings.ExplanationModelCode, cancellationToken);
        var modelCalled = modelRoute.Available;
        var explanationCalls = 0;
        var anyExplanationValidated = false;
        var anyExplanationInvalid = false;
        if (!modelCalled)
        {
            degradedFlags.Add("DEGRADED.NO_MODEL");
        }

        // Step 9 / 10 / 11：评分、Policy、持久化（软失效旧推荐）。
        await MarkPreviousRecommendationsStaleAsync(task, selectedInputs, now, cancellationToken);
        var existingVersions = await LoadRecommendationVersionsAsync(task, cancellationToken);
        var scoringOptions = new ScoringOptions();
        var candidates = new List<CodingCandidateRecord>();
        var scoreRecords = new List<RecommendationScoreRecord>();
        var recommendations = new List<CodingRecommendationRecord>();
        var recommendationEvidences = new List<RecommendationEvidenceRecord>();

        // Step 8.5 的运行级标记：任一输入尝试过 Rerank 即记口径。
        var rerankAttempted = false;
        var rerankSucceeded = false;
        foreach (var input in selectedInputs)
        {
            var inputName = input.NormalizedText ?? input.OriginalText;
            // 类型隔离：手术输入只收 ICD-9 手术码、诊断输入只收 ICD-10 诊断码，
            // 否则 "腰椎间盘突出" 的诊断页会出现 "17.92650 腰椎间盘突出推拿治疗" 这类手术码。
            var procedureInput = IsProcedureInput(input);
            List<MergedCandidate> merged;
            if (!procedureInput)
            {
                // 诊断通道：索引两段式产物已完成语义门控，不再叠加字典连续覆盖门槛。
                merged = (indexResults.GetValueOrDefault(input.Id) ?? [])
                    .Select(item => new MergedCandidate(
                        item.CodeSystemCode,
                        item.Code.Length > 64 ? item.Code[..64] : item.Code,
                        item.Title.Length > 300 ? item.Title[..300] : item.Title,
                        ExactScore: item.Score,
                        Bm25Score: null,
                        FusedScore: item.Score,
                        RecallChannel: "ICD_INDEX",
                        IndexEvidencePath: item.EvidencePath,
                        IsIndexFallback: item.IsIndexFallback,
                        IndexNote: item.Note,
                        IndexTitleDepth: item.WholeTitleDepth,
                        FamilyRank: item.FamilyRank))
                    .ToList();
                // 向量语义候选：索引命中之外的语义近似补充（口语简称 / 措辞差异），按余弦分入池。
                var vectorHits = vectorResults.GetValueOrDefault(input.Id) ?? [];
                if (vectorHits.Count > 0)
                {
                    var indexedCodes = merged.Select(item => item.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    merged.AddRange(vectorHits
                        .Where(hit => !indexedCodes.Contains(hit.Code))
                        .Select(hit => new MergedCandidate(
                            hit.CodeSystemCode,
                            hit.Code.Length > 64 ? hit.Code[..64] : hit.Code,
                            hit.Title.Length > 300 ? hit.Title[..300] : hit.Title,
                            ExactScore: null,
                            Bm25Score: null,
                            FusedScore: (decimal)hit.Score,
                            RecallChannel: "VECTOR",
                            IndexNote: "向量语义召回",
                            VectorScore: (decimal)hit.Score)));
                }
            }
            else
            {
                // CM3 索引候选在前：两段式链路已完成主导词/修饰词语义门控，不再叠加字典连续覆盖门槛
                // （口径同诊断通道）。平权入池、不加权——最终次序仍由 IndexTitleDepth 与七维分裁决。
                var cm3Candidates = (cm3Results.GetValueOrDefault(input.Id) ?? [])
                    .Select(item => new MergedCandidate(
                        item.CodeSystemCode,
                        item.Code.Length > 64 ? item.Code[..64] : item.Code,
                        item.Title.Length > 300 ? item.Title[..300] : item.Title,
                        ExactScore: item.Score,
                        Bm25Score: null,
                        FusedScore: item.Score,
                        RecallChannel: "CM3_INDEX",
                        IndexEvidencePath: item.EvidencePath,
                        IsIndexFallback: item.IsIndexFallback,
                        IndexNote: item.Note,
                        IndexTitleDepth: item.WholeTitleDepth,
                        FamilyRank: item.FamilyRank))
                    .ToList();
                var dictionaryCandidates = MergeHits(exactResults[input.Id], bm25Results.GetValueOrDefault(input.Id) ?? [])
                    .Where(candidate => IsProcedureCodeSystem(candidate.CodeSystem))
                    // ICD 星号复合码（"2型糖尿病性肩关节周围炎 E11.600X015+M14.2*"）要求病历中有糖尿病主诊断；
                    // 输入未提糖尿病时这类候选即医生眼中的"糖尿病污染"，连精确分一并剔除。
                    .Where(candidate => !TextMatch.IsUnjustifiedDiabetesCompound(candidate.Title, inputName))
                    // 部位冲突："腰椎退行性病变" 召回 "颈椎/胸椎退行性病变"（字符覆盖 6/7、连续片段都挡不住），
                    // 标题出现的部位/限定词必须是输入已声明的，否则剔除。
                    .Where(candidate => !TextMatch.HasBodyPartConflict(candidate.Title, inputName))
                    .ToList();
                var inputKey = TextMatch.NormalizeKey(inputName);
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
                    // 只闸字典通道：CM3 索引链路已经按主导词+修饰词做过语义门控，
                    // 再套一层连续覆盖会把 "腹腔镜下胆囊切除术 → 51.2300 腹腔镜胆囊切除术"
                    // 这类入路词换序的正常标题挡在门外。
                    dictionaryCandidates = dictionaryCandidates
                        .Where(candidate => candidate.ExactScore is not null
                            || TextMatch.TitleCoversInput(candidate.Title, inputName))
                        .Select(candidate => (candidate, Contiguous:
                            TextMatch.CoverageOf(inputKey, TextMatch.NormalizeKey(candidate.Title))))
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

                merged = [.. cm3Candidates, .. dictionaryCandidates
                    .Where(candidate => !cm3Candidates.Any(item =>
                        string.Equals(item.Code, candidate.Code, StringComparison.OrdinalIgnoreCase)))];
            }

            var evidenceForInput = evidences
                .Where(evidence => MatchesInput(evidence, input))
                .OrderByDescending(evidence => evidence.EvidenceScore)
                .ToList();
            var evidenceScore = evidenceForInput.Count > 0 ? evidenceForInput[0].EvidenceScore : 0m;
            // 检索池用于核验模型回显的编码确实被召回过；证据原文按候选各自选取（见 ExplanationEvidenceTexts）。
            var retrievalCodes = merged.Select(item => item.Code).ToList();

            // Step 8.5（每输入）：交叉编码 Rerank——候选池 Top-K 精排，分数入七维 Rerank 槽位（WP-B3-2）。
            // 交叉编码看的是 query-doc 全注意力，能修正双塔向量的语义近似误差（如"心房梗死"混入）。
            if (merged.Count > 0 && settings.RerankEnabled)
            {
                rerankAttempted = true;
                if (_reranker.IsAvailable)
                {
                    try
                    {
                        var rerankCandidates = merged.Take(settings.RerankTopK).ToList();
                        var scores = _reranker.Score(
                            inputName,
                            rerankCandidates.Select(item => item.Title).ToList());
                        if (scores is not null)
                        {
                            for (var i = 0; i < rerankCandidates.Count && i < scores.Length; i++)
                            {
                                var index = merged.IndexOf(rerankCandidates[i]);
                                if (index >= 0)
                                {
                                    merged[index] = rerankCandidates[i] with { RerankScore = (decimal)scores[i] };
                                }
                            }

                            rerankSucceeded = true;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(
                            ex,
                            "Rerank 打分失败，本输入候选无精排分。TaskId={TaskId}",
                            task.Id);
                    }
                }
            }

            // B3-4：把该输入对应的临床事实（按事实名与输入原文/规范名同形匹配）的
            // 否定 / 确定性 / 时间归属传入规则上下文——此前恒为 null，规则无法感知这些维度。
            var linkedFact = facts.FirstOrDefault(fact =>
                string.Equals(fact.FactName, inputName, StringComparison.Ordinal)
                || (input.NormalizedText is { } normalizedText
                    && string.Equals(fact.FactName, normalizedText, StringComparison.Ordinal))
                || string.Equals(fact.FactName, input.OriginalText, StringComparison.Ordinal));

            var scored = new List<(MergedCandidate Candidate, decimal Final, RuleEvaluationResult Rule, ScoreComputation Score, decimal? Llm)>();
            foreach (var candidate in merged.Take(settings.MaxCandidatesPerInput))
            {
                var ruleResult = RuleEngine.Evaluate(rules, new RuleEvaluationContext(
                    CandidateCode: candidate.Code,
                    CandidateCodeSystem: candidate.CodeSystem,
                    CandidateGranularity: DetermineGranularity(candidate.Code),
                    FactNegation: linkedFact?.Negation,
                    FactCertainty: linkedFact?.Certainty.ToString(),
                    FactTemporality: linkedFact?.Temporality.ToString(),
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

                // 逐候选一致性校验：校验器必须看到"这一条候选 + 这一条输入的证据原文"，
                // 才能给出可被检索池与证据核验的判断（每输入至多 MaxCandidatesPerInput 次调用）。
                // 送入模型的证据与复核引用时用的是同一份列表，编号才能对得上。
                var candidateEvidenceTexts = ExplanationEvidenceTexts(evidenceForInput, candidate.Title);
                var explanation = modelCalled
                    ? await ExplainCandidateAsync(
                        inputName,
                        candidate,
                        candidateEvidenceTexts,
                        linkedFact,
                        settings,
                        cancellationToken)
                    : new CandidateExplanation(ExplanationState.NotRequested);
                if (modelCalled)
                {
                    explanationCalls++;
                }
                if (explanation.State == ExplanationState.Validated)
                {
                    anyExplanationValidated = true;
                }
                else if (explanation.State == ExplanationState.OutputInvalid)
                {
                    anyExplanationInvalid = true;
                }

                // 模型只有断言权：四判据由 LlmConsistencyJudge 用候选池与证据原文复核后换算。
                var consistency = explanation.State == ExplanationState.Validated
                    ? LlmConsistencyJudge.Judge(
                        new ModelConsistencyJudgement(
                            explanation.EchoedCode,
                            explanation.Supported,
                            explanation.CitedEvidenceIndexes ?? [],
                            explanation.ModelReportsViolation),
                        new LlmConsistencyContext(
                            candidate.Code,
                            candidate.Title,
                            retrievalCodes,
                            candidateEvidenceTexts,
                            linkedFact?.Negation ?? false,
                            linkedFact?.Certainty ?? FactCertainty.Unknown,
                            linkedFact?.Temporality ?? FactTemporality.Unknown))
                    : null;
                var llmScore = consistency is null
                    ? null
                    : LlmConsistencyScorer.Compute(consistency);
                if (consistency is not null)
                {
                    // 判据明细都是流水线自己复核出的结论（编号与布尔值，不含模型原文），
                    // 否则落库的 llm_score 只有一个聚合值，事后无法判断是哪一维被否掉。
                    _logger.LogDebug(
                        "一致性校验判据，TaskId={TaskId}, CandidateCode={CandidateCode}, Evidence={EvidenceReferencesValid}, InRetrieval={CandidateExistsInRetrieval}, Supported={ExplanationSupportedByEvidence}, Violation={NegationOrTemporalViolation}, Cited=[{Cited}], EvidenceCount={EvidenceCount}, LlmScore={LlmScore}",
                        task.Id,
                        candidate.Code,
                        consistency.EvidenceReferencesValid,
                        consistency.CandidateExistsInRetrieval,
                        consistency.ExplanationSupportedByEvidence,
                        consistency.NegationOrTemporalViolation,
                        string.Join(",", explanation.CitedEvidenceIndexes ?? []),
                        candidateEvidenceTexts.Count,
                        llmScore);
                }

                var computed = SevenDimensionScorer.Compute(scoringOptions, new CandidateScoreInput(
                    // 族内名次以每级 0.01 折入 ExactScore：同族分数钳位同分后，重排噪声
                    // 曾能随意翻转族内秩序（实测 高血压病2级 被带括号变体顶替）；该幅度
                    // 足以抗噪声，又不阻碍交叉编码的大幅合法改序（如 膈面=下壁 同义）。
                    ExactScore: candidate.ExactScore is { } exactScore
                        ? Math.Max(0m, exactScore - 0.01m * candidate.FamilyRank)
                        : null,
                    SemanticScore: candidate.VectorScore,
                    RetrievalScore: candidate.FusedScore,
                    RerankScore: candidate.RerankScore,
                    RuleScore: ruleResult.RuleScore,
                    EvidenceScore: evidenceScore,
                    LlmScore: llmScore));

                scored.Add((candidate, computed.FinalScore, ruleResult, computed, llmScore));
            }

            // 索引通道的整标题逐字贴合度先于七维分：标题被输入逐字连续包含（且解释掉 ≥75% 输入字符）
            // 时，编码员认定的就是这一条，粒度加权的相关性分只能在其后参与比较（字典/手术通道该值恒为 0）。
            var ranked = scored
                .OrderByDescending(item => item.Candidate.IndexTitleDepth)
                .ThenByDescending(item => item.Final)
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
                    // 模型可用性以本输入 Top1 候选的实际校验结论为准：
                    // 未调用或调用未通过 Schema 时都不允许据此升高置信。
                    ModelAvailable: ranked.Count > 0 && modelCalled,
                    ModelOutputValidated: ranked.Count > 0 && ranked[0].Llm is not null,
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
                    RecallSource = item.Candidate.RecallChannel,
                    ExactScore = item.Candidate.ExactScore,
                    Bm25Score = item.Candidate.Bm25Score,
                    VectorScore = item.Candidate.VectorScore,
                    RerankScore = item.Candidate.RerankScore,
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
                    SemanticScore = item.Candidate.VectorScore,
                    RetrievalScore = item.Candidate.FusedScore,
                    RerankScore = item.Candidate.RerankScore,
                    RuleScore = item.Rule.RuleScore,
                    EvidenceScore = evidenceScore,
                    LlmScore = item.Llm,
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
                    RecallChannel = item.Candidate.RecallChannel,
                    RecallScore = item.Candidate.FusedScore,
                    RuleScore = item.Rule.RuleScore,
                    ConfidenceScore = item.Final,
                    ReviewStatus = "PENDING_REVIEW",
                    Outcome = outcomeForItem,
                    LifecycleStatus = "ACTIVE",
                    EvidenceSufficiency = evidenceScore,
                    RiskLevel = policy.RiskLevel,
                    Reason = policy.Reason,
                    // 模型与 Prompt 版本按候选自身记账：只有该候选的解释通过 Schema 校验才盖版本，
                    // 否则留空——避免"整批统一盖章"把未采信的候选伪装成模型产物。
                    ModelVersion = item.Llm is not null ? settings.ExplanationModelCode : null,
                    PromptVersion = item.Llm is not null ? settings.PromptVersion : null,
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

                // 向量通道溯源证据：语义相似度可核对（不伪造命中文本）。
                if (item.Candidate.RecallChannel == "VECTOR"
                    && item.Candidate.VectorScore is { } vectorScore)
                {
                    recommendationEvidences.Add(new RecommendationEvidenceRecord
                    {
                        Id = Guid.NewGuid(),
                        HospitalId = task.HospitalId,
                        CodingRecommendationId = recommendationId,
                        DocumentSectionId = null,
                        PipelineRunId = runId,
                        EvidenceLevel = null,
                        SourceType = "VECTOR",
                        SourceText = $"向量语义召回：与诊断输入的余弦相似度 {vectorScore:0.####}",
                        MatchText = matchedTitle,
                        Score = vectorScore,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }

                // 索引链路溯源证据：主导词→修饰词命中路径与索引编码写入推荐证据，
                // 使医生能核对 "为什么推荐这个码"（字典通道无此链路）。
                if (item.Candidate.RecallChannel is "ICD_INDEX" or "CM3_INDEX"
                    && item.Candidate.IndexEvidencePath is { Length: > 0 } indexPath)
                {
                    recommendationEvidences.Add(new RecommendationEvidenceRecord
                    {
                        Id = Guid.NewGuid(),
                        HospitalId = task.HospitalId,
                        CodingRecommendationId = recommendationId,
                        DocumentSectionId = null,
                        PipelineRunId = runId,
                        EvidenceLevel = null,
                        SourceType = item.Candidate.RecallChannel,
                        SourceText = indexPath + (item.Candidate.IndexNote is { } note ? $"；{note}" : string.Empty),
                        MatchText = matchedTitle,
                        Score = item.Candidate.FusedScore,
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

        // 模型有响应但 Schema 重试后仍不合法：必须留下结构化质量问题，
        // 且该候选的解释不进入任何评分口径（Policy 已按 ModelOutputValidated 拦住高置信）。
        if (anyExplanationInvalid)
        {
            degradedFlags.Add("DEGRADED.LLM_OUTPUT_INVALID");
            qualityIssues.Add(CreateQualityIssue(
                QualityIssueType.LlmOutputInvalid,
                "HIGH",
                $"模型 {settings.ExplanationModelCode} 输出经 2 次重试仍未通过 Schema 校验，相关候选的解释不参与评分。",
                task));
        }

        _dbContext.CodingCandidates.AddRange(candidates);
        _dbContext.RecommendationScores.AddRange(scoreRecords);
        _dbContext.CodingRecommendations.AddRange(recommendations);
        _dbContext.RecommendationEvidences.AddRange(recommendationEvidences);
        _dbContext.QualityIssues.AddRange(qualityIssues);

        recorder.Record("EXACT_RETRIEVAL", "SUCCESS", null, startedAt);
        recorder.Record(
            "BM25_RETRIEVAL",
            bm25Degraded ? "DEGRADED" : "SUCCESS",
            bm25Degraded ? "DEGRADED.NO_BM25" : null,
            startedAt);
        if (indexAttempted)
        {
            recorder.Record(
                "ICD_INDEX_RETRIEVAL",
                indexSucceeded ? "SUCCESS" : "DEGRADED",
                indexSucceeded ? null : "DEGRADED.NO_ICD_INDEX",
                indexStartedAt);
        }
        if (cm3Attempted)
        {
            recorder.Record(
                "CM3_INDEX_RETRIEVAL",
                cm3Succeeded ? "SUCCESS" : "DEGRADED",
                cm3Succeeded ? null : "DEGRADED.NO_CM3_INDEX",
                cm3StartedAt);
        }
        recorder.Record(
            "VECTOR_RETRIEVAL",
            vectorSucceeded ? "SUCCESS" : diagnosisInputCount > 0 ? "DEGRADED" : "SUCCESS",
            vectorSucceeded || diagnosisInputCount == 0 ? null : "DEGRADED.NO_VECTOR",
            startedAt);
        recorder.Record(
            "RERANK",
            rerankSucceeded ? "SUCCESS" : rerankAttempted ? "DEGRADED" : "SUCCESS",
            rerankSucceeded || !rerankAttempted ? null : "DEGRADED.NO_RERANK",
            startedAt);
        recorder.Record("RULE", "SUCCESS", null, startedAt);
        // 模型维降级口径：无网关即 NO_MODEL；有网关但全部候选输出不合法即 LLM_OUTPUT_INVALID；
        // 有网关却没有可校验的候选时不算模型维降级。
        var modelStageError = !modelCalled
            ? "DEGRADED.NO_MODEL"
            : anyExplanationValidated || (explanationCalls == 0 && !anyExplanationInvalid)
                ? null
                : anyExplanationInvalid ? "DEGRADED.LLM_OUTPUT_INVALID" : "DEGRADED.NO_MODEL";
        recorder.Record(
            "SCORE",
            modelStageError is null ? "SUCCESS" : "DEGRADED",
            modelStageError,
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
        // 与模型一致性校验的证据复核同一口径，避免两套"逐字支持"定义。
        return TextMatch.EvidenceSupportsTitle(evidenceText, title);
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

    /// <summary>一致性校验引用的证据条数上限（引用编号必须与送入模型的条数一致）。</summary>
    private const int ExplanationEvidenceLimit = 5;

    /// <summary>
    /// 该候选的证据原文列表（按引用编号顺序）：逐字含该标题的证据优先，其余按证据分补齐。
    /// 只按证据分取前 5 条时，病历切片（往往是唯一能佐证该编码的原文）会被索引推导条目挤掉，
    /// 校验器看到的就全是"看标题不像"的证据，判据退化成与病历无关的常数。
    /// </summary>
    private static IReadOnlyList<string> ExplanationEvidenceTexts(
        IReadOnlyList<ClinicalEvidenceRecord> evidenceForInput,
        string candidateTitle)
    {
        return evidenceForInput
            .OrderByDescending(evidence =>
                TextMatch.EvidenceSupportsTitle(evidence.OriginalText, candidateTitle) ? 1 : 0)
            .ThenByDescending(evidence => evidence.EvidenceScore)
            .Take(ExplanationEvidenceLimit)
            .Select(evidence => evidence.OriginalText)
            .ToList();
    }

    /// <summary>
    /// 单候选的模型一致性校验：请求带诊断原文、候选编码与带编号的证据原文，
    /// 输出必须通过 Schema 校验（最多 2 次重试）才允许进入判据换算。
    /// </summary>
    private async Task<CandidateExplanation> ExplainCandidateAsync(
        string? inputName,
        MergedCandidate candidate,
        IReadOnlyList<string> evidenceTexts,
        ClinicalFactRecord? linkedFact,
        V22PipelineOptions settings,
        CancellationToken cancellationToken)
    {
        const string schemaJson = """
            {
              "type": "object",
              "required": ["code", "supported", "cited_evidence", "negation_or_temporal_violation"],
              "properties": {
                "code": { "type": "string" },
                "supported": { "type": "boolean" },
                "cited_evidence": {
                  "type": "array",
                  "maxItems": 5,
                  "items": { "type": "integer", "minimum": 1 }
                },
                "negation_or_temporal_violation": { "type": "boolean" }
              },
              "additionalProperties": false
            }
            """;

        var request = new ModelGenerationRequest(
            settings.ExplanationModelCode,
            BuildExplanationPrompt(inputName, candidate, evidenceTexts, linkedFact),
            SystemPrompt: "你是 ICD 编码推荐的一致性校验器：只判断给定证据原文能否支持给定候选编码，只输出 JSON。",
            MaxTokens: 160,
            ResponseSchemaJson: schemaJson);

        var responded = false;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var result = await _modelGateway.GenerateAsync(request, cancellationToken);
            if (!result.Success || result.ContentJson is null)
            {
                _logger.LogWarning(
                    "模型一致性校验请求失败，ModelCode={ModelCode}, CandidateCode={CandidateCode}, Attempt={Attempt}, ErrorCode={ErrorCode}",
                    result.ModelCode,
                    candidate.Code,
                    attempt,
                    result.ErrorCode);
                continue;
            }

            responded = true;
            var validation = _schemaValidator.Validate(schemaJson, result.ContentJson);
            if (!validation.IsValid)
            {
                // 只记录错误条数：AI 响应内容不得进入普通应用日志。
                _logger.LogWarning(
                    "模型输出未通过 Schema 校验，ModelCode={ModelCode}, CandidateCode={CandidateCode}, Attempt={Attempt}, ErrorCount={ErrorCount}",
                    result.ModelCode,
                    candidate.Code,
                    attempt,
                    validation.Errors.Count);
                continue;
            }

            return ParseCandidateExplanation(result.ContentJson);
        }

        // 重试耗尽：区分"根本没响应"（模型不可用）与"响应了但始终不合法"（模型侧质量问题）。
        return new CandidateExplanation(
            responded ? ExplanationState.OutputInvalid : ExplanationState.Unavailable);
    }

    /// <summary>
    /// 一致性校验 Prompt：诊断原文 + 候选编码 + 带编号的证据原文 + 结构化事实线索。
    /// 输入与证据按长度截断，避免小模型的有限上下文被病历正文淹没。
    /// </summary>
    private static string BuildExplanationPrompt(
        string? inputName,
        MergedCandidate candidate,
        IReadOnlyList<string> evidenceTexts,
        ClinicalFactRecord? linkedFact)
    {
        var evidenceLines = evidenceTexts.Count == 0
            ? "（无证据原文）"
            : string.Join(
                "\n",
                evidenceTexts.Select((text, index) => $"{index + 1}. {Ellipsize(text, 120)}"));
        var factLine = linkedFact is null
            ? "否定=未知 确定性=Unknown 时间=Unknown"
            : $"否定={(linkedFact.Negation ? "是" : "否")} 确定性={linkedFact.Certainty} 时间={linkedFact.Temporality}";

        return $$"""
            诊断原文：{{Ellipsize(inputName, 60)}}
            候选编码：{{candidate.Code}} {{Ellipsize(candidate.Title, 60)}}
            证据原文（引用编号从 1 开始）：
            {{evidenceLines}}
            事实线索：{{factLine}}
            问题：上述证据原文能否支持把本条诊断编为候选编码？
            - code：只填候选编码的编码值本身，不要带名称、说明或其他文字。
            - supported：能支持填 true，不能支持或证据不足填 false。
            - cited_evidence：支撑该判断的证据编号列表，只能使用上面列出的编号；无可用证据时填空数组 []。
            - negation_or_temporal_violation：证据表明该诊断被否定、属于既往史或家族史时填 true。
            """;
    }

    /// <summary>
    /// 解析已通过 Schema 校验的模型输出为候选自述。解析异常按"输出不合法"处理，不采信。
    /// </summary>
    private static CandidateExplanation ParseCandidateExplanation(string contentJson)
    {
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            var root = document.RootElement;
            var cited = root.TryGetProperty("cited_evidence", out var citations)
                && citations.ValueKind == JsonValueKind.Array
                ? citations.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out _))
                    .Select(item => item.GetInt32())
                    .ToList()
                : new List<int>();

            return new CandidateExplanation(
                ExplanationState.Validated,
                root.TryGetProperty("code", out var code) ? code.GetString() : null,
                ReadBooleanTrue(root, "supported"),
                cited,
                ReadBooleanTrue(root, "negation_or_temporal_violation"));
        }
        catch (JsonException)
        {
            return new CandidateExplanation(ExplanationState.OutputInvalid);
        }
    }

    private static bool ReadBooleanTrue(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.True;
    }

    private static string Ellipsize(string? text, int maxLength)
    {
        var value = text ?? string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength] + "…";
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
            .GroupBy(item => TextMatch.NormalizeKey(item.OriginalText), StringComparer.Ordinal)
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
            if (byText.ContainsKey(TextMatch.NormalizeKey(fact.FactName)))
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
            byText[TextMatch.NormalizeKey(record.OriginalText)] = record;
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
            .GroupBy(item => TextMatch.NormalizeKey(item.Title), StringComparer.Ordinal)
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
