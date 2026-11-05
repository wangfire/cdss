using HospitalAi.Application.Coding.Cm3Index;
using HospitalAi.Application.Coding.IcdIndex;
using HospitalAi.Application.Coding.Matching;
using HospitalAi.Contracts.CodingRecommendations;

namespace HospitalAi.Application.Coding.NameLookup;

/// <summary>
/// "编码录入"名称解析引擎（无状态、纯函数，快照与字典由 Infrastructure 注入）：
/// ① 主导词全量匹配拆分（<see cref="NameLeadTermSplitter"/>，范围外主导词天然排除）；
/// ② 逐主导词复用既有 <c>IcdIndexMatcher</c>/<c>Cm3ProcedureIndexMatcher</c> 的修饰词下钻
///    （新增增益、部位限定词、病程/否定极、见/另见改道与流水线同一套门控）；
/// ③ 链路码经 <c>IndexCodeExpander</c>/<c>Cm3IndexCodeExpander</c> 展开为字典有效码
///    （推荐必须落在字典真实存在的编码上，回退码显式标注 IsIndexFallback）；
/// ④ 字典标题直达命中置顶，索引候选跨主导词去重合并。
/// </summary>
public static class NameCodingLookupEngine
{
    public const string DiagnosisKind = "diagnosis";
    public const string ProcedureKind = "procedure";
    public const string DiagnosisCodeSystem = "ICD-10";
    public const string ProcedureCodeSystem = "ICD-9-CM-3";

    /// <summary>默认推荐条数（与工作台候选池一致）。</summary>
    public const int DefaultTopK = 8;

    /// <summary>每条修饰词链路最多展开的细码数。</summary>
    private const int CodesPerPath = 4;

    /// <summary>另见提示链路的展开预算（提示性质，只补空位）。</summary>
    private const int CodesPerHintPath = 2;

    /// <summary>参与下钻的主导词数上限（展示上限 16，下钻裁到 12 控延迟）。</summary>
    private const int MaxDrillLeads = 12;

    /// <summary>
    /// 证据链重叠门（口径对齐 Cm3ProcedureIndexResolver.MinPathOverlap）：
    /// 链路词面解释不掉输入三成检索核字符时，该链路不展开不出码——
    /// 只靠一两个字沾上的无关主导词（如长泛词行靠 淋巴 沾住 中心细胞性淋巴瘤）
    /// 会产出 Z52.000 一类兜底噪声码。
    /// </summary>
    private const decimal MinPathOverlap = 0.30m;

    public static NameLookupResponse Lookup(
        string kind,
        string text,
        int? topK,
        IcdIndexSnapshot? icdSnapshot,
        TumorIndexSnapshot? tumorSnapshot,
        IcdDictionarySnapshot? icdDictionary,
        Cm3IndexSnapshot? cm3Snapshot,
        IcdDictionarySnapshot? cm3Dictionary,
        NameLookupTitleChannels? channels = null)
    {
        var titleChannels = channels ?? NameLookupTitleChannels.AllEnabled;
        var isProcedure = kind == ProcedureKind;
        var codeSystem = isProcedure ? ProcedureCodeSystem : DiagnosisCodeSystem;
        var degraded = new List<string>();
        var inputKey = TextMatch.NormalizeKey(text);
        if (inputKey.Length == 0)
        {
            return Empty(kind, text, codeSystem, titleChannels);
        }

        return isProcedure
            ? LookupProcedure(text, inputKey, topK, cm3Snapshot, cm3Dictionary, degraded, codeSystem, titleChannels)
            : LookupDiagnosis(text, inputKey, topK, icdSnapshot, tumorSnapshot, icdDictionary, degraded, codeSystem, titleChannels);
    }

    // ===== 诊断侧（icd_index + tumor_index + ICD-10 字典）=====

    private static NameLookupResponse LookupDiagnosis(
        string text,
        string inputKey,
        int? topK,
        IcdIndexSnapshot? snapshot,
        TumorIndexSnapshot? tumor,
        IcdDictionarySnapshot? dictionary,
        List<string> degraded,
        string codeSystem,
        NameLookupTitleChannels channels)
    {
        if (snapshot is null)
        {
            degraded.Add("DEGRADED.NO_ICD_INDEX");
        }

        if (dictionary is null)
        {
            degraded.Add("DEGRADED.NO_DICTIONARY");
        }

        // "又称" 等价变体补一路拆分（癫痫→癫癎 一族）：索引主导词用字与临床写法不一致时
        // 常规全量匹配召不回，与流水线 resolver 的输入侧改写同口径。
        var variants = new List<string> { inputKey };
        if (snapshot is not null)
        {
            foreach (var rewritten in snapshot.RewriteByAnotherNames(text))
            {
                var rewrittenKey = TextMatch.NormalizeKey(rewritten);
                if (rewrittenKey.Length > 0 && !variants.Contains(rewrittenKey))
                {
                    variants.Add(rewrittenKey);
                }
            }
        }

        // 诊断侧标题直达受运行时开关门控（与流水线 IcdTitleDirectEnabled 同源）。
        var directs = dictionary is null || !channels.IcdTitleDirect
            ? []
            : dictionary.TitleDirectMatches(inputKey)
                .Select(match => new NameLookupDictionaryDirect(
                    codeSystem, match.Row.Code, match.Row.Title, match.Depth))
                .ToList();
        var splitHits = new List<(NameLeadTermSplitter.IcdLeadTermHit Hit, string VariantKey)>();
        foreach (var variantKey in variants)
        {
            if (snapshot is null)
            {
                break;
            }

            foreach (var hit in NameLeadTermSplitter.SplitIcd(snapshot, variantKey, NameLeadTermSplitter.MaxLeads))
            {
                if (splitHits.All(entry => entry.Hit.SeqNo != hit.SeqNo))
                {
                    splitHits.Add((hit, variantKey));
                }
            }
        }

        var leadTerms = new List<NameLookupLeadTerm>();
        var candidates = new List<RecommendationAccumulator>();
        var drillBudget = Math.Min(splitHits.Count, MaxDrillLeads);
        for (var index = 0; index < splitHits.Count; index++)
        {
            var (hit, variantKey) = splitHits[index];
            var paths = index < drillBudget && dictionary is not null
                ? DrillIcdLead(hit, text, variantKey, snapshot!, tumor, dictionary, candidates, channels)
                : [];
            // 转示 chip 只在走出修饰词链路时展示（如 阿米巴→阿米巴病）；
            // 无产出的转示（阑尾切除术 的 切除→消融(切除)）属纯噪声，不上屏。
            if (hit.RedirectKey is not null && paths.Count == 0)
            {
                continue;
            }

            AppendLeadTerm(leadTerms, hit.SeqNo, hit.Term, hit.HitAlias,
                hit.CoveredChars, hit.Position, hit.TailHit, paths, hit.RedirectKey);
        }

        return Assemble(kind: DiagnosisKind, text, inputKey, codeSystem, channels, directs, leadTerms, candidates, topK, degraded);
    }

    private static IReadOnlyList<NameLookupModifierPath> DrillIcdLead(
        NameLeadTermSplitter.IcdLeadTermHit hit,
        string text,
        string variantKey,
        IcdIndexSnapshot snapshot,
        TumorIndexSnapshot? tumor,
        IcdDictionarySnapshot dictionary,
        List<RecommendationAccumulator> candidates,
        NameLookupTitleChannels channels)
    {
        var paths = new List<NameLookupModifierPath>();
        DrillResult? redirectOwnFallback = null;
        var drill = IcdIndexMatcher.Drill(
            snapshot,
            new LeadWordHit(hit.Node, hit.Alias, 1m, hit.CoveredChars),
            variantKey);
        if (drill is not null
            && PathOverlapRatio(drill.Path, variantKey) >= MinPathOverlap
            && drill.Path.Skip(1).Where(node => !IcdIndexMatcher.IsConnectorNode(node))
                .All(node => ModifierNodeAllowed(node, variantKey))
            && drill.Path.Skip(1).All(node => ChildNotCoveredByPathLiteral(node, drill.Path, variantKey)))
        {
            paths.Add(BuildIcdPath(hit, text, drill, dictionary, tumor, candidates, channels, variantKey, seeAlsoHint: false));

            // 另见-1 提示链路：本地带码时 Top1 不动，目标树的更细码挂为提示（口径同流水线）。
            // 提示链路同样过修饰词核心词核对（防另见目标树里的枚举借道）。
            if (drill.SeeAlsoHints is { Count: > 0 })
            {
                foreach (var hint in drill.SeeAlsoHints.Take(2))
                {
                    if (!hint.Path.Skip(1).Where(node => !IcdIndexMatcher.IsConnectorNode(node))
                            .All(node => ModifierNodeAllowed(node, variantKey)))
                    {
                        continue;
                    }

                    paths.Add(BuildIcdPath(hit, text, hint, dictionary, tumor, candidates, channels, variantKey, seeAlsoHint: true));
                }
            }
        }
        else if (drill is not null
            && drill.Path.Count > 1
            && drill.CodeNode.Code is { Length: > 0 }
            && !drill.CodeFromAncestorFallback)
        {
            // 排除句式冲突截断：主链路被排除行（不伴有提及…证实）等门控拦下时，
            // 若截断到冲突节点**之前**的父层仍有码（肺 层 A16.2），按截断链路交付——
            // 查典语义：走到肺层确定，其下的"未证实"分支与输入（显微镜检证实）矛盾，
            // 停在父层取码。父层无码时不交付（避免祖先兜底泛码钻空子）。
            // 无冲突节点（链路是被重叠门槛等其他原因拦的）→ 不截断。
            var conflictIndex = drill.Path
                .Select((node, idx) => (node, idx))
                .Skip(1)
                .Where(entry => !ModifierNodeAllowed(entry.node, variantKey)
                    || !ChildNotCoveredByPathLiteral(entry.node, drill.Path, variantKey))
                .Select(entry => (int?)entry.idx)
                .FirstOrDefault();
            if (conflictIndex is > 1)
            {
                var truncated = drill.Path.Take(conflictIndex.Value).ToList();
                var truncatedCode = truncated.LastOrDefault(node => node.Code is { Length: > 0 });
                if (truncatedCode is not null)
                {
                    var truncatedResult = new DrillResult(
                        truncated[^1],
                        truncatedCode,
                        truncated,
                        string.Join(string.Empty, truncated.Skip(1).Select(node => node.Term)),
                        1m,
                        CodeFromAncestorFallback: false,
                        Jumps: ["截断: 排除句式与输入矛盾"]);
                    paths.Add(BuildIcdPath(hit, text, truncatedResult, dictionary, tumor, candidates, channels, variantKey, seeAlsoHint: false));
                }
            }
        }
        else if (hit.RedirectKey is not null && hit.Node.Code is { Length: > 0 })
        {
            // 转示锚定兜底：查 肠炎 必转 小肠炎，但"毗邻单胞菌肠炎"里输入写不出
            // 小肠炎下的任何修饰词（细菌性/由于…）——按查典语义取转示目标主导词
            // 自身默认码成链（修饰词树仍在链路上供编码员核对）。
            // 出声时机推迟到补钻之后：子行已经给出精确码时不再并列泛词本行码（见下方）。
            redirectOwnFallback = new DrillResult(
                hit.Node, hit.Node, [hit.Node], hit.Alias.Key,
                1m, CodeFromAncestorFallback: false, Jumps: [$"{hit.RedirectKey} → {hit.Node.Term}"]);
        }

        // 另见-1 挂在**主导词行自身**时（脓毒症(全身性)(化脓性)(另见 败血症) A41.9）：
        // 匹配器的提示逻辑只认"链路停住的那一行"（ReferenceEquals(current, hintChild)），
        // 主导词层没有父行，本行带码 + 指针目标树里另有细码这一步在两条通道上都不出声——
        // 鼠伤寒沙门菌脓毒症 因此只交付 A41.900 脓毒症，而编码员口径要求转到 败血症 树取 A02.100X002。
        // 与子行提示同口径：本行链路已交付时 Top1 不动，目标树细码作 另见 提示并列（排序见 Assemble）。
        if (hit.Node.JumpKind == IndexJumpKind.SeeAlso
            && hit.Node.JumpTarget is { Length: > 0 } leadJumpTarget
            && hit.Node.Code is { Length: > 0 } leadOwnCode
            && paths.Any(path => path.Codes.Any(code => !code.IsIndexFallback))
            && IcdIndexMatcher.ResolveJumpLead(snapshot, leadJumpTarget) is { } alsoLead
            && alsoLead.SeqNo != hit.Node.SeqNo
            && LeadSeeAlsoHintEnabled)
        {
            var seedKey = IcdIndexMatcher.JumpSeedKey(alsoLead, variantKey);
            var seedAlias = alsoLead.Aliases.FirstOrDefault(alias => alias.MatchKey == seedKey)
                            ?? alsoLead.Aliases.FirstOrDefault();
            if (seedAlias is not null
                && IcdIndexMatcher.Drill(
                    snapshot,
                    new LeadWordHit(alsoLead, seedAlias, 1m, seedAlias.Key.Length),
                    variantKey,
                    jumpTrace: $"{hit.Node.Term} ⇢ {leadJumpTarget}") is { } alsoDrill
                && alsoDrill.Path.Count > 1
                && !alsoDrill.CodeFromAncestorFallback
                && alsoDrill.CodeNode.Code != leadOwnCode
                && alsoDrill.Path.Skip(1).Where(node => !IcdIndexMatcher.IsConnectorNode(node))
                    .All(node => ModifierNodeAllowed(node, variantKey))
                && alsoDrill.Path.Skip(1).All(node => ChildNotCoveredByPathLiteral(node, alsoDrill.Path, variantKey)))
            {
                paths.Add(BuildIcdPath(hit, text, alsoDrill, dictionary, tumor, candidates, channels, variantKey,
                    seeAlsoHint: true, demoteHint: true, hintExactOnly: LeadSeeAlsoExactOnly));
            }
        }

        // 贪婪下钻只走一条选层，可能被"子行全名里含输入子串"的兄弟行挤掉正牌修饰词
        // （急性完全性痉挛性截瘫：截瘫(下肢) 下 梅毒性(痉挛性) 借 痉挛性 子串的镜像覆盖
        // 挤掉正牌 －痉挛性 G82.1，主链路又被核心词核对拦下 → 空结果）；
        // 或停在本行无任何修饰词（埃尔托生物型霍乱：霍乱 → 由于01群霍乱弧菌 → 埃尔托生物型
        // 的两层链选层失利，停在 A00.9 本行）。主链路无修饰词、无产出或只有回退码时，
        // 按核心词严格准入逐个子行补钻；连接行（见于(由于)/伴/与）穿透到其下层子行
        // （脑膜炎(基底) → 见于(由于) → 伤寒 的因果链由此捞出）。编码录入专用，流水线不动。
        var allCodesAreFallback = paths.SelectMany(path => path.Codes).All(code => code.IsIndexFallback);
        if (drill is null
            || drill.Path.Count <= 1
            || allCodesAreFallback)
        {
            foreach (var child in hit.Node.Children
                .Where(child => IcdIndexMatcher.LeadNodeAllows(child, variantKey))
                .Where(child => ModifierCoreContainedInInput(child, child.CoreTerm, variantKey)))
            {
                var childAlias = child.Aliases
                    .Where(alias => ModifierCoreContainedInInput(child, alias.MatchKey, variantKey))
                    .OrderByDescending(alias => alias.Key.Length)
                    .FirstOrDefault() ?? child.Aliases.FirstOrDefault();
                if (childAlias is null)
                {
                    continue;
                }

                var childDrill = IcdIndexMatcher.Drill(
                    snapshot,
                    new LeadWordHit(child, childAlias, 1m, childAlias.Key.Length),
                    variantKey);
                if (childDrill is null
                    || !childDrill.Path.Skip(1).Where(node => !IcdIndexMatcher.IsConnectorNode(node))
                        .All(node => ModifierNodeAllowed(node, variantKey)))
                {
                    continue;
                }

                // 枚举字面占位核对：子链路各节点不得借上层括号枚举（如 鼠伤寒 ⊃ 伤寒）的字面延伸。
                // 核对拦截时，若子行自身带码（沙门菌(…鼠伤寒) A02.9），改用"本行取码"链路交付——
                // 该行就是输入修饰词的终点，不得再向枚举字面借道下钻到 伤寒 A01.0。
                if (!childDrill.Path.Skip(1).All(node => ChildNotCoveredByPathLiteral(node, childDrill.Path, variantKey)))
                {
                    if (child.Code is { Length: > 0 } ownCode)
                    {
                        var ownPath = new List<IcdIndexNode> { hit.Node, child };
                        var ownComposed = new DrillResult(
                            child, child, ownPath, hit.Alias.Key + childAlias.Key,
                            1m, CodeFromAncestorFallback: false, Jumps: []);
                        var newPath = BuildIcdPath(hit, text, ownComposed, dictionary, tumor, candidates, channels, variantKey, seeAlsoHint: false);
                        // 避免与主链路重复（主链路已有相同 pathKey+indexCode 时跳过）
                        if (!paths.Any(p => p.PathKey == newPath.PathKey && p.IndexCode == newPath.IndexCode))
                        {
                            paths.Add(newPath);
                        }
                    }

                    continue;
                }

                // 证据链前置主导词：path[0] = 主导词行；pathKey = 主导词别名 + 子链路键。
                var fullPath = new List<IcdIndexNode> { hit.Node };
                fullPath.AddRange(childDrill.Path);
                var composed = new DrillResult(
                    childDrill.Terminal,
                    childDrill.CodeNode,
                    fullPath,
                    hit.Alias.Key + childDrill.PathKey,
                    childDrill.CoverRatio,
                    childDrill.CodeFromAncestorFallback,
                    childDrill.Jumps);
                var childPath = BuildIcdPath(hit, text, composed, dictionary, tumor, candidates, channels, variantKey, seeAlsoHint: false);
                // 避免与主链路重复（主链路已有相同 pathKey+indexCode 时跳过）
                if (!paths.Any(p => p.PathKey == childPath.PathKey && p.IndexCode == childPath.IndexCode))
                {
                    paths.Add(childPath);
                }
            }

            // 连接行穿透补钻：见于(由于)/伴/与 连接行的下层子行（如 伤寒）单独成链，
            // 证据链为 主导词 → 连接行 → 子链路（严格索引树路径展示）。
            foreach (var connector in hit.Node.Children
                .Where(child => IcdIndexMatcher.IsConnectorNode(child)))
            {
                foreach (var grandchild in connector.Children
                    .Where(grandchild => IcdIndexMatcher.LeadNodeAllows(grandchild, variantKey))
                    .Where(grandchild => ModifierCoreContainedInInput(grandchild, grandchild.CoreTerm, variantKey)))
                {
                    var grandAlias = grandchild.Aliases
                        .Where(alias => ModifierCoreContainedInInput(grandchild, alias.MatchKey, variantKey))
                        .OrderByDescending(alias => alias.Key.Length)
                        .FirstOrDefault() ?? grandchild.Aliases.FirstOrDefault();
                    if (grandAlias is null)
                    {
                        continue;
                    }

                    var grandDrill = IcdIndexMatcher.Drill(
                        snapshot,
                        new LeadWordHit(grandchild, grandAlias, 1m, grandAlias.Key.Length),
                        variantKey);
                    if (grandDrill is null
                        || !grandDrill.Path.Skip(1).Where(node => !IcdIndexMatcher.IsConnectorNode(node))
                            .All(node => ModifierNodeAllowed(node, variantKey)))
                    {
                        continue;
                    }

                    // 枚举字面占位核对（连接行穿透链路同样适用）。
                    if (!grandDrill.Path.Skip(1).All(node => ChildNotCoveredByPathLiteral(node, grandDrill.Path, variantKey)))
                    {
                        continue;
                    }

                    var fullPath = new List<IcdIndexNode> { hit.Node, connector };
                    fullPath.AddRange(grandDrill.Path);
                    var composed = new DrillResult(
                        grandDrill.Terminal,
                        grandDrill.CodeNode,
                        fullPath,
                        hit.Alias.Key + grandAlias.Key + grandDrill.PathKey,
                        grandDrill.CoverRatio,
                        grandDrill.CodeFromAncestorFallback,
                        grandDrill.Jumps);
                    paths.Add(BuildIcdPath(hit, text, composed, dictionary, tumor, candidates, channels, variantKey, seeAlsoHint: false));
                }
            }
        }

        // 转示本行兜底只在别处无精确码时出声：扁桃体炎 →（见 感染）→ 感染→扁桃体
        // 已交付 J03.900 急性扁桃体炎时，不再并列 感染 本行的类目回退码 A49.900——
        // 泛词本行码与输入病种不同源，按"少而准"属噪声（噪声族实测：E05.500 危象、
        // Z71.900 咨询、T78.900 效应 由 危象/咨询/效应 这类转示目标的 本行取码 顶上）。
        if (redirectOwnFallback is { } ownFallback
            && !paths.SelectMany(path => path.Codes).Any(code => !code.IsIndexFallback))
        {
            paths.Add(BuildIcdPath(hit, text, ownFallback, dictionary, tumor, candidates, channels, variantKey, seeAlsoHint: false));
        }

        return paths;
    }

    private static NameLookupModifierPath BuildIcdPath(
        NameLeadTermSplitter.IcdLeadTermHit hit,
        string text,
        DrillResult drill,
        IcdDictionarySnapshot dictionary,
        TumorIndexSnapshot? tumor,
        List<RecommendationAccumulator> candidates,
        NameLookupTitleChannels channels,
        string inputKey,
        bool seeAlsoHint,
        // 只让**主导词行自身**的 另见-1 提示码在排序里殿后。既有子行提示码不参与：
        // 实测对它们一律降级会把正牌精确码一起按下（大脑中动脉狭窄 I66.001、急性子宫炎
        // N71.002、1型糖尿病性感觉运动性周围神经病 E10.400X026+G63.2* 各让给泛码，约 60 名 Top1 回退）。
        // 逐字整标题命中的提示码（WholeTitleDepth==1）不降级，照常参加排序：
        // 土拉菌脓毒症 → 脓毒症(另见 败血症) ⇢ 败血症→土拉菌性 → A21.700X002 才是答案，
        // 把它压在 A41.900 泛词之后等于用"只做提示"挡住编码员要用的细码（万条实测 26 名）。
        bool demoteHint = false,
        // 提示链路只交付整标题精确码（非精确的并列尾部属噪声，按"少而准"整条不收）。
        bool hintExactOnly = false)
    {
        // 展示链路严格按索引树路径（不加步骤、不折叠中间层）：
        // 霍乱 → 由于01群霍乱弧菌 → 埃尔托生物型 与库中真实层级一致。
        var path = drill.Path.Select(node => node.Term).ToList();
        var indexCode = drill.CodeNode.Code;
        var codes = new List<NameLookupCodeItem>();
        var parsed = ParsedIndexCode.Parse(indexCode);
        // 展开门控的输入恒用用户原文：改写变体（癫癎）服务索引召回，字典标题写的是临床写法（癫痫）。
        // 同族标题反查随运行时开关（IcdTitleReverseEnabled）启停，与流水线同源。
        var expanded = IndexCodeExpander.Expand(
            parsed, dictionary, text, drill.PathKey, CodesPerPath,
            NameCodingLookupEngine.DiagnosisCodeSystem, tumor, titleReverseEnabled: channels.IcdTitleReverse);
        foreach (var item in expanded)
        {
            if (item.IsIndexFallback && !FallbackDeliveryAllowed(drill, inputKey))
            {
                continue;
            }

            if (demoteHint && hintExactOnly && item.WholeTitleDepth < 1m)
            {
                continue;
            }

            codes.Add(new NameLookupCodeItem(
                item.CodeSystemCode, item.Code, item.Title, item.Score,
                item.IsIndexFallback, item.Note));
            candidates.Add(new RecommendationAccumulator(
                item.CodeSystemCode, item.Code, item.IsIndexFallback ? drill.Path[^1].Term : item.Title,
                Score(item),
                hit.Term, path, item.IsIndexFallback, item.Note,
                item.WholeTitleDepth, item.TitleSpecificity, hit.CoveredChars, drill.Path.Count,
                demoteHint && item.WholeTitleDepth < 1m));
        }

        return new NameLookupModifierPath(
            path, drill.PathKey, indexCode, drill.CodeFromAncestorFallback,
            drill.Jumps, seeAlsoHint, codes);
    }

    // ===== 手术侧（cm3_index + ICD-9-CM-3 字典）=====

    private static NameLookupResponse LookupProcedure(
        string text,
        string inputKey,
        int? topK,
        Cm3IndexSnapshot? snapshot,
        IcdDictionarySnapshot? dictionary,
        List<string> degraded,
        string codeSystem,
        NameLookupTitleChannels channels)
    {
        if (snapshot is null)
        {
            degraded.Add("DEGRADED.NO_CM3_INDEX");
        }

        if (dictionary is null)
        {
            degraded.Add("DEGRADED.NO_DICTIONARY");
        }

        // 手术侧标题直达受运行时开关门控（与流水线 Cm3TitleDirectEnabled 同源）。
        var directs = dictionary is null || !channels.Cm3TitleDirect
            ? []
            : dictionary.TitleDirectMatches(inputKey)
                .Select(match => new NameLookupDictionaryDirect(
                    codeSystem, match.Row.Code, match.Row.Title, match.Depth))
                .ToList();

        var leadTerms = new List<NameLookupLeadTerm>();
        var candidates = new List<RecommendationAccumulator>();
        var hits = snapshot is null
            ? []
            : NameLeadTermSplitter.SplitCm3(snapshot, inputKey, NameLeadTermSplitter.MaxLeads);
        var drillBudget = Math.Min(hits.Count, MaxDrillLeads);
        for (var index = 0; index < hits.Count; index++)
        {
            var hit = hits[index];
            var paths = index < drillBudget && dictionary is not null
                ? DrillCm3Lead(hit, text, snapshot!, dictionary, candidates)
                : [];
            // 同诊断侧：无产出的转示 chip 不上屏。
            if (hit.RedirectKey is not null && paths.Count == 0)
            {
                continue;
            }

            AppendLeadTerm(leadTerms, hit.SeqNo, hit.Term, hit.HitAlias,
                hit.CoveredChars, hit.Position, hit.TailHit, paths, hit.RedirectKey);
        }

        return Assemble(kind: ProcedureKind, text, inputKey, codeSystem, channels, directs, leadTerms, candidates, topK, degraded);
    }

    private static IReadOnlyList<NameLookupModifierPath> DrillCm3Lead(
        NameLeadTermSplitter.Cm3LeadTermHit hit,
        string text,
        Cm3IndexSnapshot snapshot,
        IcdDictionarySnapshot dictionary,
        List<RecommendationAccumulator> candidates)
    {
        var inputKey = TextMatch.NormalizeKey(text);
        var paths = new List<NameLookupModifierPath>();
        var drill = Cm3ProcedureIndexMatcher.Drill(
            snapshot,
            new Cm3LeadWordHit(hit.Node, hit.Alias, 1m, hit.CoveredChars),
            inputKey);
        if (drill is not null
            && Cm3ProcedureIndexResolver.PathOverlapRatio(drill.Path, text) >= MinPathOverlap
            && drill.Path.Skip(1).Where(node => !Cm3ProcedureIndexMatcher.IsConnectorNode(node))
                .All(node => ModifierNodeMatchesInput(node.CoreTerm, inputKey)))
        {
            if (!BuildCm3Path(hit, text, drill, dictionary, candidates, paths, seeAlsoHint: false)
                && drill.Displaced is { } displaced
                && displaced.Path.Skip(1).Where(node => !Cm3ProcedureIndexMatcher.IsConnectorNode(node))
                    .All(node => ModifierNodeMatchesInput(node.CoreTerm, inputKey)))
            {
                // 见-1 强制改道让位：主链路展不开时按本地让位链路兜底（口径同 Cm3ProcedureIndexResolver）。
                BuildCm3Path(hit, text, displaced, dictionary, candidates, paths, seeAlsoHint: false);
            }

            if (drill.SeeAlsoHints is { Count: > 0 })
            {
                foreach (var hint in drill.SeeAlsoHints.Take(2))
                {
                    if (!hint.Path.Skip(1).Where(node => !Cm3ProcedureIndexMatcher.IsConnectorNode(node))
                        .All(node => ModifierNodeMatchesInput(node.CoreTerm, inputKey)))
                    {
                        continue;
                    }

                    BuildCm3Path(hit, text, hint, dictionary, candidates, paths, seeAlsoHint: true);
                }
            }
        }

        // 同诊断侧：主链路无修饰词、无产出或只产出回退码时补钻（核心词入文严格准入）；连接行穿透。
        if (drill is null
            || drill.Path.Count <= 1
            || paths.SelectMany(path => path.Codes).All(code => code.IsIndexFallback))
        {
            foreach (var child in hit.Node.Children
                .Where(child => Cm3ProcedureIndexMatcher.LeadNodeAllows(child, inputKey))
                .Where(child => ModifierCoreContainedInInput(child, child.CoreTerm, inputKey)))
            {
                var childAlias = child.Aliases
                    .Where(alias => ModifierCoreContainedInInput(child, alias.MatchKey, inputKey))
                    .OrderByDescending(alias => alias.Key.Length)
                    .FirstOrDefault() ?? child.Aliases.FirstOrDefault();
                if (childAlias is null)
                {
                    continue;
                }

                var childDrill = Cm3ProcedureIndexMatcher.Drill(
                    snapshot,
                    new Cm3LeadWordHit(child, childAlias, 1m, childAlias.Key.Length),
                    inputKey);
                if (childDrill is null
                    || Cm3ProcedureIndexResolver.PathOverlapRatio(childDrill.Path, text) < MinPathOverlap
                    || !childDrill.Path.Skip(1).Where(node => !Cm3ProcedureIndexMatcher.IsConnectorNode(node))
                        .All(node => ModifierNodeMatchesInput(node.CoreTerm, inputKey)))
                {
                    continue;
                }

                var fullPath = new List<Cm3IndexNode> { hit.Node };
                fullPath.AddRange(childDrill.Path);
                var composed = new Cm3DrillResult(
                    childDrill.Terminal,
                    childDrill.CodeNode,
                    fullPath,
                    hit.Alias.Key + childDrill.PathKey,
                    childDrill.CoverRatio,
                    childDrill.CodeFromAncestorFallback,
                    childDrill.Jumps);
                BuildCm3Path(hit, text, composed, dictionary, candidates, paths, seeAlsoHint: false);
            }

            foreach (var connector in hit.Node.Children
                .Where(child => Cm3ProcedureIndexMatcher.IsConnectorNode(child)))
            {
                foreach (var grandchild in connector.Children
                    .Where(grandchild => Cm3ProcedureIndexMatcher.LeadNodeAllows(grandchild, inputKey))
                    .Where(grandchild => ModifierCoreContainedInInput(grandchild, grandchild.CoreTerm, inputKey)))
                {
                    var grandAlias = grandchild.Aliases
                        .Where(alias => ModifierCoreContainedInInput(grandchild, alias.MatchKey, inputKey))
                        .OrderByDescending(alias => alias.Key.Length)
                        .FirstOrDefault() ?? grandchild.Aliases.FirstOrDefault();
                    if (grandAlias is null)
                    {
                        continue;
                    }

                    var grandDrill = Cm3ProcedureIndexMatcher.Drill(
                        snapshot,
                        new Cm3LeadWordHit(grandchild, grandAlias, 1m, grandAlias.Key.Length),
                        inputKey);
                    if (grandDrill is null
                        || Cm3ProcedureIndexResolver.PathOverlapRatio(grandDrill.Path, text) < MinPathOverlap
                        || !grandDrill.Path.Skip(1).Where(node => !Cm3ProcedureIndexMatcher.IsConnectorNode(node))
                            .All(node => ModifierNodeMatchesInput(node.CoreTerm, inputKey)))
                    {
                        continue;
                    }

                    var fullPath = new List<Cm3IndexNode> { hit.Node, connector };
                    fullPath.AddRange(grandDrill.Path);
                    var composed = new Cm3DrillResult(
                        grandDrill.Terminal,
                        grandDrill.CodeNode,
                        fullPath,
                        hit.Alias.Key + grandAlias.Key + grandDrill.PathKey,
                        grandDrill.CoverRatio,
                        grandDrill.CodeFromAncestorFallback,
                        grandDrill.Jumps);
                    BuildCm3Path(hit, text, composed, dictionary, candidates, paths, seeAlsoHint: false);
                }
            }
        }

        return paths;
    }

    private static bool BuildCm3Path(
        NameLeadTermSplitter.Cm3LeadTermHit hit,
        string text,
        Cm3DrillResult drill,
        IcdDictionarySnapshot dictionary,
        List<RecommendationAccumulator> candidates,
        List<NameLookupModifierPath> paths,
        bool seeAlsoHint)
    {
        // 展示链路严格按索引树路径（不折叠连接行），与诊断侧口径一致。
        var path = drill.Path.Select(node => node.Term).ToList();
        var indexCode = drill.CodeNode.Code;
        var expanded = Cm3IndexCodeExpander.Expand(indexCode, dictionary, text, drill.PathKey, CodesPerPath);
        if (expanded.Count == 0)
        {
            paths.Add(new NameLookupModifierPath(
                path, drill.PathKey, indexCode, drill.CodeFromAncestorFallback,
                drill.Jumps, seeAlsoHint, []));
            return false;
        }

        var codes = new List<NameLookupCodeItem>();
        foreach (var item in expanded)
        {
            codes.Add(new NameLookupCodeItem(
                item.CodeSystemCode, item.Code, item.Title, item.Score,
                item.IsIndexFallback, item.Note));
            candidates.Add(new RecommendationAccumulator(
                item.CodeSystemCode, item.Code, item.IsIndexFallback ? drill.Path[^1].Term : item.Title,
                Score(item.Score, item.IsIndexFallback),
                hit.Term, path, item.IsIndexFallback, item.Note,
                item.WholeTitleDepth, item.TitleSpecificity, hit.CoveredChars, drill.Path.Count));
        }

        paths.Add(new NameLookupModifierPath(
            path, drill.PathKey, indexCode, drill.CodeFromAncestorFallback,
            drill.Jumps, seeAlsoHint, codes));
        return true;
    }

    // ===== 汇总 =====

    private static NameLookupResponse Assemble(
        string kind,
        string text,
        string inputKey,
        string codeSystem,
        NameLookupTitleChannels channels,
        IReadOnlyList<NameLookupDictionaryDirect> directs,
        IReadOnlyList<NameLookupLeadTerm> leadTerms,
        List<RecommendationAccumulator> candidates,
        int? topK,
        List<string> degraded)
    {
        var budget = Math.Clamp(topK ?? DefaultTopK, 1, 32);
        var recommendations = new List<NameLookupRecommendation>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 字典直达置顶（标题逐字相等 > 被完整包含）。
        foreach (var direct in directs
            .OrderByDescending(item => item.Depth)
            .ThenBy(item => item.Code, StringComparer.Ordinal))
        {
            if (taken.Add(direct.Code))
            {
                recommendations.Add(new NameLookupRecommendation(
                    direct.CodeSystem, direct.Code, direct.Title,
                    Math.Min(1m, direct.Depth), IsIndexFallback: false, DictionaryDirect: true,
                    LeadTerm: null, Path: [], Note: "字典标题直达"));
                if (recommendations.Count >= budget)
                {
                    return new NameLookupResponse(kind, text, inputKey, codeSystem,
                        ChannelsInfo(channels), directs, leadTerms, recommendations, degraded);
                }
            }
        }

        // 索引候选跨主导词去重（同码取分高者），回退码殿后、主导词行自身的 另见-1 提示码再殿后；
        // 平手时按整标题贴合度 → 标题反向覆盖 → 主导词覆盖字数 → 链路深度 → 编码长度裁决
        // （口径对齐 IndexCodeExpander.Rank）。同码多来源时去重优先取**非提示**来源，
        // 否则 鼠伤寒沙门菌脓毒症 的 A02.100X002 会带着 脓毒症 主导词提示的身份被降级
        // （它同时是 沙门菌→感染→败血症 链路正牌产出）。
        // 提示码降级只认 demoteHint（主导词行级），既有子行提示码不参与——对它们一律降级实测
        // 会把正牌精确码一起按下（大脑中动脉狭窄 I66.001、急性子宫炎 N71.002、
        // 1型糖尿病性感觉运动性周围神经病 E10.400X026+G63.2* 各让给泛码，约 60 名 Top1 回退）。
        foreach (var group in candidates
            .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => item.IsIndexFallback)
                .ThenBy(item => item.IsSeeAlsoHint)
                .ThenByDescending(item => item.Score)
                .ThenByDescending(item => item.WholeTitleDepth)
                .ThenByDescending(item => item.TitleSpecificity)
                .First())
            .OrderBy(item => item.IsIndexFallback)
            .ThenBy(item => item.IsSeeAlsoHint)
            .ThenByDescending(item => item.Score)
            .ThenByDescending(item => item.WholeTitleDepth)
            .ThenByDescending(item => item.TitleSpecificity)
            .ThenByDescending(item => item.LeadCoveredChars)
            .ThenByDescending(item => item.PathCount)
            .ThenBy(item => item.Code.Length)
            .ThenBy(item => item.Code, StringComparer.Ordinal))
        {
            if (recommendations.Count >= budget)
            {
                break;
            }

            if (taken.Add(group.Code))
            {
                recommendations.Add(new NameLookupRecommendation(
                    group.CodeSystemCode, group.Code, group.Title, Math.Min(1m, group.Score),
                    group.IsIndexFallback, DictionaryDirect: false,
                    group.LeadTerm, group.Path, group.Note));
            }
        }

        return new NameLookupResponse(kind, text, inputKey, codeSystem, ChannelsInfo(channels), directs, leadTerms, recommendations, degraded);
    }

    private static NameLookupResponse Empty(string kind, string text, string codeSystem, NameLookupTitleChannels channels)
        => new(kind, text, string.Empty, codeSystem, ChannelsInfo(channels), [], [], [], []);

    /// <summary>
    /// 逗号行的显示词面（纯展示，不影响匹配/去重/证据链——完整行名始终在 tooltip 与链路里）：
    /// 尾段与头段同义（结构化同义判定 TextMatch.IsSynonymousCommaTail："梗死，梗塞"、"炎，炎症(性)…"）→
    /// 显示头段（编码员的直观主导词：梗死/炎）；尾段有独立语义（"断裂，视网膜" 的部位段）→
    /// 保持整行词面。
    /// </summary>
    private static string DisplayTermOf(string term)
    {
        var commaIndex = term.IndexOfAny(['，', ',']);
        if (commaIndex < 0)
        {
            return term;
        }

        var head = term[..commaIndex].Trim();
        var tail = term[(commaIndex + 1)..].Trim();
        if (head.Length == 0
            || !TextMatch.IsSynonymousCommaTail(TextMatch.NormalizeKey(head), TextMatch.NormalizeKey(tail)))
        {
            return term;
        }

        return head;
    }

    /// <summary>
    /// 补钻子行的严格准入（核心词入文）。node 参数：
    /// - null：别名预筛场景（无树上下文），因果行按词面判定；
    /// - ICD 节点：带码因果行（输注…T80.2）在"病因未写入输入且无下层修饰词"时拦截，
    ///   分类过道（由于01群霍乱弧菌→埃尔托生物型）在下层存在词面入文修饰词时放行穿透；
    /// - CM3 节点：无因果行形态（实测 0 条），等价 null 分支。
    /// </summary>
    private static bool ModifierCoreContainedInInput(IcdIndexNode? node, string coreTerm, string inputKey)
    {
        var core = TextMatch.NormalizeKey(coreTerm);
        if (core.Length >= 3 && core.EndsWith("性", StringComparison.Ordinal))
        {
            core = core[..^1];
        }

        if (core.Length == 0)
        {
            return true;
        }

        var isCausalRow = core.StartsWith("由于", StringComparison.Ordinal)
            || core.StartsWith("见于", StringComparison.Ordinal)
            || core.StartsWith("继发于", StringComparison.Ordinal);
        var causalPrefixLength = core.StartsWith("继发于", StringComparison.Ordinal) ? 3 : 2;
        var isCausalText = isCausalRow
            && !inputKey.Contains(core[causalPrefixLength..], StringComparison.Ordinal);

        if (!isCausalText)
        {
            return inputKey.Contains(core, StringComparison.Ordinal);
        }

        // 因果词未在输入逐字写明：仅当该行下层存在核心词入文的修饰词（分类过道）时放行。
        return node is not null && node.Children.Any(child =>
            ModifierCoreContainedInInput((IcdIndexNode?)null, child.CoreTerm, inputKey));
    }

    private static bool ModifierCoreContainedInInput(Cm3IndexNode? node, string coreTerm, string inputKey)
    {
        var core = TextMatch.NormalizeKey(coreTerm);
        if (core.Length >= 3 && core.EndsWith("性", StringComparison.Ordinal))
        {
            core = core[..^1];
        }

        return core.Length == 0 || inputKey.Contains(core, StringComparison.Ordinal);
    }

    /// <summary>
    /// 枚举字面占位核对（"编码录入"专用，下钻子行准入的最后一道）：
    /// 括号枚举行（沙门菌(亚利桑那)(猪霍乱)(小肠炎)(鼠伤寒)）的词面里含有别的疾病的名字
    /// （鼠伤寒 ⊃ 伤寒），链路走到枚举行后，其子行 伤寒 会借 鼠伤寒 已解释过的字面
    /// 继续挣增益、把推荐带到 A01.0。
    /// 判据（三合一，只拦枚举借道、不伤正常修饰链）：
    /// ① 上层词面**含括号**（枚举行的特征，CoreTerm 与完整词面不同）；
    /// ② 上层完整词面包含子行核心词（鼠伤寒 ⊃ 伤寒）；
    /// ③ 该枚举段（括号内容或含枚举的尾段）不在输入逐字出现——上层的解释力来自
    ///    枚举字面而非输入。正常修饰链（截瘫 → 痉挛性、膀胱炎 → 阿米巴）的上层
    ///    无括号枚举或不包含子行核心词，不受影响。
    /// </summary>
    private static bool ChildNotCoveredByPathLiteral(IcdIndexNode child, IReadOnlyList<IcdIndexNode> path, string inputKey)
    {
        var childCore = TextMatch.NormalizeKey(child.CoreTerm);
        if (childCore.Length < 2)
        {
            return true;
        }

        foreach (var ancestor in path)
        {
            // 跳过子行自身（补钻链路 path[0] 就是被核对的子行，自身不是自己的"上层枚举"）。
            if (ReferenceEquals(ancestor, child))
            {
                continue;
            }

            // ① 枚举行特征：完整词面与剥括号核心词不同（含括号注解）。
            var ancestorLiteral = TextMatch.NormalizeKey(ancestor.Term);
            var ancestorCore = TextMatch.NormalizeKey(ancestor.CoreTerm);
            if (ancestorLiteral.Length < 2 || ancestorLiteral == ancestorCore)
            {
                continue;
            }

            // ② 上层完整词面包含子行核心词。
            if (!ancestorLiteral.Contains(childCore, StringComparison.Ordinal))
            {
                continue;
            }

            // ③ 枚举段不在输入逐字出现，且子行核心词在输入中出现——枚举行借字面占位成立。
            if (!inputKey.Contains(ancestorLiteral, StringComparison.Ordinal)
                && inputKey.Contains(childCore, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 链路是否**只**靠括号枚举段准入（核心词核对本身落空，靠 亚利桑那/手术后/括约肌 这类括注沾住输入）。
    /// </summary>
    private static bool AdmittedOnlyByEnumeration(IcdIndexNode node, string inputKey)
        => !ModifierNodeMatchesInput(node.CoreTerm, inputKey)
            && EnumerationSegments(node).Any(segment => inputKey.Contains(segment, StringComparison.Ordinal));

    /// <summary>
    /// 只靠枚举段沾住输入的链路，不得交付**回退码**（本行无码回退到祖先，或字典侧回退到类目码）。
    /// 这是 <see cref="EnumerationSegments"/> 的"≥2 组"判据之外的第二层：
    /// 奥迪括约肌狭窄→K62.400 肛门，肛(管)(括约肌)、亚利桑那菌肠炎→A02.000 沙门菌(病)(亚利桑那)…
    /// 都是**多组括号**（管/括约肌、亚利桑那/猪霍乱/…）却仍只交付回退码——段沾住了输入，
    /// 行身份由输入根本没写的头段部位词（肛门）决定，回退码把"别部位的同属性病"端上桌。
    /// 只剔回退码、不剔链路：本行精确展开（亚利桑那菌感染 A02.900X004）照旧交付。
    /// 这里曾一并禁过**转示锚定的多字链路**（万名 30+ 名 高→效应 T78.900、医疗→咨询 Z71.900
    /// 这类只出泛码的补出），代价是 37 名打空，而 云母尘肺→J62.800、不稳定髋→M24.500、
    /// 宫颈产科裂伤→S31.400 丢的是成链的类目码不是泛词，按硬线不可豁免 → 撤刀，
    /// 泛词噪声改由单字转示键位置门控 + 转示本行兜底让位精确码两处收。
    /// </summary>
    private static bool FallbackDeliveryAllowed(DrillResult drill, string inputKey)
        => !drill.Path.Skip(1)
            .Where(node => !IcdIndexMatcher.IsConnectorNode(node))
            .Any(node => AdmittedOnlyByEnumeration(node, inputKey));

    /// <summary>
    /// 修饰行准入（诊断侧"编码录入"唯一口径）：核心词核对落空时，再看**括号枚举段**是否逐字入文。
    /// 索引把同一病原/同一病种的另一种写法写进紧邻的多个圆括号
    /// （"沙门菌(亚利桑那)(猪霍乱)(小肠炎)(鼠伤寒)"），输入只写 亚利桑那菌感染 时核心词 沙门菌
    /// 不在词面里、整条链路有码却不交付（流水线同输入出 A02.900X004，面板 0 链路）。
    /// 只放宽**已选链路的事后核对**，不参与召回与选层；与 <see cref="ChildNotCoveredByPathLiteral"/>
    /// 的 ③ 共用"枚举段是否逐字入文"这同一条判据：入文→有解释力，未入文→字面借道照旧拦。
    /// </summary>
    private static bool ModifierNodeAllowed(IcdIndexNode node, string inputKey)
        => ModifierNodeMatchesInput(node.CoreTerm, inputKey)
            || EnumerationSegments(node).Any(segment => inputKey.Contains(segment, StringComparison.Ordinal));

    /// <summary>
    /// 圆括号里的并列枚举段（已归一化、≥3 字）。三档排除，全部是实测噪声换来的：
    /// ① **只在 ≥2 个非指针括号组时启用**——索引把"同一槽位的并列写法"写成紧邻的多组括号；
    ///    单组括号多数是头段词的澄清注解或属性尾注（肠系膜(淋巴腺或淋巴结)、局灶性和节段性(肾小球)、
    ///    输尿管(手术后)、肛门(括约肌)），放行后 淋巴结结核/腹腔淋巴结结核/腮腺淋巴结结核/纵隔淋巴结结核
    ///    四名的正牌 A18.200X006 被 肠系膜 行的 A18.316+K93.0* 顶掉、肾小球硬化 N26.X01→N04.101。
    ///    代价：常染色体隐性(婴儿型) → 婴儿型多囊肾 Q61.101 这类合法等价写法一并挡掉（万名 1 名）。
    /// ② 指针括号（另见/见/又称/译名）与带 ASCII 的形态学码（M8000/1）不是病名；
    /// ③ 以 性 收尾、连接词开头与 1-2 字段不作枚举段。
    /// 多组括号仍漏的（肛门，肛(管)(括约肌)）由 <see cref="FallbackDeliveryAllowed"/> 补刀：只准交付本行码。
    /// </summary>
    private static IReadOnlyList<string> EnumerationSegments(IcdIndexNode node)
    {
        var groups = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in ParenthesisContentRegex.Matches(node.Term))
        {
            var raw = match.Groups[1].Value;
            if (raw.Contains('见') || raw.Contains("又称") || raw.Contains("译名"))
            {
                continue;
            }

            groups.Add(raw);
        }

        if (groups.Count < 2)
        {
            return [];
        }

        var segments = new List<string>();
        foreach (var raw in groups)
        {
            foreach (var piece in raw.Split(EnumerationSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var key = TextMatch.NormalizeKey(piece);
                if (key.Length < 3
                    || key.Any(char.IsAsciiLetterOrDigit)
                    // 以 性 收尾的括注是"同一病名的可选属性写法"（(创伤性)(化脓性)(复发性)(红斑性)），
                    // 不是另一个病名：实测放行后 创伤性脑疝 会拿 S05.207 创伤性虹膜疝、
                    // 小腿慢性化脓性骨髓炎 会从正牌 M86.600X061 换成药 K10.207 化脓性颌骨髓炎。
                    || key.EndsWith("性", StringComparison.Ordinal)
                    || GenericAttributeSegmentKeys.Contains(key)
                    || EnumerationConnectorPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (!segments.Contains(key))
                {
                    segments.Add(key);
                }
            }
        }

        return segments;
    }

    private static readonly System.Text.RegularExpressions.Regex ParenthesisContentRegex =
        new(@"[（(]([^（()）]*)[）)]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly char[] EnumerationSeparators = ['或', '，', ',', '、'];

    private static readonly HashSet<string> GenericAttributeSegmentKeys =
        new(StringComparer.Ordinal)
        {
            "胎儿", "婴儿", "新生儿", "成人", "老年", "小儿", "发作",
        };

    private static readonly string[] EnumerationConnectorPrefixes =
        ["伴有", "伴", "不伴", "没有", "见于", "由于", "继发于", "和", "与", "及", "包括"];

    // 轮次口径开关（A/B 用，不入库）：主导词行自身 另见-1 提示链路。默认关。
    // 1=提示链路全收（非精确码殿后），2=只收整标题精确码。取用时求值（不是静态字段），
    // 单测与同进程内的对照轮都能按用例开关。
    private static bool LeadSeeAlsoHintEnabled =>
        Environment.GetEnvironmentVariable("NL_LEAD_SEEALSO") is "1" or "2";

    private static bool LeadSeeAlsoExactOnly =>
        Environment.GetEnvironmentVariable("NL_LEAD_SEEALSO") == "2";

    /// <summary>
    /// 修饰词节点核心词与输入的词面核对：核心词（剥括号注解后的主段）须被输入连续包含，
    /// 或与输入存在 ≥2 字共同连续段；核心词 ≤2 字时按单字相交放行——保住
    /// "索引 大脑 / 输入 脑" 的简称部位词惯例与单字部位词下钻。
    /// 逗号附加语核对：核心词含逗号（"δ因子(急性)，于乙型肝炎病毒携带者"）时，
    /// 逗号前**主段**必须与输入有 ≥2 字共同段——主段零交集、只靠附加语里一个词
    /// （乙型肝炎）沾边的行不得入链（病毒性肝炎重叠感染 实测）。
    /// </summary>
    private static bool ModifierNodeMatchesInput(string coreTerm, string inputKey)
    {
        if (IcdIndexMatcher.IsGenericConditionLeadKey(coreTerm))
        {
            return true;
        }

        var core = TextMatch.NormalizeKey(coreTerm);
        if (core.Length == 0)
        {
            return true;
        }

        // 逗号附加语核对：主段零交集 → 整行不入（附加语里的词根沾边不算数）。
        // 注意在**原文**上找逗号（NormalizeKey 会把逗号剥掉），再映射到归一化串的比例位置。
        var rawCommaIndex = coreTerm.IndexOfAny(['，', ',']);
        if (rawCommaIndex > 0 && coreTerm.Length > 0)
        {
            var headRaw = coreTerm[..rawCommaIndex];
            var head = TextMatch.NormalizeKey(headRaw);
            if (head.Length >= 2
                && !inputKey.Contains(head, StringComparison.Ordinal)
                && LongestCommonSegment(head, inputKey) < 2)
            {
                return false;
            }
        }

        // 排除句式门：索引的排除类修饰行（"不伴有提及细菌学或组织学证实" A16.2、
        // "不伴有…"族）与输入逐字写出了**被排除的关键词**（肺结核(显微镜检证实) 的 证实）
        // 相矛盾——输入声明了 已证实 却落到 未证实 分支。核对：行词面以排除词头开头
        // 且行内含输入也含的关键词尾（≥2 字共同段紧邻排除语义）→ 拦。
        // 输入写 未证实/未做/未提及 时排除词头与之一致，不触发。
        if (ModifierExclusionConflicts(coreTerm, inputKey))
        {
            return false;
        }

        if (inputKey.Contains(core, StringComparison.Ordinal))
        {
            return true;
        }

        var shortest = Math.Min(core.Length, inputKey.Length);
        for (var length = Math.Min(shortest, core.Length); length >= 2; length--)
        {
            for (var start = 0; start + length <= core.Length; start++)
            {
                if (inputKey.Contains(core.Substring(start, length), StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return core.Length <= 2 && core.Any(character => inputKey.Contains(character));
    }

    /// <summary>两串的最长共同连续段长度。</summary>
    private static int LongestCommonSegment(string left, string right)
    {
        var best = 0;
        for (var i = 0; i < left.Length; i++)
        {
            for (var j = 0; j < right.Length; j++)
            {
                var length = 0;
                while (i + length < left.Length && j + length < right.Length
                    && left[i + length] == right[j + length])
                {
                    length++;
                }

                best = Math.Max(best, length);
            }
        }

        return best;
    }

    /// <summary>
    /// 排除句式冲突核对（"编码录入"修饰词准入）：
    /// 索引排除类行（"不伴有提及细菌学或组织学证实"、"不伴有…"、"未见…"）声明的是
    /// "未做/未提及 X"分支；输入逐字写出 X 且无"未/不"前缀时（肺结核(显微镜检证实)
    /// 写了 证实），输入与该分支矛盾 → 拦。输入写 未证实/未提及/未做 或未写 X → 放行。
    /// </summary>
    private static bool ModifierExclusionConflicts(string coreTerm, string inputKey)
    {
        var raw = coreTerm.Trim();
        string[] exclusionHeads = ["不伴有", "不伴有提及", "未见", "不伴"];
        var head = exclusionHeads.FirstOrDefault(raw.StartsWith);
        if (head is null)
        {
            return false;
        }

        var rest = raw[head.Length..].Trim();
        if (rest.Length < 2)
        {
            return false;
        }

        // 被排除关键词 = 排除词头之后的实义词（细菌学/组织学/证实）。
        // 输入含该词、且**该词出现位置的前方 4 字窗口**内没有 未/不/无 前缀 → 矛盾；
        // 输入写 未做细菌学证实/未证实/未见证实 时前方有否定前缀 → 一致放行。
        var keywords = new[] { "证实", "提及", "细菌学", "组织学" };
        foreach (var keyword in keywords)
        {
            if (!rest.Contains(keyword, StringComparison.Ordinal))
            {
                continue;
            }

            if (!inputKey.Contains(keyword, StringComparison.Ordinal))
            {
                continue;
            }

            var negated = false;
            var searchFrom = 0;
            while (inputKey.IndexOf(keyword, searchFrom, StringComparison.Ordinal) is { } hit && hit >= 0)
            {
                var windowStart = Math.Max(0, hit - 6);
                var window = inputKey[windowStart..hit];
                if (window.Contains('未') || window.Contains('不') || window.Contains('无'))
                {
                    negated = true;
                    break;
                }

                searchFrom = hit + 1;
            }

            if (!negated)
            {
                return true;
            }
        }

        return false;
    }

    private static NameLookupTitleChannelsInfo ChannelsInfo(NameLookupTitleChannels channels)
        => new(channels.IcdTitleDirect, channels.Cm3TitleDirect, channels.IcdTitleReverse);

    /// <summary>
    /// 主导词按词面合并（同名多 0 级行只展示一条）：首个（排序最优）条目为准，
    /// 后续命中只并入修饰词链路（pathKey+索引码去重，避免同链路重复展示）。
    /// </summary>
    private static void AppendLeadTerm(
        List<NameLookupLeadTerm> leadTerms,
        int seqNo,
        string term,
        string hitAlias,
        int coveredChars,
        int position,
        bool tailHit,
        IReadOnlyList<NameLookupModifierPath> paths,
        string? redirectKey = null)
    {
        var existing = leadTerms.FirstOrDefault(item => item.Term == term);
        if (existing is null)
        {
            // displayTerm 与 term 相同（无同义族变换）时置空，前端回落显示 term。
            var display = DisplayTermOf(term);
            leadTerms.Add(new NameLookupLeadTerm(seqNo, term, hitAlias, coveredChars, position, tailHit, paths, redirectKey, display == term ? null : display));
            return;
        }

        if (paths.Count == 0)
        {
            return;
        }

        var merged = existing.ModifierPaths
            .Concat(paths)
            .GroupBy(path => path.PathKey + "|" + (path.IndexCode ?? string.Empty))
            .Select(group => group.First())
            .ToList();
        leadTerms[leadTerms.IndexOf(existing)] = existing with
        {
            ModifierPaths = merged,
            RedirectKey = existing.RedirectKey ?? redirectKey,
        };
    }

    /// <summary>
    /// 证据链重叠度 = 链路词条面覆盖的输入检索核字符占比（去 ASCII 字母数字，
    /// 口径同 Cm3ProcedureIndexResolver.PathOverlapRatio 的诊断侧移植）。
    /// </summary>
    private static decimal PathOverlapRatio(IReadOnlyList<IcdIndexNode> path, string inputKey)
    {
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
            foreach (var variant in new[]
            {
                node.Term,
                IcdIndexSnapshotBuilder.ParenthesisRegex.Replace(node.Term, string.Empty),
            })
            {
                covered.UnionWith(TextMatch.NormalizeKey(variant).Where(inputCharacters.Contains));
            }
        }

        return covered.Count / (decimal)inputCharacters.Count;
    }

    /// <summary>
    /// 索引候选分 = 展开器原始分 × 回退惩罚（回退码与祖先兜底码让位，不抢 Top1）。
    /// 刻意不做加成乘法：0.45 与 1.0 的区分度会被 leadBoost×depthBoost 推过 1.0 钳位抹平，
    /// 导致短泛码按编码长度反超精确命中的整标题码（阿米巴膀胱炎 实测踩坑）；
    /// 加成信息改由 Assemble 的平手裁决策（主导词覆盖/链路深度）承担。
    /// </summary>
    private static decimal Score(decimal score, bool isIndexFallback)
        => Math.Min(1m, score * (isIndexFallback ? 0.8m : 1m));

    private static decimal Score(ExpandedItem item)
        => Score(item.Score, item.IsIndexFallback);

    /// <summary>推荐候选的中间聚合（跨主导词去重前）。WholeTitleDepth/TitleSpecificity
    /// 供平手裁决策（对齐 IndexCodeExpander.Rank 的整标题贴合优先口径）。</summary>
    private sealed record RecommendationAccumulator(
        string CodeSystemCode,
        string Code,
        string Title,
        decimal Score,
        string LeadTerm,
        IReadOnlyList<string> Path,
        bool IsIndexFallback,
        string? Note,
        decimal WholeTitleDepth = 0m,
        decimal TitleSpecificity = 0m,
        int LeadCoveredChars = 0,
        int PathCount = 0,
        bool IsSeeAlsoHint = false);
}
