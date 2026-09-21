using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.CodingKnowledge;

/// <summary>
/// ICD 编码字典导入请求。
/// </summary>
public sealed record ImportCodeSystemRequest(
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("codes")] IReadOnlyList<MedicalCodeImportItem> Codes);

/// <summary>
/// 单条 ICD 编码导入项。
/// </summary>
public sealed record MedicalCodeImportItem(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("codeType")] string CodeType,
    [property: JsonPropertyName("searchText")] string? SearchText,
    [property: JsonPropertyName("isEnabled")] bool IsEnabled);

/// <summary>
/// 编码字典导入结果。
/// </summary>
public sealed record ImportCodeSystemResponse(
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("importedCount")] int ImportedCount,
    [property: JsonPropertyName("updatedCount")] int UpdatedCount);

/// <summary>
/// 同义词与规则导入请求。
/// </summary>
public sealed record ImportCodingRulesRequest(
    [property: JsonPropertyName("synonyms")] IReadOnlyList<TermSynonymImportItem> Synonyms,
    [property: JsonPropertyName("rules")] IReadOnlyList<CodingRuleImportItem> Rules);

/// <summary>
/// 医学术语同义词导入项。
/// </summary>
public sealed record TermSynonymImportItem(
    [property: JsonPropertyName("term")] string Term,
    [property: JsonPropertyName("normalizedTerm")] string NormalizedTerm,
    [property: JsonPropertyName("entityType")] string EntityType,
    [property: JsonPropertyName("codeSystemCode")] string CodeSystemCode = "ICD-10",
    [property: JsonPropertyName("code")] string Code = "");

/// <summary>
/// 编码规则导入项。V2.2-Lite 起支持版本化 JSON 条件规则；旧平面规则沿用原有字段，
/// conditionJson 为空时按 codePattern + ruleType 兼容评估。
/// </summary>
public sealed record CodingRuleImportItem(
    [property: JsonPropertyName("ruleCode")] string RuleCode,
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("codePattern")] string CodePattern,
    [property: JsonPropertyName("ruleType")] string RuleType,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("isEnabled")] bool IsEnabled,
    [property: JsonPropertyName("ruleVersion")] string RuleVersion = "v1",
    [property: JsonPropertyName("priority")] int Priority = 100,
    [property: JsonPropertyName("group")] string? Group = null,
    [property: JsonPropertyName("conditionJson")] string? ConditionJson = null,
    [property: JsonPropertyName("actionJson")] string? ActionJson = null,
    [property: JsonPropertyName("blocking")] bool Blocking = false,
    [property: JsonPropertyName("isBuiltin")] bool IsBuiltin = false);

/// <summary>
/// 编码规则导入结果。
/// </summary>
public sealed record ImportCodingRulesResponse(
    [property: JsonPropertyName("synonymCount")] int SynonymCount,
    [property: JsonPropertyName("ruleCount")] int RuleCount);
