using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Coding.IcdIndex;

/// <summary>
/// Position = 命中别名 MatchKey 在输入中的起点（与面板拆词 NameLeadTermSplitter 同一来源口径）；
/// -1 表示 MatchKey 整词取不到位置（Key 靠 LCS 片段命中），跨度未知，不参与跨度包含判定。
/// </summary>
public sealed record LeadWordHit(IcdIndexNode Node, IcdIndexAlias Alias, decimal CoverRatio, int CoveredChars, int Position = -1);

/// <summary>
/// 下钻终点：命中的最深节点与命中路径（证据链展示用）。
/// </summary>
public sealed record DrillResult(
    IcdIndexNode Terminal,
    IcdIndexNode CodeNode,
    IReadOnlyList<IcdIndexNode> Path,
    string PathKey,
    decimal CoverRatio,
    bool CodeFromAncestorFallback,
    IReadOnlyList<string> Jumps,
    /// <summary>见-1 强制改道时被让位的本地链路；改道链路取不到可用码时由调用方回退。</summary>
    DrillResult? Displaced = null,
    /// <summary>
    /// 另见-1 提示链路：本地自带编码（病案信息简单，直接采用本行码）时 Top1 不变，
    /// 但 另见 目标树里按输入还能走出的更细码交给调用方作并列候选，用于提示"漏填修饰词"。
    /// </summary>
    IReadOnlyList<DrillResult>? SeeAlsoHints = null,
    /// <summary>
    /// 规范形路径键（专供字典展开门控）：主导词段取节点规范别名（逗号切分的首段，
    /// 权重 1.0），后续层级与 PathKey 同源累加。PathKey 用的是"输入命中的那个别名"
    /// （梗塞），而字典标题写规范形（脑梗死）——别名等价（梗塞≡梗死）若不传导到
    /// 展开门控，单字覆盖 2/3&lt;1.0 会把整族字典行误杀成"字典库无此码"回退
    /// （陈旧性脑梗塞 → I63.9 的教训，2026-11-16）。与 PathKey 等长同构，仅主导词段不同。
    /// </summary>
    string? ExpansionPathKey = null);

/// <summary>
/// 主导词召回 + 逐级修饰词下钻。
/// 召回门控：别名被输入完整/高覆盖命中（TextMatch.MatchesInput），且词条部位限定词须在输入中声明；
/// 单字主导词只有在它是输入结尾的核心词时才参与召回（见 IsTailCoreWord），
/// 属性尾段复合别名剥出的单字 core 例外：逐字出现在输入里即可（见 IsAttributeTailOnlyAnchor）；
/// "A病或B" 式的 或 尾段若在别处另有自己的主导词行，不得独立锚定本行（见 IcdIndexSnapshotBuilder.Build）；
/// 输入写成 "手术后X" 时 手术 只是状态前缀的一部分，不锚定 手术 这类以 术 收尾的主导词（见 IsGenericSurgeryAliasOutsideTail）；
/// 下钻门控：子节点（任一变体别名）对输入"新增长连续段 ≥2"才进入（同样受部位限定词约束），同层取增益最大者；
/// 命中链上无码节点回退最近有码祖先，最终回退主导词码；"见"子节点改道与本树下钻并列，取输入覆盖更多者，
/// 但本地只能靠祖先兜底取码时改道结果无条件优先（索引惯例里"见 X"即命令转换主导词）；
/// "另见 X" 且本地自带编码时不改 Top1，只把目标树的更细码挂在 <see cref="DrillResult.SeeAlsoHints"/> 上；
/// 纯病因连接行（见于/由于/继发于）在输入没逐字写出它时透明：孙节点直接来本层竞选下一跳，
/// 使 肾炎 → 由于 → 链球菌 这类索引病因惯例能接上"链球菌并发肾炎"式病历（见 ConnectorCoreTerms）。
/// </summary>
public static class IcdIndexMatcher
{
    /// <summary>
    /// 泛化词不能作为主导词召回依据：索引行 "肢端缺氧，慢性" 式的
    /// 尾段别名（慢性/急性）会命中一切含该词的输入，产生跨章节噪声；
    /// 括号装饰里的通用词（"缺失[缺如](器官或部位)(完全或部分)" 的 部分/器官）同理。
    /// 下半区由 10000 条临床诊断的主导词产出台账筛出（共 1816 个别名键、30378 次召回）：
    /// 这些键合计占用 1873 个名称的召回名额，却在全部输入上下钻到码 0 次、推荐 0 条，
    /// 只把 top-8 名额占满；其中 动脉/血管/腹股沟/拇指/乙状(结肠)/颈椎 是索引 "疾病，部位"
    /// 惯例里的部位段，形成/细胞/周期/功能不良/粘液/淋巴细胞性 是形态与病程尾段，
    /// 单独出现都不构成主导词。反例（产量非零、必须放行）：肿瘤 18、妊娠 12、心脏 19、皮肤 101。
    /// </summary>
    private static readonly HashSet<string> GenericModifierKeys =
    [
        "慢性", "急性", "亚急性",
        "部分", "完全", "部位", "器官", "其他", "特指", "情况", "双侧", "单侧",
        // 部位段
        "动脉", "血管", "腹股沟", "拇指", "乙状", "乙状结肠", "颈椎", "呼吸",
        // 形态/病程尾段
        "形成", "细胞", "周期", "功能不良", "粘液", "淋巴细胞性",
        // 人群段 新生儿：放行它（本地 10000 条实测）会让 "三胎，影响到胎儿或新生儿"、
        // "大于胎龄 NEC(胎儿或婴儿)" 一类主导词挤占 top-8，把 新生儿休克/惊厥/脱水 打成
        // P00.8x·P51.9 噪声，Top3 命中 -10、Top1 -9；人群词只应作疾病主导词下的修饰词下钻。
        // 婴儿 不拉黑：台账 27 次召回 20 次下钻到码（特大婴儿→P08.000），拉黑即打成空推荐。
        "新生儿",
    ];

    /// <summary>
    /// 索引里只起接续作用的"纯连接行"核心词（关节炎 → －见于(由于) → －－类风湿）。
    /// 全库 51268 行共 403 行属于这一族（伴有 194、由于 111、见于 71、和 22…），
    /// 分布在 1-4 级、402 行不带编码：它们不表达可选修饰语，只把上一层的病因/部位接到下一层。
    /// 病历永远不会逐字写出 见于/由于，按"每一层都要为链路挣到新内容"的下钻口径，
    /// 链路必然停在连接行上、只能回退祖先泛码（万条实测：断链行榜首 －见于(由于) 挡 143 名，
    /// 横跨 26 个主导词）。本表只收病因方向的 见于(71 行)/由于(111 行) 及其复合写法。
    /// 并列连词不进表：索引的 和/及/与/或 是"累及第二个部位/另一种物质"的多发组合写法
    /// （骨折 → 多发性 → 肢 → 上(单一) → 和 → 另一上肢 = T02.4），折叠它会把单部位输入
    /// 判成多部位码——万条实测 3 条 T02.2/S 类单上肢骨折因此丢 Top1。
    /// 表并列的 伴有/合并 两个连接词也不进表：它们说的是"另一个并存的病"，不是本行的病因。
    /// 折叠它时，输入并列写出两个诊断就会被判成索引的组合码——JZEY 真实病历万名实测 19 条流失里
    /// 13 条是这一族（主动脉瓣关闭不全，二尖瓣关闭不全 I35.100→多瓣膜病 I08.005、
    /// 急性胆囊炎、胆囊泥沙样结石 K81.000→胆囊结石 K80.200X003、
    /// 甲状腺多发结节--结甲 E04.101→甲状腺毒症伴有单个结节 E05.100），
    /// 而 ICD10CAP 那 60 条新增全部出自 由于/见于 的病因方向，两条通路互不依赖。
    /// </summary>
    private static readonly HashSet<string> ConnectorCoreTerms =
    [
        "见于", "由于", "继发于", "由于合并",
    ];

    /// <summary>连接行最多连续折叠几层（实测索引里最深 2 层相连）。</summary>
    private const int MaxConnectorLookThrough = 2;

    /// <summary>
    /// 纯连接行：无编码、无见/另见指针、核心词整词就是连接词。
    /// 带编码的不算（折叠它会丢掉本行直接采用码）、带指针的不算（指针行是"必须转换"的语义载体）。
    /// public 供链路打分使用：折叠掉的连接行不是"输入真的写出的证据层"，
    /// 既不该挣链路深度加成，也不该挣覆盖度加成（纪律：改道一律平权，不加权）。
    /// </summary>
    public static bool IsConnectorNode(IcdIndexNode node)
        => node.Code is not { Length: > 0 }
            && node.JumpKind == IndexJumpKind.None
            && ConnectorCoreTerms.Contains(node.CoreTerm);

    /// <summary>
    /// 下钻候选 = 直接子节点 + 穿过连接行后的后代节点。
    /// 连接行照常进候选（选它=原地停住、回退祖先码，这是改动前的行为），
    /// 折叠只是在它之上多挂一条"穿过它去竞选孙节点"的通路，通路上的每个子节点
    /// 沿用完全相同的增益/覆盖/部位门控口径——平权、不加权。
    ///
    /// 只在输入没有逐字写出这个连接词时才折叠：透明化是为索引惯例
    /// （肾炎 → －由于 → 链球菌，病历写"链球菌并发肾炎"）服务的。输入自己写出连接词时
    /// 字面下钻本来就走得通，而折叠通路会把它的词面从 pathKey 省掉、覆盖度分母随之变小，
    /// 等于给这条链路变相加权——万名实测这道闸省下 3 条既有正确 Top1。
    /// </summary>
    private static void CollectDrillCandidates(
        IcdIndexNode parent,
        IReadOnlyList<IcdIndexNode> connectors,
        List<(IcdIndexNode Node, IcdIndexNode[] Connectors)> sink,
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

            if (IsConnectorNode(child) && connectors.Count < MaxConnectorLookThrough
                && !WrittenInInput(child, inputKey))
            {
                CollectDrillCandidates(child, [.. connectors, child], sink, inputQualifiers, inputKey);
            }
        }
    }

    /// <summary>输入是否逐字写出了该行的任一别名词面（连接词是否真的出现在病历里）。</summary>
    private static bool WrittenInInput(IcdIndexNode node, string inputKey)
        => node.Aliases.Any(alias => alias.MatchKey.Length > 0
            && inputKey.Contains(alias.MatchKey, StringComparison.Ordinal));

    /// <summary>
    /// 节点级语义门控：部位/人群限定词必须是输入已声明的子集，病程极（急性↔慢性、近期↔陈旧）
    /// 与否定极（非X↔X）都不得与输入相反。召回与下钻共用同一口径。
    /// 否定极在下钻层拦才有效：只拦字典行时 胃炎→萎缩性(慢性) 仍会被选中并耗尽链路，
    /// 让 慢性非萎缩性胃炎 连正确的 慢性胃炎 K29.500 都拿不到。
    /// 否定极只看 CoreTerm（括号注解已剥）："心肌病→肥厚型(非梗阻性)→梗阻性 I42.1" 的
    /// 括号 非梗阻性 是该节点默认码 I42.2 的注解，按整条 Term 判矛盾会把正确的
    /// 梗阻性肥厚型心肌病 I42.100 连子树一起打死；主导词 "斜视(…)(非麻痹性)" 同理误杀 H49.900。
    /// </summary>
    private static bool NodeAllows(IcdIndexNode node, HashSet<string> inputQualifiers, string inputKey)
        => node.Qualifiers.IsSubsetOf(inputQualifiers)
            // 0 级主导词锚定不看()里的内容（用户口径 11-07："急性肺炎"/"肺炎"/"慢性肺炎"
            // 主导词都是 肺炎）——病程极（急性/慢性、近期/陈旧）写在行名圆括号里
            // （肺炎(急性)(双侧)…），慢性肺炎 曾被极性冲突门控整行剔除。子行照旧门控。
            && (node.Level == 0 || !TextMatch.HasPhasePoleConflict(node.Term, inputKey))
            && !TextMatch.HasNegationPoleConflict(node.CoreTerm, inputKey);

    /// <summary>ICD-O 形态学码（M8010/3、M9800/3:C95.9 的形态学段）。</summary>
    private static readonly System.Text.RegularExpressions.Regex MorphologyCodeRegex =
        new(@"M\d{4}/\d", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>本行（或本行编码组里任一码）是形态学码 → 肿瘤形态学树。</summary>
    public static bool IsMorphologyLead(IcdIndexNode lead)
        => lead.Code is { Length: > 0 } code && MorphologyCodeRegex.IsMatch(code);

    /// <summary>
    /// 单字别名能否享用形态学主导词的豁免。索引按惯例把 癌(M8010/3)、瘤(M8000/1) 立成单字
    /// 主导词，拦住它们等于关掉整棵肿瘤形态学树（M19 的保底席位就是为它们设的）。
    /// 但豁免只该给"本行的词就是一个字"：脑(脊)膜瘤(M9530/0) 的 脑、成(神经)胶质细胞瘤(M9440/3)
    /// 的 成 是括号前的头段残段，全库只有这 2 行——放开时 艾滋病性脑膜炎 会锚上 脑(脊)膜瘤，
    /// 而 脑膜炎 与 脑膜瘤 是两个主导词。残段仍按尾重心口径放行（形成 的 成 收尾照样能锚）。
    /// </summary>
    public static bool IsMorphologyEntry(IcdIndexNode lead, IcdIndexAlias alias)
        => alias.Key.Length >= 2 || IsMorphologyLead(lead) && !alias.HeadWord;

    /// <summary>
    /// 单字主导词的尾重心门控。中文诊断名是"修饰语 + 核心名词"结构，编码员取的主导词
    /// 就是末尾那个核心名词（手术后尿道会阴瘘 → 瘘、内痔 → 痔、马蹄肾 → 肾）。
    /// 索引里 60 条 0 级单字主导词（瘘 194 子行/189 带码、痛 86、疹 9、痔、龋、疮、肾、
    /// 心、双、无、大、性…）原先整族召不回：按覆盖字数排序，1 个字永远排在任何 2 字词之后，
    /// 且 <code>alias.Key.Length &lt; 2</code> 直接拦住——万条实测 肛瘘、内痔 这类输入
    /// 一个主导词都召不到，整条索引通道返回空。
    /// 全量放开会让 心/性/大/无 沾住一切含该字的输入（跨部位误锚），所以只认结尾位置：
    /// 单字必须是本条病历写到的最后一个词，出现在中部（心肌梗死）仍按原口径拦住。
    /// 泛词 病 即使收尾也不放行（万条实测的唯一流失来源）：按编码员查典顺序它只能在
    /// 所有真主导词落空后末位兜底，放行会让 高血压病 的 病，疾病→高血压 抢在常规链路前面。
    /// </summary>
    private static bool IsTailCoreWord(string aliasKey, string inputKey)
        => aliasKey.Length == 1
            && !IsGenericConditionLeadKey(aliasKey)
            && inputKey.Length > 1
            && inputKey.EndsWith(aliasKey, StringComparison.Ordinal);

    /// <summary>泛术式主导词：词面是"手术"这一件事的统称，不带任何病种信息。</summary>
    private const string GenericSurgeryAlias = "手术";

    /// <summary>
    /// 泛术式主导词 手术 的尾重心门控（与 <see cref="IsTailCoreWord"/> 同一判据，管的是两字词）。
    /// "手术后尿道会阴瘘" 里 手术 只是 手术后 这个状态前缀的一部分：索引紧接着就写了
    /// 35946 手术的 -见情况 与 35947 手术后的 -见情况 两行（编码员口径：见情况不是主导词），
    /// 管术后状态的正牌主导词是 50034 状态 → －手术后 NEC 与 35948 手术后状态 NEC；
    /// 而 0 级行 35943 手术(R69) 只有 2 条 分娩 子行，锚住即以祖先兜底展开成 R69.X00，
    /// 且按覆盖字数排序它永远排在真正的结尾核心词 瘘 之前（工作簿因此读成"主导词=手术"）。
    /// 万条实测噪声面：JZEY 1361 条 手术/术后 词面输入里 470 条候选含 R69.X00、CAP 189 条里 27 条，
    /// 双语料 0 条以 R69 为真值；同义词等价类把 术后 写成 手术后 是它扩散到 470 条的通道。
    /// 只在 手术 就是输入结尾核心词时放行（与单字主导词同一让位判据）。
    /// </summary>
    private static bool IsGenericSurgeryAliasOutsideTail(string aliasKey, string inputKey)
        => aliasKey == GenericSurgeryAlias && !inputKey.EndsWith(GenericSurgeryAlias, StringComparison.Ordinal);

    /// <summary>
    /// 括号复合别名只在属性尾段上命中时不能当主导词锚点。索引把 "裂(先天性)" 写成一行时，
    /// ParseAliases 会生成 裂先天性 与 裂 两个键；输入 先天性大脑囊肿 与 裂先天性 的最长公共段
    /// 只有 先天性（3/4=0.75、最长段 3），按覆盖字数排序它会压过真正的结尾核心词 囊肿（2 字），
    /// 把整族 先天性 属性行塞满 12 席，Q04.6 链路根本进不了下钻。
    /// 这里剥掉属性尾段，对核心词复用单字/多字别名的原有锚定口径；
    /// 单字 core 除结尾重心外，逐字出现在输入里即可锚定（先天性鳃裂瘘管 → 裂先天性，
    /// 裂 在 鳃裂 里而不在结尾）。输入逐字含住整个复合键时一律放行。
    /// 只关召回：别名集合、权重、MatchKey、下钻与链路覆盖度一律不动。
    /// </summary>
    private static bool IsAttributeTailOnlyAnchor(IcdIndexNode lead, IcdIndexAlias alias, string inputKey)
    {
        if (!IcdIndexSnapshotBuilder.TrySplitGenericAttributeTail(alias.Key, out var core))
        {
            return false;
        }

        if (inputKey.Contains(alias.Key, StringComparison.Ordinal))
        {
            return false;
        }

        if (core.Length < 2)
        {
            return !IsMorphologyLead(lead)
                && !IsTailCoreWord(core, inputKey)
                && !(inputKey.Contains(core, StringComparison.Ordinal) && !IsGenericConditionLeadKey(core));
        }

        return GenericModifierKeys.Contains(core)
            || IsGenericSurgeryAliasOutsideTail(core, inputKey)
            || !TextMatch.MatchesInput(core, inputKey, out _);
    }

    /// <summary>
    /// 索引惯例里的泛化主导词族（情况 = 病，疾病 一族）。它们不携带"这行说的是哪个病"，
    /// 召回层与失效主导词改道层都要用它拦住，只有末位兜底通道允许进入。
    /// </summary>
    public static bool IsGenericConditionLeadKey(string aliasKey)
        => aliasKey is "病" or "疾病" or "情况" or "疾患" or "综合征";

    /// <summary>主导词节点级门控（"编码录入"名称拆分复用）：部位限定词须为输入已声明子集，病程/否定极不相反。</summary>
    public static bool LeadNodeAllows(IcdIndexNode lead, string inputKey)
        => NodeAllows(lead, TextMatch.ExtractQualifiers(inputKey), inputKey);

    /// <summary>
    /// 分型/分期/分级的构词后缀字：它们是修饰词词尾（霍乱中型、糖尿病2型、活动期），
    /// 不是疾病核心名词（瘘/痔/疝/癌）——索引里以 "…，型" 逗号尾段别名存在
    /// （弗雷德里克森高脂蛋白血症，型），输入收尾沾上即被尾重心放行，产生噪声主导词。
    /// 只关"编码录入"拆分门控；流水线共用的 <see cref="IsTailCoreWord"/> 不动，零回归。
    /// </summary>
    private static readonly HashSet<string> NameLookupGradingSuffixKeys =
    [
        "型", "性", "期", "级", "度", "类",
    ];

    /// <summary>
    /// 次因编码导语行："病原体，作为分类于他处疾病的原因"（B96/B97 章，全库 17 行）。
    /// 只有当该病原体是**分类于他处疾病**的原因时才编 B96/B97——输入未逐字写明
    /// "分类于他处"用途时（如 肠毒性大肠杆菌感染 是本源感染，编 A04.1），
    /// 该行不得作主导词锚。命中词面（大肠杆菌）沾边不代表要编他处原因码。
    /// </summary>
    private static bool IsSecondaryCauseLead(IcdIndexNode lead)
    {
        var commaIndex = lead.Term.IndexOfAny(['，', ',']);
        if (commaIndex < 0)
        {
            return false;
        }

        return TextMatch.NormalizeKey(lead.Term[(commaIndex + 1)..])
            .Contains("分类于他处", StringComparison.Ordinal);
    }

    /// <summary>
    /// "主导词，修饰" 行的修饰段声明核对（"编码录入"专用）：
    /// 头段(≥2字)+修饰段(≥2字) 的逗号/顿号行，修饰段必须在输入中有 ≥2 字连续词面——
    /// 头段单独沾边而修饰段未声明，是跨语义噪声（伤寒并发肠出血 沾
    /// "出血，经间期 NEC(规则)" 的 出血、出血、凝血或凝血酶原时间延长 的 出血）。
    /// 与既有部位限定词纪律同思路：部位类修饰段（视网膜/胃）由 NodeAllows 把关，
    /// 本核对补上非部位类修饰段（经间期/凝血酶原时间延长）的声明要求。
    /// 豁免：头段单字的同义族行（炎，炎症(性)…，由单字尾锚规则把关）、
    /// 修饰段单字（疝，膈）。流水线不动。
    /// </summary>
    private static bool CommaTailDeclaredInInput(IcdIndexNode lead, string inputKey)
    {
        // 找第一个不在括号/方括号内的逗号（排除「另见 人(类)，免疫缺陷病毒」这类指针内的逗号）
        var commaIndex = -1;
        var depth = 0;
        for (var i = 0; i < lead.Term.Length; i++)
        {
            var c = lead.Term[i];
            if (c is '(' or '（' or '[' or '［')
            {
                depth++;
            }
            else if (c is ')' or '）' or ']' or '］')
            {
                depth--;
            }
            else if (depth == 0 && (c == '，' || c == ',' || c == '、'))
            {
                commaIndex = i;
                break;
            }
        }

        if (commaIndex < 0)
        {
            return true;
        }

        var head = TextMatch.NormalizeKey(lead.Term[..commaIndex]);
        var tail = TextMatch.NormalizeKey(lead.Term[(commaIndex + 1)..]);
        if (head.Length < 2 || tail.Length < 2)
        {
            return true;
        }

        // 同义族行豁免：尾段与头段同义（"梗死，梗塞" 共首字 梗、"炎，炎症(性)…" 字根 炎）
        // ——尾段是头段的同义变体，不是未声明的修饰语。
        if (TextMatch.IsSynonymousCommaTail(head, tail))
        {
            return true;
        }

        if (inputKey.Contains(tail, StringComparison.Ordinal))
        {
            return true;
        }

        for (var length = Math.Min(tail.Length, inputKey.Length); length >= 2; length--)
        {
            for (var start = 0; start + length <= tail.Length; start++)
            {
                if (inputKey.Contains(tail.Substring(start, length), StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// "编码录入"名称拆分（<c>IcdNameLeadTermSplitter</c>）复用的别名锚点门控：
    /// 与 <see cref="RecallLeads"/> 同一套噪声防线（或 尾段锚、泛化修饰词、短别名首尾锚、
    /// 泛术式 手术、属性尾段复合别名），只把 MatchesInput 的模糊覆盖判定换成调用方的
    /// 全量匹配（严格子串）判定——本方法不判"是否命中"，只判"命中了能不能当主导词锚"。
    /// 纯连接词别名（由于/见于/继发于…）不得作锚：输入写完整索引路径（霍乱，由于O1群…所致）时，
    /// 连接词是霍乱修饰链的接续符，不是主导词（"密螺旋体病，由于" 的逗号尾段别名同理）。
    /// 部位逗号尾段（CommaTail，"小梁形成，膀胱" 的 膀胱）与分型/分期后缀字
    /// （型/性/期/级/度/类）即便收尾也不作锚。
    /// 次因导语行（"…，作为分类于他处疾病的原因"）在输入未写明该用途时不作锚。
    /// 单字收尾锚只认主导词本名（权重 1.0 主别名，方括号/或/逗号派生的单字变体
    /// 如 "瘙痒[症]" 的 症 除外）；2 字别名（含中段命中）交由 NameLeadTermSplitter
    /// 的中段碎片终结规则裁决（结核性肺炎 的 肺炎 保留，淋巴瘤 的 淋巴 剔除）。
    /// </summary>
    public static bool NameLookupAnchorAllowed(IcdIndexNode lead, IcdIndexAlias alias, string inputKey)
        => alias.RecallAnchor
            && !alias.CommaTail
            && !alias.ParenTail
            && !GenericModifierKeys.Contains(alias.Key)
            && !ConnectorCoreTerms.Contains(alias.Key)
            && !IsGenericSurgeryAliasOutsideTail(alias.Key, inputKey)
            && (!IsSecondaryCauseLead(lead)
                || inputKey.Contains("分类于他处", StringComparison.Ordinal))
            && CommaTailDeclaredInInput(lead, inputKey)
            && (alias.Key.Length >= 3
                || alias.Weight >= 1.0m
                    && !NameLookupGradingSuffixKeys.Contains(alias.Key)
                    && (alias.Key.Length == 2
                        || IsMorphologyEntry(lead, alias)
                        || IsTailCoreWord(alias.Key, inputKey)))
            && !IsAttributeTailOnlyAnchor(lead, alias, inputKey);

    public static IReadOnlyList<LeadWordHit> RecallLeads(
        IcdIndexSnapshot snapshot,
        string inputKey,
        int topK)
    {
        if (inputKey.Length < 2)
        {
            return [];
        }

        var hits = new List<LeadWordHit>();
        var inputQualifiers = TextMatch.ExtractQualifiers(inputKey);
        foreach (var lead in snapshot.Leads)
        {
            // 词条声明了输入未提及的部位/人群限定词（如 "断裂，视网膜"）→ 整节点剔除。
            if (!NodeAllows(lead, inputQualifiers, inputKey))
            {
                continue;
            }

            LeadWordHit? best = null;
            foreach (var alias in lead.Aliases)
            {
                // 单字别名（"肾"）只作下钻目标，不足以定位主导词；
                // RecallAnchor=false 的 或 尾段同理（它继承头段限定词，不是本行的主导词）。
                // 例外一：本行编码是 ICD-O 形态学码（M####/d）的单字主导词——索引里肿瘤形态
                // 树的主导词按惯例就是一个字（143 癌 M8010/3），拦住它整棵形态学树就召不回，
                // "甲状腺C细胞癌" 只能被 甲状腺X 疾病行借走。
                // 例外二：单字正是本条病历的结尾核心词（见 IsTailCoreWord）。
                // 心/汗/红 这类单字行只在收尾时锚定，出现在中部（心肌梗死）照旧拦住。
                // 0 级主导词的圆括号内容不参与召回锚定（用户口径 11-06，"只要包含()前的内容即可"），
                // 除非输入恰以该内容收尾——尾重心口径（同 IsTailCoreWord）：特大婴儿 靠
                // 大于胎龄 NEC(胎儿或婴儿) 的 婴儿 召回（P08.0，拉黑即空推荐），
                // 而 慢性丁型肝炎 不得被 放射性皮肤烧伤(急性、慢性或职业性) 的 慢性 锚走。
                if (!alias.RecallAnchor || GenericModifierKeys.Contains(alias.Key)
                    || IsGenericSurgeryAliasOutsideTail(alias.Key, inputKey)
                    || (alias.Key.Length < 2 && !IsMorphologyEntry(lead, alias) && !IsTailCoreWord(alias.Key, inputKey))
                    || (alias.ParenTail && lead.Level == 0 && !inputKey.EndsWith(alias.Key, StringComparison.Ordinal))
                    || IsAttributeTailOnlyAnchor(lead, alias, inputKey))
                {
                    continue;
                }

                if (!TextMatch.MatchesInput(alias.Key, inputKey, out var coverage))
                {
                    continue;
                }

                if (best is null
                    || coverage.CoveredChars > best.CoveredChars
                    || (coverage.CoveredChars == best.CoveredChars
                        && coverage.Ratio * alias.Weight > best.CoverRatio * best.Alias.Weight))
                {
                    best = new LeadWordHit(lead, alias, coverage.Ratio, coverage.CoveredChars,
                        inputKey.IndexOf(alias.MatchKey, StringComparison.Ordinal));
                }
            }

            if (best is not null)
            {
                hits.Add(best);
            }
        }

        var ranked = hits
            .OrderByDescending(hit => hit.CoveredChars)
            .ThenByDescending(hit => hit.CoverRatio * hit.Alias.Weight)
            .ToList();

        // 跨度包含剔除（炎 ⊂ 肺炎 只留 肺炎）：面板 chip 已按同一判据隐藏
        // （NameCodingLookupEngine 的 SpanContainedHiddenSeqNos），这里把它下沉为管线
        // 行为——慢性肺炎 不再借 炎→－慢性，乳突切除术后空腔 H95.1 混进耳科候选。
        // 先剔后取：空出的席位让给后续实词主导词。豁免=形态学码主导词本行别名
        // （瘤/癌 是形态学树唯一入口，HeadWord 残段不豁免，同展示层 989c7e5 口径），
        // 以及有实词子行可下钻的（HasSubstantiveChildDrill：被包含不等于无独立产出，
        // 状态 ⊂ 手术后状态 但 状态→－甲状腺切除术后 E89.0 是变体一路的精确码供货者）；
        // 管线召回全是真主导词命中、无转示概念，容器不设限；Position<0（MatchKey
        // 整词不在输入）的跨度未知，既不被剔也不当容器。
        var locatable = ranked.Where(hit => hit.Position >= 0).ToList();
        if (locatable.Count > 1)
        {
            var hiddenLeads = NameLookup.NameLeadTermSplitter.SpanContainedHiddenSeqNos(
                locatable,
                hit => hit.Node.SeqNo,
                hit => hit.Position,
                hit => hit.CoveredChars,
                hit => hit.Node.CoreTerm,
                hit => IsMorphologyLead(hit.Node) && !hit.Alias.HeadWord,
                _ => true);
            if (hiddenLeads.Count > 0)
            {
                ranked.RemoveAll(hit => hiddenLeads.Contains(hit.Node.SeqNo)
                    && !HasSubstantiveChildDrill(hit.Node, inputKey));
            }
        }
        var taken = ranked.Take(topK).ToList();

        // 形态学入口保底席位（M18 的席位版）。肿瘤形态学主导词 瘤(M8000/1)/癌(M8010/3) 是
        // tumor_index 解部位码的唯一入口，但召回第一排序键是覆盖字数，1 个字的它永远排在
        // 所有 2~3 字疾病主导词之后：肾盂输尿管连接处恶性肿瘤 的 瘤 排第 13 名，在 12 席之外，
        // 于是整条肿瘤链路根本没被调用，交付唯一过 0.30 门槛的 输尿管积水 N13.400（覆盖 0.333）。
        // 占掉第 12 席的是 49644 肿瘤(形成)(另见 肿瘤)——自身无码、下钻也走不出码的死指针行：
        // 9b2b85c 让它退出下钻选层，但没让它退出召回席位。
        // 三条收窄（都是避免"变相放宽全局席位"，24 席实测在真实病历上流失 8 条）：
        // ① 只在席位内一条形态学链路都没有时补位（有就不动，避免与既有形态学行抢席）；
        // ② 只补泛形态码主导词（IsSitelessMorphology，与 M18、resolver 覆盖度记账同一判据），
        //    特异形态码（M9530/0 脑(脊)膜瘤）是病名不是入口，不配保底；
        // ③ 只补"主导词就是输入结尾核心词"的（与单字主导词、泛术式 手术、M18 同一尾重心判据），
        //    动脉瘤/淋巴瘤 那类 瘤 在词内部的输入不启用。
        // 补位是替换末位席位而不是追加，席位总数不变（改道平权纪律：不借保底之名加权）。
        if (ranked.Count > topK
            && !taken.Any(hit => IsMorphologyLead(hit.Node))
            && ranked.Skip(topK).FirstOrDefault(IsMorphologyEntryHit(inputKey)) is { } rescue)
        {
            taken[^1] = rescue;
        }

        return taken;
    }

    /// <summary>
    /// 可作肿瘤表入口的候选：泛形态码主导词，且命中的别名键就是输入的结尾核心词。
    /// </summary>
    private static Func<LeadWordHit, bool> IsMorphologyEntryHit(string inputKey)
        => hit => IndexCodeExpander.IsSitelessMorphology(hit.Node.Code)
            && hit.Alias.Key.Length > 0
            && inputKey.EndsWith(hit.Alias.Key, StringComparison.Ordinal);

    /// <param name="jumpTrace">
    /// 证据链首跳标注（失效主导词 "X -见 Y" 的改道：调用方自己构造命中时链路里没有这一步，
    /// 不标注就看不出编码员口径上的"必须转换"发生在哪里）。
    /// </param>
    public static DrillResult? Drill(
        IcdIndexSnapshot snapshot,
        LeadWordHit leadHit,
        string inputKey,
        int maxHops = 2,
        List<IcdIndexNode>? genericJumps = null,
        string? jumpTrace = null,
        IReadOnlyList<string>? evidenceKeys = null)
    {
        return DrillFromLead(
            snapshot,
            leadHit.Node,
            leadHit.Alias.Key,
            CanonicalLeadKey(leadHit.Node, leadHit.Alias.MatchKey),
            inputKey,
            hops: 0,
            maxHops,
            visited: [leadHit.Node.SeqNo],
            jumps: jumpTrace is null ? [] : [jumpTrace],
            genericJumps: genericJumps,
            evidenceKeys: evidenceKeys);
    }

    private static DrillResult? DrillFromLead(
        IcdIndexSnapshot snapshot,
        IcdIndexNode lead,
        string initialPathKey,
        string canonicalLeadKey,
        string inputKey,
        int hops,
        int maxHops,
        HashSet<int> visited,
        List<string> jumps,
        List<IcdIndexNode>? genericJumps = null,
        IReadOnlyList<string>? evidenceKeys = null)
    {
        var path = new List<IcdIndexNode> { lead };
        var pathKey = initialPathKey;
        // 规范形路径：主导词段用节点规范别名，后续层级与 pathKey 同步累加同一子节点词。
        // 详见 DrillResult.ExpansionPathKey 的说明——字典标题写规范形（脑梗死），
        // 命中别名（梗塞）拼出的路径会把整族字典行误杀成回退。
        var expansionPathKey = canonicalLeadKey;
        var inputQualifiers = TextMatch.ExtractQualifiers(inputKey);
        var currentCover = CoverageOfPath(pathKey, inputKey);
        // P3 证据词面占位区间：证据等价组成员（病理(+)→确定的被组织学/组织学(+)/病理检查(+)）
        // 在输入里的连续出现位置。证据分支的词面就住在这个区间里，非证据子行若
        // "新增内容"全部落在区间内，挣的是证据锚点的字面而不是自己的——按无新内容拦截。
        var evidenceSpans = ComputeEvidenceSpans(inputKey, evidenceKeys);
        var current = lead;
        IcdIndexNode? pendingJump = null;
        IcdIndexNode? pendingSeeAlsoJump = null;
        // 泛形态学主导词行（M8000 肿瘤 / M8010 癌）自带的 M####/d 是肿瘤表解部位码的唯一入口：
        // 下钻若停在"头段词面根本没写进输入、只靠部位尾段或括号注解沾上"的带码子行，交付码就从
        // 形态学码换成别病种的疾病码，肿瘤链路整条失效——22386 瘤(M8000/1) 的 126 个子行里
        // 脑室动态未定肿瘤 只沾到 －梅毒性，脑 的 脑，换成 A52.1† G94.8* 后覆盖度 2/8=0.25
        // 掉进 0.30 门槛，索引通道返回空；而 tumor_index 用 /1 + 室 本来就解得出 D43.0
        //（字典 D43.002 脑室动态未定肿瘤）。
        // 判据两条都是实测收窄出来的：
        // ① 只保泛码——特异形态码（M9530/0 脑(脊)膜瘤）自己就是病名，下钻是索引给的细分，
        //    一并保护会让 右侧桥小脑角脑膜瘤 丢掉期望码 D32.900X002；泛码链路的部位信息
        //    完全来自肿瘤表，与 resolver 的覆盖度记账用同一条 IsSitelessMorphology 判据。
        // ② 只在主导词就是输入的结尾核心词时启用（与单字主导词、泛术式 手术 同一尾重心判据）：
        //    动脉瘤/淋巴瘤 的 瘤 写在病案词内部，放开会把 C70.9 蛛网膜恶性肿瘤注进血管病案。
        var protectMorphologyLead = IndexCodeExpander.IsSitelessMorphology(lead.Code)
            && initialPathKey.Length > 0
            && inputKey.EndsWith(initialPathKey, StringComparison.Ordinal);

        while (true)
        {
            IcdIndexNode? bestChild = null;
            string bestChildKey = string.Empty;
            string? bestChildMatchKey = null;
            int bestGain = 0;
            var bestRatio = 0m;
            var bestSegment = 0;
            IcdIndexNode[] bestConnectors = [];
            // 路径当前已在输入中占住的片段，用于判定子节点是否带来"新内容"。
            var pathPositions = TextMatch.CoveredTextPositions(pathKey, inputKey);

            var candidates = new List<(IcdIndexNode Node, IcdIndexNode[] Connectors)>();
            CollectDrillCandidates(current, [], candidates, inputQualifiers, inputKey);

            foreach (var (child, connectors) in candidates)
            {
                // 子节点部位词（"－玻璃体"、"－钙化性牙源性"）未在输入声明 → 禁止进入该子树。
                if (!NodeAllows(child, inputQualifiers, inputKey))
                {
                    continue;
                }

                // 纯指针行（无码、无子行）只有当改道真能出发时才准参加本层竞选：
                // 目标解析失败（索引死指针）或解析回已访问节点（自指环——"小脑脑桥(角) - 见 囊肿，脑"
                // 的目标键头词按精确键回落到本主导词自己）时它无任何产出，却常靠输入里散落的
                // 单字残段（桥/角）拿最高词面增益抢走选层权——选层不回溯，整条链路随之落空
                // （桥小脑角蛛网膜囊肿 的 囊肿→蛛网膜，脑(后天性) G93.0 链路因此全灭）。
                // "见情况" 行走末位兜底通道（IsGenericConditionTarget），不在此列。
                if (child.JumpKind is IndexJumpKind.See or IndexJumpKind.SeeAlso
                    && !IsGenericConditionTarget(child.JumpTarget)
                    && child.Code is null
                    && child.Children.Count == 0
                    && (ResolveJumpLead(snapshot, child.JumpTarget ?? string.Empty) is not { } jumpableLead
                        || visited.Contains(jumpableLead.SeqNo)))
                {
                    continue;
                }

                var childBestGain = 0;
                string? childBestKey = null;
                string? childBestMatchKey = null;
                var childBestRatio = 0m;
                var childBestSegment = 0;
                foreach (var alias in child.Aliases)
                {
                    // 子节点必须为链路带来输入的"新内容"（≥2 连续片段，且不是路径已占过的字）：
                    // 只有虚字符（"原因不明致命性" 的 "性"）或重复字
                    // （先兆→流产→"伴有随后性流产" 的 "流产"）可沾的分支不得进入。
                    if (!AliasAddsInputContent(alias.MatchKey, inputKey, pathPositions))
                    {
                        continue;
                    }

                    // P3 证据词面占位：非证据子行的"新增内容"若全部落在证据词面区间内
                    // （结缔组织 靠 被组织学 里的 组织 2 字），挣的是证据锚点的字面而非
                    // 自己的——按无新内容拦截。否则变体输入（肺结核瘤,确定的被组织学）的
                    // 第二层选层被 gain=2 的噪声分支抢占，证据深链（确定的→被组织学 A15.2）
                    // 整支进不去。证据子行自身豁免（它们的词面就住在区间里，见 EvidenceKeysMatch）。
                    if (evidenceSpans.Count > 0
                        && !EvidenceKeysMatch(child, evidenceKeys)
                        && AliasNewContentInsideEvidenceSpans(alias.MatchKey, inputKey, pathPositions, evidenceSpans))
                    {
                        continue;
                    }

                    // 泛形态学主导词行不得被"只靠部位尾段/括号注解沾上"的带码子行顶掉：
                    // 这类子行的头段词面（梅毒性、内分泌多发性）根本没写进输入，本层选它
                    // 就把 M####/d 换成别病种的疾病码，肿瘤表再也拿不到形态学码。
                    // 子行自身带码且不是形态学码才拦——指针行（另见/见）与
                    // 22660 "－脂细胞，卵巢 M8670/0:D27" 这类同族形态学子行照常下钻。
                    if (protectMorphologyLead
                        && alias.Weight < 1.0m
                        && child.Code is { Length: > 0 } childCode
                        && !MorphologyCodeRegex.IsMatch(childCode))
                    {
                        continue;
                    }

                    // 增益按 MatchKey（剥离"(慢性)"等泛化修饰括号）计算：
                    // 修饰词命中不得单独驱动进入核心词与输入无关的子树。
                    // 索引惯例 "主导词 → －部位"（钙化→肾）在输入中常倒置（"肾…钙化"），
                    // 正序/逆序拼接取较大覆盖；单字部位词按包含计增益（仅限遍历，输出门控不变）。
                    var combined = pathKey + alias.MatchKey;
                    var combinedCover = Best(
                        CoverageOfPath(combined, inputKey),
                        CoverageOfPath(TextMatch.Reverse(alias.MatchKey) + TextMatch.Reverse(pathKey), inputKey));
                    // 单字部位词（"－肾"）与主导词在输入中常分离（"肾…钙化"），
                    // LCS 拼段无法连续命中，按整字包含计 1 增益。
                    var singleHit = alias.MatchKey.Length == 1
                        && inputKey.Contains(alias.MatchKey, StringComparison.Ordinal);
                    // 简称部位词（索引 大脑 / 输入 脑）同样挣不到连续覆盖，按单字部位词口径补 1 增益。
                    var siteAbbrev = SiteAbbrevWord(alias.MatchKey, inputKey, pathPositions) is not null;
                    var gain = combinedCover.CoveredChars - currentCover.CoveredChars
                        + ((singleHit || siteAbbrev) && combinedCover.CoveredChars == currentCover.CoveredChars ? 1 : 0);
                    // 同一子节点的多个别名变体取"整条路径被输入覆盖比例"最高者（同比例取增益大者）：
                    // 带澄清括号的长变体（"十二指肠(腐蚀性)(消化性)"）常靠括号里一个游离字
                    // （"可能性大" 的 性）多挣 1 增益，却把 12 个字符的噪声塞进命中路径，
                    // 既挤掉下一层枢纽词（"伴有" 增益归 0），又让字典展开的
                    // "路径词被标题完整覆盖" 放行判定失效。
                    if (gain >= 1
                        && (childBestKey is null
                            || combinedCover.Ratio > childBestRatio
                            || (combinedCover.Ratio == childBestRatio && gain > childBestGain)
                            || (combinedCover.Ratio == childBestRatio && gain == childBestGain
                                && combinedCover.MaxSegment > childBestSegment)))
                    {
                        childBestGain = gain;
                        childBestKey = combined;
                        childBestMatchKey = alias.MatchKey;
                        childBestRatio = combinedCover.Ratio;
                        childBestSegment = combinedCover.MaxSegment;
                    }
                }

                // "见情况 / 另见情况" 指向的是索引惯例泛词（=病，疾病），不是真正的目标主导词：
                // 按编码员查典顺序，它只在**所有主导词都找不到**时才启用，所以本层不跳转，
                // 只把这个跳转子节点交给调用方作末位兜底候选（见 DrillFromRedirectAnchor）。
                // 登记判据用 "词面出现在输入里" 而不是 "挣到下钻增益"："枕大颅神经痛" 的
                // 颅神经 与主导词 神经痛 在输入中重叠，按增益口径它永远进不了候选，
                // 而兜底通道要的只是它的锚点词。
                if (child.JumpKind is IndexJumpKind.See or IndexJumpKind.SeeAlso
                    && IsGenericConditionTarget(child.JumpTarget)
                    && child.Aliases.Any(alias => alias.MatchKey.Length >= 2
                        && inputKey.Contains(alias.MatchKey, StringComparison.Ordinal)))
                {
                    if (genericJumps is not null && !genericJumps.Contains(child))
                    {
                        genericJumps.Add(child);
                    }
                }
                else if (child.JumpKind == IndexJumpKind.See
                    && childBestGain >= 1
                    && pendingJump is null
                    && hops < maxHops)
                {
                    pendingJump = child;
                }
                else if (child.JumpKind == IndexJumpKind.SeeAlso
                    && childBestGain >= 1
                    && pendingSeeAlsoJump is null
                    && hops < maxHops)
                {
                    pendingSeeAlsoJump = child;
                }

                // 同增益兄弟节点取"整条命中路径被输入覆盖比例"更高者：
                // "闭塞→动脉"(闭塞动脉 全在输入) 与 "闭塞→冠状(动脉)"(冠状 不在输入) 增益都是 2，
                // 按序号后来者胜会把链路带到冠状动脉 I21.9；覆盖比例才反映输入真正写出的部位。
                // 同增益同比例再取最长连续段大者：散落单字（桥/角）凑的覆盖是弱证据，
                // 不应压过输入里连续写出的词（蛛网膜），否则选层不回溯会带偏整条链路。
                // P3 证据词优先（最高优先档）：节点词面与证据词面整词包含
                // （"被组织学" ⊂ "确定的被组织学"）且自身挣到 ≥1 增益时，
                // 优先于增益更高的非证据兄弟——输入经同义词改写携带了证据词面，
                // 证据分支是唯一正确去向，字面噪声分支不得抢占。
                var childIsEvidence = EvidenceKeysMatch(child, evidenceKeys);
                var bestIsEvidence = bestChild is not null && EvidenceKeysMatch(bestChild, evidenceKeys);
                // P3 选层证据子树前瞻：证据分支的**祖先层**（结核→肺→确定的→仅被培养 的 肺/确定的）
                // 自身不是证据词面，gain 也可能低于借字面涨分的噪声分支（多发性 靠 继发性 的 发性
                // 2 字）——但其子树能走到证据词面。有证据词时优先保住"通往证据分支"的选层，
                // 否则贪婪选层在第一层就被噪声分支带偏，证据深链整支进不去。
                var childReachesEvidence = childIsEvidence
                    || (evidenceKeys is { Count: > 0 }
                        && SubtreeReachesEvidence(child, evidenceKeys, EvidenceDescentDepth));
                var bestReachesEvidence = bestChild is not null
                    && (bestIsEvidence
                        || (evidenceKeys is { Count: > 0 }
                            && SubtreeReachesEvidence(bestChild, evidenceKeys, EvidenceDescentDepth)));
                // 完全锚定的竞争分支不受前瞻抢占：血行播散性肺结核…涂阴培阳 的 播散性
                // （别名逐字在输入里、gain=3）是输入明确点名的分布分支——证据前瞻只能从
                // "借字面涨分的噪声分支"（多发性 靠 继发性 的 发性、结缔组织 靠 被组织学 的
                // 组织）手里抢选层权，不能从输入点名的分支手里抢（否则 A19 血行播散性
                // 整族被拖进 A15 肺结核，万条 CAP 实测 6 条族级回归）。
                var bestFullyAnchored = bestChildMatchKey is { Length: > 0 } bestMatchKey
                    && TextMatch.CoverageOf(bestMatchKey, inputKey).Ratio >= 1.0m;
                if (childBestKey is not null
                    && childBestGain >= 1
                    && (childReachesEvidence && !bestReachesEvidence && !bestFullyAnchored
                        || (childReachesEvidence == bestReachesEvidence
                            && (childIsEvidence && !bestIsEvidence
                                || (childIsEvidence == bestIsEvidence
                                    && (childBestGain > bestGain
                                        || (childBestGain == bestGain && childBestRatio > bestRatio)
                                        || (childBestGain == bestGain && childBestRatio == bestRatio
                                            && childBestSegment > bestSegment)))))))
                {
                    bestGain = childBestGain;
                    bestRatio = childBestRatio;
                    bestSegment = childBestSegment;
                    bestChild = child;
                    bestChildKey = childBestKey;
                    bestChildMatchKey = childBestMatchKey;
                    bestConnectors = connectors;
                }
            }

            if (bestChild is null || bestGain < 1)
            {
                break;
            }

            // 途经的连接行进链路：证据链要能让编码员看到码是从 见于(由于) 下面哪一支取的。
            foreach (var connector in bestConnectors)
            {
                path.Add(connector);
            }

            current = bestChild;
            path.Add(current);
            pathKey = bestChildKey;
            expansionPathKey += bestChildMatchKey;
            currentCover = CoverageOfPath(pathKey, inputKey);
        }

        var local = BuildResult(current, CodeNodeOf(path), path, pathKey, inputKey, jumps, expansionPathKey);

        // "见 X" 改道与本树下钻并列取优：跳转子节点仅靠虚字符挣到增益时
        // （"阻塞(性)" 下 "器官或部位,先天性NEC -见 闭锁"）不得劫走 J44.9 链路。
        if (pendingJump is { } jumpChild
            && ResolveJumpLead(snapshot, jumpChild.JumpTarget ?? string.Empty) is { } jumpLead
            && NodeAllows(jumpLead, inputQualifiers, inputKey)
            && visited.Add(jumpLead.SeqNo))
        {
            var jumpAlias = JumpSeedKey(jumpLead, inputKey);
            var jumpTrace = new List<string> { $"{jumpChild.Term} → {jumpChild.JumpTarget}" };
            var jumped = DrillFromLead(
                snapshot, jumpLead, jumpAlias, CanonicalLeadKey(jumpLead, jumpAlias), inputKey,
                hops + 1, maxHops, visited, jumpTrace, genericJumps);
            if (jumped is not null)
            {
                // 见-1（见 + 具体主导词必须转换）：指针行本身不给可采用的码，本地只能回退祖先泛化码时
                // （"β脂蛋白血症，宽或上浮 → －过度 -见 高β脂蛋白血症" 的 E78.2），
                // 祖先兜底码不具备与本地自码同等的对抗资格，直接让位给跳转链路；
                // 跳转链路自己也只是祖先兜底时不在此列，仍按覆盖数定优。
                var mandatory = local is { CodeFromAncestorFallback: true } && !jumped.CodeFromAncestorFallback;
                if (local is null
                    || mandatory
                    || CoveredInputChars(jumped.PathKey, inputKey)
                        >= CoveredInputChars(local.PathKey, inputKey))
                {
                    // 让位不等于丢弃：改道链路展不开时由调用方回退本地链路，
                    // "必须转换" 不以输入失去推荐为代价。
                    return mandatory ? jumped with { Displaced = local } : jumped;
                }
            }
        }

        // "另见 X" 改道只在本地下钻停在该跳转子节点（其下没找到更深修饰词）、
        // 且本地链路只能靠祖先兜底取码时生效："闭塞，闭合 → 动脉 -另见 栓塞，动脉"
        // 在输入没有更深分支可下时进入 栓塞→动脉 拿 I74.x；
        // 本地已有自码（闭塞→动脉→…自带编码）则绝不被另见劫走。
        if (pendingSeeAlsoJump is { } alsoChild
            && ReferenceEquals(current, alsoChild)
            && (local is null || local.CodeFromAncestorFallback)
            && ResolveJumpLead(snapshot, alsoChild.JumpTarget ?? string.Empty) is { } alsoLead
            && NodeAllows(alsoLead, inputQualifiers, inputKey)
            && visited.Add(alsoLead.SeqNo))
        {
            var alsoAlias = JumpSeedKey(alsoLead, inputKey);
            var alsoTrace = new List<string> { $"{alsoChild.Term} ⇢ {alsoChild.JumpTarget}" };
            var alsoJumped = DrillFromLead(
                snapshot, alsoLead, alsoAlias, CanonicalLeadKey(alsoLead, alsoAlias), inputKey,
                hops + 1, maxHops, visited, alsoTrace);
            if (alsoJumped is not null && !alsoJumped.CodeFromAncestorFallback)
            {
                return alsoJumped;
            }
        }

        // 另见-1（本行带编码）：病案信息简单时直接采用本行编码，Top1 不动；
        // 但索引在 另见 处明示了"推荐主导词下才有细码"（分娩，剖宫产 → 瘢痕 → 子宫 O34.2），
        // 输入里写出更详细情况时把细码作为并列提示交给调用方——这是发现"诊断漏填修饰词"的唯一途径。
        // "本行" 必须是链路真正停住的那一行：pendingSeeAlsoJump 只记录层内第一个挣到增益的 另见 兄弟，
        // 不加这层等值判断时 腘动脉闭塞 会拿 动脉 的兄弟 冠状(另见 梗死，心肌) 出题，
        // 把 心肌梗死 I21.9 塞进外周动脉闭塞的候选里。
        // 码相同但链路更详细时也允许提示（矮小→身材 NEC E34.3 vs 侏儒症→先天性 E34.3）。
        // 另见-1（本行带编码）：病案信息简单时直接采用本行编码，Top1 不动；
        // 但索引在 另见 处明示了"推荐主导词下才有细码"（分娩，剖宫产 → 瘢痕 → 子宫 O34.2），
        // 输入里写出更详细情况时把细码作为并列提示交给调用方——这是发现"诊断漏填修饰词"的唯一途径。
        // "本行" 必须是链路真正停住的那一行：pendingSeeAlsoJump 只记录层内第一个挣到增益的 另见 兄弟，
        // 不加这层等值判断时 腘动脉闭塞 会拿 动脉 的兄弟 冠状(另见 梗死，心肌) 出题，
        // 把 心肌梗死 I21.9 塞进外周动脉闭塞的候选里。
        // 码相同但链路更详细时也允许提示（矮小→身材 NEC E34.3 vs 侏儒症→先天性 E34.3）。
        if (pendingSeeAlsoJump is { } hintChild
            && ReferenceEquals(current, hintChild)
            && local is { CodeFromAncestorFallback: false }
            && ResolveJumpLead(snapshot, hintChild.JumpTarget ?? string.Empty) is { } hintLead
            && NodeAllows(hintLead, inputQualifiers, inputKey)
            && !visited.Contains(hintLead.SeqNo)
            && DrillFromLead(
                snapshot, hintLead, JumpSeedKey(hintLead, inputKey),
                CanonicalLeadKey(hintLead, JumpSeedKey(hintLead, inputKey)), inputKey,
                hops + 1, maxHops, [.. visited, hintLead.SeqNo],
                [$"{hintChild.Term} ⇢ {hintChild.JumpTarget}"], genericJumps: null) is { } alt
            && alt.Path.Count > 1
            && !alt.CodeFromAncestorFallback)
        {
            return local with { SeeAlsoHints = [alt] };
        }

        return local;
    }

    /// <summary>
    /// "见情况" 式失效主导词行的改道入口：索引惯例 "X的 -见 情况" 意为
    /// "到泛化主导词 病，疾病 下找部位修饰词 X"，所以锚点词本身就是要定位的子节点，
    /// 从 病，疾病 的子层精确匹配（"胆囊的" 去属格 "的" → 胆囊 K82.9），
    /// 而不是从根节点空种子自由下钻——单字符泛词（"儿童期慢性大疱性" 的 期/性/大）
    /// 的增益会高于真正的部位词，把链路带偏。
    /// </summary>
    public static DrillResult? DrillFromRedirectAnchor(
        IcdIndexSnapshot snapshot,
        IcdIndexNode lead,
        string anchorKey,
        string inputKey,
        string jumpTrace)
    {
        var core = anchorKey.TrimEnd('的');
        if (core.Length < 2)
        {
            return null;
        }

        var inputQualifiers = TextMatch.ExtractQualifiers(inputKey);
        foreach (var child in lead.Children)
        {
            if (!NodeAllows(child, inputQualifiers, inputKey))
            {
                continue;
            }

            var alias = child.Aliases.FirstOrDefault(item => item.MatchKey == core || item.Key == core);
            if (alias is null)
            {
                continue;
            }

            return DrillFromLead(
                snapshot, child, alias.MatchKey, CanonicalLeadKey(child, alias.MatchKey), inputKey,
                hops: 0, maxHops: 1, visited: [lead.SeqNo, child.SeqNo], jumps: [jumpTrace]);
        }

        return null;
    }

    private static int CoveredInputChars(string pathKey, string inputKey)
        => CoverageOfPath(pathKey, inputKey).CoveredChars;

    /// <summary>
    /// 跳转目标主导词的种子命中键：取与输入贴合度最高的别名（同覆盖字数取覆盖比例高者）。
    /// Aliases[0] 常是带澄清括号的整词（"栓塞(脓毒性)" 的 栓塞脓毒性），
    /// 把它拼进命中路径会让字典展开的"路径词被标题完整覆盖"放行判定失效，
    /// 只能回退推荐索引码本身（I74.9 展不开 I74.902 动脉栓塞）。
    /// public：编码录入门面板（NameCodingLookupEngine）挂主导词行自身的 另见-1 提示时同口径复用。
    /// </summary>
    public static string JumpSeedKey(IcdIndexNode lead, string inputKey)
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
    /// 索引部位词的构词性前缀：只用于把 "大脑"（索引对 脑 的规范全称）
    /// 与 "颞骨/舟骨" 这类 "下位具体部位" 区分开。
    /// 中/外/上/下/前/后/内/左/右 不进表：它们改变解剖子区，会让裸部位输入被过细分
    /// （"耳瘢痕" 被下钻到 "－中耳"），与精度优先冲突。
    /// </summary>
    private static readonly HashSet<char> MorphologicalPrefixes =
        new(['大', '小']);

    /// <summary>
    /// 子节点别名能否给链路增加输入中的新内容。
    /// 单字部位词（"－肾"、"－肺"）按整字包含放行；其余要求有 ≥2 字符的连续片段
    /// 尚未被路径占住，挡住靠虚字符与重复字挣增益的下钻。
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

        // 索引连接枢纽词 "伴有"（"溃疡→十二指肠→伴有→出血"）临床常简写为单字 "伴"：
        // 整词没有 ≥2 连续公共片段，位置口径恒为 0，故改为要求 伴/有 任一字符
        // 落在输入中尚未被路径占位的位置；下钻增益门槛(≥1)仍拦住 0 新字的搭车分支。
        if (matchKey == "伴有")
        {
            return matchKey.Any(character =>
            {
                var position = inputKey.IndexOf(character);
                return position >= 0 && !pathPositions.Contains(position);
            });
        }

        var newPositions = TextMatch.CoveredTextPositions(matchKey, inputKey)
            .Except(pathPositions)
            .ToList();
        if (newPositions.Count >= 2 && !IsEtiologyTailResidue(newPositions, matchKey, inputKey))
        {
            return true;
        }

        // 索引子节点写全称部位词（"－大脑"），病历按习惯只写简称（"脑动脉…"）时，
        // CoveredTextPositions 只登记 ≥2 连续片段，孤立的 脑 恒为 0 新字、被门槛彻底挡死，
        // 下钻停在 "动脉" 这一层泛码上。只对最窄口径放行：别名整体是 "构词前缀+部位词"
        // （见 MorphologicalPrefixes），其余别名内含部位字的形态
        // （腹壁、颞骨、舟骨）与方位前缀的过细分（耳瘢痕→中耳）实测都是正误各半，不放开。
        return SiteAbbrevWord(matchKey, inputKey, pathPositions) is not null;
    }

    /// <summary>
    /// 被包含主导词的实词下钻豁免：直接子行里存在 MatchKey 不属于泛化修饰词、
    /// 且能在输入中挣到内容的子行（多字 ≥2 连续段、单字整字命中——痛→－腹→上 R10.1
    /// 的"腹"是单字实词）时，它仍有独立产出，不得因跨度包含被整体剔除。
    /// 只拦"下钻增益全部来自泛化词"的：炎→－慢性，乳突切除术后空腔（慢性 在
    /// <see cref="GenericModifierKeys"/> 里，乳突… 在输入中零命中）——慢性肺炎 的
    /// "炎"正是靠"慢性"两个字搭车进耳科的。流水线召回（RecallLeads）与编码录入
    /// 引擎（NameCodingLookupEngine）共用同一口径。
    /// </summary>
    public static bool HasSubstantiveChildDrill(IcdIndexNode lead, string inputKey)
    {
        foreach (var child in lead.Children)
        {
            foreach (var alias in child.Aliases)
            {
                var matchKey = alias.MatchKey;
                if (matchKey.Length == 0 || GenericModifierKeys.Contains(matchKey))
                {
                    continue;
                }

                if (matchKey.Length == 1
                    ? inputKey.Contains(matchKey, StringComparison.Ordinal)
                    : TextMatch.CoveredTextPositions(matchKey, inputKey).Count >= 2)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 新增内容是否只是 "…病 + 性" 的跨词残段。索引的病因修饰行把病种写在头段
    /// （放线菌病性 的 放线菌、糖尿病性 的 糖尿），"病性" 只是形容词后缀的构词产物；
    /// 输入 "艾滋病性脑膜炎" 的 病性 = 艾滋病 的尾字 + 后缀，零病因证据却能过 ≥2 新字门槛，
    /// 而选层不回溯，整条 脑膜炎 子树因此停在 放线菌病性（A42.8? G01*）上。
    /// 只拦"残段就是全部新内容"这一种：真写出 糖尿病性 时新字有 4 个，左侧字也在别名内。
    /// 库内 83 行以 病性 收尾（糖尿病性 25 行、淋病性/性病性/神经病性/精神病性/佝偻病性…），
    /// 任何写了 "X病性" 的输入都可能被其中一行按 2 字残段劫走。
    /// </summary>
    private static bool IsEtiologyTailResidue(List<int> newPositions, string matchKey, string inputKey)
    {
        if (newPositions.Count != 2 || !matchKey.EndsWith("病性", StringComparison.Ordinal))
        {
            return false;
        }

        var start = Math.Min(newPositions[0], newPositions[1]);
        if (Math.Abs(newPositions[0] - newPositions[1]) != 1
            || start + 2 > inputKey.Length
            || inputKey[start] != '病' || inputKey[start + 1] != '性')
        {
            return false;
        }

        return start == 0 || !matchKey.Contains(inputKey[start - 1]);
    }

    /// <summary>
    /// 别名是否为 "构词性前缀 + 输入已声明、且尚未被路径占位的部位词"（部位词在末尾）。
    /// 索引 "－大脑" 对输入 "脑动脉供血不足" 成立；索引 "－颞骨"、"－舟骨" 对输入 "...骨..."
    /// 属下位具体部位，不成立。命中返回该部位词，否则 null。
    /// </summary>
    private static string? SiteAbbrevWord(
        string matchKey,
        string inputKey,
        HashSet<int> pathPositions)
    {
        foreach (var site in TextMatch.ExtractQualifiers(inputKey))
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

    private static DrillResult? BuildResult(
        IcdIndexNode terminal,
        IcdIndexNode? codeNode,
        List<IcdIndexNode> path,
        string pathKey,
        string inputKey,
        List<string> jumps,
        string? expansionPathKey = null)
    {
        if (codeNode is null)
        {
            return null;
        }

        var cover = CoverageOfPath(pathKey, inputKey);
        return new DrillResult(
            terminal,
            codeNode,
            path,
            pathKey,
            cover.Ratio,
            !ReferenceEquals(codeNode, terminal),
            jumps.AsReadOnly(),
            ExpansionPathKey: expansionPathKey);
    }

    /// <summary>
    /// 节点是否命中证据词面（P3 选层证据词优先）：**整词包含**判据——
    /// 证据词面包含节点核心词（"确定的被组织学" ⊇ 被组织学）或节点核心词包含证据词，
    /// 且不含"共享单字/双字散落重叠"的弱形态（结缔组织 vs 被组织学 只共享 组织 2 字）。
    /// 判据用核心词（CoreTerm，剥括号）**与各别名 MatchKey**：真实索引行常带解释尾段
    /// （"被显微镜下痰检查，伴有或不伴有痰培养"），尾段让核心词与证据词面
    /// （确定的被显微镜下痰检查）的整词包含失效——别名才是实际参与匹配的词面。
    /// 证据词由调用方从同义词等价组提取（GroupMembersForText），无证据词时恒 false。
    /// </summary>
    private static bool EvidenceKeysMatch(IcdIndexNode node, IReadOnlyList<string>? evidenceKeys)
    {
        if (evidenceKeys is null || evidenceKeys.Count == 0)
        {
            return false;
        }

        foreach (var face in NodeEvidenceFaces(node))
        {
            if (face.Length < 2)
            {
                continue;
            }

            foreach (var evidence in evidenceKeys)
            {
                var evidenceKey = TextMatch.NormalizeKey(evidence);
                if (evidenceKey.Length < 2)
                {
                    continue;
                }

                // 整词包含：证据词面含节点词面（确定的被组织学 ⊇ 被组织学）
                // 或节点词面含证据词面（罕见反向）。
                if (evidenceKey.Contains(face, StringComparison.Ordinal)
                    || face.Contains(evidenceKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>节点的证据判据词面：核心词 + 各别名 MatchKey（去重，归一化）。</summary>
    private static IEnumerable<string> NodeEvidenceFaces(IcdIndexNode node)
    {
        var core = TextMatch.NormalizeKey(node.CoreTerm);
        if (core.Length >= 2)
        {
            yield return core;
        }

        foreach (var alias in node.Aliases)
        {
            var matchKey = TextMatch.NormalizeKey(alias.MatchKey);
            if (matchKey.Length >= 2 && !string.Equals(matchKey, core, StringComparison.Ordinal))
            {
                yield return matchKey;
            }
        }
    }

    /// <summary>
    /// 证据分支前瞻层数：证据词面通常在噪声分支被抢层级的下方 2-3 层
    /// （结核→肺→确定的→被组织学/仅被培养）。前瞻只判定"能否走到证据词面"，
    /// 不改写选层本身，层数封顶避免大子树的扫描开销。
    /// </summary>
    private const int EvidenceDescentDepth = 3;

    /// <summary>
    /// 节点子树内（限 <paramref name="depth"/> 层）是否存在证据词面命中的节点
    /// （P3 选层证据子树前瞻）：命中即当前节点是"通往证据分支"的祖先层，
    /// 选层时优先保住它。node 自身命中也算。
    /// </summary>
    private static bool SubtreeReachesEvidence(
        IcdIndexNode node,
        IReadOnlyList<string> evidenceKeys,
        int depth)
    {
        if (EvidenceKeysMatch(node, evidenceKeys))
        {
            return true;
        }

        if (depth <= 0)
        {
            return false;
        }

        foreach (var child in node.Children)
        {
            if (SubtreeReachesEvidence(child, evidenceKeys, depth - 1))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 证据词面在输入中的占位区间（P3 证据词门控的选层侧判据）：
    /// 证据等价组成员（病理(+) → 确定的被组织学/组织学(+)/病理检查(+)）归一化后
    /// 在输入键里的连续出现位置，0 基、右开。无证据词或词面未出现返回空。
    /// </summary>
    private static List<(int Start, int End)> ComputeEvidenceSpans(
        string inputKey,
        IReadOnlyList<string>? evidenceKeys)
    {
        var spans = new List<(int Start, int End)>();
        if (evidenceKeys is null || evidenceKeys.Count == 0 || inputKey.Length < 2)
        {
            return spans;
        }

        foreach (var evidence in evidenceKeys)
        {
            var face = TextMatch.NormalizeKey(evidence);
            if (face.Length < 2)
            {
                continue;
            }

            var start = 0;
            while ((start = inputKey.IndexOf(face, start, StringComparison.Ordinal)) >= 0)
            {
                spans.Add((start, start + face.Length));
                start += 1;
            }
        }

        return spans;
    }

    /// <summary>
    /// 别名的"新增内容"是否全部落在证据词面占位区间内（P3 证据词面占位拦截）：
    /// 噪声分支（结缔组织(全身性) 靠 被组织学 里的 组织 2 字）借证据锚点的字面挣
    /// 新增内容去抢占选层——其字面实为证据分支的保留词面，按无新内容拦截。
    /// 部分重叠（区间外还带来真内容）不拦；单字别名走整字包含门，不进本判据。
    /// </summary>
    private static bool AliasNewContentInsideEvidenceSpans(
        string matchKey,
        string inputKey,
        HashSet<int> pathPositions,
        List<(int Start, int End)> evidenceSpans)
    {
        if (matchKey.Length < 2 || evidenceSpans.Count == 0)
        {
            return false;
        }

        var newPositions = TextMatch.CoveredTextPositions(matchKey, inputKey)
            .Except(pathPositions)
            .ToList();
        if (newPositions.Count == 0)
        {
            return false;
        }

        return newPositions.All(position => evidenceSpans.Any(span =>
            position >= span.Start && position < span.End));
    }

    /// <summary>
    /// 节点的规范形别名键：权重 1.0 别名里取最短 MatchKey —— 逗号切分首段是索引书主词条目
    /// （"梗死，梗塞" 的 梗死），装饰节点（"瘢痕(粘连)(挛缩)…"）的首段是长装饰串，
    /// 剥括号的裸头段（瘢痕）才是与字典标题对齐的规范形。
    /// 无 1.0 权重别名时回落到调用方给的命中键。
    /// </summary>
    private static string CanonicalLeadKey(IcdIndexNode node, string fallback)
        => node.Aliases.Where(alias => alias.Weight >= 1.0m)
            .OrderBy(alias => alias.MatchKey.Length)
            .Select(alias => alias.MatchKey)
            .FirstOrDefault(key => key.Length > 0) is { } canonical
            ? canonical
            : fallback;

    /// <summary>无码终点回退最近有码祖先；全链无码返回 null。</summary>
    private static IcdIndexNode? CodeNodeOf(List<IcdIndexNode> path)
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

    private static TextMatch.Coverage Best(TextMatch.Coverage left, TextMatch.Coverage right)
        => left.CoveredChars >= right.CoveredChars ? left : right;

    // 下钻遍历专用覆盖口径：单字符残段按包含计（"肾" 参与 "钙化→肾" 增益）。
    private static TextMatch.Coverage CoverageOfPath(string fragmentKey, string inputKey)
        => TextMatch.CoverageOf(fragmentKey, inputKey, countSingleCharSegments: true);

    /// <summary>
    /// 该别名键是否属于"不能当主导词用"的泛化词（部位段/人群段/形态尾段/病程极）。
    /// 召回层用它拉黑，失效主导词改道层同样要用它：索引里 "新生儿 -见 母体情况" 式行
    /// 说的是 新生儿 这个人群段，不是主导词，放行会把 新生儿黄疸 P57.9 顶成 P00.9。
    /// </summary>
    public static bool IsGenericLeadKey(string aliasKey) => GenericModifierKeys.Contains(aliasKey);

    /// <summary>
    /// 跳转目标是否为索引惯例里的泛词"情况"（= 主导词 病，疾病）。
    /// 这类改道不是真正的目标主导词，按编码员查典顺序只能在**所有主导词都找不到**后启用，
    /// 所以匹配器遇到它只登记不跳转（见 IcdIndexDiagnosisResolver 的末位兜底通道）。
    /// </summary>
    public static bool IsGenericConditionTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var first = target.Split(['，', ',', '、'], StringSplitOptions.RemoveEmptyEntries)
            .Select(TextMatch.NormalizeKey)
            .FirstOrDefault(key => key.Length > 0);
        return first is "情况" or "疾病" or "病";
    }

    /// <summary>
    /// 解析 "脓肿，阿米巴" / "阿米巴病" 式跳转目标为主导词节点。
    /// 索引惯例里 "X的 -见情况" 的 "情况" 不是主导词文本，指的是泛化主导词 "病，疾病"，按此映射。
    /// </summary>
    public static IcdIndexNode? ResolveJumpLead(IcdIndexSnapshot snapshot, string target)
    {
        var segments = target.Split(['，', ',', '、'], StringSplitOptions.RemoveEmptyEntries)
            .Select(TextMatch.NormalizeKey)
            .Where(key => key.Length >= 2)
            .Select(key => key == "情况" ? "疾病" : key)
            .ToList();
        if (segments.Count == 0)
        {
            return null;
        }

        if (snapshot.LeadByExactKey.TryGetValue(segments[0], out var exact))
        {
            return exact;
        }

        var concatenated = string.Concat(segments);
        if (snapshot.LeadByExactKey.TryGetValue(concatenated, out var joined))
        {
            return joined;
        }

        // 目标可能带修饰尾巴，退化为召回打分定位。
        return RecallLeads(snapshot, concatenated, topK: 1).FirstOrDefault()?.Node;
    }

    /// <summary>
    /// 失效主导词的"现译名/见"重定向：输入键完全命中重定向表时给出目标文本。
    /// </summary>
    public static string? ResolveRedirect(IcdIndexSnapshot snapshot, string inputKey)
    {
        return snapshot.Redirects.GetValueOrDefault(inputKey);
    }
}
