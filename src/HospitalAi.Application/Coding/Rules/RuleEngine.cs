using System.Text.Json;

namespace HospitalAi.Application.Coding.Rules;

/// <summary>
/// 单条规则定义。字段与 coding_rule 表对应；
/// Lite 阶段只支持"版本化 JSON 条件 + 类型化求值器"，禁止动态执行任意表达式或脚本。
/// </summary>
public sealed record RuleDefinition(
    string RuleCode,
    string RuleVersion,
    int Priority,
    string? Group,
    string? Severity,
    string Message,
    bool Blocking,
    bool IsBuiltin,
    string? ConditionJson,
    string? ActionJson);

/// <summary>
/// 规则求值上下文。字段白名单：
/// candidate.code / candidate.code_system / candidate.granularity /
/// fact.negation / fact.certainty / fact.temporality / fact.fact_type /
/// evidence.level / evidence.score / document.document_type。
/// </summary>
public sealed record RuleEvaluationContext(
    string? CandidateCode,
    string? CandidateCodeSystem,
    string? CandidateGranularity,
    bool? FactNegation,
    string? FactCertainty,
    string? FactTemporality,
    string? FactFactType,
    string? EvidenceLevel,
    decimal? EvidenceScore,
    string? DocumentDocumentType)
{
    public string? ResolveField(string field)
    {
        return field switch
        {
            "candidate.code" => CandidateCode,
            "candidate.code_system" => CandidateCodeSystem,
            "candidate.granularity" => CandidateGranularity,
            "fact.negation" => FactNegation.HasValue ? FactNegation.Value.ToString() : null,
            "fact.certainty" => FactCertainty,
            "fact.temporality" => FactTemporality,
            "fact.fact_type" => FactFactType,
            "evidence.level" => EvidenceLevel,
            "evidence.score" => EvidenceScore?.ToString("0.####"),
            "document.document_type" => DocumentDocumentType,
            _ => null
        };
    }

    public decimal? ResolveNumber(string field)
    {
        return field == "evidence.score" ? EvidenceScore : null;
    }
}

/// <summary>
/// 规则命中结果，写入 Trace 与 quality_issue。
/// </summary>
public sealed record RuleMatch(
    string RuleCode,
    string RuleVersion,
    string Severity,
    string Message,
    bool Blocking,
    string? Group,
    string? ActionJson);

/// <summary>
/// 规则引擎输出：PASS / WARNING / BLOCK 与命中的规则列表。
/// 规则失败（存在 WARNING）的推荐不得进入 HIGH_CONFIDENCE。
/// </summary>
public sealed record RuleEvaluationResult(
    decimal RuleScore,
    bool Blocked,
    IReadOnlyList<RuleMatch> Matches)
{
    public static RuleEvaluationResult Empty { get; } = new(1.0m, false, []);
}

/// <summary>
/// 规则求值器。条件为 JSON：
/// { "any": [ { "field": ..., "op": ..., "value": ... } ] } 或
/// { "all": [ ... ] }；缺省语义为 any。
/// 允许的操作：equals / not_equals / contains / in / greater_than /
/// less_than / exists / all / any。
/// </summary>
public static class RuleConditionEvaluator
{
    public static bool Evaluate(string? conditionJson, RuleEvaluationContext context)
    {
        if (string.IsNullOrWhiteSpace(conditionJson))
        {
            // 无条件规则默认命中，由规则引擎决定严重程度。
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(conditionJson);
            return EvaluateElement(document.RootElement, context);
        }
        catch (JsonException)
        {
            // 条件不可解析的规则不得命中（保守处理）。
            return false;
        }
    }

    private static bool EvaluateElement(JsonElement element, RuleEvaluationContext context)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(item => EvaluateElement(item, context));
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty("all", out var all))
        {
            return all.EnumerateArray().All(item => EvaluateElement(item, context));
        }

        if (element.TryGetProperty("any", out var any))
        {
            return any.EnumerateArray().Any(item => EvaluateElement(item, context));
        }

        if (!element.TryGetProperty("field", out var fieldElement)
            || !element.TryGetProperty("op", out var opElement))
        {
            return false;
        }

        var field = fieldElement.GetString() ?? string.Empty;
        var op = opElement.GetString() ?? "equals";
        var value = element.TryGetProperty("value", out var valueElement)
            ? valueElement
            : default;

        return ApplyOperator(op, field, value, context);
    }

    private static bool ApplyOperator(
        string op,
        string field,
        JsonElement value,
        RuleEvaluationContext context)
    {
        switch (op)
        {
            case "exists":
                return context.ResolveField(field) is not null;
            case "equals":
            {
                var actual = context.ResolveField(field);
                return actual is not null && string.Equals(actual, AsText(value), StringComparison.OrdinalIgnoreCase);
            }
            case "not_equals":
            {
                var actual = context.ResolveField(field);
                return actual is null || !string.Equals(actual, AsText(value), StringComparison.OrdinalIgnoreCase);
            }
            case "contains":
            {
                var actual = context.ResolveField(field);
                return actual is not null && actual.Contains(AsText(value), StringComparison.Ordinal);
            }
            case "in":
            {
                var actual = context.ResolveField(field);
                if (actual is null || value.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                return value.EnumerateArray()
                    .Any(item => string.Equals(actual, AsText(item), StringComparison.OrdinalIgnoreCase));
            }
            case "greater_than":
            case "less_than":
            {
                var actual = context.ResolveNumber(field) ?? ParseDecimal(context.ResolveField(field));
                if (actual is null || value.ValueKind != JsonValueKind.Number)
                {
                    return false;
                }

                var expected = value.GetDecimal();
                return op == "greater_than" ? actual > expected : actual < expected;
            }
            default:
                return false;
        }
    }

    private static string AsText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => element.GetRawText(),
            _ => element.GetRawText()
        };
    }

    private static decimal? ParseDecimal(string? value)
    {
        return decimal.TryParse(value, out var parsed) ? parsed : null;
    }
}

/// <summary>
/// 轻量规则引擎执行器。规则处理顺序：
/// 1. 内置规则优先于医院规则。
/// 2. 同一 group 内取 priority 数值最小的规则。
/// 3. blocking=true 直接阻断安全推荐。
/// 4. 所有命中规则写入 Trace 和 QualityIssue。
/// 5. 规则失败不得进入 HIGH_CONFIDENCE。
/// </summary>
public static class RuleEngine
{
    public static RuleEvaluationResult Evaluate(
        IReadOnlyList<RuleDefinition> rules,
        RuleEvaluationContext context)
    {
        if (rules.Count == 0)
        {
            return RuleEvaluationResult.Empty;
        }

        var matches = new List<RuleMatch>();
        var bestByGroup = new Dictionary<string, RuleDefinition>(StringComparer.Ordinal);
        var ungrouped = new List<RuleDefinition>();

        foreach (var rule in rules.OrderBy(item => item.IsBuiltin ? 0 : 1))
        {
            if (!RuleConditionEvaluator.Evaluate(rule.ConditionJson, context))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.Group))
            {
                ungrouped.Add(rule);
                continue;
            }

            if (bestByGroup.TryGetValue(rule.Group, out var existing))
            {
                if (rule.Priority >= existing.Priority)
                {
                    continue;
                }
            }

            bestByGroup[rule.Group] = rule;
        }

        foreach (var rule in ungrouped.Concat(bestByGroup.Values))
        {
            matches.Add(new RuleMatch(
                rule.RuleCode,
                rule.RuleVersion,
                string.IsNullOrWhiteSpace(rule.Severity) ? "INFO" : rule.Severity,
                rule.Message,
                rule.Blocking,
                rule.Group,
                rule.ActionJson));
        }

        if (matches.Count == 0)
        {
            return RuleEvaluationResult.Empty;
        }

        var blocked = matches.Any(item => item.Blocking);
        var severity = blocked
            ? "BLOCK"
            : matches.Any(item => string.Equals(item.Severity, "WARN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Severity, "WARNING", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Severity, "MEDIUM", StringComparison.OrdinalIgnoreCase))
                ? "WARNING"
                : "PASS";

        var score = severity switch
        {
            "BLOCK" => 0.0m,
            "WARNING" => 0.5m,
            _ => 1.0m
        };

        return new RuleEvaluationResult(score, blocked, matches);
    }
}
