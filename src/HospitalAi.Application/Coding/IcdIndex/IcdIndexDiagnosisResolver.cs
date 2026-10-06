using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Coding.IcdIndex;

/// <summary>ICD 索引树快照来源（平台级共享）。</summary>
public interface IIcdIndexSource
{
    IcdIndexSnapshot GetSnapshot(CancellationToken cancellationToken = default);

    void Invalidate();
}

/// <summary>医学编码字典快照来源（按医院 + 编码体系）。</summary>
public interface IIcdDictionarySource
{
    IcdDictionarySnapshot GetDictionary(
        Guid hospitalId,
        string codeSystemCode,
        CancellationToken cancellationToken = default);

    void Invalidate();
}

public sealed record DiagnosisIndexRecommendation(
    string CodeSystemCode,
    string Code,
    string Title,
    decimal Score,
    bool IsIndexFallback,
    bool IsConditionalAddOn,
    string EvidencePath,
    string? Note,
    /// <summary>整标题逐字贴合度（标题长/输入长），供管道复排时保住"逐字完整标题优先"口径。</summary>
    decimal WholeTitleDepth = 0m);

/// <summary>
/// 诊断输入的两段式解析：主导词召回 → 逐级下钻 → 索引编码格式解析 → 字典展开。
/// 诊断侧不再回退字典直匹配（决策 2）：无主导词命中即返回空。
/// 索引命中但字典展不开 → 回退推荐索引编码本身（IsIndexFallback）。
/// 输入按术语同义词生成规范变体（原文 + 改写文各跑一遍，按编码去重取高分）；
/// "X的 -见情况" 式失效主导词行命中时改道 病，疾病 主导词下钻部位修饰词。
/// </summary>
public sealed class IcdIndexDiagnosisResolver(int leadWordTopK, int maxResults, string diagnosisCodeSystem)
{
    public IReadOnlyList<DiagnosisIndexRecommendation> Resolve(
        IcdIndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        string inputText,
        IReadOnlyList<string>? aliasTexts = null,
        IcdTermSynonymSnapshot? synonyms = null,
        CancellationToken cancellationToken = default)
    {
        var texts = new List<string> { inputText };
        if (aliasTexts is { Count: > 0 })
        {
            texts.AddRange(aliasTexts.Where(text => text.Length > 0 && !texts.Contains(text)));
        }

        if (synonyms is not null)
        {
            texts.AddRange(synonyms
                .ExpandVariants(inputText, TermSynonymTypes.Diagnosis)
                .Where(text => text.Length > 0 && !texts.Contains(text)));
        }

        var ranked = new List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)>();
        foreach (var text in texts)
        {
            ResolveText(snapshot, dictionary, text, ranked, cancellationToken);
        }

        // 整标题贴合度优先于链路分数：标题被输入逐字连续包含（且解释掉 ≥75% 输入字符）时，
        // 编码员看的就是这一条，其它链路的相关性再高也只能排其后。贴合度同为 0（无整标题命中）
        // 时退回链路分数口径，非命中类结果的排序完全不变。
        return ranked
            .GroupBy(item => item.Recommendation.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Recommendation.WholeTitleDepth)
                .ThenByDescending(item => item.RawScore)
                .First())
            .OrderByDescending(item => item.Recommendation.WholeTitleDepth)
            .ThenByDescending(item => item.RawScore)
            .Take(maxResults)
            .Select(item => item.Recommendation)
            .ToList();
    }

    private void ResolveText(
        IcdIndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        string inputText,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> ranked,
        CancellationToken cancellationToken)
    {
        var inputKey = TextMatch.NormalizeKey(inputText);
        if (inputKey.Length < 2)
        {
            return;
        }

        var leadHits = IcdIndexMatcher.RecallLeads(snapshot, inputKey, leadWordTopK).ToList();

        // 失效主导词"现译名/见"重定向：仅当常规召回全空时按目标文本重试一次。
        if (leadHits.Count == 0
            && IcdIndexMatcher.ResolveRedirect(snapshot, inputKey) is { } redirect)
        {
            leadHits = IcdIndexMatcher
                .RecallLeads(snapshot, TextMatch.NormalizeKey(redirect), leadWordTopK)
                .ToList();
        }

        foreach (var hit in leadHits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Collect(snapshot, dictionary, inputText,
                IcdIndexMatcher.Drill(snapshot, hit, inputKey), hit.Alias.Weight, ranked);
        }

        // "X的 -见 情况" 失效主导词行通道：输入完整含住该行别名（如 "胆囊的继发" 含 胆囊的）
        // 但该行本身不产码（跳转目标是惯例泛词"情况"=病，疾病）。改道 病，疾病 后按该
        // 别名键精确锚定子节点（病，疾病→胆囊 K82.9）；从根节点自由下钻会被输入里的
        // 程度词/继发词带进无关子树。
        foreach (var (key, target) in snapshot.Redirects)
        {
            if (!target.Contains("情况", StringComparison.Ordinal)
                || key.Length < 2
                || !inputKey.Contains(key, StringComparison.Ordinal))
            {
                continue;
            }

            if (IcdIndexMatcher.ResolveJumpLead(snapshot, target) is { } lead)
            {
                Collect(
                    snapshot, dictionary, inputText,
                    IcdIndexMatcher.DrillFromRedirectAnchor(
                        snapshot, lead, key, inputKey, $"{key} -见 {target}"),
                    1m,
                    ranked);
            }
        }
    }

    private void Collect(
        IcdIndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        string inputText,
        DrillResult? drill,
        decimal leadBoost,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> ranked)
    {
        if (drill?.CodeNode.Code is not { Length: > 0 } indexCode)
        {
            return;
        }

        var parsed = ParsedIndexCode.Parse(indexCode);
        var pathDisplay = string.Join(" → ", drill.Path.Select(node => node.Term));
        if (drill.Jumps.Count > 0)
        {
            pathDisplay += " [跳转: " + string.Join("; ", drill.Jumps) + "]";
        }

        var expanded = IndexCodeExpander.Expand(
            parsed, dictionary, inputText, drill.PathKey, maxResults, diagnosisCodeSystem);
        if (expanded.Count == 0)
        {
            return;
        }

        var depthBoost = 1m + 0.05m * drill.Path.Count;
        // 链路字符重叠门控：主导词+下钻路径（取各节点与输入重叠最好的别名变体）
        // 必须解释输入 30% 以上字符，否则视为无关链路直接丢弃；重叠度并入评分做精排。
        // 跨部位误下钻（基底节/结核/牙源性）由主导词与子树限定词门控拦截，此处阈值只兜底。
        var pathOverlap = PathOverlapRatio(drill.Path, inputText);
        if (pathOverlap < 0.30m)
        {
            return;
        }

        foreach (var item in expanded)
        {
            // 排序用未截断分：Reported Score 上限 1.0 会把 "扁桃体息肉→J35.805"（标题全覆盖）
            // 与 "→J03.901 急性化脓性扁桃体炎"（沾词）压成同分，取Top时只剩召回顺序。
            var rawScore = item.Score * leadBoost * depthBoost * (0.5m + pathOverlap)
                * (item.IsIndexFallback ? 0.8m : 1m);
            ranked.Add((rawScore, new DiagnosisIndexRecommendation(
                item.CodeSystemCode,
                item.Code,
                item.IsIndexFallback ? drill.Path[^1].Term : item.Title,
                Math.Min(1m, rawScore),
                item.IsIndexFallback,
                item.IsConditionalAddOn,
                pathDisplay + $" | 索引编码: {indexCode}",
                item.Note,
                item.WholeTitleDepth)));
        }
    }

    /// <summary>
    /// 命中链路对输入的字符覆盖比例（去重字符集合口径）：每个路径节点取
    /// "含括号/去括号/去装饰"变体，统计链路命中的输入字符占比，
    /// 挡住 "闭锁→输卵管" 这类只沾两个字边的无关链路。
    /// 分母只计检索核字符：列表序号与编码标注（"2.肝大" 的 2、"II°" 的罗马数字）是纯 ASCII，
    /// "原因待查/可能性大" 类引导语与程度修饰是书写噪声，
    /// 否则 "2.肝大原因待查 → 肝大" 这类短名会被稀释到门槛之下。
    /// </summary>
    private static decimal PathOverlapRatio(IReadOnlyList<IcdIndexNode> path, string inputText)
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
            var (bare, _) = IcdIndexSnapshotBuilder.StripDecorations(node.Term);
            foreach (var variant in new[]
            {
                node.Term,
                IcdIndexSnapshotBuilder.ParenthesisRegex.Replace(node.Term, string.Empty),
                bare,
                IcdIndexSnapshotBuilder.ParenthesisRegex.Replace(bare, string.Empty),
            })
            {
                covered.UnionWith(TextMatch.NormalizeKey(variant).Where(inputCharacters.Contains));
            }
        }

        return covered.Count / (decimal)inputCharacters.Count;
    }
}
