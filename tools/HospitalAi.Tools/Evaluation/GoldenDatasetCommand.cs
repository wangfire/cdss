using Microsoft.Extensions.Configuration;

namespace HospitalAi.Tools;

/// <summary>
/// `golden --validate`：对 Golden 数据集做静态校验。
/// 校验内容：必填字段、caseId 唯一、12 类场景全覆盖、安全断言合法、
/// “无证据”类用例不得同时期望出编码。
/// </summary>
public static class GoldenDatasetCommand
{
    public static int Run(IConfiguration configuration)
    {
        var path = configuration["Golden"] ?? DefaultDatasetPath();
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Golden 数据集不存在：{path}");
            return 2;
        }

        IReadOnlyList<GoldenCase> cases;
        try
        {
            cases = GoldenDataset.LoadAsync(path, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Golden 数据集解析失败：{exception.Message}");
            return 2;
        }

        var problems = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var goldenCase in cases)
        {
            if (string.IsNullOrWhiteSpace(goldenCase.CaseId))
            {
                problems.Add("存在空 caseId");
                continue;
            }

            if (!seenIds.Add(goldenCase.CaseId))
            {
                problems.Add($"caseId 重复：{goldenCase.CaseId}");
            }

            if (string.IsNullOrWhiteSpace(goldenCase.DocumentText))
            {
                problems.Add($"{goldenCase.CaseId}：documentText 为空");
            }

            if (string.IsNullOrWhiteSpace(goldenCase.DiagnosisText))
            {
                problems.Add($"{goldenCase.CaseId}：diagnosisText 为空");
            }

            foreach (var assertion in goldenCase.SafetyAssertions)
            {
                if (!GoldenSafetyAssertions.Known.Contains(assertion))
                {
                    problems.Add($"{goldenCase.CaseId}：未知安全断言 {assertion}");
                }
            }

            var expectsCodes = goldenCase.ExpectedCodes.Count > 0;
            var saysNoEvidence = goldenCase.SafetyAssertions.Contains(
                GoldenSafetyAssertions.NoEvidenceNoRecommendation);
            if (saysNoEvidence && expectsCodes)
            {
                problems.Add($"{goldenCase.CaseId}：声明无证据不出推荐，却又期望编码");
            }

            if (goldenCase.Category == "no_evidence" && !saysNoEvidence)
            {
                problems.Add($"{goldenCase.CaseId}：no_evidence 用例缺少 NO_EVIDENCE_NO_RECOMMENDATION 断言");
            }
        }

        var missingCategories = GoldenCategories.Required
            .Where(category => !cases.Any(item => item.Category == category))
            .ToArray();
        foreach (var category in missingCategories)
        {
            problems.Add($"缺少场景覆盖：{category}");
        }

        ToolHost.Print($"Golden 数据集：{path}");
        foreach (var category in GoldenCategories.Required)
        {
            var count = cases.Count(item => item.Category == category);
            ToolHost.Print($"  {category,-16} {count}");
        }

        if (problems.Count > 0)
        {
            foreach (var problem in problems)
            {
                Console.Error.WriteLine($"校验失败：{problem}");
            }

            return 1;
        }

        ToolHost.Print($"校验通过：{cases.Count} 条用例，12 类场景全覆盖。");
        return 0;
    }

    private static string DefaultDatasetPath()
    {
        return Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "phase1", "golden",
            "golden-dataset.json");
    }
}
