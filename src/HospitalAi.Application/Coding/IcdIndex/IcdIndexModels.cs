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

    /// <summary>
    /// 索引自带的"另一种写法"（"X - 又称 Y"、"X(又称 Y)"）里的 Y 列表。
    /// 全库 13 条 0 级行用它声明等价译名，其中最要命的一条是 "癫癎 - 又称 癫痫"：
    /// 索引主导词用字（异体字 癎）与临床写法（痫）不一致时，主导词根本召不回来，
    /// 癫痫 类输入只能靠 "杰克逊癫癎或发作" 的 发作 尾段跨行蒙码。
    /// </summary>
    public IReadOnlyList<string> AnotherNames { get; init; } = [];
}

/// <summary>
/// 匹配别名：Key 为完整归一化键；MatchKey 为剥离泛化修饰括号（慢性/急性/亚急性）后的键，
/// 下钻增益必须来自 MatchKey，防止 "(慢性)" 把下钻带进与输入无关的子树。
/// HuoTail 标记该键是否来自 "A或B" 式并列的 或 尾段（解析期事实）。
/// RecallAnchor 由快照构建期判定（见 IcdIndexSnapshotBuilder.Build），false 表示该键
/// 只是本行的一个并列尾段而非主导词，不得单独作召回锚点；下钻与链路覆盖度照旧使用它。
/// </summary>
public sealed record IcdIndexAlias(string Key, decimal Weight, string MatchKey, bool HuoTail = false)
{
    public bool RecallAnchor { get; set; } = true;
}

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

    /// <summary>
    /// "又称" 等价写法：临床变体词 → 索引主导词写法（"癫痫" → "癫癎"、"中风" → "卒中"）。
    /// 只登记不合并树：索引写的是异体字/旧译名时，输入侧补一路改写变体就够了。
    /// </summary>
    public required IReadOnlyDictionary<string, string> AnotherNames { get; init; }

    /// <summary>按 <see cref="AnotherNames"/> 改写输入文本，无等价词命中时返回空。</summary>
    public IReadOnlyList<string> RewriteByAnotherNames(string inputText)
    {
        if (inputText.Length < 2 || AnotherNames.Count == 0)
        {
            return [];
        }

        var rewritten = new List<string>();
        foreach (var (variant, canonical) in AnotherNames)
        {
            if (inputText.Contains(variant, StringComparison.Ordinal))
            {
                rewritten.Add(inputText.Replace(variant, canonical, StringComparison.Ordinal));
            }
        }

        return rewritten;
    }

    public required int TotalNodes { get; init; }
}

/// <summary>
/// 词条解析 + 快照构建（纯函数，Infrastructure 只负责加载行）。
/// </summary>
public static class IcdIndexSnapshotBuilder
{
    private static readonly char[] DashCharacters = ['－', '-', '—', '–', '﹘', '─'];

    /// <summary>
    /// 只作形态/病因属性、不作主导词的裸尾段词表（ICD 第三字符那一类）。
    /// 收录判据是万条实测的"占用召回名额"热族：先天性 315、遗传性 17、异常的 13、病理性 7。
    /// </summary>
    private static readonly HashSet<string> GenericAttributeTailKeys =
        new(StringComparer.Ordinal) { "先天性", "遗传性", "病理性", "异常的" };

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

        // 索引惯例 "A病或B" 的 或 尾段继承头段的人名限定："施莫尔病或结节" 讲的是施莫尔结节，
        // 尾段 结节 不是这一行的主导词。尾段若在别处另有自己的主导词行（结节 → 结节(性)、结(节)），
        // 就禁止它锚定本行的召回，去掉的是跨行噪声
        // （万条实测：左乳腺结节 的候选里混进 施莫尔病 M51.400、赫伯登病 M15.100；
        // 0 级行 "病或X" 式共 140 条，尾段 骨软骨病/综合征/发作 同理会跨行锚定）。
        // 两个豁免在 ParseAliases 里判：尾段词已在头段出现过（"蒙克贝格动脉硬化、变性、病或硬化"
        // 的核心名词就是 硬化）、或 在圆括号内（"NEC(胎儿或婴儿)"），都仍作合法锚点——
        // 实测少了这两条豁免会有 5 条输入丢掉既有推荐。逗号尾段不并入此口径，
        // 它是索引 "主导词，部位" 惯例的部位段，见 见/另见 纪律 6 的实测否决。
        var headAliasKeys = leads
            .SelectMany(lead => lead.Aliases)
            .Where(alias => alias.Weight >= 1.0m)
            .Select(alias => alias.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var alias in leads.SelectMany(lead => lead.Aliases))
        {
            if (alias.HuoTail && headAliasKeys.Contains(alias.Key))
            {
                alias.RecallAnchor = false;
            }

            // 索引把 "病名，先天性" / "病名(先天性)" 整条写成 0 级行时，尾段的 先天性 是形态属性
            // （ICD 第三字符），不是主导词：任何含这三个字的输入都会被它锚到毫不相干的病种行上，
            // 而且这些行靠"主导词自身带码"直接交付本行病种码。万条实测 345 个名的 top-8 里有这种行，
            // 233 个的索引 Top1 未命中期望码、146 个给的码根本不在先天/围生章节
            // （先天性鼻瘘→D71.X00X004 先天性吞噬细胞功能不良）。更糟的是它覆盖 3 个字，
            // 在"先按覆盖字数排序"的召回名额里整族压住真正的结尾核心词
            // （泌尿道之间的先天性瘘 的 瘘 L98.8→－泌尿道→Q64.9 落在第 32 名）。
            // 只关召回，别名集合/权重/下钻/链路覆盖度一律不动（同 或 尾段口径）。
            // 行头段本身就是该属性词（权重 1.0）不在射程内：41907 先天性 - 另见情况 是泛词兜底入口，
            // 实测 先天性畸形→Q89.900、鼻先天性畸形→Q30.900 都靠它下钻出来。
            if (alias.Weight < 1.0m && GenericAttributeTailKeys.Contains(alias.Key))
            {
                alias.RecallAnchor = false;
            }
        }

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

        // "又称" 等价表（只取主导词层）：临床写法与索引主导词用字不一致时，
        // 由 IcdIndexDiagnosisResolver 在输入侧补一路改写变体。
        // 实测：索引写 癫癎(7105 癫癎(性) G40.9)、病历写 癫痫，万条 8 条含 癫痫 的输入
        // 一条都召不到该主导词，只能靠 "杰克逊癫癎或发作" 的 发作 尾段跨行蒙码。
        var anotherNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in nodes.Values.Where(node => node.Level == 0 && node.AnotherNames.Count > 0))
        {
            var canonical = ExtractCanonicalName(node.Term);
            if (canonical.Length < 2)
            {
                continue;
            }

            foreach (var variant in node.AnotherNames)
            {
                if (!variant.Equals(canonical, StringComparison.Ordinal))
                {
                    anotherNames.TryAdd(variant, canonical);
                }
            }
        }

        return new IcdIndexSnapshot
        {
            Leads = leads,
            LeadByExactKey = leadByExact,
            Redirects = redirects,
            AnotherNames = anotherNames,
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
            else
            {
                // 库里 2409 行 "－剖宫产(另见 分娩，剖宫产)" 式指针写在结尾圆括号里（其中 2342 行自带编码，
                // 正是编码员口径的 另见-1"本行带编码"），横杠正则认不出来，指针等于不存在。
                // 只登记跳转语义、不动词面：括号段仍是别名与链路覆盖度的一部分，
                // 实测把 "(另见 X)" 从词面剥掉会让 20 条输入失去推荐（含 脑梗死、中毒 类命中），
                // 违反"不得因改道让输入失去推荐"的硬约束。
                var parenMatch = ParenJumpRegex.Match(text);
                if (parenMatch.Success)
                {
                    jumpKind = IndexJumpKind.SeeAlso;
                    jumpTarget = parenMatch.Groups[1].Value.Trim();
                    jumpTargetIsCode = ContainsCodeToken(jumpTarget);
                }
            }
        }

        // "又称" 是索引自带的等价译名声明（全库 13 条 0 级行），三种词面形态都要认：
        // 横杠形态 "癫癎 - 又称 癫痫" 剥掉指针段（留在词面只会造出 癫癎又称癫痫 这种永不命中的脏别名）；
        // 圆括号形态 "卒中(…)(又称 中风)" 只登记不剥词面，理由同 ParenJumpRegex（纪律 1）；
        // 嵌在 现译名 目标尾部的 "…，又称 布-加综合征" 也要摘出来。
        var anotherNames = new List<string>();
        if (jumpKind == IndexJumpKind.None && jumpTarget is null)
        {
            var dashAnother = DashAnotherNameRegex.Match(text);
            if (dashAnother.Success)
            {
                AddAnotherNames(anotherNames, dashAnother.Groups[1].Value);
                text = text[..dashAnother.Index].Trim(DashCharacters).Trim();
            }
        }

        foreach (System.Text.RegularExpressions.Match match in ParenAnotherNameRegex.Matches(text))
        {
            AddAnotherNames(anotherNames, match.Groups[1].Value);
        }

        if (jumpTarget is { Length: > 0 } jumpText)
        {
            var tailAnother = TailAnotherNameRegex.Match(jumpText);
            if (tailAnother.Success)
            {
                AddAnotherNames(anotherNames, tailAnother.Groups[1].Value);
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
            AnotherNames = anotherNames,
        };
    }

    private static void AddAnotherNames(List<string> target, string value)
    {
        foreach (var piece in AnotherNameSeparatorRegex.Split(value.Trim()))
        {
            var name = piece.Trim(DashCharacters).Trim();
            if (name.Length >= 2 && !target.Contains(name))
            {
                target.Add(name);
            }
        }
    }

    /// <summary>
    /// 本行声明的主导词写法：取第一个 逗号/顿号/括号/空白 之前的词段
    /// （raw_name 常把编码尾在词面里，如 "土拉菌病(又称 兔热病) A21.9"，切到括号即止）。
    /// </summary>
    private static string ExtractCanonicalName(string text)
    {
        var cut = text.Length;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is '，' or '、' or '（' or '(' or '［' or '[' or ' ' or '\t')
            {
                cut = index;
                break;
            }
        }

        return text[..cut].Trim(DashCharacters).Trim();
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

    /// <summary>
    /// 结尾圆括号里的 另见 指针（"(另见 分娩，剖宫产)"）。只认 另见 不认 见：
    /// "－输尿管(见于)"、"巨结肠(见于)(由于)" 这类尾巴会被误判成指针。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex ParenJumpRegex =
        new(@"[（(]\s*另见\s*(.+?)\s*[)）]\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex RedirectRegex =
        new(@"\s*[-－—–\s]*现译名\s*(.+)$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>"X - 又称 Y"：等价译名指针的横杠形态（"癫癎 - 又称 癫痫"）。</summary>
    private static readonly System.Text.RegularExpressions.Regex DashAnotherNameRegex =
        new(@"\s*[-－—–]\s*又称\s*(.+)$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>"X(又称 Y)"：等价译名的圆括号形态（"卒中(脑)(又称 中风)"）。</summary>
    private static readonly System.Text.RegularExpressions.Regex ParenAnotherNameRegex =
        new(@"[（(]\s*又称\s*([^()（）]*?)\s*[）)]",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>"…，又称 Y"：等价译名嵌在 现译名/见 目标串尾部。</summary>
    private static readonly System.Text.RegularExpressions.Regex TailAnotherNameRegex =
        new(@"[，、,]\s*又称\s*([^，、,]+)\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex AnotherNameSeparatorRegex =
        new(@"[，、,]",
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
        var result = new Dictionary<string, (decimal Weight, string MatchKey, bool HuoTail)>(StringComparer.Ordinal);

        void Add(string value, decimal weight, bool huoTail)
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
                result[key] = (weight, matchKey, huoTail && weight < 1.0m);
            }
        }

        var (withoutBrackets, bracketContent) = StripDecorations(text);

        // 切段口径与权重保持原样（[或，、] 都切、非首段 0.85），只是记住每段前面那个分隔符
        // 是不是**顶层** 或：顶层 或 的尾段继承头段的人名限定（"施莫尔病或结节" 的 结节），
        // 括号内的 或（"NEC(胎儿或婴儿)"）只是括注并列，不改变该段的别名地位。
        var mainSegments = new List<(string Value, bool HuoTail)>();
        var precedingText = string.Empty;
        var parenDepth = 0;
        var topLevelHuoCount = 0;
        var parts = SegmentSeparatorRegex.Split(withoutBrackets.Trim());
        for (var index = 0; index < parts.Length; index++)
        {
            // 捕获组切分的结果是 段/分隔符/段/分隔符/... 交替，奇数位是分隔符本身。
            if (index % 2 == 1)
            {
                if (parts[index] == "或" && parenDepth == 0)
                {
                    topLevelHuoCount++;
                }

                continue;
            }

            var segment = NecTokenRegex.Replace(parts[index], string.Empty).Trim();
            if (segment.Length > 0)
            {
                // 尾段词在 precedingText 里已经出现过（"蒙克贝格动脉硬化、变性、病或硬化" 的 硬化）
                // 说明本行的核心名词就是它，仍是合法锚点；没出现过（"施莫尔病或结节" 的 结节、
                // "杰克逊癫癎或发作" 的 发作）才是继承头段人名的第二名词。
                mainSegments.Add((segment, topLevelHuoCount > 0
                    && !precedingText.Contains(segment, StringComparison.Ordinal)));
            }

            precedingText += parts[index];
            foreach (var character in parts[index])
            {
                if (character is '（' or '(')
                {
                    parenDepth++;
                }
                else if (character is '）' or ')' && parenDepth > 0)
                {
                    parenDepth--;
                }
            }
        }

        if (mainSegments.Count == 0)
        {
            mainSegments.Add((NecTokenRegex.Replace(withoutBrackets, string.Empty).Trim(), false));
        }

        for (var index = 0; index < mainSegments.Count; index++)
        {
            var (segment, huoTail) = mainSegments[index];
            var weight = index == 0 ? 1.0m : 0.85m;
            Add(segment, weight, huoTail);
            var stripped = ParenthesisRegex.Replace(segment, string.Empty).Trim();
            // 核心词长度可为 1："－肺(活动性)(感染后)" 的裸部位词 肺 是索引 "主导词→部位" 
            // 下钻的合法目标，丢掉它会让 "右肺…钙化" 这类输入无处下钻。
            if (stripped.Length >= 1 && stripped != segment)
            {
                Add(stripped, weight, huoTail);
            }
        }

        foreach (var content in bracketContent)
        {
            Add(content, 0.85m, false);
        }

        return result
            .Select(pair => new IcdIndexAlias(pair.Key, pair.Value.Weight, pair.Value.MatchKey, pair.Value.HuoTail))
            .ToList();
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

    /// <summary>
    /// 并列分隔符，带捕获组以便 Split 把分隔符本身留在结果里（ParseAliases 需要知道
    /// 每段前面是 或 还是 逗号，以及它是否落在圆括号内）。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex SegmentSeparatorRegex =
        new(@"([或，、])", System.Text.RegularExpressions.RegexOptions.Compiled);
}
