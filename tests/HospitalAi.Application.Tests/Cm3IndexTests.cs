using HospitalAi.Application.Coding.Cm3Index;
using HospitalAi.Application.Coding.IcdIndex;
using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Tests;

/// <summary>
/// 手术名称按 CM3 索引库推荐链路的纯逻辑单测：词条解析（一字线指针、见亚目、主[附] 双码）、
/// 主导词双轨判据与召回、修饰词下钻与连接行透明化、索引码→手术字典细码展开与两级回退。
/// 夹具取自已验证的真实数据行（cm3_index seq 11/110/2695/2955/3004/3005/4243/7836/7837/14930-14956/15484-16100，
/// medical_code ICD-9-CM-3 标题）。
/// </summary>
public sealed class Cm3IndexTests
{
    private static Cm3IndexRow Row(
        int seqNo, int level, int parentSeqNo, string name, string? code = null, int status = 0) =>
        new(seqNo, name, code, level, parentSeqNo, status);

    private static IcdDictionaryRow Dict(string code, string title) =>
        new("ICD-9-CM-3", code, title, title);

    /// <summary>吻合术 子树真实切片（主导词无码，靠子行取码；含 与 纯连接行与自我指针）。</summary>
    private static Cm3IndexSnapshot AnastomosisSnapshot() => Cm3IndexSnapshotBuilder.Build([
        Row(14930, 0, 0, "吻合术"),
        Row(14932, 1, 14930, "―肠―另见吻合术,肠 45.90", "45.90"),
        Row(14933, 1, 14930, "―肠 45.90", "45.90"),
        Row(14934, 2, 14933, "――大肠与大肠 45.94", "45.94"),
        Row(14943, 1, 14930, "―胆管 51.39", "51.39"),
        Row(14944, 1, 14930, "―胆囊 51.35", "51.35"),
        Row(14945, 2, 14944, "――与"),
        Row(14946, 3, 14945, "―――肠 51.32", "51.32"),
        Row(14947, 3, 14945, "―――肝管 51.31", "51.31"),
        Row(14948, 3, 14945, "―――胃 51.34", "51.34"),
        Row(14949, 3, 14945, "―――胰 51.33", "51.33"),
    ]);

    /// <summary>真实 ICD-9-CM-3 字典切片（含 X 占位详码与展不开码族）。</summary>
    private static IcdDictionarySnapshot ProcedureDictionary() => new([
        Dict("45.9000", "肠吻合术"),
        Dict("51.2200", "胆囊切除术"),
        Dict("51.2200X004", "胆囊扩大切除术"),
        Dict("51.2201", "残余胆囊切除术"),
        Dict("51.2300", "腹腔镜下胆囊切除术"),
        Dict("51.3100", "胆囊肝管吻合术"),
        Dict("51.3200", "胆囊肠吻合术"),
        Dict("51.3200X001", "胆囊-结肠吻合术"),
        Dict("51.3201", "胆囊空肠吻合术"),
        Dict("51.3202", "胆囊十二指肠吻合术"),
        Dict("51.3203", "腹腔镜下胆囊空肠吻合术"),
        Dict("51.3300", "胆囊胰腺吻合术"),
        Dict("51.3400", "胆囊胃吻合术"),
        Dict("51.3500", "其他胆囊吻合术"),
        Dict("51.3900", "其他胆管吻合术"),
        Dict("17.4100", "开放性机器人援助操作"),
        Dict("17.4200", "腹腔镜机器人援助操作"),
        Dict("17.4300", "经皮机器人援助操作"),
        Dict("17.4400", "内镜机器人援助操作"),
        Dict("17.4900", "其他和未特指的机器人援助操作"),
        Dict("17.4901", "机器人辅助康复"),
        Dict("38.1000", "动脉内膜切除术"),
        Dict("38.1000X002", "动脉内膜剥脱术"),
        Dict("38.6000", "血管的其他切除术"),
        Dict("38.6000X010", "静脉内异物取出术"),
        Dict("38.6000X011", "躯干部血管瘤切除术"),
        Dict("38.6000X012", "血管病损切除术"),
        Dict("38.6000X013", "血管球瘤切除术"),
        Dict("43.6X00", "胃部分切除术伴胃十二指肠吻合术"),
        Dict("43.6X00X005", "胃幽门切除术伴胃-十二指肠吻合术"),
        Dict("43.6X00X006", "胃远端切除术伴胃-十二指肠吻合术"),
        Dict("43.6X01", "胃大部切除伴胃十二指肠吻合术"),
        Dict("43.6X02", "腹腔镜胃大部切除伴胃十二指肠吻合术"),
        Dict("53.0000", "腹股沟疝单侧修补术"),
        Dict("53.0001", "单侧腹股沟疝修补术"),
        Dict("53.0002", "腹腔镜下单侧腹股沟疝修补术"),
        Dict("53.0100", "其他和开放性腹股沟直疝修补术"),
        Dict("53.0200", "其他和开放性腹股沟斜疝修补术"),
        Dict("53.0300", "用移植物或假体的其他和开放性腹股沟直疝修补术"),
        Dict("71.5X00", "根治性外阴切除术"),
        Dict("71.5X00X001", "外阴广泛性切除术"),
        Dict("71.5X00X003", "外阴根治性局部扩大切除术"),
        Dict("71.5X00X004", "外阴根治性局部切除术"),
    ]);

    private static Cm3ProcedureIndexResolver Resolver(int leadWordTopK = 8, int maxResults = 3) =>
        new(leadWordTopK, maxResults);

    [Fact]
    public void 黄金用例_胆囊肠吻合术走主导词下钻推荐字典细码()
    {
        var results = Resolver().Resolve(AnastomosisSnapshot(), ProcedureDictionary(), "胆囊肠吻合术");

        // 名额内可以带同族的空肠/十二指肠吻合术，但逐字命中的标准细码必须排第一且不能被兜底码挤掉。
        Assert.NotEmpty(results);
        var top = results[0];
        Assert.Equal("51.3200", top.Code);
        Assert.Equal("胆囊肠吻合术", top.Title);
        Assert.False(top.IsIndexFallback);
        Assert.True(top.WholeTitleDepth > 0m);
        Assert.Equal(0, top.FamilyRank);
        Assert.Contains("吻合术 → 胆囊", top.EvidencePath);
        Assert.Contains("索引编码: 51.32", top.EvidencePath);
        Assert.All(results, item => Assert.StartsWith("51.32", item.Code, StringComparison.Ordinal));
    }

    [Fact]
    public void 真实链路_自我指针行不死循环_肠吻合术出码()
    {
        var results = Resolver().Resolve(AnastomosisSnapshot(), ProcedureDictionary(), "肠吻合术");

        var top = Assert.Single(results);
        Assert.Equal("45.9000", top.Code);
        Assert.False(top.IsIndexFallback);
    }

    [Fact]
    public void 一字线指针被识别_并保留本行编码()
    {
        // 库内指针段 1201/1204 条写成 U+2015 "―见"，照抄诊断侧字符集会整条认不出来。
        var node = Cm3IndexSnapshotBuilder.ParseNode(Row(14932, 1, 14930, "―肠―另见吻合术,肠 45.90", "45.90"));

        Assert.Equal("肠", node.Term);
        Assert.Equal(Cm3JumpKind.SeeAlso, node.JumpKind);
        Assert.Equal("45.90", node.Code);
        Assert.StartsWith("吻合术", node.JumpTarget);
    }

    [Fact]
    public void 见亚目正写与反写都取到亚目码()
    {
        var forward = Cm3IndexSnapshotBuilder.ParseNode(
            Row(11, 0, 0, "AESOP?可视性自动定位内镜系统―见亚目17.4", "17.4"));
        var reverse = Cm3IndexSnapshotBuilder.ParseNode(
            Row(4243, 2, 4242, "――骨―见 78.2 亚目缩短,骨或 78.3 亚目,延长,骨"));

        Assert.Equal(Cm3JumpKind.SubEntry, forward.JumpKind);
        Assert.Equal("AESOP?可视性自动定位内镜系统", forward.Term);
        Assert.Equal("17.4", forward.Code);
        Assert.Equal(Cm3JumpKind.SubEntry, reverse.JumpKind);
        Assert.Equal("骨", reverse.Term);
        Assert.Equal("78.2", reverse.SubEntryCode);
        Assert.Equal("78.2", reverse.Code);
    }

    [Fact]
    public void 主附双码只解析附加码不进编码产出()
    {
        var node = Cm3IndexSnapshotBuilder.ParseNode(
            Row(110, 0, 0, "Bassett 手术（女阴切除术伴腹股沟淋巴结清扫）71.5[40.3]"));

        Assert.Equal("71.5", node.Code);
        Assert.Equal("40.3", node.AddOnCode);
        Assert.DoesNotContain("71.5", node.Term);
    }

    [Fact]
    public void 括注别名只作同义写法不作召回锚点()
    {
        var node = Cm3IndexSnapshotBuilder.ParseNode(
            Row(1889, 1, 1888, "― VH TM IVUS[ 虚拟组织学血管内超声] ―见超声,血管内的,按部位"));

        Assert.Equal(Cm3JumpKind.See, node.JumpKind);
        var bracket = Assert.Single(node.Aliases, alias => alias.RecallAnchor == false);
        Assert.Equal("虚拟组织学血管内超声", bracket.Key);
        Assert.All(node.Aliases.Where(alias => alias != bracket), alias => Assert.True(alias.RecallAnchor));
    }

    [Fact]
    public void NEC_控制标记在别名里剥掉但保留在证据文本()
    {
        var node = Cm3IndexSnapshotBuilder.ParseNode(Row(14952, 1, 14930, "―动静脉 NEC 39.29", "39.29"));

        Assert.Equal("39.29", node.Code);
        Assert.Contains("动静脉", node.Aliases.Select(alias => alias.Key));
        Assert.All(node.Aliases, alias => Assert.DoesNotContain("nec", alias.Key, StringComparison.Ordinal));
    }

    [Fact]
    public void 主导词双轨_词面有见指针即判失效即使状态列为零()
    {
        var snapshot = Cm3IndexSnapshotBuilder.Build([
            Row(3004, 0, 0, "动脉瘤切开术―见动脉瘤切除术", status: 1),
            Row(2955, 0, 0, "动脉瘤切除术 38.60", "38.60"),
            Row(207, 0, 0, "瓣膜切除术,心脏―见瓣膜成形术,心脏"),
        ]);

        Assert.Equal("动脉瘤切除术", Assert.Single(snapshot.Leads).Term);
        Assert.Equal("动脉瘤切除术", snapshot.Redirects["动脉瘤切开术"]);
        // 逗号分段行 "瓣膜切除术,心脏" 的两段都登记为失效主导词别名。
        Assert.Equal("瓣膜成形术,心脏", snapshot.Redirects["瓣膜切除术"]);
        // 207 词面有 见 指针但状态列为 0 —— 实测 154 条同形，台账以词面为准。
        Assert.Equal(1, snapshot.LeadStatusDisagreements);
        // 命中主导词即终点是本库常态（实测 1945 条无子节点，其中 1391 条自带编码）。
        Assert.Equal(1, snapshot.DeadEndLeads);
    }

    [Fact]
    public void 指针目标按逗号分段定位到主导词_泛词段不参与()
    {
        var snapshot = Cm3IndexSnapshotBuilder.Build([
            Row(15484, 0, 0, "修补术"),
            Row(1888, 0, 0, "超声"),
            Row(8761, 0, 0, "切除术"),
        ]);

        Assert.Equal("修补术", Cm3ProcedureIndexMatcher.ResolveJumpLead(snapshot, "修补术,疝,腹股沟的")?.Term);
        Assert.Equal("超声", Cm3ProcedureIndexMatcher.ResolveJumpLead(snapshot, "超声,血管内的,按部位")?.Term);
        Assert.True(Cm3ProcedureIndexMatcher.IsGenericLeadKey("按部位"));
        // 切除术 / 修补术 是 CM3 真正的术式主导词，不能按诊断侧泛词表拉黑。
        Assert.False(Cm3ProcedureIndexMatcher.IsGenericLeadKey("切除术"));
        Assert.False(Cm3ProcedureIndexMatcher.IsGenericLeadKey("吻合术"));
    }

    [Fact]
    public void 见一_失效主导词改道到目标树取细码()
    {
        var snapshot = Cm3IndexSnapshotBuilder.Build([
            Row(2695, 0, 0, "胆囊切除术（全部） 51.22", "51.22"),
            Row(7836, 0, 0, "囊切除术―另见切除术,病损,按部位"),
            Row(7837, 1, 7836, "―胆囊―见胆囊切除术"),
        ]);

        var results = Resolver().Resolve(snapshot, ProcedureDictionary(), "胆囊切除术");

        var top = results[0];
        Assert.Equal("51.2200", top.Code);
        Assert.False(top.IsIndexFallback);
        Assert.All(results, item => Assert.StartsWith("51.22", item.Code, StringComparison.Ordinal));
    }

    [Fact]
    public void 连接行被识别且不挣编码()
    {
        var snapshot = AnastomosisSnapshot();
        var 与 = snapshot.Leads.SelectMany(Flat).First(node => node.Term == "与");
        var 胆囊 = snapshot.Leads.SelectMany(Flat).First(node => node.Term == "胆囊");

        Assert.True(Cm3ProcedureIndexMatcher.IsConnectorNode(与));
        Assert.False(Cm3ProcedureIndexMatcher.IsConnectorNode(胆囊));
    }

    [Fact]
    public void 下钻穿过连接行停在真正挣到内容的子行()
    {
        var snapshot = AnastomosisSnapshot();
        var hit = Cm3ProcedureIndexMatcher
            .RecallLeads(snapshot, TextMatch.NormalizeKey("胆囊肠吻合术"), topK: 8)
            .First(item => item.Node.Term == "吻合术");

        var drill = Cm3ProcedureIndexMatcher.Drill(
            snapshot, hit, TextMatch.NormalizeKey("胆囊肠吻合术"));

        Assert.NotNull(drill);
        Assert.Equal("51.32", drill!.CodeNode.Code);
        Assert.False(drill.CodeFromAncestorFallback);
        Assert.Equal(
            "吻合术 → 胆囊 → 与 → 肠",
            string.Join(" → ", drill.Path.Select(node => node.Term)));
    }

    [Fact]
    public void 死路主导词带码即出码_不判为弱证据()
    {
        var snapshot = Cm3IndexSnapshotBuilder.Build([
            Row(3005, 0, 0, "动脉内膜切除术（气体）（伴补片移植） 38.10", "38.10"),
        ]);
        Assert.Equal(1, snapshot.DeadEndLeads);

        var results = Resolver().Resolve(snapshot, ProcedureDictionary(), "动脉内膜切除术");

        var top = Assert.Single(results);
        Assert.Equal("38.1000", top.Code);
        Assert.False(top.IsIndexFallback);
        // 兜底惩罚只打在"没拿到本行真码"的链路上；带码死路主导词的系数取 1.0。
        Assert.True(top.Score > 0.8m, $"死路主导词被降权：Score={top.Score}");
    }

    [Fact]
    public void 索引码前缀按字面长度展开_吃到字典的X占位详码()
    {
        var expanded = Cm3IndexCodeExpander.Expand(
            "71.5", ProcedureDictionary(), "女阴切除术伴腹股沟淋巴结清扫", "女阴切除术");

        Assert.NotEmpty(expanded);
        Assert.All(expanded, item => Assert.StartsWith("71.5X", item.Code, StringComparison.Ordinal));
    }

    [Fact]
    public void 同族扇出受展开上限约束()
    {
        var expanded = Cm3IndexCodeExpander.Expand(
            "38.60", ProcedureDictionary(), "血管病损切除术", "动脉瘤切除术");

        Assert.InRange(expanded.Count, 1, Cm3IndexCodeExpander.MaxExpandedCodes);
        Assert.Equal(
            expanded.Select(item => item.FamilyRank),
            Enumerable.Range(0, expanded.Count));
    }

    [Fact]
    public void 同前缀有码但门控全落空_回退同族最泛码()
    {
        var expanded = Cm3IndexCodeExpander.Expand("53.0", ProcedureDictionary(), "Bassini手术", "修补术疝");

        var only = Assert.Single(expanded);
        Assert.True(only.IsIndexFallback);
        Assert.Equal("53.0000", only.Code);
        Assert.Contains("无门控命中", only.Note);
    }

    [Fact]
    public void 索引码在字典里一行都没有_回退索引编码本身()
    {
        // 37.17 / 40.92 / 64.69 实测属知识库覆盖缺口（方案 §6.5-5），按 E 类排除台账处理。
        var expanded = Cm3IndexCodeExpander.Expand("37.17", ProcedureDictionary(), "主动脉与冠状动脉吻合术", "吻合术");

        var only = Assert.Single(expanded);
        Assert.True(only.IsIndexFallback);
        Assert.Equal("37.17", only.Code);
        Assert.Contains("无展开结果", only.Note);
    }

    [Fact]
    public void 非法码形不展开()
    {
        Assert.Empty(Cm3IndexCodeExpander.Expand(null, ProcedureDictionary(), "胆囊肠吻合术", "吻合术"));
        Assert.Empty(Cm3IndexCodeExpander.Expand("5132", ProcedureDictionary(), "胆囊肠吻合术", "吻合术"));
        Assert.Empty(Cm3IndexCodeExpander.Expand("0", ProcedureDictionary(), "胆囊肠吻合术", "吻合术"));
    }

    [Fact]
    public void 见亚目行按本行亚目码展开()
    {
        var snapshot = Cm3IndexSnapshotBuilder.Build([
            Row(11, 0, 0, "AESOP?可视性自动定位内镜系统―见亚目17.4", "17.4"),
        ]);

        var results = Resolver().Resolve(
            snapshot, ProcedureDictionary(), "AESOP?可视性自动定位内镜系统");

        Assert.NotEmpty(results);
        Assert.All(results, item => Assert.StartsWith("17.4", item.Code, StringComparison.Ordinal));
    }

    [Fact]
    public void 主附双码的附加码只进证据说明不出码()
    {
        var snapshot = Cm3IndexSnapshotBuilder.Build([
            Row(110, 0, 0, "Bassett 手术（女阴切除术伴腹股沟淋巴结清扫）71.5[40.3]"),
        ]);

        var results = Resolver().Resolve(snapshot, ProcedureDictionary(), "女阴切除术伴腹股沟淋巴结清扫");

        Assert.NotEmpty(results);
        Assert.All(results, item => Assert.StartsWith("71.5", item.Code, StringComparison.Ordinal));
        Assert.All(results, item => Assert.Contains("附加码 40.3", item.Note));
    }

    [Fact]
    public void 术式通用词手术不被当成部位限定词()
    {
        // BodyQualifiers 是单字词表（手/骨/肌/腺…），而 "X 手术（Y）" 是本库给人名术式的命名惯例
        // （Noble 手术 seq 7825 / Bassett 手术 seq 110），不抹掉 手术 就让 手 冒充部位限定词，
        // 整族 eponym 主导词被 NodeAllows 挡在召回之外。
        var snapshot = Cm3IndexSnapshotBuilder.Build([
            Row(110, 0, 0, "Bassett 手术（女阴切除术伴腹股沟淋巴结清扫）71.5[40.3]"),
            Row(7825, 0, 0, "Noble 手术（小肠折叠术） 46.62", "46.62"),
        ]);

        // 抹掉的是 手术 这个整词，真实部位写法仍然留得住：手部手术 → 手。
        Assert.Empty(Cm3SiteQualifiers.Extract("Noble 手术（小肠折叠术）"));
        Assert.Contains("手", Cm3SiteQualifiers.Extract("手部手术"));

        Assert.Equal(2, snapshot.Leads.Count);
        Assert.NotEmpty(Resolver().Resolve(
            snapshot, ProcedureDictionary(), "女阴切除术伴腹股沟淋巴结清扫"));
    }

    [Fact]
    public void 输入尾部括号里的院内编码与术式编号被剥离()
    {
        var snapshot = AnastomosisSnapshot();

        Assert.Equal(
            Resolver().Resolve(snapshot, ProcedureDictionary(), "胆囊肠吻合术"),
            Resolver().Resolve(snapshot, ProcedureDictionary(), "胆囊肠吻合术（51.3200）"),
            new RecommendationComparer());
        Assert.Equal(
            Resolver().Resolve(snapshot, ProcedureDictionary(), "胆囊肠吻合术"),
            Resolver().Resolve(snapshot, ProcedureDictionary(), "1.胆囊肠吻合术"),
            new RecommendationComparer());
    }

    [Fact]
    public void 单字输入不产出索引推荐()
    {
        // 库里只有 根/骨/血 三条单字 0 级行且全部无自码，单字要靠子行取码又挣不到新内容；
        // 覆盖率门控按别名长度做分母，也不会让 "肠" 锚到 吻合术。
        var snapshot = Cm3IndexSnapshotBuilder.Build([
            Row(14930, 0, 0, "吻合术"),
            Row(14933, 1, 14930, "―肠 45.90", "45.90"),
            Row(4417, 0, 0, "骨"),
        ]);

        Assert.Empty(Resolver().Resolve(snapshot, ProcedureDictionary(), "肠"));
        Assert.Empty(Resolver().Resolve(snapshot, ProcedureDictionary(), "骨"));
    }

    [Fact]
    public void 无主导词命中时索引通道返回空不回退字典直匹配()
    {
        var results = Resolver().Resolve(AnastomosisSnapshot(), ProcedureDictionary(), "经皮冠状动脉介入治疗");

        Assert.Empty(results);
    }

    [Fact]
    public void 同层兄弟部位词不被串用()
    {
        var snapshot = AnastomosisSnapshot();

        var results = Resolver().Resolve(snapshot, ProcedureDictionary(), "胆囊胃吻合术");

        Assert.NotEmpty(results);
        Assert.Equal("51.3400", results[0].Code);
        Assert.DoesNotContain(results, item => item.Code == "51.3200");
        Assert.DoesNotContain(results, item => item.Code == "51.3300");
    }

    /// <summary>「部位+术式」整词死路行（鼓膜切开术 20.09 自码）与正确的主导词子树同场竞技。</summary>
    private static Cm3IndexSnapshot MembraneSnapshot() => Cm3IndexSnapshotBuilder.Build([
        Row(4561, 0, 0, "鼓膜切开术（伴抽吸）（伴引流） 20.09", "20.09"),
        Row(10318, 0, 0, "切开（和引流）"),
        Row(10501, 1, 10318, "―脑膜（脑的） 01.31", "01.31"),
    ]);

    private static IcdDictionarySnapshot MembraneDictionary() => new([
        Dict("20.0901", "鼓膜切开引流术"),
        Dict("01.3107", "脑膜切开引流术"),
    ]);

    [Fact]
    public void 输入写明部位后别部位的整词主导词不再锚定()
    {
        var results = Resolver().Resolve(MembraneSnapshot(), MembraneDictionary(), "脑膜切开引流术");

        Assert.Equal("01.3107", Assert.Single(results).Code);
    }

    [Fact]
    public void 输入未声明部位时整词主导词照旧可用()
    {
        // 只缺不判：输入一个部位词都没写，不能因为主导词行自带部位前缀就把唯一链路打掉。
        // 隐眼摘除术 只能靠 眼球摘除术 这条整词死路行拿到 16.4901，"只缺也判"把它打空过（实测硬线 1 条）。
        var snapshot = Cm3IndexSnapshotBuilder.Build([
            Row(17156, 0, 0, "眼球摘除术 16.49", "16.49"),
        ]);
        var dict = new IcdDictionarySnapshot([
            Dict("16.4900", "眼球其他摘除术"),
            Dict("16.4900X001", "眼球摘除术"),
            Dict("16.4901", "隐眼摘除术"),
        ]);

        var results = Resolver().Resolve(snapshot, dict, "隐眼摘除术");

        Assert.Equal("16.4901", results[0].Code);
    }

    [Fact]
    public void 整词主导词被挡后不补发别部位码()
    {
        // 门控打掉的是"术式同名、部位无关"的整词行本身：输入只写 骨折 而不写 鼓膜 时，
        // 宁可空推荐（实测这类打空 D_CM3 面 82 条、病历面 25 条，无一丢命中码），也不留 20.0901。
        var results = Resolver().Resolve(MembraneSnapshot(), MembraneDictionary(), "骨折切开引流术");

        Assert.Empty(results);
    }

    /// <summary>超声 子树真实切片（seq 1888-1917）：治疗性 下 头/颈/头和颈血管 同为 00.01，周围血管 00.03。</summary>
    private static Cm3IndexSnapshot UltrasoundSnapshot() => Cm3IndexSnapshotBuilder.Build([
        Row(1888, 0, 0, "超声"),
        Row(1890, 1, 1888, "―疗法 93.35", "93.35"),
        Row(1895, 1, 1888, "―心脏"),
        Row(1896, 2, 1895, "――非侵入的 88.72", "88.72"),
        Row(1900, 1, 1888, "―血管内（IVUS） 00.29", "00.29"),
        Row(1907, 2, 1900, "――周围血管 00.23", "00.23"),
        Row(1911, 1, 1888, "―治疗性"),
        Row(1912, 2, 1911, "――颈 00.01", "00.01"),
        Row(1913, 2, 1911, "――其他治疗性超声 00.09", "00.09"),
        Row(1914, 2, 1911, "――头 00.01", "00.01"),
        Row(1915, 2, 1911, "――头和颈血管 00.01", "00.01"),
        Row(1916, 2, 1911, "――心脏 00.02", "00.02"),
        Row(1917, 2, 1911, "――周围血管 00.03", "00.03"),
    ]);

    private static IcdDictionarySnapshot UltrasoundDictionary() => new([
        Dict("00.0100", "头和颈部血管治疗性超声"),
        Dict("00.0101", "头部血管治疗性超声"),
        Dict("00.0102", "颈部血管治疗性超声"),
        Dict("00.0200", "心脏治疗性超声"),
        Dict("00.0300", "周围血管治疗性超声"),
        Dict("00.0900", "其他治疗性超声"),
        Dict("93.3500", "超声疗法"),
    ]);

    [Theory]
    [InlineData("头和颈部血管治疗性超声", "00.0100", "头和颈血管")]
    [InlineData("头部血管治疗性超声", "00.0101", "头")]
    [InlineData("颈部血管治疗性超声", "00.0102", "颈")]
    public void 部位兄弟行不串用_病历写哪个部位走哪个兄弟行(string input, string expected, string pathNode)
    {
        // 索引 治疗性 下把 头/颈/头和颈血管 并列成三行（同为 00.01）。病历只写 头 或 颈 时，
        // 「本行部位词输入必须全写」把 头和颈血管 整行剔除；此时 周围血管 00.03 靠只沾上义词 血管
        // 的两个字增益，会压过逐字写出的 头/颈（一个字）。部位冲突门控把「本行另写了输入没写的
        // 周围」的兄弟行剔掉，三个部位各走各的兄弟行。
        var results = Resolver().Resolve(UltrasoundSnapshot(), UltrasoundDictionary(), input);

        Assert.Equal(expected, results[0].Code);
        Assert.Contains($"→ {pathNode} | 索引编码: 00.01", results[0].EvidencePath);
        Assert.DoesNotContain(results, item => item.Code == "00.0300");
    }

    [Theory]
    [InlineData("周围血管治疗性超声", "00.0300")]
    [InlineData("心脏治疗性超声", "00.0200")]
    public void 单部位兄弟行照旧各取各码(string input, string expected)
    {
        var results = Resolver().Resolve(UltrasoundSnapshot(), UltrasoundDictionary(), input);

        Assert.Equal(expected, results[0].Code);
    }

    /// <summary>只比编码与回退标志：两条链路的分数会随路径长度浮动，断言等价的输入面收敛到同一批编码即可。</summary>
    private sealed class RecommendationComparer : IEqualityComparer<ProcedureIndexRecommendation>
    {
        public bool Equals(ProcedureIndexRecommendation? left, ProcedureIndexRecommendation? right) =>
            left?.Code == right?.Code && left?.IsIndexFallback == right?.IsIndexFallback;

        public int GetHashCode(ProcedureIndexRecommendation item) =>
            HashCode.Combine(item.Code, item.IsIndexFallback);
    }

    private static IEnumerable<Cm3IndexNode> Flat(Cm3IndexNode node)
    {
        yield return node;
        foreach (var descendant in node.Children.SelectMany(Flat))
        {
            yield return descendant;
        }
    }
}
