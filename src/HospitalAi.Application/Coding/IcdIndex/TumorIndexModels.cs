using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Coding.IcdIndex;

/// <summary>tumor_index 表行投影（平台级共享，加载快照用）。</summary>
public sealed record TumorIndexRow(
    int SeqNo,
    string IndexName,
    int IndexLevel,
    int ParentSeqNo,
    string FullIndexName,
    string? CodeMalignantPrimary,
    string? CodeMalignantSecondary,
    string? CodeInSitu,
    string? CodeBenign,
    string? CodeUncertain,
    int JumpKind,
    string? JumpTarget);

/// <summary>肿瘤索引（部位表）快照来源。</summary>
public interface ITumorIndexSource
{
    TumorIndexSnapshot GetSnapshot(CancellationToken cancellationToken = default);

    void Invalidate();
}

/// <summary>部位查找产出：形态学码行为位对应的肿瘤表列值与命中路径。
/// AliasText 是把输入锚到该部位行上的别名词面（已归一化、必为输入的子串），
/// 未特指兜底行没有部位词面，留空。</summary>
public sealed record TumorSiteMatch(
    int NodeSeqNo,
    string Code,
    string DisplayPath,
    char BehaviorDigit,
    string ColumnName,
    string AliasText = "");

/// <summary>诊断投影：输入命中的部位行与排序键（离线台账排查"查不到/双亲并列"用）。
/// EmittedCode 是该行按所查行为位实际会发出的部位码，TiedWithBest 表示与最优行同名次（并列解出不同码就放弃查表）。</summary>
public sealed record TumorSiteCandidate(
    int SeqNo,
    int ParentSeqNo,
    int Level,
    string FullIndexName,
    string CoreTerm,
    int JumpKind,
    decimal AliasScore,
    decimal PathScore,
    int AliasLength,
    bool TiedWithBest,
    string? EmittedCode);

/// <summary>
/// 肿瘤索引（"肿瘤，肿瘤性"部位表）内存快照：形态学码行为位 → 动态码列 → 部位码查找。
/// 词条解析复用 <see cref="IcdIndexSnapshotBuilder.ParseNode"/>（横杠层级/见另见/括号别名同口径）。
/// </summary>
public sealed class TumorIndexSnapshot
{
    private sealed class Node
    {
        public required int SeqNo { get; init; }

        public required int Level { get; init; }

        public required int ParentSeqNo { get; init; }

        public required string Term { get; init; }

        public required string CoreTerm { get; init; }

        public required IReadOnlyList<IcdIndexAlias> Aliases { get; init; }

        public required int JumpKind { get; init; }

        public required string? JumpTarget { get; init; }

        public required string FullIndexName { get; init; }

        public required string?[] Codes { get; init; }

        /// <summary>祖先核心词（不含根与本节点），父路径消歧用。</summary>
        public string[] PathWords { get; set; } = [];

        /// <summary>所属主导词族（1 级行自身）。同族内的层级是特指程度，跨族不是（见 SameSiteRank）。</summary>
        public int FamilySeqNo { get; set; }
    }

    // 行为位 → 列下标（ICD-O 动态编码；/9 恶性未特指原发继发，按原发处理）
    private const int PrimaryColumn = 0;
    private const int SecondaryColumn = 1;
    private const int InSituColumn = 2;
    private const int BenignColumn = 3;
    private const int UncertainColumn = 4;

    private static readonly string[] ColumnNames =
        ["恶性-原发", "恶性-继发", "原位", "良性", "动态未定或未知"];

    private readonly Dictionary<int, Node> _nodes;

    private readonly Dictionary<char, int> _columnByBehavior = new()
    {
        ['0'] = BenignColumn,
        ['1'] = UncertainColumn,
        ['2'] = InSituColumn,
        ['3'] = PrimaryColumn,
        ['6'] = SecondaryColumn,
        ['9'] = PrimaryColumn,
    };

    private TumorIndexSnapshot(Dictionary<int, Node> nodes, int rootSeqNo)
    {
        _nodes = nodes;
        RootSeqNo = rootSeqNo;
    }

    public int RootSeqNo { get; }

    public int TotalNodes => _nodes.Count;

    public static TumorIndexSnapshot Build(IReadOnlyList<TumorIndexRow> rows)
    {
        var nodes = new Dictionary<int, Node>();
        foreach (var row in rows)
        {
            if (nodes.ContainsKey(row.SeqNo))
            {
                continue;
            }

            var parsed = IcdIndexSnapshotBuilder.ParseNode(new IcdIndexRow(
                row.SeqNo, row.IndexName, null, row.IndexLevel, row.ParentSeqNo, 0));
            var coreTerm = StripClassifierTokens(StripTableMarkers(parsed.CoreTerm));
            nodes[row.SeqNo] = new Node
            {
                SeqNo = row.SeqNo,
                Level = row.IndexLevel,
                ParentSeqNo = row.ParentSeqNo,
                Term = parsed.Term,
                CoreTerm = coreTerm,
                Aliases = WithCoreTermAlias(WithTrimmedClassifierAliases(WithMarkerStrippedAliases(parsed.Aliases)), coreTerm),
                JumpKind = row.JumpKind,
                JumpTarget = row.JumpTarget,
                FullIndexName = row.FullIndexName,
                Codes =
                [
                    row.CodeMalignantPrimary, row.CodeMalignantSecondary, row.CodeInSitu,
                    row.CodeBenign, row.CodeUncertain,
                ],
            };
        }

        var root = nodes.Values.FirstOrDefault(node => node.Level == 0);
        var rootSeqNo = root?.SeqNo ?? 0;
        foreach (var node in nodes.Values)
        {
            var chain = new List<string>();
            var parent = node.ParentSeqNo;
            while (parent != 0 && nodes.TryGetValue(parent, out var ancestor) && ancestor.SeqNo != rootSeqNo)
            {
                if (ancestor.CoreTerm.Length >= 1)
                {
                    chain.Add(ancestor.CoreTerm);
                }

                parent = ancestor.ParentSeqNo;
            }

            node.PathWords = [.. chain];
        }

        // 主导词族 = 1 级行本身（它自己的父行是表名行）。同族内的层级就是特指程度，
        // 跨族才需要并列（见 SameRank）。按层级由浅入深赋值，父行必先于子行完成。
        foreach (var node in nodes.Values.OrderBy(item => item.Level))
        {
            node.FamilySeqNo = node.Level <= 1
                ? node.SeqNo
                : nodes.TryGetValue(node.ParentSeqNo, out var parent) && parent.SeqNo != rootSeqNo
                    ? parent.FamilySeqNo
                    : node.SeqNo;
        }

        return new TumorIndexSnapshot(nodes, rootSeqNo);
    }

    /// <summary>
    /// 诊断用：与 TryResolveSiteCode 共用候选收集与排序键，逐行带出「该行为位列实际会发出的部位码」，
    /// 并标出与最优行同名次的并列集——用来回答「表里明明有这个部位，为什么解不出码」
    /// （并列行解出不同码时按口径放弃查表，不硬猜）。
    /// </summary>
    public IReadOnlyList<TumorSiteCandidate> DescribeCandidates(string inputText, string morphologyCode, int top = 10)
    {
        var inputKey = TextMatch.StripUncertaintyPhrases(inputText);
        var (column, digit) = ResolveBehaviorColumn(morphologyCode) ?? (PrimaryColumn, '3');
        var sorted = RankCandidates(CollectCandidates(inputKey));
        var best = sorted.Count > 0 ? sorted[0] : null;

        return sorted
            .Take(top)
            .Select(item => new TumorSiteCandidate(
                item.Node.SeqNo, item.Node.ParentSeqNo, item.Node.Level, item.Node.FullIndexName,
                item.Node.CoreTerm, item.Node.JumpKind, item.AliasScore, item.PathScore, item.AliasLength,
                best is not null && SameSiteRank(item, best), Emit(item.Node, column, digit, item.AliasText)?.Code))
            .ToList();
    }

    /// <summary>行为位 → 动态码列；不是合法形态学码时返回 null（调用方不查表）。</summary>
    private (int Column, char Digit)? ResolveBehaviorColumn(string morphologyCode)
    {
        var slash = morphologyCode.IndexOf('/');
        if (slash <= 0 || slash + 1 >= morphologyCode.Length
            || !_columnByBehavior.TryGetValue(morphologyCode[slash + 1], out var column))
        {
            return null;
        }

        return (column, morphologyCode[slash + 1]);
    }

    private List<Candidate> CollectCandidates(string inputKey)
    {
        var candidates = new List<Candidate>();
        foreach (var node in _nodes.Values)
        {
            if (node.SeqNo == RootSeqNo)
            {
                continue;
            }

            var alias = BestAlias(node, inputKey);
            if (alias is null)
            {
                continue;
            }

            candidates.Add(new Candidate(
                node, alias.Value.Score, alias.Value.Length, PathScore(node, inputKey), alias.Value.Key));
        }

        return candidates;
    }

    /// <summary>
    /// 候选排序键：匹配质量优先于层级——甲状腺 的行别名整词命中（1.0）必须赢过 颌－癌 的单字 癌（0.7），
    /// 按层级优先会把 C03.9 顶到 C73 前面。
    /// 同分先取本行带码的直接部位行，再取走 见/另见 的指针行：胃平滑肌肉瘤 的 肌(另见 结缔组织 C49.9)
    /// 与 胃(C16.9) 同分同层，病案写的是器官部位；乙状结肠 也靠这条赢过 结肠(另见 肠，大 C18.9)。
    /// 词面长度是"特指程度"：甲状腺乳头状癌 里 甲状腺(3字) 是部位，乳头(2字) 是形态学词，
    /// 同级同分并列时必须让长词面胜出，否则解不出码。
    /// </summary>
    private static List<Candidate> RankCandidates(List<Candidate> candidates) => candidates
        .OrderByDescending(item => item.AliasScore)
        .ThenBy(item => item.Node.JumpKind != 0)
        .ThenByDescending(item => item.PathScore)
        .ThenByDescending(item => item.AliasLength)
        .ThenByDescending(item => item.Node.Level)
        .ThenByDescending(item => item.Node.FullIndexName.Length)
        .ToList();

    /// <summary>
    /// 形态学码（M8510/3）+ 输入文本 → 肿瘤表部位码。
    /// 并列最优行解出不同部位码时不硬猜，但也不再一枪打死整张表：把并列的这几个部位码都交出去，
    /// 由调用方按字典门控裁决（哪个码族里有与输入逐字相等的标题，谁上 Top1）。
    /// </summary>
    public TumorSiteMatch? TryResolveSiteCode(string morphologyCode, string inputText)
        => ResolveSiteCodes(morphologyCode, inputText).FirstOrDefault();

    /// <summary>
    /// 形态学码 + 输入文本 → 肿瘤表部位码列表（按排序键名次，最多 maxCodes 个互不相同的码）。
    /// 单码时列表长度为 1；并列解出不同码时按名次给出并列族，让字典门控去分辨
    /// （颅底继发恶性肿瘤 的 颅◇=C79.5 与 颅(窝，任何)=C79.3 谁对，字典标题 颅底继发恶性肿瘤 挂在 C79.3 下）。
    /// 列值沿父链仍取不到、或输入没有部位词时返回空列表——空推荐好过错推荐。
    /// </summary>
    public IReadOnlyList<TumorSiteMatch> ResolveSiteCodes(
        string morphologyCode, string inputText, int maxCodes = 3)
    {
        if (ResolveBehaviorColumn(morphologyCode) is not (var column, var digit))
        {
            return [];
        }

        var inputKey = TextMatch.StripUncertaintyPhrases(inputText);
        if (inputKey.Length < 2)
        {
            return [];
        }

        var candidates = CollectCandidates(inputKey);
        if (candidates.Count == 0)
        {
            return [];
        }

        var sorted = RankCandidates(candidates);

        // 按排序键逐组裁决：同一组（并列）解出的码一致才采用；解出不同码说明病案信息不足以定位，放弃不硬猜。
        // 特例：整组一行码都解不出时（腮腺 的最优组是 淋巴－腺(继发性)－腮腺，这条转移分支只有 /6 码），
        // 允许沿"同一别名词面"的下一组继续找——换词面就不是同一个部位，放行会取到错码
        // （生殖器官继发恶性肿瘤 的次名组是 呼吸器官 C78.3、腹部器官 C79.8）。
        for (var from = 0; from < sorted.Count;)
        {
            var anchor = sorted[from];
            var group = sorted.Skip(from)
                .TakeWhile(item => SameSiteRank(item, anchor))
                .ToList();
            from += group.Count;

            var emitted = group
                .Select(item => Emit(item.Node, column, digit, item.AliasText))
                .Where(match => match is not null)
                .Select(match => match!)
                .ToList();
            if (emitted.Count > 0)
            {
                // 一致 → 一个码；不一致 → 把并列族按名次去重交给字典门控裁决，最多 maxCodes 个。
                var distinct = new List<TumorSiteMatch>();
                foreach (var match in emitted)
                {
                    if (distinct.All(item => !string.Equals(item.Code, match.Code, StringComparison.OrdinalIgnoreCase)))
                    {
                        distinct.Add(match);
                        if (distinct.Count >= maxCodes)
                        {
                            break;
                        }
                    }
                }

                return distinct;
            }

            if (from >= sorted.Count || !string.Equals(sorted[from].AliasText, anchor.AliasText, StringComparison.Ordinal))
            {
                return [];
            }
        }

        return [];
    }

    /// <summary>
    /// 没有部位语义的形态学码（M8000/3 恶性肿瘤、M8010/2 原位癌，部位词一个都锚不上表时）→
    /// 肿瘤表根本行该行为位列的"未特指部位"码（/3 C80、/2 D09.9、/0 D36.9、/1 D48.9）。
    /// 这就是肿瘤索引表对"只写了肿瘤、没写部位"的病案给出的答案，比交付 M80000/3 更符合编码口径。
    /// 不参与 <see cref="Emit"/> 的父链上溯（那条上溯明确止步于根行），只读根行自身的列值。
    /// </summary>
    public TumorSiteMatch? ResolveUnspecifiedSiteCode(string morphologyCode)
    {
        if (ResolveBehaviorColumn(morphologyCode) is not (var column, var digit))
        {
            return null;
        }

        if (!_nodes.TryGetValue(RootSeqNo, out var root) || root.Codes[column] is not { Length: > 0 } code)
        {
            return null;
        }

        return new TumorSiteMatch(root.SeqNo, code, root.FullIndexName, digit, ColumnNames[column]);
    }

    /// <summary>命中行 → 部位码：先跟 见 指针，再沿父链取该行为位列的值。
    /// aliasText 是把输入锚住该行的别名词面，随码一起交出（覆盖度记账用，不参与选码）。</summary>
    private TumorSiteMatch? Emit(Node node, int column, char digit, string aliasText)
    {
        var resolved = FollowPointer(node);
        if (resolved is null)
        {
            return null;
        }

        // 列值空沿父链上溯（编码惯例：特指部位未另列该动态码时继承更泛部位）。
        // 上溯不过表名行：0 级 肿瘤，肿瘤性 带的是 C80/D36.9「未特指」兜底码，
        // 让 内、底 这类方位词一路继承到它，会把 唇内 的 C00.9 顶掉。
        for (var current = resolved; current is not null && current.SeqNo != RootSeqNo;)
        {
            if (current.Codes[column] is { Length: > 0 } code)
            {
                return new TumorSiteMatch(current.SeqNo, code, current.FullIndexName, digit,
                    ColumnNames[column], aliasText);
            }

            current = current.ParentSeqNo != 0 && _nodes.TryGetValue(current.ParentSeqNo, out var parent)
                ? parent
                : null;
        }

        return null;
    }

    private Node? FollowPointer(Node node)
    {
        if (node.JumpKind == 0 || node.JumpTarget is not { Length: > 0 } target)
        {
            return node;
        }

        // "肿瘤，窦" / "肿瘤，唇，内"：剥掉表名后按段匹配，取父路径覆盖最好的目标行
        var cleaned = target.Trim().TrimStart('－', '-', '—');
        if (cleaned.StartsWith("肿瘤，", StringComparison.Ordinal))
        {
            cleaned = cleaned["肿瘤，".Length..];
        }

        var segments = cleaned.Split('，').Select(TextMatch.NormalizeKey).Where(s => s.Length > 0).ToArray();
        if (segments.Length == 0)
        {
            return null;
        }

        // 指针尾段可能是单字（肿瘤，唇，内 的 内）：单字只有在"就是尾段"或"以它收尾"时才算锚点，
        // 避免一个 内 字把消化道全树的行都锚进来。
        var tail = segments[^1];
        var matched = new List<Candidate>();
        foreach (var candidateNode in _nodes.Values)
        {
            if (candidateNode.SeqNo == RootSeqNo || candidateNode.JumpKind != 0)
            {
                continue;
            }

            var matchedAliases = candidateNode.Aliases
                .Where(alias => alias.MatchKey.Length >= tail.Length
                    && (tail.Length >= 2
                        ? alias.MatchKey.Contains(tail, StringComparison.Ordinal)
                        : alias.MatchKey == tail || alias.MatchKey.EndsWith(tail, StringComparison.Ordinal)))
                .Select(alias => alias.MatchKey.Length)
                .ToList();
            if (matchedAliases.Count == 0)
            {
                continue;
            }

            // 指针说的是主导词本身（另见 肿瘤，乳房 → 乳房 行）：词面等长的行优先于
            // 只是包含该词的行（乳房，皮肤 / 皮肤(乳房...)），否则一个 乳房 把整棵皮下树都锚进来。
            var isLeadWord = matchedAliases.Contains(tail.Length);
            var headCover = segments.Length > 1
                ? candidateNode.PathWords.Count(word => segments[..^1].Any(segment => segment.Contains(word, StringComparison.Ordinal)))
                    / (decimal)(segments.Length - 1)
                : 1m;
            matched.Add(new Candidate(
                candidateNode, isLeadWord ? 1.05m : 1m, matchedAliases.Min(), headCover, tail));
        }

        if (matched.Count == 0)
        {
            return null;
        }

        // 单段指针（另见 肿瘤，乳房）指向主导词本身 → 取最浅的 乳房 行，
        // 不能取 皮肤 树下的 －－乳房（否则 乳腺恶性肿瘤 解成 C44.5 皮肤癌）。
        // 多段指针（肿瘤，唇，内）自带亚部位限定 → 父段覆盖相同后取最深行。
        var top = matched
            .OrderByDescending(item => item.AliasScore)
            .ThenByDescending(item => item.PathScore)
            .ThenBy(item => segments.Length == 1 ? item.Node.Level : -item.Node.Level)
            .First();
        // 目标词在本表多处（如 窦 在鼻与副鼻窦）且父段无法区分 → 放弃。
        // 这里层级是歧义判据（见 SameRank 注释）：1 级 乳房 与 皮肤树下的 2 级 －－乳房 是不同器官，不能判成并列。
        return matched.Count(item => SamePointerRank(item, top)) == 1 ? top.Node : null;
    }

    /// <summary>
    /// 父路径覆盖率：祖先部位词必须出现在输入里才算特指部位。
    /// 肿瘤表的器官词大量是单字（唇、鼻、眼），"唇 皮肤" 这种 2 级行不能只凭 皮肤 就命中，
    /// 否则 皮肤基底细胞癌 会在 唇/眼/乳腺 的皮下树与 皮肤(非黑变病) 之间并列而放弃查表。
    /// 不做"别名吃字"的收窄（为 腮腺 试过）：腺/膜 这类父路径词本来就含在别名里，扣掉后
    /// 矢状窦脑膜瘤(D32.000X021)、结肠良性肿瘤(D12.600) 的特指行会掉到泛行之下，万名实测 Top1 -9。
    /// 腮腺 交给 TryResolveSiteCode 的"同词面次名组放行"。
    /// </summary>
    private static decimal PathScore(Node node, string inputKey)
    {
        if (node.PathWords.Length == 0)
        {
            return 1m;
        }

        var hit = node.PathWords.Count(word => inputKey.Contains(word, StringComparison.Ordinal));
        return hit / (decimal)node.PathWords.Length;
    }

    /// <summary>
    /// 剥掉索引的分类限定后缀（NEC / NOS，可连写）："喉 NEC" 的部位词就是 喉。
    /// 肿瘤表 1 级行大量是单字器官词，词面挂着 NEC 就让单字放行（CoreTerm==别名）失效，
    /// 喉恶性肿瘤 会一个候选都找不到。剥空了返回原词。
    /// </summary>
    private static string TrimClassifierSuffix(string text)
    {
        var current = text.Trim();
        while (current.Length >= 3
               && (current.EndsWith("nec", StringComparison.OrdinalIgnoreCase)
                   || current.EndsWith("nos", StringComparison.OrdinalIgnoreCase)))
        {
            var trimmed = current[..^3].TrimEnd(' ', '.', '（', '(').Trim();
            if (trimmed.Length == 0)
            {
                return current;
            }

            current = trimmed;
        }

        return current;
    }

    /// <summary>
    /// ICD-O 肿瘤表沿用的分区标记：◇ 标骨与关节、# 标皮肤与软组织。
    /// 它们既不算标点也不算空白，<see cref="TextMatch.NormalizeKey"/> 原样留在别名键里，
    /// 别名就成了 骨◇ / 颅◇ / 骶骨◇，输入写 骨 / 颅 / 骶骨 永远锚不上表（全表 100 行带标记）。
    /// </summary>
    private const string TableMarkerCharacters = "◇◆○●▲△※#＃†‡";

    private static string StripTableMarkers(string text)
        => string.Concat(text.Where(ch => !TableMarkerCharacters.Contains(ch))).Trim();

    private static IReadOnlyList<IcdIndexAlias> WithMarkerStrippedAliases(IReadOnlyList<IcdIndexAlias> aliases)
    {
        var result = new List<IcdIndexAlias>(aliases.Count);
        foreach (var alias in aliases)
        {
            var key = StripTableMarkers(alias.Key);
            if (key.Length == 0)
            {
                continue;
            }

            var matchKey = StripTableMarkers(alias.MatchKey);
            result.Add(alias with { Key = key, MatchKey = matchKey.Length > 0 ? matchKey : key });
        }

        return result;
    }

    /// <summary>
    /// 把"本行讲的部位词"补成一条满权重别名。ParseAliases 先按 [或，、] 切段、再剥成对括号，
    /// 所以 "颅(窝，任何)" 的别名集合只有 颅窝/任何，主导词本身 颅 从未进过别名；
    /// 颅底继发恶性肿瘤 因此一个部位候选都找不到（表里 C79.3 就挂在 颅(窝，任何) 行上）。
    /// CoreTerm 是括号与分区标记都剥干净后的词面，正是编码员读到的主导词。
    /// </summary>
    private static IReadOnlyList<IcdIndexAlias> WithCoreTermAlias(
        IReadOnlyList<IcdIndexAlias> aliases, string coreTerm)
    {
        var key = TextMatch.NormalizeKey(StripClassifierTokens(coreTerm));
        if (key.Length == 0
            || aliases.Any(alias => string.Equals(alias.MatchKey, key, StringComparison.Ordinal)))
        {
            return aliases;
        }

        return [.. aliases, new IcdIndexAlias(key, 1.0m, key)];
    }

    /// <summary>词面中间的 NEC/NOS 也是分类标记（关节 NEC (另见 肿瘤，骨)◇），整体剥掉；剥空则保留原词。</summary>
    private static string StripClassifierTokens(string text)
    {
        var stripped = ClassifierTokenRegex.Replace(TrimClassifierSuffix(text), " ").Trim();
        return stripped.Length == 0 ? text : stripped;
    }

    private static readonly System.Text.RegularExpressions.Regex ClassifierTokenRegex =
        new(@"(?i)\b(NEC|NOS)\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static IReadOnlyList<IcdIndexAlias> WithTrimmedClassifierAliases(IReadOnlyList<IcdIndexAlias> aliases)
    {
        var result = new List<IcdIndexAlias>(aliases.Count);
        foreach (var alias in aliases)
        {
            result.Add(alias);
            var trimmed = TrimClassifierSuffix(alias.MatchKey);
            if (trimmed.Length > 0
                && !string.Equals(trimmed, alias.MatchKey, StringComparison.Ordinal)
                && result.All(item => !string.Equals(item.MatchKey, trimmed, StringComparison.Ordinal)))
            {
                result.Add(alias with { Key = trimmed, MatchKey = trimmed });
            }
        }

        return result;
    }

    /// <summary>
    /// 行为词/泛词不是部位词：输入 "胃恶性肿瘤" 的 恶性 由形态学码本身携带，
    /// 让它当锚点会把 多发性，恶性 C97 顶到 胃 C16.9 前面（万条实测的肿瘤类输入几乎全部带 恶性/良性 字样）。
    /// </summary>
    private static readonly HashSet<string> NonSiteAnchorKeys =
    [
        "恶性", "良性", "原位", "转移", "继发", "继发于", "多发", "多发性", "未特指", "特指", "部位",
        "动态", "癌", "瘤", "恶性肿瘤", "良性肿瘤", "囊肿", "息肉", "增生",
    ];

    /// <summary>
    /// 节点词面与输入的匹配：主别名完整包含 1.0，去括号变体 0.9，单字部位词 0.7，未命中 null。
    /// 肿瘤表的器官主体大量是单字，拦住单字 胃恶性肿瘤 就查不到 C16.9；单字放行条件见下文。
    /// 降权保证多字部位词（甲状腺、乙状结肠）优先。
    /// 同分取最长词面（甲状腺 优于 甲状）：词面长度就是特指程度。
    /// </summary>
    private static (decimal Score, int Length, string Key)? BestAlias(Node node, string inputKey)
    {
        (decimal Score, int Length, string Key)? best = null;
        foreach (var alias in node.Aliases)
        {
            if (alias.MatchKey.Length == 0
                || NonSiteAnchorKeys.Contains(alias.MatchKey)
                || !inputKey.Contains(alias.MatchKey, StringComparison.Ordinal))
            {
                continue;
            }

            // 单字部位词放行：1 级行（－胃、－喉、－唇）本身就是器官主导词；
            // 更深层的单字（舌－根、唇－内）只有整条父路径都出现在输入里才算特指亚部位
            // （舌根 → C01，而不是 舌 → C02.9）。
            var singleSiteAllowed = alias.MatchKey.Length == 1
                && string.Equals(node.CoreTerm, alias.MatchKey, StringComparison.Ordinal)
                && (node.Level == 1
                    || (node.PathWords.Length > 0 && PathScore(node, inputKey) == 1m));
            if (alias.MatchKey.Length < 2 && !singleSiteAllowed)
            {
                continue;
            }

            var score = alias.MatchKey.Length >= 2
                ? (alias.Weight >= 1.0m ? 1m : 0.9m)
                : 0.7m;
            if (best is null
                || score > best.Value.Score
                || (score == best.Value.Score && alias.MatchKey.Length > best.Value.Length))
            {
                best = (score, alias.MatchKey.Length, alias.MatchKey);
            }
        }

        return best;
    }

    private sealed class Candidate(Node node, decimal aliasScore, int aliasLength, decimal pathScore, string aliasText)
    {
        public Node Node { get; } = node;

        public decimal AliasScore { get; } = aliasScore;

        public int AliasLength { get; } = aliasLength;

        public decimal PathScore { get; } = pathScore;

        /// <summary>本行锚定输入的词面；整组解不出码时只允许沿同一词面继续找。</summary>
        public string AliasText { get; } = aliasText;
    }

    /// <summary>排序键的公共前四键：匹配质量 / 直接行-指针行 / 父路径覆盖 / 词面长度。</summary>
    private static bool SameMatchKeys(Candidate left, Candidate right)
        => left.AliasScore == right.AliasScore
            && (left.Node.JumpKind != 0) == (right.Node.JumpKind != 0)
            && left.PathScore == right.PathScore
            && left.AliasLength == right.AliasLength;

    /// <summary>
    /// 指针目标歧义判据（另见 肿瘤，乳房 这类目标词在表里多处）：层级必须参与。
    /// 去掉它实测否决——乳腺/肌肉/滑膜 一族 6 行被 皮肤树下的 2 级 －－乳房 判成歧义，
    /// 指针跟不动就回退成裸形态学码或泛码（CAP Top1 +5/−6、JZEY 肿瘤子集 Top1 −2 且"该改查肿瘤表"面 48→70）。
    /// </summary>
    private static bool SamePointerRank(Candidate left, Candidate right)
        => SameMatchKeys(left, right) && left.Node.Level == right.Node.Level;

    /// <summary>
    /// 查表并列判据：同族（1 级主导词相同）时层级就是特指程度，口(1级 C06.9) 与 口－底(2级 C04.9)
    /// 必须分层，判成并列会把 C06.9 一起交给字典门控（口底/硬腭/小肠/直乙交界 实测掉码）；
    /// 跨族时层级只是表里谁挂得深，不是病案信息——会厌舌骨上良性肿瘤 的 骨◇－舌骨(2级 D16.4)
    /// 与 会厌(1级 D14.1) 判成并列都交出去，精确标题 会厌舌骨上良性肿瘤 才会在 D14.1 族里赢。
    /// </summary>
    private static bool SameSiteRank(Candidate left, Candidate right)
        => SameMatchKeys(left, right)
            && (left.Node.Level == right.Node.Level
                || left.Node.FamilySeqNo != right.Node.FamilySeqNo);
}
