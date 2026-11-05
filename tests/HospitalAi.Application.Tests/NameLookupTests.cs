using HospitalAi.Application.Coding.Cm3Index;
using HospitalAi.Application.Coding.IcdIndex;
using HospitalAi.Application.Coding.NameLookup;

namespace HospitalAi.Application.Tests;

/// <summary>
/// "编码录入"名称解析单测：主导词全量匹配拆分（黄金用例：中心细胞性淋巴瘤 →
/// 中心/细胞(性)/淋巴瘤/瘤，不含 丘疹病/淋巴瘤样/性）与引擎端到端（拆分 → 下钻 → 展开 → 有效码）。
/// 夹具为按真实索引惯例构造的合成行。
/// </summary>
public sealed class NameLookupTests
{
    private static IcdIndexRow LeadRow(int seqNo, string name, string? code = null, int status = 0) =>
        new(seqNo, name, code, 0, 0, status);

    private static IcdIndexRow ChildRow(
        int seqNo, int parentSeqNo, string name, string? code = null, int level = 1) =>
        new(seqNo, name, code, level, parentSeqNo, 0);

    private static IcdDictionaryRow Dict(string codeSystem, string code, string title, string? searchText = null) =>
        new(codeSystem, code, title, searchText ?? title);

    /// <summary>
    /// 需求黄金用例夹具：除 4 个期望命中的主导词外，还布置了三类"必须排除"的行——
    /// 丘疹病（词面无交集）、淋巴瘤样（样 字超出输入，部分匹配）、性（单字主导词不在结尾）、
    /// 施莫尔病或中心（或 尾段 锚定被禁用的行）。
    /// </summary>
    private static IcdIndexSnapshot LymphomaSnapshot() => IcdIndexSnapshotBuilder.Build([
        LeadRow(1, "中心", "D48.0"),
        LeadRow(2, "细胞(性)", "M8010/3"),
        LeadRow(3, "淋巴瘤", "C85.9"),
        LeadRow(4, "瘤", "M8000/1"),
        LeadRow(5, "丘疹病", "L44.8"),
        LeadRow(6, "淋巴瘤样", "L44.8"),
        LeadRow(7, "性"),
        LeadRow(8, "施莫尔病或中心", "M51.4"),
    ]);

    [Fact]
    public void 黄金用例_中心细胞性淋巴瘤_命中四个全量匹配主导词()
    {
        var hits = NameLeadTermSplitter.SplitIcd(LymphomaSnapshot(), "中心细胞性淋巴瘤");

        // 排序口径：覆盖字数 ↓ → 尾部命中优先 → 位置靠后优先 → 权重 ↓ → SeqNo 稳定序。
        Assert.Equal(
            new[] { "淋巴瘤", "细胞(性)", "中心", "瘤" },
            hits.Select(hit => hit.Term).ToArray());
        Assert.All(hits, hit => Assert.True(hit.CoveredChars > 0));
    }

    [Fact]
    public void 黄金用例_范围外与部分匹配主导词不召回()
    {
        var hits = NameLeadTermSplitter.SplitIcd(LymphomaSnapshot(), "中心细胞性淋巴瘤");
        var terms = hits.Select(hit => hit.Term).ToList();

        Assert.DoesNotContain("丘疹病", terms);   // 词面无交集
        Assert.DoesNotContain("淋巴瘤样", terms); // 样 字超出输入，部分匹配
        Assert.DoesNotContain("性", terms);       // 单字主导词不在结尾（尾重心门控）
        // "施莫尔病或中心" 的 中心 是 或 尾段（RecallAnchor=false），只展示正牌主导词 中心。
        Assert.DoesNotContain("施莫尔病或中心", terms);
    }

    [Fact]
    public void 黄金用例_括号主导词展示原始词面_命中别名取归一键()
    {
        var hits = NameLeadTermSplitter.SplitIcd(LymphomaSnapshot(), "中心细胞性淋巴瘤");

        var cell = hits.Single(hit => hit.Term == "细胞(性)");
        Assert.Equal("细胞性", cell.HitAlias); // 泛化修饰词 细胞 被拦，命中的是完整别名 细胞性
        Assert.False(cell.TailHit);
        Assert.Equal(2, cell.Position);

        var tumor = hits.Single(hit => hit.Term == "瘤");
        Assert.True(tumor.TailHit); // 单字主导词按尾重心放行
        Assert.Equal(7, tumor.Position);
    }

    [Fact]
    public void 诊断引擎端到端_拆分下钻展开_字典有效码置顶()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(3, "淋巴瘤", "C85.9"),
            LeadRow(5, "丘疹病", "L44.8"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "C85.900", "中心细胞性淋巴瘤"),
            Dict("ICD-10", "L44.800", "丘疹病"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "中心细胞性淋巴瘤", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null);

        Assert.Empty(response.DegradedFlags);
        var lead = Assert.Single(response.LeadTerms);
        Assert.Equal("淋巴瘤", lead.Term);
        var path = Assert.Single(lead.ModifierPaths);
        Assert.Equal("C85.9", path.IndexCode);
        Assert.Contains(path.Codes, code => code.Code == "C85.900" && !code.IsIndexFallback);

        // 字典标题直达（标题与输入逐字相等）置顶；索引链路同码被去重。
        Assert.NotEmpty(response.Recommendations);
        Assert.Equal("C85.900", response.Recommendations[0].Code);
        Assert.True(response.Recommendations[0].DictionaryDirect);
        Assert.All(response.Recommendations, item =>
            Assert.Contains(item.Code, new[] { "C85.900" }));
    }

    [Fact]
    public void 诊断引擎_字典展不开_回退码显式标注()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([LeadRow(3, "淋巴瘤", "C85.9")]);
        // 字典里没有 C85.9 前缀族：展开落空 → 回退推荐索引编码本身并显式标注。
        var dictionary = new IcdDictionarySnapshot([Dict("ICD-10", "L44.800", "丘疹病")]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "中心细胞性淋巴瘤", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null);

        Assert.NotEmpty(response.Recommendations);
        var fallback = Assert.Single(response.Recommendations);
        Assert.Equal("C85.9", fallback.Code);
        Assert.True(fallback.IsIndexFallback);
    }

    [Fact]
    public void 手术引擎端到端_主导词下修饰词下钻_展开细码()
    {
        var snapshot = Cm3IndexSnapshotBuilder.Build([
            new Cm3IndexRow(201, "切除术", null, 0, 0, 0),
            new Cm3IndexRow(202, "－阑尾", "47.0", 1, 201, 0),
            new Cm3IndexRow(210, "修补术", "53.0", 0, 0, 0),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-9-CM-3", "47.0901", "阑尾切除术"),
            Dict("ICD-9-CM-3", "53.000", "腹股沟疝修补术"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.ProcedureKind, "阑尾切除术", topK: 8,
            icdSnapshot: null, tumorSnapshot: null, icdDictionary: null,
            cm3Snapshot: snapshot, cm3Dictionary: dictionary);

        Assert.Empty(response.DegradedFlags);
        var lead = Assert.Single(response.LeadTerms);
        Assert.Equal("切除术", lead.Term);
        var path = Assert.Single(lead.ModifierPaths);
        Assert.Equal(new[] { "切除术", "阑尾" }, path.Path);
        Assert.Equal("47.0", path.IndexCode);
        Assert.Contains(path.Codes, code => code.Code == "47.0901" && !code.IsIndexFallback);

        // "修补术" 不是 阑尾切除术 的全量匹配子串，不得出现；推荐码全部落在字典里。
        Assert.All(response.LeadTerms, item => Assert.NotEqual("修补术", item.Term));
        Assert.NotEmpty(response.Recommendations);
        Assert.Equal("47.0901", response.Recommendations[0].Code);
        Assert.All(response.Recommendations, item =>
            Assert.Contains(item.Code, new[] { "47.0901", "53.000" }));
    }

    [Fact]
    public void 手术拆分_惯例泛词与不在输入中的主导词不命中()
    {
        var snapshot = Cm3IndexSnapshotBuilder.Build([
            new Cm3IndexRow(201, "切除术", null, 0, 0, 0),
            new Cm3IndexRow(211, "按部位", null, 0, 0, 0),
            new Cm3IndexRow(212, "吻合术", null, 0, 0, 0),
        ]);

        var hits = NameLeadTermSplitter.SplitCm3(snapshot, "阑尾切除术");

        var lead = Assert.Single(hits);
        Assert.Equal("切除术", lead.Term);
        Assert.True(lead.TailHit);
    }

    [Fact]
    public void 黄金用例_完整索引路径输入_纯连接词别名不作主导词锚()
    {
        // 真库形态：霍乱 A00.9 下挂 －由于O1群霍乱弧菌…；0 级行中还存在
        // "由于或与躯体状况有关…发作"(F06.9，或 切分后首段=由于) 与
        // "密螺旋体病，由于"(逗号尾段别名=由于) 两条可被 由于 锚上的行。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "霍乱(亚洲)(流行性)(恶性)", "A00.9"),
            ChildRow(2, 1, "－由于O1群霍乱弧菌,霍乱生物型所致", "A00.0"),
            LeadRow(3, "由于或与躯体状况有关而引起的精神病发作", "F06.9"),
            LeadRow(4, "密螺旋体病，由于", "A53.9"),
        ]);

        var hits = NameLeadTermSplitter.SplitIcd(snapshot, "霍乱由于o1群霍乱弧菌霍乱生物型所致");

        // 全量匹配主导词只有 霍乱：由于 是其修饰链的接续符，两条含 由于 的行不得显示。
        var lead = Assert.Single(hits);
        Assert.Equal("霍乱(亚洲)(流行性)(恶性)", lead.Term);
        Assert.DoesNotContain(hits, hit => hit.Term.Contains("由于"));
    }

    [Fact]
    public void 黄金用例_分型后缀收尾_不作主导词锚()
    {
        // 真库形态：霍乱中型 的 型 是分型后缀（索引里以 "弗雷德里克森高脂蛋白血症，型"
        // 逗号尾段别名存在），收尾也不得锚——只应命中 霍乱。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "霍乱(亚洲)(流行性)(恶性)", "A00.9"),
            LeadRow(5, "弗雷德里克森高脂蛋白血症，型"),
            LeadRow(6, "瘘", "K60.2"),
        ]);

        var hits = NameLeadTermSplitter.SplitIcd(snapshot, "霍乱中型");

        var lead = Assert.Single(hits);
        Assert.Equal("霍乱(亚洲)(流行性)(恶性)", lead.Term);
        Assert.DoesNotContain(hits, hit => hit.Term.Contains("型"));
        // 对照：真核心名词单字主导词（瘘）在收尾时仍正常放行（回归护栏）。
        var fistula = NameLeadTermSplitter.SplitIcd(snapshot, "肛瘘");
        Assert.Single(fistula);
        Assert.Equal("瘘", fistula[0].Term);
    }

    [Fact]
    public void 黄金用例_括号派生单字别名不作主导词锚()
    {
        // 真库形态：伤寒杆菌败血症 应只锚 伤寒 + 败血症；瘙痒[症] 的方括号单字变体
        // 症（权重 0.85，非行名）虽在输入尾部也不得锚。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "伤寒(顿挫型)(逍遥型)", "A01.0"),
            LeadRow(2, "败血症(性)(全身性)(化脓性)", "A41.9"),
            LeadRow(3, "瘙痒[症]", "R20.2"),
            LeadRow(4, "瘘", "K60.2"),
        ]);

        var hits = NameLeadTermSplitter.SplitIcd(snapshot, "伤寒杆菌败血症");

        Assert.Equal(2, hits.Count);
        Assert.DoesNotContain(hits, hit => hit.Term.Contains("瘙痒"));
        // 对照：行名本名（权重 1.0）的单字主导词收尾仍正常放行（回归护栏）。
        var fistula = NameLeadTermSplitter.SplitIcd(snapshot, "肛瘘");
        Assert.Single(fistula);
        Assert.Equal("瘘", fistula[0].Term);
    }

    [Fact]
    public void 修饰词核心词核对_括号枚举借道子行被拦_核心词命中子行保留()
    {
        // 真库形态：败血症 0 级行下挂 －沙门菌性(亚利桑那)(猪霍乱)(小肠炎)(鼠伤寒) A02.1——
        // 其变体词 鼠伤寒 含 伤寒 二字，会对 伤寒杆菌败血症 挣到假增益；核心词 沙门菌性
        // 与输入零交集，整条链路必须被拦。伤寒 主导词链路不受影响。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "伤寒(顿挫型)(逍遥型)", "A01.0"),
            LeadRow(2, "败血症(性)(全身性)(化脓性)", "A41.9"),
            ChildRow(3, 2, "－沙门菌性(亚利桑那)(猪霍乱)(小肠炎)(鼠伤寒)", "A02.1"),
            ChildRow(4, 2, "－伤寒杆菌性", "A01.0"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A01.000X002", "伤寒杆菌败血症"),
            Dict("ICD-10", "A02.100", "沙门菌脓毒症"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "伤寒杆菌败血症", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null);

        var salmonella = response.Recommendations.Where(item => item.Code.StartsWith("A02.1", StringComparison.Ordinal)).ToList();
        Assert.Empty(salmonella);

        // 正牌链路：伤寒 主导词 + 败血症→伤寒杆菌性（核心词 ⊂ 输入）都应产出。
        Assert.Contains(response.Recommendations, item => item.Code == "A01.000X002");
        var bacteremiaLead = response.LeadTerms.Single(item => item.Term.StartsWith("败血症", StringComparison.Ordinal));
        Assert.All(bacteremiaLead.ModifierPaths.SelectMany(path => path.Codes),
            code => Assert.False(code.Code.StartsWith("A02.1", StringComparison.Ordinal)));
    }

    [Fact]
    public void 黄金用例_部位逗号尾段别名不作主导词锚()
    {
        // 真库形态：阿米巴膀胱炎 应只锚 阿米巴/膀胱炎/炎(炎症族)；"小梁形成，膀胱" 的
        // 逗号尾段别名 膀胱（索引 "主导词，部位" 惯例的部位段）不是核心词，不得作锚。
        // 阿米巴 是转示行（阿米巴 - 见 阿米巴病），应锚到转示目标 阿米巴病 并回显转示键。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "阿米巴 - 见 阿米巴病", status: 1),
            LeadRow(2, "阿米巴病", "A06.9"),
            LeadRow(3, "膀胱炎(性)", "N30.9"),
            LeadRow(4, "炎，炎症(性)，发炎(伴有渗出)(另见 感染)"),
            LeadRow(5, "小梁形成，膀胱"),
        ]);

        var hits = NameLeadTermSplitter.SplitIcd(snapshot, "阿米巴膀胱炎");

        var terms = hits.Select(hit => hit.Term).ToList();
        Assert.DoesNotContain("小梁形成，膀胱", terms);
        Assert.Contains("阿米巴病", terms);
        var amoeba = hits.Single(hit => hit.Term == "阿米巴病");
        Assert.Equal("阿米巴", amoeba.RedirectKey);
        Assert.Contains("膀胱炎(性)", terms);
        Assert.Contains("炎，炎症(性)，发炎(伴有渗出)(另见 感染)", terms);
    }

    [Fact]
    public void 黄金用例_短别名中段命中不作主导词锚()
    {
        // 真库形态：肠毒性大肠杆菌感染 应只锚 大肠杆菌(B96.2 行)/感染(性)(机会性)；
        // 毒性(中毒) 靠 2 字主别名在 肠毒性 中段锚上、"尿道结石病(伴有绞痛或感染)" 靠
        // 括注切出的幽灵别名 感染 锚上，都不是这条的主导词。
        // 需求口径：≤2 字别名只认首/尾位置（中心细胞性淋巴瘤 不列中段的 淋巴 同理）。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "感染(性)(机会性)(另见 炎症)", "B99"),
            ChildRow(2, 1, "－大肠杆菌 NEC", "A49.8"),
            LeadRow(3, "毒性(中毒)", "T65.9"),
            LeadRow(4, "尿道结石病(伴有绞痛或感染)", "N20.9"),
            LeadRow(5, "大肠杆菌，作为分类于他处疾病的原因", "B96.2"),
        ]);

        // 别名层断言：括号内的 或 仍切段（校准口径，"NEC(胎儿或婴儿)" 的 婴儿 是合法锚），
        // 但幽灵别名 感染 带 ParenTail 标记，编码录入拆分会排除它。
        var stoneAliases = IcdIndexSnapshotBuilder.ParseAliases("尿道结石病(伴有绞痛或感染)");
        var spliced = stoneAliases.Single(alias => alias.Key == "感染");
        Assert.True(spliced.ParenTail);

        var hits = NameLeadTermSplitter.SplitIcd(snapshot, "肠毒性大肠杆菌感染");
        var terms = hits.Select(hit => hit.Term).ToList();
        Assert.DoesNotContain("毒性(中毒)", terms);
        Assert.DoesNotContain("尿道结石病(伴有绞痛或感染)", terms);
        // B96.2 是次因编码导语行（"作为分类于他处疾病的原因"）：本源感染输入不得锚上。
        Assert.DoesNotContain("大肠杆菌，作为分类于他处疾病的原因", terms);
        Assert.Contains("感染(性)(机会性)(另见 炎症)", terms);
    }

    [Fact]
    public void 黄金用例_逗号修饰段未声明不作主导词锚()
    {
        // 真库形态：伤寒并发肠出血 应锚 伤寒 + 肠出血/出血(性) 泛行；
        // "出血，经间期 NEC(规则)" 的修饰段 经间期 未在输入声明（经间期出血是妇科词），
        // 头段 出血 沾边不得作锚——同类：出血、凝血或凝血酶原时间延长 同拦。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "伤寒(顿挫型)(逍遥型)", "A01.0"),
            LeadRow(2, "出血，经间期 NEC(规则)", "N93.0"),
            LeadRow(3, "出血、凝血或凝血酶原时间延长(另见 缺陷，凝血)"),
            LeadRow(4, "肠出血", "K92.2"),
        ]);

        var hits = NameLeadTermSplitter.SplitIcd(snapshot, "伤寒并发肠出血");
        var terms = hits.Select(hit => hit.Term).ToList();

        Assert.DoesNotContain("出血，经间期 NEC(规则)", terms);
        Assert.DoesNotContain("出血、凝血或凝血酶原时间延长(另见 缺陷，凝血)", terms);
        Assert.Contains("伤寒(顿挫型)(逍遥型)", terms);
        Assert.Contains("肠出血", terms);
    }

    [Fact]
    public void 黄金用例_同义族逗号行显示头段词面()
    {
        // 真库形态：肺梗死 直观主导词是 梗死 与 死；"梗死，梗塞" 的尾段 梗塞 与头段同义
        // （共字），chip 应显示头段 梗死（完整行名走 tooltip），单字 死 按尾重心正常锚定。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "梗死，梗塞", "I21.9"),
            LeadRow(2, "死", "R99"),
        ]);

        var hits = NameLeadTermSplitter.SplitIcd(snapshot, "肺梗死");

        Assert.Equal(2, hits.Count);
        var infarct = hits.Single(hit => hit.Term == "梗死，梗塞");
        Assert.Equal("梗死", infarct.Alias.Key); // 命中的是头段别名
        Assert.Equal("梗死，梗塞", infarct.Term);

        // 引擎层：displayTerm 随响应回传（纯展示，term/去重/证据链不变）。
        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "肺梗死", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: null,
            cm3Snapshot: null, cm3Dictionary: null);
        var infarctTerm = response.LeadTerms.Single(item => item.Term == "梗死，梗塞");
        Assert.Equal("梗死", infarctTerm.DisplayTerm);
        Assert.Equal("梗死，梗塞", infarctTerm.Term);
        Assert.Null(response.LeadTerms.Single(item => item.Term == "死").DisplayTerm);
    }

    [Fact]
    public void 黄金用例_贪婪选层被挤掉时按核心词补钻()
    {
        // 真库形态：截瘫(下肢) G82.2 下同时挂 －痉挛性 G82.1 与 －梅毒性(痉挛性) A52.1——
        // 后者全名含 痉挛性 子串，贪婪选层的镜像覆盖会挤掉正牌 －痉挛性，
        // 主链路被核心词核对拦下后，须按核心词逐个子行补钻捞出 G82.1。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "截瘫(下肢)", "G82.2"),
            ChildRow(2, 1, "－痉挛性", "G82.1"),
            ChildRow(3, 1, "－梅毒性(痉挛性)", "A52.1"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "G82.100", "痉挛性截瘫"),
            Dict("ICD-10", "A52.100", "梅毒性痉挛性截瘫"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "急性完全性痉挛性截瘫", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null);

        // A52.1（梅毒性）不得产出；G82.1 修饰词链路必须补到。
        Assert.DoesNotContain(response.Recommendations, item => item.Code.StartsWith("A52.1", StringComparison.Ordinal));
        var paraplegia = response.LeadTerms.Single(item => item.Term == "截瘫(下肢)");
        var spastic = paraplegia.ModifierPaths.SelectMany(path => path.Codes)
            .Where(code => code.Code.StartsWith("G82.1", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(spastic);
        Assert.All(spastic, code => Assert.Contains("痉挛性", code.Title));
    }

    [Fact]
    public void 黄金用例_结核性肺炎的主导词()
    {
        // 真库形态：结核性肺炎,细胞学(组织学)(-) 应锚 结核(病) + 肺炎(性)——
        // 肺炎 处于中段，但其后随 细胞学 非命中跨度，不属于中段碎片，须保留。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "结核(病)", "A16.0"),
            LeadRow(2, "肺炎(性)", "J18.1"),
            LeadRow(3, "细胞(性)"),
        ]);

        var hits = NameLeadTermSplitter.SplitIcd(snapshot, "结核性肺炎细胞学组织学");
        var terms = hits.Select(hit => hit.Term).ToList();

        Assert.Contains("结核(病)", terms);
        Assert.Contains("肺炎(性)", terms);
        // 细胞(性) 的 3 字别名 细胞性 也在输入中段——行是否展示由其自身词面决定，
        // 但 肺炎/结核 两个正牌主导词必须在列。
    }

    [Fact]
    public void 黄金用例_因果连接行穿透补钻()
    {
        // 真库形态：脑膜炎(基底)(大脑)(脊髓) G03.9 下挂 －见于(由于)（连接行）→ －－伤寒 A01.0†G01*，
        // 以及一堆病原兄弟行。伤寒并发脑膜炎 的贪婪选层被 创伤性(损伤的并发症)（单字散落沾 并/发）
        // 挤掉，主链路只剩回退码 → 须穿透连接行补钻捞出 伤寒 因果链（A01.0 系）。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "脑膜炎(基底)(大脑)(脊髓)", "G03.9"),
            ChildRow(2, 1, "－见于(由于)"),
            ChildRow(3, 2, "－伤寒", "A01.0† G01*"),
            ChildRow(4, 1, "－脑膜炎球菌性", "A39.0† G01*"),
            ChildRow(5, 1, "－创伤性(损伤的并发症)", "T79.8"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A01.000X003+G01*", "伤寒并发脑膜炎"),
            Dict("ICD-10", "T79.800", "创伤性休克"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "伤寒并发脑膜炎", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        var typhoid = response.Recommendations
            .Where(item => item.Code.StartsWith("A01.0", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(typhoid);
        // 展示链路严格按索引树：脑膜炎(基底)(大脑)(脊髓) → 见于(由于) → 伤寒（连接行如实显示）。
        Assert.Contains(typhoid, item => item.Path.Count == 3
            && item.Path[0].StartsWith("脑膜炎", StringComparison.Ordinal)
            && item.Path[1] == "见于(由于)"
            && item.Path[2] == "伤寒");
        // 回退码（T79.800）不得压过真实链路码。
        var typhoidIndex = response.Recommendations
            .ToList()
            .FindIndex(item => item.Code.StartsWith("A01.0", StringComparison.Ordinal));
        var traumaIndex = response.Recommendations
            .ToList()
            .FindIndex(item => item.Code.StartsWith("T79.8", StringComparison.Ordinal));
        var dump = string.Join(" | ", response.Recommendations.Select(item => $"{item.Code}({item.Score})"));
        Assert.True(traumaIndex < 0 || typhoidIndex < traumaIndex,
            $"回退码不应排在真实链路码之前：[{dump}]");
    }

    [Fact]
    public void 黄金用例_补钻子行核心词须整体入文()
    {
        // 真库形态：伤寒杆菌败血症 下补钻 败血症(性) 的子行——
        // 大肠杆菌/流感嗜血杆菌/巴斯德菌血症 靠通用词根 杆菌/血症 沾边（ModifierNodeMatchesInput 放行），
        // 但它们的核心词都不整体出现在输入里；只有 伤寒杆菌性→伤寒杆菌 ⊂ 输入 是真修饰词。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "败血症(性)(全身性)(化脓性)", "A41.9"),
            ChildRow(2, 1, "－伤寒杆菌性", "A01.0"),
            ChildRow(3, 1, "－大肠杆菌", "A41.5"),
            ChildRow(4, 1, "－流感嗜血杆菌", "A41.3"),
            ChildRow(5, 1, "－芽胞杆菌败血症", "A22.7"),
            // 真库形态：带码因果死路行（无子行、自身就是码）——输注病因与输入无关，不得入链。
            ChildRow(6, 1, "－由于输注、治疗性注射或输血引起的", "T80.2"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A01.000X002", "伤寒杆菌败血症"),
            Dict("ICD-10", "A41.500X006", "大肠杆菌败血症"),
            Dict("ICD-10", "T80.200X002", "输注、输血和治疗性注射后的感染"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "伤寒杆菌败血症", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        Assert.DoesNotContain(response.Recommendations, item => item.Code.StartsWith("A41.", StringComparison.Ordinal));
        Assert.DoesNotContain(response.Recommendations, item => item.Code.StartsWith("A22.", StringComparison.Ordinal));
        Assert.DoesNotContain(response.Recommendations, item => item.Code.StartsWith("T80.2", StringComparison.Ordinal));
        Assert.Contains(response.Recommendations, item => item.Code.StartsWith("A01.0", StringComparison.Ordinal));
        var bacteremia = response.LeadTerms.Single(item => item.Term.StartsWith("败血症", StringComparison.Ordinal));
        Assert.All(bacteremia.ModifierPaths.SelectMany(path => path.Codes),
            code => Assert.False(code.Code.StartsWith("A41.", StringComparison.Ordinal)));
    }

    [Fact]
    public void 黄金用例_埃尔托生物型霍乱_主链路不受补钻收紧影响()
    {
        // 回归护栏：伤寒杆菌败血症 的补钻收紧（核心词入文）不得误伤埃尔托链路——
        // 主链路 霍乱 → 由于01群霍乱弧菌 → 埃尔托生物型 是 Drill 自产（非补钻），
        // 应照常产出 A00.100X001（非回退码）。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "霍乱(亚洲)(流行性)(恶性)", "A00.9"),
            ChildRow(2, 1, "－由于01群霍乱弧菌", "A00.9"),
            ChildRow(3, 2, "－埃尔托生物型", "A00.1"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A00.100X001", "埃尔托生物型霍乱"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "埃尔托生物型霍乱", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        Assert.Contains(response.Recommendations, item => item.Code == "A00.100X001" && !item.IsIndexFallback);
    }

    [Fact]
    public void 黄金用例_鼠伤寒沙门菌感染_枚举字面不得延伸下钻()
    {
        // 真库形态：感染(性) → 沙门菌(亚利桑那)(猪霍乱)(小肠炎)(鼠伤寒) A02.9 → －伤寒 A01.0。
        // 输入 鼠伤寒沙门菌感染 的链路到 A02.9 即应结束：伤寒 是被 鼠伤寒 枚举字面
        // （伤寒 二字已被占）借道延伸的下钻，产出 A01.000 属错码。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "感染(性)(机会性)(另见 炎症)", "B99"),
            ChildRow(2, 1, "－沙门菌(亚利桑那)(猪霍乱)(小肠炎)(鼠伤寒)", "A02.9"),
            ChildRow(3, 2, "－伤寒", "A01.0"),
            // 真库还有 伤寒(顿挫型) 0 级行：鼠伤寒 的 伤寒 是中段 2 字命中，
            // 且前一字符 鼠 是复合名前缀（鼠伤寒=血清型名），该行不得入主导词。
            LeadRow(4, "伤寒(顿挫型)(逍遥型)(任何部位)(热)(出血性)(感染)(间歇性)(恶性)(风湿性)", "A01.0"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A02.900", "沙门菌感染"),
            Dict("ICD-10", "A02.900X002", "鼠伤寒沙门菌感染"),
            Dict("ICD-10", "A01.000", "伤寒"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "鼠伤寒沙门菌感染", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        // A01.0 不得出现在任何推荐或链路码里；A02.9 链路必须产出。
        var a01 = response.Recommendations.Where(item => item.Code.StartsWith("A01.0", StringComparison.Ordinal)).ToList();
        var dump = string.Join(" | ", response.LeadTerms.Select(lead => lead.Term
            + " [" + string.Join("; ", lead.ModifierPaths.Select(path =>
                string.Join("→", path.Path) + "=" + string.Join(",", path.Codes.Select(code => code.Code + (code.IsIndexFallback ? "(fb)" : ""))))) + "]"));
        var recDump = string.Join("; ", response.Recommendations.Select(item => item.Code + "(" + item.Note + ")"));
        Assert.True(a01.Count == 0, $"A01.0 泄漏调试: recs=[{recDump}] leads=[{dump}]");
        var a02Dump = string.Join(" | ", response.LeadTerms.Select(lead => lead.Term
            + " [" + string.Join("; ", lead.ModifierPaths.Select(path =>
                string.Join("→", path.Path) + " codes=" + path.Codes.Count + " idx=" + path.IndexCode)) + "]"));
        Assert.True(response.Recommendations.Any(item => item.Code.StartsWith("A02.9", StringComparison.Ordinal)),
            $"A02.9 缺失调试: leads=[{dump}] a02Paths=[{a02Dump}]");
        Assert.All(response.LeadTerms.SelectMany(lead => lead.ModifierPaths)
            .SelectMany(path => path.Codes),
            code => Assert.False(code.Code.StartsWith("A01.0", StringComparison.Ordinal)));
    }

    [Fact]
    public void 黄金用例_肠炎转示行_锚到小肠炎()
    {
        // 真库形态：肠炎 - 见 小肠炎（0 级失效主导词，seq 4993）——
        // 查 肠炎 必须转 小肠炎(A09.9)，在其修饰词树下钻。毗邻单胞菌肠炎 等病原性肠炎
        // 的链路终点是 小肠炎 → 细菌/由于… A04.8/A04.9 族。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "肠炎 - 见 小肠炎", status: 1),
            LeadRow(2, "小肠炎 (腹泻) (出血性)", "A09.9"),
            ChildRow(3, 2, "－细菌性", "A04.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A04.900X001", "吡邻单胞菌肠炎"),
            Dict("ICD-10", "A04.900", "细菌性肠炎"),
        ]);

        // 转示键进 Redirects：肠炎 → 小肠炎。
        Assert.True(snapshot.Redirects.ContainsKey("肠炎"), "肠炎 转示键未进 Redirects");

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "毗邻单胞菌肠炎", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        // 链路：肠炎(转示) → 小肠炎，取转示目标自身码 A09.9（查典语义：转示后无词面匹配修饰词则用其默认码）。
        Assert.Contains(response.Recommendations, item => item.Code == "A09.9");
        var enteritis = response.LeadTerms.SingleOrDefault(item => item.Term == "小肠炎 (腹泻) (出血性)");
        Assert.NotNull(enteritis);
        Assert.Equal("肠炎", enteritis.RedirectKey);
    }

    [Fact]
    public void 黄金用例_逗号附加语主段零交集不入链()
    {
        // 真库形态：感染(性) 下挂 －δ因子(急性)，于乙型肝炎病毒携带者 B17.0——
        // 病毒性肝炎重叠感染 与该行的共同字面只有附加语里的 乙型肝炎 二字，
        // 主段 δ因子 与输入零交集，整行不得入链（文字覆盖率门）。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "感染(性)(机会性)(另见 炎症)", "B99"),
            ChildRow(2, 1, "－δ因子(急性)，于乙型肝炎病毒携带者", "B17.0"),
            ChildRow(3, 1, "－病毒性", "B34.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "B34.900", "病毒性感染"),
            Dict("ICD-10", "B17.000", "慢性乙型肝炎的急性δ因子(超级)感染"),
            Dict("ICD-10", "B17.001", "急性δ因子感染"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "病毒性肝炎重叠感染", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        // 链路层（修饰词路径）必须无 B17.0：δ因子 行被逗号附加语门控拦在准入层。
        Assert.All(response.LeadTerms.SelectMany(lead => lead.ModifierPaths)
            .SelectMany(path => path.Codes),
            code => Assert.False(code.Code.StartsWith("B17.0", StringComparison.Ordinal)));
        Assert.All(response.LeadTerms.SelectMany(lead => lead.ModifierPaths),
            path => Assert.DoesNotContain("δ因子", string.Join("→", path.Path), StringComparison.Ordinal));
        // 展开器回退码（若有）按既有口径殿后，不抢 Top1。
        if (response.Recommendations.Any(item => item.Code.StartsWith("B17.0", StringComparison.Ordinal)))
        {
            Assert.True(response.Recommendations[0].Code.StartsWith("B34.9", StringComparison.Ordinal),
                "回退码不应排在真实链路码之前");
        }
    }

    [Fact]
    public void 黄金用例_排除句式行与输入证实声明冲突不入链()
    {
        // 真库形态：结核(病) → 肺(空洞性)… → 不伴有提及细菌学或组织学证实 A16.2（未证实分支）。
        // 输入 肺结核(显微镜检证实) 逐字写了 证实（已证实方向），与未证实分支矛盾 → 该行不入链；
        // 肺 层（A16.2 行自身带码）仍作为链路终点交付。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "结核(病)，结核性(干酪样)(变性)(坏疽)(坏死)", "A16.9"),
            ChildRow(2, 1, "－肺(空洞性)(纤维变性)(浸润性)(结节性)", "A16.2"),
            ChildRow(3, 2, "－－不伴有提及细菌学或组织学证实", "A16.2"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A16.200X002", "肺结核"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "肺结核(显微镜检证实)", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        // 链路到 肺 层为止（A16.2 可产出），排除行 不伴有提及…证实 不得出现在路径或码里。
        Assert.All(response.LeadTerms.SelectMany(lead => lead.ModifierPaths),
            path => Assert.DoesNotContain("不伴有", string.Join("→", path.Path), StringComparison.Ordinal));
        Assert.Contains(response.Recommendations, item => item.Code.StartsWith("A16.2", StringComparison.Ordinal));
        // 输入写 未证实 时排除行与之一致，应正常入链（反例护栏）。
        var response2 = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "肺结核(未做细菌学证实)", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));
        var dump2 = string.Join(" | ", response2.LeadTerms.Select(lead => lead.Term
            + " [" + string.Join("; ", lead.ModifierPaths.Select(path =>
                string.Join("→", path.Path) + "=" + string.Join(",", path.Codes.Select(code => code.Code)))) + "]"));
        Assert.True(response2.LeadTerms.Any(lead => lead.ModifierPaths
                .SelectMany(path => path.Path).Any(segment => segment.Contains("不伴有", StringComparison.Ordinal))),
            $"未证实反例调试: recs=[{string.Join("; ", response2.Recommendations.Select(item => item.Code))}] leads=[{dump2}]");
    }

    [Fact]
    public void 标题直达开关关闭_字典直达通道停用()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([LeadRow(3, "淋巴瘤", "C85.9")]);
        var dictionary = new IcdDictionarySnapshot([Dict("ICD-10", "C85.900", "中心细胞性淋巴瘤")]);
        var channelsOff = new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "中心细胞性淋巴瘤", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null, channelsOff);

        // 直达通道关闭：无字典直达命中，候选只由索引链路自产（仍走 淋巴瘤 主导词展开）。
        Assert.Empty(response.DictionaryDirects);
        Assert.All(response.Recommendations, item => Assert.False(item.DictionaryDirect));
        Assert.NotEmpty(response.Recommendations);
        // 开关状态随响应回显。
        Assert.False(response.TitleChannels.IcdTitleDirect);
        Assert.True(response.TitleChannels.IcdTitleReverse);

        var cm3Snapshot = Cm3IndexSnapshotBuilder.Build([new Cm3IndexRow(201, "切除术", "47.0", 0, 0, 0)]);
        var cm3Dictionary = new IcdDictionarySnapshot([Dict("ICD-9-CM-3", "47.0901", "阑尾切除术")]);
        var cm3Response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.ProcedureKind, "阑尾切除术", topK: 8,
            icdSnapshot: null, tumorSnapshot: null, icdDictionary: null,
            cm3Snapshot: cm3Snapshot, cm3Dictionary: cm3Dictionary, channelsOff);

        Assert.Empty(cm3Response.DictionaryDirects);
        Assert.All(cm3Response.Recommendations, item => Assert.False(item.DictionaryDirect));
        Assert.False(cm3Response.TitleChannels.Cm3TitleDirect);
    }

    [Fact]
    public void 括号枚举段准入_核心词不在输入时交付本行精确码()
    {
        // 真库形态（seq 11051 → 11537）：感染(性) → －沙门菌(亚利桑那)(猪霍乱)(小肠炎)(鼠伤寒) A02.9。
        // 输入 亚利桑那菌感染 词面没有 沙门菌，核心词核对必然落空；索引把"同一病原的另一种写法"
        // 写成紧邻多组圆括号，按枚举段准入交付 A02.9 的精确展开（改前面板 0 推荐、流水线已出 A02.900X004）。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "感染(性)(机会性)(另见 炎症)", "B99"),
            ChildRow(2, 1, "－沙门菌(亚利桑那)(猪霍乱)(小肠炎)(鼠伤寒)", "A02.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A02.900", "沙门菌感染"),
            Dict("ICD-10", "A02.900X004", "亚利桑那菌感染"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "亚利桑那菌感染", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        Assert.Contains(response.Recommendations, item => item.Code == "A02.900X004" && !item.IsIndexFallback);
    }

    [Fact]
    public void 括号枚举段准入_单组括号澄清注解不作枚举段()
    {
        // 真库形态（麻痹 主导词下 －肛门(括约肌) K62.5）：单组括号是头段部位词 肛门 的澄清注解，
        // 不是"另一写法"。放行后 瞳孔括约肌麻痹 拿 K62.5/K62.8 肛门码、
        // 淋巴结结核 族的正牌 A18.200X006 被 肠系膜(淋巴腺或淋巴结) 顶掉（万名实测 4 名），
        // 故只在 ≥2 个非指针括号组时产枚举段。代价：常染色体隐性(婴儿型) 这类合法等价写法一并挡掉
        // （见 括号枚举段准入_单组括号等价写法为已知代价）。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "麻痹(完全性)(不完全性)", "G83.8"),
            ChildRow(2, 1, "－肛门(括约肌)", "K62.5"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "K62.500", "肛门括约肌损伤"),
            Dict("ICD-10", "H52.601", "瞳孔括约肌麻痹"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "瞳孔括约肌麻痹", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        Assert.DoesNotContain(response.Recommendations, item => item.Code.StartsWith("K62.5", StringComparison.Ordinal));
    }

    [Fact]
    public void 括号枚举段准入_多组括号不交付回退码()
    {
        // 真库形态（狭窄 41468 → 肛门，肛(管)(括约肌) 41573 K62.4）：(管)(括约肌) 是两组括号，
        // 括约肌 逐字入文 → 枚举段准入放行，但交付的是回退码（K62.400 肛门和直肠狭窄），
        // 行身份由输入根本没写的 肛门 决定。实测同型还有 亚利桑那菌肠炎→A02.000、
        // 手术后肝外胆管狭窄→N13.500 输尿管(手术后)。判据落在回退码上而不是括号组数上：
        // 只剔回退码，本行精确展开（亚利桑那菌感染 A02.900X004）不受影响。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "狭窄", "K62.4"),
            ChildRow(2, 1, "－肛门，肛(管)(括约肌)", null),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "K62.400", "肛门和直肠狭窄"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "奥迪括约肌狭窄", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        var dump = string.Join(" | ", response.Recommendations.Select(item => item.Code + "(" + item.Title + ")"));
        Assert.True(
            response.Recommendations.All(item => item.IsIndexFallback is false && item.Code != "K62.400"),
            $"枚举段准入链路交付了回退码: [{dump}]");
    }

    [Fact]
    public void 括号枚举段准入_单组括号等价写法为已知代价()
    {
        // 真库形态（多囊(病) 7837 → 肾 7836 → 常染色体隐性(婴儿型) 7838 Q61.1）。
        // (婴儿型) 确实是 常染色体隐性 的等价写法、输入也逐字写了 婴儿型，但词面上与
        // 肛门(括约肌)、肠系膜(淋巴腺或淋巴结) 这类"头段部位词的澄清注解"不可区分
        // （索引圆括号同时承担两种语义，见技能纪律 5：不要在匹配器里赌括号语义）。
        // 万名实测：按"≥2 组"放行会让 淋巴结结核/腹腔/腮腺/纵隔 四名的正牌码换位，
        // 按"≥1 组"只多救回 婴儿型多囊肾 一名 → 记为已知代价，要治归数据侧补主导词行。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "多囊(病)"),
            ChildRow(2, 1, "－肾", null),
            ChildRow(3, 2, "－－常染色体隐性(婴儿型)", "Q61.1"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "Q61.101", "婴儿型多囊肾"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "婴儿型多囊肾", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        Assert.DoesNotContain(response.Recommendations, item => item.Code == "Q61.101");
    }

    [Fact]
    public void 见一多段目标_转示按索引惯例取首段主导词()
    {
        // 真库形态：10550 "副伤寒 - 见 热，副伤寒"（失效转示行）→ 32216 "热，发热" R50.9 → 32336 "－副伤寒" A01.4。
        // NormalizeKey 剥逗号，整串目标键（热副伤寒）在 LeadByExactKey 里必然查不到 → 转示静默失效：
        // 输入 副伤寒 既不锚 热 也不锚 沙门菌，只落在 伤寒 泛行上（改前只出 A01.000）。
        // 索引 "见 主导词，修饰词，修饰词" 是通行惯例（全库 73 行多段目标），取首段精确键、整串键留作第二档。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "副伤寒 - 见 热，副伤寒", status: 1),
            LeadRow(2, "热，发热", "R50.9"),
            ChildRow(3, 2, "－副伤寒", "A01.4"),
            LeadRow(4, "伤寒(顿挫型)(逍遥型)(任何部位)(热)", "A01.0"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A01.400", "副伤寒"),
            Dict("ICD-10", "A01.000", "伤寒"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "副伤寒", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        Assert.Contains(response.LeadTerms, lead => lead.Term == "热，发热" && lead.RedirectKey == "副伤寒");
        Assert.Contains(response.Recommendations, item => item.Code == "A01.400" && !item.IsIndexFallback);
    }

    [Fact]
    public void 见一多段目标_沙门菌转示到感染()
    {
        // 需求黄金用例：输入 沙门菌 改前 0 主导词 0 推荐。真库 33811 "沙门菌[又译名 沙门氏菌] - 见 感染，沙门菌"
        // 是指针行（无码、不进 Leads），正牌入口是 11051 感染(性) → 11537 －沙门菌(亚利桑那)(猪霍乱)(小肠炎)(鼠伤寒) A02.9。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "沙门菌[又译名 沙门氏菌] - 见 感染，沙门菌", status: 1),
            LeadRow(2, "感染(性)(机会性)(另见 炎症)", "B99"),
            ChildRow(3, 2, "－沙门菌(亚利桑那)(猪霍乱)(小肠炎)(鼠伤寒)", "A02.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A02.900", "沙门菌感染"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "沙门菌", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        Assert.Contains(response.Recommendations, item => item.Code == "A02.900" && !item.IsIndexFallback);
    }

    [Fact]
    public void 单字转示键_只在词首词尾锚定_两字键中段照转()
    {
        // 索引把 髋/高 这类单字立成转示行，但病历里它们几乎都是更长词的一部分：
        // 人工髋关节置换术后髋臼松动 的 髋 在中段，锚到 挛缩 会把正牌候选顶成髋关节挛缩；
        // 词尾的 不稳定髋 才是索引本来的答案。
        // 两字键不受此限——见-1 是"必须转换"，肠炎 -见 小肠炎 写在输入中段也要转。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "髋 -见 挛缩，狭窄", status: 1),
            LeadRow(2, "挛缩", "M24.5"),
            LeadRow(3, "肠炎 -见 小肠炎", status: 1),
            LeadRow(4, "小肠炎", "K52.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "M24.501", "髋关节挛缩"),
            Dict("ICD-10", "K52.900", "感染性胃肠炎"),
        ]);

        var mid = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "人工髋关节置换术后髋臼松动", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        Assert.DoesNotContain(mid.LeadTerms, lead => lead.RedirectKey is not null);
        Assert.DoesNotContain(mid.LeadTerms, lead => lead.Term.StartsWith("挛缩"));

        var tail = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "不稳定髋", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        Assert.Contains(tail.LeadTerms, lead => lead.RedirectKey is not null);

        var twoCharMid = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "病毒性肠炎伴脱水", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        Assert.Contains(twoCharMid.LeadTerms, lead => lead.RedirectKey == "肠炎");
    }

    [Fact]
    public void 转示锚定_子行已出精确码时不并列泛词本行回退码()
    {
        // "扁桃体炎 - 见 感染" 锚到 感染：子行 扁桃体 给出精确码 J03.900，
        // 而 感染 本行取码兜底只能给出类目回退码 A49.900（未特指的细菌感染）——
        // 泛词本行码与输入病种不同源，并列进候选就是噪声（实测噪声族：
        // 孕产妇医疗→咨询 Z71.900、D-二聚体升高→效应 T78.900、沙门菌→危象 E05.500）。
        // 兜底仍保留给出路：无精确码时（查 肠炎 必转 小肠炎，输入写不出修饰词）照旧出声。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "扁桃体炎 - 见 感染", status: 1),
            LeadRow(2, "感染(性)(机会性)", "A49.9"),
            ChildRow(3, 2, "－扁桃体", "J03.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A49.900", "未特指的细菌感染"),
            Dict("ICD-10", "J03.900", "急性扁桃体炎"),
        ]);

        var redirected = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "扁桃体炎", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        var redirectLead = Assert.Single(redirected.LeadTerms, lead => lead.RedirectKey is not null);
        Assert.NotEmpty(redirectLead.ModifierPaths);
        Assert.Contains(redirected.Recommendations, item => item.Code == "J03.900");
        Assert.DoesNotContain(redirected.Recommendations, item => item.IsIndexFallback);
        // 对照组：同一主导词由自己的别名锚定（非转示）且输入写不出任何修饰词时，
        // 本行取码兜底照旧交付——"查 肠炎 必转 小肠炎" 那类无修饰词输入全靠它。
        var direct = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "细菌感染", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        Assert.Contains(direct.LeadTerms, lead => lead.Term.StartsWith("感染") && lead.RedirectKey is null);
        Assert.Contains(direct.Recommendations, item => item.Code == "A49.900");
    }

    [Fact]
    public void 主导词行另见提示_整标题精确码不因提示身份降级()
    {
        // 真库形态：脓毒症(另见 败血症) A41.9 → 败血症 → －土拉菌性 A21.7 → A21.700X002 土拉菌脓毒症。
        // 编码员口径（另见-1：本行带码但指针树里有更细的码必须转）要求 A21.700X002 交在首位；
        // 按"提示码一律殿后"实现时它被压在泛词 A41.900 之后（万名实测 26 名同样形态：
        // 大肠杆菌脓毒症 A41.501、金黄色葡萄球菌性脓毒症 A41.000、钱币状湿疹 L30.000X001…）。
        // 故逐字整标题命中（WholeTitleDepth==1）的提示码照常参加排序，只有非精确的提示码才垫后。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "脓毒症(全身性)(化脓性)(另见 败血症)", "A41.9"),
            LeadRow(2, "败血症(性)(全身性)(化脓性)", "A41.9"),
            ChildRow(3, 2, "－土拉菌性", "A21.7"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A41.900", "脓毒症"),
            Dict("ICD-10", "A21.700X002", "土拉菌脓毒症"),
        ]);

        Environment.SetEnvironmentVariable("NL_LEAD_SEEALSO", "1");
        try
        {
            var response = NameCodingLookupEngine.Lookup(
                NameCodingLookupEngine.DiagnosisKind, "土拉菌脓毒症", topK: 8,
                icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
                cm3Snapshot: null, cm3Dictionary: null,
                new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

            Assert.Equal("A21.700X002", response.Recommendations[0].Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NL_LEAD_SEEALSO", null);
        }
    }

    [Fact]
    public void 主导词行另见提示_开关关时不出提示链路()
    {
        // 轮次口径开关默认关：主导词行自身的 另见-1 目标树不进入面板链路（流水线不动，
        // 开关只为 A/B 台账存在，落地形态另行拍板）。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "脓毒症(全身性)(化脓性)(另见 败血症)", "A41.9"),
            LeadRow(2, "败血症(性)(全身性)(化脓性)", "A41.9"),
            ChildRow(3, 2, "－土拉菌性", "A21.7"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("ICD-10", "A41.900", "脓毒症"),
            Dict("ICD-10", "A21.700X002", "土拉菌脓毒症"),
        ]);

        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, "土拉菌脓毒症", topK: 8,
            icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
            cm3Snapshot: null, cm3Dictionary: null,
            new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

        Assert.DoesNotContain(response.Recommendations, item => item.Code == "A21.700X002");
    }

    [Fact]
    public void 引擎_空文本返回空结果()
    {
        var response = NameCodingLookupEngine.Lookup(
            NameCodingLookupEngine.DiagnosisKind, " 　", topK: 8,
            icdSnapshot: LymphomaSnapshot(), tumorSnapshot: null, icdDictionary: null,
            cm3Snapshot: null, cm3Dictionary: null);

        Assert.Empty(response.LeadTerms);
        Assert.Empty(response.Recommendations);
        Assert.Empty(response.DictionaryDirects);
    }
}
