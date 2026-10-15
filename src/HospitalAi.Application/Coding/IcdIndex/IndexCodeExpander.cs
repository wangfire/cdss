using System.Text.RegularExpressions;
using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Coding.IcdIndex;

/// <summary>medical_code 表行投影（按医院 + 编码体系加载）。</summary>
public sealed record IcdDictionaryRow(
    string CodeSystemCode,
    string Code,
    string Title,
    string SearchText);

/// <summary>
/// 字典快照：预计算归一化键，展开阶段纯内存匹配，
/// 规避 SQL Server 大小写不敏感 collation 对编码前缀比对的干扰。
/// </summary>
public sealed class IcdDictionarySnapshot
{
    private readonly List<DictionaryEntry> _entries;

    private sealed record DictionaryEntry(
        IcdDictionaryRow Row,
        string UpperCode,
        string TitleKey,
        string SearchTextKey);

    public IcdDictionarySnapshot(IReadOnlyList<IcdDictionaryRow> rows)
    {
        _entries = rows.Select(row => new DictionaryEntry(
            row,
            row.Code.ToUpperInvariant(),
            TextMatch.NormalizeKey(row.Title),
            TextMatch.NormalizeKey(row.SearchText))).ToList();
    }

    public int Count => _entries.Count;

    public IReadOnlyList<IcdDictionaryRow> Prefix(string codePrefixUpper) => _entries
        .Where(entry => entry.UpperCode.StartsWith(codePrefixUpper, StringComparison.Ordinal))
        .Select(entry => entry.Row)
        .ToList();

    public IReadOnlyList<IcdDictionaryRow> Joint(string daggerUpper, string asteriskUpper) => _entries
        .Where(entry => entry.UpperCode.StartsWith(daggerUpper, StringComparison.Ordinal)
            && entry.UpperCode.Contains("+" + asteriskUpper, StringComparison.Ordinal))
        .Select(entry => entry.Row)
        .ToList();
}

/// <summary>展开产物：字典详码或索引码回退。</summary>
public sealed record ExpandedItem(
    string CodeSystemCode,
    string Code,
    string Title,
    decimal Score,
    IndexCodeFormatKind Format,
    bool IsIndexFallback,
    bool IsConditionalAddOn,
    string? Note,
    decimal TitleSpecificity = 0m,
    decimal WholeTitleDepth = 0m,
    /// <summary>同族（同一索引编码展开批次）内的精排名次：0 = 族内最优。
    /// 分数同族普遍钳位到 1.0，该名次是把族内秩序带给下游评分的通道。</summary>
    int FamilyRank = 0);

/// <summary>
/// 索引编码 → 字典详码展开器。联合码优先、三级回退：
/// 联合展开为空 → † 码单码展开；整体无结果 → 回退推荐索引编码本身。
/// </summary>
public static class IndexCodeExpander
{
    public const decimal FallbackScore = 0.55m;

    private const decimal GateRatio = 0.6m;
    private const int GateMaxSegment = 3;

    /// <summary>整标题连续命中要抬到满分，标题至少得解释掉输入的这么多字符（上位码防抬）。</summary>
    private const decimal WholeTitleMinInputRatio = 0.75m;

    /// <summary>
    /// 派生形容词后缀：字典标题的 性 是构词虚词（高血压性视网膜病变 ≡ 高血压视网膜病变），
    /// 只从标题侧剥，且剥后的词干仍须被输入逐字连续包含，避免把不相关标题判成整标题命中。
    /// 词干下限 5 是实测值：4 字词干（病毒感染、乳腺增生 这类）都是上位类词，
    /// 输入 肠病毒感染 会把 病毒性感染 B34.900 顶到具体兄弟码前面，是噪声不是命中。
    /// </summary>
    private const char AdjectivalSuffixCharacter = '性';
    private const int AdjectivalMinTitleLength = 5;
    private const int AdjectivalMinStrippedLength = 5;

    public static IReadOnlyList<ExpandedItem> Expand(
        ParsedIndexCode parsed,
        IcdDictionarySnapshot dictionary,
        string inputText,
        string pathKey,
        int maxResults,
        string fallbackCodeSystem = "ICD-10")
    {
        // 覆盖度分母用剥掉不确定性引导语后的输入核："十二指肠溃疡伴出血可能性大" 的 性 会给
        // 慢性十二指肠溃疡伴有出血 白送一分覆盖，把与输入逐字相等的 K26.400X001 挤到 Top-2。
        var inputKey = TextMatch.StripUncertaintyPhrases(inputText);
        var results = ExpandCore(parsed, dictionary, inputText, inputKey, pathKey, fallbackCodeSystem);

        // 先按编码收敛，再统一排序，避免下游按编码去重时由字典行序决定谁占住 Top-N 名额。
        return Rank(results
                .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => Rank(group).First()))
            .Take(maxResults)
            .ToList();
    }

    private static IReadOnlyList<ExpandedItem> ExpandCore(
        ParsedIndexCode parsed,
        IcdDictionarySnapshot dictionary,
        string inputText,
        string inputKey,
        string pathKey,
        string fallbackCodeSystem,
        bool inAlternatives = false)
    {
        switch (parsed.Kind)
        {
            case IndexCodeFormatKind.Alternatives:
                return ExpandAlternatives(parsed, dictionary, inputText, inputKey, pathKey, fallbackCodeSystem);

            case IndexCodeFormatKind.Conditional:
                return ExpandConditional(parsed, dictionary, inputText, inputKey, pathKey,
                    fallbackCodeSystem, inAlternatives);

            case IndexCodeFormatKind.Morphology:
            {
                var site = parsed.SiteCode ?? string.Empty;
                var items = ExpandByPrefix(
                    site.ToUpperInvariant(), dictionary, inputText, inputKey, pathKey,
                    parsed.Kind, excludeJoint: false, inAlternatives,
                    note: $"形态学编码 {parsed.MorphologyCode}");
                return items.Count > 0 ? items : FallbackItems(parsed, dictionary, inputText, fallbackCodeSystem);
            }

            case IndexCodeFormatKind.DaggerPair:
            {
                var dagger = (parsed.DaggerCode ?? string.Empty).ToUpperInvariant();
                var asterisk = (parsed.AsteriskCode ?? string.Empty).ToUpperInvariant();
                if (dagger.Length > 0 && asterisk.Length > 0)
                {
                    var joint = dictionary.Joint(dagger, asterisk);
                    var jointItems = RankRows(joint, inputText, inputKey, pathKey, parsed.Kind,
                        inAlternatives, note: null);
                    if (jointItems.Count > 0)
                    {
                        return jointItems;
                    }
                }

                // 联合展开为空 → 病因/表现两码分别展开，成对给出。
                var pair = new List<ExpandedItem>();
                if (dagger.Length > 0)
                {
                    pair.AddRange(ExpandByPrefix(dagger, dictionary, inputText, inputKey, pathKey,
                        parsed.Kind, excludeJoint: true, inAlternatives,
                        note: $"联合编码病因码（配 {parsed.AsteriskCode}*）"));
                }

                if (asterisk.Length > 0 && pair.Count < 3)
                {
                    pair.AddRange(ExpandByPrefix(asterisk, dictionary, inputText, inputKey, pathKey,
                        parsed.Kind, excludeJoint: false, inAlternatives,
                        note: $"联合编码表现码（配 {parsed.DaggerCode}†）"));
                }

                return pair.Count > 0 ? pair : FallbackItems(parsed, dictionary, inputText, fallbackCodeSystem);
            }

            case IndexCodeFormatKind.CodeRange:
            {
                var prefixes = ParsedIndexCode.RangePrefixes(
                    parsed.RangeStart ?? string.Empty, parsed.RangeEnd ?? string.Empty);
                if (prefixes.Count == 0)
                {
                    return FallbackItems(parsed, dictionary, inputText, fallbackCodeSystem);
                }

                var rows = prefixes
                    .SelectMany(prefix => dictionary.Prefix(prefix))
                    .ToList();
                var items = RankRows(rows, inputText, inputKey, pathKey, parsed.Kind,
                    inAlternatives, note: null);
                return items.Count > 0 ? items : FallbackItems(parsed, dictionary, inputText, fallbackCodeSystem);
            }

            case IndexCodeFormatKind.CategoryUnspecified:
            case IndexCodeFormatKind.Single:
            case IndexCodeFormatKind.Empty:
            default:
            {
                var prefix = (parsed.Prefix ?? parsed.Raw).ToUpperInvariant().TrimEnd('*');
                if (prefix.Length < 3)
                {
                    return FallbackItems(parsed, dictionary, inputText, fallbackCodeSystem);
                }

                var items = ExpandByPrefix(prefix, dictionary, inputText, inputKey, pathKey,
                    parsed.Kind, excludeJoint: false, inAlternatives, note: null);
                return items.Count > 0 ? items : FallbackItems(parsed, dictionary, inputText, fallbackCodeSystem);
            }
        }
    }

    private static IReadOnlyList<ExpandedItem> ExpandByPrefix(
        string prefixUpper,
        IcdDictionarySnapshot dictionary,
        string inputText,
        string inputKey,
        string pathKey,
        IndexCodeFormatKind format,
        bool excludeJoint,
        bool inAlternatives,
        string? note)
    {
        var rows = dictionary.Prefix(prefixUpper);
        if (excludeJoint)
        {
            rows = rows.Where(row => !row.Code.Contains('+', StringComparison.Ordinal)).ToList();
        }

        return RankRows(rows, inputText, inputKey, pathKey, format, inAlternatives, note);
    }

    private static IReadOnlyList<ExpandedItem> ExpandConditional(
        ParsedIndexCode parsed,
        IcdDictionarySnapshot dictionary,
        string inputText,
        string inputKey,
        string pathKey,
        string fallbackCodeSystem,
        bool inAlternatives)
    {
        // 触发码 = 本行主码；索引链路已完成语义验证，字典展不开时按索引码原样推荐。
        var triggerItems = parsed.Trigger is null
            ? []
            : ExpandCore(parsed.Trigger, dictionary, inputText, inputKey, pathKey,
                fallbackCodeSystem, inAlternatives);
        IReadOnlyList<ExpandedItem> result = triggerItems.Count > 0
            ? triggerItems.Select(item => item with { Score = Math.Max(item.Score, 0.62m) }).ToList()
            :
            [
                new ExpandedItem(fallbackCodeSystem, parsed.Trigger?.Raw ?? parsed.Raw, inputText,
                    0.62m, IndexCodeFormatKind.Single, IsIndexFallback: true,
                    IsConditionalAddOn: false, Note: "触发码在字典中无展开，按索引码原样推荐"),
            ];

        // @ 语义（修正版）：仅当已推导出 @ 前触发码时，才附加推荐 @ 后附加码。
        if (parsed.AddOn is { } addOn)
        {
            var addOnItems = ExpandCore(addOn, dictionary, inputText, inputKey, pathKey,
                fallbackCodeSystem, inAlternatives);
            var chosen = addOnItems.FirstOrDefault(item => !item.IsIndexFallback) ?? addOnItems.FirstOrDefault();
            if (chosen is not null)
            {
                result = [.. result, chosen with
                {
                    Score = Math.Min(chosen.Score, 0.72m),
                    IsConditionalAddOn = true,
                    Note = $"条件联动附加码：因已推出 {parsed.Trigger?.Raw} 而追加推荐",
                }];
            }
        }

        return result;
    }

    private static IReadOnlyList<ExpandedItem> ExpandAlternatives(
        ParsedIndexCode parsed,
        IcdDictionarySnapshot dictionary,
        string inputText,
        string inputKey,
        string pathKey,
        string fallbackCodeSystem)
    {
        var alternatives = parsed.Alternatives ?? [];
        var scored = alternatives
            .Select(alt =>
            {
                var items = ExpandCore(alt, dictionary, inputText, inputKey, pathKey,
                    fallbackCodeSystem, inAlternatives: true);
                return (Alt: alt, Items: items, Best: items.Count > 0 ? items.Max(item => item.Score) : 0m);
            })
            .Where(entry => !entry.Items.Any(item => item.IsIndexFallback))
            .OrderByDescending(entry => entry.Best)
            .ToList();

        if (scored.Count == 0)
        {
            return FallbackItems(parsed, dictionary, inputText, fallbackCodeSystem);
        }

        // 病历信息不足以区分替代编码（并列/全部低分）→ 回退主导词未特指码，禁止全量输出。
        var top = scored[0];
        var second = scored.Count > 1 ? scored[1].Best : 0m;
        if (top.Best <= 0m || top.Best - second < 0.15m)
        {
            return FallbackItems(parsed, dictionary, inputText, fallbackCodeSystem);
        }

        return top.Items;
    }

    private static IReadOnlyList<ExpandedItem> RankRows(
        IReadOnlyList<IcdDictionaryRow> rows,
        string inputText,
        string inputKey,
        string pathKey,
        IndexCodeFormatKind format,
        bool inAlternatives,
        string? note)
    {
        if (rows.Count == 0 || inputKey.Length == 0)
        {
            return [];
        }

        var results = new List<ExpandedItem>();
        foreach (var row in rows)
        {            if (TextMatch.IsUnjustifiedDiabetesCompound(row.Title, inputText)
                || TextMatch.HasDiabetesTypeConflict(row.Title, row.Code, inputText, inAlternatives)
                || TextMatch.HasNegationPoleConflict(row.Title, inputText)
                || TextMatch.HasBodyPartConflict(row.Title, inputText))
            {
                continue;
            }

            var titleKey = TextMatch.NormalizeKey(row.Title);
            var searchTextKey = TextMatch.NormalizeKey(row.SearchText);
            var titleCover = TextMatch.CoverageOf(inputKey, titleKey);
            var inputCover = Best(titleCover, TextMatch.CoverageOf(inputKey, searchTextKey));
            // 命中路径按索引顺序拼接（钙化→肾 = "钙化肾"），字典标题是自然语序（"肾钙化"），
            // 正序/逆序镜像 + 单字包含口径取较大覆盖，做 "路径词被标题完整覆盖" 放行判定。
            var pathCover = Best(
                Best(
                    TextMatch.CoverageOf(pathKey, titleKey),
                    TextMatch.CoverageOf(pathKey, titleKey, countSingleCharSegments: true)),
                TextMatch.CoverageOf(TextMatch.Reverse(pathKey), titleKey, countSingleCharSegments: true));
            var maxSegment = Math.Max(inputCover.MaxSegment, pathCover.MaxSegment);

            // 门控口径与字典直匹配一致：输入被标题连续覆盖 ≥0.6（最长段≥3 或全覆盖）；
            // 命中路径词被标题完整覆盖时放行（修饰词就是标题核心词的场景）。
            var inputPass = inputCover.Ratio >= GateRatio
                && (inputKey.Length < 3 || maxSegment >= GateMaxSegment || inputCover.Ratio >= 1.0m);
            if (!inputPass && pathCover.Ratio < 1.0m)
            {
                continue;
            }

            // 路径通道按 0.9 参与打分：一层链路的 pathKey 就是主导词本身，标题含主导词即
            // 完整覆盖，这是索引书的共现口径。去掉这层抬高会让 "高血压3级"（输入覆盖 0.714）
            // 输给检索串聚合了上位类的 "高血压"，438 个名字的 Top1 改道。
            // 同为 0.9 的并列交给下面的 TitleSpecificity 选与输入逐字更贴的标题。
            var score = Math.Max(inputCover.Ratio, pathCover.Ratio * 0.9m);

            // 标题被输入逐字连续包含 = 编码员视觉上认的"就是这一条"。段贪心覆盖会把它和
            // 拆成几段沾满输入的长标题算成同分（输入 甲状腺肿大：甲状腺肿 与 甲状腺锥叶代偿性肿大
            // 都是 5/5 全覆盖），只靠反向覆盖并列择一会让长标题赢；连续整标题优先。
            // 门槛不可省：糖尿病 ⊂ 2型糖尿病性酮症酸中毒（输入覆盖 0.33），无条件抬满分
            // 会把上位码顶到 Top-1。
            // 标题只比输入多一个派生虚词 "性" 时（临床写 高血压视网膜病变、字典写 高血压性视网膜病变），
            // 逐字包含判定失败，反而是短上位标题 视网膜病 被输入包含、凭满分占 Top-1。
            // 仅标题侧剥 "性"，剥后的词干仍须被输入逐字连续包含。
            var wholeTitleKey = titleKey;
            var wholeTitleHit = titleKey.Length > 0
                && inputKey.Contains(titleKey, StringComparison.Ordinal)
                && inputCover.Ratio >= WholeTitleMinInputRatio;
            if (!wholeTitleHit
                && titleKey.Contains(AdjectivalSuffixCharacter)
                && titleKey.Length >= AdjectivalMinTitleLength)
            {
                var strippedTitleKey = titleKey.Replace(
                    AdjectivalSuffixCharacter.ToString(), string.Empty, StringComparison.Ordinal);
                if (strippedTitleKey.Length >= AdjectivalMinStrippedLength
                    && inputKey.Contains(strippedTitleKey, StringComparison.Ordinal)
                    && inputCover.Ratio >= WholeTitleMinInputRatio)
                {
                    wholeTitleHit = true;
                    wholeTitleKey = strippedTitleKey;
                }
            }
            if (wholeTitleHit)
            {
                score = 1.0m;
            }
            results.Add(new ExpandedItem(
                row.CodeSystemCode, row.Code, row.Title, score, format,
                IsIndexFallback: false, IsConditionalAddOn: false, note,
                TitleSpecificity: TextMatch.CoverageOf(titleKey, inputKey).Ratio,
                WholeTitleDepth: wholeTitleHit
                    ? Math.Min(1m, wholeTitleKey.Length / (decimal)inputKey.Length)
                    : 0m));
        }

        // 括号限定词纪律（B3-4 粒度调优）：标题括号内是特异性限定词（高危/低危/部位/NEC 等）。
        // 同族内存在"限定词全部被输入满足"的兄弟行时，限定词未被满足的行剔除——
        // 输入未写 高危 就不得编 高血压病2级(高危)，且不受重排噪声影响；
        // 整族都不满足时全部保留（部分编码只以带括号形态存在，防整族丢失）。
        var satisfiedRows = results.Where(item => BracketQualifiersSatisfied(item.Title, inputKey)).ToList();
        var rowsToRank = satisfiedRows.Count > 0 ? satisfiedRows : results;

        var ranked = Rank(rowsToRank)
            .Take(10)
            .ToList();
        // 族内名次注入：同族分数几乎必然钳位同分，名次是下游抗重排噪声的唯一信号。
        for (var i = 0; i < ranked.Count; i++)
        {
            ranked[i] = ranked[i] with { FamilyRank = i };
        }

        return ranked;
    }

    private static TextMatch.Coverage Best(TextMatch.Coverage left, TextMatch.Coverage right)
    {
        return left.Ratio >= right.Ratio ? left : right;
    }

    private static readonly Regex BracketQualifierRegex = new("[（(]([^（）)]*)[）)]", RegexOptions.Compiled);

    /// <summary>标题全部括号限定词（高危/部位/NEC 等）都出现在输入中（无括号视为满足）。</summary>
    private static bool BracketQualifiersSatisfied(string title, string inputKey)
    {
        foreach (Match match in BracketQualifierRegex.Matches(title))
        {
            var qualifier = TextMatch.NormalizeKey(match.Groups[1].Value);
            if (qualifier.Length > 0 && !inputKey.Contains(qualifier, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 展开结果排序口径：分数 → 整标题贴合度 → 标题反向覆盖 → 编码长度。
    /// 贴合度挡住同分下的上位/下位并列：窦性心动过缓 与 心动过缓 都被输入逐字包含、
    /// 反向覆盖都是 1.0，按编码长度会让上位码 R00.100 抢占 Top-1；取解释掉输入字符更多的标题。
    /// 反向覆盖并列优先，挡住只靠 search_text 聚合上位类的编码（甲状腺毒症 E05.900 收了
    /// "甲状腺功能亢进性心脏病"）压过与输入逐字相等的子码，也挡住自带输入未提及限定词的
    /// 兄弟码（高血压病3级(极高危) 抢 高血压3级 I10.X05）。
    /// </summary>
    private static IOrderedEnumerable<ExpandedItem> Rank(IEnumerable<ExpandedItem> items) => items
        .OrderByDescending(item => item.Score)
        .ThenByDescending(item => item.WholeTitleDepth)
        .ThenByDescending(item => item.TitleSpecificity)
        .ThenBy(item => item.Code.Length);

    private static ExpandedItem Fallback(ParsedIndexCode parsed, string inputText, string fallbackCodeSystem)
    {
        return new ExpandedItem(
            CodeSystemCode: fallbackCodeSystem,
            Code: parsed.Raw,
            Title: inputText,
            Score: FallbackScore,
            Format: parsed.Kind,
            IsIndexFallback: true,
            IsConditionalAddOn: false,
            Note: "索引编码在字典中无展开结果，回退推荐索引编码本身");
    }

    // 裸形态学码（M8140/3）：诊断字典（ICD-10）里 0 行，它只是肿瘤学形态/行为码，
    // 没有部位与行为语义，不能作为临床编码出推荐。
    private static readonly Regex BareMorphologyRegex =
        new(@"^M\d{4}/\d$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 回退产物：推荐必须落在字典里真实存在的编码上。索引原串（"O82.9"、"S31.0"、
    /// "E85.4† K93.8*"）不是可采用的临床编码，下游按编码取标题会取空，
    /// 所以按 †/* 与 ; 拆成单码，各自取字典同前缀最泛码（星码按其本身编码）；
    /// 裸形态学码直接丢弃，宁可不推也不推一个查不到的码。
    /// </summary>
    private static IReadOnlyList<ExpandedItem> FallbackItems(
        ParsedIndexCode parsed,
        IcdDictionarySnapshot dictionary,
        string inputText,
        string fallbackCodeSystem)
    {
        var components = parsed.Kind switch
        {
            IndexCodeFormatKind.DaggerPair
                when parsed.DaggerCode is { Length: > 0 } dagger && parsed.AsteriskCode is { Length: > 0 } asterisk
                => [dagger, asterisk],
            IndexCodeFormatKind.Morphology when parsed.SiteCode is { Length: > 0 } site => [site],
            IndexCodeFormatKind.Alternatives
                when parsed.Alternatives is { } alternatives => alternatives.Select(item => item.Raw).ToList(),
            _ => [parsed.Raw],
        };

        var items = new List<ExpandedItem>();
        foreach (var component in components)
        {
            var key = component.Trim().TrimEnd('*').ToUpperInvariant();
            if (key.Length == 0 || BareMorphologyRegex.IsMatch(key))
            {
                continue;
            }

            var rows = dictionary.Prefix(key);
            var exact = rows.FirstOrDefault(row =>
                row.Code.TrimEnd('*').Equals(key, StringComparison.Ordinal));
            var chosen = exact
                ?? rows
                    .OrderBy(row => row.Code.Length)
                    .ThenBy(row => row.Code, StringComparer.Ordinal)
                    .FirstOrDefault();
            items.Add(chosen is null
                ? Fallback(parsed, inputText, fallbackCodeSystem)
                : new ExpandedItem(
                    fallbackCodeSystem,
                    chosen.Code,
                    chosen.Title,
                    FallbackScore,
                    parsed.Kind,
                    IsIndexFallback: true,
                    IsConditionalAddOn: false,
                    Note: $"索引编码 {parsed.Raw} 在字典中无展开结果，回退推荐同前缀码"));
        }

        return items;
    }
}
