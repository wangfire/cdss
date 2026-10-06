using HospitalAi.Application.Coding.IcdIndex;
using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Tests;

/// <summary>
/// ICD 索引驱动两段式推荐纯逻辑单测：词条解析、主导词召回、逐级下钻、
/// 7 种索引编码格式解析、字典展开与三级回退。夹具取自已验证的真实数据行。
/// </summary>
public sealed class IcdIndexTests
{
    private static IcdIndexRow LeadRow(int seqNo, string name, string? code = null, int status = 0) =>
        new(seqNo, name, code, 0, 0, status);

    private static IcdIndexRow ChildRow(
        int seqNo, int parentSeqNo, string name, string? code = null, int level = 1) =>
        new(seqNo, name, code, level, parentSeqNo, 0);

    private static IcdDictionaryRow Dict(string code, string title, string? searchText = null) =>
        new("ICD-10", code, title, searchText ?? title);

    /// <summary>真实索引树：阿尔茨海默病 seq 65-72（icd-index-batches）。</summary>
    private static IcdIndexSnapshot AlzheimerSnapshot() => IcdIndexSnapshotBuilder.Build([
        LeadRow(65, "阿尔茨海默病或硬化", "G30.9"),
        ChildRow(66, 65, "－早期发病(早老性)", "G30.0"),
        ChildRow(67, 65, "－晚期发病(老年性)", "G30.1"),
        ChildRow(68, 65, "－特指的 NEC", "G30.8"),
        ChildRow(69, 65, "－引起痴呆", "G30.9† F00.9*"),
        ChildRow(70, 69, "－－早期发病(早老性)", "G30.0† F00.0*", level: 2),
        ChildRow(71, 69, "－－晚期发病(老年性)", "G30.1† F00.1*", level: 2),
        ChildRow(72, 69, "－－非典型型或混合型", "G30.8† F00.2*", level: 2),
    ]);

    /// <summary>真实 ICD-10 字典行（icd10-2026.json）。</summary>
    private static IcdDictionarySnapshot AlzheimerDictionary() => new([
        Dict("G30.100X002+F00.1*", "家族性阿尔茨海默病性痴呆(老年型)"),
        Dict("G30.100X003+F00.1*", "阿尔茨海默病性痴呆(老年型)"),
        Dict("G30.100X001", "阿尔茨海默病(老年型)"),
        Dict("G30.100", "阿尔茨海默病伴有晚期发病"),
        Dict("F00.1*", "晚发性阿尔茨海默病性痴呆(G30.1+)"),
    ]);

    [Fact]
    public void 黄金用例_索引链路端到端推荐_老年型阿尔茨海默病性痴呆()
    {
        var resolver = new IcdIndexDiagnosisResolver(leadWordTopK: 8, maxResults: 3, diagnosisCodeSystem: "ICD-10");

        var results = resolver.Resolve(
            AlzheimerSnapshot(), AlzheimerDictionary(), "阿尔茨海默病引起痴呆(老年型)");

        Assert.Equal(2, results.Count);
        Assert.Collection(results.OrderBy(item => item.Code),
            item => Assert.Equal("G30.100X002+F00.1*", item.Code),
            item => Assert.Equal("G30.100X003+F00.1*", item.Code));
        Assert.All(results, item => Assert.False(item.IsIndexFallback));
        Assert.All(results, item => Assert.False(item.IsConditionalAddOn));
        Assert.Contains("引起痴呆", results[0].EvidencePath);
        Assert.Contains("晚期发病(老年性)", results[0].EvidencePath);
        Assert.Contains("索引编码: G30.1† F00.1*", results[0].EvidencePath);
    }

    [Fact]
    public void 黄金用例_字典展不开_回退推荐索引编码本身()
    {
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");
        var dictionary = new IcdDictionarySnapshot([Dict("J18.900", "肺炎")]);

        var results = resolver.Resolve(
            AlzheimerSnapshot(), dictionary, "阿尔茨海默病引起痴呆(老年型)");

        var only = Assert.Single(results);
        Assert.True(only.IsIndexFallback);
        Assert.Equal("G30.1† F00.1*", only.Code);
    }

    [Fact]
    public void 诊断侧无主导词命中_不再回退字典直匹配()
    {
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var results = resolver.Resolve(
            AlzheimerSnapshot(), AlzheimerDictionary(), "社区获得性肺炎");

        Assert.Empty(results);
    }

    [Fact]
    public void ParseNode_或拆分别名与跳转段剥离()
    {
        var alzheimer = IcdIndexSnapshotBuilder.ParseNode(LeadRow(65, "阿尔茨海默病或硬化", "G30.9"));
        Assert.Contains(alzheimer.Aliases, alias => alias.Key == TextMatch.NormalizeKey("阿尔茨海默病"));
        Assert.Contains(alzheimer.Aliases, alias => alias.Key == "硬化" && alias.Weight == 0.85m);

        var see = IcdIndexSnapshotBuilder.ParseNode(
            LeadRow(97, "阿米巴 - 见 阿米巴病", status: 1));
        Assert.Equal(IndexJumpKind.See, see.JumpKind);
        Assert.Equal("阿米巴病", see.JumpTarget);
        Assert.Equal("阿米巴", see.Term);

        var seeAlso = IcdIndexSnapshotBuilder.ParseNode(
            LeadRow(10, "HIV - 另见 人(类)，免疫缺陷病毒[HIV]病"));
        Assert.Equal(IndexJumpKind.SeeAlso, seeAlso.JumpKind);
        Assert.Equal("HIV", seeAlso.Term);
    }

    [Fact]
    public void 快照构建_失效主导词进入重定向表()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(97, "阿米巴 - 见 阿米巴病", status: 1),
            LeadRow(98, "阿米巴病", "A06.9"),
        ]);

        Assert.NotEmpty(snapshot.Leads);
        Assert.All(snapshot.Leads, lead => Assert.Equal(98, lead.SeqNo));
        Assert.Equal("阿米巴病", IcdIndexMatcher.ResolveRedirect(
            snapshot, TextMatch.NormalizeKey("阿米巴")));
    }

    [Fact]
    public void Drill_子级见跳转_改道目标主导词继续下钻()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(26228, "脓肿(栓子)(感染性)(转移性)(多发性)(生脓性)(脓毒性)", "L02.9"),
            ChildRow(9002, 26228, "－阿米巴 - 见 阿米巴病"),
            LeadRow(98, "阿米巴病", "A06.9"),
            ChildRow(9003, 98, "－肝脓肿", "A06.0"),
        ]);
        var inputKey = TextMatch.NormalizeKey("肝脓肿阿米巴");
        var leadHit = IcdIndexMatcher.RecallLeads(snapshot, inputKey, 8)
            .Single(hit => hit.Node.SeqNo == 26228);

        var drill = IcdIndexMatcher.Drill(snapshot, leadHit, inputKey);

        Assert.NotNull(drill);
        Assert.Equal(9003, drill!.CodeNode.SeqNo);
        Assert.Equal("A06.0", drill.CodeNode.Code);
        Assert.Equal(["阿米巴 → 阿米巴病"], drill.Jumps);
    }

    [Fact]
    public void Drill_终点无编码_回退最近有码祖先()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(500, "肾衰竭", "N19.9"),
            ChildRow(501, 500, "－急性"),
        ]);
        var inputKey = TextMatch.NormalizeKey("急性肾衰竭");
        var leadHit = Assert.Single(IcdIndexMatcher.RecallLeads(snapshot, inputKey, 8));

        var drill = IcdIndexMatcher.Drill(snapshot, leadHit, inputKey);

        Assert.NotNull(drill);
        Assert.Equal(501, drill!.Terminal.SeqNo);
        Assert.Equal(500, drill.CodeNode.SeqNo);
        Assert.True(drill.CodeFromAncestorFallback);
    }

    [Fact]
    public void 部位门控_主导词词条含输入未声明的部位词_整节点剔除()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(7740, "断裂"),
            LeadRow(7752, "断裂，视网膜(不伴有脱离)", "H33.3"),
            ChildRow(7753, 7752, "－－部分切除", "H33.2"),
        ]);
        var dictionary = new IcdDictionarySnapshot([Dict("H33.300", "视网膜断裂不伴有脱离")]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        // 前臂肌腱断裂不得被 "断裂，视网膜" 主导词抢走。
        Assert.Empty(resolver.Resolve(snapshot, dictionary, "左前臂伸指肌部分断裂"));

        // 输入声明视网膜时链路正常。
        var retinal = resolver.Resolve(snapshot, dictionary, "视网膜断裂不伴有脱离");
        Assert.Contains(retinal, item => item.Code == "H33.300");
    }

    [Fact]
    public void 部位门控_下钻禁止进入输入未声明部位的子树()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(10789, "钙化"),
            ChildRow(10830, 10789, "－肾", "N28.8"),
            ChildRow(10844, 10789, "－转移性钙化 - 见 钙化性牙源性"),
            LeadRow(10900, "钙化性牙源性", "K00.4"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("N28.800", "肾钙化"),
            Dict("K00.400", "牙钙化"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");
        var results = resolver.Resolve(snapshot, dictionary, "左肾囊肿伴钙化");

        var only = Assert.Single(results);
        Assert.Equal("N28.800", only.Code);
    }

    [Fact]
    public void 部位门控_见跳转不得劫走本树更贴合的下钻链路()
    {
        // 真实索引树 51026 阻塞(性)：虚字符"性"使 "器官或部位,先天性NEC -见 闭锁"
        // 挣到增益，曾把 慢阻肺 从 "－呼吸道，慢性 J44.9" 劫到先天心血管 Q25.5。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(51026, "阻塞(性) - 另见 梗阻"),
            ChildRow(51076, 51026, "－器官或部位，先天性 NEC - 见 闭锁，按部位"),
            ChildRow(51057, 51026, "－呼吸道，慢性", "J44.9"),
            LeadRow(60001, "闭锁，按部位 NEC"),
            ChildRow(60002, 60001, "－肺(动脉)", "Q25.5"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("J44.900", "慢性阻塞性肺病"),
            Dict("Q25.500", "肺动脉瓣闭锁"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var results = resolver.Resolve(snapshot, dictionary, "慢性阻塞性肺病");

        Assert.Contains("J44.900", results.Single().Code);
    }

    [Fact]
    public void 部位门控_钙化不得下钻进输入未声明的基底节或肾()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(10789, "钙化"),
            ChildRow(10797, 10789, "－肺(活动性)(感染后)", "J98.4"),
            ChildRow(10807, 10789, "－基底节", "G23.8"),
            ChildRow(10830, 10789, "－肾", "N28.8"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("J98.400X001", "肺病"),
            Dict("G23.802", "基底节钙化"),
            Dict("N28.808", "肾钙化"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var codes = resolver.Resolve(snapshot, dictionary, "右肺微小结节并钙化")
            .Select(item => item.Code)
            .ToList();

        Assert.DoesNotContain("G23.802", codes);
        Assert.DoesNotContain("N28.808", codes);
        Assert.Contains(codes, code => code.StartsWith("J98.4", StringComparison.Ordinal));
    }

    [Fact]
    public void 部位门控_四肢肌腱断裂不得下钻心脏腱索()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(7740, "断裂"),
            ChildRow(7741, 7740, "－腱索 NEC", "I51.1"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("I51.100", "腱索断裂,不可归类在他处者"),
            Dict("I51.100X001", "心脏腱索断裂"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        Assert.Empty(resolver.Resolve(snapshot, dictionary, "右冈上肌肌腱断裂"));
        Assert.NotEmpty(resolver.Resolve(snapshot, dictionary, "二尖瓣腱索断裂"));
    }

    [Fact]
    public void 病程门控_急性输入不得下钻慢性分支()
    {
        // 真实索引树 12443 梗死，梗塞：心肌(急性或…4周内) I21.9 / 慢性或…超过4周 I25.8。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(12443, "梗死，梗塞"),
            ChildRow(12493, 12443, "－心肌(急性或伴有提及持续时间在4周或小于4周者)", "I21.9"),
            ChildRow(12494, 12493, "慢性或伴有提及持续时间超过4周者", "I25.8"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("I21.900", "急性心肌梗死"),
            Dict("G23.802", "基底节钙化"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var codes = resolver.Resolve(snapshot, dictionary, "4.急性心肌梗死")
            .Select(item => item.Code)
            .ToList();

        Assert.Equal("I21.900", Assert.Single(codes));
    }

    [Fact]
    public void 部位门控_澄清括号不作硬约束_半月板与肠梗阻恢复()
    {
        // "肠梗阻(结肠)(抑制性)"、"半月板(膝)(近期损伤)" 的括号是索引惯例注解，
        // 输入未提 结肠/膝 时仍须命中（曾把 肠梗阻待查、半月板撕裂 全部误杀）。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(4967, "肠梗阻(结肠)(抑制性)", "K56.7"),
            LeadRow(36774, "撕裂，撕裂伤(创伤性)"),
            ChildRow(36775, 36774, "－半月板(膝)(近期损伤)", "S83.2"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("K56.700", "肠梗阻"),
            Dict("S83.200", "半月板撕裂,近期的"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        Assert.Contains("K56.700",
            resolver.Resolve(snapshot, dictionary, "肠梗阻原因待查").Select(item => item.Code));
        Assert.Contains("S83.200",
            resolver.Resolve(snapshot, dictionary, "半月板撕裂，近期的").Select(item => item.Code));
    }

    [Fact]
    public void ResolveRedirect_现译名重定向后完成索引推荐()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(9001, "矽肺 - 现译名 肺硅沉着病", status: 1),
            LeadRow(9002, "肺硅沉着病", "J62.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([Dict("J62.900", "肺硅沉着病")]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var results = resolver.Resolve(snapshot, dictionary, "矽肺");

        var only = Assert.Single(results);
        Assert.Equal("J62.900", only.Code);
    }

    [Theory]
    [InlineData("", IndexCodeFormatKind.Empty)]
    [InlineData("G30.9", IndexCodeFormatKind.Single)]
    [InlineData("G30.9† F00.9*", IndexCodeFormatKind.DaggerPair)]
    [InlineData("M9270/3:C41.1", IndexCodeFormatKind.Morphology)]
    [InlineData("E10.3† H28.0*;E11.3† H28.0*", IndexCodeFormatKind.Alternatives)]
    [InlineData("I00@I09.0", IndexCodeFormatKind.Conditional)]
    [InlineData("N04.-", IndexCodeFormatKind.CategoryUnspecified)]
    [InlineData("D65-D69", IndexCodeFormatKind.CodeRange)]
    public void Parse_七种索引编码格式(string raw, IndexCodeFormatKind expected)
    {
        Assert.Equal(expected, ParsedIndexCode.Parse(raw).Kind);
    }

    [Fact]
    public void Parse_细节字段与嵌套格式()
    {
        var pair = ParsedIndexCode.Parse("G30.9† F00.9*");
        Assert.Equal("G30.9", pair.DaggerCode);
        Assert.Equal("F00.9", pair.AsteriskCode);

        var morphology = ParsedIndexCode.Parse("M9270/3:C41.1");
        Assert.Equal("M9270/3", morphology.MorphologyCode);
        Assert.Equal("C41.1", morphology.SiteCode);

        var alternatives = ParsedIndexCode.Parse(
            "E10.3† H28.0*;E11.3† H28.0*;E12.3† H28.0*;E13.3† H28.0*;E14.3† H28.0*");
        Assert.Equal(5, alternatives.Alternatives!.Count);
        Assert.Equal("E10.3", alternatives.Alternatives[0].DaggerCode);

        var unspecified = ParsedIndexCode.Parse("N04.-");
        Assert.Equal("N04", unspecified.Prefix);

        // 嵌套：范围替代组 @ 附加码（真实 seq 24480）。
        var nested = ParsedIndexCode.Parse("I00-I99;Q20-Q28@P00.3");
        Assert.Equal(IndexCodeFormatKind.Conditional, nested.Kind);
        Assert.Equal(IndexCodeFormatKind.Alternatives, nested.Trigger!.Kind);
        Assert.Equal(IndexCodeFormatKind.CodeRange, nested.Trigger.Alternatives![0].Kind);
        Assert.Equal("P00.3", nested.AddOn!.Prefix);
    }

    [Theory]
    [InlineData("D65", "D69", new[] { "D65", "D66", "D67", "D68", "D69" })]
    [InlineData("I00", "I99", new string[0])]
    public void RangePrefixes_逐类目前缀展开(string start, string end, string[] expected)
    {
        Assert.Equal(expected, ParsedIndexCode.RangePrefixes(start, end));
    }

    [Fact]
    public void Expand_替代编码组_按输入择一且禁止全量输出()
    {
        var parsed = ParsedIndexCode.Parse("E10.3† H28.0*;E11.3† H28.0*");
        var dictionary = new IcdDictionarySnapshot([
            Dict("E11.302+H28.0*", "2型糖尿病性白内障"),
        ]);

        var results = IndexCodeExpander.Expand(
            parsed, dictionary, "2型糖尿病性白内障",
            TextMatch.NormalizeKey("白内障糖尿病性"), maxResults: 3);

        var only = Assert.Single(results);
        Assert.Equal("E11.302+H28.0*", only.Code);
        Assert.False(only.IsIndexFallback);
    }

    [Fact]
    public void Expand_替代编码组全部无法判定_回退索引编码本身()
    {
        var parsed = ParsedIndexCode.Parse("E10.3† H28.0*;E11.3† H28.0*");
        var dictionary = new IcdDictionarySnapshot([]);

        var results = IndexCodeExpander.Expand(
            parsed, dictionary, "白内障", TextMatch.NormalizeKey("白内障"), maxResults: 3);

        var only = Assert.Single(results);
        Assert.True(only.IsIndexFallback);
        Assert.Equal("E10.3† H28.0*;E11.3† H28.0*", only.Code);
    }

    [Fact]
    public void Expand_条件联动_I00触发码存在才追加I090附加码()
    {
        var parsed = ParsedIndexCode.Parse("I00@I09.0");
        var dictionary = new IcdDictionarySnapshot([
            Dict("I00.X00X002", "风湿热"),
            Dict("I09.000", "风湿性心肌炎"),
        ]);

        var results = IndexCodeExpander.Expand(
            parsed, dictionary, "心肌变性伴有风湿热",
            TextMatch.NormalizeKey("变性心肌伴有风湿热"), maxResults: 5);

        Assert.Equal(2, results.Count);
        Assert.Equal("I00", results[0].Code);
        Assert.True(results[0].IsIndexFallback);
        Assert.Equal("I09.0", results[1].Code);
        Assert.True(results[1].IsConditionalAddOn);
        Assert.Contains("条件联动附加码", results[1].Note);
    }

    [Fact]
    public void Expand_父类目检索串聚合子码标题_逐字命中者排首位()
    {
        // 甲状腺毒症 E05.900 的 search_text 把子码全名整批收进来，与输入逐字相等的
        // E05.903+I43.8* 若与父码同分，会被 ThenBy(编码长度) 判给短码父类目。
        var parsed = ParsedIndexCode.Parse("E05.9");
        var dictionary = new IcdDictionarySnapshot([
            new IcdDictionaryRow("ICD-10", "E05.900", "甲状腺毒症",
                "甲状腺毒症 甲状腺功能亢进症 甲状腺功能亢进性心脏病"),
            new IcdDictionaryRow("ICD-10", "E05.903+I43.8*", "甲状腺功能亢进性心脏病",
                "甲状腺功能亢进性心脏病"),
        ]);

        var results = IndexCodeExpander.Expand(
            parsed, dictionary, "甲状腺功能亢进性心脏病",
            TextMatch.NormalizeKey("甲状腺功能亢进症"), maxResults: 3);

        Assert.Equal("E05.903+I43.8*", results[0].Code);
        // 父码与子码同为满分（子码靠标题、父码靠聚合检索串），胜负在反向覆盖这一票。
        Assert.Equal("E05.900", results[1].Code);
        Assert.Equal(1m, results[0].Score);
        Assert.True(results[0].TitleSpecificity > results[1].TitleSpecificity);
    }

    [Fact]
    public void Expand_类目范围_逐类目字典前缀命中()
    {
        var parsed = ParsedIndexCode.Parse("D65-D69");
        var dictionary = new IcdDictionarySnapshot([
            Dict("D65.X00X001", "弥散性血管内凝血"),
        ]);

        var results = IndexCodeExpander.Expand(
            parsed, dictionary, "弥散性血管内凝血",
            TextMatch.NormalizeKey("出血(性)由于凝血缺陷"), maxResults: 3);

        var only = Assert.Single(results);
        Assert.Equal("D65.X00X001", only.Code);
    }

    [Fact]
    public void Expand_未特指类目_N04横线展开为详码()
    {
        var parsed = ParsedIndexCode.Parse("N04.-");
        var dictionary = new IcdDictionarySnapshot([
            Dict("N04.000X002", "肾病综合征伴极微病理改变的肾损害"),
        ]);

        var results = IndexCodeExpander.Expand(
            parsed, dictionary, "肾病综合征",
            TextMatch.NormalizeKey("肾病(肾炎性)(先天性)"), maxResults: 3);

        var only = Assert.Single(results);
        Assert.Equal("N04.000X002", only.Code);
    }

    [Fact]
    public void 部位门控_或式并列别名的部位词不禁掉另一别名()
    {
        // 真实索引行 12188：主导词 "戈谢病或戈谢脾大"，第二个别名的 脾 不得把输入 "戈谢病" 整体挡掉。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(12188, "戈谢病或戈谢脾大(成人)(婴儿)", "E75.2"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("E75.201", "戈谢病,未特指的"),
            Dict("E75.200X002", "婴儿型戈谢病"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        Assert.NotEmpty(resolver.Resolve(snapshot, dictionary, "戈谢病"));
    }

    [Fact]
    public void 重叠门槛_待查类书写噪声不计入链路覆盖分母()
    {
        // "轻度贫血原因待查" 的合法链路只有 贫血 两字（真实索引行 27644 → D64.9），
        // 分母若含待查/程度修饰就稀释到 0.25 被丢弃。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(27644, "贫血", "D64.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("D64.900", "贫血,未特指的"),
            Dict("D64.900X001", "轻度贫血"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var codes = resolver.Resolve(snapshot, dictionary, "轻度贫血原因待查")
            .Select(item => item.Code)
            .ToList();

        Assert.Contains("D64.900", codes);
    }

    [Fact]
    public void 下钻新内容_重复字与虚字符不得撑起多跳()
    {
        // 真实索引树 41921 先兆 → 41925 流产(O20.0) → 41926 伴有随后性流产(O03.-)：
        // 第三跳只靠路径已有的 "流产" 与输入没有的 "性" 挣增益，终点应停在 O20.0。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(41921, "先兆"),
            ChildRow(41925, 41921, "－流产", "O20.0"),
            ChildRow(41926, 41925, "－－伴有随后性流产", "O03.-", level: 2),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("O20.000", "先兆流产"),
            Dict("O03.900", "自然流产"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var codes = resolver.Resolve(snapshot, dictionary, "孕2产0 孕13周+6 先兆流产")
            .Select(item => item.Code)
            .ToList();

        Assert.Contains("O20.000", codes);
        Assert.DoesNotContain("O03.900", codes);
    }

    public static TheoryData<string, int, string> 部位与形态尾段指针行() => new()
    {
        // 索引原行：这些 level-0 行自身无编码，子级全是 "呼吸，锻炼" "细胞，尿中" 式冷门分支，
        // 索引把它们标成 "另见 情况"，10000 条临床诊断的产出台账里 0 次下钻到码。
        { "动脉", 7269, "动脉" },
        { "呼吸", 15233, "呼吸" },
        { "形成", 43561, "形成" },
        { "功能不良", 12591, "功能不良" },
        { "拇指", 24493, "拇指 - 另见情况" },
        { "腹股沟", 10573, "腹股沟 - 另见情况" },
        { "颈椎", 19883, "颈(椎) - 另见情况" },
        { "粘液", 25679, "粘液 - 另见情况" },
        { "细胞", 41436, "细胞(性) - 另见情况" },
    };

    [Theory]
    [MemberData(nameof(部位与形态尾段指针行))]
    public void 主导词黑名单_部位与形态尾段不得锚定召回(string aliasKey, int seqNo, string rawName)
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([LeadRow(seqNo, rawName)]);

        var leads = IcdIndexMatcher.RecallLeads(
            snapshot, TextMatch.NormalizeKey($"{aliasKey}不适"), topK: 8);

        Assert.Empty(leads);
    }

    [Fact]
    public void 主导词黑名单_真实主导词锚点不受部位词拉黑影响()
    {
        // 呼吸困难 15253、动脉硬化 7482 在索引里本就是独立主导词行，
        // 拉黑 呼吸/动脉 只关掉指针词锚点，不切断这些真实链路。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(7269, "动脉"),
            LeadRow(15233, "呼吸"),
            LeadRow(7482, "动脉硬化(弥漫性)(病)(全身性)(闭塞性)(老年性)(伴有钙化)", "I70.9"),
            LeadRow(15253, "呼吸困难(夜间)(阵发性)", "R06.0"),
        ]);

        var leads = IcdIndexMatcher.RecallLeads(
            snapshot, TextMatch.NormalizeKey("双侧颈动脉硬化伴呼吸困难"), topK: 8);

        Assert.All(leads, hit => Assert.DoesNotContain(hit.Alias.Key, new[] { "动脉", "呼吸" }));
        Assert.Equal(2, leads.Count);
        Assert.Contains(leads, hit => hit.Node.SeqNo == 7482);
        Assert.Contains(leads, hit => hit.Node.SeqNo == 15253);
    }

    [Fact]
    public void 见情况跳转_按别名键精确锚定疾病主导词的部位子节点()
    {
        // 真实行：6770 "胆囊的 -见 情况"（失效主导词）、2972 病，疾病 → 3068 胆囊 K82.9。
        // 该别名键带 "的"，索引行 "胆囊的继发" 的表述靠诊断同义词 胆囊→胆囊的 生成变体，
        // 链路须精确锚定到 病，疾病→胆囊，而不是从根节点乱钻。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(6770, "胆囊的 - 见 情况", status: 1),
            LeadRow(2972, "病，疾病"),
            ChildRow(3068, 2972, "胆囊", "K82.9"),
            ChildRow(3069, 2972, "肾", "N28.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("K82.900", "胆囊疾病,未特指的"),
            Dict("N28.902", "肾疾病"),
        ]);
        var synonyms = IcdTermSynonymSnapshot.Build([
            new IcdTermSynonymRow(TermSynonymTypes.Diagnosis, "胆囊的", "胆囊"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var results = resolver.Resolve(
            snapshot, dictionary, "胆囊继发可能性大", synonyms: synonyms);

        var only = Assert.Single(results);
        Assert.Equal("K82.900", only.Code);
        Assert.False(only.IsIndexFallback);
        Assert.Contains("胆囊的 -见 情况", only.EvidencePath);
    }

    [Fact]
    public void 下钻别名择一_括号变体的游离字不得挡住枢纽词()
    {
        // 真实链 20794 溃疡(性) → 20955 十二指肠(腐蚀性)(消化性) → 20966 伴有 → 20967 出血 K26.4。
        // "十二指肠溃疡伴出血可能性大" 里 别名 "十二指肠腐蚀性消化性" 靠 "性" 多挣 1 增益，
        // 却把 12 个输入没写的字符塞进路径，"伴有" 增益归零、字典展开也过不了。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(20794, "溃疡(性)", "L98.4"),
            ChildRow(20955, 20794, "十二指肠(腐蚀性)(消化性)", "K26.9"),
            ChildRow(20966, 20955, "伴有"),
            ChildRow(20967, 20966, "出血", "K26.4", level: 3),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("K26.400", "十二指肠溃疡伴有出血"),
            Dict("K26.900", "十二指肠溃疡未特指的"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var results = resolver.Resolve(snapshot, dictionary, "十二指肠溃疡伴出血可能性大");

        Assert.Equal("K26.400", results[0].Code);
        Assert.Contains("伴有 → 出血", results[0].EvidencePath);
    }

    [Fact]
    public void 另见跳转_本树停在跳转子节点且无自码时改道取码()
    {
        // 真实链 1445 闭塞，闭合 → 1456 "动脉 -另见 栓塞，动脉"（本行无码、下面没有
        // "胫前/右侧" 可下）；改道 36359 栓塞，动脉 → 36372 动脉 I74.9 才能出码。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1445, "闭塞，闭合", "I99"),
            ChildRow(1456, 1445, "动脉 - 另见 栓塞，动脉"),
            ChildRow(1457, 1456, "肠系膜(慢性)", "K55.0", level: 2),
            LeadRow(36359, "栓塞(脓毒性)", "I74.9"),
            ChildRow(36372, 36359, "动脉", "I74.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("I74.900", "动脉栓塞,未特指的"),
            Dict("K55.000", "肠系膜动脉闭塞"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var results = resolver.Resolve(snapshot, dictionary, "右侧胫前动脉闭塞");

        Assert.Equal("I74.900", results[0].Code);
        Assert.DoesNotContain(results, item => item.Code.StartsWith("K55.0", StringComparison.Ordinal));
        Assert.Contains("⇢ 栓塞，动脉", results[0].EvidencePath);
    }

    [Fact]
    public void 人群修饰词_只能作为疾病主导词的下钻目标()
    {
        // 真实链 39718 脱水，缺水血症 → 39719 新生儿 P74.1；43674 休克 → 43679 出生后，
        // 胎儿或新生儿 NEC P96.8。新生儿 拉黑只关掉 "…，影响到胎儿或新生儿" 式锚点，
        // 不得影响它作为疾病主导词下钻修饰词。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(39718, "脱水，缺水血症", "E86"),
            ChildRow(39719, 39718, "新生儿", "P74.1"),
            LeadRow(43674, "休克", "R57.9"),
            ChildRow(43679, 43674, "出生后，胎儿或新生儿 NEC", "P96.8"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("P74.100", "新生儿脱水"),
            Dict("P96.804", "新生儿休克"),
            Dict("E86.000", "血容量不足"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        Assert.Equal("P74.100", resolver.Resolve(snapshot, dictionary, "新生儿脱水")[0].Code);
        Assert.Equal("P96.804", resolver.Resolve(snapshot, dictionary, "新生儿休克")[0].Code);
    }

    [Fact]
    public void 主导词黑名单_新生儿拉黑但婴儿放行()
    {
        // 新生儿 拉黑依据（本地 10000 条实测）：放行它会让 "重于胎龄儿 NEC(胎儿或婴儿)"、
        // "三胎，影响到胎儿或新生儿" 挤占召回名额，把 新生儿休克/惊厥 打成 P00.8x 噪声。
        // 婴儿 不拉黑：它 27 次召回 20 次下钻到码（特大婴儿→P08.0），拉黑即打成空推荐。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(39600, "三胎，影响到胎儿或新生儿", "O30.3"),
            LeadRow(49856, "重于胎龄儿 NEC (胎儿或婴儿)", "P08.1"),
            ChildRow(49857, 49856, "特大(4500克或以上)", "P08.0"),
        ]);

        var newborn = IcdIndexMatcher.RecallLeads(
            snapshot, TextMatch.NormalizeKey("新生儿评分低"), topK: 8);
        Assert.Empty(newborn);

        var macrosomia = IcdIndexMatcher.RecallLeads(
            snapshot, TextMatch.NormalizeKey("特大婴儿"), topK: 8);
        Assert.Contains(macrosomia, hit => hit.Node.SeqNo == 49856);
    }

    [Fact]
    public void 术语同义词_诊断改写产生规范变体且手术通道不串味()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(13000, "甲状腺功能亢进", "E05.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([Dict("E05.900", "甲状腺功能亢进症")]);
        var synonyms = IcdTermSynonymSnapshot.Build([
            new IcdTermSynonymRow(TermSynonymTypes.Diagnosis, "甲状腺功能亢进", "甲亢"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        Assert.Empty(resolver.Resolve(snapshot, dictionary, "甲亢"));
        Assert.Equal("E05.900",
            resolver.Resolve(snapshot, dictionary, "甲亢", synonyms: synonyms)[0].Code);

        // 手术类型独立成组：诊断规则不参与手术侧改写。
        Assert.Equal(["甲亢"], synonyms.ExpandVariants("甲亢", TermSynonymTypes.Procedure));
    }

    [Theory]
    // 输入明写 2 型 → 其他分型的候选行整行剔除（E13.4 继发性抢 E11.4 的导出第 21 行）。
    [InlineData("继发性糖尿病性周围神经病变", null, "2型糖尿病性植物神经病变", false, true)]
    [InlineData("1型糖尿病性周围神经病", null, "2型糖尿病", false, true)]
    [InlineData("糖尿病性周围神经病", null, "2型糖尿病", false, false)]
    [InlineData("2型糖尿病性周围神经病", null, "糖尿病周围神经病变", false, false)]
    [InlineData("2型糖尿病性周围神经病", null, "2型糖尿病", false, false)]
    // 任一方声明多型（索引书的 "1型或2型" 合并行）交给覆盖度打分，不做互斥、也不套默认。
    [InlineData("1型或2型糖尿病性周围神经病", null, "2型糖尿病", false, false)]
    [InlineData("2型糖尿病性周围神经病", null, "1型或2型糖尿病", false, false)]
    // ASCII 罗码经 NormalizeKey 统一成数字，II型 与 2型 同源。
    [InlineData("继发性糖尿病", null, "II型糖尿病", false, true)]
    // 非糖尿病行不参与判定，避免 "1型呼吸衰竭" 之类误伤。
    [InlineData("1型呼吸衰竭", null, "2型糖尿病", false, false)]
    // 分型替代组内：标题不写分型按类目定性，输入不写分型按 2型，两者互斥。
    [InlineData("糖尿病性视网膜病变", "E14.301+H36.0*", "轻度非增殖性糖尿病视网膜病变", true, true)]
    [InlineData("糖尿病性视网膜病变", "E14.301+H36.0*", "1型糖尿病", true, true)]
    [InlineData("2型糖尿病性视网膜病变", "E11.301+H36.0*", "轻度非增殖性糖尿病视网膜病变", true, false)]
    // 替代组外不套默认：主导词 糖尿病(性) 自身的编码就是 E14.-，裸输入"糖尿病"要留得住。
    [InlineData("糖尿病性视网膜病变", "E14.301+H36.0*", "轻度非增殖性糖尿病视网膜病变", false, false)]
    [InlineData("糖尿病", "E14.900X001", "继发性糖尿病", false, false)]
    // 非 E10~E14 类目（E71 槭糖尿病、E15 高血糖昏迷）不因"糖尿病"字样被分型规则牵连。
    [InlineData("槭糖尿病", "E71.000", "糖尿病视网膜病变", true, false)]
    [InlineData("高血糖昏迷", "E15", "糖尿病", true, false)]
    public void 糖尿病分型_互斥判定(
        string title, string? code, string input, bool inAlternatives, bool expected)
    {
        Assert.Equal(expected,
            TextMatch.HasDiabetesTypeConflict(title, code, input, inAlternatives));
    }

    [Fact]
    public void 糖尿病分型_替代编码组剔除他型后本型独占()
    {
        var parsed = ParsedIndexCode.Parse("E10.4† G63.2*;E11.4† G63.2*;E13.4† G63.2*");
        var dictionary = new IcdDictionarySnapshot([
            Dict("E10.401+G63.2*", "1型糖尿病性周围神经病"),
            Dict("E11.401+G63.2*", "2型糖尿病性周围神经病"),
            Dict("E13.400+G63.2*", "继发性糖尿病性周围神经病"),
        ]);

        var results = IndexCodeExpander.Expand(
            parsed, dictionary, "2型糖尿病性植物神经病变",
            TextMatch.NormalizeKey("神经病糖尿病性"), maxResults: 3);

        var only = Assert.Single(results);
        Assert.Equal("E11.401+G63.2*", only.Code);
        Assert.False(only.IsIndexFallback);
    }

    [Fact]
    public void 糖尿病分型_未写分型的输入按2型独占替代组()
    {
        // 索引书 视网膜病(变) → 糖尿病性 = E10.3†H36.0*;E11.3†H36.0*;…;E14.3†H36.0*。
        // 五元替代组在输入没写分型时原本整组判"信息不足"回退成原始编码串，
        // 使 轻度非增殖性糖尿病视网膜病变 的 Top-1 落到 中心性浆液性脉络膜视网膜病变 H35.701。
        var parsed = ParsedIndexCode.Parse(
            "E10.3† H36.0*;E11.3† H36.0*;E12.3† H36.0*;E13.3† H36.0*;E14.3† H36.0*");
        var dictionary = new IcdDictionarySnapshot([
            Dict("E10.301+H36.0*", "1型糖尿病性视网膜病变"),
            Dict("E11.301+H36.0*", "2型糖尿病性视网膜病变"),
            Dict("E14.301+H36.0*", "糖尿病性视网膜病变"),
        ]);

        var results = IndexCodeExpander.Expand(
            parsed, dictionary, "轻度非增殖性糖尿病视网膜病变",
            TextMatch.NormalizeKey("视网膜病糖尿病性"), maxResults: 3);

        var only = Assert.Single(results);
        Assert.Equal("E11.301+H36.0*", only.Code);
        Assert.False(only.IsIndexFallback);
    }

    [Theory]
    // 输入否定了标题肯定的限定词：非增殖性 ≠ 增殖性（也 ≠ 增殖性前期），子串匹配会误判成同义。
    [InlineData("2型糖尿病性增殖性视网膜病", "轻度非增殖性糖尿病视网膜病变", true)]
    [InlineData("2型糖尿病性增殖性前期视网膜病", "非增殖性糖尿病视网膜病变", true)]
    [InlineData("小细胞肺癌", "非小细胞肺癌", true)]
    [InlineData("霍奇金病", "非霍奇金淋巴瘤", true)]
    [InlineData("结核病", "非结核分枝杆菌感染", true)]
    [InlineData("胰岛素依赖型糖尿病", "非胰岛素依赖型糖尿病", true)]
    // 反方向同理：标题否定而输入肯定。
    [InlineData("非增殖性糖尿病性视网膜病变", "增殖性糖尿病视网膜病变", true)]
    // 标题不含被否定的限定词 → 放行（泛码正是 非增殖性 的正确落点）。
    [InlineData("2型糖尿病性视网膜病变", "非增殖性糖尿病视网膜病变", false)]
    [InlineData("腹股沟疝", "非嵌顿性腹股沟疝", false)]
    // 双方同侧否定不算矛盾；中文里 不典型 与 非典型 同义。
    [InlineData("非增殖性糖尿病性视网膜病变", "轻度非增殖性糖尿病视网膜病变", false)]
    [InlineData("不典型增生", "非典型增生", false)]
    // 输入没有"非"时整条规则不介入。
    [InlineData("增殖性视网膜病", "糖尿病视网膜病变", false)]
    public void 否定极_互斥判定(string title, string input, bool expected)
    {
        Assert.Equal(expected, TextMatch.HasNegationPoleConflict(title, input));
    }

    [Fact]
    public void 否定极_非增殖性不推增殖性详码()
    {
        var parsed = ParsedIndexCode.Parse(
            "E10.3† H36.0*;E11.3† H36.0*;E12.3† H36.0*;E13.3† H36.0*;E14.3† H36.0*");
        var dictionary = new IcdDictionarySnapshot([
            Dict("E11.300X031+H36.0*", "2型糖尿病性增殖性视网膜病"),
            Dict("E11.300X021+H36.0*", "2型糖尿病性增殖性前期视网膜病"),
            Dict("E11.301+H36.0*", "2型糖尿病性视网膜病变"),
            Dict("E11.300X011+H36.0*", "2型糖尿病性背景性视网膜病"),
        ]);

        var results = IndexCodeExpander.Expand(
            parsed, dictionary, "轻度非增殖性糖尿病视网膜病变",
            TextMatch.NormalizeKey("视网膜病糖尿病性"), maxResults: 3);

        Assert.Equal("E11.301+H36.0*", results[0].Code);
        Assert.DoesNotContain(results, item => item.Title.Contains("增殖", StringComparison.Ordinal));
    }

    [Fact]
    public void 否定极_括号里的默认否定注解不封死子树()
    {
        // 索引书 心肌病 → －肥厚型(非梗阻性) I42.2 → －－梗阻性 I42.1：
        // 第二层的 非梗阻性 只是"输入不写梗阻性时默认落非梗阻性码"的澄清注解，
        // 按整条 Term 判矛盾会把正确的 梗阻性肥厚型心肌病 I42.100 连子树一起打死，
        // 推荐退化成父码 心肌病 I42.900。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(43201, "心肌病(家族性)(特发性)", "I42.9"),
            ChildRow(43210, 43201, "－肥厚型(非梗阻性)", "I42.2"),
            ChildRow(43211, 43210, "－－梗阻性", "I42.1"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("I42.900", "心肌病"),
            Dict("I42.100", "梗阻性肥厚型心肌病"),
            Dict("I42.101", "肥厚型梗阻性心肌病"),
            Dict("I42.200X001", "肥厚性非梗阻性心肌病"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var results = resolver.Resolve(snapshot, dictionary, "梗阻性肥厚型心肌病");
        Assert.Equal("I42.100", results[0].Code);

        // 输入没写梗阻性时不放行矛盾判定：链路停在 I42.2，非梗阻性详码仍是正确推荐。
        Assert.Contains(
            resolver.Resolve(snapshot, dictionary, "肥厚型心肌病"),
            item => item.Code == "I42.200X001");
    }

    [Fact]
    public void 否定极_主导词括号注解不阻断召回()
    {
        // 主导词 "斜视(交替性)(先天性)(非麻痹性)" 自身编码 H50.9，其子层 －麻痹性 才是 H49.9。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(43066, "斜视(交替性)(先天性)(非麻痹性)", "H50.9"),
            ChildRow(43074, 43066, "－麻痹性", "H49.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("H49.900", "麻痹性斜视"),
            Dict("H49.901", "先天性麻痹性斜视"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var results = resolver.Resolve(snapshot, dictionary, "麻痹性斜视");
        Assert.Equal("H49.900", results[0].Code);
    }

    [Fact]
    public void 不确定性引导语_不得给带该字面的兄弟码白送覆盖()
    {
        // 真实索引链路：溃疡(性) 20794 → －十二指肠(腐蚀性)(消化性) 20955 → －－伴有 20966 → －－－出血 20967。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(20794, "溃疡(性)", "L98.4"),
            ChildRow(20955, 20794, "－十二指肠(腐蚀性)(消化性)", "K26.9"),
            ChildRow(20966, 20955, "－－伴有"),
            ChildRow(20967, 20966, "－－－出血", "K26.4"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("K26.400", "慢性十二指肠溃疡伴有出血"),
            Dict("K26.400X001", "十二指肠溃疡伴出血"),
            Dict("K26.401", "十二指肠球部溃疡伴出血"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        // 输入与 K26.400X001 的标题逐字相等；"可能性大" 是医生对诊断成立与否的标注，
        // 其 性 字不得替 慢性十二指肠溃疡伴有出血 补上输入未写的限定词。
        Assert.Equal(
            "K26.400X001",
            resolver.Resolve(snapshot, dictionary, "十二指肠溃疡伴出血可能性大")[0].Code);

        // 写了 球后（索引书无此分支）时，与输入只差一个字的 球部 兄弟码压过不含球字的通用码。
        Assert.Equal(
            "K26.401",
            resolver.Resolve(snapshot, dictionary, "十二指肠球后溃疡伴出血可能性大")[0].Code);
    }

    [Fact]
    public void 不确定性引导语_只剥引导语不剥程度词()
    {
        Assert.Equal("十二指肠溃疡伴出血", TextMatch.StripUncertaintyPhrases("十二指肠溃疡伴出血可能性大"));
        Assert.Equal("贫血", TextMatch.StripUncertaintyPhrases("贫血原因待查"));

        // 轻/中/重度是有语义的限定，剥了会让 轻度 与 重度 标题在候选打分里并列。
        Assert.Equal("轻度贫血", TextMatch.StripUncertaintyPhrases("轻度贫血"));
        Assert.Equal("贫血", TextMatch.StripIndexingFillers("轻度贫血"));
    }

    [Fact]
    public void 整标题命中_拆段沾满输入的长标题不得压过逐字相等的标题()
    {
        // 真实主导词：甲状腺肿(游动性)(胸骨后) seq 17790 → E04.9。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(17790, "甲状腺肿(游动性)(胸骨后)", "E04.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("E04.900", "非毒性甲状腺肿"),
            Dict("E04.900X001", "甲状腺肿"),
            Dict("E04.900X006", "甲状腺锥叶代偿性肿大"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        // 段贪心覆盖下 甲状腺锥叶代偿性肿大 也是 5/5 全覆盖（甲状腺 + 肿大 两段沾满），
        // 只有"标题被输入逐字连续包含"才是编码员认的那一条。
        Assert.Equal(
            "E04.900X001",
            resolver.Resolve(snapshot, dictionary, "甲状腺肿大原因待查")[0].Code);
    }

    [Fact]
    public void 整标题命中_上位短标题不得越过输入声明的限定词()
    {
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(17790, "甲状腺肿(游动性)(胸骨后)", "E04.9"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("E04.900X001", "甲状腺肿"),
            Dict("E04.902", "结节性甲状腺肿"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        // 甲状腺肿 同样被输入连续包含，但它只解释 8 字输入里的 4 字；
        // 不加覆盖门槛就抬满分，上位短标题会压掉输入声明的 结节性。
        var results = resolver.Resolve(snapshot, dictionary, "结节性甲状腺肿大");
        Assert.Equal("E04.902", results[0].Code);
        Assert.Contains(results, item => item.Code == "E04.900X001");
    }

    [Fact]
    public void 整标题命中_同分下上位码不得凭编码更短压过逐字更完整的标题()
    {
        // 血管瘤 与 肝血管瘤 都被输入逐字包含（3/4 与 4/4 都过 0.75 门槛）、同分满分，
        // 只按编码长度排会让上位码 D18.000 抢到 Top-1；贴合度取"解释掉输入字符更多"的那一条。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(1, "血管瘤", "D18.0"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("D18.000", "血管瘤"),
            Dict("D18.013", "肝血管瘤"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        Assert.Equal("D18.013", resolver.Resolve(snapshot, dictionary, "肝血管瘤")[0].Code);
    }

    [Fact]
    public void 整标题命中_标题比输入多一个构词性后缀仍算逐字命中()
    {
        // 真实主导词：视网膜病(变)(背景性)(科茨)(渗出性)(高血压性) seq 35795 → H35.0。
        // 临床写 高血压视网膜病变、字典写 高血压性视网膜病变：性 是构词虚词，
        // 按严格逐字包含判它会丢掉满分，被输入包含的短上位标题 视网膜病 凭 1.0 抢 Top-1。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(35795, "视网膜病(变)(背景性)(科茨)(渗出性)(高血压性)", "H35.0"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("H35.000X005", "视网膜病"),
            Dict("H35.004", "高血压性视网膜病变"),
            Dict("H35.001", "视网膜血管病变"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        var results = resolver.Resolve(snapshot, dictionary, "--双眼高血压视网膜病变");
        Assert.Equal("H35.004", results[0].Code);
        Assert.Contains(results, item => item.Code == "H35.000X005");
    }

    [Fact]
    public void 整标题命中_剥性后缀不得放行标题未逐字说出的限定词()
    {
        // 剥掉 性 后 糖尿病性视网膜病变 的词干是 糖尿病视网膜病变，输入并未逐字连续说出它，
        // 宽容判定必须照样失败：只允许补虚词，不允许补病变概念。
        var snapshot = IcdIndexSnapshotBuilder.Build([
            LeadRow(35795, "视网膜病(变)(背景性)(科茨)(渗出性)(高血压性)", "H35.0"),
        ]);
        var dictionary = new IcdDictionarySnapshot([
            Dict("H35.000X005", "视网膜病"),
            Dict("H36.000X001", "糖尿病性视网膜病变"),
        ]);
        var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");

        Assert.Equal("H35.000X005", resolver.Resolve(snapshot, dictionary, "--双眼高血压视网膜病变")[0].Code);
    }
}
