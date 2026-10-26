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

    /// <summary>
    /// 字典标题直达匹配：标题键与输入键逐字相等，或标题键（≥3 字）被输入键逐字完整包含
    /// 且贴合度（标题长/输入长）≥0.5——解释不了输入一半的短泛标题不是直达证据
    /// （肠吻合术 ⊂ 胃空肠吻合术 的教训）。
    /// 只认硬词面证据（复用预计算键），供手术索引链路的直达候选使用；
    /// 刻意不认"输入被标题覆盖"的模糊方向——女阴/外阴族的教训，0.9 级别的模糊覆盖
    /// 会把 广泛性/根治性 一类变体错配。返回按贴合度降序，同码去重。
    /// </summary>
    public IReadOnlyList<(IcdDictionaryRow Row, decimal Depth)> TitleDirectMatches(string inputKey)
    {
        if (inputKey.Length == 0)
        {
            return [];
        }

        var matches = new List<(IcdDictionaryRow Row, decimal Depth, string TitleKey)>();
        foreach (var entry in _entries)
        {
            if (entry.TitleKey.Length < 2)
            {
                continue;
            }

            if (string.Equals(entry.TitleKey, inputKey, StringComparison.Ordinal)
                || string.Equals(entry.SearchTextKey, inputKey, StringComparison.Ordinal))
            {
                matches.Add((entry.Row, 1m, entry.TitleKey));
            }
            else if (entry.TitleKey.Length >= 3
                && inputKey.Contains(entry.TitleKey, StringComparison.Ordinal))
            {
                var depth = Math.Min(1m, entry.TitleKey.Length / (decimal)inputKey.Length);
                if (depth >= 0.5m)
                {
                    matches.Add((entry.Row, depth, entry.TitleKey));
                }
            }
        }

        // 极大性：包含匹配若被任一已准入标题包含则丢弃——更长标题已经逐字解释了这段词面，
        // 短泛包含不该挤进同一场竞争（肠吻合术 ⊂ 胆囊肠吻合术 的教训）。
        return matches
            .Where(match => match.Depth >= 1m || !matches.Any(other =>
                other.TitleKey.Length > match.TitleKey.Length
                && other.TitleKey.Contains(match.TitleKey, StringComparison.Ordinal)))
            .GroupBy(match => match.Row.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(match => match.Depth)
            .ThenBy(match => match.Row.Code, StringComparer.Ordinal)
            .Select(match => (match.Row, match.Depth))
            .ToList();
    }
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
        string fallbackCodeSystem = "ICD-10",
        TumorIndexSnapshot? tumor = null,
        bool titleReverseEnabled = true)
    {
        // 覆盖度分母用剥掉不确定性引导语后的输入核："十二指肠溃疡伴出血可能性大" 的 性 会给
        // 慢性十二指肠溃疡伴有出血 白送一分覆盖，把与输入逐字相等的 K26.400X001 挤到 Top-2。
        var inputKey = TextMatch.StripUncertaintyPhrases(inputText);
        var results = ExpandCore(parsed, dictionary, inputText, inputKey, pathKey, fallbackCodeSystem,
            tumor: tumor, titleReverseEnabled: titleReverseEnabled);

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
        bool inAlternatives = false,
        TumorIndexSnapshot? tumor = null,
        bool titleReverseEnabled = true)
    {
        switch (parsed.Kind)
        {
            case IndexCodeFormatKind.Alternatives:
                return ExpandAlternatives(parsed, dictionary, inputText, inputKey, pathKey,
                    fallbackCodeSystem, tumor, titleReverseEnabled);

            case IndexCodeFormatKind.Conditional:
                return ExpandConditional(parsed, dictionary, inputText, inputKey, pathKey,
                    fallbackCodeSystem, inAlternatives, tumor, titleReverseEnabled);

            case IndexCodeFormatKind.Morphology:
            {
                // 索引已给部位码（M8332/3:C73）→ 直接用，不查肿瘤表；
                // 裸形态学码（M8510/3）→ 按行为位到肿瘤索引表查特指部位码。
                var morph = parsed.MorphologyCode;
                IReadOnlyList<(string Prefix, string? Note)> families = [];
                if (parsed.SiteCode is { Length: > 0 } indexSite)
                {
                    families = [(indexSite, null)];
                }
                else if (tumor is not null && morph is { } morphologyCode)
                {
                    // 肿瘤表解出的并列族（颅◇=C79.5 与 颅(窝，任何)=C79.3 之于 颅底、
                    // 腹壁 在 神经/皮肤/结缔组织 三族）：表本身分不出，但字典标题分得出——
                    // 都展开交给字典门控裁决，不再一枪打死整张表。
                    families = tumor.ResolveSiteCodes(morphologyCode, inputText)
                        .Select(match => (match.Code, (string?)SiteNote(match, morphologyCode)))
                        .ToList();

                    // 部位词一个都没锚上表，且形态学码本身不含部位信息（M8000/x 肿瘤、M8010/x 癌）：
                    // 交付肿瘤表对"未特指部位"给的码（C80 / D36.9 / D09.9 / D48.9），
                    // 而不是 M80000/3 这种补零后仍然没有部位的形态学码。
                    if (families.Count == 0 && IsSitelessMorphology(morphologyCode)
                        && tumor.ResolveUnspecifiedSiteCode(morphologyCode) is { } unspecified)
                    {
                        families =
                        [
                            (unspecified.Code, $"形态学编码 {morphologyCode} 不含部位信息，"
                                + $"按动态码 {unspecified.BehaviorDigit}「{unspecified.ColumnName}」"
                                + $"取肿瘤索引表未特指部位码: {unspecified.DisplayPath}")
                        ];
                    }
                }

                if (families.Count > 0)
                {
                    var items = families
                        .SelectMany(family => ExpandSiteFamily(
                            family.Prefix, family.Note, morph, dictionary,
                            inputText, inputKey, pathKey, parsed.Kind, inAlternatives))
                        .ToList();
                    if (items.Count > 0)
                    {
                        // 无部位形态学码的 5 位详码（M80000/3 恶性肿瘤）与它查表得到的部位码同义不同写，
                        // 不能再占交付席位；有部位信息的形态学码（M8460/0 浆液性乳头状囊腺瘤）仍作粒度补充附上。
                        return IsSitelessMorphology(morph)
                            ? items
                            : MergeMorphologyDetailed(
                                items, morph, dictionary, inputText, inputKey, pathKey, tumor);
                    }
                }

                // 无部位可展开：裸形态学码本身不进推荐（FallbackItems 丢弃），
                // 但字典里的 5 位详码（M85100/3 甲状腺髓样癌C73）仍按门控给形态学候选。
                var morphologyOnly = ExpandMorphologyDetailed(
                    parsed.MorphologyCode, dictionary, inputText, inputKey, pathKey);
                return morphologyOnly.Count > 0
                    ? morphologyOnly
                    : FallbackItems(parsed, dictionary, inputText, inputKey, pathKey,
                        fallbackCodeSystem, titleReverseEnabled);
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

                return pair.Count > 0 ? pair : FallbackItems(parsed, dictionary, inputText, inputKey, pathKey,
                    fallbackCodeSystem, titleReverseEnabled);
            }

            case IndexCodeFormatKind.CodeRange:
            {
                var prefixes = ParsedIndexCode.RangePrefixes(
                    parsed.RangeStart ?? string.Empty, parsed.RangeEnd ?? string.Empty);
                if (prefixes.Count == 0)
                {
                    return FallbackItems(parsed, dictionary, inputText, inputKey, pathKey,
                        fallbackCodeSystem, titleReverseEnabled);
                }

                var rows = prefixes
                    .SelectMany(prefix => dictionary.Prefix(prefix))
                    .ToList();
                var items = RankRows(rows, inputText, inputKey, pathKey, parsed.Kind,
                    inAlternatives, note: null);
                return items.Count > 0 ? items : FallbackItems(parsed, dictionary, inputText, inputKey, pathKey,
                    fallbackCodeSystem, titleReverseEnabled);
            }

            case IndexCodeFormatKind.CategoryUnspecified:
            case IndexCodeFormatKind.Single:
            case IndexCodeFormatKind.Empty:
            default:
            {
                var prefix = (parsed.Prefix ?? parsed.Raw).ToUpperInvariant().TrimEnd('*');
                if (prefix.Length < 3)
                {
                    return FallbackItems(parsed, dictionary, inputText, inputKey, pathKey,
                        fallbackCodeSystem, titleReverseEnabled);
                }

                var items = ExpandByPrefix(prefix, dictionary, inputText, inputKey, pathKey,
                    parsed.Kind, excludeJoint: false, inAlternatives, note: null);
                return items.Count > 0 ? items : FallbackItems(parsed, dictionary, inputText, inputKey, pathKey,
                    fallbackCodeSystem, titleReverseEnabled);
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
        bool inAlternatives,
        TumorIndexSnapshot? tumor = null,
        bool titleReverseEnabled = true)
    {
        // 触发码 = 本行主码；索引链路已完成语义验证，字典展不开时按索引码原样推荐。
        var triggerItems = parsed.Trigger is null
            ? []
            : ExpandCore(parsed.Trigger, dictionary, inputText, inputKey, pathKey,
                fallbackCodeSystem, inAlternatives, tumor, titleReverseEnabled);
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
                fallbackCodeSystem, inAlternatives, tumor, titleReverseEnabled);
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
        string fallbackCodeSystem,
        TumorIndexSnapshot? tumor = null,
        bool titleReverseEnabled = true)
    {
        var alternatives = parsed.Alternatives ?? [];
        var scored = alternatives
            .Select(alt =>
            {
                // 替代组是索引自己列出的分型/分支菜单，选哪一支由字典展开裁决；
                // 标题反查只补"单码展不开"，若在替代组内也生效，E14「未特指」这类
                // 别名词面会被翻成非回退候选、顶掉 E11「2型」的正式展开
                // （万条实测糖尿病整族 10 名 E11.x→E14.x）。所以组内一律不反查，
                // 整组全落空后的组级回退仍按开关执行。
                var items = ExpandCore(alt, dictionary, inputText, inputKey, pathKey,
                    fallbackCodeSystem, inAlternatives: true, tumor: tumor,
                    titleReverseEnabled: false);
                return (Alt: alt, Items: items, Best: items.Count > 0 ? items.Max(item => item.Score) : 0m);
            })
            .Where(entry => !entry.Items.Any(item => item.IsIndexFallback))
            .OrderByDescending(entry => entry.Best)
            .ToList();

        if (scored.Count == 0)
        {
            return FallbackItems(parsed, dictionary, inputText, inputKey, pathKey,
                fallbackCodeSystem, titleReverseEnabled);
        }

        // 病历信息不足以区分替代编码（并列/全部低分）→ 回退主导词未特指码，禁止全量输出。
        var top = scored[0];
        var second = scored.Count > 1 ? scored[1].Best : 0m;
        if (top.Best <= 0m || top.Best - second < 0.15m)
        {
            return FallbackItems(parsed, dictionary, inputText, inputKey, pathKey,
                fallbackCodeSystem, titleReverseEnabled);
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

    /// <summary>
    /// 裸形态学码（M8510/3）→ 字典 5 位详码（M85100/3 甲状腺髓样癌）候选。
    /// 索引写 4 位（ICD-O 3 版 morphology 主码补 0 成 5 位即字典形态学段前缀），
    /// 按行为位过滤同族码（M8510 家族含 /0 良性与 /3 恶性，行为位错了就是另一个诊断）。
    /// 常规门控（≥0.6）在这里过严：病历写 "甲状腺C细胞癌"，字典标题是 "髓样癌"，
    /// 字面不沾但索引已判定同义 —— 只要标题与输入有 ≥2 字连续共同段即候选，
    /// 打分取共同段占比，让 "髓样癌"（部位词 甲状腺 也被输入含住）赢过泛标题。
    /// </summary>
    private static IReadOnlyList<ExpandedItem> ExpandMorphologyDetailed(
        string? morphologyCode,
        IcdDictionarySnapshot dictionary,
        string inputText,
        string inputKey,
        string pathKey)
    {
        if (morphologyCode is not { Length: >= 6 } code || code.IndexOf('/') < 0)
        {
            return [];
        }

        var slash = code.IndexOf('/');
        var digit = code[(slash + 1)..];
        if (digit.Length != 1)
        {
            return [];
        }

        // 索引 4 位形态学段（M8510）→ 字典 5 位段前缀（M85100）：补一个 0 再连行为位过滤。
        var prefix = $"{code[..slash]}0".ToUpperInvariant();
        var rows = dictionary.Prefix(prefix)
            .Where(row => row.Code.EndsWith("/" + digit, StringComparison.Ordinal)
                && row.Code.Length >= 8)
            .ToList();

        var items = new List<ExpandedItem>();
        foreach (var row in rows)
        {
            var titleKey = TextMatch.NormalizeKey(row.Title);
            if (titleKey.Length < 2 || inputKey.Length == 0)
            {
                continue;
            }

            var cover = TextMatch.CoverageOf(inputKey, titleKey);
            if (cover.MaxSegment < 2)
            {
                continue;
            }

            items.Add(new ExpandedItem(
                row.CodeSystemCode, row.Code, row.Title,
                // 上限 0.68：形态学详码是"再加一码"的补充信息，部位码才是诊断本身，
                // 不能让 M80900/3 这种详码压过肿瘤表查出的 C44.9 当 Top1。
                Math.Min(0.68m, 0.55m + cover.Ratio * 0.45m),
                IndexCodeFormatKind.Morphology,
                IsIndexFallback: false, IsConditionalAddOn: false,
                Note: $"肿瘤形态学详码（索引 {code}）",
                TitleSpecificity: TextMatch.CoverageOf(titleKey, inputKey).Ratio));
        }

        _ = pathKey;
        _ = inputText;
        return Rank(items).Take(3).ToList();
    }

    /// <summary>部位码来源证据：肿瘤索引表的哪一列、哪条路径解出的这个部位。</summary>
    private static string SiteNote(TumorSiteMatch match, string morphologyCode)
        => $"形态学编码 {morphologyCode} 按动态码 {match.BehaviorDigit}「{match.ColumnName}」"
            + $"在肿瘤索引查得部位: {match.DisplayPath}";

    // 无部位语义的形态学码根：M8000 肿瘤、M8010 癌（上皮性肿瘤）——它们只说"是个肿瘤"，
    // 补零成 5 位（M80000/3）也还是没有部位，编码口径上必须回肿瘤索引表拿"未特指部位"码或部位码。
    private static readonly string[] SitelessMorphologyRoots = ["M8000", "M8010"];

    /// <summary>形态学码（M8000/3、M80100/2）是否属于"只说肿瘤、不说部位"的泛码族。
    /// 诊断侧的覆盖度记账也按这条判据（见 <c>IcdIndexDiagnosisResolver.TumorSiteAliasFaces</c>）：
    /// 只有泛码链路的部位信息完全来自肿瘤表，特异形态码（M9160/0 骨内血管瘤）自带疾病身份，
    /// 输入里的部位词不是它的证据。</summary>
    public static bool IsSitelessMorphology(string? morphologyCode)
    {
        var slash = morphologyCode?.IndexOf('/') ?? -1;
        if (morphologyCode is not { Length: >= 6 } || slash < 0)
        {
            return false;
        }

        // 索引写 4 位（M8000/3），字典详码是补零的 5 位（M80000/3）：两种写法都算泛码。
        var root = morphologyCode[..slash];
        return SitelessMorphologyRoots.Any(candidate =>
            root.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)
            && (root.Length == candidate.Length
                || (root.Length == candidate.Length + 1 && root[^1] == '0')));
    }

    /// <summary>
    /// 一个肿瘤部位码族 → 候选：先按常规门控展开该前缀；字典门控全挡（输入还写着术式/化疗等修饰词）
    /// 时取该前缀最泛码——部位词是索引链路或肿瘤表给的，语义已验证，形态学详码不是诊断。
    /// 并列族逐族走同一条口径，否则第二族展不开会把整批部位候选一起丢掉（腹壁及腹腔转移癌 退回 M80100/6）。
    /// </summary>
    private static IReadOnlyList<ExpandedItem> ExpandSiteFamily(
        string site,
        string? siteNote,
        string? morphologyCode,
        IcdDictionarySnapshot dictionary,
        string inputText,
        string inputKey,
        string pathKey,
        IndexCodeFormatKind format,
        bool inAlternatives)
    {
        var prefix = site.ToUpperInvariant();
        var noteHead = siteNote is null
            ? $"形态学编码 {morphologyCode}"
            : $"{siteNote}（形态学编码 {morphologyCode}）";
        var items = ExpandByPrefix(
            prefix, dictionary, inputText, inputKey, pathKey, format,
            excludeJoint: false, inAlternatives, note: noteHead);
        if (items.Count > 0)
        {
            return items;
        }

        var relaxed = dictionary.Prefix(prefix)
            .Where(row => !row.Code.StartsWith('M') && !row.Code.Contains('+', StringComparison.Ordinal))
            .OrderBy(row => row.Code.Length)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .FirstOrDefault();
        return relaxed is null
            ? []
            :
            [
                new ExpandedItem(
                    relaxed.CodeSystemCode, relaxed.Code, relaxed.Title,
                    0.72m, format,
                    IsIndexFallback: false,
                    IsConditionalAddOn: false,
                    Note: noteHead + "；字典门控未命中，取部位前缀最泛码",
                    TitleSpecificity: TextMatch.CoverageOf(
                        TextMatch.NormalizeKey(relaxed.Title), inputKey).Ratio),
            ];
    }

    /// <summary>部位码展开与形态学详码候选合并（部位候选在前，形态学详码补充粒度选项）。</summary>
    private static IReadOnlyList<ExpandedItem> MergeMorphologyDetailed(
        IReadOnlyList<ExpandedItem> siteItems,
        string? morphologyCode,
        IcdDictionarySnapshot dictionary,
        string inputText,
        string inputKey,
        string pathKey,
        TumorIndexSnapshot? tumor)
    {
        if (tumor is null || morphologyCode is not { Length: > 0 })
        {
            return siteItems;
        }

        var detailed = ExpandMorphologyDetailed(morphologyCode, dictionary, inputText, inputKey, pathKey);
        var seen = siteItems.Select(item => item.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extra = detailed.Where(item => seen.Add(item.Code)).ToList();
        return extra.Count == 0 ? siteItems : [.. siteItems, .. extra];
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
    /// 同族标题反查开启时，取最泛码之前先在同一个前缀族里按双向逐字包含找证据
    /// （口径同 CM3 <c>ReverseTitleRows</c>）；反查不中仍走原有回退——诊断侧回退是
    /// 字典精确码或语义已验证的索引码，与 CM3 的"纯最泛码"不是同一资产，不整段删除。
    /// </summary>
    private static IReadOnlyList<ExpandedItem> FallbackItems(
        ParsedIndexCode parsed,
        IcdDictionarySnapshot dictionary,
        string inputText,
        string inputKey,
        string pathKey,
        string fallbackCodeSystem,
        bool titleReverseEnabled)
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
            if (exact is not null)
            {
                // 索引码本身就在字典里：这不是"猜一个最泛码"，是精确码，保留原口径。
                items.Add(new ExpandedItem(
                    fallbackCodeSystem,
                    exact.Code,
                    exact.Title,
                    FallbackScore,
                    parsed.Kind,
                    IsIndexFallback: true,
                    IsConditionalAddOn: false,
                    Note: $"索引编码 {parsed.Raw} 在字典中无展开结果，回退推荐同前缀码"));
                continue;
            }

            if (titleReverseEnabled)
            {
                var reversed = ReverseTitleRows(rows, inputKey, pathKey, parsed.Kind);
                if (reversed.Count > 0)
                {
                    items.AddRange(reversed);
                    continue;
                }
            }

            var chosen = rows
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

    /// <summary>
    /// 门控全落空时的同族标题反查（移植 CM3 口径）：标题被输入逐字完整包含，
    /// 或输入字符 90% 以上被标题覆盖。三道硬门（糖尿病分型/否定极/部位冲突）与
    /// <see cref="RankRows"/> 完全一致——门控刻意挡掉的行不会被反查放回来。
    /// 反查只做"补证据"，调用方在无命中时仍走原回退。
    /// </summary>
    private static IReadOnlyList<ExpandedItem> ReverseTitleRows(
        IReadOnlyList<IcdDictionaryRow> rows,
        string inputKey,
        string pathKey,
        IndexCodeFormatKind format)
    {
        if (rows.Count == 0 || inputKey.Length == 0)
        {
            return [];
        }

        var results = new List<ExpandedItem>();
        foreach (var row in rows)
        {
            if (TextMatch.IsUnjustifiedDiabetesCompound(row.Title, inputKey)
                || TextMatch.HasDiabetesTypeConflict(row.Title, row.Code, inputKey, resolveTypeFromCategoryAndDefault: false)
                || TextMatch.HasNegationPoleConflict(row.Title, inputKey)
                || TextMatch.HasBodyPartConflict(row.Title, inputKey))
            {
                continue;
            }

            var titleKey = TextMatch.NormalizeKey(row.Title);
            var searchTextKey = TextMatch.NormalizeKey(row.SearchText);
            var titleInInput = TextMatch.CoverageOf(titleKey, inputKey);
            var inputInTitle = Best(
                TextMatch.CoverageOf(inputKey, titleKey),
                TextMatch.CoverageOf(inputKey, searchTextKey));
            var pathCover = Best(
                Best(
                    TextMatch.CoverageOf(pathKey, titleKey),
                    TextMatch.CoverageOf(pathKey, titleKey, countSingleCharSegments: true)),
                TextMatch.CoverageOf(TextMatch.Reverse(pathKey), titleKey, countSingleCharSegments: true));

            var admitted = (titleKey.Length >= 2 && titleInInput.Ratio >= 1.0m)
                || (inputKey.Length >= 3 && inputInTitle.Ratio >= 0.9m);
            if (!admitted)
            {
                continue;
            }

            var score = Math.Max(inputInTitle.Ratio, pathCover.Ratio * 0.9m);
            var wholeTitleHit = titleKey.Length > 0
                && inputKey.Contains(titleKey, StringComparison.Ordinal)
                && inputInTitle.Ratio >= WholeTitleMinInputRatio;
            if (wholeTitleHit)
            {
                score = 1.0m;
            }

            results.Add(new ExpandedItem(
                row.CodeSystemCode,
                row.Code,
                row.Title,
                score,
                format,
                IsIndexFallback: false,
                IsConditionalAddOn: false,
                Note: "门控全落空，按同族标题反查命中",
                TitleSpecificity: titleInInput.Ratio,
                WholeTitleDepth: wholeTitleHit
                    ? Math.Min(1m, titleKey.Length / (decimal)inputKey.Length)
                    : 0m));
        }

        var ranked = Rank(results)
            .Take(10)
            .ToList();
        for (var i = 0; i < ranked.Count; i++)
        {
            ranked[i] = ranked[i] with { FamilyRank = i };
        }

        return ranked;
    }
}
