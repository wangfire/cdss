using HospitalAi.Application.Coding.Preprocessing;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker;
using HospitalAi.Worker.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HospitalAi.Tools;

/// <summary>
/// `evaluate`：把 Golden Dataset 逐条灌入独立临时医院，真实跑一遍 V2.2-Lite 流水线，
/// 再从 clinical_fact / clinical_evidence / coding_recommendation /
/// pipeline_trace_step 读回结果，计算可量化指标。
///
/// 约束：
/// - 病历正文只写入数据库（受控存储），不进入普通应用日志；报告里只输出用例 ID。
/// - 评测使用独立医院与独立就诊，不触碰任何生产数据；结束即清理。
/// - V2.2-Lite 不宣称真实模型准确率，只验收安全、可解释、降级和数据链路。
/// </summary>
public static class EvaluationCommand
{
    private const int ExpectedStageCount = 11;

    public static async Task<int> RunAsync(IConfiguration configuration)
    {
        var connectionString = ToolHost.ResolveConnectionString(configuration);
        var goldenPath = configuration["Golden"] ?? "data/phase1/golden/golden-dataset.json";
        if (!File.Exists(goldenPath))
        {
            Console.Error.WriteLine($"Golden 数据集不存在：{goldenPath}");
            return 2;
        }

        var cases = await GoldenDataset.LoadAsync(goldenPath);

        var services = new ServiceCollection();
        // 评测进程不打印业务日志：病历正文不得进入普通应用日志。
        services.AddLogging(builder => builder.ClearProviders());
        services.AddDbContext<HospitalAiDbContext>(options => options.UseSqlServer(connectionString));
        services.AddCodingTaskPipeline(configuration);

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HospitalAiDbContext>();

        var hospitalId = ToolHost.ResolveHospitalId(configuration) ?? Guid.NewGuid();
        var patientId = Guid.NewGuid();

        try
        {
            await SeedAsync(dbContext, hospitalId, patientId, CancellationToken.None);

            var results = new List<CaseResult>();
            foreach (var goldenCase in cases)
            {
                results.Add(await RunCaseAsync(
                    scope.ServiceProvider,
                    dbContext,
                    hospitalId,
                    patientId,
                    goldenCase,
                    CancellationToken.None));
            }

            return Report(results);
        }
        finally
        {
            await CleanupAsync(dbContext, hospitalId, patientId, CancellationToken.None);
        }
    }

    private static async Task<CaseResult> RunCaseAsync(
        IServiceProvider serviceProvider,
        HospitalAiDbContext dbContext,
        Guid hospitalId,
        Guid patientId,
        GoldenCase goldenCase,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var visitId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var traceId = Guid.NewGuid().ToString("N");

        dbContext.Visits.Add(new VisitRecord
        {
            Id = visitId,
            HospitalId = hospitalId,
            PatientId = patientId,
            AdmissionAt = now.AddDays(-2),
            DischargeAt = now.AddDays(-1),
            CreatedAt = now,
            UpdatedAt = now
        });

        dbContext.MedicalDocuments.Add(new MedicalDocumentRecord
        {
            Id = documentId,
            HospitalId = hospitalId,
            VisitId = visitId,
            DocumentType = "GOLDEN_CASE",
            ContentReference = goldenCase.DocumentText,
            ContentHash = DocumentChunker.ComputeHash(goldenCase.DocumentText),
            Version = 1,
            ParseVersion = "golden-v1",
            DocumentStatus = "ACTIVE",
            IsCurrent = true,
            CreatedAt = now,
            UpdatedAt = now
        });

        dbContext.CodingTasks.Add(new CodingTaskRecord
        {
            Id = taskId,
            HospitalId = hospitalId,
            VisitId = visitId,
            PipelineVersion = PipelineVersions.Lite,
            Status = CodingTaskStatus.Pending,
            CodingStage = CodingStage.Imported,
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        dbContext.PipelineTraces.Add(new PipelineTraceRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            CodingTaskId = taskId,
            TraceId = traceId,
            Status = "PENDING",
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var runner = serviceProvider.GetRequiredService<V22LiteCodingPipelineRunner>();
        var startedAt = DateTimeOffset.UtcNow;
        await runner.RunAsync(
            new CodingTaskCreatedMessage(
                Guid.NewGuid(),
                hospitalId,
                taskId,
                visitId,
                PipelineVersions.Lite,
                traceId,
                Guid.NewGuid().ToString("N")),
            cancellationToken);
        var elapsedMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

        var traceSteps = await dbContext.PipelineTraceSteps
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId
                && dbContext.PipelineTraces.Any(
                    trace => trace.Id == item.PipelineTraceId && trace.CodingTaskId == taskId))
            .OrderBy(item => item.StartedAt)
            .ToListAsync(cancellationToken);

        var factNames = await dbContext.ClinicalFacts
            .AsNoTracking()
            .Where(item => item.CodingTaskId == taskId)
            .Select(item => new FactProjection(item.FactName, item.Negation))
            .ToListAsync(cancellationToken);
        var evidenceTexts = await dbContext.ClinicalEvidences
            .AsNoTracking()
            .Where(item => item.CodingTaskId == taskId)
            .Select(item => item.OriginalText)
            .ToListAsync(cancellationToken);
        var recommendations = await dbContext.CodingRecommendations
            .AsNoTracking()
            .Where(item => item.CodingTaskId == taskId)
            .OrderBy(item => item.Rank)
            .Select(item => new RecommendationProjection(
                item.Code,
                item.Outcome,
                item.RiskLevel,
                item.IsReadOnly,
                item.PipelineVersion))
            .ToListAsync(cancellationToken);

        var factIssues = await dbContext.QualityIssues
            .AsNoTracking()
            .Where(item => item.CodingTaskId == taskId)
            .Select(item => item.IssueType)
            .ToListAsync(cancellationToken);

        var top1 = recommendations.FirstOrDefault();
        var top3Codes = recommendations.Take(3).Select(item => item.Code).ToHashSet(StringComparer.Ordinal);

        var problems = ValidateSafety(
            goldenCase,
            factNames,
            evidenceTexts,
            recommendations,
            traceSteps,
            factIssues);

        ToolHost.Print(
            $"{goldenCase.CaseId,-22} 场景 {goldenCase.Category,-15} "
            + $"事实 {factNames.Count}，证据 {evidenceTexts.Count}，推荐 {recommendations.Count}，"
            + $"Top1={(top1?.Code ?? "-")}，TraceStep {traceSteps.Count}/{ExpectedStageCount}，"
            + $"{elapsedMs:F0}ms，{(problems.Count == 0 ? "PASS" : "FAIL")}");

        return new CaseResult(
            CaseId: goldenCase.CaseId,
            Category: goldenCase.Category,
            ExpectedFactCount: goldenCase.ExpectedFacts.Count,
            MatchedFactCount: CountMatches(factNames.Select(item => item.FactName), goldenCase.ExpectedFacts),
            ExpectedNegationCount: goldenCase.ExpectedNegation.Count,
            MatchedNegationCount: factNames.Count(item => item.Negation
                && goldenCase.ExpectedNegation.Any(
                    expected => item.FactName.Contains(expected, StringComparison.Ordinal))),
            ExpectedEvidenceCount: goldenCase.ExpectedEvidence.Count,
            EvidenceCount: evidenceTexts.Count,
            GranularityIssues: factIssues.Count(item => item == QualityIssueType.GranularityInsufficient),
            ExpectedCodeCount: goldenCase.ExpectedCodes.Count,
            Top1Code: top1?.Code,
            Top1Hit: goldenCase.ExpectedCodes.Count > 0
                && goldenCase.ExpectedCodes.Contains(top1?.Code ?? string.Empty, StringComparer.Ordinal),
            Top3HitCount: top3Codes.Count(goldenCase.ExpectedCodes.Contains),
            UnexpectedTop3Count: top3Codes.Count(code => goldenCase.ExpectedCodes.Count > 0
                && !goldenCase.ExpectedCodes.Contains(code)),
            Outcome: top1?.Outcome,
            ExpectedRisk: goldenCase.ExpectedRisk,
            ActualRisk: top1?.RiskLevel,
            TraceStepCount: traceSteps.Count,
            LatencyMs: elapsedMs,
            Problems: problems);
    }

    private static int CountMatches(IEnumerable<string> actual, IReadOnlyList<string> expected)
    {
        if (expected.Count == 0)
        {
            return 0;
        }

        return actual.Count(
            value => expected.Any(
                item => value.Contains(item, StringComparison.Ordinal)
                    || item.Contains(value, StringComparison.Ordinal)));
    }

    /// <summary>
    /// 安全断言校验。Lite 阶段这些结论不依赖模型，必须成立；不成立即回归。
    /// </summary>
    private static List<string> ValidateSafety(
        GoldenCase goldenCase,
        IReadOnlyList<FactProjection> facts,
        IReadOnlyList<string> evidences,
        IReadOnlyList<RecommendationProjection> recommendations,
        IReadOnlyList<PipelineTraceStepRecord> traceSteps,
        IReadOnlyList<QualityIssueType> issues)
    {
        var problems = new List<string>();

        if (goldenCase.SafetyAssertions.Contains(GoldenSafetyAssertions.NoEvidenceNoRecommendation)
            && recommendations.Any(item => item.Outcome != "NO_SAFE_RECOMMENDATION"))
        {
            problems.Add("无证据用例仍然产出了可编码推荐");
        }

        if (goldenCase.SafetyAssertions.Contains(GoldenSafetyAssertions.HasEvidence)
            && goldenCase.ExpectedEvidence.Count > 0
            && !goldenCase.ExpectedEvidence.Any(
                expected => evidences.Any(
                    evidence => evidence.Contains(expected, StringComparison.Ordinal)
                        || expected.Contains(evidence, StringComparison.Ordinal))))
        {
            problems.Add("缺少期望证据锚点");
        }

        if (goldenCase.SafetyAssertions.Contains(GoldenSafetyAssertions.NotHighConfidence)
            && recommendations.Any(item => string.Equals(item.Outcome, "HIGH_CONFIDENCE", StringComparison.Ordinal)))
        {
            problems.Add("无模型环境下出现了 HIGH_CONFIDENCE 判定");
        }

        if (goldenCase.SafetyAssertions.Contains("NEGATED_NOT_CODED") && recommendations.Count > 0)
        {
            problems.Add("否定诊断仍然产出了推荐");
        }

        if (goldenCase.SafetyAssertions.Contains("GRANULARITY_ISSUE_RAISED")
            && !issues.Contains(QualityIssueType.GranularityInsufficient))
        {
            problems.Add("粒度不足用例没有产生结构化质量问题");
        }

        if (goldenCase.SafetyAssertions.Contains("NOT_LEGACY")
            && recommendations.Any(item => string.Equals(item.PipelineVersion, PipelineVersions.Legacy, StringComparison.Ordinal)
                || item.IsReadOnly))
        {
            problems.Add("新任务命中了只读 Legacy 推荐");
        }

        if (goldenCase.ExpectedCodes.Count > 0)
        {
            var top3 = recommendations.Take(3).Select(item => item.Code).ToHashSet(StringComparer.Ordinal);
            if (!top3.Any(code => goldenCase.ExpectedCodes.Contains(code)))
            {
                problems.Add(
                    $"Top-3 未命中期望编码（期望 {string.Join("/", goldenCase.ExpectedCodes)}）");
            }
        }

        if (traceSteps.Count < ExpectedStageCount)
        {
            problems.Add($"Trace 步骤不完整：{traceSteps.Count}/{ExpectedStageCount}");
        }

        if (traceSteps.Any(item => string.IsNullOrWhiteSpace(item.Stage)))
        {
            problems.Add("存在未标注 stage 的 Trace 步骤");
        }

        return problems;
    }

    private sealed record FactProjection(string FactName, bool Negation);

    private sealed record RecommendationProjection(
        string Code,
        string Outcome,
        string? RiskLevel,
        bool IsReadOnly,
        string PipelineVersion);

    private static int Report(IReadOnlyList<CaseResult> results)
    {
        var total = results.Count;
        static int Percent(int matched, int expected) => expected == 0 ? 100 : matched * 100 / expected;

        var coded = results.Where(item => item.ExpectedCodeCount > 0).ToArray();
        var latencies = results.Select(item => item.LatencyMs).OrderBy(value => value).ToArray();
        var failures = results.Where(item => item.Problems.Count > 0).ToArray();

        ToolHost.Print(
            $"""
            === V2.2-Lite 离线评测报告 ===
            用例数               {total}
            Fact Accuracy        {Percent(results.Sum(item => item.MatchedFactCount), results.Sum(item => item.ExpectedFactCount))}%
            Negation Accuracy    {Percent(results.Sum(item => item.MatchedNegationCount), results.Sum(item => item.ExpectedNegationCount))}%
            Evidence Sufficiency {Percent(results.Count(item => item.EvidenceCount > 0), results.Count(item => item.ExpectedEvidenceCount > 0))}%
            Top-1 Accuracy       {Percent(coded.Count(item => item.Top1Hit), coded.Length)}%
            Top-3 Recall         {Percent(coded.Count(item => item.Top3HitCount > 0), coded.Length)}%
            Hallucination Rate   {Percent(coded.Sum(item => item.UnexpectedTop3Count), coded.Length)}%
            Trace 完整率         {Percent(results.Sum(item => item.TraceStepCount), total * ExpectedStageCount)}%（每用例 {ExpectedStageCount} 步）
            P50 / P95 延迟       {Percentile(latencies, 0.5):F0}ms / {Percentile(latencies, 0.95):F0}ms
            安全断言失败          {failures.Length}
            """);

        foreach (var failure in failures)
        {
            foreach (var problem in failure.Problems)
            {
                Console.Error.WriteLine($"{failure.CaseId}：{problem}");
            }
        }

        return failures.Length == 0 ? 0 : 1;
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static async Task SeedAsync(
        HospitalAiDbContext dbContext,
        Guid hospitalId,
        Guid patientId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        dbContext.Hospitals.Add(new HospitalRecord
        {
            Id = hospitalId,
            Code = $"GOLDEN-{hospitalId:N}"[..12],
            Name = "Golden 评测临时医院",
            Status = "ACTIVE",
            CreatedAt = now,
            UpdatedAt = now
        });

        // DisplayName 留空：病历相关标识不进入日志，评测也不落患者姓名。
        dbContext.Patients.Add(new PatientRecord
        {
            Id = patientId,
            HospitalId = hospitalId,
            SourceSystem = "GOLDEN",
            SourcePatientId = patientId.ToString("N"),
            DisplayName = null,
            CreatedAt = now,
            UpdatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task CleanupAsync(
        HospitalAiDbContext dbContext,
        Guid hospitalId,
        Guid patientId,
        CancellationToken cancellationToken)
    {
        // 只清理本次评测医院，不影响其他医院任何数据。
        dbContext.RecommendationScores.RemoveRange(dbContext.RecommendationScores.Where(item => item.HospitalId == hospitalId));
        dbContext.CodingCandidates.RemoveRange(dbContext.CodingCandidates.Where(item => item.HospitalId == hospitalId));
        dbContext.QualityIssues.RemoveRange(dbContext.QualityIssues.Where(item => item.HospitalId == hospitalId));
        dbContext.RecommendationEvidences.RemoveRange(dbContext.RecommendationEvidences.Where(item => item.HospitalId == hospitalId));
        dbContext.CodingDiagnosisInputs.RemoveRange(dbContext.CodingDiagnosisInputs.Where(item => item.HospitalId == hospitalId));
        dbContext.CodingReviews.RemoveRange(dbContext.CodingReviews.Where(item => item.HospitalId == hospitalId));
        dbContext.FinalCodingResults.RemoveRange(dbContext.FinalCodingResults.Where(item => item.HospitalId == hospitalId));
        dbContext.PipelineTraceSteps.RemoveRange(dbContext.PipelineTraceSteps.Where(item => item.HospitalId == hospitalId));
        dbContext.PipelineTraces.RemoveRange(dbContext.PipelineTraces.Where(item => item.HospitalId == hospitalId));
        dbContext.CodingRecommendations.RemoveRange(dbContext.CodingRecommendations.Where(item => item.HospitalId == hospitalId));
        dbContext.ClinicalFactEvidences.RemoveRange(dbContext.ClinicalFactEvidences.Where(item => item.HospitalId == hospitalId));
        dbContext.ClinicalEvidences.RemoveRange(dbContext.ClinicalEvidences.Where(item => item.HospitalId == hospitalId));
        dbContext.ClinicalFacts.RemoveRange(dbContext.ClinicalFacts.Where(item => item.HospitalId == hospitalId));
        dbContext.ClinicalEntities.RemoveRange(dbContext.ClinicalEntities.Where(item => item.HospitalId == hospitalId));
        dbContext.DocumentSections.RemoveRange(dbContext.DocumentSections.Where(item => item.HospitalId == hospitalId));
        dbContext.MedicalDocuments.RemoveRange(dbContext.MedicalDocuments.Where(item => item.HospitalId == hospitalId));
        dbContext.CodingTasks.RemoveRange(dbContext.CodingTasks.Where(item => item.HospitalId == hospitalId));
        dbContext.Visits.RemoveRange(dbContext.Visits.Where(item => item.HospitalId == hospitalId));
        dbContext.Patients.RemoveRange(dbContext.Patients.Where(item => item.Id == patientId));
        dbContext.Hospitals.RemoveRange(dbContext.Hospitals.Where(item => item.Id == hospitalId));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record CaseResult(
        string CaseId,
        string Category,
        int ExpectedFactCount,
        int MatchedFactCount,
        int ExpectedNegationCount,
        int MatchedNegationCount,
        int ExpectedEvidenceCount,
        int EvidenceCount,
        int GranularityIssues,
        int ExpectedCodeCount,
        string? Top1Code,
        bool Top1Hit,
        int Top3HitCount,
        int UnexpectedTop3Count,
        string? Outcome,
        string? ExpectedRisk,
        string? ActualRisk,
        int TraceStepCount,
        double LatencyMs,
        IReadOnlyList<string> Problems);
}
