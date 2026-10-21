using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Coding.IcdIndex;

public sealed record LeadWordHit(IcdIndexNode Node, IcdIndexAlias Alias, decimal CoverRatio, int CoveredChars);

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
    IReadOnlyList<DrillResult>? SeeAlsoHints = null);

/// <summary>
/// 主导词召回 + 逐级修饰词下钻。
/// 召回门控：别名被输入完整/高覆盖命中（TextMatch.MatchesInput），且词条部位限定词须在输入中声明；
/// 单字主导词只有在它是输入结尾的核心词时才参与召回（见 IsTailCoreWord）；
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
            && !TextMatch.HasPhasePoleConflict(node.Term, inputKey)
            && !TextMatch.HasNegationPoleConflict(node.CoreTerm, inputKey);

    /// <summary>ICD-O 形态学码（M8010/3、M9800/3:C95.9 的形态学段）。</summary>
    private static readonly System.Text.RegularExpressions.Regex MorphologyCodeRegex =
        new(@"M\d{4}/\d", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>本行（或本行编码组里任一码）是形态学码 → 肿瘤形态学树。</summary>
    private static bool IsMorphologyLead(IcdIndexNode lead)
        => lead.Code is { Length: > 0 } code && MorphologyCodeRegex.IsMatch(code);

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
    /// 索引惯例里的泛化主导词族（情况 = 病，疾病 一族）。它们不携带"这行说的是哪个病"，
    /// 召回层与失效主导词改道层都要用它拦住，只有末位兜底通道允许进入。
    /// </summary>
    public static bool IsGenericConditionLeadKey(string aliasKey)
        => aliasKey is "病" or "疾病" or "情况" or "疾患" or "综合征";

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
                if (!alias.RecallAnchor || GenericModifierKeys.Contains(alias.Key)
                    || IsGenericSurgeryAliasOutsideTail(alias.Key, inputKey)
                    || (alias.Key.Length < 2 && !IsMorphologyLead(lead) && !IsTailCoreWord(alias.Key, inputKey)))
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
                    best = new LeadWordHit(lead, alias, coverage.Ratio, coverage.CoveredChars);
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
        string? jumpTrace = null)
    {
        return DrillFromLead(
            snapshot,
            leadHit.Node,
            leadHit.Alias.Key,
            inputKey,
            hops: 0,
            maxHops,
            visited: [leadHit.Node.SeqNo],
            jumps: jumpTrace is null ? [] : [jumpTrace],
            genericJumps: genericJumps);
    }

    private static DrillResult? DrillFromLead(
        IcdIndexSnapshot snapshot,
        IcdIndexNode lead,
        string initialPathKey,
        string inputKey,
        int hops,
        int maxHops,
        HashSet<int> visited,
        List<string> jumps,
        List<IcdIndexNode>? genericJumps = null)
    {
        var path = new List<IcdIndexNode> { lead };
        var pathKey = initialPathKey;
        var inputQualifiers = TextMatch.ExtractQualifiers(inputKey);
        var currentCover = CoverageOfPath(pathKey, inputKey);
        var current = lead;
        IcdIndexNode? pendingJump = null;
        IcdIndexNode? pendingSeeAlsoJump = null;

        while (true)
        {
            IcdIndexNode? bestChild = null;
            string bestChildKey = string.Empty;
            int bestGain = 0;
            var bestRatio = 0m;
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

                var childBestGain = 0;
                string? childBestKey = null;
                var childBestRatio = 0m;
                foreach (var alias in child.Aliases)
                {
                    // 子节点必须为链路带来输入的"新内容"（≥2 连续片段，且不是路径已占过的字）：
                    // 只有虚字符（"原因不明致命性" 的 "性"）或重复字
                    // （先兆→流产→"伴有随后性流产" 的 "流产"）可沾的分支不得进入。
                    if (!AliasAddsInputContent(alias.MatchKey, inputKey, pathPositions))
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
                            || (combinedCover.Ratio == childBestRatio && gain > childBestGain)))
                    {
                        childBestGain = gain;
                        childBestKey = combined;
                        childBestRatio = combinedCover.Ratio;
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

            // 途经的连接行进链路：证据链要能让编码员看到码是从 见于(由于) 下面哪一支取的。
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
                snapshot, jumpLead, jumpAlias, inputKey,
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
                snapshot, alsoLead, alsoAlias, inputKey,
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
        if (pendingSeeAlsoJump is { } hintChild
            && ReferenceEquals(current, hintChild)
            && local is { CodeFromAncestorFallback: false }
            && ResolveJumpLead(snapshot, hintChild.JumpTarget ?? string.Empty) is { } hintLead
            && NodeAllows(hintLead, inputQualifiers, inputKey)
            && !visited.Contains(hintLead.SeqNo)
            && DrillFromLead(
                snapshot, hintLead, JumpSeedKey(hintLead, inputKey), inputKey,
                hops + 1, maxHops, [.. visited, hintLead.SeqNo],
                [$"{hintChild.Term} ⇢ {hintChild.JumpTarget}"], genericJumps: null) is { } alt
            && alt.Path.Count > 1
            && !alt.CodeFromAncestorFallback
            && alt.CodeNode.Code != local.CodeNode.Code)
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
                snapshot, child, alias.MatchKey, inputKey,
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
    /// </summary>
    private static string JumpSeedKey(IcdIndexNode lead, string inputKey)
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
        if (newPositions.Count >= 2)
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
        List<string> jumps)
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
            jumps.AsReadOnly());
    }

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
