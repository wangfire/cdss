using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Coding.Cm3Index;

public sealed record Cm3LeadWordHit(Cm3IndexNode Node, Cm3IndexAlias Alias, decimal CoverRatio, int CoveredChars);

/// <summary>
/// 手术索引的两段式产物：命中的最深节点、取码节点与命中路径。
/// 结构对标诊断侧 <c>DrillResult</c>，去掉了 CM3 库中不存在的形态学码/泛词情况分支。
/// </summary>
public sealed record Cm3DrillResult(
    Cm3IndexNode Terminal,
    Cm3IndexNode CodeNode,
    IReadOnlyList<Cm3IndexNode> Path,
    string PathKey,
    decimal CoverRatio,
    bool CodeFromAncestorFallback,
    IReadOnlyList<string> Jumps,
    /// <summary>见-1 改道让位的本地链路：改道链路的码展不开时由调用方回退，改道不以失去推荐为代价。</summary>
    Cm3DrillResult? Displaced = null,
    /// <summary>另见-1 提示链路：本行带码时 Top1 不动，目标树能走出的更细码只补空位。</summary>
    IReadOnlyList<Cm3DrillResult>? SeeAlsoHints = null);

/// <summary>
/// 主导词召回（只认 0 级）+ 该主导词子树内的修饰词逐层下钻。
/// 与诊断侧同一套机制（同一份 <see cref="TextMatch"/> 门控），差异只在词面事实：
/// CM3 的 0 级主导词本身就是术式动作词（切除术/修补术/吻合术），1945 条无子节点、
/// 其中 1391 条自带编码——"命中主导词即终点"是本库常态，因此死路主导词不判为弱证据、不降权；
/// 指针只有 ―见（532 条空码，必须转换）、―另见（413 条，多为带码深层行）与 ―见亚目 N.N
/// （106 条，目标亚目码已写在 index_code 上，改道落在码展开而非召回）三种形态。
/// </summary>
public static class Cm3ProcedureIndexMatcher
{
    /// <summary>
    /// 不能作为召回锚点的索引惯例泛词。诊断侧那张表是按疾病词族（部位段/病程尾段/人群段）
    /// 从万条台账筛出来的，手术侧不能照搬：切除术/修补术/吻合术 在 CM3 里是真正的
    /// 主导词（各有 236/214/…条子节点），拉黑它们等于关掉整条下钻通路。
    /// 本表只收实测的纯惯例尾段（"缝合术,按部位" 的 按部位 会命中一切含该词的输入）。
    /// NEC/NOS 不在此列：解析期已被 NecTokenRegex 剥掉，整词就是 NEC 的行连别名都生成不出来，
    /// 天然召不回，不需要再进黑名单（黑名单里放一个规范化后永不出现的键只会误导后来人）。
    /// </summary>
    private static readonly HashSet<string> GenericLeadKeys =
    [
        "按部位", "部位", "其他", "特指", "情况", "疾病", "按类型",
    ];

    /// <summary>
    /// 纯连接行核心词：CM3 实测只有 伴（90 条）与 与（1 条），全部不带编码。
    /// 它们只把上一层的术式接到下一层（"修补术 → ―鼻的 → ――伴结膜瓣 11.53"），
    /// 病历不会逐字写出这一行，按"每层都要挣新内容"的口径链路必然停在它上面、
    /// 只能回退祖先泛码——与诊断侧 见于/由于 同一问题，同一处理（透明化，不加权）。
    /// 诊断侧刻意不折叠 和/及/与/或（多部位组合语义）与 伴有/合并（并存的另一诊断）：
    /// 本库 和/及 没有独立成行，"大肠与大肠" 是同一行内的词，不受此处影响。
    /// </summary>
    private static readonly HashSet<string> ConnectorCoreTerms =
    [
        "伴", "与",
    ];

    /// <summary>连接行最多连续折叠几层（诊断侧实测最深 2 层，本库样本 ――伴… 同形）。</summary>
    private const int MaxConnectorLookThrough = 2;

    /// <summary>
    /// 纯连接行：无编码、无指针、核心词整词就是连接词。
    /// public 供链路打分使用：折叠掉的连接行不是输入真写出的证据层，
    /// 既不挣链路深度加成也不挣覆盖度（纪律 3：改道平权，不加权）。
    /// </summary>
    public static bool IsConnectorNode(Cm3IndexNode node)
        => node.Code is not { Length: > 0 }
            && node.JumpKind == Cm3JumpKind.None
            && ConnectorCoreTerms.Contains(node.CoreTerm);

    public static IReadOnlyList<Cm3LeadWordHit> RecallLeads(
        Cm3IndexSnapshot snapshot,
        string inputKey,
        int topK)
    {
        if (inputKey.Length < 2)
        {
            return [];
        }

        var hits = new List<Cm3LeadWordHit>();
        var inputQualifiers = Cm3SiteQualifiers.Extract(inputKey);
        var inputSites = Cm3SiteTerms.Extract(inputKey);
        foreach (var lead in snapshot.Leads)
        {
            if (!NodeAllows(lead, inputQualifiers, inputKey))
            {
                continue;
            }

            Cm3LeadWordHit? best = null;
            foreach (var alias in lead.Aliases)
            {
                // 单字别名只作下钻目标，不足以定位主导词；方括号注解与并列尾段同理。
                if (!alias.RecallAnchor || GenericLeadKeys.Contains(alias.Key) || alias.Key.Length < 2)
                {
                    continue;
                }

                if (!TextMatch.MatchesInput(alias.Key, inputKey, out var coverage)
                    || !SiteHeadAllows(alias.Key, inputKey, inputSites))
                {
                    continue;
                }

                if (best is null
                    || coverage.CoveredChars > best.CoveredChars
                    || (coverage.CoveredChars == best.CoveredChars
                        && coverage.Ratio * alias.Weight > best.CoverRatio * best.Alias.Weight))
                {
                    best = new Cm3LeadWordHit(lead, alias, coverage.Ratio, coverage.CoveredChars);
                }
            }

            if (best is not null)
            {
                hits.Add(best);
            }
        }

        return hits
            .OrderByDescending(hit => hit.CoveredChars)
            .ThenByDescending(hit => hit.CoverRatio * hit.Alias.Weight)
            .Take(topK)
            .ToList();
    }

    /// <summary>
    /// 主导词词头部位串门控。CM3 的 0 级行写成「部位+术式」整词（髌骨固定术 78.46 实测把
    /// 尺骨骨折切开复位内固定术 领到 78.4600），而 <see cref="TextMatch.MatchesInput"/> 的 0.6 覆盖门
    /// 只要术式尾段三字相同就放行；这类主导词又多是带自码的死路行，码展开再经"路径词被标题完整覆盖"
    /// 通道拿 0.9 分，于是"术式同名、部位无关"的编码直接产 Top1（D_CM3 13740 名实测：
    /// 这类链路 2905 行 Top1 仅 27.40%，对照逐字连续命中的 63.21%）。
    /// 与上一轮被否决的逐字判据不同，这里只认 <see cref="Cm3SiteTerms.HeadSites"/> 里实测抽出的部位双字串：
    /// 词头不在表内一律不表态，所以判据只会收紧已放行的锚点，不会新增召回，也不会碰人名术式行
    /// （拉丁词头规范化后不是汉字双字串，天然在表外）。
    /// 只在输入自己另外写了表内别的部位串时才挡（"只缺不判"）：脑膜切开引流术 的输入声明了 脑膜，
    /// 挡掉 鼓膜切开术 是对的；隐眼摘除术 一个部位词都没写，挡掉 眼球 就是打掉唯一链路。
    /// 两轮变体在同一张表上的实测（D_CM3 面 / 病历 2371 名面，改前基线 5721 与 872 名 Top1）：
    /// 只缺也判 = +38 但打空且丢命中 1 条（隐眼摘除术）、病历 +1；只缺不判 = +21 硬线 0、病历 0。
    /// 按「丢期望码的清空 = 0」的硬线取后者。
    /// </summary>
    private static bool SiteHeadAllows(string aliasKey, string inputKey, HashSet<string> inputSites)
    {
        if (aliasKey.Length <= 2)
        {
            return true;
        }

        var head = aliasKey[..2];
        return !Cm3SiteTerms.HeadSites.Contains(head)
            || inputKey.Contains(aliasKey, StringComparison.Ordinal)
            || aliasKey.Contains(inputKey, StringComparison.Ordinal)
            || inputSites.Contains(head)
            || !inputSites.Any(site => !aliasKey.Contains(site, StringComparison.Ordinal));
    }

    /// <summary>锚定到主导词后在其子树内下钻；jumpTrace 为调用方自构造链路时的首跳标注。</summary>
    public static Cm3DrillResult? Drill(
        Cm3IndexSnapshot snapshot,
        Cm3LeadWordHit leadHit,
        string inputKey,
        int maxHops = 2,
        string? jumpTrace = null)
        => DrillFromLead(
            snapshot,
            leadHit.Node,
            leadHit.Alias.Key,
            inputKey,
            hops: 0,
            maxHops,
            visited: [leadHit.Node.SeqNo],
            jumps: jumpTrace is null ? [] : [jumpTrace]);

    private static Cm3DrillResult? DrillFromLead(
        Cm3IndexSnapshot snapshot,
        Cm3IndexNode lead,
        string initialPathKey,
        string inputKey,
        int hops,
        int maxHops,
        HashSet<int> visited,
        List<string> jumps)
    {
        var path = new List<Cm3IndexNode> { lead };
        var pathKey = initialPathKey;
        var inputQualifiers = Cm3SiteQualifiers.Extract(inputKey);
        var currentCover = CoverageOfPath(pathKey, inputKey);
        var current = lead;
        Cm3IndexNode? pendingJump = null;
        // 主导词行自身写成 "X ―另见 Y" 且本行无码（实测 69 条 0 级行：电切除术/暴露/闭合/操作…）：
        // 编码员口径 另见-2「本行无码，须另查」。把它当作待改道登记，本地子树一行码都没挣到时改道，
        // 否则这条主导词永远不出码。带码的 另见 主导词不预登记——那个名额要让给真正下钻到的子行（纪律 2）。
        Cm3IndexNode? pendingSeeAlsoJump =
            lead.JumpKind == Cm3JumpKind.SeeAlso && lead.Code is not { Length: > 0 } ? lead : null;

        while (true)
        {
            Cm3IndexNode? bestChild = null;
            string bestChildKey = string.Empty;
            int bestGain = 0;
            var bestRatio = 0m;
            Cm3IndexNode[] bestConnectors = [];
            var pathPositions = TextMatch.CoveredTextPositions(pathKey, inputKey);

            var candidates = new List<(Cm3IndexNode Node, Cm3IndexNode[] Connectors)>();
            CollectDrillCandidates(current, [], candidates, inputQualifiers, inputKey);

            foreach (var (child, connectors) in candidates)
            {
                if (!NodeAllows(child, inputQualifiers, inputKey))
                {
                    continue;
                }

                var childBestGain = 0;
                string? childBestKey = null;
                var childBestRatio = 0m;
                foreach (var alias in child.Aliases)
                {
                    if (!AliasAddsInputContent(alias.MatchKey, inputKey, pathPositions))
                    {
                        continue;
                    }

                    var combined = pathKey + alias.MatchKey;
                    var combinedCover = Best(
                        CoverageOfPath(combined, inputKey),
                        CoverageOfPath(TextMatch.Reverse(alias.MatchKey) + TextMatch.Reverse(pathKey), inputKey));
                    var singleHit = alias.MatchKey.Length == 1
                        && inputKey.Contains(alias.MatchKey, StringComparison.Ordinal);
                    var siteAbbrev = SiteAbbrevWord(alias.MatchKey, inputKey, pathPositions) is not null;
                    var gain = combinedCover.CoveredChars - currentCover.CoveredChars
                        + ((singleHit || siteAbbrev) && combinedCover.CoveredChars == currentCover.CoveredChars ? 1 : 0);
                    if (gain >= 1
                        && (childBestKey is null
                            || combinedCover.Ratio > childBestRatio
                            || (combinedCover.Ratio == childBestRatio && gain > childBestGain)))
                    {
                        childBestGain = gain;
                        childBestKey = combined;
                        childBestRatio = combinedCover.Ratio;
                    }
                }

                // 指针行不在子树里就地跳转：登记为本层待改道，等本地链路走完再比覆盖度
                // （口径同诊断侧，见-1 优先但让位可回退）。
                if (child.JumpKind == Cm3JumpKind.See
                    && childBestGain >= 1
                    && pendingJump is null
                    && hops < maxHops)
                {
                    pendingJump = child;
                }
                else if (child.JumpKind == Cm3JumpKind.SeeAlso
                    && childBestGain >= 1
                    && pendingSeeAlsoJump is null
                    && hops < maxHops)
                {
                    pendingSeeAlsoJump = child;
                }

                // 同增益兄弟取"整条链路被输入覆盖比例"更高者：覆盖比例才反映输入真写出的部位。
                if (childBestKey is not null
                    && (childBestGain > bestGain
                        || (childBestGain == bestGain && childBestRatio > bestRatio)))
                {
                    bestGain = childBestGain;
                    bestRatio = childBestRatio;
                    bestChild = child;
                    bestChildKey = childBestKey;
                    bestConnectors = connectors;
                }
            }

            if (bestChild is null || bestGain < 1)
            {
                break;
            }

            foreach (var connector in bestConnectors)
            {
                path.Add(connector);
            }

            current = bestChild;
            path.Add(current);
            pathKey = bestChildKey;
            currentCover = CoverageOfPath(pathKey, inputKey);
        }

        var local = BuildResult(current, CodeNodeOf(path), path, pathKey, inputKey, jumps);

        // ―见 Y（见-1，"必须转换"）：本地只能靠祖先兜底取码时改道无条件优先；
        // 否则比输入覆盖字数。改道目标平权入池，不加权（纪律 3）。
        if (pendingJump is { } jumpChild
            && ResolveJumpLead(snapshot, jumpChild.JumpTarget ?? string.Empty) is { } jumpLead
            && NodeAllows(jumpLead, inputQualifiers, inputKey)
            && visited.Add(jumpLead.SeqNo))
        {
            var jumpAlias = JumpSeedKey(jumpLead, inputKey);
            var jumpTrace = new List<string> { $"{jumpChild.Term} → {jumpChild.JumpTarget}" };
            var jumped = DrillFromLead(
                snapshot, jumpLead, jumpAlias, inputKey, hops + 1, maxHops, visited, jumpTrace);
            if (jumped is not null)
            {
                var mandatory = local is { CodeFromAncestorFallback: true } && !jumped.CodeFromAncestorFallback;
                if (local is null
                    || mandatory
                    || CoveredInputChars(jumped.PathKey, inputKey)
                        >= CoveredInputChars(local.PathKey, inputKey))
                {
                    return mandatory ? jumped with { Displaced = local } : jumped;
                }
            }
        }

        // ―另见 Y（另见-1）：本地无自码只能祖先兜底时，改道目标若有自码直接接管；
        // 本地已有自码则 Top1 不动，只把目标树的更细码挂到 SeeAlsoHints 上补空位。
        // "本行"必须等于链路真正停住的那一行（ReferenceEquals），否则兄弟行的指针会串码（纪律 2）。
        if (pendingSeeAlsoJump is { } alsoChild
            && (ReferenceEquals(current, alsoChild)
                || (local is null && ReferenceEquals(alsoChild, lead)))
            && (local is null || local.CodeFromAncestorFallback)
            && ResolveJumpLead(snapshot, alsoChild.JumpTarget ?? string.Empty) is { } alsoLead
            && NodeAllows(alsoLead, inputQualifiers, inputKey)
            && visited.Add(alsoLead.SeqNo))
        {
            var alsoJumped = DrillFromLead(
                snapshot, alsoLead, JumpSeedKey(alsoLead, inputKey), inputKey,
                hops + 1, maxHops, visited, [$"{alsoChild.Term} ⇢ {alsoChild.JumpTarget}"]);
            if (alsoJumped is not null && !alsoJumped.CodeFromAncestorFallback)
            {
                return alsoJumped;
            }
        }

        if (pendingSeeAlsoJump is { } hintChild
            && ReferenceEquals(current, hintChild)
            && local is { CodeFromAncestorFallback: false }
            && ResolveJumpLead(snapshot, hintChild.JumpTarget ?? string.Empty) is { } hintLead
            && NodeAllows(hintLead, inputQualifiers, inputKey)
            && !visited.Contains(hintLead.SeqNo)
            && DrillFromLead(
                snapshot, hintLead, JumpSeedKey(hintLead, inputKey), inputKey,
                hops + 1, maxHops, [.. visited, hintLead.SeqNo],
                [$"{hintChild.Term} ⇢ {hintChild.JumpTarget}"]) is { } alt
            && alt.Path.Count > 1
            && !alt.CodeFromAncestorFallback
            && alt.CodeNode.Code != local.CodeNode.Code)
        {
            return local with { SeeAlsoHints = [alt] };
        }

        return local;
    }

    private static void CollectDrillCandidates(
        Cm3IndexNode parent,
        IReadOnlyList<Cm3IndexNode> connectors,
        List<(Cm3IndexNode Node, Cm3IndexNode[] Connectors)> sink,
        HashSet<string> inputQualifiers,
        string inputKey)
    {
        foreach (var child in parent.Children)
        {
            if (!NodeAllows(child, inputQualifiers, inputKey))
            {
                continue;
            }

            sink.Add((child, [.. connectors]));

            // 只在输入没有逐字写出这个连接词时才折叠：输入自己写出时折叠会把词面
            // 从 pathKey 省掉、覆盖度分母随之变小，等于给这条链路变相加权（诊断侧实测省下 3 条正确 Top1）。
            if (IsConnectorNode(child) && connectors.Count < MaxConnectorLookThrough
                && !WrittenInInput(child, inputKey))
            {
                CollectDrillCandidates(child, [.. connectors, child], sink, inputQualifiers, inputKey);
            }
        }
    }

    private static bool WrittenInInput(Cm3IndexNode node, string inputKey)
        => node.Aliases.Any(alias => alias.MatchKey.Length > 0
            && inputKey.Contains(alias.MatchKey, StringComparison.Ordinal));

    /// <summary>节点级语义门控：部位限定词须为输入已声明的子集，病程极与否定极不得与输入相反。</summary>
    private static bool NodeAllows(Cm3IndexNode node, HashSet<string> inputQualifiers, string inputKey)
        => node.Qualifiers.IsSubsetOf(inputQualifiers)
            && !TextMatch.HasPhasePoleConflict(node.Term, inputKey)
            && !TextMatch.HasNegationPoleConflict(node.CoreTerm, inputKey);

    private static Cm3DrillResult? BuildResult(
        Cm3IndexNode terminal,
        Cm3IndexNode? codeNode,
        List<Cm3IndexNode> path,
        string pathKey,
        string inputKey,
        List<string> jumps)
    {
        if (codeNode is null)
        {
            return null;
        }

        var cover = CoverageOfPath(pathKey, inputKey);
        return new Cm3DrillResult(
            terminal,
            codeNode,
            path,
            pathKey,
            cover.Ratio,
            !ReferenceEquals(codeNode, terminal),
            jumps.AsReadOnly());
    }

    /// <summary>无码终点回退最近有码祖先；全链无码返回 null。回退必须计入证据链（本库回退频率与诊断侧不同分布）。</summary>
    private static Cm3IndexNode? CodeNodeOf(List<Cm3IndexNode> path)
    {
        if (path[^1].Code is not null)
        {
            return path[^1];
        }

        for (var index = path.Count - 2; index >= 0; index--)
        {
            if (path[index].Code is not null)
            {
                return path[index];
            }
        }

        return null;
    }

    private static int CoveredInputChars(string pathKey, string inputKey)
        => CoverageOfPath(pathKey, inputKey).CoveredChars;

    private static TextMatch.Coverage Best(TextMatch.Coverage left, TextMatch.Coverage right)
        => left.CoveredChars >= right.CoveredChars ? left : right;

    // 下钻遍历专用覆盖口径：单字符残段按包含计（"―胆囊" 的单字部位词要能参与增益）。
    private static TextMatch.Coverage CoverageOfPath(string fragmentKey, string inputKey)
        => TextMatch.CoverageOf(fragmentKey, inputKey, countSingleCharSegments: true);

    /// <summary>
    /// 解析指针目标为主导词节点。CM3 目标是"主导词,部位,用途"式逗号分段
    /// （"修补术,疝,腹股沟的"、"超声,血管内的,按部位"），首段才是主导词，
    /// 按段依次精确定位，全落空时退化为召回打分。
    /// </summary>
    public static Cm3IndexNode? ResolveJumpLead(Cm3IndexSnapshot snapshot, string target)
    {
        var segments = System.Text.RegularExpressions.Regex
            .Split(target, @"[，,、]")
            .Select(segment => TextMatch.NormalizeKey(segment))
            .Where(key => key.Length >= 2)
            .ToList();
        if (segments.Count == 0)
        {
            return null;
        }

        foreach (var segment in segments)
        {
            if (snapshot.LeadByExactKey.TryGetValue(segment, out var exact))
            {
                return exact;
            }
        }

        var concatenated = string.Concat(segments);
        if (snapshot.LeadByExactKey.TryGetValue(concatenated, out var joined))
        {
            return joined;
        }

        return RecallLeads(snapshot, concatenated, topK: 1).FirstOrDefault()?.Node;
    }

    /// <summary>失效主导词 "X ―见 Y" 的重定向：输入键完全命中重定向表时给出目标文本。</summary>
    public static string? ResolveRedirect(Cm3IndexSnapshot snapshot, string inputKey)
        => snapshot.Redirects.GetValueOrDefault(inputKey);

    public static bool IsGenericLeadKey(string aliasKey) => GenericLeadKeys.Contains(aliasKey);

    /// <summary>
    /// 跳转目标主导词的种子键：取与输入贴合度最高的别名。Aliases[0] 常是带澄清括号的整词，
    /// 拼进命中路径会让码展开的"路径词被标题完整覆盖"放行判定失效。
    /// </summary>
    private static string JumpSeedKey(Cm3IndexNode lead, string inputKey)
    {
        var best = string.Empty;
        var bestChars = -1;
        var bestRatio = -1m;
        foreach (var alias in lead.Aliases)
        {
            var cover = CoverageOfPath(alias.MatchKey, inputKey);
            if (cover.CoveredChars > bestChars
                || (cover.CoveredChars == bestChars && cover.Ratio > bestRatio))
            {
                best = alias.MatchKey;
                bestChars = cover.CoveredChars;
                bestRatio = cover.Ratio;
            }
        }

        return best;
    }

    /// <summary>
    /// 子节点别名能否给链路带来输入中的新内容：单字按整字包含放行，其余要求
    /// ≥2 连续片段尚未被路径占住，挡住靠虚字符与重复字挣增益的下钻。
    /// </summary>
    private static bool AliasAddsInputContent(
        string matchKey,
        string inputKey,
        HashSet<int> pathPositions)
    {
        if (matchKey.Length == 1)
        {
            return inputKey.Contains(matchKey, StringComparison.Ordinal);
        }

        // 索引枢纽词 "伴" 独立成行（90 条），病历写 "伴/和/及" 时常与父行词面重叠；
        // 诊断侧对 伴有 用过同一口径，本库整词就是单字，按包含放行。
        var newPositions = TextMatch.CoveredTextPositions(matchKey, inputKey)
            .Except(pathPositions)
            .ToList();
        if (newPositions.Count >= 2)
        {
            return true;
        }

        return SiteAbbrevWord(matchKey, inputKey, pathPositions) is not null;
    }

    /// <summary>索引全称部位词（"－大肠"）对输入简称（"结肠/肠"）的构词前缀口径，只放 大/小。</summary>
    private static readonly HashSet<char> MorphologicalPrefixes = new(['大', '小']);

    private static string? SiteAbbrevWord(
        string matchKey,
        string inputKey,
        HashSet<int> pathPositions)
    {
        foreach (var site in Cm3SiteQualifiers.Extract(inputKey))
        {
            if (site.Length == 0 || matchKey.Length <= site.Length
                || !matchKey.EndsWith(site, StringComparison.Ordinal))
            {
                continue;
            }

            var prefix = matchKey[..^site.Length];
            if (!prefix.All(character => MorphologicalPrefixes.Contains(character)))
            {
                continue;
            }

            var position = inputKey.IndexOf(site, StringComparison.Ordinal);
            while (position >= 0)
            {
                var free = true;
                for (var offset = 0; offset < site.Length; offset++)
                {
                    if (pathPositions.Contains(position + offset))
                    {
                        free = false;
                        break;
                    }
                }

                if (free)
                {
                    return site;
                }

                position = inputKey.IndexOf(site, position + 1, StringComparison.Ordinal);
            }
        }

        return null;
    }
}
