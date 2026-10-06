namespace HospitalAi.Application.Coding.Matching;

/// <summary>
/// 术语同义词的通道类型。手术与诊断分开建组，防止
/// "手术/术后" 类改写串入诊断索引链路（反之亦然）。
/// </summary>
public static class TermSynonymTypes
{
    public const string Diagnosis = "DIAGNOSIS";
    public const string Procedure = "PROCEDURE";
}

public sealed record IcdTermSynonymRow(string TermType, string CanonicalTerm, string VariantTerm);

/// <summary>术语同义词表快照来源（平台级共享，与 icd_index 同待遇）。</summary>
public interface IIcdTermSynonymSource
{
    IcdTermSynonymSnapshot GetSnapshot(CancellationToken cancellationToken = default);

    void Invalidate();
}

/// <summary>
/// 术语同义词内存快照：表里每行 (规范词, 变体词) 视为一条"等同边"，
/// 按连通分量合并成等价类（早搏=早跳=过早搏动=… 一类），类内任意两词都可互换改写；
/// 库里成对镜像的两行（乳腺→乳房 与 乳房→乳腺）也会被合并成同一个类，不必再手工配对。
/// 仍只做输入侧改写（原输入与各改写变体都跑一遍），不在索引节点侧做字符替换——
/// 下钻的新增字符门控按输入字符位置计算，节点侧替换会让位置口径失真。
/// </summary>
public sealed class IcdTermSynonymSnapshot
{
    /// <summary>单条输入最多展开成的检索文本数（含原输入），限制检索扇出。</summary>
    private const int MaxExpandedTexts = 8;

    private sealed record Rule(string Term, int GroupIndex);

    private sealed record Group(string Canonical, IReadOnlyList<string> Members);

    private sealed record Hit(int Index, int Length, int GroupIndex, string Term);

    private sealed class TypeRules
    {
        /// <summary>可命中的类成员，按长度降序（长词优先占位）。</summary>
        public List<Rule> Sources { get; } = [];

        public List<Group> Groups { get; } = [];
    }

    private readonly Dictionary<string, TypeRules> _rulesByType;

    private IcdTermSynonymSnapshot(Dictionary<string, TypeRules> rulesByType)
        => _rulesByType = rulesByType;

    public static IcdTermSynonymSnapshot Empty { get; } =
        new([]);

    public static IcdTermSynonymSnapshot Build(IEnumerable<IcdTermSynonymRow> rows)
    {
        // 每个通道独立建类：term_type 相同的行才互相连通。
        var edgesByType = new Dictionary<string, List<(string Left, string Right, string Canonical)>>(StringComparer.Ordinal);
        var canonicalHitsByType = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var canonical = (row.CanonicalTerm ?? string.Empty).Trim();
            var variant = (row.VariantTerm ?? string.Empty).Trim();
            var termType = (row.TermType ?? string.Empty).Trim();
            if (canonical.Length == 0 || variant.Length == 0 || termType.Length == 0)
            {
                continue;
            }

            if (!edgesByType.TryGetValue(termType, out var edges))
            {
                edges = [];
                edgesByType[termType] = edges;
                canonicalHitsByType[termType] = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            canonicalHitsByType[termType][canonical] =
                canonicalHitsByType[termType].GetValueOrDefault(canonical) + 1;
            if (!string.Equals(canonical, variant, StringComparison.Ordinal))
            {
                edges.Add((variant, canonical, canonical));
            }
        }

        var byType = new Dictionary<string, TypeRules>(StringComparer.Ordinal);
        foreach (var (termType, edges) in edgesByType)
        {
            var canonicalHits = canonicalHitsByType[termType];
            var members = UnionMembers(edges);
            if (members.Count == 0)
            {
                continue;
            }

            var rules = new TypeRules();
            // 类标签取"被最多行当作规范词引用"的成员，其次取最长者：
            // 与既有已验证口径（期前收缩、甲状腺功能亢进、心肌梗死）保持一致。
            foreach (var memberGroup in members)
            {
                var labeled = memberGroup
                    .OrderByDescending(candidate => canonicalHits.GetValueOrDefault(candidate))
                    .ThenByDescending(candidate => candidate.Length)
                    .ThenBy(candidate => candidate, StringComparer.Ordinal)
                    .First();
                // 单字成员（"术"）作替换锚点会大面积误伤（"手术"→"手术后"），
                // 数字/罗码分度（1=I=Ⅰ）已由 TextMatch.NormalizeKey 统一，两侧都不入改写规则。
                var targets = memberGroup
                    .Where(candidate => candidate.Length >= 2)
                    .OrderByDescending(candidate => candidate.Length)
                    .ThenBy(candidate => candidate, StringComparer.Ordinal)
                    .ToList();
                if (targets.Count < 2)
                {
                    continue;
                }

                var groupIndex = rules.Groups.Count;
                rules.Groups.Add(new Group(labeled, targets));
                foreach (var target in targets)
                {
                    rules.Sources.Add(new Rule(target, groupIndex));
                }
            }

            if (rules.Sources.Count > 0)
            {
                rules.Sources.Sort(static (left, right) => right.Term.Length.CompareTo(left.Term.Length));
                byType[termType] = rules;
            }
        }

        return new IcdTermSynonymSnapshot(byType);
    }

    /// <summary>原输入 + 规范改写 + 等价类互换改写（未命中任何类时只有原输入）。</summary>
    public IReadOnlyList<string> ExpandVariants(string? text, string termType)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !_rulesByType.TryGetValue(termType, out var rules)
            || rules.Groups.Count == 0)
        {
            return [text ?? string.Empty];
        }

        var hits = Scan(text, rules);
        if (hits.Count == 0)
        {
            return [text];
        }

        var expanded = new List<string>(MaxExpandedTexts) { text };
        // 全类规范改写排在最前，保持既有检索顺序与打分口径。
        AddUnique(expanded, Render(text, hits, rules, -1, null));
        for (var index = 0; index < hits.Count && expanded.Count < MaxExpandedTexts; index++)
        {
            foreach (var member in rules.Groups[hits[index].GroupIndex].Members)
            {
                if (expanded.Count >= MaxExpandedTexts
                    || string.Equals(member, hits[index].Term, StringComparison.Ordinal))
                {
                    continue;
                }

                AddUnique(expanded, Render(text, hits, rules, index, member));
            }
        }

        return expanded;
    }

    /// <summary>
    /// 从左到右扫描，每个位置取最长可命中的成员；已替换的区间不再参与后续匹配
    /// （避免 早搏→过早搏动 再被 搏动 类二次改写）。
    /// </summary>
    private static List<Hit> Scan(string text, TypeRules rules)
    {
        var hits = new List<Hit>();
        var position = 0;
        while (position < text.Length)
        {
            var matched = false;
            foreach (var rule in rules.Sources)
            {
                if (position + rule.Term.Length > text.Length
                    || string.CompareOrdinal(text, position, rule.Term, 0, rule.Term.Length) != 0)
                {
                    continue;
                }

                hits.Add(new Hit(position, rule.Term.Length, rule.GroupIndex, rule.Term));
                position += rule.Term.Length;
                matched = true;
                break;
            }

            if (!matched)
            {
                position++;
            }
        }

        return hits;
    }

    /// <summary>
    /// 按命中区间拼装检索文本：overrideIndex 指向的命中替换为 overrideTerm，
    /// 其余命中一律替换为所属类标签（多类输入也只产生一条组合文本）。
    /// </summary>
    private static string Render(string text, List<Hit> hits, TypeRules rules, int overrideIndex, string? overrideTerm)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var cursor = 0;
        for (var index = 0; index < hits.Count; index++)
        {
            builder.Append(text.AsSpan(cursor, hits[index].Index - cursor));
            builder.Append(index == overrideIndex
                ? overrideTerm
                : rules.Groups[hits[index].GroupIndex].Canonical);
            cursor = hits[index].Index + hits[index].Length;
        }

        builder.Append(text.AsSpan(cursor));
        return builder.ToString();
    }

    private static void AddUnique(List<string> texts, string candidate)
    {
        if (candidate.Length > 0 && !texts.Contains(candidate, StringComparer.Ordinal))
        {
            texts.Add(candidate);
        }
    }

    /// <summary>把等同边按连通分量合并，返回每个等价类的全部成员。</summary>
    private static List<List<string>> UnionMembers(IEnumerable<(string Left, string Right, string Canonical)> edges)
    {
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);

        string Find(string term)
        {
            if (!parent.TryGetValue(term, out var upstream))
            {
                parent[term] = term;
                return term;
            }

            if (string.Equals(upstream, term, StringComparison.Ordinal))
            {
                return term;
            }

            // 路径压缩，链式同义词（a=b, b=c, c=d）也只做一次遍历。
            var root = Find(upstream);
            parent[term] = root;
            return root;
        }

        foreach (var (left, right, _) in edges)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);
            if (!string.Equals(leftRoot, rightRoot, StringComparison.Ordinal))
            {
                parent[leftRoot] = rightRoot;
            }
        }

        var classes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var term in parent.Keys)
        {
            var root = Find(term);
            if (!classes.TryGetValue(root, out var members))
            {
                members = [];
                classes[root] = members;
            }

            members.Add(term);
        }

        return classes.Values.ToList();
    }
}
