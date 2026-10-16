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

/// <summary>
/// ICD 索引库导入请求（全列保留，源自《ICD索引整理_编码规则修正版_最终版.xlsx》）。
/// </summary>
public sealed record ImportIcdIndexRequest(
    [property: JsonPropertyName("items")] IReadOnlyList<IcdIndexImportItem> Items);

/// <summary>
/// ICD 索引库单条导入项。leadWordStatus：0=有效，1=无效。
/// </summary>
public sealed record IcdIndexImportItem(
    [property: JsonPropertyName("seqNo")] int SeqNo,
    [property: JsonPropertyName("rawName")] string RawName,
    [property: JsonPropertyName("rawNameEn")] string? RawNameEn,
    [property: JsonPropertyName("indexName")] string IndexName,
    [property: JsonPropertyName("indexCode")] string? IndexCode,
    [property: JsonPropertyName("indexLevel")] int IndexLevel,
    [property: JsonPropertyName("parentSeqNo")] int ParentSeqNo,
    [property: JsonPropertyName("fullIndexName")] string FullIndexName,
    [property: JsonPropertyName("leadWordStatus")] int LeadWordStatus);

/// <summary>
/// ICD 索引库导入结果。
/// </summary>
public sealed record ImportIcdIndexResponse(
    [property: JsonPropertyName("importedCount")] int ImportedCount,
    [property: JsonPropertyName("updatedCount")] int UpdatedCount);

/// <summary>
/// CM3（手术操作）索引库导入请求（全列保留，源自《CM3索引整理_最终版.xlsx》sheet「索引中英对照」）。
/// </summary>
public sealed record ImportCm3IndexRequest(
    [property: JsonPropertyName("items")] IReadOnlyList<Cm3IndexImportItem> Items);

/// <summary>
/// CM3 索引库单条导入项。leadWordStatus：0=有效，1=无效。
/// </summary>
public sealed record Cm3IndexImportItem(
    [property: JsonPropertyName("seqNo")] int SeqNo,
    [property: JsonPropertyName("rawName")] string RawName,
    [property: JsonPropertyName("rawNameEn")] string? RawNameEn,
    [property: JsonPropertyName("indexName")] string IndexName,
    [property: JsonPropertyName("indexCode")] string? IndexCode,
    [property: JsonPropertyName("indexLevel")] int IndexLevel,
    [property: JsonPropertyName("parentSeqNo")] int ParentSeqNo,
    [property: JsonPropertyName("fullIndexName")] string FullIndexName,
    [property: JsonPropertyName("leadWordStatus")] int LeadWordStatus);

/// <summary>
/// CM3 索引库导入结果。
/// </summary>
public sealed record ImportCm3IndexResponse(
    [property: JsonPropertyName("importedCount")] int ImportedCount,
    [property: JsonPropertyName("updatedCount")] int UpdatedCount);

/// <summary>
/// 术语同义词组导入请求（termType：DIAGNOSIS=诊断、PROCEDURE=手术，两侧口径互不串用）。
/// </summary>
public sealed record ImportTermSynonymRequest(
    [property: JsonPropertyName("items")] IReadOnlyList<TermSynonymGroupImportItem> Items);

/// <summary>
/// 单个同义词组：Variants 中每个变体词在索引匹配前被改写为 CanonicalTerm。
/// </summary>
public sealed record TermSynonymGroupImportItem(
    [property: JsonPropertyName("termType")] string TermType,
    [property: JsonPropertyName("canonicalTerm")] string CanonicalTerm,
    [property: JsonPropertyName("variants")] IReadOnlyList<string> Variants);

/// <summary>术语同义词导入结果。</summary>
public sealed record ImportTermSynonymResponse(
    [property: JsonPropertyName("importedCount")] int ImportedCount,
    [property: JsonPropertyName("updatedCount")] int UpdatedCount);

/// <summary>
/// 单条索引链路同义词（页面增删改查用）。字段为空表示沿用库中原值，
/// isEnabled 为 null 时不改动启停状态。
/// </summary>
public sealed record IcdTermSynonymInput(
    [property: JsonPropertyName("termType")] string? TermType,
    [property: JsonPropertyName("canonicalTerm")] string? CanonicalTerm,
    [property: JsonPropertyName("variantTerm")] string? VariantTerm,
    [property: JsonPropertyName("isEnabled")] bool? IsEnabled);
