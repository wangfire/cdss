using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Coding.IcdIndex;

/// <summary>ICD 索引树快照来源（平台级共享）。</summary>
public interface IIcdIndexSource
{
    IcdIndexSnapshot GetSnapshot(CancellationToken cancellationToken = default);

    void Invalidate();
}

/// <summary>医学编码字典快照来源（按医院 + 编码体系）。</summary>
public interface IIcdDictionarySource
{
    IcdDictionarySnapshot GetDictionary(
        Guid hospitalId,
        string codeSystemCode,
        CancellationToken cancellationToken = default);

    void Invalidate();
}

public sealed record DiagnosisIndexRecommendation(
    string CodeSystemCode,
    string Code,
    string Title,
    decimal Score,
    bool IsIndexFallback,
    bool IsConditionalAddOn,
    string EvidencePath,
    string? Note,
    /// <summary>整标题逐字贴合度（标题长/输入长），供管道复排时保住"逐字完整标题优先"口径。</summary>
    decimal WholeTitleDepth = 0m,
    /// <summary>同族（同一索引编码展开批次）内的精排名次，供下游抗重排噪声。</summary>
    int FamilyRank = 0,
    /// <summary>产出本条的索引编码（同族粒度菜单的共同身份）。名额分配按族计，不按键计。</summary>
    string? IndexFamilyCode = null);

/// <summary>
/// 诊断输入的两段式解析：主导词召回 → 逐级下钻 → 索引编码格式解析 → 字典展开。
/// 诊断侧不再回退字典直匹配（决策 2）：无主导词命中即返回空。
/// 索引命中但字典展不开 → 回退推荐索引编码本身（IsIndexFallback）。
/// 输入按术语同义词生成规范变体（原文 + 改写文各跑一遍，按编码去重取高分）；
/// "X的 -见情况" 式失效主导词行命中时改道 病，疾病 主导词下钻部位修饰词。
/// </summary>
public sealed class IcdIndexDiagnosisResolver(int leadWordTopK, int maxResults, string diagnosisCodeSystem)
{
    /// <summary>
    /// "见情况/另见情况" 泛词改道候选：文本变体 + 可在 病，疾病 下锚定的别名键 + 证据链标注。
    /// 只在常规主导词全链路无产出时才执行。
    /// </summary>
    private sealed record ConditionFallback(string InputText, IReadOnlyList<string> AnchorKeys, string Trace);

    /// <summary>
    /// 泛化主导词族（查典顺序）：情况 → 病，疾病(2972)，再 疾患(16842)、综合征(50429)。
    /// 索引里三行互为 另见 指针，编码员口径写的是"病，疾病、疾患"。
    /// </summary>
    private static readonly string[] GenericConditionFamily = ["情况", "疾患", "综合征"];

    public IReadOnlyList<DiagnosisIndexRecommendation> Resolve(
        IcdIndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        string inputText,
        IReadOnlyList<string>? aliasTexts = null,
        IcdTermSynonymSnapshot? synonyms = null,
        CancellationToken cancellationToken = default)
    {
        var texts = new List<string> { inputText };
        if (aliasTexts is { Count: > 0 })
        {
            texts.AddRange(aliasTexts.Where(text => text.Length > 0 && !texts.Contains(text)));
        }

        if (synonyms is not null)
        {
            texts.AddRange(synonyms
                .ExpandVariants(inputText, TermSynonymTypes.Diagnosis)
                .Where(text => text.Length > 0 && !texts.Contains(text)));
        }

        // 索引自带的 "又称" 等价译名（癫癎↔癫痫、卒中↔中风、土拉菌病↔兔热病…共 13 条 0 级行）：
        // 主导词用字与临床写法不一致时常规召回全空，必须在输入侧补一路改写变体，
        // 否则这类输入只能靠别的行的并列尾段蒙码（万条实测：8 条含 癫痫 的输入全部走不到
        // 主导词 癫癎(性) G40.9，改前靠 "杰克逊癫癎或发作" 的 发作 尾段拿到 G40.100x）。
        texts.AddRange(snapshot.RewriteByAnotherNames(inputText)
            .Where(text => text.Length > 0 && !texts.Contains(text)));

        // 病历缩写 "X术后"：索引管这件事的主导词是 `50034 状态`（82 子行，其中 33 行就是
        // "－甲状腺切除术后/－胆囊切除术后/－手术后 NEC" 这类术式行）与 `35948 手术后状态 NEC Z98.8`，
        // 词面写的是 状态/手术后，缩写 术后 一字不沾 → 整条链路召不回，输入被 甲状腺肿 E04.9
        // 这类"活动性疾病"主导词借走（万条实测：1361 条含 术后 的输入里 808 条 Top1 是疾病码、
        // 落 Z98 的只有 4 条）。补两路变体：带术式前缀的取索引给具体术式的精确码
        // （甲状腺切除术后 → E89.000 甲状腺切除后甲状腺功能减退），泛词的取状态兜底码
        // （手术后状态 → Z98.8 → Z98.800，医保对照口径就是这条）。
        texts.AddRange(PostoperativeStateVariants(inputText)
            .Where(text => text.Length > 0 && !texts.Contains(text)));

        var ranked = new List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)>();
        // 另见-1 提示候选：只在常规链路的名额没占满时补位，不参与 Top1 竞争。
        var hints = new List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)>();
        // "见情况/另见情况" 的泛词改道候选：按编码员查典顺序，只有常规主导词全部落空才启用，
        // 所以先登记不下钻，避免 "2.直肠肿瘤" 被 直肠-见情况→K62.9 抢在 肿瘤 主导词之前。
        var conditionFallbacks = new List<ConditionFallback>();
        foreach (var text in texts)
        {
            ResolveText(snapshot, dictionary, text, ranked, hints, conditionFallbacks, cancellationToken);
        }

        if (ranked.Count == 0 && conditionFallbacks.Count > 0)
        {
            foreach (var fallback in conditionFallbacks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RunConditionFallback(snapshot, dictionary, fallback, ranked);
            }
        }

        // 整标题贴合度优先于链路分数：标题被输入逐字连续包含（且解释掉 ≥75% 输入字符）时，
        // 编码员看的就是这一条，其它链路的相关性再高也只能排其后。贴合度同为 0（无整标题命中）
        // 时退回链路分数口径，非命中类结果的排序完全不变。
        var results = Order(ranked).ToList();

        // 提示链路补位：Top1 恒为"直接采用码"，另见目标树的更细码只补空出来的名额，
        // 让编码员看到 "病案信息更详细时须转 分娩，剖宫产 取 O34.2" 这类漏填信号。
        // 满额时不让位（万条实测：让位只多 1 条 Top3 口径命中，却挤掉 20 条既有候选，
        // 其中 胆囊结石 让位给 先天性胆囊缺如 Q44.3、创伤性关节病 让位给 肘关节扭伤 S53.1
        // 这类"指针树里的另一个部位"，精度上不划算）。
        var taken = results.Select(item => item.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var hint in Order(hints))
        {
            if (results.Count < maxResults && taken.Add(hint.Code))
            {
                results.Add(hint);
            }
        }

        return results;
    }

    /// <summary>同一索引编码（同族粒度菜单）在最终名额里最多占的席位数。</summary>
    private const int MaxSeatsPerIndexFamily = 1;

    private IEnumerable<DiagnosisIndexRecommendation> Order(
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> candidates)
    {
        var sorted = candidates
            .GroupBy(item => item.Recommendation.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Recommendation.WholeTitleDepth)
                .ThenByDescending(item => item.RawScore)
                .ThenBy(item => item.Recommendation.FamilyRank)
                .First())
            .OrderByDescending(item => item.Recommendation.WholeTitleDepth)
            .ThenByDescending(item => item.RawScore)
            .ThenBy(item => item.Recommendation.FamilyRank)
            .ToList();

        // 一族一席优先，空出来的名额再由同族续填：同一个索引编码展开出的多级详码分数必然同分
        // （族内钳位到 1.0），一条疾病链路就能把推荐位占满，把别的链路顶出 Top3
        // （万条实测 --甲状腺切除术后：甲状腺肿 E04.9 一族占 3 席，状态链路的
        //  E89.000 手术后甲减 排第 6 进不了交付）。
        // 二遍补位保证 "二尖瓣术后 → Z98.800｜Z98.800X409｜Z98.800X406" 这类同族粒度菜单
        // 在没有竞争链路时仍然占满名额——只有别的链路正在被挤掉时才让席。
        var seats = new List<DiagnosisIndexRecommendation>();
        var takenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seatsByFamily = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in sorted)
        {
            if (seats.Count >= maxResults)
            {
                break;
            }

            var family = item.Recommendation.IndexFamilyCode ?? item.Recommendation.Code;
            if (seatsByFamily.TryGetValue(family, out var used) && used >= MaxSeatsPerIndexFamily
                || !takenCodes.Add(item.Recommendation.Code))
            {
                continue;
            }

            seatsByFamily[family] = used + 1;
            seats.Add(item.Recommendation);
        }

        foreach (var item in sorted)
        {
            if (seats.Count >= maxResults)
            {
                break;
            }

            if (takenCodes.Add(item.Recommendation.Code))
            {
                seats.Add(item.Recommendation);
            }
        }

        return seats;
    }

    private void ResolveText(
        IcdIndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        string inputText,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> ranked,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> hints,
        List<ConditionFallback> conditionFallbacks,
        CancellationToken cancellationToken)
    {
        var inputKey = TextMatch.NormalizeKey(inputText);
        if (inputKey.Length < 2)
        {
            // 单字输入（如"痔"、"疝"）：仅当精确命中有效主导词别名时放行——
            // 逐字覆盖召回（MaxSegment≥3）天然挡住单字，但单字主导词与输入 1:1
            // 相等不属于"甲周炎↔肩周炎"式误召回，直接锚定该主导词下钻。
            var exactHit = FindSingleCharacterLeadHit(snapshot, inputKey);
            if (exactHit is null)
            {
                return;
            }

            Collect(snapshot, dictionary, inputText,
                IcdIndexMatcher.Drill(snapshot, exactHit, inputKey), exactHit.Alias.Weight, ranked, hints);
            return;
        }

        var leadHits = IcdIndexMatcher.RecallLeads(snapshot, inputKey, leadWordTopK).ToList();

        // 失效主导词"现译名/见"重定向：仅当常规召回全空时按目标文本重试一次。
        if (leadHits.Count == 0
            && IcdIndexMatcher.ResolveRedirect(snapshot, inputKey) is { } redirect)
        {
            leadHits = IcdIndexMatcher
                .RecallLeads(snapshot, TextMatch.NormalizeKey(redirect), leadWordTopK)
                .ToList();
        }

        var rankedBefore = ranked.Count;
        var genericJumps = new List<IcdIndexNode>();
        foreach (var hit in leadHits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Collect(snapshot, dictionary, inputText,
                IcdIndexMatcher.Drill(snapshot, hit, inputKey, genericJumps: genericJumps),
                hit.Alias.Weight, ranked, hints);
        }

        // 见-1 的 level-0 形态（"剖腹产 - 见 剖宫产"）：这些行不是主导词，原先只有整条输入
        // 与别名一字不差相等时才改道（ResolveRedirect），输入里还写出别的词就什么都不发生。
        // 规则写的是"见 + 具体主导词 必须转换"，所以只要输入含住这个失效词，就把目标主导词的
        // 链路放进常规候选池一起比分：不加权、不压制——能不能顶掉本地链路（如 瘢痕→子宫）
        // 由覆盖度与整标题贴合度裁决；目标链路与常规召回同树时跳过，避免重复展开。
        foreach (var (key, target) in snapshot.Redirects)
        {
            if (key.Length < 2
                || IcdIndexMatcher.IsGenericConditionTarget(target)
                || IcdIndexMatcher.IsGenericLeadKey(key)
                || IsGenericConditionWord(key)
                // 两字失效词只有在"输入主体就是它"（整条不超过 4 字）时才够格改道：
                // 马尾神经损害 里的 神经 是通道构成成分，不是被索引判了失效的主导词，
                // 放行会把 G83.400 顶成 S34.300（万条实测 250 条 Top1 变更里的一类）。
                || (key.Length < 3 && inputKey.Length > 4)
                || !inputKey.Contains(key, StringComparison.Ordinal))
            {
                continue;
            }

            if (IcdIndexMatcher.ResolveJumpLead(snapshot, target) is not { } redirectLead
                || leadHits.Any(hit => hit.Node.SeqNo == redirectLead.SeqNo))
            {
                continue;
            }

            var redirectSeed = redirectLead.Aliases.FirstOrDefault(item => item.MatchKey.Length >= 2
                    && inputKey.Contains(item.MatchKey, StringComparison.Ordinal))
                ?? redirectLead.Aliases.FirstOrDefault();
            if (redirectSeed is null)
            {
                continue;
            }

            Collect(
                snapshot, dictionary, inputText,
                IcdIndexMatcher.Drill(
                    snapshot,
                    new LeadWordHit(redirectLead, redirectSeed, 0m, 0),
                    inputKey,
                    jumpTrace: $"{key} → {redirectLead.Term}"),
                1m,
                ranked,
                hints);
        }

        // 常规主导词只要有产出，泛词 病，疾病 通道就不启用（编码员也是先找真主导词，
        // 全都找不到才翻 "病，疾病" 这个泛词）。
        if (ranked.Count > rankedBefore)
        {
            return;
        }

        // 登记末位兜底候选：真正的下钻等所有文本变体都跑完、确认整体无产出后再做。
        foreach (var jump in genericJumps)
        {
            conditionFallbacks.Add(new ConditionFallback(
                inputText,
                jump.Aliases.SelectMany(alias => new[] { alias.Key, alias.MatchKey }).Distinct().ToList(),
                $"{jump.Term} -见 {jump.JumpTarget}"));
        }

        // "X的 -见 情况" 失效主导词行通道：输入完整含住该行别名（如 "胆囊的继发" 含 胆囊的）。
        // 改道 病，疾病 后按该别名键精确锚定子节点（病，疾病→胆囊 K82.9）；从根节点自由下钻
        // 会被输入里的程度词/继发词带进无关子树。
        foreach (var (key, target) in snapshot.Redirects)
        {
            if (!target.Contains("情况", StringComparison.Ordinal)
                || key.Length < 2
                || !inputKey.Contains(key, StringComparison.Ordinal))
            {
                continue;
            }

            conditionFallbacks.Add(new ConditionFallback(inputText, [key], $"{key} -见 {target}"));
        }
    }

    /// <summary>
    /// 泛化主导词族的词面本身（情况/疾患/综合征/病，疾病）。
    /// 索引行 "斯托克斯-亚当斯病或综合征 -见 阿-斯综合征" 按 或 拆别名会产出裸尾段 综合征，
    /// 它进了 Redirects 的键却不携带"这行说的是哪个病"的信息：放行后 眩晕综合征、烟雾综合征、
    /// 肾病综合征 全部被 阿-斯综合征 I45.9 抢走名次，万条实测把
    /// 原发性干燥综合征伴间质性肺病 的 M35.000（正确码）挤出了 Top3。
    /// </summary>
    private static bool IsGenericConditionWord(string key)
        => GenericConditionFamily.Contains(key) || key is "病" or "疾病";

    private const string PostoperativeSuffix = "术后";

    /// <summary>"X术后" 补的泛词变体：命中 35948 手术后状态 NEC 与 50034 状态 的 －手术后 NEC 两支。</summary>
    private const string PostoperativeStateLead = "手术后状态";

    /// <summary>输入结尾的标点与空白：判定 "X术后" 是否以 术后 收尾时先剥掉。</summary>
    private static readonly char[] TrailingNoiseCharacters =
    [
        ' ', '\t', '　', '、', '，', ',', '。', '.', '．', '；', ';', '：', ':', '！', '!', '？', '?',
        '（', '(', '）', ')', '【', '】', '[', ']', '\'', '"', '’', '”', '-', '－', '—', '–',
    ];

    /// <summary>
    /// 结尾形态的 "X术后" 补两路变体，两路各管一件事：
    /// - "X手术后状态"（带术式前缀）：让下钻能落到索引给具体术式的行上，取精确码
    ///   （状态 → －甲状腺切除术后(甲状腺功能减退症) → E89.0）。
    /// - "手术后状态"（泛词）：万条实测带前缀那一路在真实索引里常常挣不到召回名额——
    ///   甲状腺切除手术后状态 的 top-8 主导词全被 甲状腺肿/甲状腺毒症 这类疾病行占走，
    ///   状态 排在名额之外，整条链路不执行（改前台账一模一样）。泛词只有 4 字，
    ///   锚得住 50034 状态 与 35948 手术后状态 NEC，稳稳拿到 Z98.8→Z98.800 的状态兜底码。
    /// 只在 术后 是输入结尾时补：术后感染(T81)、术后骨愈合(Z47)、术后化疗(Z51) 的 术后 后面
    /// 还有中心词，补成"手术后状态"会把它们带偏——万条实测结尾类 890 条期望码 Z98=480，
    /// 非结尾类 471 条是 Z51=219、Z47=75、T81=20。补的是变体不是替换，原文照跑，
    /// 所以这一路不可能让输入失去推荐。
    /// </summary>
    private static IReadOnlyList<string> PostoperativeStateVariants(string inputText)
    {
        var trimmed = inputText.TrimEnd(TrailingNoiseCharacters);
        if (!trimmed.EndsWith(PostoperativeSuffix, StringComparison.Ordinal))
        {
            return [];
        }

        var head = trimmed[..^PostoperativeSuffix.Length];
        return head.Length == 0
            ? [PostoperativeStateLead]
            : [$"{head}手术后状态", PostoperativeStateLead];
    }

    /// <summary>
    /// 末位兜底：在泛化主导词族下按锚点词精确定位子节点并下钻。
    /// 索引里 病，疾病(2972) -另见 综合征(50429)、疾患(16842) -另见 病 互为指针，
    /// 编码员口径也是"病，疾病、疾患"一族，所以按 病，疾病 → 疾患 → 综合征 依次找；
    /// 族词只在末位兜底通道内串联，常规主导词有产出时整条通道不启用。
    /// 锚点键可能有多个变体（带括号/去括号/"的"属格），同一族词下取第一个能锚定的。
    /// </summary>
    private void RunConditionFallback(
        IcdIndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        ConditionFallback fallback,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> ranked)
    {
        var inputKey = TextMatch.NormalizeKey(fallback.InputText);
        foreach (var target in GenericConditionFamily)
        {
            if (IcdIndexMatcher.ResolveJumpLead(snapshot, target) is not { } genericLead)
            {
                continue;
            }

            // 首个族词就是 "见情况" 的默认落点，证据链沿用原指针文案；换族词时补一跳，
            // 让编码员看到 "病，疾病 下没有这个部位词，是 疾患 给的码"。
            var trace = target == GenericConditionFamily[0]
                ? fallback.Trace
                : $"{fallback.Trace} ⇢ {genericLead.Term}";

            foreach (var anchorKey in fallback.AnchorKeys)
            {
                var drill = IcdIndexMatcher.DrillFromRedirectAnchor(
                    snapshot, genericLead, anchorKey, inputKey, trace);
                if (drill is null)
                {
                    continue;
                }

                var before = ranked.Count;
                Collect(snapshot, dictionary, fallback.InputText, drill, 1m, ranked);
                if (ranked.Count > before)
                {
                    return;
                }
            }
        }
    }

    private void Collect(
        IcdIndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        string inputText,
        DrillResult? drill,
        decimal leadBoost,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> ranked,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)>? hints = null)
    {
        var produced = CollectOnce(snapshot, dictionary, inputText, drill, leadBoost, ranked);

        // 见-1 强制改道让位给跳转链路的本地祖先兜底链路：改道链路展不开时回退，
        // 规则要求的是"优先"，不是"没有别的码就不出推荐"。
        if (!produced && drill?.Displaced is { } displaced)
        {
            CollectOnce(snapshot, dictionary, inputText, displaced, leadBoost, ranked);
        }

        // 另见-1：Top1 保持本行直接采用码，跳转树的更细码只进提示池补位。
        if (hints is not null && drill is { SeeAlsoHints.Count: > 0 })
        {
            foreach (var hint in drill.SeeAlsoHints)
            {
                CollectOnce(snapshot, dictionary, inputText, hint, leadBoost, hints,
                    $"本行 {drill.CodeNode.Code} 为直接采用码，更详细情况见");
            }
        }
    }

    private bool CollectOnce(
        IcdIndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        string inputText,
        DrillResult? drill,
        decimal leadBoost,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> ranked,
        string? hintNote = null)
    {
        var rankedBefore = ranked.Count;
        if (drill?.CodeNode.Code is not { Length: > 0 } indexCode)
        {
            return false;
        }

        var parsed = ParsedIndexCode.Parse(indexCode);
        var pathDisplay = string.Join(" → ", drill.Path.Select(node => node.Term));
        if (drill.Jumps.Count > 0)
        {
            pathDisplay += " [跳转: " + string.Join("; ", drill.Jumps) + "]";
        }

        var expanded = IndexCodeExpander.Expand(
            parsed, dictionary, inputText, drill.PathKey, maxResults, diagnosisCodeSystem);
        if (expanded.Count == 0)
        {
            return false;
        }

        // 折叠掉的连接行（见于/由于/继发于）不是输入真写出来的证据层：既不挣链路深度加成，
        // 也不挣覆盖度评分——否则"透明化"本身就等于给这类链路加权（改道平权纪律）。
        // 万条实测漏掉这一条时 酒精性痴呆 F10.700X002 被 痴呆 → 见于(由于) → 酒精 的折叠链路
        // 靠"多一层深度"顶掉（同一改动带进 9 条流失、补上这条判据后 8 条）。
        // 30% 门槛仍按完整链路算：透明化不该让任何既有链路新掉进门控（打空硬线）。
        var evidencePath = drill.Path.Where(node => !IcdIndexMatcher.IsConnectorNode(node)).ToList();
        var evidenceOverlap = evidencePath.Count == drill.Path.Count
            ? PathOverlapRatio(drill.Path, inputText)
            : PathOverlapRatio(evidencePath, inputText);
        var depthBoost = 1m + 0.05m * evidencePath.Count;
        // 链路字符重叠门控：主导词+下钻路径（取各节点与输入重叠最好的别名变体）
        // 必须解释输入 30% 以上字符，否则视为无关链路直接丢弃；重叠度并入评分做精排。
        // 跨部位误下钻（基底节/结核/牙源性）由主导词与子树限定词门控拦截，此处阈值只兜底。
        var pathOverlap = PathOverlapRatio(drill.Path, inputText);
        if (pathOverlap < 0.30m)
        {
            return false;
        }

        foreach (var item in expanded)
        {
            // 排序用未截断分：Reported Score 上限 1.0 会把 "扁桃体息肉→J35.805"（标题全覆盖）
            // 与 "→J03.901 急性化脓性扁桃体炎"（沾词）压成同分，取Top时只剩召回顺序。
            var rawScore = item.Score * leadBoost * depthBoost * (0.5m + evidenceOverlap)
                * (item.IsIndexFallback ? 0.8m : 1m);
            ranked.Add((rawScore, new DiagnosisIndexRecommendation(
                item.CodeSystemCode,
                item.Code,
                item.IsIndexFallback ? drill.Path[^1].Term : item.Title,
                Math.Min(1m, rawScore),
                item.IsIndexFallback,
                item.IsConditionalAddOn,
                pathDisplay + $" | 索引编码: {indexCode}",
                hintNote is null ? item.Note
                    : $"{hintNote} {pathDisplay} {indexCode}"
                        + (item.Note is { Length: > 0 } fallbackNote ? $"（{fallbackNote}）" : string.Empty),
                item.WholeTitleDepth,
                item.FamilyRank,
                indexCode)));
        }

        return ranked.Count > rankedBefore;
    }

    /// <summary>单字输入的精确主导词锚定：别名（含去装饰变体）与输入完全一致才命中。</summary>
    private static LeadWordHit? FindSingleCharacterLeadHit(IcdIndexSnapshot snapshot, string inputKey)
    {
        foreach (var lead in snapshot.Leads)
        {
            foreach (var alias in lead.Aliases)
            {
                if (string.Equals(TextMatch.NormalizeKey(alias.Key), inputKey, StringComparison.Ordinal)
                    || string.Equals(alias.MatchKey, inputKey, StringComparison.Ordinal))
                {
                    return new LeadWordHit(lead, alias, 1m, inputKey.Length);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 命中链路对输入的字符覆盖比例（去重字符集合口径）：每个路径节点取
    /// "含括号/去括号/去装饰"变体，统计链路命中的输入字符占比，
    /// 挡住 "闭锁→输卵管" 这类只沾两个字边的无关链路。
    /// 分母只计检索核字符：列表序号与编码标注（"2.肝大" 的 2、"II°" 的罗马数字）是纯 ASCII，
    /// "原因待查/可能性大" 类引导语与程度修饰是书写噪声，
    /// 否则 "2.肝大原因待查 → 肝大" 这类短名会被稀释到门槛之下。
    /// </summary>
    private static decimal PathOverlapRatio(IReadOnlyList<IcdIndexNode> path, string inputText)
    {
        var inputKey = TextMatch.StripIndexingFillers(inputText);
        if (inputKey.Length == 0)
        {
            inputKey = TextMatch.NormalizeKey(inputText);
        }

        if (inputKey.Length == 0)
        {
            return 0m;
        }

        var contentCharacters = inputKey
            .Where(character => !char.IsAsciiLetterOrDigit(character))
            .Distinct()
            .ToHashSet();
        var inputCharacters = contentCharacters.Count > 0
            ? contentCharacters
            : inputKey.Distinct().ToHashSet();
        var covered = new HashSet<char>();
        foreach (var node in path)
        {
            var (bare, _) = IcdIndexSnapshotBuilder.StripDecorations(node.Term);
            foreach (var variant in new[]
            {
                node.Term,
                IcdIndexSnapshotBuilder.ParenthesisRegex.Replace(node.Term, string.Empty),
                bare,
                IcdIndexSnapshotBuilder.ParenthesisRegex.Replace(bare, string.Empty),
            })
            {
                covered.UnionWith(TextMatch.NormalizeKey(variant).Where(inputCharacters.Contains));
            }
        }

        return covered.Count / (decimal)inputCharacters.Count;
    }
}
