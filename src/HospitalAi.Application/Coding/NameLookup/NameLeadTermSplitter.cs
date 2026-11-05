using HospitalAi.Application.Coding.Cm3Index;
using HospitalAi.Application.Coding.IcdIndex;
using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Coding.NameLookup;

/// <summary>
/// "编码录入"的主导词拆分：把一条诊断/手术名称拆成**全量匹配**的主导词集合。
/// 口径（与既有流水线召回刻意不同，见 docs/2026-11-03-诊断手术编码录入与主导词拆分推荐方案.md §3）：
/// 候选 = 索引快照 0 级有效主导词的别名键（含 又称 变体由调用方在输入侧改写）；
/// 命中 = 别名键作为连续子串**完整**出现在归一化输入中（严格子串，一条规则排除全部范围外主导词——
/// 中心细胞性淋巴瘤 命中 中心/细胞(性)/淋巴瘤/瘤，不含 丘疹病（无字面交集）与 淋巴瘤样（样 字超出输入））；
/// 锚点门控 = <c>IcdIndexMatcher.NameLookupAnchorAllowed</c> / <c>Cm3ProcedureIndexMatcher.NameLookupAnchorAllowed</c>，
/// 与流水线召回同一套噪声防线（或 尾段锚、泛化修饰词、单字尾重心、泛术式 手术、属性尾段）。
/// 同一主导词节点多个别名命中只留最优一条；排序：覆盖字数 ↓ → 尾部命中优先 → 位置靠后优先 → 权重 ↓。
/// </summary>
public static class NameLeadTermSplitter
{
    /// <summary>展示上限兜底（临床名称的全量命中通常 ≤ 8，超长名称防刷屏）。</summary>
    public const int MaxLeads = 16;

    /// <summary>诊断侧命中：包住引擎主导词节点与命中别名，供下钻直接构造 <see cref="LeadWordHit"/>。
    /// RedirectKey 非空时表示经失效主导词转示行（"阿米巴 - 见 阿米巴病"）锚到转示目标主导词。</summary>
    public sealed record IcdLeadTermHit(
        IcdIndexNode Node,
        IcdIndexAlias Alias,
        int Position,
        bool TailHit,
        string? RedirectKey = null)
    {
        public int SeqNo => Node.SeqNo;

        /// <summary>索引原始词面（如 细胞(性)）。</summary>
        public string Term => Node.Term;

        /// <summary>实际命中的别名键（归一化后）。</summary>
        public string HitAlias => Alias.Key;

        public int CoveredChars => Alias.Key.Length;
    }

    /// <summary>手术侧命中：包住 CM3 引擎主导词节点与命中别名。RedirectKey 语义同诊断侧。</summary>
    public sealed record Cm3LeadTermHit(
        Cm3IndexNode Node,
        Cm3IndexAlias Alias,
        int Position,
        bool TailHit,
        string? RedirectKey = null)
    {
        public int SeqNo => Node.SeqNo;

        public string Term => Node.Term;

        public string HitAlias => Alias.Key;

        public int CoveredChars => Alias.Key.Length;
    }

    /// <summary>诊断侧（icd_index 快照）。</summary>
    public static IReadOnlyList<IcdLeadTermHit> SplitIcd(IcdIndexSnapshot snapshot, string inputKey, int maxLeads = MaxLeads)
    {
        if (inputKey.Length < 1 || snapshot.Leads.Count == 0)
        {
            return [];
        }

        var hits = new List<IcdLeadTermHit>();
        foreach (var lead in snapshot.Leads)
        {
            if (!IcdIndexMatcher.LeadNodeAllows(lead, inputKey))
            {
                continue;
            }

            IcdLeadTermHit? best = null;
            foreach (var alias in lead.Aliases)
            {
                if (!IcdIndexMatcher.NameLookupAnchorAllowed(lead, alias, inputKey))
                {
                    continue;
                }

                var position = inputKey.IndexOf(alias.Key, StringComparison.Ordinal);
                if (position < 0)
                {
                    continue;
                }

                var candidate = new IcdLeadTermHit(
                    lead, alias, position, position + alias.Key.Length == inputKey.Length);
                if (best is null || RankBetter(candidate, best))
                {
                    best = candidate;
                }
            }

            if (best is not null)
            {
                hits.Add(best);
            }
        }

        AddRedirectHits(snapshot, inputKey, hits);
        var leadSpans = hits
            .Where(hit => hit.RedirectKey is null)
            .Select(hit => (Start: hit.Position, End: hit.Position + hit.CoveredChars))
            .ToList();
        var childSpans = ChildSpansOfHits(hits.Select(hit => hit.Node), inputKey);
        DropMidWordFragments(
            hits,
            hit => hit.Position,
            hit => hit.CoveredChars,
            hit => hit.RedirectKey is not null,
            leadSpans,
            childSpans,
            inputKey);
        return RankAndTake(
            hits,
            maxLeads,
            hit => hit.CoveredChars,
            hit => hit.TailHit,
            hit => hit.Position,
            hit => hit.Alias.Weight,
            hit => hit.Node.SeqNo);
    }

    /// <summary>命中主导词的直接子行词面跨度（MatchKey ≥2 且被输入包含）——"紧邻修饰子行"判据的参照，
    /// 如 感染(性) 的子行 大肠杆菌 NEC 之于 肠毒性大肠杆菌感染 的 毒性。</summary>
    private static List<(int Start, int End)> ChildSpansOfHits(IEnumerable<IcdIndexNode> nodes, string inputKey)
    {
        var spans = new List<(int Start, int End)>();
        foreach (var node in nodes)
        {
            foreach (var child in node.Children)
            {
                foreach (var alias in child.Aliases)
                {
                    if (alias.MatchKey.Length < 2)
                    {
                        continue;
                    }

                    var start = inputKey.IndexOf(alias.MatchKey, StringComparison.Ordinal);
                    if (start >= 0)
                    {
                        spans.Add((start, start + alias.MatchKey.Length));
                        break;
                    }
                }
            }
        }

        return spans;
    }

    /// <summary>
    /// 中段碎片终结（"编码录入"专用，诊断/手术同口径）：
    /// 2 字别名在输入**中段**（非首/尾）命中时——
    /// (a) 其跨度被另一命中主导词跨度严格包含（中心细胞性淋巴瘤：淋巴 ⊂ 淋巴瘤）；
    /// (b) 紧邻另一命中主导词的**修饰子行**跨度之后（肠毒性大肠杆菌感染：毒性|大肠杆菌）；
    /// (c) 输入中段的 2 字命中，其前一字符拼成的 3 字串是**复合病原名后半段**——
    ///     "鼠|伤寒" 的 伤寒（鼠伤寒 是血清型名，复合名 鼠伤寒沙门菌感染 的核心是 沙门菌）。
    ///     判据：输入里该 2 字词前的 1 个字符 + 2 字词 组成的 3 字串，与 2 字词本身不同
    ///     且不为另一命中主导词所涵盖时，视为复合名碎片（伤寒(顿挫型) 行因此不入主导词；
    ///     结核性肺炎 的 肺炎 前一字符 性 拼成 性肺炎 非复合名语义，仍保留——
    ///     该判据只对 后字=前字+枚举结构 的 前缀复合 型生效：鼠+伤寒、(薪)水+肿 之类由实测增补）。
    /// 结核性肺炎细胞学… 的 肺炎（后随 细胞学 非命中/子行跨度）保留；≥3 字、首/尾位置、转示命中不适用。
    /// </summary>
    /// <summary>
    /// 疾病名的复合前缀字：这些字 + 疾病核心词组成的 3 字串（鼠伤寒、副伤寒…）
    /// 是索引里的**独立病名**（鼠伤寒=沙门菌血清型、副伤寒=副伤寒杆菌病），
    /// 输入中段的 2 字核心词若是这种复合名的后半段（前一字是复合前缀），
    /// 它是复合名的一部分，不是独立主导词。
    /// </summary>
    private static readonly HashSet<char> CompoundNamePrefixes =
    [
        '鼠', '副',
    ];

    private static bool IsCompoundNameTail(int start, string inputKey)
        => start >= 1 && CompoundNamePrefixes.Contains(inputKey[start - 1]);

    private static void DropMidWordFragments<T>(
        List<T> hits,
        Func<T, int> position,
        Func<T, int> coveredChars,
        Func<T, bool> isRedirect,
        List<(int Start, int End)> leadSpans,
        List<(int Start, int End)> childSpans,
        string inputKey)
    {
        hits.RemoveAll(hit =>
        {
            var length = coveredChars(hit);
            if (isRedirect(hit) || length != 2)
            {
                return false;
            }

            var start = position(hit);
            var end = start + length;
            if (start == 0 || end == inputKey.Length)
            {
                return false;
            }

            if (leadSpans.Any(span => span.Start <= start && span.End >= end && span.End - span.Start > length))
            {
                return true;
            }

            // (c) 复合名后半段：前一字符是复合前缀（鼠伤寒 的 伤寒、副伤寒 的 伤寒）。
            if (IsCompoundNameTail(start, inputKey))
            {
                return true;
            }

            return childSpans.Any(span => span.Start == end);
        });
    }

    /// <summary>
    /// 失效主导词转示行（"阿米巴 - 见 阿米巴病"）不在 <see cref="IcdIndexSnapshot.Leads"/> 里，
    /// 其键（阿米巴）全量命中输入时，锚到转示目标主导词——编码员口径"查 阿米巴 必须转 阿米巴病"，
    /// 与流水线 见-1 改道同口径；转示目标已命中或键已被正牌别名覆盖时跳过。
    /// </summary>
    private static void AddRedirectHits(IcdIndexSnapshot snapshot, string inputKey, List<IcdLeadTermHit> hits)
    {
        foreach (var (key, target) in snapshot.Redirects)
        {
            if (key.Length < 1 || hits.Any(hit => hit.RedirectKey == key))
            {
                continue;
            }

            var position = inputKey.IndexOf(key, StringComparison.Ordinal);
            if (position < 0)
            {
                continue;
            }

            // 单字转示键只在词首或词尾锚定：索引确实把 髋/高/瘤 这类"部位/属性单字"立成转示行
            // （"髋 -见 挛缩，狭窄"、"高 -见 效应，有害，高海拔"），但病历里它们几乎都是
            // 更长词的一部分——人工髋关节置换术后髋臼松动 的 髋 落在中段，锚到 挛缩 后
            // 把正牌 Z96.601 人工髋关节 顶成 M24.501 髋关节挛缩；词尾的 不稳定髋 才是索引本来的答案。
            // 2 字及以上不动：见-1 是"必须转换"，肠炎 -见 小肠炎 的 肠炎 写在输入中段
            // （病毒性肠炎伴脱水）照样要转，按长度一刀切会毁掉 编码员口径。
            if (key.Length == 1 && position > 0 && position + key.Length < inputKey.Length)
            {
                continue;
            }

            if (ResolveRedirectLead(snapshot, target) is not { } targetLead)
            {
                continue;
            }

            if (hits.Any(hit => hit.Node.SeqNo == targetLead.SeqNo))
            {
                continue;
            }

            hits.Add(new IcdLeadTermHit(
                targetLead,
                new IcdIndexAlias(key, 1m, key),
                position,
                position + key.Length == inputKey.Length,
                key));
        }
    }

    /// <summary>
    /// 转示目标 → 主导词节点。索引把目标写成 "主导词，修饰词，修饰词"
    /// （"副伤寒 - 见 热，副伤寒"、"沙门菌 - 见 感染，沙门菌" 全库 73 行），
    /// 整串归一化键（热副伤寒）必然查不到——转示静默失效，输入 副伤寒 既不锚 热
    /// 也不锚 沙门菌，只能落在 伤寒 上。按索引惯例取**首段**精确键，
    /// 整串键保留为第二档（不改变单段目标 阿米巴病 的既有命中面）。
    /// 不做流水线 ResolveJumpLead 的模糊回落档：面板口径是"全量匹配"，
    /// 召不回的正牌主导词不该由近似词顶上。
    /// 泛词目标（见情况/疾病/病）照旧不作转示锚点——纪律 4。
    /// </summary>
    private static IcdIndexNode? ResolveRedirectLead(IcdIndexSnapshot snapshot, string target)
    {
        if (IcdIndexMatcher.IsGenericConditionTarget(target))
        {
            return null;
        }

        var segments = target
            .Split(['，', ',', '、'], StringSplitOptions.RemoveEmptyEntries)
            .Select(TextMatch.NormalizeKey)
            .Where(key => key.Length > 0)
            .ToList();

        if (segments.Count == 0)
        {
            return null;
        }

        if (snapshot.LeadByExactKey.TryGetValue(segments[0], out var lead))
        {
            return lead;
        }

        return snapshot.LeadByExactKey.TryGetValue(TextMatch.NormalizeKey(target), out var whole)
            ? whole
            : null;
    }

    /// <summary>手术侧（cm3_index 快照）。</summary>
    public static IReadOnlyList<Cm3LeadTermHit> SplitCm3(Cm3IndexSnapshot snapshot, string inputKey, int maxLeads = MaxLeads)
    {
        if (inputKey.Length < 1 || snapshot.Leads.Count == 0)
        {
            return [];
        }

        var hits = new List<Cm3LeadTermHit>();
        foreach (var lead in snapshot.Leads)
        {
            if (!Cm3ProcedureIndexMatcher.LeadNodeAllows(lead, inputKey))
            {
                continue;
            }

            Cm3LeadTermHit? best = null;
            foreach (var alias in lead.Aliases)
            {
                if (!Cm3ProcedureIndexMatcher.NameLookupAnchorAllowed(lead, alias, inputKey))
                {
                    continue;
                }

                var position = inputKey.IndexOf(alias.Key, StringComparison.Ordinal);
                if (position < 0)
                {
                    continue;
                }

                var candidate = new Cm3LeadTermHit(
                    lead, alias, position, position + alias.Key.Length == inputKey.Length);
                if (best is null || RankBetter(
                    (candidate.CoveredChars, candidate.TailHit, candidate.Position, candidate.Alias.Weight, candidate.Node.SeqNo),
                    (best.CoveredChars, best.TailHit, best.Position, best.Alias.Weight, best.Node.SeqNo)))
                {
                    best = candidate;
                }
            }

            if (best is not null)
            {
                hits.Add(best);
            }
        }

        // 失效主导词转示行（"阿贝手术 ―见 Abbe 手术"）同诊断侧口径。
        foreach (var (key, target) in snapshot.Redirects)
        {
            if (key.Length < 2 || hits.Any(hit => hit.RedirectKey == key))
            {
                continue;
            }

            var position = inputKey.IndexOf(key, StringComparison.Ordinal);
            if (position < 0
                || !snapshot.LeadByExactKey.TryGetValue(TextMatch.NormalizeKey(target), out var targetLead)
                || hits.Any(hit => hit.Node.SeqNo == targetLead.SeqNo))
            {
                continue;
            }

            hits.Add(new Cm3LeadTermHit(
                targetLead,
                new Cm3IndexAlias(key, 1m, key),
                position,
                position + key.Length == inputKey.Length,
                key));
        }

        DropMidWordFragments(
            hits,
            hit => hit.Position,
            hit => hit.CoveredChars,
            hit => hit.RedirectKey is not null,
            hits.Select(hit => (Start: hit.Position, End: hit.Position + hit.CoveredChars)).ToList(),
            ChildSpansOfHitsCm3(hits.Select(hit => hit.Node), inputKey),
            inputKey);
        return RankAndTake(
            hits,
            maxLeads,
            hit => hit.CoveredChars,
            hit => hit.TailHit,
            hit => hit.Position,
            hit => hit.Alias.Weight,
            hit => hit.Node.SeqNo);
    }

    /// <summary>命中主导词的直接子行词面跨度（CM3 版，MatchKey ≥2 且被输入包含）。</summary>
    private static List<(int Start, int End)> ChildSpansOfHitsCm3(IEnumerable<Cm3IndexNode> nodes, string inputKey)
    {
        var spans = new List<(int Start, int End)>();
        foreach (var node in nodes)
        {
            foreach (var child in node.Children)
            {
                foreach (var alias in child.Aliases)
                {
                    if (alias.MatchKey.Length < 2)
                    {
                        continue;
                    }

                    var start = inputKey.IndexOf(alias.MatchKey, StringComparison.Ordinal);
                    if (start >= 0)
                    {
                        spans.Add((start, start + alias.MatchKey.Length));
                        break;
                    }
                }
            }
        }

        return spans;
    }

    private static bool RankBetter(IcdLeadTermHit candidate, IcdLeadTermHit best)
        => RankBetter(
            (candidate.CoveredChars, candidate.TailHit, candidate.Position, candidate.Alias.Weight, candidate.Node.SeqNo),
            (best.CoveredChars, best.TailHit, best.Position, best.Alias.Weight, best.Node.SeqNo));

    /// <summary>同节点多别名择优：覆盖字数 ↓ → 尾部命中优先 → 位置靠后（更贴近尾部）优先 → 权重 ↓ → 稳定序。</summary>
    private static bool RankBetter(
        (int CoveredChars, bool TailHit, int Position, decimal Weight, int SeqNo) candidate,
        (int CoveredChars, bool TailHit, int Position, decimal Weight, int SeqNo) best)
        => candidate.CoveredChars > best.CoveredChars
            || (candidate.CoveredChars == best.CoveredChars
                && (candidate.TailHit && !best.TailHit
                    || (candidate.TailHit == best.TailHit
                        && (candidate.Position > best.Position
                            || (candidate.Position == best.Position
                                && (candidate.Weight > best.Weight
                                    || (candidate.Weight == best.Weight && candidate.SeqNo < best.SeqNo)))))));

    private static IReadOnlyList<T> RankAndTake<T>(
        List<T> hits,
        int maxLeads,
        Func<T, int> coveredChars,
        Func<T, bool> tailHit,
        Func<T, int> position,
        Func<T, decimal> weight,
        Func<T, int> seqNo)
    {
        return hits
            .OrderByDescending(coveredChars)
            .ThenByDescending(tailHit)
            .ThenByDescending(position)
            .ThenByDescending(weight)
            .ThenBy(seqNo)
            .Take(Math.Max(1, maxLeads))
            .ToList();
    }
}
