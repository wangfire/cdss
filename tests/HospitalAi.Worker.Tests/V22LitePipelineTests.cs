using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Coding.Preprocessing;
using HospitalAi.Application.Coding.Rules;
using HospitalAi.Application.Coding.Scoring;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Contracts.Models;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.Elasticsearch;
using HospitalAi.Infrastructure.ModelGateways;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HospitalAi.Worker.Tests;

/// <summary>
/// V2.2-Lite 流水线出口判据测试。
/// 覆盖：无诊断输入不编码、命中编码必须带证据与 Trace、重跑软失效而非物理删除、
/// 已确认 Final Coding 不被覆盖、否定诊断不编码、规则阻断不出推荐、
/// 缺失维度重新归一化、模型不可用不出高置信、ES 不可用不回退 SQL 全量扫描、
/// V2.2 任务不得回退 Legacy Runner。
///
/// 数据库用例需要 SQL Server（HOSPITAL_AI_TEST_CONNECTION_STRING）；
/// 纯逻辑用例不依赖数据库。
/// </summary>
public sealed class V22LitePipelineTests
{
    private const string Fracture = "左胫骨平台粉碎性骨折";
    private const string FractureCode = "S82.142A";
    private const string FullDocumentText = "【出院诊断】" + Fracture + "。";

    private static readonly string[] ExpectedStages =
    [
        "QUALITY_GATE", "DOCUMENT_VERSION", "CHUNK", "FACT", "EVIDENCE",
        "EXACT_RETRIEVAL", "BM25_RETRIEVAL", "RULE", "SCORE", "POLICY", "PERSIST"
    ];

    [Fact]
    public async Task 无诊断输入_不产出任何推荐并标记原因()
    {
        await using var database = await V22TestDatabase.CreateAsync();
        // 文书里没有任何诊断段落，只有入院记录描述。
        var (hospitalId, taskId, message) = await database.SeedV22TaskAsync(
            "【入院记录】患者因摔倒后疼痛入院，生命体征平稳。");

        await database.CreateV22Runner().RunAsync(message);

        await using var context = database.CreateContext();
        var task = await context.CodingTasks.SingleAsync(item => item.Id == taskId);
        Assert.Equal(CodingTaskStatus.PendingReview, task.Status);
        Assert.Equal(CodingStage.Ready, task.CodingStage);
        Assert.Equal("NO_DIAGNOSIS_INPUT", task.ErrorCode);
        Assert.Empty(await context.CodingRecommendations.ToListAsync());
        Assert.Empty(await context.CodingCandidates.ToListAsync());
        // 没有诊断可编码时流水线在 POLICY 阶段明确拒绝，而不是静默结束。
        Assert.Contains("POLICY", await context.PipelineTraceSteps.Select(item => item.Stage).ToListAsync());
        Assert.DoesNotContain(
            "PERSIST",
            await context.PipelineTraceSteps.Select(item => item.Stage).ToListAsync());
    }

    [Fact]
    public async Task 命中编码_产出候选七维分数证据与11个Trace阶段()
    {
        await using var database = await V22TestDatabase.CreateAsync();
        var (hospitalId, taskId, message) = await database.SeedV22TaskAsync(FullDocumentText);
        await database.SeedCodeAsync(hospitalId, "ICD-10", FractureCode, Fracture);

        await database.CreateV22Runner().RunAsync(message);

        await using var context = database.CreateContext();
        var task = await context.CodingTasks.SingleAsync(item => item.Id == taskId);
        var recommendation = await context.CodingRecommendations
            .Include(item => item.Evidences)
            .SingleAsync();
        var candidate = await context.CodingCandidates.SingleAsync();
        var score = await context.RecommendationScores.SingleAsync();

        Assert.Equal(CodingTaskStatus.PendingReview, task.Status);
        Assert.Equal(CodingStage.AiRecommending, task.CodingStage);
        Assert.Equal(PipelineVersions.Lite, recommendation.PipelineVersion);
        Assert.Equal(FractureCode, recommendation.Code);
        Assert.Equal("r-1", recommendation.RecommendationVersion);
        // 模型不可用：不得出现高置信，也不得伪造 LlmScore / ModelVersion。
        Assert.False(string.Equals(
            recommendation.Outcome, "HIGH_CONFIDENCE", StringComparison.Ordinal));
        Assert.Null(score.LlmScore);
        Assert.Null(recommendation.ModelVersion);
        // 证据必须可追溯到原文切片。
        Assert.NotEmpty(recommendation.Evidences);
        Assert.All(
            recommendation.Evidences,
            item => Assert.Contains(Fracture, item.SourceText, StringComparison.Ordinal));
        // 未接入向量与重排：缺失维度必须保持 null，不得填默认值。
        Assert.Null(candidate.VectorScore);
        Assert.Null(candidate.RerankScore);
        Assert.Null(score.SemanticScore);
        // Trace 覆盖全部 11 个阶段（同一次运行的 CreatedAt 可能同刻，按阶段名称校验集合）。
        var stages = await context.PipelineTraceSteps.Select(item => item.Stage).ToListAsync();
        Assert.Equal(11, stages.Count);
        foreach (var stage in ExpectedStages)
        {
            Assert.Contains(stage, stages);
        }
        // 诊断输入保留原文与来源位置。
        var input = await context.CodingDiagnosisInputs.SingleAsync();
        Assert.Equal(Fracture, input.OriginalText);
        Assert.Equal("DOCUMENT_AUTO", input.SourceType);
        Assert.NotNull(input.SourceDocumentId);
        Assert.NotNull(input.SourceStart);
    }

    [Fact]
    public async Task 重跑_旧推荐软失效且旧证据与候选保留()
    {
        await using var database = await V22TestDatabase.CreateAsync();
        var (hospitalId, taskId, message) = await database.SeedV22TaskAsync(FullDocumentText);
        await database.SeedCodeAsync(hospitalId, "ICD-10", FractureCode, Fracture);
        var runner = database.CreateV22Runner();

        await runner.RunAsync(message);
        await using (var first = database.CreateContext())
        {
            Assert.Single(await first.CodingRecommendations.ToListAsync());
        }

        await runner.RunAsync(message);

        await using var context = database.CreateContext();
        var recommendations = await context.CodingRecommendations
            .OrderBy(item => item.RecommendationVersion)
            .ToListAsync();

        // 只做软失效：物理记录一条都不删，审计可回放。
        Assert.Equal(2, recommendations.Count);
        Assert.Equal(2, await context.CodingCandidates.CountAsync());
        Assert.Equal(2, await context.RecommendationScores.CountAsync());
        Assert.Equal(
            new[] { "STALE", "ACTIVE" },
            recommendations.Select(item => item.LifecycleStatus));
        // 两次运行有独立的 PipelineRunId，推荐版本号递增。
        Assert.NotEqual(recommendations[0].PipelineRunId, recommendations[1].PipelineRunId);
        Assert.Equal("r-1", recommendations[0].RecommendationVersion);
        Assert.Equal("r-2", recommendations[1].RecommendationVersion);
    }

    [Fact]
    public async Task 重跑_不覆盖已确认的最终编码()
    {
        await using var database = await V22TestDatabase.CreateAsync();
        var (hospitalId, taskId, message) = await database.SeedV22TaskAsync(FullDocumentText);
        await database.SeedCodeAsync(hospitalId, "ICD-10", FractureCode, Fracture);
        var runner = database.CreateV22Runner();

        await runner.RunAsync(message);

        Guid confirmedRecommendationId;
        await using (var first = database.CreateContext())
        {
            var recommendation = await first.CodingRecommendations.SingleAsync();
            recommendation.ReviewStatus = "ACCEPTED";
            confirmedRecommendationId = recommendation.Id;
            var now = DateTimeOffset.UtcNow;
            first.FinalCodingResults.Add(new FinalCodingResultRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = hospitalId,
                CodingTaskId = taskId,
                SourceRecommendationId = recommendation.Id,
                ResultType = "DIAGNOSIS",
                CodeSystemCode = recommendation.CodeSystemCode,
                Code = recommendation.Code,
                Title = recommendation.Title,
                ReviewerId = "coder-1",
                ConfirmedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
            await first.SaveChangesAsync();
        }

        await runner.RunAsync(message);

        await using var context = database.CreateContext();
        var confirmed = await context.CodingRecommendations.SingleAsync(
            item => item.Id == confirmedRecommendationId);

        // 已确认推荐保持 ACTIVE、非只读，Final Coding 只有一条。
        Assert.Equal("ACTIVE", confirmed.LifecycleStatus);
        Assert.False(confirmed.IsReadOnly);
        Assert.Single(await context.FinalCodingResults.ToListAsync());
    }

    [Fact]
    public async Task 诊断输入被否定_不得产出该编码推荐()
    {
        await using var database = await V22TestDatabase.CreateAsync();
        // 同一诊断行里包含一个被否定的诊断与一个明确诊断。
        var (hospitalId, taskId, message) = await database.SeedV22TaskAsync(
            "【出院诊断】否认高血压；" + Fracture + "。");
        await database.SeedCodeAsync(hospitalId, "ICD-10", "I10", "高血压");
        await database.SeedCodeAsync(hospitalId, "ICD-10", FractureCode, Fracture);

        await database.CreateV22Runner().RunAsync(message);

        await using var context = database.CreateContext();
        var recommendations = await context.CodingRecommendations.ToListAsync();

        // 否定事实入库供审计，但不作为编码输入。
        Assert.Contains(
            await context.ClinicalFacts.ToListAsync(),
            item => item.Negation && item.OriginalValue.Contains("高血压", StringComparison.Ordinal));
        Assert.DoesNotContain(
            await context.CodingDiagnosisInputs.ToListAsync(),
            item => item.OriginalText.Contains("高血压", StringComparison.Ordinal));
        // 明确诊断仍正常推荐，否定诊断不得混入候选。
        Assert.Single(recommendations);
        Assert.Equal(FractureCode, recommendations[0].Code);
    }

    [Fact]
    public async Task 规则阻断_候选被丢弃并记录质量问题()
    {
        await using var database = await V22TestDatabase.CreateAsync();
        var (hospitalId, taskId, message) = await database.SeedV22TaskAsync(FullDocumentText);
        await database.SeedCodeAsync(hospitalId, "ICD-10", FractureCode, Fracture);
        await database.SeedBlockingRuleAsync(hospitalId, FractureCode);

        await database.CreateV22Runner().RunAsync(message);

        await using var context = database.CreateContext();
        var recommendations = await context.CodingRecommendations.ToListAsync();
        var issues = await context.QualityIssues.ToListAsync();

        Assert.Empty(recommendations);
        Assert.Empty(await context.CodingCandidates.ToListAsync());
        Assert.Contains(
            issues,
            item => item.IssueType == QualityIssueType.RuleViolation
                && item.CurrentCode == FractureCode);
    }

    [Fact]
    public void 七维评分_缺失维度按实际参与维度重新归一化()
    {
        var computation = SevenDimensionScorer.Compute(new ScoringOptions(), new CandidateScoreInput(
            ExactScore: 1m,
            SemanticScore: null,
            RetrievalScore: 0.5m,
            RerankScore: null,
            RuleScore: 1m,
            EvidenceScore: 1m,
            LlmScore: null));

        Assert.Equal(new[] { "exact", "retrieval", "rule", "evidence" }, computation.ParticipatingDimensions);
        Assert.DoesNotContain("semantic", computation.ParticipatingDimensions);
        // 参与权重 0.15 + 0.10 + 0.15 + 0.20 = 0.60，
        // 加权和 0.15×1 + 0.10×0.5 + 0.15×1 + 0.20×1 = 0.55。
        Assert.Equal(0.9167m, computation.FinalScore);
        Assert.Equal("exact+retrieval+rule+evidence", computation.ScoreProfile);
    }

    [Fact]
    public void LlmScore_模型不可用或输出未校验时为空()
    {
        Assert.Null(LlmConsistencyScorer.Compute(new LlmConsistencyInput(
            ModelAvailable: false,
            OutputValidated: false,
            EvidenceReferencesValid: true,
            CandidateExistsInRetrieval: true,
            ExplanationSupportedByEvidence: true,
            NegationOrTemporalViolation: false)));

        // 模型可用但输出未通过 Schema 校验时同样不得给出 LlmScore。
        Assert.Null(LlmConsistencyScorer.Compute(new LlmConsistencyInput(
            ModelAvailable: true,
            OutputValidated: false,
            EvidenceReferencesValid: true,
            CandidateExistsInRetrieval: true,
            ExplanationSupportedByEvidence: true,
            NegationOrTemporalViolation: false)));
    }

    [Fact]
    public void Policy_模型不可用时不给出高置信()
    {
        var result = RecommendationPolicy.Evaluate(
            new ScoringOptions(),
            new PolicyInput(
                HasEvidence: true,
                RuleBlocked: false,
                GranularityInsufficient: false,
                ModelAvailable: false,
                ModelOutputValidated: false,
                UnresolvedConflict: false,
                QualityGateFailed: false,
                DegradedFlags: ["DEGRADED.NO_MODEL", "DEGRADED.NO_VECTOR"]),
            finalScore: 1m,
            evidenceScore: 1m,
            margin: 1m,
            rulePass: true);

        Assert.Equal("NEED_REVIEW", result.Outcome);
    }

    [Fact]
    public void Policy_无证据时不编码()
    {
        var result = RecommendationPolicy.Evaluate(
            new ScoringOptions(),
            new PolicyInput(
                HasEvidence: false,
                RuleBlocked: false,
                GranularityInsufficient: false,
                ModelAvailable: true,
                ModelOutputValidated: true,
                UnresolvedConflict: false,
                QualityGateFailed: false,
                DegradedFlags: []),
            finalScore: 1m,
            evidenceScore: 0m,
            margin: 1m,
            rulePass: true);

        Assert.Equal("NO_SAFE_RECOMMENDATION", result.Outcome);
    }

    [Fact]
    public void Policy_规则阻断时不给出安全推荐()
    {
        var result = RecommendationPolicy.Evaluate(
            new ScoringOptions(),
            new PolicyInput(
                HasEvidence: true,
                RuleBlocked: true,
                GranularityInsufficient: false,
                ModelAvailable: true,
                ModelOutputValidated: true,
                UnresolvedConflict: false,
                QualityGateFailed: false,
                DegradedFlags: []),
            finalScore: 1m,
            evidenceScore: 1m,
            margin: 1m,
            rulePass: true);

        Assert.Equal("NO_SAFE_RECOMMENDATION", result.Outcome);
    }

    [Fact]
    public void Policy_无向量召回且证据不足时降级为人工复核()
    {
        var result = RecommendationPolicy.Evaluate(
            new ScoringOptions(),
            new PolicyInput(
                HasEvidence: true,
                RuleBlocked: false,
                GranularityInsufficient: false,
                ModelAvailable: true,
                ModelOutputValidated: true,
                UnresolvedConflict: false,
                QualityGateFailed: false,
                DegradedFlags: ["DEGRADED.NO_VECTOR"]),
            finalScore: 1m,
            evidenceScore: 0.5m,
            margin: 1m,
            rulePass: true);

        Assert.Equal("NEED_REVIEW", result.Outcome);
    }

    [Fact]
    public void 规则引擎_json条件命中且阻断优先级最高()
    {
        var rules = new[]
        {
            new RuleDefinition(
                "builtin-info",
                "v1",
                Priority: 1,
                Group: null,
                Severity: "INFO",
                Message: "内置提示",
                Blocking: false,
                IsBuiltin: true,
                ConditionJson: """{ "field": "candidate.code_system", "op": "equals", "value": "ICD-10" }""",
                ActionJson: null),
            new RuleDefinition(
                "hospital-block",
                "v1",
                Priority: 10,
                Group: null,
                Severity: "HIGH",
                Message: "阻断规则",
                Blocking: true,
                IsBuiltin: false,
                ConditionJson: """{ "field": "candidate.code", "op": "equals", "value": "S82.142A" }""",
                ActionJson: """{ "action": "require_human_review" }""")
        };

        var result = RuleEngine.Evaluate(
            rules,
            new RuleEvaluationContext(
                CandidateCode: "S82.142A",
                CandidateCodeSystem: "ICD-10",
                CandidateGranularity: "SPECIFIC",
                FactNegation: false,
                FactCertainty: "Confirmed",
                FactTemporality: "Current",
                FactFactType: "Diagnosis",
                EvidenceLevel: "PRIMARY",
                EvidenceScore: 1m,
                DocumentDocumentType: "出院小结"));

        Assert.True(result.Blocked);
        Assert.Equal(0m, result.RuleScore);
        Assert.Equal(2, result.Matches.Count);
    }

    [Fact]
    public void 规则引擎_条件不可解析时不得命中()
    {
        var result = RuleEngine.Evaluate(
            [new RuleDefinition(
                "broken",
                "v1",
                Priority: 10,
                Group: null,
                Severity: "HIGH",
                Message: "坏规则",
                Blocking: true,
                IsBuiltin: false,
                ConditionJson: "not-json",
                ActionJson: null)],
            new RuleEvaluationContext(
                CandidateCode: "S82.142A",
                CandidateCodeSystem: "ICD-10",
                CandidateGranularity: "SPECIFIC",
                FactNegation: false,
                FactCertainty: null,
                FactTemporality: null,
                FactFactType: null,
                EvidenceLevel: null,
                EvidenceScore: null,
                DocumentDocumentType: null));

        Assert.False(result.Blocked);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task Bm25不可用时_声明不可用且不返回任何命中()
    {
        var search = new UnavailableCodingKnowledgeSearch();

        Assert.False(search.IsAvailable);
        Assert.Empty(await search.SearchAsync(new CodingKnowledgeSearchQuery(
            Guid.NewGuid(),
            Fracture,
            "v1",
            "v1",
            "v1",
            ContentHash: null)));
    }

    [Fact]
    public async Task ModelRouter_未接通模型时明确报告不可用()
    {
        var router = new StaticModelRouter(Options.Create(new ModelGatewayOptions
        {
            ModelMode = nameof(ModelMode.Unavailable),
            Models = { ["model.small"] = "local-med-small" }
        }));

        var decision = await router.ResolveAsync("model.small");

        Assert.False(decision.Available);
        Assert.Equal("MODEL_MODE_UNAVAILABLE", decision.Reason);
    }

    [Fact]
    public async Task ModelGateway_不可用实现不伪造任何模型输出()
    {
        var gateway = new UnavailableModelGateway();

        var generated = await gateway.GenerateAsync(new ModelGenerationRequest(
            "model.small",
            "prompt",
            SystemPrompt: null,
            ResponseSchemaJson: null));
        var embedded = await gateway.EmbedAsync(new EmbeddingRequest(
            "model.embed",
            [Fracture]));

        Assert.False(generated.Success);
        Assert.Null(generated.ContentJson);
        Assert.Equal(UnavailableModelGateway.ModelUnavailable, generated.ErrorCode);
        // 禁止伪向量：不可用时向量为空而不是随机值。
        Assert.False(embedded.Success);
        Assert.Null(embedded.Vectors);
    }

    [Fact]
    public void Dispatcher_V22任务不得回退到LegacyRunner()
    {
        var dispatcher = new CodingTaskPipelineDispatcher(
            [new LegacyStubRunner()],
            Options.Create(new PipelineDispatchOptions { DefaultPipelineVersion = PipelineVersions.Lite }),
            NullLogger<CodingTaskPipelineDispatcher>.Instance);

        var exception = Assert.Throws<InvalidOperationException>(
            () => dispatcher.Select(PipelineVersions.Lite));

        Assert.Contains("拒绝降级执行", exception.Message);
    }

    [Fact]
    public void Dispatcher_Legacy任务继续使用LegacyRunner()
    {
        var dispatcher = new CodingTaskPipelineDispatcher(
            [new LegacyStubRunner(), new V22LiteStubRunner()],
            Options.Create(new PipelineDispatchOptions { DefaultPipelineVersion = PipelineVersions.Lite }),
            NullLogger<CodingTaskPipelineDispatcher>.Instance);

        Assert.IsType<LegacyStubRunner>(dispatcher.Select(PipelineVersions.Legacy));
    }

    [Fact]
    public void Dispatcher_LegacyRunner不得处理新V22任务()
    {
        var dispatcher = new CodingTaskPipelineDispatcher(
            [new LegacyStubRunner(), new V22LiteStubRunner()],
            Options.Create(new PipelineDispatchOptions { DefaultPipelineVersion = PipelineVersions.Lite }),
            NullLogger<CodingTaskPipelineDispatcher>.Instance);

        Assert.IsType<V22LiteStubRunner>(dispatcher.Select(PipelineVersions.Lite));
        Assert.IsType<V22LiteStubRunner>(dispatcher.Select(PipelineVersions.Full));
    }

    [Fact]
    public void Dispatcher_未注册任何执行器时报错()
    {
        var dispatcher = new CodingTaskPipelineDispatcher(
            [],
            Options.Create(new PipelineDispatchOptions { DefaultPipelineVersion = PipelineVersions.Lite }),
            NullLogger<CodingTaskPipelineDispatcher>.Instance);

        Assert.Throws<InvalidOperationException>(() => dispatcher.Select(PipelineVersions.Lite));
    }

    [Fact]
    public void 默认灰度版本_来自配置而非代码硬编码()
    {
        var options = new Infrastructure.CodingTasks.CodingTaskPipelineOptions();

        Assert.Equal(PipelineVersions.Lite, options.Resolve(null));
        Assert.Equal(PipelineVersions.Legacy, options.Resolve(PipelineVersions.Legacy));

        var configured = new Infrastructure.CodingTasks.CodingTaskPipelineOptions
        {
            DefaultPipelineVersion = PipelineVersions.Legacy
        };
        Assert.Equal(PipelineVersions.Legacy, configured.Resolve(null));
    }

    [Fact]
    public async Task 模型输出Schema校验失败_标记LLM_OUTPUT_INVALID且不得采信()
    {
        await using var database = await V22TestDatabase.CreateAsync();
        var (hospitalId, taskId, message) = await database.SeedV22TaskAsync(FullDocumentText);
        await database.SeedCodeAsync(hospitalId, "ICD-10", FractureCode, Fracture);

        // 模型路由可用，但网关始终返回不满足 Schema 的输出。
        var runner = database.CreateV22Runner(
            new InvalidOutputModelGateway(),
            new AvailableModelRouter());

        await runner.RunAsync(message);

        await using var context = database.CreateContext();
        var issues = await context.QualityIssues.ToListAsync();
        var traceSteps = await context.PipelineTraceSteps.ToListAsync();

        // 输出不合法必须留下结构化质量问题，且必须标为降级，而不是静默当作"模型不可用"。
        Assert.Contains(
            issues,
            item => item.IssueType == QualityIssueType.LlmOutputInvalid);
        // 11 个阶段仍然全部记录：模型问题不中断流水线。
        var stages = traceSteps.Select(item => item.Stage).ToList();
        foreach (var stage in ExpectedStages)
        {
            Assert.Contains(stage, stages);
        }
        // 不采信该次输出：不得出现高置信。
        var recommendations = await context.CodingRecommendations.ToListAsync();
        Assert.DoesNotContain(
            recommendations,
            item => item.Outcome == "HIGH_CONFIDENCE");
    }

    private sealed class LegacyStubRunner : ICodingTaskPipelineRunner, ICodingTaskPipelineCapabilities
    {
        public IReadOnlyCollection<string> SupportedPipelineVersions { get; } = [PipelineVersions.Legacy];

        public Task RunAsync(
            CodingTaskCreatedMessage message,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AvailableModelRouter : IModelRouter
    {
        public Task<ModelRouteDecision> ResolveAsync(
            string modelCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelRouteDecision(modelCode, Available: true, Reason: null));
    }

    /// <summary>始终返回不满足 Schema 的输出：缺少 required 字段且类型不符。</summary>
    private sealed class InvalidOutputModelGateway : IModelGateway
    {
        public Task<ModelGenerationResult> GenerateAsync(
            ModelGenerationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelGenerationResult(
                Success: true,
                ContentJson: """{ "answer": 42 }""",
                ErrorCode: null,
                ModelCode: request.ModelCode,
                ModelVersion: "test",
                PromptTokens: 1,
                CompletionTokens: 1,
                DurationMilliseconds: 1));
    }

    private sealed class V22LiteStubRunner : ICodingTaskPipelineRunner, ICodingTaskPipelineCapabilities
    {
        public IReadOnlyCollection<string> SupportedPipelineVersions { get; } =
            [PipelineVersions.Lite, PipelineVersions.Full];

        public Task RunAsync(
            CodingTaskCreatedMessage message,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
