using System.Text;
using HospitalAi.Application.Coding.Cm3Index;
using HospitalAi.Application.Coding.IcdIndex;
using HospitalAi.Application.Coding.Matching;
using Microsoft.Data.SqlClient;

// 索引链路单例复算：ICD_INDEX_DIAG_CONN 必填，用于定位门控误杀/漏放的具体环节。
var conn = Environment.GetEnvironmentVariable("ICD_INDEX_DIAG_CONN")
    ?? throw new InvalidOperationException("请先设置 ICD_INDEX_DIAG_CONN 连接字符串。");
var hospital = Environment.GetEnvironmentVariable("ICD_INDEX_DIAG_HOSPITAL")
    ?? "868F2319-9A0C-46C8-B478-C78FB350237D";
var codeSystem = "ICD-10";

if (args.Length == 0)
{
    Console.Error.WriteLine("用法: IcdIndexDiag <诊断文本> [...] [--tree <seq_no>] [--terms <输出文件>] [--blank <输入名文件> <输出判定文件>] [--leadstats <输入名文件> <产出台账文件>] [--leads <输入名文件> <全主导词台账文件>] [--batch <输入名文件> <Top1台账文件>] [--export <输入名文件> <推荐与链路明细文件>]");
    Console.Error.WriteLine("     手术通道: IcdIndexDiag --procedure <手术名> [...] [--ptree <seq_no>] [--pbatch <输入名文件> <台账文件>] [--pblank <输入名文件> <归因文件>]");
    return 1;
}

var treeSeq = -1;
var termsOut = (string?)null;
var blankIn = (string?)null;
var blankOut = (string?)null;
var leadStatsIn = (string?)null;
var leadStatsOut = (string?)null;
var batchIn = (string?)null;
var batchOut = (string?)null;
var exportIn = (string?)null;
var exportOut = (string?)null;
var leadsIn = (string?)null;
var leadsOut = (string?)null;
var tumorSiteMorph = (string?)null;
var tumorLedgerIn = (string?)null;
var tumorLedgerOut = (string?)null;
var procedureMode = false;
var cm3TreeSeq = -1;
var cm3BatchIn = (string?)null;
var cm3BatchOut = (string?)null;
var cm3BlankIn = (string?)null;
var cm3BlankOut = (string?)null;
var inputs = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--procedure")
    {
        procedureMode = true;
    }
    else if (args[i] == "--ptree" && i + 1 < args.Length)
    {
        procedureMode = true;
        cm3TreeSeq = int.Parse(args[++i]);
    }
    else if (args[i] == "--pbatch" && i + 2 < args.Length)
    {
        procedureMode = true;
        cm3BatchIn = args[++i];
        cm3BatchOut = args[++i];
    }
    else if (args[i] == "--pblank" && i + 2 < args.Length)
    {
        procedureMode = true;
        cm3BlankIn = args[++i];
        cm3BlankOut = args[++i];
    }
    else if (args[i] == "--tree" && i + 1 < args.Length)
    {
        treeSeq = int.Parse(args[++i]);
    }
    else if (args[i] == "--terms" && i + 1 < args.Length)
    {
        termsOut = args[++i];
    }
    else if (args[i] == "--blank" && i + 2 < args.Length)
    {
        blankIn = args[++i];
        blankOut = args[++i];
    }
    else if (args[i] == "--leadstats" && i + 2 < args.Length)
    {
        leadStatsIn = args[++i];
        leadStatsOut = args[++i];
    }
    else if (args[i] == "--leads" && i + 2 < args.Length)
    {
        leadsIn = args[++i];
        leadsOut = args[++i];
    }
    else if (args[i] == "--batch" && i + 2 < args.Length)
    {
        batchIn = args[++i];
        batchOut = args[++i];
    }
    else if (args[i] == "--export" && i + 2 < args.Length)
    {
        exportIn = args[++i];
        exportOut = args[++i];
    }
    else if (args[i] == "--tumorsite" && i + 1 < args.Length)
    {
        tumorSiteMorph = args[++i];
    }
    else if (args[i] == "--tumorledger" && i + 2 < args.Length)
    {
        tumorLedgerIn = args[++i];
        tumorLedgerOut = args[++i];
    }
    else
    {
        inputs.Add(args[i]);
    }
}

if (termsOut is not null)
{
    DumpTerms(conn, termsOut);
    return 0;
}

// 手术通道单独一套 face：索引库（cm3_index）与展开字典（ICD-9-CM-3）都与诊断侧不同构，不混用快照。
if (procedureMode)
{
    return RunCm3Modes(conn, hospital, cm3TreeSeq, cm3BatchIn, cm3BatchOut, cm3BlankIn, cm3BlankOut, inputs);
}

var snapshot = BuildSnapshot(conn);

if (treeSeq > 0)
{
    DumpTree(snapshot, treeSeq);
    return 0;
}

var dictionary = BuildDictionary(conn, hospital, codeSystem);
var synonyms = BuildSynonyms(conn);
var tumor = BuildTumor(conn);

if (tumorSiteMorph is not null)
{
    if (tumor is null)
    {
        Console.Error.WriteLine("tumor_index 表为空。");
        return 1;
    }

    foreach (var text in inputs)
    {
        Console.WriteLine($"===== {tumorSiteMorph} + {text} =====");
        var match = tumor.TryResolveSiteCode(tumorSiteMorph, text);
        Console.WriteLine(match is null
            ? "  → 未取到部位码"
            : $"  → {match.Code} [{match.ColumnName}] 路径={match.DisplayPath}");
        foreach (var candidate in tumor.DescribeCandidates(text, tumorSiteMorph))
        {
            Console.WriteLine($"   {(candidate.TiedWithBest ? "并列" : "    ")}"
                + $" seq={candidate.SeqNo} 父={candidate.ParentSeqNo} 级={candidate.Level}"
                + $" alias={candidate.AliasScore}/{candidate.AliasLength}字 path={candidate.PathScore} jump={candidate.JumpKind}"
                + $" 出码={candidate.EmittedCode ?? "-"} {candidate.FullIndexName}");
        }
    }

    return 0;
}
// ICD_INDEX_DIAG_TOP 可放宽产出条数上限，用于查看被 Top-3 挤掉的候选及其分值。
var diagTop = int.TryParse(Environment.GetEnvironmentVariable("ICD_INDEX_DIAG_TOP"), out var parsedTop) ? parsedTop : 3;
var resolver = new IcdIndexDiagnosisResolver(8, diagTop, codeSystem);

if (blankIn is not null && blankOut is not null)
{
    RunBlankDiagnosis(snapshot, dictionary, codeSystem, resolver, blankIn, blankOut, synonyms, tumor);
    return 0;
}

if (leadStatsIn is not null && leadStatsOut is not null)
{
    RunLeadStatistics(snapshot, dictionary, codeSystem, leadStatsIn, leadStatsOut);
    return 0;
}

if (leadsIn is not null && leadsOut is not null)
{
    RunLeadEnumeration(snapshot, dictionary, synonyms, codeSystem, leadsIn, leadsOut, tumor);
    return 0;
}

if (batchIn is not null && batchOut is not null)
{
    // 批量台账固定用生产口径 maxResults=3，便于改动前后逐名 Top1 对比。
    // ICD_INDEX_DIAG_LEADK 只放宽诊断工具的召回席位，用于实测「名额」这一档杠杆的上限，不改生产口径。
    var batchLeadK = int.TryParse(Environment.GetEnvironmentVariable("ICD_INDEX_DIAG_LEADK"), out var parsedLeadK)
        ? parsedLeadK
        : 8;
    RunBatch(snapshot, dictionary, synonyms, new IcdIndexDiagnosisResolver(batchLeadK, 3, codeSystem), batchIn, batchOut, tumor);
    return 0;
}

// 肿瘤专项台账：逐名同时带出「生产链路交付了什么」与「肿瘤表按各行为位能解出什么部位码」，
// 用于回答 M8000/3 这类泛形态码该不该丢、丢了以后部位码在表里到底有没有。
if (tumorLedgerIn is not null && tumorLedgerOut is not null)
{
    if (tumor is null)
    {
        Console.Error.WriteLine("tumor_index 表为空。");
        return 1;
    }

    var ledgerLeadK = int.TryParse(Environment.GetEnvironmentVariable("ICD_INDEX_DIAG_LEADK"), out var parsedLedgerK)
        ? parsedLedgerK
        : 8;
    RunTumorLedger(snapshot, dictionary, synonyms, tumor,
        new IcdIndexDiagnosisResolver(ledgerLeadK, 3, codeSystem), tumorLedgerIn, tumorLedgerOut);
    return 0;
}

if (exportIn is not null && exportOut is not null)
{
    RunExport(snapshot, dictionary, synonyms, tumor, exportIn, exportOut);
    return 0;
}

foreach (var input in inputs)
{
    Console.WriteLine($"===== {input} =====");
    Diagnose(snapshot, dictionary, input, codeSystem);
    var results = resolver.Resolve(snapshot, dictionary, input, null, synonyms, tumor: tumor);
    Console.WriteLine($"-- resolver 产出 {results.Count} 条");
    foreach (var item in results)
    {
        Console.WriteLine($"   {item.Code} {item.Title} score={item.Score} fb={item.IsIndexFallback} | {item.EvidencePath}");
    }
}

return 0;

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

// 肿瘤索引（部位表）快照；表为空时返回 null（等价于无肿瘤链路）。
static TumorIndexSnapshot? BuildTumor(string conn)
{
    using var sql = new SqlConnection(conn);
    sql.Open();
    using var cmd = new SqlCommand(
        @"SELECT seq_no, index_name, index_level, parent_seq_no, full_index_name,
                 code_malignant_primary, code_malignant_secondary, code_in_situ,
                 code_benign, code_uncertain, jump_kind, ISNULL(jump_target, '') AS jump_target
          FROM tumor_index ORDER BY seq_no", sql);
    using var reader = cmd.ExecuteReader();
    var rows = new List<TumorIndexRow>();
    while (reader.Read())
    {
        rows.Add(new TumorIndexRow(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetInt32(10),
            reader.IsDBNull(11) || reader.GetString(11).Length == 0 ? null : reader.GetString(11)));
    }

    return rows.Count == 0 ? null : TumorIndexSnapshot.Build(rows);
}

// 术语同义词快照（与生产同源：icd_term_synonym 表，仅取启用行）。
static IcdTermSynonymSnapshot BuildSynonyms(string conn)
{
    using var sql = new SqlConnection(conn);
    sql.Open();
    using var cmd = new SqlCommand(
        "SELECT term_type, canonical_term, variant_term FROM icd_term_synonym WHERE is_enabled = 1", sql);
    using var reader = cmd.ExecuteReader();
    var rows = new List<IcdTermSynonymRow>();
    while (reader.Read())
    {
        rows.Add(new IcdTermSynonymRow(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
    }

    return IcdTermSynonymSnapshot.Build(rows);
}

// 无推荐归因：按生产口径逐环节统计，定位每个空结果死在哪一层。
static void RunBlankDiagnosis(
    IcdIndexSnapshot snapshot,
    IcdDictionarySnapshot dictionary,
    string codeSystem,
    IcdIndexDiagnosisResolver resolver,
    string inFile,
    string outFile,
    IcdTermSynonymSnapshot synonyms,
    TumorIndexSnapshot? tumor = null)
{
    var names = File.ReadLines(inFile)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .ToList();
    var sb = new StringBuilder();
    sb.AppendLine("名称\t判定\t主导词召回\t下钻无终点\t链路无编码\t重叠门槛\t字典展不开\t最高重叠\t复算产出\t召回主导词");

    foreach (var name in names)
    {
        var inputKey = TextMatch.NormalizeKey(name);
        if (inputKey.Length < 2)
        {
            sb.AppendLine(string.Join('\t', Sanitize(name), "输入过短", "0", "0", "0", "0", "0", "", "0", ""));
            continue;
        }

        var leads = IcdIndexMatcher.RecallLeads(snapshot, inputKey, 8).ToList();
        var redirected = false;
        if (leads.Count == 0 && IcdIndexMatcher.ResolveRedirect(snapshot, inputKey) is { } redirect)
        {
            redirected = true;
            leads = IcdIndexMatcher.RecallLeads(snapshot, TextMatch.NormalizeKey(redirect), 8).ToList();
        }

        var drillNull = 0;
        var codeless = 0;
        var overlapKilled = 0;
        var expandEmpty = 0;
        var bestOverlap = 0m;
        foreach (var hit in leads)
        {
            var drill = IcdIndexMatcher.Drill(snapshot, hit, inputKey);
            if (drill is null)
            {
                drillNull++;
                continue;
            }

            if (drill.CodeNode.Code is not { Length: > 0 } indexCode)
            {
                codeless++;
                continue;
            }

            var overlap = PathOverlapProduction(drill.Path, name);
            bestOverlap = Math.Max(bestOverlap, overlap);
            if (overlap < 0.30m)
            {
                overlapKilled++;
                continue;
            }

            if (IndexCodeExpander
                    .Expand(ParsedIndexCode.Parse(indexCode), dictionary, name, drill.PathKey, 3, codeSystem)
                    .Count == 0)
            {
                expandEmpty++;
            }
        }

        var produced = resolver.Resolve(snapshot, dictionary, name, null, synonyms, tumor: tumor).Count;
        var died = drillNull + codeless + overlapKilled + expandEmpty;
        var verdict = produced > 0
            ? "复算有产出(空结果来自管道下游或字典差异)"
            : leads.Count == 0
                ? (redirected ? "无主导词召回(重定向后仍无)" : "无主导词召回")
                : drillNull == leads.Count
                    ? "下钻无终点"
                    : codeless == leads.Count
                        ? "链路终点无编码"
                        : overlapKilled == leads.Count
                            ? "链路重叠门槛剔除"
                            : expandEmpty == leads.Count
                                ? "索引编码字典展不开"
                                : died < leads.Count ? "混合" : "其他";

        sb.AppendLine(string.Join('\t',
            Sanitize(name), verdict, leads.Count, drillNull, codeless, overlapKilled, expandEmpty,
            bestOverlap.ToString("0.00"), produced,
            Sanitize(string.Join("|", leads.Take(3).Select(hit => hit.Node.Term)))));
    }

    File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine("wrote " + outFile + " names=" + names.Count);
}

static string Sanitize(string value) =>
    value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

/// <summary>
/// 批量 Top1 台账（改动前后回归对比的唯一口径）：逐名记录产出条数、Top1 编码、
/// Top1 是否索引码回退、全部编码列表与 Top1 证据链路。
/// 输出：名称 \t 条数 \t Top1编码 \t Top1回退 \t 编码列表 \t Top1链路
/// </summary>
static void RunBatch(
    IcdIndexSnapshot snapshot,
    IcdDictionarySnapshot dictionary,
    IcdTermSynonymSnapshot synonyms,
    IcdIndexDiagnosisResolver resolver,
    string inFile,
    string outFile,
    TumorIndexSnapshot? tumor = null)
{
    var sb = new StringBuilder();
    foreach (var name in File.ReadLines(inFile).Select(line => line.Trim()).Where(line => line.Length > 0))
    {
        var results = resolver.Resolve(snapshot, dictionary, name, null, synonyms, tumor: tumor);
        sb.Append(Sanitize(name)).Append('\t').Append(results.Count);
        if (results.Count == 0)
        {
            sb.AppendLine();
            continue;
        }

        var top = results[0];
        sb.Append('\t').Append(top.Code).Append('\t').Append(top.IsIndexFallback ? 1 : 0)
            .Append('\t').Append(Sanitize(string.Join("|", results.Select(item => item.Code))))
            .Append('\t').Append(Sanitize(top.EvidencePath))
            .AppendLine();
    }

    File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine("wrote " + outFile);
}

/// <summary>
/// 肿瘤专项台账：逐名同时带出「链路实际交付了什么」与「肿瘤索引表按该行为位能解出什么部位码」，
/// 用于判定 M8000/3 这类泛形态码是「表里没部位」还是「部位词锚不上表」，两者修法不同。
/// 输出：名称 \t 交付编码 \t 交付标题 \t Top1证据 \t 交付形态码数 \t 行为位 \t 肿瘤表部位码 \t 肿瘤表候选Top5
/// </summary>
static void RunTumorLedger(
    IcdIndexSnapshot snapshot,
    IcdDictionarySnapshot dictionary,
    IcdTermSynonymSnapshot synonyms,
    TumorIndexSnapshot tumor,
    IcdIndexDiagnosisResolver resolver,
    string inFile,
    string outFile)
{
    var sb = new StringBuilder();
    sb.AppendLine(string.Join('\t',
        "名称", "交付编码", "交付标题", "Top1证据", "交付形态码数", "行为位", "肿瘤表部位码", "肿瘤表候选Top5"));
    foreach (var name in File.ReadLines(inFile).Select(line => line.Trim()).Where(line => line.Length > 0))
    {
        var results = resolver.Resolve(snapshot, dictionary, name, null, synonyms, tumor: tumor);
        var morphological = results.Where(item => System.Text.RegularExpressions.Regex
            .IsMatch(item.Code.ToUpperInvariant(), @"^M\d{4,5}/\d")).ToList();
        var digit = BehaviorDigitOfInput(name);
        var site = tumor.TryResolveSiteCode($"M8000/{digit}", name);
        var candidates = string.Join(" ‖ ", tumor
            .DescribeCandidates(name, $"M8000/{digit}", 5)
            .Select(item => $"{item.FullIndexName}[级{item.Level} 别名{item.AliasScore}/{item.AliasLength}字 父路{item.PathScore}"
                + $" 出码{item.EmittedCode ?? "-"}{(item.TiedWithBest ? " 并列" : "")}]"));

        sb.Append(Sanitize(name))
            .Append('\t').Append(Sanitize(string.Join("|", results.Select(item => item.Code))))
            .Append('\t').Append(Sanitize(string.Join("|", results.Select(item => item.Title))))
            .Append('\t').Append(Sanitize(results.Count > 0 ? results[0].EvidencePath : string.Empty))
            .Append('\t').Append(morphological.Count)
            .Append('\t').Append(digit)
            .Append('\t').Append(site?.Code ?? "-")
            .Append('\t').Append(Sanitize(candidates))
            .AppendLine();
    }

    File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine("wrote " + outFile);
}

/// <summary>输入词面 → 动态码行为位（肿瘤表列）：写 继发/转移 走 /6，良性 /0，原位 /2，动态未定 /1，其余按原发 /3。</summary>
static string BehaviorDigitOfInput(string name)
    => name.Contains("继发", StringComparison.Ordinal) || name.Contains("转移", StringComparison.Ordinal) ? "6"
        : name.Contains("良性", StringComparison.Ordinal) ? "0"
        : name.Contains("原位", StringComparison.Ordinal) ? "2"
        : name.Contains("动态未定", StringComparison.Ordinal) || name.Contains("交界", StringComparison.Ordinal) ? "1"
        : "3";

/// <summary>
/// 对照导出的明细台账（本地复算，不写库）：每个诊断名先按生产口径（maxResults=3）取推荐，
/// 再把各条推荐证据里的链路词条序列回溯到索引节点，带出节点自身编码。
/// 输出（R=推荐行，C=链路行，均以类型列开头）：
///   R \t 名称 \t 排名 \t 编码 \t 标题 \t 是否索引码回退
///   C \t 名称 \t 主导词 \t 主导词编码 \t 修饰词路径 \t 修饰词编码
/// </summary>
static void RunExport(
    IcdIndexSnapshot snapshot,
    IcdDictionarySnapshot dictionary,
    IcdTermSynonymSnapshot synonyms,
    TumorIndexSnapshot? tumor,
    string inFile,
    string outFile)
{
    var nodesByTerm = new Dictionary<string, List<IcdIndexNode>>();
    foreach (var lead in snapshot.Leads)
    {
        foreach (var node in Flatten(lead))
        {
            if (!nodesByTerm.TryGetValue(node.Term, out var list))
            {
                list = [];
                nodesByTerm[node.Term] = list;
            }

            list.Add(node);
        }
    }

    var resolver = new IcdIndexDiagnosisResolver(8, 3, "ICD-10");
    var sb = new StringBuilder();
    var names = 0;
    var unresolved = 0;

    foreach (var name in File.ReadLines(inFile).Select(line => line.Trim()).Where(line => line.Length > 0))
    {
        names++;
        var results = resolver.Resolve(snapshot, dictionary, name, null, synonyms, tumor: tumor);
        var rank = 0;
        var chainKeys = new List<string>();

        foreach (var item in results)
        {
            rank++;
            sb.Append('R').Append('\t').Append(Sanitize(name)).Append('\t').Append(rank)
                .Append('\t').Append(Sanitize(item.Code))
                .Append('\t').Append(Sanitize(item.Title))
                .Append('\t').Append(item.IsIndexFallback ? 1 : 0)
                .AppendLine();

            var key = ChainKey(item.EvidencePath);
            if (!chainKeys.Contains(key))
            {
                chainKeys.Add(key);
            }
        }

        foreach (var key in chainKeys)
        {
            var terms = key.Split(" → ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var path = ResolveChain(nodesByTerm, terms);
            if (path is null)
            {
                unresolved++;
                sb.Append('C').Append('\t').Append(Sanitize(name))
                    .Append('\t').Append(Sanitize(terms.Length > 0 ? terms[0] : string.Empty))
                    .Append("\t链路未解析\t\t\n");
                continue;
            }

            sb.Append('C').Append('\t').Append(Sanitize(name))
                .Append('\t').Append(Sanitize(path[0].Term))
                .Append('\t').Append(Sanitize(path[0].Code ?? string.Empty))
                .Append('\t').Append(Sanitize(string.Join("→", path.Skip(1).Select(node => node.Term))))
                .Append('\t').Append(Sanitize(string.Join("→", path.Skip(1).Select(node => node.Code ?? string.Empty))))
                .AppendLine();
        }
    }

    File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"wrote {outFile} names={names} unresolvedChains={unresolved}");
}

static IEnumerable<IcdIndexNode> Flatten(IcdIndexNode node)
{
    yield return node;
    foreach (var child in node.Children)
    {
        foreach (var descendant in Flatten(child))
        {
            yield return descendant;
        }
    }
}

/// <summary>"溃疡(性) → … → 出血 [跳转: …] | 索引编码: K26.4" → 链路词条序列。</summary>
static string ChainKey(string evidencePath)
{
    var head = evidencePath.Split(" | 索引编码: ")[0];
    var jumpIndex = head.IndexOf(" [跳转: ", StringComparison.Ordinal);
    if (jumpIndex >= 0)
    {
        head = head[..jumpIndex];
    }

    return head.Trim();
}

/// <summary>
/// 链路回溯：主导词层优先取有效主导词里的同名节点，再逐级要求子节点同名；
/// "胆囊 [跳转: 胆囊的 -见 情况]" 这类改道链路首词是 病，疾病 的子节点，
/// 故主导词找不到时退回全树同名节点。
/// </summary>
static List<IcdIndexNode>? ResolveChain(
    Dictionary<string, List<IcdIndexNode>> nodesByTerm,
    IReadOnlyList<string> terms)
{
    if (terms.Count == 0 || !nodesByTerm.TryGetValue(terms[0], out var candidates))
    {
        return null;
    }

    foreach (var start in candidates.OrderByDescending(node => node.Level == 0))
    {
        var path = new List<IcdIndexNode> { start };
        var current = start;
        var matched = true;
        for (var i = 1; i < terms.Count; i++)
        {
            current = current.Children.FirstOrDefault(child => child.Term == terms[i]);
            if (current is null)
            {
                matched = false;
                break;
            }

            path.Add(current);
        }

        if (matched)
        {
            return path;
        }
    }

    return null;
}

/// <summary>
/// 主导词级产出台账：逐条召回链路记录"命中的别名键、下钻是否到带码节点、链路重叠、字典展开条数"，
/// 用于统计哪些别名键高召回零产出（泛化词黑名单候选——按键统计才不会误伤共用该键的有效主导词）。
/// 输出：名称 \t 主导词 \t 别名键 \t 下钻到码 \t 重叠 \t 展开条数
/// </summary>
static void RunLeadStatistics(
    IcdIndexSnapshot snapshot,
    IcdDictionarySnapshot dictionary,
    string codeSystem,
    string inFile,
    string outFile)
{
    var sb = new StringBuilder();
    foreach (var name in File.ReadLines(inFile).Select(line => line.Trim()).Where(line => line.Length > 0))
    {
        var inputKey = TextMatch.NormalizeKey(name);
        if (inputKey.Length < 2)
        {
            continue;
        }

        var leads = IcdIndexMatcher.RecallLeads(snapshot, inputKey, 8).ToList();
        if (leads.Count == 0 && IcdIndexMatcher.ResolveRedirect(snapshot, inputKey) is { } redirect)
        {
            leads = IcdIndexMatcher.RecallLeads(snapshot, TextMatch.NormalizeKey(redirect), 8).ToList();
        }

        foreach (var hit in leads)
        {
            var drill = IcdIndexMatcher.Drill(snapshot, hit, inputKey);
            if (drill?.CodeNode.Code is not { Length: > 0 } indexCode)
            {
                sb.Append(Sanitize(name)).Append('\t').Append(Sanitize(hit.Node.Term))
                  .Append('\t').Append(Sanitize(hit.Alias.Key))
                  .Append('\t').Append(0).Append('\t').Append(0).Append('\t').Append(0).AppendLine();
                continue;
            }

            var overlap = PathOverlapProduction(drill.Path, name);
            var expanded = overlap < 0.30m
                ? -1
                : IndexCodeExpander.Expand(
                    ParsedIndexCode.Parse(indexCode), dictionary, name, drill.PathKey, 3, codeSystem).Count;
            sb.Append(Sanitize(name)).Append('\t').Append(Sanitize(hit.Node.Term))
              .Append('\t').Append(Sanitize(hit.Alias.Key))
              .Append('\t').Append(1).Append('\t').Append(overlap.ToString("0.000")).Append('\t')
              .Append(expanded).AppendLine();
        }
    }

    File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine("wrote " + outFile);
}

/// <summary>
/// 全主导词枚举台账（"这个诊断名里有哪些主导词、每个主导词下能配到哪些修饰词"）：
/// 召回宽度取 100（生产口径 leadWordTopK=8），每个主导词各自下钻一次，
/// 所以 右眼视网膜瘢痕 这类一名多主导词的输入会把 视网膜 与 瘢痕 各自成行列出；
/// 被生产名额挤掉、以及召回了却走不出编码的主导词同样列出（下钻列写明卡在哪），
/// 下钻失败时补列"词面能在输入里沾到的直接子行"，供人工核对是不是索引缺修饰词。
/// 输入变体（术语同义词改写、索引"又称"改写）各自成组列出并在 变体/来源 列标注，
/// 因为生产链路确实会跑这些变体，只看原文会漏掉实际生效的主导词。
/// 输出（首行表头）：
///   名称 \t 变体 \t 来源 \t 名次 \t 主导词 \t 主导词编码 \t 命中的别名键 \t 覆盖字数 \t 覆盖率
///   \t 生产名额内 \t 下钻 \t 修饰词(下钻路径) \t 修饰词编码 \t 链路终点编码 \t 祖先兜底 \t 链路重叠 \t 字典展开码
/// </summary>
static void RunLeadEnumeration(
    IcdIndexSnapshot snapshot,
    IcdDictionarySnapshot dictionary,
    IcdTermSynonymSnapshot synonyms,
    string codeSystem,
    string inFile,
    string outFile,
    TumorIndexSnapshot? tumor = null)
{
    const int enumerateTopK = 100;
    // 生产席位随 V22PipelineOptions.LeadWordTopK 放宽到 12；台账里的 生产名额内 标记必须跟着改口径，
    // 否则人工判噪还在按 8 席看。可用 ICD_INDEX_DIAG_LEADK 临时对照别的席位。
    var productionTopK = int.TryParse(Environment.GetEnvironmentVariable("ICD_INDEX_DIAG_LEADK"), out var parsedLeadK)
        ? parsedLeadK
        : 12;

    var sb = new StringBuilder();
    sb.AppendLine(string.Join('\t',
        "名称", "变体", "来源", "名次", "主导词", "主导词编码", "命中的别名键", "覆盖字数", "覆盖率",
        "生产名额内", "下钻", "修饰词(下钻路径)", "修饰词编码", "链路终点编码", "祖先兜底", "链路重叠", "字典展开码"));
    var nameCount = 0;
    var rowCount = 0;
    var namesWithoutLead = 0;

    foreach (var name in File.ReadLines(inFile).Select(line => line.Trim()).Where(line => line.Length > 0))
    {
        nameCount++;
        var variants = new List<(string Text, string Source)> { (name, "原文") };
        foreach (var text in synonyms.ExpandVariants(name, TermSynonymTypes.Diagnosis))
        {
            if (text.Length > 0 && !variants.Any(item => item.Text == text))
            {
                variants.Add((text, "术语同义词"));
            }
        }

        foreach (var text in snapshot.RewriteByAnotherNames(name))
        {
            if (text.Length > 0 && !variants.Any(item => item.Text == text))
            {
                variants.Add((text, "索引又称"));
            }
        }

        var emitted = 0;
        foreach (var variant in variants)
        {
            var inputKey = TextMatch.NormalizeKey(variant.Text);
            if (inputKey.Length < 2)
            {
                continue;
            }

            var source = variant.Source;
            var drillKey = inputKey;
            var leads = IcdIndexMatcher.RecallLeads(snapshot, drillKey, enumerateTopK).ToList();
            if (leads.Count == 0 && IcdIndexMatcher.ResolveRedirect(snapshot, drillKey) is { } redirect)
            {
                var redirectKey = TextMatch.NormalizeKey(redirect);
                leads = IcdIndexMatcher.RecallLeads(snapshot, redirectKey, enumerateTopK).ToList();
                if (leads.Count > 0)
                {
                    source += "→见重定向";
                    drillKey = redirectKey;
                }
            }

            var rank = 0;
            foreach (var hit in leads)
            {
                rank++;
                var drill = IcdIndexMatcher.Drill(snapshot, hit, drillKey);
                string verdict;
                string modifierTerms;
                string modifierCodes;
                var terminalCode = string.Empty;
                var ancestorFallback = "0";
                var overlapText = string.Empty;
                var expandedText = string.Empty;

                if (drill is null)
                {
                    var candidates = DirectModifierCandidates(hit.Node, drillKey);
                    modifierTerms = string.Join("→", candidates.Select(node => node.Term));
                    modifierCodes = string.Join("→", candidates.Select(node => node.Code ?? "无"));
                    verdict = candidates.Count > 0 ? "下钻无编码(列出词面可沾的子行)" : "下钻无编码";
                    overlapText = "0.000";
                }
                else
                {
                    var indexCode = drill.CodeNode.Code;
                    var hasCode = !string.IsNullOrEmpty(indexCode);
                    terminalCode = indexCode ?? string.Empty;
                    ancestorFallback = drill.CodeFromAncestorFallback ? "1" : "0";
                    modifierTerms = string.Join("→", drill.Path.Skip(1).Select(node => node.Term));
                    modifierCodes = string.Join("→", drill.Path.Skip(1).Select(node => node.Code ?? "无"));
                    var overlap = PathOverlapProduction(drill.Path, variant.Text);
                    overlapText = overlap.ToString("0.000");
                    if (!hasCode)
                    {
                        verdict = "链路终点无编码";
                    }
                    else if (overlap < 0.30m)
                    {
                        verdict = "有编码但被0.30重叠门槛剔除";
                        expandedText = "（未展开）";
                    }
                    else
                    {
                        verdict = modifierTerms.Length > 0 ? "有编码+修饰词" : "有编码(主导词自身带码)";
                        expandedText = string.Join("|", IndexCodeExpander
                            .Expand(ParsedIndexCode.Parse(indexCode!), dictionary, variant.Text, drill.PathKey, 3, codeSystem)
                            .Select(item => item.Code + (item.IsIndexFallback ? "(fb)" : string.Empty)));
                    }
                }

                sb.AppendLine(string.Join('\t',
                    Sanitize(name),
                    Sanitize(variant.Text == name ? string.Empty : variant.Text),
                    source,
                    rank,
                    Sanitize(hit.Node.Term),
                    Sanitize(hit.Node.Code ?? string.Empty),
                    Sanitize(hit.Alias.Key),
                    hit.CoveredChars,
                    hit.CoverRatio.ToString("0.00"),
                    rank <= productionTopK ? 1 : 0,
                    verdict,
                    Sanitize(modifierTerms),
                    Sanitize(modifierCodes),
                    Sanitize(terminalCode),
                    ancestorFallback,
                    overlapText,
                    Sanitize(expandedText)));
                rowCount++;
                emitted++;
            }
        }

        if (emitted == 0)
        {
            namesWithoutLead++;
        }
    }

    File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"wrote {outFile} names={nameCount} rows={rowCount} namesWithoutLead={namesWithoutLead}");
}

/// <summary>下钻取不到编码时，列出词面出现在输入里、且部位限定词门控放行的直接子行（最多 6 条）。</summary>
static List<IcdIndexNode> DirectModifierCandidates(IcdIndexNode lead, string inputKey)
{
    var inputQualifiers = TextMatch.ExtractQualifiers(inputKey);
    return lead.Children
        .Where(child => NodeAllows(child, inputQualifiers, inputKey))
        .Where(child => child.Aliases.Any(alias => alias.MatchKey.Length >= 2
            && inputKey.Contains(alias.MatchKey, StringComparison.Ordinal)))
        .Take(6)
        .ToList();
}

// 与 IcdIndexDiagnosisResolver.PathOverlapRatio 同口径（书写噪声不计入分母）。
static decimal PathOverlapProduction(IReadOnlyList<IcdIndexNode> path, string input)
{
    var inputKey = TextMatch.StripIndexingFillers(input);
    if (inputKey.Length == 0)
    {
        inputKey = TextMatch.NormalizeKey(input);
    }

    if (inputKey.Length == 0)
    {
        return 0m;
    }

    var content = inputKey.Where(character => !char.IsAsciiLetterOrDigit(character)).Distinct().ToHashSet();
    var characters = content.Count > 0 ? content : inputKey.Distinct().ToHashSet();
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
            covered.UnionWith(TextMatch.NormalizeKey(variant).Where(characters.Contains));
        }
    }

    return characters.Count == 0 ? 0m : covered.Count / (decimal)characters.Count;
}

// 导出生产口径的节点词条（Term 已剥离层级符与"见/另见/现译名"跳转段），供外部按链路文本回查节点编码。
static void DumpTerms(string conn, string outFile)
{
    var sb = new StringBuilder();
    foreach (var row in BuildRows(conn))
    {
        var node = IcdIndexSnapshotBuilder.ParseNode(row);
        sb.Append(node.SeqNo).Append('\t')
            .Append(node.Level).Append('\t')
            .Append(node.ParentSeqNo).Append('\t')
            .Append(node.Code ?? string.Empty).Append('\t')
            .Append(node.Term.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '))
            .AppendLine();
    }

    File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine("wrote " + outFile);
}

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

static void Diagnose(IcdIndexSnapshot snapshot, IcdDictionarySnapshot dictionary, string input, string codeSystem)
{
    var inputKey = TextMatch.NormalizeKey(input);
    var inputQualifiers = TextMatch.ExtractQualifiers(inputKey);
    Console.WriteLine($"-- key={inputKey} qualifiers=[{string.Join(",", inputQualifiers)}]");

    // 召回完全走生产口径（IcdIndexMatcher.RecallLeads），本工具只负责展开链路。
    foreach (var hit in IcdIndexMatcher.RecallLeads(snapshot, inputKey, 8))
    {
        var lead = hit.Node;
        Console.WriteLine($"-- 召回 lead={lead.SeqNo}:{lead.Term} alias={hit.Alias.Key} cover={hit.CoverRatio} chars={hit.CoveredChars}");
        var drill = IcdIndexMatcher.Drill(snapshot, hit, inputKey);
        if (drill is null)
        {
            DescribeChildren(lead, inputKey, inputKey, inputQualifiers, "   子层");
            Console.WriteLine("   drill=null（链路无编码）");
            continue;
        }

        var overlap = PathOverlap(drill.Path, input);
        Console.WriteLine($"   path={string.Join(" → ", drill.Path.Select(n => n.Term))} code={drill.CodeNode.Code} pathKey={drill.PathKey} overlap={overlap}");
        if (overlap < 0.30m)
        {
            Console.WriteLine("   !! 被 0.30 链路重叠门槛丢弃");
        }

        DescribeChildren(drill.Path[^1], drill.PathKey, inputKey, inputQualifiers, "   下一层");
        var expanded = IndexCodeExpander.Expand(
            ParsedIndexCode.Parse(drill.CodeNode.Code!), dictionary, input, drill.PathKey, 3, codeSystem);
        Console.WriteLine($"   expanded={string.Join("|", expanded.Select(e => $"{e.Code}{(e.IsIndexFallback ? "(fb)" : "")}"))}");
    }

    // "X的 -见 情况" 泛化主导词通道逐环节展开，定位空产出死在哪一层。
    Console.WriteLine($"-- redirects={snapshot.Redirects.Count} 见情况通道候选：");
    foreach (var (key, target) in snapshot.Redirects)
    {
        if (!target.Contains("情况", StringComparison.Ordinal)
            || key.Length < 2
            || !inputKey.Contains(key, StringComparison.Ordinal))
        {
            continue;
        }

        var anchorLead = IcdIndexMatcher.ResolveJumpLead(snapshot, target);
        Console.WriteLine($"   key={key} target={target} lead={(anchorLead is null ? "null" : $"{anchorLead.SeqNo}:{anchorLead.Term}")}");
        if (anchorLead is null)
        {
            continue;
        }

        var anchor = IcdIndexMatcher.DrillFromRedirectAnchor(snapshot, anchorLead, key, inputKey, $"{key} -见 {target}");
        if (anchor is null)
        {
            Console.WriteLine("     drill=null（锚点链路无编码）");
            continue;
        }

        Console.WriteLine($"     path={string.Join(" → ", anchor.Path.Select(n => n.Term))}"
            + $" code={anchor.CodeNode.Code} pathKey={anchor.PathKey} fbAncestor={anchor.CodeFromAncestorFallback}"
            + $" overlap={PathOverlap(anchor.Path, input)}");
        var anchorExpanded = IndexCodeExpander.Expand(
            ParsedIndexCode.Parse(anchor.CodeNode.Code!), dictionary, input, anchor.PathKey, 3, codeSystem);
        Console.WriteLine($"     expanded={string.Join("|", anchorExpanded.Select(e => $"{e.Code}{(e.IsIndexFallback ? "(fb)" : "")}"))}");
    }
}

// 与 IcdIndexMatcher.NodeAllows 同口径，仅用于解释子层剔除原因。
static bool NodeAllows(IcdIndexNode node, HashSet<string> inputQualifiers, string inputKey)
    => node.Qualifiers.IsSubsetOf(inputQualifiers)
        && !TextMatch.HasPhasePoleConflict(node.Term, inputKey);

static void DescribeChildren(IcdIndexNode node, string pathKey, string inputKey, HashSet<string> inputQualifiers, string label)
{
    foreach (var child in node.Children)
    {
        Console.WriteLine($"{label} seq={child.SeqNo} {child.Term} code={child.Code} jump={child.JumpKind}"
            + (!NodeAllows(child, inputQualifiers, inputKey)
                ? $" <<门控剔除 缺[{string.Join(",", child.Qualifiers.Except(inputQualifiers))}]"
                  + (TextMatch.HasPhasePoleConflict(child.Term, inputKey) ? " 病程极冲突" : string.Empty)
                : string.Empty));
    }
}

static decimal PathOverlap(IReadOnlyList<IcdIndexNode> path, string input)
{
    var inputKey = TextMatch.NormalizeKey(input);
    // 与生产口径一致：列表序号/罗马数字等纯 ASCII 字符不计入分母。
    var content = inputKey.Where(character => !char.IsAsciiLetterOrDigit(character)).Distinct().ToHashSet();
    var characters = content.Count > 0 ? content : inputKey.Distinct().ToHashSet();
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
            covered.UnionWith(TextMatch.NormalizeKey(variant).Where(characters.Contains));
        }
    }

    return characters.Count == 0 ? 0m : covered.Count / (decimal)characters.Count;
}

static void DumpTree(IcdIndexSnapshot snapshot, int seq)
{
    var node = Find(snapshot, seq);
    if (node is null)
    {
        Console.WriteLine("未找到 seq_no " + seq);
        return;
    }

    var sb = new StringBuilder();
    Walk(node, 0, sb);
    Console.Write(sb);
}

static IcdIndexNode? Find(IcdIndexSnapshot snapshot, int seq)
{
    foreach (var lead in snapshot.Leads)
    {
        var found = FindIn(lead, seq);
        if (found is not null)
        {
            return found;
        }
    }

    return null;
}

static IcdIndexNode? FindIn(IcdIndexNode node, int seq)
{
    if (node.SeqNo == seq)
    {
        return node;
    }

    foreach (var child in node.Children)
    {
        var found = FindIn(child, seq);
        if (found is not null)
        {
            return found;
        }
    }

    return null;
}

static void Walk(IcdIndexNode node, int depth, StringBuilder sb)
{
    sb.Append(new string(' ', depth * 2))
        .Append(node.SeqNo)
        .Append(' ')
        .Append(node.Term)
        .Append(" | code=").Append(node.Code ?? "-")
        .Append(" | aliases=").Append(string.Join(",", node.Aliases.Select(a => a.Key)))
        .Append(" | qual=").Append(string.Join(",", node.Qualifiers))
        .Append(" | jump=").Append(node.JumpKind).Append('(').Append(node.JumpTarget ?? "-").Append(')')
        .AppendLine();
    foreach (var child in node.Children)
    {
        Walk(child, depth + 1, sb);
    }
}

// ===== 手术通道（cm3_index 两段式链路）=====
// 判据全部走生产代码（Cm3ProcedureIndexMatcher / Cm3IndexCodeExpander / Cm3ProcedureIndexResolver），
// 本工具只负责把每一层的中间量打出来；口径与在线不一致时以在线为准。

static int RunCm3Modes(
    string conn,
    string hospital,
    int treeSeq,
    string? batchIn,
    string? batchOut,
    string? blankIn,
    string? blankOut,
    List<string> inputs)
{
    var snapshot = BuildCm3Snapshot(conn);
    Console.Error.WriteLine(
        $"-- cm3_index：{snapshot.TotalNodes} 行 / 0 级 {snapshot.Level0Rows} 条 / 有效主导词 {snapshot.Leads.Count} 条 / " +
        $"死路主导词 {snapshot.DeadEndLeads} 条 / 状态列与词面不一致 {snapshot.LeadStatusDisagreements} 条 / " +
        $"失效主导词改道键 {snapshot.Redirects.Count} 个");

    if (treeSeq > 0)
    {
        DumpCm3Tree(snapshot, treeSeq);
        return 0;
    }

    var dictionary = BuildDictionary(conn, hospital, Cm3CodeSystem.DictionaryCodeSystem);
    Console.Error.WriteLine(
        $"-- 展开字典：{Cm3CodeSystem.DictionaryCodeSystem} 共 {dictionary.Count} 行"
        + (dictionary.Count == 0
            ? "（该医院下没有手术字典，用 ICD_INDEX_DIAG_HOSPITAL 指定拥有 ICD-9-CM-3 的医院）"
            : string.Empty));
    var synonyms = BuildSynonyms(conn);

    if (batchIn is not null && batchOut is not null)
    {
        RunProcedureBatch(snapshot, dictionary, synonyms, batchIn, batchOut);
        return 0;
    }

    if (blankIn is not null && blankOut is not null)
    {
        RunProcedureBlank(snapshot, dictionary, synonyms, blankIn, blankOut);
        return 0;
    }

    if (inputs.Count == 0)
    {
        Console.Error.WriteLine(
            "用法: --procedure <手术名> [...] / --ptree <seq_no> / --pbatch <名文件> <台账文件> / --pblank <名文件> <归因文件>");
        return 1;
    }

    var diagTop = int.TryParse(Environment.GetEnvironmentVariable("ICD_INDEX_DIAG_TOP"), out var parsedTop)
        ? parsedTop
        : 3;
    var resolver = new Cm3ProcedureIndexResolver(8, diagTop);
    foreach (var input in inputs)
    {
        Console.WriteLine($"===== {input} =====");
        DiagnoseProcedure(snapshot, dictionary, input);
        var results = resolver.Resolve(snapshot, dictionary, input, synonyms);
        Console.WriteLine($"-- resolver 产出 {results.Count} 条");
        foreach (var item in results)
        {
            Console.WriteLine(
                $"   {item.Code} {item.Title} score={item.Score} fb={item.IsIndexFallback} "
                + $"depth={item.WholeTitleDepth} fam={item.FamilyRank} | {item.EvidencePath}");
        }
    }

    return 0;
}

static List<Cm3IndexRow> BuildCm3Rows(string conn)
{
    using var sql = new SqlConnection(conn);
    sql.Open();
    using var cmd = new SqlCommand(
        "SELECT seq_no, index_name, index_code, index_level, parent_seq_no, lead_word_status FROM cm3_index ORDER BY seq_no", sql);
    using var reader = cmd.ExecuteReader();
    var rows = new List<Cm3IndexRow>();
    while (reader.Read())
    {
        rows.Add(new Cm3IndexRow(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5)));
    }

    AppendCm3DraftRows(rows);

    return rows;
}

/// <summary>
/// 知识库侧候选行的离线注入通道：CM3_DRAFT_ROWS=&lt;TSV&gt;，列序 seq_no / index_name / index_code / index_level / parent_seq_no / lead_word_status。
/// 只在诊断工具里生效，不写库；两面复算都不净负才谈入库。
/// </summary>
static void AppendCm3DraftRows(List<Cm3IndexRow> rows)
{
    var path = Environment.GetEnvironmentVariable("CM3_DRAFT_ROWS");
    if (string.IsNullOrEmpty(path) || !File.Exists(path))
    {
        return;
    }

    var added = 0;
    foreach (var line in File.ReadAllLines(path))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
        {
            continue;
        }

        var f = line.Split('\t');
        if (f.Length < 5)
        {
            continue;
        }

        rows.Add(new Cm3IndexRow(
            int.Parse(f[0]),
            f[1],
            f[2].Length > 0 ? f[2] : null,
            int.Parse(f[3]),
            int.Parse(f[4]),
            f.Length > 5 ? int.Parse(f[5]) : 0));
        added++;
    }

    Console.Error.WriteLine($"-- 草稿行注入 {added} 条（{path}）");
}

static Cm3IndexSnapshot BuildCm3Snapshot(string conn) => Cm3IndexSnapshotBuilder.Build(BuildCm3Rows(conn));

static void DiagnoseProcedure(Cm3IndexSnapshot snapshot, IcdDictionarySnapshot dictionary, string input)
{
    var inputKey = TextMatch.NormalizeKey(input);
    Console.WriteLine($"-- key={inputKey} 部位限定词=[{string.Join(",", Cm3SiteQualifiers.Extract(inputKey))}]");

    var hits = Cm3ProcedureIndexMatcher.RecallLeads(snapshot, inputKey, 8);
    if (hits.Count == 0
        && Cm3ProcedureIndexMatcher.ResolveRedirect(snapshot, inputKey) is { } redirect)
    {
        Console.WriteLine($"-- 失效主导词改道：{inputKey} → {redirect}");
        hits = Cm3ProcedureIndexMatcher.RecallLeads(snapshot, TextMatch.NormalizeKey(redirect), 8);
    }

    if (hits.Count == 0)
    {
        Console.WriteLine("-- 无主导词召回（索引通道返回空，手术输入仍由字典/向量通道服务）");
        return;
    }

    foreach (var hit in hits)
    {
        var lead = hit.Node;
        Console.WriteLine(
            $"-- 召回 lead={lead.SeqNo}:{lead.Term} alias={hit.Alias.Key}(w{hit.Alias.Weight}) "
            + $"cover={hit.CoverRatio} chars={hit.CoveredChars} 本行码={lead.Code ?? "-"} 子节点={lead.Children.Count}");
        var drill = Cm3ProcedureIndexMatcher.Drill(snapshot, hit, inputKey);
        if (drill is null)
        {
            Console.WriteLine("   drill=null（链路无编码）");
            continue;
        }

        var overlap = Cm3ProcedureIndexResolver.PathOverlapRatio(drill.Path, input);
        Console.WriteLine(
            $"   path={string.Join(" → ", drill.Path.Select(node => node.Term))} "
            + $"code={drill.CodeNode.Code} 祖先兜底={(drill.CodeFromAncestorFallback ? 1 : 0)} "
            + $"pathKey={drill.PathKey} overlap={overlap}");
        if (drill.Jumps.Count > 0)
        {
            Console.WriteLine($"   跳转={string.Join("; ", drill.Jumps)}");
        }

        if (overlap < Cm3ProcedureIndexResolver.MinPathOverlap)
        {
            Console.WriteLine($"   链路重叠低于 {Cm3ProcedureIndexResolver.MinPathOverlap} → 生产口径丢弃");
            continue;
        }

        var expanded = Cm3IndexCodeExpander.Expand(drill.CodeNode.Code, dictionary, input, drill.PathKey, 3);
        if (expanded.Count == 0)
        {
            Console.WriteLine($"   字典展不开：前缀 {drill.CodeNode.Code} 零命中");
            continue;
        }

        foreach (var item in expanded)
        {
            Console.WriteLine(
                $"     {item.Code} {item.Title} score={item.Score} fb={item.IsIndexFallback} "
                + $"depth={item.WholeTitleDepth} fam={item.FamilyRank} {item.Note ?? string.Empty}");
        }
    }
}

/// <summary>批量台账（列序与诊断侧 --batch 一致，可直接套逐名对比脚本）。</summary>
static void RunProcedureBatch(
    Cm3IndexSnapshot snapshot,
    IcdDictionarySnapshot dictionary,
    IcdTermSynonymSnapshot synonyms,
    string inFile,
    string outFile)
{
    var names = File.ReadLines(inFile).Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
    var resolver = new Cm3ProcedureIndexResolver(8, 3);
    var sb = new StringBuilder();
    var produced = 0;
    var fallback = 0;
    foreach (var name in names)
    {
        var results = resolver.Resolve(snapshot, dictionary, name, synonyms);
        sb.Append(Sanitize(name)).Append('\t').Append(results.Count);
        if (results.Count == 0)
        {
            sb.AppendLine();
            continue;
        }

        produced++;
        var top = results[0];
        fallback += top.IsIndexFallback ? 1 : 0;
        sb.Append('\t').Append(top.Code).Append('\t').Append(top.IsIndexFallback ? 1 : 0)
            .Append('\t').Append(Sanitize(string.Join("|", results.Select(item => item.Code))))
            .Append('\t').Append(Sanitize(top.EvidencePath))
            .AppendLine();
    }

    File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"wrote {outFile} names={names.Count} 有推荐={produced} Top1索引码回退={fallback}");
}

/// <summary>空推荐归因：按生产判据依次落到 无主导词召回 / 链路无编码 / 链路重叠门控 / 字典前缀零命中。</summary>
static void RunProcedureBlank(
    Cm3IndexSnapshot snapshot,
    IcdDictionarySnapshot dictionary,
    IcdTermSynonymSnapshot synonyms,
    string inFile,
    string outFile)
{
    var names = File.ReadLines(inFile).Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
    var resolver = new Cm3ProcedureIndexResolver(8, 3);
    var buckets = new Dictionary<string, int>(StringComparer.Ordinal);
    var sb = new StringBuilder();
    var empty = 0;
    foreach (var name in names)
    {
        if (resolver.Resolve(snapshot, dictionary, name, synonyms).Count > 0)
        {
            continue;
        }

        empty++;
        var stage = ClassifyEmptyProcedure(snapshot, dictionary, name);
        buckets[stage] = buckets.GetValueOrDefault(stage) + 1;
        sb.Append(Sanitize(name)).Append('\t').Append(stage).AppendLine();
    }

    File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine(
        $"wrote {outFile} names={names.Count} 空推荐={empty} 归因="
        + string.Join(" ", buckets.OrderBy(item => item.Key).Select(item => $"{item.Key}:{item.Value}")));
}

static string ClassifyEmptyProcedure(
    Cm3IndexSnapshot snapshot,
    IcdDictionarySnapshot dictionary,
    string name)
{
    var inputKey = TextMatch.NormalizeKey(name);
    var hits = Cm3ProcedureIndexMatcher.RecallLeads(snapshot, inputKey, 8);
    if (hits.Count == 0)
    {
        var redirect = Cm3ProcedureIndexMatcher.ResolveRedirect(snapshot, inputKey);
        if (redirect is not null)
        {
            hits = Cm3ProcedureIndexMatcher.RecallLeads(snapshot, TextMatch.NormalizeKey(redirect), 8);
        }

        if (hits.Count == 0)
        {
            // 见-1 的 0 级形态：输入含住失效主导词也能改道，逐条查表。
            hits =
            [
                .. snapshot.Redirects
                    .Where(pair => pair.Key.Length >= 2 && inputKey.Contains(pair.Key, StringComparison.Ordinal))
                    .Select(pair => Cm3ProcedureIndexMatcher.ResolveJumpLead(snapshot, pair.Value))
                    .OfType<Cm3IndexNode>()
                    .Select(node => new Cm3LeadWordHit(node, node.Aliases[0], 0m, 0)),
            ];
        }

        if (hits.Count == 0)
        {
            return "无主导词召回";
        }
    }

    var drills = hits
        .Select(hit => Cm3ProcedureIndexMatcher.Drill(snapshot, hit, inputKey))
        .OfType<Cm3DrillResult>()
        .ToList();
    if (drills.Count == 0)
    {
        return "下钻无终点/链路无编码";
    }

    if (drills.All(drill => Cm3ProcedureIndexResolver.PathOverlapRatio(drill.Path, name)
        < Cm3ProcedureIndexResolver.MinPathOverlap))
    {
        return "链路重叠门控";
    }

    return drills.All(drill => Cm3IndexCodeExpander.Expand(
        drill.CodeNode.Code, dictionary, name, drill.PathKey, 3).Count == 0)
        ? "字典前缀零命中"
        : "同族名额被挤占";
}

static void DumpCm3Tree(Cm3IndexSnapshot snapshot, int seq)
{
    var node = FindCm3(snapshot, seq);
    if (node is null)
    {
        Console.WriteLine("未找到 seq_no " + seq + "（0 级 见 指针行不进主导词树，用 --pblank 查归因）");
        return;
    }

    var sb = new StringBuilder();
    WalkCm3(node, 0, sb);
    Console.Write(sb);
}

static Cm3IndexNode? FindCm3(Cm3IndexSnapshot snapshot, int seq)
{
    foreach (var lead in snapshot.Leads)
    {
        var found = FindCm3In(lead, seq);
        if (found is not null)
        {
            return found;
        }
    }

    return null;
}

static Cm3IndexNode? FindCm3In(Cm3IndexNode node, int seq)
{
    if (node.SeqNo == seq)
    {
        return node;
    }

    foreach (var child in node.Children)
    {
        var found = FindCm3In(child, seq);
        if (found is not null)
        {
            return found;
        }
    }

    return null;
}

static void WalkCm3(Cm3IndexNode node, int depth, StringBuilder sb)
{
    sb.Append(new string(' ', depth * 2))
        .Append(node.SeqNo)
        .Append(' ')
        .Append(node.Term)
        .Append(" | code=").Append(node.Code ?? "-")
        .Append(node.AddOnCode is { Length: > 0 } addOn ? "[+" + addOn + "]" : string.Empty)
        .Append(" | 连接行=").Append(Cm3ProcedureIndexMatcher.IsConnectorNode(node) ? 1 : 0)
        .Append(" | aliases=").Append(string.Join(",", node.Aliases.Select(a =>
            $"{a.Key}(w{a.Weight}{(a.RecallAnchor ? string.Empty : "·非锚")})")))
        .Append(" | qual=").Append(string.Join(",", node.Qualifiers))
        .Append(" | jump=").Append(node.JumpKind).Append('(').Append(node.JumpTarget ?? "-").Append(')')
        .AppendLine();
    foreach (var child in node.Children)
    {
        WalkCm3(child, depth + 1, sb);
    }
}
