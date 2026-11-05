using System.Text;
using HospitalAi.Application.Coding.IcdIndex;
using HospitalAi.Application.Coding.Matching;
using HospitalAi.Application.Coding.NameLookup;
using Microsoft.Data.SqlClient;

// 编码录入·全量细码导出（诊断侧）：
//   从 medical_code 筛 ICD-10 细码（编码后两位≠00、不含 +/*/组合码），
//   逐名走 NameCodingLookupEngine 同款拆分/下钻/展开（编码录入生产口径），
//   输出 TSV：每名一节（全量命中主导词 + 修饰词链路 + 展开码 + 可匹配性判定）。
// 用法：NameDetailExport <输出TSV> [医院ID] [上限条数]
// 环境变量：DETAIL_EXPORT_CONN（连接串，必填）
var outPath = args.Length > 0 ? args[0] : "detail-export.tsv";
var hospital = args.Length > 1 ? args[1] : "ff840089-ea7d-4c40-bb4c-5d47b4b49956";
var limit = args.Length > 2 && int.TryParse(args[2], out var parsedLimit) ? parsedLimit : int.MaxValue;
var connString = Environment.GetEnvironmentVariable("DETAIL_EXPORT_CONN")
    ?? throw new InvalidOperationException("请先设置 DETAIL_EXPORT_CONN 连接字符串。");
var codeSystem = "ICD-10";

Console.Error.WriteLine("加载索引快照…");
var snapshot = BuildSnapshot(connString);
Console.Error.WriteLine($"索引 {snapshot.TotalNodes} 行，有效主导词 {snapshot.Leads.Count}。");
var dictionary = BuildDictionary(connString, hospital, codeSystem);
Console.Error.WriteLine($"字典 {dictionary.Count} 行。");
Console.Error.WriteLine($"Redirects 总数 {snapshot.Redirects.Count}；含 肠炎={snapshot.Redirects.ContainsKey("肠炎")}；含 肠胃炎={snapshot.Redirects.ContainsKey("肠胃炎")}。");
foreach (var probe in new[] { "肠炎", "肠胃炎" })
{
    if (snapshot.Redirects.TryGetValue(probe, out var target))
    {
        Console.Error.WriteLine($"  {probe} → {target}；目标可解析={snapshot.LeadByExactKey.ContainsKey(TextMatch.NormalizeKey(target))}");
    }
}

var probeKey = Environment.GetEnvironmentVariable("DETAIL_EXPORT_PROBE") is { Length: > 0 } probeText
    ? TextMatch.NormalizeKey(probeText)
    : TextMatch.NormalizeKey("毗邻单胞菌肠炎");
var probeHits = NameLeadTermSplitter.SplitIcd(snapshot, probeKey, 16);
Console.Error.WriteLine($"SplitIcd('{probeKey}') 命中 {probeHits.Count}：");
foreach (var probeHit in probeHits)
{
    Console.Error.WriteLine($"  [{probeHit.Term}] alias={probeHit.HitAlias} redirect={probeHit.RedirectKey ?? "-"}");
}
if (snapshot.LeadByExactKey.TryGetValue("结核", out var tbLead))
{
    Console.Error.WriteLine($"结核 LeadByExactKey 命中: seq={tbLead.SeqNo} term={tbLead.Term} code={tbLead.Code}");
    foreach (var alias in tbLead.Aliases)
    {
        Console.Error.WriteLine($"    alias={alias.Key} w={alias.Weight} anchor={alias.RecallAnchor}");
    }
}
else
{
    Console.Error.WriteLine("结核 不在 LeadByExactKey。");
}

var codes = LoadDetailCodes(connString, hospital, codeSystem, limit);
Console.Error.WriteLine($"待导出细码 {codes.Count} 条。");

using var writer = new StreamWriter(outPath, append: false, new UTF8Encoding(false));
writer.WriteLine(string.Join('\t',
    "序号", "编码", "诊断名称", "可匹配", "匹配方式",
    "全量命中主导词数", "主导词(显示词面)", "命中的别名键",
    "修饰词链路(索引树原样)", "链路索引编码", "字典展开码",
    "Top1编码", "Top1标题", "Top1是否回退", "Top1链路",
    "不可匹配原因"));

var index = 0;
var matched = 0;
foreach (var (code, title) in codes)
{
    index++;
    var response = NameCodingLookupEngine.Lookup(
        NameCodingLookupEngine.DiagnosisKind, title, topK: 8,
        icdSnapshot: snapshot, tumorSnapshot: null, icdDictionary: dictionary,
        cm3Snapshot: null, cm3Dictionary: null,
        new NameLookupTitleChannels(IcdTitleDirect: false, Cm3TitleDirect: false, IcdTitleReverse: true));

    var leads = response.LeadTerms;
    var recs = response.Recommendations;

    // 可匹配判定：Top1 为非回退码 且 码值落在对照码/其细码族上（对照码=该细码自身）。
    var top1 = recs.FirstOrDefault();
    var isMatch = top1 is { IsIndexFallback: false } &&
        (string.Equals(Normalize(top1.Code), Normalize(code), StringComparison.OrdinalIgnoreCase)
            || Normalize(top1.Code).StartsWith(Normalize(code)[..3], StringComparison.OrdinalIgnoreCase));
    if (isMatch)
    {
        matched++;
    }

    var matchMode = top1 is null
        ? "无推荐"
        : top1.DictionaryDirect ? "字典标题直达"
        : top1.IsIndexFallback ? "索引回退/反查"
        : string.Equals(Normalize(top1.Code), Normalize(code), StringComparison.OrdinalIgnoreCase) ? "索引链路精确"
        : "索引链路同族";
    var reason = isMatch
        ? ""
        : top1 is null
            ? (leads.Count == 0 ? "无主导词召回" : "链路无产出")
            : top1.IsIndexFallback ? "链路展开为回退码" : "链路码与对照码不同族";

    var leadDisplay = string.Join("；", leads.Select(lead =>
    {
        var d = lead.DisplayTerm ?? lead.Term;
        return lead.RedirectKey is null ? d : $"{lead.RedirectKey}→{d}";
    }));
    var aliasKeys = string.Join("；", leads.Select(lead => lead.HitAlias));
    var pathText = string.Join("；", leads.SelectMany(lead => lead.ModifierPaths)
        .Select(path => string.Join("→", path.Path)));
    var pathCodes = string.Join("；", leads.SelectMany(lead => lead.ModifierPaths)
        .Select(path => path.IndexCode ?? "无"));
    var expandedText = string.Join("；", leads.SelectMany(lead => lead.ModifierPaths)
        .SelectMany(path => path.Codes)
        .Select(code1 => code1.Code + (code1.IsIndexFallback ? "(fb)" : ""))
        .Distinct());
    var top1Path = top1 is null ? "" : string.Join("→", top1.Path);

    writer.WriteLine(string.Join('\t',
        index, code, title, isMatch ? "✓" : "✗", matchMode,
        leads.Count, leadDisplay, aliasKeys,
        pathText, pathCodes, expandedText,
        top1?.Code ?? "", top1?.Title ?? "", top1 is { IsIndexFallback: true } ? 1 : 0, top1Path,
        reason));

    if (index % 1000 == 0)
    {
        Console.Error.WriteLine($"… {index}（可匹配 {matched}）");
    }
}

Console.Error.WriteLine($"完成：{index} 条，可匹配 {matched}（{matched * 100.0 / Math.Max(1, index):F1}%）。输出 {outPath}");
return 0;

static string Normalize(string code) => code.TrimEnd('*').ToUpperInvariant();

static List<IcdIndexRow> BuildRows(string conn)
{
    using var sql = new SqlConnection(conn);
    sql.Open();
    using var cmd = new SqlCommand(
        "SELECT seq_no, index_name, index_code, index_level, parent_seq_no, lead_word_status FROM icd_index", sql);
    using var reader = cmd.ExecuteReader();
    var rows = new List<IcdIndexRow>();
    while (reader.Read())
    {
        rows.Add(new IcdIndexRow(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5)));
    }

    return rows;
}

static IcdIndexSnapshot BuildSnapshot(string conn) => IcdIndexSnapshotBuilder.Build(BuildRows(conn));

static IcdDictionarySnapshot BuildDictionary(string conn, string hospital, string codeSystem)
{
    using var sql = new SqlConnection(conn);
    sql.Open();
    using var cmd = new SqlCommand(
        """
        SELECT mc.code_system_code, mc.code, mc.title, mc.search_text
        FROM medical_code mc
        LEFT JOIN code_system cs ON cs.id = mc.code_system_id
        WHERE mc.is_enabled = 1
          AND (mc.hospital_id = @hospital OR cs.hospital_id = @hospital)
          AND mc.code_system_code = @codeSystem
        """, sql);
    cmd.Parameters.AddWithValue("@hospital", Guid.Parse(hospital));
    cmd.Parameters.AddWithValue("@codeSystem", codeSystem);
    using var reader = cmd.ExecuteReader();
    var rows = new List<IcdDictionaryRow>();
    while (reader.Read())
    {
        rows.Add(new IcdDictionaryRow(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
    }

    return new IcdDictionarySnapshot(rows);
}

static List<(string Code, string Title)> LoadDetailCodes(string conn, string hospital, string codeSystem, int limit)
{
    using var sql = new SqlConnection(conn);
    sql.Open();
    // 与编码录入/流水线同口径：按医院隔离取字典（本院 medical_code ∪ 本院 code_system 归属），
    // 并对 code+title 去重（两院副本/导入重复行只导一条）。
    using var cmd = new SqlCommand(
        """
        SELECT DISTINCT mc.code, mc.title
        FROM medical_code mc
        LEFT JOIN code_system cs ON cs.id = mc.code_system_id
        WHERE mc.is_enabled = 1
          AND (mc.hospital_id = @hospital OR cs.hospital_id = @hospital)
          AND mc.code_system_code = @codeSystem
          AND RIGHT(mc.code, 2) <> '00'
          AND mc.code NOT LIKE '%+%' AND mc.code NOT LIKE '%*%'
        ORDER BY mc.code
        """, sql);
    cmd.Parameters.AddWithValue("@hospital", Guid.Parse(hospital));
    cmd.Parameters.AddWithValue("@codeSystem", codeSystem);
    using var reader = cmd.ExecuteReader();
    var rows = new List<(string, string)>();
    while (reader.Read() && rows.Count < limit)
    {
        rows.Add((reader.GetString(0), reader.GetString(1)));
    }

    return rows;
}
