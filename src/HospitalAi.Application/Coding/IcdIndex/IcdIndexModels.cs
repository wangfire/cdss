using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Coding.IcdIndex;

/// <summary>icd_index 表行投影（加载快照用，平台级共享，不按医院隔离）。</summary>
public sealed record IcdIndexRow(
    int SeqNo,
    string IndexName,
    string? IndexCode,
    int IndexLevel,
    int ParentSeqNo,
    int LeadWordStatus);

public enum IndexJumpKind
{
    None,

    /// <summary>"- 见"：本行不编码，必须跳转目标主导词重查。</summary>
    See,

    /// <summary>"- 另见"：本行可留用，目标主导词可能有更精确码（P1 仅作降权候选）。</summary>
    SeeAlso,

    /// <summary>"- 现译名 X"：失效译名重定向。</summary>
    Redirect,
}

/// <summary>
/// 索引树节点：词条文本已在快照构建期解析完成（层级前缀、见/另见、括号用途、多别名拆分）。
/// </summary>
public sealed class IcdIndexNode
{
    public required int SeqNo { get; init; }

    public required int Level { get; init; }

    public required int ParentSeqNo { get; init; }

    /// <summary>索引编码列原样值（7 种格式由 IndexCodeFormatParser 延迟解析），可为空。</summary>
    public string? Code { get; init; }

    /// <summary>剥离跳转段后的词条文本。</summary>
    public required string Term { get; init; }

    /// <summary>匹配用别名（归一化键，含权重：主别名 1.0，"或"次别名/方括号别名 0.85~0.9）。</summary>
    public required IReadOnlyList<IcdIndexAlias> Aliases { get; init; }

    public IndexJumpKind JumpKind { get; init; } = IndexJumpKind.None;

    /// <summary>跳转目标原文（"脓肿，阿米巴" 式逗号分段），无跳转为 null。</summary>
    public string? JumpTarget { get; init; }

    public List<IcdIndexNode> Children { get; } = [];

    /// <summary>本节点任一跳转（含括号内嵌另见）的目标是否指向编码类目（如 "另见E10-E14…"）而非主导词文本。</summary>
    public bool JumpTargetIsCode { get; init; }

    /// <summary>
    /// 核心词声明的部位/人群限定词（召回与下钻的部位门控用）。澄清圆括号不计入：
    /// "肠梗阻(结肠)(抑制性)"、"半月板(膝)(近期损伤)" 的括号是索引惯例的可选注解，
    /// 若当硬约束就会误杀 "肠梗阻待查"、"半月板撕裂" 这类部位自明的输入。
    /// </summary>
    public required HashSet<string> Qualifiers { get; init; }

    /// <summary>
    /// 剥掉全部括号注解后的核心词（"肥厚型(非梗阻性)"→"肥厚型"、"斜视(交替性)(非麻痹性)"→"斜视"）。
    /// 否定极门控用它判定：圆括号里的 非X 是该节点**默认码**的注解，不是节点的语义约束——
    /// "心肌病 → －肥厚型(非梗阻性) → －－梗阻性(I42.1)" 的第二层靠括号注明默认落 I42.2，
    /// 第三层的显式 梗阻性 才是输入要的；按整条 Term 判矛盾会把整棵子树连正确的 I42.100 一起打死。
    /// </summary>
    public required string CoreTerm { get; init; }
}

/// <summary>
/// 匹配别名：Key 为完整归一化键；MatchKey 为剥离泛化修饰括号（慢性/急性/亚急性）后的键，
/// 下钻增益必须来自 MatchKey，防止 "(慢性)" 把下钻带进与输入无关的子树。
/// </summary>
public sealed record IcdIndexAlias(string Key, decimal Weight, string MatchKey);

/// <summary>
/// ICD 索引树内存快照：有效主导词列表 + 精确键定位表 + 失效译名重定向表。
/// </summary>
public sealed class IcdIndexSnapshot
{
    public required IReadOnlyList<IcdIndexNode> Leads { get; init; }

    /// <summary>主导词任一变体键 → 节点，跳转/重定向定位用。</summary>
    public required IReadOnlyDictionary<string, IcdIndexNode> LeadByExactKey { get; init; }

    /// <summary>失效主导词别名键 → 重定向目标文本（"阿耳茨海默" → "阿尔茨海默"）。</summary>
    public required IReadOnlyDictionary<string, string> Redirects { get; init; }

    public required int TotalNodes { get; init; }
}

/// <summary>
/// 词条解析 + 快照构建（纯函数，Infrastructure 只负责加载行）。
/// </summary>
public static class IcdIndexSnapshotBuilder
{
    private static readonly char[] DashCharacters = ['－', '-', '—', '–', '﹘', '─'];

    public static IcdIndexSnapshot Build(IReadOnlyList<IcdIndexRow> rows)
    {
        var nodes = new Dictionary<int, IcdIndexNode>();
        foreach (var row in rows)
        {
            if (nodes.ContainsKey(row.SeqNo))
            {
                continue;
            }

            nodes[row.SeqNo] = ParseNode(row);
        }

        foreach (var node in nodes.Values)
        {
            if (node.ParentSeqNo != 0
                && nodes.TryGetValue(node.ParentSeqNo, out var parent)
                && !ReferenceEquals(parent, node))
            {
                parent.Children.Add(node);
            }
        }

        var leads = nodes.Values
            .Where(node => node.Level == 0 && IsValidLead(node))
            .ToList();

        var leadByExact = new Dictionary<string, IcdIndexNode>(StringComparer.Ordinal);
        foreach (var lead in leads)
        {
            foreach (var alias in lead.Aliases)
            {
                if (!leadByExact.ContainsKey(alias.Key))
                {
                    leadByExact[alias.Key] = lead;
                }
            }

            var termKey = TextMatch.NormalizeKey(lead.Term);
            if (termKey.Length > 0 && !leadByExact.ContainsKey(termKey))
            {
                leadByExact[termKey] = lead;
            }
        }

        var redirects = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in nodes.Values.Where(node => node.Level == 0 && !IsValidLead(node)))
        {
            if (node.JumpKind != IndexJumpKind.Redirect && node.JumpKind != IndexJumpKind.See)
            {
                continue;
            }

            if (node.JumpTarget is not { Length: > 0 } target)
            {
                continue;
            }

            foreach (var alias in node.Aliases)
            {
                redirects[alias.Key] = target;
            }
        }

        foreach (var node in nodes.Values)
        {
            SortByLevel(node);
        }

        return new IcdIndexSnapshot
        {
            Leads = leads,
            LeadByExactKey = leadByExact,
            Redirects = redirects,
            TotalNodes = nodes.Count,
        };
    }

    private static void SortByLevel(IcdIndexNode node)
    {
        node.Children.Sort(static (left, right) => left.SeqNo.CompareTo(right.SeqNo));
        foreach (var child in node.Children)
        {
            SortByLevel(child);
        }
    }

    private static bool IsValidLead(IcdIndexNode node) => node.JumpKind is IndexJumpKind.None or IndexJumpKind.SeeAlso;

    /// <summary>
    /// 解析单行词条：剥离行首"－"层级符与跳转段（见/另见/现译名），
    /// 方括号与澄清圆括号生成别名变体，"或/，"拆分多别名。
    /// </summary>
    public static IcdIndexNode ParseNode(IcdIndexRow row)
    {
        var text = (row.IndexName ?? string.Empty).Trim();
        text = text.TrimStart(DashCharacters).Trim();

        var jumpKind = IndexJumpKind.None;
        string? jumpTarget = null;
        var jumpTargetIsCode = false;

        var redirectMatch = RedirectRegex.Match(text);
        if (redirectMatch.Success)
        {
            jumpKind = IndexJumpKind.Redirect;
            jumpTarget = redirectMatch.Groups[1].Value.Trim();
            text = text[..redirectMatch.Index].Trim(DashCharacters).Trim();
        }
        else
        {
            var jumpMatch = JumpRegex.Match(text);
            if (jumpMatch.Success)
            {
                jumpKind = jumpMatch.Groups[1].Value == "另见" ? IndexJumpKind.SeeAlso : IndexJumpKind.See;
                jumpTarget = jumpMatch.Groups[2].Value.Trim();
                jumpTargetIsCode = ContainsCodeToken(jumpTarget);
                text = text[..jumpMatch.Index].Trim(DashCharacters).Trim();
            }
        }

        var aliases = ParseAliases(text);
        var coreText = ParenthesisRegex.Replace(StripDecorations(text).WithoutBrackets, string.Empty);
        return new IcdIndexNode
        {
            SeqNo = row.SeqNo,
            Level = row.IndexLevel,
            ParentSeqNo = row.ParentSeqNo,
            Code = string.IsNullOrWhiteSpace(row.IndexCode) ? null : row.IndexCode.Trim(),
            Term = text,
            Aliases = aliases,
            Qualifiers = ExtractCoreQualifiers(coreText),
            CoreTerm = coreText,
            JumpKind = jumpKind,
            JumpTarget = jumpTarget,
            JumpTargetIsCode = jumpTargetIsCode,
        };
    }

    /// <summary>
    /// "或"式并列别名的限定词取各段交集：主导词 "戈谢病或戈谢脾大" 的第二个别名带 脾，
    /// 若按整条词取并集，输入 "戈谢病" 就会被部位门控判成"未声明 脾"而整节点剔除。
    /// 逗号不在此列——"断裂，视网膜" 的逗号是索引的"主导词，部位"惯例，部位词是真约束。
    /// </summary>
    private static HashSet<string> ExtractCoreQualifiers(string coreText)
    {
        var segments = coreText.Split('或')
            .Select(segment => NecTokenRegex.Replace(segment, string.Empty).Trim())
            .Where(segment => segment.Length > 0)
            .ToList();
        if (segments.Count <= 1)
        {
            return TextMatch.ExtractQualifiers(coreText);
        }

        var common = TextMatch.ExtractQualifiers(segments[0]);
        foreach (var segment in segments.Skip(1))
        {
            common.IntersectWith(TextMatch.ExtractQualifiers(segment));
        }

        return common;
    }

    private static readonly System.Text.RegularExpressions.Regex JumpRegex =
        new(@"\s*[-－—–\s]\s*(另见|见)\s*(.+)$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex RedirectRegex =
        new(@"\s*[-－—–\s]*现译名\s*(.+)$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool ContainsCodeToken(string? text) =>
        text is not null
        && System.Text.RegularExpressions.Regex.IsMatch(text, @"[A-Z]\d{2}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 别名生成：方括号内容并入主词、圆括号生成含/不含两变体、
    /// "或/，、"拆分并列别名（首别名权重 1.0，其余 0.85）。
    /// NEC/NOS 为索引控制标记（未分类/不可另分类），剥离后保留主体词别名。
    /// </summary>
    public static IReadOnlyList<IcdIndexAlias> ParseAliases(string text)
    {
        var result = new Dictionary<string, (decimal Weight, string MatchKey)>(StringComparer.Ordinal);

        void Add(string value, decimal weight)
        {
            var key = TextMatch.NormalizeKey(value);
            if (key.Length < 1)
            {
                return;
            }

            var matchKey = TextMatch.NormalizeKey(GenericModifierParenRegex.Replace(value, string.Empty));
            if (matchKey.Length < 1)
            {
                // 整词即泛化修饰词（如子线 "－慢性"）时保留原键，允许其自身作为下钻目标。
                matchKey = key;
            }

            if (!result.TryGetValue(key, out var existing) || weight > existing.Weight)
            {
                result[key] = (weight, matchKey);
            }
        }

        var (withoutBrackets, bracketContent) = StripDecorations(text);

        var mainSegments = SegmentSeparatorRegex.Split(withoutBrackets.Trim())
            .Select(segment => NecTokenRegex.Replace(segment, string.Empty).Trim())
            .Where(segment => segment.Length > 0)
            .ToList();
        if (mainSegments.Count == 0)
        {
            mainSegments.Add(NecTokenRegex.Replace(withoutBrackets, string.Empty).Trim());
        }

        for (var index = 0; index < mainSegments.Count; index++)
        {
            var segment = mainSegments[index];
            var weight = index == 0 ? 1.0m : 0.85m;
            Add(segment, weight);
            var stripped = ParenthesisRegex.Replace(segment, string.Empty).Trim();
            // 核心词长度可为 1："－肺(活动性)(感染后)" 的裸部位词 肺 是索引 "主导词→部位" 
            // 下钻的合法目标，丢掉它会让 "右肺…钙化" 这类输入无处下钻。
            if (stripped.Length >= 1 && stripped != segment)
            {
                Add(stripped, weight);
            }
        }

        foreach (var content in bracketContent)
        {
            Add(content, 0.85m);
        }

        return result.Select(pair => new IcdIndexAlias(pair.Key, pair.Value.Weight, pair.Value.MatchKey)).ToList();
    }

    /// <summary>剥离方括号（内容另存）与括号内 见/另见 指令、形态学码括号。</summary>
    public static (string WithoutBrackets, List<string> BracketContent) StripDecorations(string text)
    {
        var bracketContent = SquareBracketRegex.Matches(text)
            .Select(match => match.Groups[1].Value)
            .ToList();
        var withoutBrackets = SquareBracketRegex.Replace(text, string.Empty);
        withoutBrackets = ParenthesisedDirectiveRegex.Replace(withoutBrackets, string.Empty);
        withoutBrackets = MorphologyParenthesisRegex.Replace(withoutBrackets, string.Empty);
        return (withoutBrackets, bracketContent);
    }

    private static readonly System.Text.RegularExpressions.Regex NecTokenRegex =
        new(@"(?i)\s*\b(NEC|NOS)\b\s*", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex GenericModifierParenRegex =
        new(@"[（(]\s*(?:慢性|急性|亚急性)\s*[）)]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex SquareBracketRegex =
        new(@"[［\[]([^\]］]*)[\]］]", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static readonly System.Text.RegularExpressions.Regex ParenthesisRegex =
        new(@"[（(][^（()）]*[）)]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex ParenthesisedDirectiveRegex =
        new(@"[（(][^（()）]*(?:另?见)[^（()）]*[）)]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex MorphologyParenthesisRegex =
        new(@"[（(]\s*M\d{4}\s*/\s*\d\s*[）)]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex SegmentSeparatorRegex =
        new(@"[或，、]", System.Text.RegularExpressions.RegexOptions.Compiled);
}
