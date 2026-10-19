using System.Text.RegularExpressions;
using HospitalAi.Application.Coding.IcdIndex;
using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Coding.Cm3Index;

/// <summary>
/// 手术输入的两段式产物。字段口径对齐诊断侧 <c>DiagnosisIndexRecommendation</c>，
/// 少了 <c>IsConditionalAddOn</c>：CM3 的方括号双码按方案 §6.5-3 只解析不出码（Q3 待拍板）。
/// </summary>
public sealed record ProcedureIndexRecommendation(
    string CodeSystemCode,
    string Code,
    string Title,
    decimal Score,
    bool IsIndexFallback,
    string EvidencePath,
    string? Note,
    /// <summary>整标题逐字贴合度（标题长/输入长），供管道复排时保住"逐字完整标题优先"口径。</summary>
    decimal WholeTitleDepth = 0m,
    /// <summary>同族（同一索引码展开批次）内的精排名次，供下游抗重排噪声。</summary>
    int FamilyRank = 0,
    /// <summary>产出本条的索引编码（同族粒度菜单的共同身份）。名额分配按族计，不按键计。</summary>
    string? IndexFamilyCode = null);

/// <summary>
/// 手术名称的两段式解析：主导词召回 → 该主导词子树内逐层下钻 → 索引码前缀展开。
/// 与诊断侧同一套决策：主导词全落空即返回空，不回退字典直匹配（字典通道本来就在流水线里，
/// 索引通道的产出只与它平权入池，不构成兜底）；索引命中但字典展不开时回退同前缀最泛码。
/// </summary>
public sealed class Cm3ProcedureIndexResolver(int leadWordTopK, int maxResults)
{
    /// <summary>
    /// 医生把院内码直接写在手术名后面（"针刺（99.9200）"）。抽取通道里 <c>ClinicalFactExtractor</c>
    /// 已按同一口径剥过，但手工录入与首页路径没走那里；数字留在词面会稀释覆盖率。
    /// 只剥结尾括号里的编码形态，避免误删 "胆囊切除术（伴粘连）" 这类有语义的限定语。
    /// </summary>
    private static readonly Regex TrailingCodeParenthesis = new(
        @"[\(（]\s*[A-Za-z]?\d[\d.A-Za-z]*\s*[\)）]\s*$",
        RegexOptions.Compiled);

    /// <summary>同一索引编码（同族粒度菜单）在最终名额里最多占的席位数。</summary>
    /// <remarks>
    /// CM3 必须一开始就限族：扇出均值 7.2、最大 154（一个 86.04 就能吃掉整条推荐），
    /// 诊断侧这条是纪律 9 点名未解决、留到线上才补的坑。
    /// </remarks>
    private const int MaxSeatsPerIndexFamily = 1;

    /// <summary>链路字符重叠下限：低于此值视为无关链路（口径同诊断侧起步值）。public 供离线工具复用同一判据。</summary>
    public const decimal MinPathOverlap = 0.30m;

    public IReadOnlyList<ProcedureIndexRecommendation> Resolve(
        Cm3IndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        string inputText,
        IcdTermSynonymSnapshot? synonyms = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeInput(inputText);
        if (normalized.Length == 0)
        {
            return [];
        }

        var texts = new List<string> { normalized };
        if (synonyms is not null)
        {
            // 术语同义词表有 PROCEDURE 类型（乳腺→乳房 那类等价边）：只在输入侧改写，不动索引树。
            texts.AddRange(synonyms
                .ExpandVariants(normalized, TermSynonymTypes.Procedure)
                .Where(text => text.Length > 0 && !texts.Contains(text)));
        }

        var ranked = new List<(decimal RawScore, ProcedureIndexRecommendation Recommendation)>();
        // 另见-1 提示候选：只在常规链路名额没占满时补位，不参与 Top1 竞争（末位让位实测被否决）。
        var hints = new List<(decimal RawScore, ProcedureIndexRecommendation Recommendation)>();

        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveText(snapshot, dictionary, text, ranked, hints);
        }

        var results = Order(ranked).ToList();
        var taken = results.Select(item => item.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var hint in Order(hints))
        {
            if (results.Count < maxResults && taken.Add(hint.Code))
            {
                results.Add(hint);
            }
        }

        return results;
    }

    private void ResolveText(
        Cm3IndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        string inputText,
        List<(decimal RawScore, ProcedureIndexRecommendation Recommendation)> ranked,
        List<(decimal RawScore, ProcedureIndexRecommendation Recommendation)> hints)
    {
        var inputKey = TextMatch.NormalizeKey(inputText);
        if (inputKey.Length < 2)
        {
            // 单字/双字输入只有与主导词别名逐字相等时才锚定：覆盖率门控天然挡住"沾两个字边"，
            // 但 疝 / 痣 这类单字手术名与主导词 1:1 相等不属于误召回。
            var exactHit = FindExactLeadHit(snapshot, inputKey);
            if (exactHit is not null)
            {
                Collect(snapshot, dictionary, inputText,
                    Cm3ProcedureIndexMatcher.Drill(snapshot, exactHit, inputKey),
                    exactHit.Alias.Weight, ranked, hints);
            }

            return;
        }

        var leadHits = Cm3ProcedureIndexMatcher.RecallLeads(snapshot, inputKey, leadWordTopK);

        // 失效主导词 "X ―见 Y"：常规召回全空时按指针目标文本重试一次。
        if (leadHits.Count == 0
            && Cm3ProcedureIndexMatcher.ResolveRedirect(snapshot, inputKey) is { } redirect)
        {
            leadHits = Cm3ProcedureIndexMatcher.RecallLeads(
                snapshot, TextMatch.NormalizeKey(redirect), leadWordTopK);
        }

        foreach (var hit in leadHits)
        {
            Collect(snapshot, dictionary, inputText,
                Cm3ProcedureIndexMatcher.Drill(snapshot, hit, inputKey),
                hit.Alias.Weight, ranked, hints);
        }

        // 见-1 的 level-0 形态（"阿贝手术 ―见 Abbe 手术"）：这些行不是主导词，编码员口径是
        // "见 + 具体主导词必须转换"。输入含住这个失效词就把目标链路平权放进常规池一起比分，
        // 不加权、不压制（纪律 3）；两字失效词只在输入本身就短（≤4 字）时才够格改道，
        // 否则它是术名的构成成分而不是被判失效的主导词。
        foreach (var (key, target) in snapshot.Redirects)
        {
            if (key.Length < 2
                || Cm3ProcedureIndexMatcher.IsGenericLeadKey(key)
                || (key.Length < 3 && inputKey.Length > 4)
                || !inputKey.Contains(key, StringComparison.Ordinal))
            {
                continue;
            }

            if (Cm3ProcedureIndexMatcher.ResolveJumpLead(snapshot, target) is not { } redirectLead
                || leadHits.Any(hit => hit.Node.SeqNo == redirectLead.SeqNo))
            {
                continue;
            }

            var seed = redirectLead.Aliases.FirstOrDefault(alias => alias.MatchKey.Length >= 2
                    && inputKey.Contains(alias.MatchKey, StringComparison.Ordinal))
                ?? redirectLead.Aliases.FirstOrDefault();
            if (seed is null)
            {
                continue;
            }

            Collect(
                snapshot, dictionary, inputText,
                Cm3ProcedureIndexMatcher.Drill(
                    snapshot,
                    new Cm3LeadWordHit(redirectLead, seed, 0m, 0),
                    inputKey,
                    jumpTrace: $"{key} → {redirectLead.Term}"),
                1m,
                ranked,
                hints);
        }
    }

    private void Collect(
        Cm3IndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        string inputText,
        Cm3DrillResult? drill,
        decimal leadBoost,
        List<(decimal RawScore, ProcedureIndexRecommendation Recommendation)> ranked,
        List<(decimal RawScore, ProcedureIndexRecommendation Recommendation)> hints)
    {
        var produced = CollectOnce(dictionary, inputText, drill, leadBoost, ranked);

        // 见-1 强制改道让位给本地祖先兜底链路：规则要求的是"优先"，不是"改道展不开就没推荐"。
        if (!produced && drill?.Displaced is { } displaced)
        {
            CollectOnce(dictionary, inputText, displaced, leadBoost, ranked);
        }

        if (drill is { SeeAlsoHints.Count: > 0 })
        {
            foreach (var hint in drill.SeeAlsoHints)
            {
                CollectOnce(dictionary, inputText, hint, leadBoost, hints,
                    $"本行 {drill.CodeNode.Code} 为直接采用码，更详细情况见");
            }
        }
    }

    private bool CollectOnce(
        IcdDictionarySnapshot dictionary,
        string inputText,
        Cm3DrillResult? drill,
        decimal leadBoost,
        List<(decimal RawScore, ProcedureIndexRecommendation Recommendation)> ranked,
        string? hintNote = null)
    {
        if (drill?.CodeNode.Code is not { Length: > 0 } indexCode)
        {
            return false;
        }

        var rankedBefore = ranked.Count;
        var pathDisplay = string.Join(" → ", drill.Path.Select(node => node.Term));
        if (drill.Jumps.Count > 0)
        {
            pathDisplay += " [跳转: " + string.Join("; ", drill.Jumps) + "]";
        }

        var expanded = Cm3IndexCodeExpander.Expand(indexCode, dictionary, inputText, drill.PathKey, maxResults);
        if (expanded.Count == 0)
        {
            return false;
        }

        // 折叠掉的连接行（――伴 / ――与）不是输入真写出来的证据层：既不挣链路深度加成，
        // 也不挣覆盖度评分，否则"透明化"本身就成了给这类链路加权（纪律 3）。
        // 30% 重叠门仍按完整链路算：透明化不该让任何既有链路新掉进门控（打空硬线）。
        var evidencePath = drill.Path.Where(node => !Cm3ProcedureIndexMatcher.IsConnectorNode(node)).ToList();
        var evidenceOverlap = evidencePath.Count == drill.Path.Count
            ? PathOverlapRatio(drill.Path, inputText)
            : PathOverlapRatio(evidencePath, inputText);
        var depthBoost = 1m + 0.05m * evidencePath.Count;
        if (PathOverlapRatio(drill.Path, inputText) < MinPathOverlap)
        {
            return false;
        }

        var addOnNote = drill.CodeNode.AddOnCode is { Length: > 0 } addOnCode
            ? $"本行词面另带附加码 {addOnCode}（主[附] 双码语义待拍板，暂不展开）"
            : null;

        foreach (var item in expanded)
        {
            // 0.8 只打在"没拿到本行真码"的两种情况上：字典展不开（回退同前缀最泛码）与
            // 索引链路的祖先兜底。带码死路主导词（本库 1391 条，命中即终点）系数取 1.0：
            // 它给的就是索引指定的码，"没下钻"不是弱证据（方案 §6.3）。
            var penalty = item.IsIndexFallback || drill.CodeFromAncestorFallback ? 0.8m : 1m;
            var rawScore = item.Score * leadBoost * depthBoost * (0.5m + evidenceOverlap) * penalty;
            var note = addOnNote is null
                ? (hintNote is null ? item.Note : $"{hintNote} {pathDisplay} {indexCode}")
                : item.Note is { Length: > 0 } existing ? $"{existing}；{addOnNote}" : addOnNote;
            ranked.Add((rawScore, new ProcedureIndexRecommendation(
                item.CodeSystemCode,
                item.Code,
                item.IsIndexFallback ? drill.Path[^1].Term : item.Title,
                Math.Min(1m, rawScore),
                item.IsIndexFallback,
                pathDisplay + $" | 索引编码: {indexCode}",
                note,
                item.WholeTitleDepth,
                item.FamilyRank,
                indexCode)));
        }

        return ranked.Count > rankedBefore;
    }

    private IEnumerable<ProcedureIndexRecommendation> Order(
        List<(decimal RawScore, ProcedureIndexRecommendation Recommendation)> candidates)
    {
        var sorted = candidates
            .GroupBy(item => item.Recommendation.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Recommendation.WholeTitleDepth)
                .ThenByDescending(item => item.RawScore)
                .ThenBy(item => item.Recommendation.FamilyRank)
                .First())
            .OrderByDescending(item => item.Recommendation.WholeTitleDepth)
            .ThenByDescending(item => item.RawScore)
            .ThenBy(item => item.Recommendation.FamilyRank)
            .ToList();

        // 一族一席优先，空出的名额再由同族续填：同一个索引码展开出的多级详码分数必然同分，
        // 一条链路占满推荐位会把别的链路顶出 Top3。二遍补位保证没有竞争者时粒度菜单仍占满名额。
        var seats = new List<ProcedureIndexRecommendation>();
        var takenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seatsByFamily = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in sorted)
        {
            if (seats.Count >= maxResults)
            {
                break;
            }

            var family = item.Recommendation.IndexFamilyCode ?? item.Recommendation.Code;
            if (seatsByFamily.TryGetValue(family, out var used) && used >= MaxSeatsPerIndexFamily
                || !takenCodes.Add(item.Recommendation.Code))
            {
                continue;
            }

            seatsByFamily[family] = used + 1;
            seats.Add(item.Recommendation);
        }

        foreach (var item in sorted)
        {
            if (seats.Count >= maxResults)
            {
                break;
            }

            if (takenCodes.Add(item.Recommendation.Code))
            {
                seats.Add(item.Recommendation);
            }
        }

        return seats;
    }

    private static string NormalizeInput(string inputText)
    {
        var text = (inputText ?? string.Empty).Trim();
        text = TrailingCodeParenthesis.Replace(text, string.Empty).Trim();
        // 首页与录入习惯会把术式编号写在前面（"1.腹腔镜下胆囊切除术"）：编号属检索噪声。
        text = LeadingEnumeration.Replace(text, string.Empty).Trim();
        return text;
    }

    private static readonly Regex LeadingEnumeration = new(
        @"^\s*(?:[（(]?\d+[)）.、：:]|[①②③④⑤⑥⑦⑧⑨⑩])\s*",
        RegexOptions.Compiled);

    /// <summary>主导词别名（含去装饰变体）与输入逐字相等才算命中。</summary>
    private static Cm3LeadWordHit? FindExactLeadHit(Cm3IndexSnapshot snapshot, string inputKey)
    {
        foreach (var lead in snapshot.Leads)
        {
            foreach (var alias in lead.Aliases)
            {
                if (string.Equals(alias.Key, inputKey, StringComparison.Ordinal)
                    || string.Equals(alias.MatchKey, inputKey, StringComparison.Ordinal))
                {
                    return new Cm3LeadWordHit(lead, alias, 1m, inputKey.Length);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 链路对输入的字符覆盖比例（去重字符集口径）：每个节点取"含括号/去括号"两个词面变体，
    /// 挡住只沾两个字边的无关链路。分母只计检索核字符且排除 ASCII 字母数字
    /// （列表序号与编码标注是纯 ASCII，短术名会被稀释到门槛之下）。
    /// public 供离线诊断工具复用生产口径，避免工具侧另写一套判据。
    /// </summary>
    public static decimal PathOverlapRatio(IReadOnlyList<Cm3IndexNode> path, string inputText)
    {
        var inputKey = TextMatch.StripIndexingFillers(inputText);
        if (inputKey.Length == 0)
        {
            inputKey = TextMatch.NormalizeKey(inputText);
        }

        if (inputKey.Length == 0)
        {
            return 0m;
        }

        var contentCharacters = inputKey
            .Where(character => !char.IsAsciiLetterOrDigit(character))
            .Distinct()
            .ToHashSet();
        var inputCharacters = contentCharacters.Count > 0
            ? contentCharacters
            : inputKey.Distinct().ToHashSet();
        var covered = new HashSet<char>();
        foreach (var node in path)
        {
            foreach (var variant in new[]
            {
                node.Term,
                Cm3IndexSnapshotBuilder.ParenthesisRegex.Replace(node.Term, string.Empty),
            })
            {
                covered.UnionWith(TextMatch.NormalizeKey(variant).Where(inputCharacters.Contains));
            }
        }

        return covered.Count / (decimal)inputCharacters.Count;
    }
}
