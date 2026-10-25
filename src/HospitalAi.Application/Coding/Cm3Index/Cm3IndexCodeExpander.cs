using System.Text.RegularExpressions;
using HospitalAi.Application.Coding.IcdIndex;
using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Coding.Cm3Index;

/// <summary>
/// 手术索引码的展开产物。字段口径对齐诊断侧 <c>ExpandedItem</c>，
/// 但不带 <c>IndexCodeFormatKind</c>——CM3 码形只有 <c>NN.N</c> 与 <c>NN.NN</c> 两种，
/// 没有星剑码、形态学码、区间码与替代码（实测 §2.4），复用那个枚举等于给它加一个恒为 Single 的维度。
/// </summary>
public sealed record Cm3ExpandedItem(
    string CodeSystemCode,
    string Code,
    string Title,
    decimal Score,
    bool IsIndexFallback,
    string? Note,
    decimal TitleSpecificity = 0m,
    decimal WholeTitleDepth = 0m,
    /// <summary>同族（同一索引码展开批次）内精排名次，0 = 族内最优。诊断侧靠它抗重排噪声。</summary>
    int FamilyRank = 0);

/// <summary>
/// 索引编码 → 手术字典细码展开器（方案 §6.5）。
/// 诊断侧的 见亚目 改道在这里落地：那 106 行的目标亚目码已写在 index_code 上，
/// 前缀展开天然走到 <c>NN.N</c> 细码族，不需要额外的编码改道机制。
/// </summary>
public static class Cm3IndexCodeExpander
{
    /// <summary>单个索引码最多展开的细码数：86.04 这类扇出 154 的前缀必须被这条挡住。</summary>
    public const int MaxExpandedCodes = 3;

    private const decimal GateRatio = 0.6m;
    private const int GateMaxSegment = 3;

    /// <summary>整标题字面命中要抬到满分，标题至少解释掉输入的这些字符（上位码防抬）。</summary>
    private const decimal WholeTitleMinInputRatio = 0.75m;

    /// <summary>CM3 索引码的合法形态：<c>NN.N</c>、<c>NN.NN</c>，以及词面残留的 <c>NN.NNN</c>（seq 00.162 一例）。</summary>
    private static readonly Regex Cm3CodeRegex = new(@"^\d{2}\.\d{1,3}$", RegexOptions.Compiled);

    public static IReadOnlyList<Cm3ExpandedItem> Expand(
        string? indexCode,
        IcdDictionarySnapshot dictionary,
        string inputText,
        string pathKey,
        int maxResults = MaxExpandedCodes)
    {
        var inputKey = TextMatch.StripUncertaintyPhrases(inputText);
        if (inputKey.Length == 0)
        {
            inputKey = TextMatch.NormalizeKey(inputText);
        }

        var results = ExpandCore(indexCode, dictionary, inputKey, pathKey);
        if (results.Count == 0)
        {
            return [];
        }

        // 先按编码收敛再统一排序：同码可能由多个索引行展开出来，让字典行序决定谁占名额是随机性。
        return Rank(results
                .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => Rank(group).First()))
            .Take(Math.Max(1, Math.Min(maxResults, MaxExpandedCodes)))
            .ToList();
    }

    private static IReadOnlyList<Cm3ExpandedItem> ExpandCore(
        string? indexCode,
        IcdDictionarySnapshot dictionary,
        string inputKey,
        string pathKey)
    {
        var code = (indexCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!Cm3CodeRegex.IsMatch(code))
        {
            return [];
        }

        // 前缀 = 索引码字面（"按字面长度"就是这条：5 位的 NN.NN 取 5、4 位的 NN.N 取 4，
        // 字典侧的 X 占位详码 43.6X00 / 43.6X00X005 都落在 43.6 前缀下，固定 5 位前缀会整族漏掉）。
        var rows = dictionary.Prefix(code);
        var items = RankRows(rows, inputKey, pathKey);
        return items.Count > 0 ? items : ReverseTitleRows(rows, inputKey, pathKey);
    }

    private static IReadOnlyList<Cm3ExpandedItem> RankRows(
        IReadOnlyList<IcdDictionaryRow> rows,
        string inputKey,
        string pathKey)
    {
        if (rows.Count == 0 || inputKey.Length == 0)
        {
            return [];
        }

        var results = new List<Cm3ExpandedItem>();
        var inputQualifiers = Cm3SiteQualifiers.Extract(inputKey);
        foreach (var row in rows)
        {
            // 诊断侧的糖尿病复合码判据（IsUnjustifiedDiabetesCompound / HasDiabetesTypeConflict）
            // 不搬：那是 ICD-10 E10/E11 分型专门项，CM3 标题里没有分型语义。
            if (TextMatch.HasNegationPoleConflict(row.Title, inputKey)
                || Cm3SiteQualifiers.Conflicts(row.Title, inputQualifiers)
                || TextMatch.HasPhasePoleConflict(row.Title, inputKey))
            {
                continue;
            }

            var titleKey = TextMatch.NormalizeKey(row.Title);
            var searchTextKey = TextMatch.NormalizeKey(row.SearchText);
            var titleCover = TextMatch.CoverageOf(inputKey, titleKey);
            var inputCover = Best(titleCover, TextMatch.CoverageOf(inputKey, searchTextKey));
            // 命中路径按索引顺序拼接（"修补术鼻伴结膜瓣"），字典标题是自然语序，
            // 正序/逆序镜像 + 单字包含口径取较大覆盖，做"路径词被标题完整覆盖"的放行判定。
            var pathCover = Best(
                Best(
                    TextMatch.CoverageOf(pathKey, titleKey),
                    TextMatch.CoverageOf(pathKey, titleKey, countSingleCharSegments: true)),
                TextMatch.CoverageOf(TextMatch.Reverse(pathKey), titleKey, countSingleCharSegments: true));
            var maxSegment = Math.Max(inputCover.MaxSegment, pathCover.MaxSegment);

            var inputPass = inputCover.Ratio >= GateRatio
                && (inputKey.Length < 3 || maxSegment >= GateMaxSegment || inputCover.Ratio >= 1.0m);
            if (!inputPass && pathCover.Ratio < 1.0m)
            {
                continue;
            }

            // 路径通道按 0.9 参与打分：口径同诊断侧——一层链路的 pathKey 就是主导词本身，
            // 标题含主导词即完整覆盖（索引书共现），无条件抬满分会让上位术式顶掉具体术式。
            var score = Math.Max(inputCover.Ratio, pathCover.Ratio * 0.9m);

            // 标题被输入逐字连续包含 = 编码员认的"就是这一条"。手术名恰恰是
            // "部位+入路+术式"的整串写法，胆囊切除术 逐字命中 51.2300 标题是强证据。
            // 诊断侧的 性 后缀剥离分支不搬：CM3 标题以 术 结尾，实测无 性 派生形态，
            // 写一个没有对象的分支只会成为下一个人在这里改坏的死代码（纪律 5）。
            var wholeTitleHit = titleKey.Length > 0
                && inputKey.Contains(titleKey, StringComparison.Ordinal)
                && inputCover.Ratio >= WholeTitleMinInputRatio;
            if (wholeTitleHit)
            {
                score = 1.0m;
            }

            results.Add(new Cm3ExpandedItem(
                row.CodeSystemCode,
                row.Code,
                row.Title,
                score,
                IsIndexFallback: false,
                Note: null,
                TitleSpecificity: TextMatch.CoverageOf(titleKey, inputKey).Ratio,
                WholeTitleDepth: wholeTitleHit
                    ? Math.Min(1m, titleKey.Length / (decimal)inputKey.Length)
                    : 0m));
        }

        // 括号限定词纪律：族内存在"限定词全部被输入满足"的兄弟行时剔除未满足者
        // （手术侧形态是 "胆囊部分切除术(伴结石)" 这类），整族都不满足时全部保留防整族丢失。
        var satisfied = results.Where(item => BracketQualifiersSatisfied(item.Title, inputKey)).ToList();
        var ranked = Rank(satisfied.Count > 0 ? satisfied : results)
            .Take(MaxExpandedCodes)
            .ToList();
        for (var i = 0; i < ranked.Count; i++)
        {
            ranked[i] = ranked[i] with { FamilyRank = i };
        }

        return ranked;
    }

    /// <summary>
    /// 门控全落空时的同族标题反查，取代原"回退同前缀最泛码"：
    /// 两面真值实测回退码 Top1 命中 0/1555，最泛码是纯负资产；改为只认硬词面证据——
    /// 标题被输入逐字完整包含（标题短于输入、输入带前缀修饰语），或输入字符 90% 以上被标题覆盖
    /// （语序倒装/插字被 MaxSegment≥3 门槛打散的写法）。三道硬门（否定极/部位冲突/相位极）原样保留，
    /// 门控刻意挡掉的行不会被反查放回来。反查仍无命中即返回空——错分支链路（如 周围血管的血管内显像
    /// 落进 38.5x 族）宁空勿错，由知识库覆盖台账接手。
    /// </summary>
    private static IReadOnlyList<Cm3ExpandedItem> ReverseTitleRows(
        IReadOnlyList<IcdDictionaryRow> rows,
        string inputKey,
        string pathKey)
    {
        if (rows.Count == 0 || inputKey.Length == 0)
        {
            return [];
        }

        var results = new List<Cm3ExpandedItem>();
        var inputQualifiers = Cm3SiteQualifiers.Extract(inputKey);
        foreach (var row in rows)
        {
            if (TextMatch.HasNegationPoleConflict(row.Title, inputKey)
                || Cm3SiteQualifiers.Conflicts(row.Title, inputQualifiers)
                || TextMatch.HasPhasePoleConflict(row.Title, inputKey))
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

            results.Add(new Cm3ExpandedItem(
                row.CodeSystemCode,
                row.Code,
                row.Title,
                score,
                IsIndexFallback: false,
                Note: "门控全落空，按同族标题反查命中",
                TitleSpecificity: titleInInput.Ratio,
                WholeTitleDepth: wholeTitleHit
                    ? Math.Min(1m, titleKey.Length / (decimal)inputKey.Length)
                    : 0m));
        }

        var ranked = Rank(results)
            .Take(MaxExpandedCodes)
            .ToList();
        for (var i = 0; i < ranked.Count; i++)
        {
            ranked[i] = ranked[i] with { FamilyRank = i };
        }

        return ranked;
    }

    private static TextMatch.Coverage Best(TextMatch.Coverage left, TextMatch.Coverage right)
        => left.Ratio >= right.Ratio ? left : right;

    private static readonly Regex BracketQualifierRegex = new("[（(]([^（()）]*)[）)]", RegexOptions.Compiled);

    /// <summary>标题全部圆括号限定词都出现在输入中（无括号视为满足）。</summary>
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
    /// 排序口径同诊断侧：分数 → 整标题贴合度 → 标题反向覆盖 → 编码长度。
    /// 反向覆盖挡住只靠 search_text 聚合上位类的编码压过与输入逐字相等的细码。
    /// </summary>
    private static IOrderedEnumerable<Cm3ExpandedItem> Rank(IEnumerable<Cm3ExpandedItem> items) => items
        .OrderByDescending(item => item.Score)
        .ThenByDescending(item => item.WholeTitleDepth)
        .ThenByDescending(item => item.TitleSpecificity)
        .ThenBy(item => item.Code.Length);
}
