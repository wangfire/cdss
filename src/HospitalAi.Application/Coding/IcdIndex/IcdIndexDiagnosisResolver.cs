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
    string? IndexFamilyCode = null,
    /// <summary>链路终点的证据极性（P0 极性标签）：R2 极性冲突剔除与 P1 极性匹配优先用。</summary>
    string Polarity = "Unspecified");

/// <summary>
/// 诊断输入的两段式解析：主导词召回 → 逐级下钻 → 索引编码格式解析 → 字典展开。
/// 诊断侧不再回退字典直匹配（决策 2）：无主导词命中即返回空。
/// 索引命中但字典展不开 → 回退推荐索引编码本身（IsIndexFallback）。
/// 输入按术语同义词生成规范变体（原文 + 改写文各跑一遍，按编码去重取高分）；
/// "X的 -见情况" 式失效主导词行命中时改道 病，疾病 主导词下钻部位修饰词。
/// 两条标题词面通道受开关控制（口径对齐手术侧 CM3）：
/// <c>titleDirectEnabled</c> = 字典标题直达候选常驻同台竞争；
/// <c>titleReverseEnabled</c> = 索引门控全落空时先做同族标题反查、再取最泛码。
/// </summary>
public sealed class IcdIndexDiagnosisResolver(
    int leadWordTopK,
    int maxResults,
    string diagnosisCodeSystem,
    bool titleDirectEnabled = true,
    bool titleReverseEnabled = true)
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
        TumorIndexSnapshot? tumor = null,
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

        // 肿瘤动态词的"性"尾缀：索引写成 继发性（瘤 －继发性 M8000/6 - 见 肿瘤，继发性），
        // 临床常省成 继发（基底节继发恶性肿瘤）。差一个字，主导词召回覆盖率只有 2/3，
        // 进不了名额 → 只剩 瘤→恶性 M8000/3，行为位取错列，肿瘤表按 /3 给出原发部位码
        // （基底节继发恶性肿瘤 期望 C79.300X021，实给 C71.000X006 基底节恶性肿瘤）。
        texts.AddRange(TumorDynamicFormVariants(inputText)
            .Where(text => text.Length > 0 && !texts.Contains(text)));

        // 检验结果极性标记 (+)/(-) 改写成 阳性/阴性：结核 A15/A16 一族整族靠这个符号分阴阳，
        // 而 NormalizeKey 把半角连字符按标点剥掉、把 '+' 当数学符号留下——"(-)" 在词面上整个消失
        // （等于没说），"(+)" 只剩一个孤零零的 '+'。索引写明的极性行（结核 → 肺 →
        // 细菌学和组织学检查为阴性 A16.0）一个字都锚不住，输入只能落在 不伴有提及细菌学或
        // 组织学证实 A16.2 这类"没提证实"的中性行上（CAP 万名 38 条带标记的输入只有 6 条 Top1 命中，
        // 且全是词面自带 细菌学、不靠符号的那批）。
        texts.AddRange(TestPoleMarkerVariants(inputText)
            .Where(text => text.Length > 0 && !texts.Contains(text)));

        // P0-P3 极性族（流水线口径，与编码录入面板引擎共用同一判据）：
        // 输入带证据极性时才启用证据模式——证据等价组成员词面（病理(+)→确定的被组织学…）
        // 传给 Drill 做选层证据优先与词面占位；无极性的普通同义词命中不得触发，
        // 否则占位拦截会波及全部同义词输入（万条零偏差的保险）。
        var inputPolarity = IcdIndexPolarity.DeriveInputPolarity(inputText);
        IReadOnlyList<string>? evidenceKeys = null;
        // 证据模式下的同义词快照（供字典展开的同义词解释门用）：只有输入带证据极性
        // 且确实命中等价类时才接——无极性输入接这条门会放宽全部同义词命中的字典放行口径。
        IcdTermSynonymSnapshot? evidenceSynonyms = null;
        if (synonyms is not null && inputPolarity != "None")
        {
            evidenceKeys = synonyms.GroupMembersForText(inputText, TermSynonymTypes.Diagnosis);
            if (evidenceKeys.Count > 0)
            {
                evidenceSynonyms = synonyms;
            }
        }

        var ranked = new List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)>();
        // 另见-1 提示候选：只在常规链路的名额没占满时补位，不参与 Top1 竞争。
        var hints = new List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)>();
        // "见情况/另见情况" 的泛词改道候选：按编码员查典顺序，只有常规主导词全部落空才启用，
        // 所以先登记不下钻，避免 "2.直肠肿瘤" 被 直肠-见情况→K62.9 抢在 肿瘤 主导词之前。
        var conditionFallbacks = new List<ConditionFallback>();
        foreach (var text in texts)
        {
            ResolveText(snapshot, dictionary, text, inputText, ranked, hints, conditionFallbacks, tumor,
                evidenceKeys, evidenceSynonyms, cancellationToken);
        }

        // R2 极性冲突剔除（P0）：输入含阳性证据词（病理(+)、痰镜检(+)、培阳、涂阳）时，
        // 终点极性为 阴性/未证实 的链路剔除——语义反转防护（2026-11-16 用户案例：
        // 结核性肺纤维变性,病理(+) 曾产出 A16.023 病理(-)）。
        // **有阳性分支可交付时才剔**：候选池里存在极性一致的阳性分支（同义词变体走出的
        // 证据深链）时，阴性/未证实候选让位；整个池子都没有阳性分支时保留原推荐——
        // 流水线硬线是"不得让任何输入失去既有推荐"（面板引擎配 POLARITY.UNMATCHED
        // 降级提示可以空，流水线没有这条通道，无替代时不剔）。
        if (inputPolarity == "Positive"
            && ranked.Any(item => item.Recommendation.Polarity == "Positive"))
        {
            ranked.RemoveAll(item => item.Recommendation.Polarity is "Negative" or "NotPerformed");
        }

        // 字典标题直达：输入逐字包含完整标题（或与标题逐字相等）是比任何链路都硬的证据。
        // 常驻参与竞争而非只在零产出时兜底——走错分支的链路产出再高，也不该压过输入里
        // 逐字写着的标准标题码。诊断侧比手术侧多两道约束：①沿用链路的三道硬门
        // （糖尿病分型/否定极/部位冲突），否则 "无高血压" 会把 高血压 直达进来；
        // ②只认可交付的单码，+ * / 组合码交给索引展开链路拆分。
        if (titleDirectEnabled)
        {
            CollectDictionaryDirect(dictionary, texts, ranked);
        }

        if (ranked.Count == 0 && conditionFallbacks.Count > 0)
        {
            foreach (var fallback in conditionFallbacks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RunConditionFallback(snapshot, dictionary, fallback, ranked, tumor);
            }
        }

        // 整标题贴合度优先于链路分数：标题被输入逐字连续包含（且解释掉 ≥75% 输入字符）时，
        // 编码员看的就是这一条，其它链路的相关性再高也只能排其后。贴合度同为 0（无整标题命中）
        // 时退回链路分数口径，非命中类结果的排序完全不变。
        var results = Order(ranked, inputPolarity).ToList();

        // 提示链路补位：Top1 恒为"直接采用码"，另见目标树的更细码只补空出来的名额，
        // 让编码员看到 "病案信息更详细时须转 分娩，剖宫产 取 O34.2" 这类漏填信号。
        // 满额时不让位（万条实测：让位只多 1 条 Top3 口径命中，却挤掉 20 条既有候选，
        // 其中 胆囊结石 让位给 先天性胆囊缺如 Q44.3、创伤性关节病 让位给 肘关节扭伤 S53.1
        // 这类"指针树里的另一个部位"，精度上不划算）。
        var taken = results.Select(item => item.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var hint in Order(hints, inputPolarity))
        {
            if (results.Count < maxResults && taken.Add(hint.Code))
            {
                results.Add(hint);
            }
        }

        return results;
    }

    /// <summary>
    /// P2 R3 极性缺失降级提示（流水线口径）：输入含阳性/阴性证据词，但交付的候选里
    /// 没有任何极性一致的（证据深链未走到）——说明"证实分支没找到"，给出可解释提示。
    /// 口径对齐面板引擎（NameCodingLookupEngine 同名降级位）：只提示，不改推荐内容。
    /// 流水线与面板的差别：面板的候选带链路极性可直接比对，流水线的候选极性取
    /// 链路终点（DiagnosisIndexRecommendation.Polarity），同源同判据。
    /// </summary>
    public static string? PolarityUnmatchedFlag(
        string inputText,
        IReadOnlyList<DiagnosisIndexRecommendation> recommendations)
    {
        var inputPolarity = IcdIndexPolarity.DeriveInputPolarity(inputText);
        if (inputPolarity is not ("Positive" or "Negative") || recommendations.Count == 0)
        {
            return null;
        }

        var matched = recommendations.Any(item =>
            string.Equals(item.Polarity, inputPolarity, StringComparison.Ordinal));
        return matched ? null : $"POLARITY.UNMATCHED.{inputPolarity.ToUpperInvariant()}";
    }

    /// <summary>
    /// 字典标题直达候选：只认 <see cref="IcdDictionarySnapshot.TitleDirectMatches"/> 的硬词面证据
    /// （逐字相等，或 ≥3 字标题被输入逐字包含且贴合度 ≥0.5）。分数与手术侧同口径
    /// （depth×1.5，钳位 1.0）；只有逐字相等才给 WholeTitleDepth=1，让管道按整标题贴合保首。
    /// </summary>
    private void CollectDictionaryDirect(
        IcdDictionarySnapshot dictionary,
        IReadOnlyList<string> texts,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> ranked)
    {
        foreach (var text in texts)
        {
            var inputKey = TextMatch.NormalizeKey(text);
            if (inputKey.Length < 3)
            {
                // 过短输入的逐字包含噪声大（"肺炎"能包含进一串含肺炎的标题），不参与直达。
                continue;
            }

            foreach (var (row, depth) in dictionary.TitleDirectMatches(inputKey).Take(maxResults * 2))
            {
                // 交付码形态：+ * / 组合码不是单一临床编码，由索引展开链路负责拆分与回退。
                if (row.Code.Contains('+') || row.Code.Contains('*') || row.Code.Contains('/'))
                {
                    continue;
                }

                if (TextMatch.IsUnjustifiedDiabetesCompound(row.Title, text)
                    || TextMatch.HasDiabetesTypeConflict(row.Title, row.Code, text, resolveTypeFromCategoryAndDefault: false)
                    || TextMatch.HasNegationPoleConflict(row.Title, text)
                    || TextMatch.HasBodyPartConflict(row.Title, text))
                {
                    continue;
                }

                var raw = Math.Min(1m, depth * 1.5m);
                ranked.Add((raw, new DiagnosisIndexRecommendation(
                    row.CodeSystemCode,
                    row.Code,
                    row.Title,
                    raw,
                    IsIndexFallback: false,
                    IsConditionalAddOn: false,
                    $"字典标题直达 | 标题: {row.Title}",
                    depth >= 1m ? "字典标题直达（标题逐字相等）" : "字典标题直达（标题被输入逐字包含）",
                    WholeTitleDepth: depth >= 1m ? 1m : 0m)));
            }
        }
    }

    /// <summary>同一索引编码（同族粒度菜单）在最终名额里最多占的席位数。</summary>
    private const int MaxSeatsPerIndexFamily = 1;

    private IEnumerable<DiagnosisIndexRecommendation> Order(
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> candidates,
        string inputPolarity = "None")
    {
        // P1 R1 极性匹配优先（口径对齐面板引擎 Assemble）：输入含阳性/阴性证据词时，
        // 极性一致的候选排在未证实泛码之前（整标题贴合仍是更高优先档——逐字写着的
        // 标准标题码不因极性让位）。极性冲突的候选已在 R2 剔除。
        // 全档并列的终判继承候选池顺序（DictionaryDirect 按标题贴合+searchText 长度
        // 已排好序；LINQ OrderBy 系稳定排序，First 进组的既有次序保留到组间并列）——
        // 库内重复标题行（前体细胞淋巴母细胞型淋巴瘤 的 C83.506 主行 vs C96.707
        // 错置族别行）的取舍由此落在 searchText 更全的主行上，而不是编码字典序。
        var sorted = candidates
            .Select((item, index) => (item, index))
            .GroupBy(entry => entry.item.Recommendation.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(entry => entry.item.Recommendation.WholeTitleDepth)
                .ThenByDescending(entry => IcdIndexPolarity.PolarityRank(entry.item.Recommendation.Polarity, inputPolarity))
                .ThenByDescending(entry => entry.item.RawScore)
                .ThenBy(entry => entry.item.Recommendation.FamilyRank)
                .ThenBy(entry => entry.index)
                .First().item)
            .OrderByDescending(item => item.Recommendation.WholeTitleDepth)
            .ThenByDescending(item => IcdIndexPolarity.PolarityRank(item.Recommendation.Polarity, inputPolarity))
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
        string originalInputText,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> ranked,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> hints,
        List<ConditionFallback> conditionFallbacks,
        TumorIndexSnapshot? tumor,
        IReadOnlyList<string>? evidenceKeys,
        IcdTermSynonymSnapshot? evidenceSynonyms,
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

            Collect(snapshot, dictionary, inputText, originalInputText,
                IcdIndexMatcher.Drill(snapshot, exactHit, inputKey, evidenceKeys: evidenceKeys),
                exactHit.Alias.Weight, ranked, hints, tumor: tumor, evidenceSynonyms: evidenceSynonyms);
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
            Collect(snapshot, dictionary, inputText, originalInputText,
                IcdIndexMatcher.Drill(snapshot, hit, inputKey, genericJumps: genericJumps,
                    evidenceKeys: evidenceKeys),
                hit.Alias.Weight, ranked, hints, tumor: tumor, evidenceSynonyms: evidenceSynonyms);
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
                snapshot, dictionary, inputText, originalInputText,
                IcdIndexMatcher.Drill(
                    snapshot,
                    new LeadWordHit(redirectLead, redirectSeed, 0m, 0),
                    inputKey,
                    jumpTrace: $"{key} → {redirectLead.Term}",
                    evidenceKeys: evidenceKeys),
                1m,
                ranked,
                hints,
                tumor: tumor,
                evidenceSynonyms: evidenceSynonyms);
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
        => IcdIndexMatcher.IsGenericConditionLeadKey(key);

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

    /// <summary>索引里的肿瘤动态写法（瘤 －继发性 M8000/6 - 见 肿瘤，继发性）。</summary>
    private const string SecondaryTumorStem = "继发";

    private const string SecondaryTumorLead = "继发性";

    /// <summary>
    /// 肿瘤动态词补「性」形变体：输入含 继发 而不含 继发性 时，把 继发 改写成 继发性 再跑一路。
    /// 主导词召回按别名键对输入的覆盖率卡名额，「继发」对「继发性」只有 2/3，进不了名额，
    /// 于是只剩 瘤→恶性(M8000/3)，肿瘤表按行为位 3 取「恶性-原发」列，给出的是原发部位码
    /// （基底节继发恶性肿瘤 期望 C79.300X021，实给 C71.000X006 基底节恶性肿瘤）。
    /// 补的是变体不是替换，原文照跑，所以这一路不可能让输入失去既有推荐。
    /// </summary>
    private static IReadOnlyList<string> TumorDynamicFormVariants(string inputText)
    {
        if (!inputText.Contains(SecondaryTumorStem, StringComparison.Ordinal)
            || inputText.Contains(SecondaryTumorLead, StringComparison.Ordinal))
        {
            return [];
        }

        return [inputText.Replace(SecondaryTumorStem, SecondaryTumorLead, StringComparison.Ordinal)];
    }

    private const string PositivePoleWord = "阳性";

    private const string NegativePoleWord = "阴性";

    /// <summary>
    /// 检验结果极性标记 (+)/(-) 补一路中文词变体。病历与字典都写成 "痰镜检(+)"、"病理(-)"，
    /// 索引里管这件事的行写的是「细菌学和组织学检查为阴性 A16.0」——符号在 NormalizeKey 之后
    /// 要么消失（连字符按标点剥掉）要么剩一个孤零零的 '+'，与 阴/阳 二字一字不沾，整条极性分支
    /// 锚不住。补的是变体不是替换，原文照跑，所以这一路不可能让输入失去既有推荐。
    /// 半角/全角括号与 + ＋ - － − 的混写都认；括号里是别的字（(a)(脊)(症)）原样保留。
    /// </summary>
    private static IReadOnlyList<string> TestPoleMarkerVariants(string inputText)
    {
        var builder = new System.Text.StringBuilder(inputText.Length + 4);
        var rewritten = false;
        for (var position = 0; position < inputText.Length; position++)
        {
            var consumed = TryReadPoleMarker(inputText, position, out var word);
            if (consumed == 0)
            {
                builder.Append(inputText[position]);
                continue;
            }

            builder.Append(word);
            rewritten = true;
            position += consumed - 1;
        }

        return rewritten ? [builder.ToString()] : [];
    }

    /// <summary>从 start 起读一个 "(+)" 形态的极性标记，返回吃掉的长度（0 = 不是标记）。</summary>
    private static int TryReadPoleMarker(string text, int start, out string word)
    {
        word = string.Empty;
        if (text[start] is not ('(' or '（'))
        {
            return 0;
        }

        var cursor = start + 1;
        SkipSpaces(text, ref cursor);
        word = cursor >= text.Length
            ? string.Empty
            : text[cursor] switch
            {
                '+' or '＋' => PositivePoleWord,
                '-' or '－' or '−' => NegativePoleWord,
                _ => string.Empty,
            };
        if (word.Length == 0)
        {
            return 0;
        }

        cursor++;
        SkipSpaces(text, ref cursor);
        if (cursor >= text.Length || text[cursor] is not (')' or '）'))
        {
            word = string.Empty;
            return 0;
        }

        return cursor - start + 1;
    }

    private static void SkipSpaces(string text, ref int cursor)
    {
        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
        {
            cursor++;
        }
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
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> ranked,
        TumorIndexSnapshot? tumor)
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
                Collect(snapshot, dictionary, fallback.InputText, fallback.InputText, drill, 1m, ranked,
                    tumor: tumor);
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
        string originalInputText,
        DrillResult? drill,
        decimal leadBoost,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> ranked,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)>? hints = null,
        TumorIndexSnapshot? tumor = null,
        IcdTermSynonymSnapshot? evidenceSynonyms = null)
    {
        var produced = CollectOnce(snapshot, dictionary, inputText, originalInputText, drill, leadBoost, ranked,
            tumor: tumor, evidenceSynonyms: evidenceSynonyms);

        // 见-1 强制改道让位给跳转链路的本地祖先兜底链路：改道链路展不开时回退，
        // 规则要求的是"优先"，不是"没有别的码就不出推荐"。
        if (!produced && drill?.Displaced is { } displaced)
        {
            CollectOnce(snapshot, dictionary, inputText, originalInputText, displaced, leadBoost, ranked,
                tumor: tumor, evidenceSynonyms: evidenceSynonyms);
        }

        // 另见-1：Top1 保持本行直接采用码，跳转树的更细码只进提示池补位。
        if (hints is not null && drill is { SeeAlsoHints.Count: > 0 })
        {
            foreach (var hint in drill.SeeAlsoHints)
            {
                CollectOnce(snapshot, dictionary, inputText, originalInputText, hint, leadBoost, hints,
                    $"本行 {drill.CodeNode.Code} 为直接采用码，更详细情况见",
                    tumor: tumor, evidenceSynonyms: evidenceSynonyms);
            }
        }
    }

    private bool CollectOnce(
        IcdIndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        string inputText,
        string originalInputText,
        DrillResult? drill,
        decimal leadBoost,
        List<(decimal RawScore, DiagnosisIndexRecommendation Recommendation)> ranked,
        string? hintNote = null,
        TumorIndexSnapshot? tumor = null,
        IcdTermSynonymSnapshot? evidenceSynonyms = null)
    {
        var rankedBefore = ranked.Count;
        if (drill?.CodeNode.Code is not { Length: > 0 } indexCode)
        {
            return false;
        }

        var parsed = ParsedIndexCode.Parse(indexCode);
        // 肿瘤表解部位码所依据的部位别名词面：索引链路词面（"瘤"）解释不了的病历部位信息，
        // 在查表这一步被真实用过了，必须计入覆盖度，否则整条形态学链路会被 0.30 门槛打掉。
        var tumorFaces = TumorSiteAliasFaces(parsed, tumor, inputText);
        var pathDisplay = string.Join(" → ", drill.Path.Select(node => node.Term));
        if (drill.Jumps.Count > 0)
        {
            pathDisplay += " [跳转: " + string.Join("; ", drill.Jumps) + "]";
        }

        // 字典展开门控用规范形路径：标题写规范形（脑梗死），命中别名（梗塞）拼的路径
        // 会让单字覆盖差一个字而整族误杀（详见 DrillResult.ExpansionPathKey）。
        var expansionPathKey = drill.ExpansionPathKey ?? drill.PathKey;
        // 同义词快照**只在证据模式下**接入字典展开门（同义词解释门，2026-11-16）：
        // 索引分支词 被组织学 与字典标题 肺结核瘤,病理(+) 同义不同字，字面覆盖永远凑不齐，
        // 整族被误杀成"字典库无此码"回退（A15.201 的教训）。
        // 只在证据模式下接：无极性输入走这条门会放宽所有同义词命中的字典放行口径
        // （万条实测 1051 行变化：非极性输入的精确码被泛码顶掉），超出本轮改动范围。
        var expansionSynonyms = evidenceSynonyms;
        var expanded = IndexCodeExpander.Expand(
            parsed, dictionary, inputText, expansionPathKey, maxResults, diagnosisCodeSystem, tumor,
            titleReverseEnabled,
            pathSegments: expansionSynonyms is null
                ? null
                : drill.Path.Select(node => node.Term).ToList(),
            synonyms: expansionSynonyms);

        // 变体输入的字典展开回退：证据链是**改写变体**走出来的（肺结核瘤,病理(+) →
        // 肺结核瘤,确定的被组织学），字典标题写的是临床原文（肺结核瘤,病理(+)）——
        // 用变体文本过门控，标题的字面永远对不上，整族只剩回退码 A15.200。
        // 变体展开不出**真码**时用**用户原文**再展开一次：原文与标题逐字相等，
        // 拿到的是字典真码（A15.201 肺结核瘤,病理(+)）。
        // 判据是"没有非回退产出"（展开器在放行失败时会先给一条回退码占位，
        // 不能用 expanded.Count==0 判断）；原文展开有产出则原样保留（不引入第二套口径）。
        // **只在证据模式下启用**：变体链路的路径是变体文本钻出来的，用原文去展开别的
        // 路径（如 乳腺腺瘤术后 → 手术后状态 Z98.8 的路径上冒出乳腺腺瘤 N60.200）
        // 会把与该链路无关的码塞进候选池——万条实测 37 行变化、11 条 Top1 变更，
        // 全部与极性无关，属越界改动。
        if (evidenceSynonyms is not null
            && !expanded.Any(item => !item.IsIndexFallback)
            && !string.Equals(originalInputText, inputText, StringComparison.Ordinal))
        {
            var originalExpanded = IndexCodeExpander.Expand(
                parsed, dictionary, originalInputText, expansionPathKey, maxResults, diagnosisCodeSystem,
                tumor, titleReverseEnabled,
                pathSegments: drill.Path.Select(node => node.Term).ToList(),
                synonyms: expansionSynonyms);
            if (originalExpanded.Any(item => !item.IsIndexFallback))
            {
                expanded = originalExpanded;
            }
        }

        // 形态学码在肿瘤表没查到部位（输入只写 "C细胞癌"，没有部位词）：索引在同一节点下备了
        // "－－未特指部位" 行（全库 311 条、269 条带码，值就是该形态的最常见部位码
        // C细胞癌→C73、胃癌→C16.9），编码员口径 "没指出部位就按最常见编码处理"。
        // 只在肿瘤表查找落空后启用：输入写出部位时（甲状腺C细胞癌）必须用查得的部位码，
        // 不能让"最常见"顶掉"特指"。
        if (expanded.Count == 0
            && parsed.Kind == IndexCodeFormatKind.Morphology
            && parsed.SiteCode is not { Length: > 0 }
            && FindUnspecifiedSiteRow(drill.CodeNode) is { } unspecified
            && unspecified.Code is { Length: > 0 } unspecifiedCode)
        {
            pathDisplay += $" → {unspecified.Term}";
            indexCode = unspecifiedCode;
            parsed = ParsedIndexCode.Parse(unspecifiedCode);
            expanded = IndexCodeExpander.Expand(
                parsed, dictionary, inputText, expansionPathKey, maxResults, diagnosisCodeSystem, tumor,
                titleReverseEnabled);
        }

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
            ? PathOverlapRatio(drill.Path, inputText, tumorFaces)
            : PathOverlapRatio(evidencePath, inputText, tumorFaces);
        var depthBoost = 1m + 0.05m * evidencePath.Count;
        // 链路字符重叠门控：主导词+下钻路径（取各节点与输入重叠最好的别名变体）
        // 必须解释输入 30% 以上字符，否则视为无关链路直接丢弃；重叠度并入评分做精排。
        // 跨部位误下钻（基底节/结核/牙源性）由主导词与子树限定词门控拦截，此处阈值只兜底。
        // （"带码停链豁免"只在编码录入面板引擎实行——流水线自动取 Top1，万条实测全面
        // 豁免会让 术后状态 Z98.8 类低覆盖停链链路顶掉真值码：C50.900→Z98.800 一族
        // 改坏 15 条、Top1 重排 645 条，撤回待专项设计，11-08。）
        var pathOverlap = PathOverlapRatio(drill.Path, inputText, tumorFaces);
        if (pathOverlap < 0.30m)
        {
            return false;
        }

        foreach (var item in expanded)
        {
            // 排序用未截断分：Reported Score 上限 1.0 会把 "扁桃体息肉→J35.805"（标题全覆盖）
            // 与 "→J03.901 急性化脓性扁桃体炎"（沾词）压成同分，取Top时只剩召回顺序。
            // 证据模式变体轮的整标题贴合按**原文**复核：同义词改写把 继发 改成 继发性
            // 后，变体文本与细码标题（舌骨继发恶性肿瘤）差一个 性 字，逐字相等判定
            // 失败（wtd=0），而原文（舌骨继发恶性肿瘤）与细码标题逐字相等——原文贴合
            // 才是编码员的视觉判定。取变体/原文两算的较大者。
            var wtd = item.WholeTitleDepth;
            // 带证据极性的输入：原文复核不得给"不含证据词面"的浅码标题（喉结核）整标题贴合分——
            // 否则它先于极性档排到确诊链之前。确诊标题（喉结核(细菌学和组织学证实)）含证据概念词，
            // 仍照常复核；非极性输入完全不受影响。
            var evidenceInput = IcdIndexPolarity.DeriveInputPolarity(originalInputText) != "None";
            var titleHasEvidenceFace = item.Title.Contains("证实", StringComparison.Ordinal)
                || item.Title.Contains("确定的", StringComparison.Ordinal)
                || item.Title.Contains("阳性", StringComparison.Ordinal)
                || item.Title.Contains("培阳", StringComparison.Ordinal)
                || item.Title.Contains("涂阳", StringComparison.Ordinal);
            if (!string.Equals(originalInputText, inputText, StringComparison.Ordinal)
                && wtd < 1m
                && (!evidenceInput || titleHasEvidenceFace))
            {
                var originalKey = TextMatch.StripUncertaintyPhrases(originalInputText);
                var titleKey = TextMatch.NormalizeKey(item.Title);
                if (titleKey.Length >= 3
                    && originalKey.Contains(titleKey, StringComparison.Ordinal)
                    && titleKey.Length * 100m / Math.Max(1, originalKey.Length) >= 50m)
                {
                    wtd = Math.Min(1m, titleKey.Length / (decimal)originalKey.Length);
                }
            }

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
                wtd,
                item.FamilyRank,
                indexCode,
                // 链路终点（drill 停住的那一行）的极性标签，口径同面板引擎 path.Polarity：
                // R2 极性冲突剔除与 P1 极性匹配优先按它裁决。
                drill.Path.Count > 0 ? drill.Path[^1].Polarity : "Unspecified")));
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
    /// <summary>
    /// 形态学节点下的 "未特指部位" 子行（索引惯例：该形态的最常见部位码）。
    /// 只认带编码的末级行，"特指部位 - 见 肿瘤，恶性" 那一条由肿瘤表通道处理，不算兜底。
    /// </summary>
    private static IcdIndexNode? FindUnspecifiedSiteRow(IcdIndexNode node)
        => node.Children.FirstOrDefault(child => child.Code is { Length: > 0 }
            && (child.CoreTerm.Contains("未特指部位", StringComparison.Ordinal)
                || child.CoreTerm.Contains("部位未特指", StringComparison.Ordinal)));

    /// <summary>
    /// 肿瘤表解部位码时真实用到的部位别名词面（只给"泛形态学码"链路：索引写 M8000/1，部位全靠查表）。
    /// 口径与 <see cref="PathOverlapRatio"/> 一致——只计"输入里真写出来的字符"，
    /// 所以只有查表解出了码的行所锚定的词面才算证据；表落空、走"未特指部位"兜底时返回空集，
    /// 不给任何加成（免得让位轮交付的 C80/D36.9 这类无部位链路自己给自己抬覆盖度）。
    /// 索引已自带部位码（M8332/3:C73）时不查表，证据本就由链路词面承担。
    ///
    /// 限定到 <see cref="IndexCodeExpander.IsSitelessMorphology"/>（M8000/M8010）是实测出来的：
    /// 放开到特异形态码后，"血管瘤" 的 上肢/下肢/背部 这些部位词给 43920 纤维血管瘤 M9160/0
    /// （骨内血管瘤，靠别名 血管瘤 沾住输入）刷满覆盖度，把索引明示"任何部位"的
    /// 43920 血管瘤 M9120/0:D18.0 顶下 Top1——万条实测 9 条这类流失（右上肢血管瘤 D18.000X001→D16.100 等）。
    /// 特异形态码本身就说清了疾病，部位词不是它的证据，不该拿这份加成。
    /// </summary>
    private static IReadOnlyList<string> TumorSiteAliasFaces(
        ParsedIndexCode parsed, TumorIndexSnapshot? tumor, string inputText)
    {
        if (tumor is null
            || parsed.Kind != IndexCodeFormatKind.Morphology
            || parsed.SiteCode is { Length: > 0 }
            || parsed.MorphologyCode is not { Length: > 0 } morphology
            || !IndexCodeExpander.IsSitelessMorphology(morphology))
        {
            return [];
        }

        return tumor.ResolveSiteCodes(morphology, inputText)
            .Where(match => match.AliasText.Length > 0)
            .Select(match => match.AliasText)
            .ToList();
    }

    private static decimal PathOverlapRatio(
        IReadOnlyList<IcdIndexNode> path, string inputText, IReadOnlyList<string>? extraFaces = null)
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

        foreach (var face in extraFaces ?? [])
        {
            covered.UnionWith(TextMatch.NormalizeKey(face).Where(inputCharacters.Contains));
        }

        return covered.Count / (decimal)inputCharacters.Count;
    }
}
