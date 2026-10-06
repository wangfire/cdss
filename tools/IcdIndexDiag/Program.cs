using System.Text;
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
    Console.Error.WriteLine("用法: IcdIndexDiag <诊断文本> [...] [--tree <seq_no>] [--terms <输出文件>] [--blank <输入名文件> <输出判定文件>] [--leadstats <输入名文件> <产出台账文件>] [--batch <输入名文件> <Top1台账文件>] [--export <输入名文件> <推荐与链路明细文件>]");
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
var inputs = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--tree" && i + 1 < args.Length)
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

var snapshot = BuildSnapshot(conn);

if (treeSeq > 0)
{
    DumpTree(snapshot, treeSeq);
    return 0;
}

var dictionary = BuildDictionary(conn, hospital, codeSystem);
var synonyms = BuildSynonyms(conn);
// ICD_INDEX_DIAG_TOP 可放宽产出条数上限，用于查看被 Top-3 挤掉的候选及其分值。
var diagTop = int.TryParse(Environment.GetEnvironmentVariable("ICD_INDEX_DIAG_TOP"), out var parsedTop) ? parsedTop : 3;
var resolver = new IcdIndexDiagnosisResolver(8, diagTop, codeSystem);

if (blankIn is not null && blankOut is not null)
{
    RunBlankDiagnosis(snapshot, dictionary, codeSystem, resolver, blankIn, blankOut, synonyms);
    return 0;
}

if (leadStatsIn is not null && leadStatsOut is not null)
{
    RunLeadStatistics(snapshot, dictionary, codeSystem, leadStatsIn, leadStatsOut);
    return 0;
}

if (batchIn is not null && batchOut is not null)
{
    // 批量台账固定用生产口径 maxResults=3，便于改动前后逐名 Top1 对比。
    RunBatch(snapshot, dictionary, synonyms, new IcdIndexDiagnosisResolver(8, 3, codeSystem), batchIn, batchOut);
    return 0;
}

if (exportIn is not null && exportOut is not null)
{
    RunExport(snapshot, dictionary, synonyms, exportIn, exportOut);
    return 0;
}

foreach (var input in inputs)
{
    Console.WriteLine($"===== {input} =====");
    Diagnose(snapshot, dictionary, input, codeSystem);
    var results = resolver.Resolve(snapshot, dictionary, input, null, synonyms);
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
    IcdTermSynonymSnapshot synonyms)
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

        var produced = resolver.Resolve(snapshot, dictionary, name, null, synonyms).Count;
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
    string outFile)
{
    var sb = new StringBuilder();
    foreach (var name in File.ReadLines(inFile).Select(line => line.Trim()).Where(line => line.Length > 0))
    {
        var results = resolver.Resolve(snapshot, dictionary, name, null, synonyms);
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
        var results = resolver.Resolve(snapshot, dictionary, name, null, synonyms);
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
