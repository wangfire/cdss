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
    /// 链路终点的证据极性（P0 极性标签）：从节点名推导——父节点名含"确定的"→其子行为阳性；
    /// 节点名含"阴性"/"未做"/"不伴有…证实"→阴性/未做/未证实；无匹配→Unspecified。
    /// 用于输入含阳性/阴性证据词时，阻止阴性输入落阳性分支（语义反转防护）。
    /// 可写：快照构建期按父链继承补标（init-only 无法二次赋值）。
    /// </summary>
    public string Polarity { get; set; } = "Unspecified";

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
/// CommaTail 标记该键是否来自 "A，B" 式并列的 逗号/顿号 尾段（索引 "主导词，部位" 惯例的
/// 部位/限定段，如 "小梁形成，膀胱" 的 膀胱、"断裂，视网膜" 的 视网膜）——它不是核心词，
/// "编码录入"拆分不得以它作主导词锚（流水线召回不受影响：部位段仍作限定词参与门控）。
/// ParenTail 标记该键是否来自括注内的切段（"尿道结石病(伴有绞痛或感染)" 的 感染）——
/// 流水线照旧作召回锚（校准口径），"编码录入"拆分排除。
/// RecallAnchor 由快照构建期判定（见 IcdIndexSnapshotBuilder.Build），false 表示该键
/// 只是本行的一个并列尾段而非主导词，不得单独作召回锚点；下钻与链路覆盖度照旧使用它。
/// </summary>
public sealed record IcdIndexAlias(string Key, decimal Weight, string MatchKey, bool HuoTail = false)
{
    public bool RecallAnchor { get; set; } = true;

    public bool CommaTail { get; init; }

    public bool ParenTail { get; init; }

    /// <summary>本键是第一个括号前的头段词残段（"脑(脊)膜瘤" 的 脑），不是本行完整的词。</summary>
    public bool HeadWord { get; init; }
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

    /// <summary>
    /// "现译名" 等价（"发育迟滞 - 现译名 发育迟缓" 的 发育迟滞 → 发育迟缓，全库 146 条 0 级行）。
    /// 与 <see cref="AnotherNames"/> 分开登记：流水线 resolver 对这类输入已有"召回全空后按目标
    /// 重试一次"的兜底（IcdIndexDiagnosisResolver），混进来会改变流水线召回时机；目前只有
    /// "编码录入"面板引擎消费（输入侧改写，编码员口径：查 旧名 即查 现译名 的主导词）。
    /// </summary>
    public required IReadOnlyDictionary<string, string> RenameEquivalents { get; init; }

    /// <summary>按 <see cref="AnotherNames"/> 改写输入文本，无等价词命中时返回空。</summary>
    public IReadOnlyList<string> RewriteByAnotherNames(string inputText)
        => RewriteByEquivalents(AnotherNames, inputText);

    /// <summary>按 <see cref="RenameEquivalents"/> 改写输入文本（旧译名 → 现译名），无命中返回空。</summary>
    public IReadOnlyList<string> RewriteByRenameEquivalents(string inputText)
        => RewriteByEquivalents(RenameEquivalents, inputText);

    private IReadOnlyList<string> RewriteByEquivalents(IReadOnlyDictionary<string, string> equivalents, string inputText)
    {
        if (inputText.Length < 2 || equivalents.Count == 0)
        {
            return [];
        }

        var rewritten = new List<string>();
        foreach (var (variant, canonical) in equivalents)
        {
            // 等价词必须独立成词出现才改写：出现位置若与前/后邻字能拼成另一条**有效主导词**
            // （胆+囊炎=胆囊炎 ∈ LeadByExactKey），说明它只是长词的内部子串——伤寒并发胆囊炎
            // 的 囊炎 曾被裸 Replace 改写成 胆滑囊炎，凭空造出 滑囊炎 主导词命中（11-07 用户口径）。
            if (!OccursAsStandaloneWord(inputText, variant))
            {
                continue;
            }

            rewritten.Add(inputText.Replace(variant, canonical, StringComparison.Ordinal));
        }

        return rewritten;
    }

    /// <summary>
    /// 等价词是否存在独立出现：任一出现位置若前/后邻字都拼不出更长有效主导词键，
    /// 该次出现即独立成词；全部出现都是长词内部子串时不改写。
    /// </summary>
    private bool OccursAsStandaloneWord(string inputText, string variant)
    {
        var start = 0;
        while ((start = inputText.IndexOf(variant, start, StringComparison.Ordinal)) >= 0)
        {
            var end = start + variant.Length;
            var insideBefore = start > 0 && LeadByExactKey.ContainsKey(inputText[(start - 1)..end]);
            var insideAfter = end < inputText.Length && LeadByExactKey.ContainsKey(inputText[start..(end + 1)]);
            if (!insideBefore && !insideAfter)
            {
                return true;
            }

            start += 1;
        }

        return false;
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

    /// <summary>
    /// 从 "核心词+属性尾段" 的复合别名键里剥出核心词（裂先天性 → 裂）。
    /// 裸属性键（先天性）不在此口径，仍由 RecallAnchor 的整键门控处理。
    /// </summary>
    internal static bool TrySplitGenericAttributeTail(string key, out string core)
    {
        foreach (var attribute in GenericAttributeTailKeys)
        {
            if (key.Length > attribute.Length
                && key.EndsWith(attribute, StringComparison.Ordinal))
            {
                core = key[..^attribute.Length];
                return true;
            }
        }

        core = string.Empty;
        return false;
    }

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

        // 极性继承（P0，双向）：
        // ① 自顶向下——子节点名无极性词时继承父链极性（父在"确定的"下 → 子为阳性分支）；
        // ② 自底向上——节点自身无极性词、但其下挂了阳性分支（"确定的"）时，
        //    该节点本身是"未证实的分叉点"，极性记为 Negative——
        //    肺(空洞性) A16.2 的下级是 确定的(阳性)/不伴有证实(阴性)，A16.2 语义是"未证实"，
        //    不继承则阳性输入落 A16.2 无法被 R2 剔除（用户案例 2026-11-16）。
        foreach (var node in nodes.Values.OrderBy(node => node.Level))
        {
            if (node.Polarity != "Unspecified")
            {
                continue;
            }

            var parentPolarity = "Unspecified";
            var current = node;
            var guard = 0;
            while (current.ParentSeqNo != 0
                && nodes.TryGetValue(current.ParentSeqNo, out var parentNode)
                && !ReferenceEquals(parentNode, current)
                && guard++ < 8)
            {
                if (parentNode.Polarity != "Unspecified")
                {
                    parentPolarity = parentNode.Polarity;
                    break;
                }

                current = parentNode;
            }

            if (parentPolarity != "Unspecified")
            {
                node.Polarity = parentPolarity;
            }
        }

        // ② 分叉点标注：有阳性子分支、自身无极性词的节点 → Negative（未证实分叉点）。
        foreach (var node in nodes.Values)
        {
            if (node.Polarity != "Unspecified" || node.Children.Count == 0)
            {
                continue;
            }

            var hasPositiveChild = node.Children.Any(child => child.Polarity == "Positive"
                && !child.CoreTerm.Contains("细菌学和组织学证实", StringComparison.OrdinalIgnoreCase));
            if (hasPositiveChild)
            {
                node.Polarity = "Negative";
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
        // "现译名" 等价（旧译名 → 现译名）：与 Redirects（见/现译名共用）分开登记，
        // 只供面板引擎输入侧改写。目标按转示目标同款惯例取首段（巴德-恰里 - 现译名
        // 巴德-基亚里或巴德-吉亚利，又称 布-加综合征 → 巴德-基亚里或巴德-吉亚利），
        // 又称 尾段照旧归 AnotherNames 通道。
        var renameEquivalents = new Dictionary<string, string>(StringComparer.Ordinal);
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

            if (node.JumpKind == IndexJumpKind.Redirect)
            {
                var renameTarget = TailAnotherNameRegex.Match(target) is { Success: true, Index: var tailIndex }
                    ? target[..tailIndex].Trim().TrimEnd(DashCharacters).Trim()
                    : target;
                renameTarget = renameTarget
                    .Split(['，', ','], StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.Trim() ?? string.Empty;
                if (renameTarget.Length >= 2)
                {
                    TryAddRenameVariant(renameEquivalents, node.Term, renameTarget);
                    var strippedTerm = ParenthesisRegex.Replace(node.Term, string.Empty).Trim();
                    if (strippedTerm != node.Term)
                    {
                        TryAddRenameVariant(renameEquivalents, strippedTerm, renameTarget);
                    }
                }
            }

            foreach (var alias in node.Aliases)
            {
                // 0 级指针行的圆括号内容不是这行的词（用户口径 11-06）：
                // "放射性皮肤烧伤(急性、慢性或职业性) - 见 烧伤" 的括号段 慢性 入改道键后，
                // 编码录入门把 慢性丁型肝炎 拆出的 慢性 改道到 烧伤，交付 T30.000。
                // 括号前词面（放射性皮肤烧伤）仍是键；非 0 级行括号内容照旧
                // （沙门菌(亚利桑那)(猪霍乱)(小肠炎)(鼠伤寒) 的 亚利桑那）。
                if (alias.ParenTail)
                {
                    continue;
                }

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

        // 用户口径（2026-11-19）：涂阳培阳/涂阴培阳 是"已做细菌学和组织学证实"的检验结果写法，
        // 与 支气管结核(细菌学和组织学证实) 同义。作为**输入侧文本改写**注入（原文照跑、变体补一路），
        // 不走 icd_term_synonym 等价类——后者会让 痰培养(+)/培阳 同组连坐，污染肺树选层。
        // OccursAsStandaloneWord 保证只在整词命中时改写，不误伤长词内部子串。
        renameEquivalents.TryAdd("涂阳培阳", "细菌学和组织学证实");
        renameEquivalents.TryAdd("涂阴培阳", "细菌学和组织学证实");
        renameEquivalents.TryAdd("未做细菌学和组织学检查", "未做");
        // 阴性证据族：痰镜检(-)/痰培养(-)/病理(-)/细胞学(组织学)(-) 均为"细菌学和组织学检查为阴性"(A16.0)。
        renameEquivalents.TryAdd("痰镜检(-)", "阴性");
        renameEquivalents.TryAdd("痰培养(-)", "阴性");
        renameEquivalents.TryAdd("病理(-)", "阴性");
        renameEquivalents.TryAdd("细胞学(组织学)(-)", "阴性");
        // 转示部位（气胸/支气管扩张）在索引树里是"见 结核，肺"转示行，本身无证据子行；
        // 输入侧补一路"部位→肺"改写变体，使结核主导词下钻进肺树证据行(A16.0/A16.1)；
        // 原文照跑保证字典展开仍按原标题命中部位细码（A16.004 等）。
        renameEquivalents.TryAdd("结核性气胸", "肺结核");
        renameEquivalents.TryAdd("结核性支气管扩张", "肺结核");
        // 甲肝：输入前缀"急性"会被病毒层"急性(未特指)"(B17.9)抢层，挡住"型→甲"精确链。
        // 甲肝本身即急性，去掉前缀语义无损；只针对完整"急性甲型"整词改写。
        renameEquivalents.TryAdd("急性甲型", "甲型");
        renameEquivalents.TryAdd("急性黄疸型甲型", "黄疸型甲型");
        renameEquivalents.TryAdd("急性淤胆型甲型", "淤胆型甲型");
        renameEquivalents.TryAdd("急性无黄疸型甲型", "无黄疸型甲型");
        // 慢性病毒性肝炎：主导词切分要求"肝炎"在结尾才召回；"病毒性肝炎慢性X"里肝炎在中间
        // 被 DropMidWordFragments 丢弃。调语序把肝炎挪到结尾（原文照跑，字典展开按原标题命中细码）。
        renameEquivalents.TryAdd("病毒性肝炎慢性重型", "慢性重型病毒性肝炎");
        renameEquivalents.TryAdd("病毒性肝炎慢性淤胆型", "慢性淤胆型病毒性肝炎");
        // 病毒性肝炎感染族：索引里"丁型/庚型/重叠感染…"归在"非甲非乙型"(B17.8)下，
        // 但诊断词面不含该锚点→下钻停在病毒层或被"急性(未特指)"抢层。补"非甲非乙型"锚点。
        renameEquivalents.TryAdd("重叠感染", "非甲非乙型重叠感染");
        renameEquivalents.TryAdd("重叠型", "非甲非乙型重叠型");
        renameEquivalents.TryAdd("双重感染", "非甲非乙型双重感染");
        renameEquivalents.TryAdd("三重感染", "非甲非乙型三重感染");
        renameEquivalents.TryAdd("混合感染", "非甲非乙型混合感染");
        renameEquivalents.TryAdd("丁型病毒性肝炎", "非甲非乙型丁型病毒性肝炎");
        renameEquivalents.TryAdd("庚型病毒性肝炎", "非甲非乙型庚型病毒性肝炎");
        // "急性重症庚型肝炎"词面缺"病毒"，下钻被"急性NEC"B17.9抢层；补"病毒性"锚点
        // 连同上条规则走 肝炎→病毒→型→非甲非乙。整词改写避免误伤"慢性庚型肝炎"。
        renameEquivalents.TryAdd("急性重症庚型肝炎", "急性重症庚型病毒性肝炎");

        return new IcdIndexSnapshot
        {
            Leads = leads,
            LeadByExactKey = leadByExact,
            Redirects = redirects,
            AnotherNames = anotherNames,
            RenameEquivalents = renameEquivalents,
            TotalNodes = nodes.Count,
        };
    }

    private static void TryAddRenameVariant(Dictionary<string, string> target, string variant, string canonical)
    {
        var key = variant.Trim(DashCharacters).Trim();
        if (key.Length >= 2 && key != canonical && !target.ContainsKey(key))
        {
            target[key] = canonical;
        }
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
            Polarity = DerivePolarity(text),
        };
    }

    /// <summary>
    /// 从节点名推导证据极性（P0 极性标签，数据驱动而非人工维护）：
    /// 节点名含"阴性/未做/未查/未证实/未特指/不伴有…"→ Negative；
    /// 节点名含"确定的"（其下全为阳性分支）→ Positive；
    /// 其余 → Unspecified（不参与极性裁决）。
    /// 注意："伴有或不伴有痰培养"（A15.0 节点）含"不伴有"但语义中立，
    /// 故阳性分支的判定优先——含"确定的/证实/阳性"且不含明确否定词时判 Positive。
    /// </summary>
    private static string DerivePolarity(string term)
    {
        // "被未特指的方法"是"确定的"下的**阳性证实分支**（A15.3 经证实），
        // "未特指"修饰的是检查方法而非"未证实"——不得按阴性词剔除。
        var isConfirmationByUnspecifiedMethod = term.Contains("被未特指的方法", StringComparison.Ordinal);
        var isNegative = !isConfirmationByUnspecifiedMethod
            && NegativePolarityKeywords.Any(keyword =>
                term.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (isNegative)
        {
            // "伴有或不伴有…"是中性表述（伴随培养与否都不改变"镜检证实"的事实），
            // 不按否定处理：仅当节点名以否定语义为主（不伴有提及…证实 / 未做…检查）时判阴性。
            var neutralConjunction = term.Contains("伴有或不伴有", StringComparison.OrdinalIgnoreCase)
                || term.Contains("伴有或不提及", StringComparison.OrdinalIgnoreCase);
            if (!neutralConjunction)
            {
                return "Negative";
            }
        }

        foreach (var keyword in PositivePolarityKeywords)
        {
            if (term.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return "Positive";
            }
        }

        return "Unspecified";
    }

    private static readonly string[] NegativePolarityKeywords =
        ["阴性", "未做", "未查", "未证实", "未特指", "不伴有", "不提及"];

    private static readonly string[] PositivePolarityKeywords =
        ["确定的", "阳性", "培阳", "涂阳", "细菌学和组织学证实", "被未特指的方法"];

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
    /// "或/，、"拆分并列别名（首别名权重 1.0，其余 0.85），并记录每段的来源分隔符
    /// （或 尾段 → HuoTail；逗号/顿号 尾段 → CommaTail，索引 "主导词，部位" 惯例的部位段）。
    /// NEC/NOS 为索引控制标记（未分类/不可另分类），剥离后保留主体词别名。
    /// </summary>
    public static IReadOnlyList<IcdIndexAlias> ParseAliases(string text)
    {
        var result = new Dictionary<string, (decimal Weight, string MatchKey, bool HuoTail, bool CommaTail, bool ParenTail, bool HeadWord)>(StringComparer.Ordinal);

        void Add(string value, decimal weight, bool huoTail, bool commaTail, bool parenTail, bool headWord = false)
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
                result[key] = (weight, matchKey, huoTail && weight < 1.0m, commaTail, parenTail, headWord);
            }
        }

        var (withoutBrackets, bracketContent) = StripDecorations(text);

        // 切段口径与权重保持原样（[或，、] 都切、非首段 0.85），只是记住每段前面那个分隔符
        // 是不是**顶层** 或：顶层 或 的尾段继承头段的人名限定（"施莫尔病或结节" 的 结节），
        // 括号内的 或（"NEC(胎儿或婴儿)"）只是括注并列，不改变该段的别名地位。
        // 括注切段标 ParenTail：段起点在括号内（"尿道结石病(伴有绞痛或感染)" 的 感染）。
        // 流水线照旧把它们作召回锚（校准口径："NEC(胎儿或婴儿)" 的 婴儿 是合法锚点）；
        // "编码录入"拆分经 NameLookupAnchorAllowed 排除（任何含 感染 的输入都不该锚上结石行）。
        var mainSegments = new List<(string Value, bool HuoTail, bool CommaTail, bool ParenTail)>();
        var precedingText = string.Empty;
        var parenDepth = 0;
        var topLevelHuoCount = 0;
        var pendingCommaTail = false;
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

                pendingCommaTail = parenDepth == 0 && parts[index] is "，" or "、";
                continue;
            }

            // 括注段：段起点在未闭合括号内（本段文本嵌在上一段开启的括号里）。
            var parenTail = parenDepth > 0;
            var segment = NecTokenRegex.Replace(parts[index], string.Empty).Trim();
            if (segment.Length > 0)
            {
                // 尾段词在 precedingText 里已经出现过（"蒙克贝格动脉硬化、变性、病或硬化" 的 硬化）
                // 说明本行的核心名词就是它，仍是合法锚点；没出现过（"施莫尔病或结节" 的 结节、
                // "杰克逊癫癎或发作" 的 发作）才是继承头段人名的第二名词。
                mainSegments.Add((segment,
                    topLevelHuoCount > 0 && !precedingText.Contains(segment, StringComparison.Ordinal),
                    index > 0 && pendingCommaTail,
                    parenTail));
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
            mainSegments.Add((NecTokenRegex.Replace(withoutBrackets, string.Empty).Trim(), false, false, false));
        }

        for (var index = 0; index < mainSegments.Count; index++)
        {
            var (segment, huoTail, commaTail, parenTail) = mainSegments[index];
            var weight = index == 0 ? 1.0m : 0.85m;
            Add(segment, weight, huoTail, commaTail, parenTail);
            var stripped = ParenthesisRegex.Replace(segment, string.Empty).Trim();
            // 核心词长度可为 1："－肺(活动性)(感染后)" 的裸部位词 肺 是索引 "主导词→部位" 
            // 下钻的合法目标，丢掉它会让 "右肺…钙化" 这类输入无处下钻。
            if (stripped.Length >= 1 && stripped != segment)
            {
                Add(stripped, weight, huoTail, commaTail, parenTail);
            }

            // 头段词别名：第一个括号/方括号前的纯文本（"艾滋病 (相关复合征)…" → "艾滋病"）。
            // ParenthesisRegex 无法处理嵌套括号（"(另见 人 (类)…)"），导致 stripped 仍含尾部噪声，
            // 归一化别名键不是头段词，输入"艾滋病性脑炎"无法召回该主导词。
            // 单字头段（"成(神经)胶质细胞瘤" 的 成）照旧登记：输入以它收尾时是合法锚点（尾重心口径），
            // 但它只是残段、不是本行的词，HeadWord 标记让召回层收回它的形态学豁免
            // （脑(脊)膜瘤 的 脑 否则能锚上任何含 脑 的输入）。
            var headWord = HeadWordRegex.Replace(segment, string.Empty).Trim();
            if (headWord.Length >= 1 && headWord != segment && headWord != stripped)
            {
                Add(headWord, weight, huoTail, commaTail, parenTail, headWord: true);
            }
        }

        foreach (var content in bracketContent)
        {
            Add(content, 0.85m, false, false, false);
        }

        return result
            .Select(pair => new IcdIndexAlias(pair.Key, pair.Value.Weight, pair.Value.MatchKey, pair.Value.HuoTail)
            {
                CommaTail = pair.Value.CommaTail,
                ParenTail = pair.Value.ParenTail,
                HeadWord = pair.Value.HeadWord,
            })
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

    /// <summary>
    /// 头段词提取：匹配从第一个括号/方括号开始到末尾的全部内容。
    /// "艾滋病 (相关复合征)[获得性免疫缺陷综合征](另见 人 (类)，免疫缺陷病毒 [HIV] 病)" → 移除后剩 "艾滋病"。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex HeadWordRegex =
        new(@"[\[（(].*$", System.Text.RegularExpressions.RegexOptions.Compiled);

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

/// <summary>
/// 输入侧证据极性口径（P0 极性族，流水线 IcdIndexDiagnosisResolver 与编码录入面板引擎
/// NameCodingLookupEngine 共用同一判据，避免两套极性口径漂移）。
/// </summary>
internal static class IcdIndexPolarity
{
    /// <summary>
    /// 输入的证据极性识别：阳性——(+)、阳性、培阳、涂阳 等"已证实"措辞；
    /// 阴性——(-)、阴性、未做、未查 等"未证实/阴性"措辞；都没写 → None（不参与极性裁决）。
    /// 顺序：阴性优先于阳性（"未做检查"含"未"，不应被"检查"类词误判）。
    /// 「阳性/阴性」只有在**检验结果语境**才成立——标志物语境（EBV阳性、CD30阳性、
    /// ALK阴性、BCR/ABL阴性、Ph1阳性、Rh阴性、革兰氏阴性、凝固酶阴性）是疾病分型依据，
    /// 不是检验结果，误判会给非结核输入套上证据极性（万条 CAP 实测 8+ 条淋巴瘤/白血病/肺炎）。
    /// </summary>
    public static string DeriveInputPolarity(string? inputText)
    {
        if (string.IsNullOrWhiteSpace(inputText))
        {
            return "None";
        }

        foreach (var keyword in NegativeInputPolarityKeywords)
        {
            if (ContainsResultPolarityWord(inputText, keyword))
            {
                return "Negative";
            }
        }

        foreach (var keyword in PositiveInputPolarityKeywords)
        {
            if (ContainsResultPolarityWord(inputText, keyword))
            {
                return "Positive";
            }
        }

        return "None";
    }

    private static readonly string[] NegativeInputPolarityKeywords =
        ["(-)", "阴性", "未做", "未查", "未做细菌学"];

    private static readonly string[] PositiveInputPolarityKeywords =
        ["(+)", "阳性", "培阳", "涂阳"];

    /// <summary>
    /// 词面命中且不是标志物语境：逐一检查「阳性/阴性」的每个出现位置，
    /// 紧邻前缀是拉丁字母数字串（EBV/CD30/ALK/BCR-ABL/Ph1/Rh）或标志物词干
    /// （病毒/抗体/抗原/革兰/凝固酶）时跳过——那些位置是分型标记不是检验结果。
    /// </summary>
    private static bool ContainsResultPolarityWord(string text, string word)
    {
        var searchStart = 0;
        while (searchStart <= text.Length - word.Length)
        {
            var hit = text.IndexOf(word, searchStart, StringComparison.Ordinal);
            if (hit < 0)
            {
                return false;
            }

            if (!IsMarkerContext(text, hit))
            {
                return true;
            }

            searchStart = hit + word.Length;
        }

        return false;
    }

    /// <summary>标志物词干：这些词紧贴「阳性/阴性」时，阳性/阴性是分型标记不是检验结果。</summary>
    private static readonly string[] MarkerStems =
        ["病毒", "抗体", "抗原", "革兰氏", "革兰", "凝固酶", "染色体", "基因", "受体"];

    /// <summary>
    /// 「阳性/阴性」出现位置的前缀是否为标志物语境：
    /// ① 向左吞 ASCII 字母/数字/斜杠/连字符（EBV、CD30、ALK、BCR/ABL、Ph1、Rh）；
    /// ② 吞完后词干以标志物词收尾（EB病毒、抗D抗体、革兰氏、凝固酶）。
    /// ①② 任一成立即标志物语境。位置在词首（无前缀）不是标志物。
    /// </summary>
    private static bool IsMarkerContext(string text, int polarityStart)
    {
        if (polarityStart == 0)
        {
            return false;
        }

        var start = polarityStart;
        while (start > 0)
        {
            var previous = text[start - 1];
            if (previous is '/' or '-' or '－' or '／')
            {
                start--;
                continue;
            }

            // ASCII 字母数字（EBV/CD30/ALK/Ph1/Rh）；中文汉字不算（"结核阳性"是检验语境）。
            if (previous < 0x80 && (char.IsAsciiLetterOrDigit(previous)))
            {
                start--;
                continue;
            }

            break;
        }

        if (start < polarityStart)
        {
            return true;
        }

        return MarkerStems.Any(stem =>
            start >= stem.Length
            && string.CompareOrdinal(text, start - stem.Length, stem, 0, stem.Length) == 0);
    }

    /// <summary>
    /// 候选的极性排序权重（P1 极性匹配优先）：输入极性与候选极性一致 → 2；
    /// 输入无证据词或候选未标注 → 1；相反 → 0。只做同分附近的次序调整，
    /// 极性冲突的候选已由 R2 剔除。
    /// </summary>
    public static int PolarityRank(string? candidatePolarity, string inputPolarity)
    {
        if (inputPolarity is not ("Positive" or "Negative")
            || candidatePolarity is not ("Positive" or "Negative"))
        {
            return 1;
        }

        return string.Equals(candidatePolarity, inputPolarity, StringComparison.Ordinal) ? 2 : 0;
    }
}
