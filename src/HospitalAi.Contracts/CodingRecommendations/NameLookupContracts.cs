using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.CodingRecommendations;

/// <summary>
/// "编码录入"名称解析请求。kind = diagnosis（ICD-10）或 procedure（ICD-9-CM-3）。
/// 无状态即时解析，不落库、不建任务。
/// </summary>
public sealed record NameLookupRequest(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("topK")] int? TopK);

/// <summary>
/// 名称解析响应：全量匹配主导词 + 各主导词下的修饰词链路 + 合并后的有效编码推荐。
/// 推荐码全部来自字典展开（IsIndexFallback 标注回退码），与流水线索引链路同一套引擎。
/// 标题通道开关与流水线运行时配置（pipeline_runtime_config "v22lite"）同源。
/// </summary>
public sealed record NameLookupResponse(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("requestText")] string RequestText,
    [property: JsonPropertyName("normalizedText")] string NormalizedText,
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("titleChannels")] NameLookupTitleChannelsInfo TitleChannels,
    [property: JsonPropertyName("dictionaryDirects")] IReadOnlyList<NameLookupDictionaryDirect> DictionaryDirects,
    [property: JsonPropertyName("leadTerms")] IReadOnlyList<NameLookupLeadTerm> LeadTerms,
    [property: JsonPropertyName("recommendations")] IReadOnlyList<NameLookupRecommendation> Recommendations,
    [property: JsonPropertyName("degradedFlags")] IReadOnlyList<string> DegradedFlags);

/// <summary>本次解析实际应用的标题通道开关（供前端回显）。</summary>
public sealed record NameLookupTitleChannelsInfo(
    [property: JsonPropertyName("icdTitleDirect")] bool IcdTitleDirect,
    [property: JsonPropertyName("cm3TitleDirect")] bool Cm3TitleDirect,
    [property: JsonPropertyName("icdTitleReverse")] bool IcdTitleReverse);

/// <summary>字典标题直达命中（输入名与字典标题逐字相等或被完整包含）。</summary>
public sealed record NameLookupDictionaryDirect(
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("depth")] decimal Depth);

/// <summary>
/// 全量匹配主导词：Term 为索引原始词面（如 细胞(性)），HitAlias 为实际命中的归一化别名键。
/// </summary>
public sealed record NameLookupLeadTerm(
    [property: JsonPropertyName("seqNo")] int SeqNo,
    [property: JsonPropertyName("term")] string Term,
    [property: JsonPropertyName("hitAlias")] string HitAlias,
    [property: JsonPropertyName("coveredChars")] int CoveredChars,
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("tailHit")] bool TailHit,
    [property: JsonPropertyName("modifierPaths")] IReadOnlyList<NameLookupModifierPath> ModifierPaths,
    /// <summary>非空表示经失效主导词转示行锚定（输入含 阿米巴，索引写 "阿米巴 - 见 阿米巴病"，Term=阿米巴病）。</summary>
    [property: JsonPropertyName("redirectKey")] string? RedirectKey = null,
    /// <summary>逗号同义族行的直观显示词面（"梗死，梗塞" → 梗死）；空 = 直接显示 Term。</summary>
    [property: JsonPropertyName("displayTerm")] string? DisplayTerm = null);

/// <summary>
/// 一个主导词下按输入走出的修饰词链路（Path[0] 恒为主导词）；Codes 为该链路展开出的字典有效码。
/// </summary>
public sealed record NameLookupModifierPath(
    [property: JsonPropertyName("path")] IReadOnlyList<string> Path,
    [property: JsonPropertyName("pathKey")] string PathKey,
    [property: JsonPropertyName("indexCode")] string? IndexCode,
    [property: JsonPropertyName("fromAncestorFallback")] bool FromAncestorFallback,
    [property: JsonPropertyName("jumps")] IReadOnlyList<string> Jumps,
    [property: JsonPropertyName("seeAlsoHint")] bool SeeAlsoHint,
    [property: JsonPropertyName("codes")] IReadOnlyList<NameLookupCodeItem> Codes);

/// <summary>链路展开出的候选码（来自字典展开或显式索引回退）。</summary>
public sealed record NameLookupCodeItem(
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("score")] decimal Score,
    [property: JsonPropertyName("isIndexFallback")] bool IsIndexFallback,
    [property: JsonPropertyName("note")] string? Note);

/// <summary>
/// 合并后的最终推荐（跨主导词去重）。DictionaryDirect=true 表示来自字典标题直达，
/// 否则来自索引链路，LeadTerm/Path 为证据链。
/// </summary>
public sealed record NameLookupRecommendation(
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("score")] decimal Score,
    [property: JsonPropertyName("isIndexFallback")] bool IsIndexFallback,
    [property: JsonPropertyName("dictionaryDirect")] bool DictionaryDirect,
    [property: JsonPropertyName("leadTerm")] string? LeadTerm,
    [property: JsonPropertyName("path")] IReadOnlyList<string> Path,
    [property: JsonPropertyName("note")] string? Note);
