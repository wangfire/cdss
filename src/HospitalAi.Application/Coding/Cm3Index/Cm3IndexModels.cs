using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Coding.Cm3Index;

/// <summary>
/// 手术通道的部位限定词提取。先把术式通用词 手术 从词面抹掉再抽部位：
/// <see cref="TextMatch.BodyQualifiers"/> 是单字词表（手/骨/肌/腺…），而
/// "X 手术（Y 术）" 是本库给人名术式的命名惯例（Noble 手术 / Bassett 手术 …），
/// 不抹掉就让 手 冒充部位限定词，整族 eponym 主导词被 NodeAllows 挡在召回之外
/// （夹具 seq 110 实测召不回）。真实部位写法 手部手术 抹掉尾段 手术 后仍留 手，不受影响。
/// </summary>
public static class Cm3SiteQualifiers
{
    public static HashSet<string> Extract(string? text)
        => TextMatch.ExtractQualifiers(string.IsNullOrEmpty(text) ? text : text.Replace("手术", string.Empty));

    /// <summary>标题声明了输入没有的部位词即冲突（口径同 <see cref="TextMatch.HasBodyPartConflict"/>，只是走本通道的词表）。</summary>
    public static bool Conflicts(string? title, HashSet<string> inputQualifiers)
    {
        var titleQualifiers = Extract(title);
        return titleQualifiers.Count > 0 && !titleQualifiers.IsSubsetOf(inputQualifiers);
    }
}

/// <summary>
/// 部位双字串表。<see cref="TextMatch.BodyQualifiers"/> 是单字词表，髌骨/尺骨/桡骨 在骨 面前互相相容，
/// 「部位+术式」整词的部位前缀因此判不出来。本表按实测词面抽出来：手术字典标题与 cm3_index 0 级行
/// 都写「部位+术式」，剥掉范围前缀（全/双侧/部分…）后的首双字串就是部位词，
/// 再只留确实能当 0 级锚点词头的条目，并按"D_CM3 13740 名 + 病历 2371 名"的暴露度剔掉
/// 术式动作串（转位/抽出/骨切…）与同族泛码串（回肠 之于 结肠：主导词部位不同但 17.33 本来同码，
/// 门控只会打掉正确答案）。抽取与剪枝配方见 logs/site_table.mjs、logs/site_prune.mjs、logs/site_final.mjs，
/// 终表 288 条。残留代价：血管/腹腔/椎管 这类上义或邻位词仍会挡掉"输入只写下位名"的行
/// （颈动脉造影术 被 血管 挡，两面上各丢 1 条命中），要治得给这些词标上下位关系，属数据侧活儿。
/// </summary>
public static class Cm3SiteTerms
{
    private const string HeadSiteText =
        "白内 半月 瓣环 瓣膜 膀胱 包皮 贲门 鼻唇 鼻甲 鼻镜 鼻咽 鼻中 扁桃 杓状 髌骨 并指 玻璃 苍白 肠肠 肠吻 齿槽 齿龈 耻骨 垂体 大脑 胆囊 胆总 镫骨 骶骨 蝶窦 "
        + "动静 动脉 对掌 额叶 腭裂 耳镜 耳前 耳蜗 二尖 房间 肺活 分娩 附睾 腹壁 腹部 腹股 腹膜 腹腔 腹式 肝胆 肛裂 肛瘘 肛门 肛周 睾丸 膈神 根尖 跟腱 巩膜 股四 "
        + "骨病 骨骼 骨关 骨膜 骨盆 骨折 鼓窦 鼓膜 鼓室 关节 龟头 喉镜 喉咽 后穹 后推 环甲 会厌 会阴 肌腱 肌肉 脊髓 脊柱 脊椎 甲状 肩峰 肩胛 睑板 剑突 腱肌 腱膜 "
        + "交感 焦痂 角膜 结肠 结膜 睫状 筋膜 晶状 精囊 精索 颈动 静脉 距骨 距下 开腹 空肠 空腔 口腔 髋臼 眶内 括约 拉德 阑尾 肋骨 肋间 肋软 泪点 泪囊 泪腺 淋巴 "
        + "流产 瘘管 颅底 颅骨 颅外 卵巢 马斯 帽状 门静 迷路 迷走 面部 拇指 囊肿 脑白 脑池 脑动 脑回 脑膜 脑室 内淋 内脏 黏膜 黏液 尿道 尿路 襻式 皮肤 皮下 脾门 "
        + "剖宫 奇静 气腹 气管 气脑 前房 前列 前庭 腔静 丘脑 全身 韧带 乳房 乳头 乳突 乳腺 软骨 腮腺 三叉 沙眼 筛窦 上颌 上消 舌沟 舌系 肾膀 肾部 肾上 肾输 肾盏 "
        + "声带 十二 食管 视网 室间 输精 输卵 输尿 死骨 松果 胎儿 胎盘 胎头 体积 听小 通道 瞳孔 头部 外耳 外阴 外展 外折 腕骨 腕掌 网膜 危象 尾骨 胃肠 胃底 胃镜 "
        + "胃空 胃十 系带 细胞 下颌 下消 纤维 涎管 涎腺 显微 陷凹 小梁 斜角 心包 心房 心肺 心肌 心尖 心室 心外 心血 心音 心脏 杏仁 胸骨 胸膜 胸腔 胸腺 血管 循环 "
        + "牙槽 牙齿 牙冠 牙髓 眼肌 眼睑 眼内 眼球 咽喉 咽镜 羊膜 咬合 胰管 胰十 胰腺 乙状 翼管 阴唇 阴道 阴蒂 阴茎 隐睾 隐窝 硬化 硬脑 幽门 粘连 砧镫 砧骨 支气 "
        + "脂肪 脂膜 直肠 跖骨 趾骨 中耳 周围 皱纹 主动 椎板 椎骨 椎管 椎间 椎体 子宫 籽骨 纵隔 坐骨";

    public static readonly IReadOnlySet<string> HeadSites =
        HeadSiteText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>输入声明了哪些部位双字串（键空间与 <see cref="HeadSites"/> 一致，规范化词面上做包含）。</summary>
    public static HashSet<string> Extract(string normalizedKey)
        => HeadSites.Where(site => normalizedKey.Contains(site, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
}

/// <summary>cm3_index 表行投影（平台级共享的手术操作索引库，19671 行，不按医院隔离）。</summary>
public sealed record Cm3IndexRow(
    int SeqNo,
    string IndexName,
    string? IndexCode,
    int IndexLevel,
    int ParentSeqNo,
    int LeadWordStatus);

/// <summary>手术索引库的 CM3 编码体系标识（与 medical_code.code_system_code 一致）。</summary>
public static class Cm3CodeSystem
{
    public const string DictionaryCodeSystem = "ICD-9-CM-3";
}

/// <summary>
/// 指针形态。CM3 实测只有这几种词面形态（方案 §2.3、本轮 sqlcmd 复核）：
/// <see cref="See"/> 532 条 0 级行（100% 空码）、<see cref="SeeAlso"/> 413 条（多为带码深层行）、
/// <see cref="SubEntry"/> 106 条（词面写 ―见亚目 N.N，且 index_code 已给出该亚目码）。
/// 诊断侧的括号形态 另见（2409 条）与 又称/现译名 在 CM3 库中为 0 条，不建对应判据。
/// </summary>
public enum Cm3JumpKind
{
    None,

    /// <summary>"X ―见 Y"：本行不编码，必须转到目标主导词（编码员口径 见-1）。</summary>
    See,

    /// <summary>"X ―另见 Y"：本行可留用，目标树里可能有更细码（编码员口径 另见-1）。</summary>
    SeeAlso,

    /// <summary>"X ―见亚目 17.4"：目标是**编码亚目**而非主导词，改道落在码展开而不是召回。</summary>
    SubEntry,
}

/// <summary>
/// CM3 索引树节点：词条文本在快照构建期解析完成（层级前缀、指针段、词面残留码、多别名拆分）。
/// </summary>
public sealed class Cm3IndexNode
{
    public required int SeqNo { get; init; }

    public required int Level { get; init; }

    public required int ParentSeqNo { get; init; }

    /// <summary>本行采用的索引编码（index_code 列为准；空码行解析词面残留的 <c>NN.NN[NN.NN]</c>）。</summary>
    public string? Code { get; init; }

    /// <summary>方括号双码 <c>57.87[45.51]</c> 的附加码：只解析、不出码（方案 §6.5-3，Q3 待拍板）。</summary>
    public string? AddOnCode { get; init; }

    /// <summary>剥离指针段与词面残留码后的词条文本。</summary>
    public required string Term { get; init; }

    /// <summary>匹配用别名（首段 1.0，并列尾段与方括号注解 0.85）。</summary>
    public required IReadOnlyList<Cm3IndexAlias> Aliases { get; init; }

    public Cm3JumpKind JumpKind { get; init; } = Cm3JumpKind.None;

    /// <summary>指针目标原文（"修补术,疝,腹股沟的"），无跳转为 null。</summary>
    public string? JumpTarget { get; init; }

    /// <summary>词面残留的亚目码（"骨―见 78.2 亚目缩短…"）：无码的 见亚目 行靠它取码。</summary>
    public string? SubEntryCode { get; init; }

    public List<Cm3IndexNode> Children { get; } = [];

    /// <summary>词条声明的部位限定词（召回与下钻门控用，口径同诊断侧 <see cref="TextMatch.ExtractQualifiers"/>）。</summary>
    public required HashSet<string> Qualifiers { get; init; }

    /// <summary>剥掉圆括号注解后的核心词（连接行判定用它）。</summary>
    public required string CoreTerm { get; init; }

    /// <summary>主导词状态列与词面判据不一致（本行无 见 指针却被标为无效主导词）。</summary>
    public bool LeadStatusDisagrees { get; init; }
}

/// <summary>
/// 匹配别名。MatchKey 为剥离泛化修饰括号后的键，下钻增益按它计算（口径同诊断侧）。
/// RecallAnchor=false 表示该键来自方括号注解或 或 尾段，不得单独作召回锚点，仍参与下钻。
/// </summary>
public sealed record Cm3IndexAlias(string Key, decimal Weight, string MatchKey)
{
    public bool RecallAnchor { get; set; } = true;
}

/// <summary>
/// cm3_index 内存快照：有效主导词 + 精确键定位表 + 失效主导词改道表 + 构建期诊断计数。
/// </summary>
public sealed class Cm3IndexSnapshot
{
    public required IReadOnlyList<Cm3IndexNode> Leads { get; init; }

    /// <summary>主导词任一变体键 → 节点（指针改道定位用）。</summary>
    public required IReadOnlyDictionary<string, Cm3IndexNode> LeadByExactKey { get; init; }

    /// <summary>失效主导词别名键 → 指针目标文本（"阿贝手术" → "Abbe 手术"）。</summary>
    public required IReadOnlyDictionary<string, string> Redirects { get; init; }

    public required int TotalNodes { get; init; }

    /// <summary>0 级行总数（含无效主导词行）。</summary>
    public required int Level0Rows { get; init; }

    /// <summary>词面判据与主导词状态列不一致的 0 级行数（实测 154 条，台账口径以词面为准）。</summary>
    public required int LeadStatusDisagreements { get; init; }

    /// <summary>命中"零子节点主导词"（实测 1945 条，其中 1391 条自带编码）的行数——本库的常态而非异常。</summary>
    public required int DeadEndLeads { get; init; }
}

/// <summary>cm3_index 快照来源（平台级共享，与 <c>IIcdIndexSource</c> 同一套单例+失效模式）。</summary>
public interface ICm3IndexSource
{
    Cm3IndexSnapshot GetSnapshot(CancellationToken cancellationToken = default);

    void Invalidate();
}

/// <summary>
/// 词条解析 + 快照构建（纯函数，Infrastructure 只负责把表读成行）。
/// </summary>
public static class Cm3IndexSnapshotBuilder
{
    /// <summary>
    /// 层级前缀与指针分隔符。除诊断侧那套横杠外必须包含 U+2015 一字线 ―：
    /// CM3 库的指针段 1201/1204 条写成 "―见"，照抄诊断侧字符集会整条认不出来。
    /// </summary>
    private static readonly char[] DashCharacters = ['―', '－', '-', '—', '–', '﹘', '─'];

    public static Cm3IndexSnapshot Build(IReadOnlyList<Cm3IndexRow> rows)
    {
        var nodes = new Dictionary<int, Cm3IndexNode>();
        foreach (var row in rows)
        {
            if (!nodes.ContainsKey(row.SeqNo))
            {
                nodes[row.SeqNo] = ParseNode(row);
            }
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

        var level0 = nodes.Values.Where(node => node.Level == 0).ToList();
        var leads = level0.Where(IsValidLead).ToList();
        var disagreements = level0.Count(node => node.LeadStatusDisagrees);
        var deadEnds = leads.Count(node => node.Children.Count == 0);

        var headAliasKeys = leads
            .SelectMany(lead => lead.Aliases)
            .Where(alias => alias.Weight >= 1.0m)
            .Select(alias => alias.Key)
            .ToHashSet(StringComparer.Ordinal);

        // 与诊断侧纪律 7 同判据，但按 CM3 词面收窄：0 级含 或 的只有 12 行（实测），
        // 尾段继承头段限定的形态在本库几乎不存在，只保留"尾段键在别处是头段键"这一条硬约束。
        foreach (var lead in leads)
        {
            foreach (var alias in lead.Aliases)
            {
                if (alias.Weight < 1.0m && alias.RecallAnchor && headAliasKeys.Contains(alias.Key))
                {
                    alias.RecallAnchor = false;
                }
            }
        }

        var leadByExact = new Dictionary<string, Cm3IndexNode>(StringComparer.Ordinal);
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
        foreach (var node in level0.Where(item => !IsValidLead(item)))
        {
            if (node.JumpKind is not (Cm3JumpKind.See or Cm3JumpKind.SeeAlso)
                || node.JumpTarget is not { Length: > 0 } target)
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

        return new Cm3IndexSnapshot
        {
            Leads = leads,
            LeadByExactKey = leadByExact,
            Redirects = redirects,
            TotalNodes = nodes.Count,
            Level0Rows = level0.Count,
            LeadStatusDisagreements = disagreements,
            DeadEndLeads = deadEnds,
        };
    }

    private static void SortByLevel(Cm3IndexNode node)
    {
        node.Children.Sort(static (left, right) => left.SeqNo.CompareTo(right.SeqNo));
        foreach (var child in node.Children)
        {
            SortByLevel(child);
        }
    }

    /// <summary>
    /// 主导词有效性双轨：词面判据（无 见 指针）为主、lead_word_status 列为辅。
    /// 实测 532 条 status=1 的 0 级行 100% 含"见"且 100% 空码（列就是指针行的标记），
    /// 但 0 级含"见"的共 686 条，其中 154 条列标为 0 —— 只信列会让这 154 条指针行自码对抗。
    /// 见亚目 行带码（106 条中 105 条 index_code 就是目标亚目码），按有效主导词处理。
    /// </summary>
    private static bool IsValidLead(Cm3IndexNode node) => node.JumpKind != Cm3JumpKind.See;

    /// <summary>
    /// 解析单行词条：剥行首层级横杠、剥指针段、取词面残留码、方括号与并列逗号拆别名。
    /// </summary>
    public static Cm3IndexNode ParseNode(Cm3IndexRow row)
    {
        var text = (row.IndexName ?? string.Empty).Trim();
        text = text.TrimStart(DashCharacters).Trim();

        var jumpKind = Cm3JumpKind.None;
        string? jumpTarget = null;
        string? subEntryCode = null;

        // 见亚目 必须在 见 之前判：`―见亚目 78.9` 按通用指针正则会切成目标 "亚目 78.9"，
        // 而编码员口径是"取本行码展开"，两种处理结果不同码。
        var subEntryMatch = SubEntryRegex.Match(text);
        if (subEntryMatch.Success)
        {
            jumpKind = Cm3JumpKind.SubEntry;
            // 正则有正反两种词面（"见亚目 78.9" / "见 78.2 亚目缩短,骨"），捕获组分属 1、2 两组。
            subEntryCode = FirstNonEmpty(subEntryMatch.Groups[1].Value, subEntryMatch.Groups[2].Value);
            text = text[..subEntryMatch.Index].Trim(DashCharacters).Trim();
        }
        else
        {
            var jumpMatch = JumpRegex.Match(text);
            if (jumpMatch.Success)
            {
                jumpKind = jumpMatch.Groups[1].Value == "另见" ? Cm3JumpKind.SeeAlso : Cm3JumpKind.See;
                jumpTarget = jumpMatch.Groups[2].Value.Trim();
                text = text[..jumpMatch.Index].Trim(DashCharacters).Trim();
            }
        }

        // 词面残留码：导入期已把 index_code 列拆出去，剩 13 行（实测）仍把码写在词面里，
        // 形态是方括号双码 `57.87[45.51]` 或英文行的 `00.162`。前者主码可用、附加码只存不出。
        string? mainCode = null;
        string? addOnCode = null;
        var codeMatch = TrailingCodeRegex.Match(text);
        if (codeMatch.Success)
        {
            mainCode = codeMatch.Groups[1].Value;
            addOnCode = codeMatch.Groups[2].Success ? codeMatch.Groups[2].Value : null;
            text = text[..codeMatch.Index].Trim(DashCharacters).Trim();
        }

        var code = FirstNonEmpty(row.IndexCode, mainCode, subEntryCode);
        var aliases = ParseAliases(text);
        var coreText = ParenthesisRegex.Replace(StripBrackets(text).WithoutBrackets, string.Empty);
        return new Cm3IndexNode
        {
            SeqNo = row.SeqNo,
            Level = row.IndexLevel,
            ParentSeqNo = row.ParentSeqNo,
            Code = code,
            AddOnCode = addOnCode,
            Term = text,
            Aliases = aliases,
            Qualifiers = Cm3SiteQualifiers.Extract(coreText),
            CoreTerm = coreText,
            JumpKind = jumpKind,
            JumpTarget = jumpTarget,
            SubEntryCode = subEntryCode,
            LeadStatusDisagrees = row.IndexLevel == 0
                && (row.LeadWordStatus == 1) != (jumpKind == Cm3JumpKind.See),
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    /// <summary>
    /// 别名生成（口径同诊断侧 <c>ParseAliases</c>，去掉 CM3 库中为 0 条的形态）：
    /// 并列段按 [或，,、] 切，首段 1.0、其余 0.85；圆括号生成含/不含两变体；
    /// 方括号内容（`[TGS]`、`[重组人活化蛋白 C]` 这类英文缩写注释）另存为 0.85 别名，
    /// 但禁止单独作召回锚点——它不是主导词，只是同一事物的另一种写法。
    /// NEC/NOS 是索引控制标记（未分类），剥离后保留主体词别名。
    /// </summary>
    public static IReadOnlyList<Cm3IndexAlias> ParseAliases(string text)
    {
        var result = new Dictionary<string, (decimal Weight, string MatchKey, bool Bracket)>();

        void Add(string value, decimal weight, bool bracket)
        {
            var key = TextMatch.NormalizeKey(value);
            if (key.Length < 1)
            {
                return;
            }

            var matchKey = TextMatch.NormalizeKey(GenericModifierParenRegex.Replace(value, string.Empty));
            if (matchKey.Length < 1)
            {
                matchKey = key;
            }

            if (!result.TryGetValue(key, out var existing) || weight > existing.Weight)
            {
                result[key] = (weight, matchKey, bracket);
            }
        }

        var (withoutBrackets, bracketContent) = StripBrackets(text);
        var segments = SegmentSeparatorRegex.Split(withoutBrackets.Trim())
            .Where(segment => segment.Length > 0)
            .Select(segment => NecTokenRegex.Replace(segment, string.Empty).Trim())
            .Where(segment => segment.Length > 0)
            .ToList();
        if (segments.Count == 0)
        {
            segments.Add(NecTokenRegex.Replace(withoutBrackets, string.Empty).Trim());
        }

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var weight = index == 0 ? 1.0m : 0.85m;
            Add(segment, weight, false);
            var stripped = ParenthesisRegex.Replace(segment, string.Empty).Trim();
            // 核心词长度可为 1："―胆囊" 去括号后仍是整词，而 "鼻（外）（内的）" 剥完只剩 鼻，
            // 单字部位词是索引 "主导词 → 部位" 下钻的合法目标，不能丢。
            if (stripped.Length >= 1 && stripped != segment)
            {
                Add(stripped, weight, false);
            }
        }

        foreach (var content in bracketContent)
        {
            Add(content, 0.85m, true);
        }

        return result
            .Select(pair => new Cm3IndexAlias(pair.Key, pair.Value.Weight, pair.Value.MatchKey)
            {
                RecallAnchor = !pair.Value.Bracket,
            })
            .ToList();
    }

    public static (string WithoutBrackets, List<string> BracketContent) StripBrackets(string text)
    {
        var bracketContent = SquareBracketRegex.Matches(text)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(content => content.Length > 0)
            .ToList();
        return (SquareBracketRegex.Replace(text, string.Empty), bracketContent);
    }

    /// <summary>
    /// 指针段：横杠（含 U+2015）+ 另见/见 + 目标。只认横杠形态——CM3 库中括号形态的
    /// "（另见 …）" 实测 0 条，不建诊断侧那条 ParenJumpRegex（纪律 5：不写没有对象的分支）。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex JumpRegex =
        new(@"\s*[-－—–―\s]\s*(另见|见)\s*(.+)$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// "―见亚目 78.9" 与反写形态 "―见 78.2 亚目缩短,骨或 78.3 亚目,延长,骨"（seq 4243）。
    /// 只取第一个亚目码：这类行本身就是"到该亚目下找"的入口，index_code 列通常已给出同一个码。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex SubEntryRegex =
        new(@"\s*[-－—–―\s]*见\s*亚目\s*([0-9]{2}\.[0-9]{1,3})|" +
            @"\s*[-－—–―\s]*见\s*([0-9]{2}\.[0-9]{1,3})\s*亚目",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 词面残留码：<c>NN.NN</c> 或 <c>NN.N</c>（索引编码列的两种形态），
    /// 可选方括号附加码 <c>57.87[45.51]</c>。必须锚在词面结尾，
    /// 否则会吃掉 "89.52" 之类正文数字。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex TrailingCodeRegex =
        new(@"\s*([0-9]{2}\.[0-9]{1,2})(?:\s*[［\[]\s*([0-9]{2}\.[0-9]{1,2})\s*[\]］])?\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex NecTokenRegex =
        new(@"(?i)\s*\b(NEC|NOS)\b\s*", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex GenericModifierParenRegex =
        new(@"[（(]\s*(?:慢性|急性|亚急性|单侧|双侧|部分|全部)\s*[）)]",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex SquareBracketRegex =
        new(@"[［\[]([^\]］]*)[\]］]", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static readonly System.Text.RegularExpressions.Regex ParenthesisRegex =
        new(@"[（(][^（()）]*[）)]", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>并列分隔符：CM3 词面同时用半角逗号（"―肠,肠 NEC"）与全角逗号。</summary>
    private static readonly System.Text.RegularExpressions.Regex SegmentSeparatorRegex =
        new(@"[或，,、]", System.Text.RegularExpressions.RegexOptions.Compiled);
}
