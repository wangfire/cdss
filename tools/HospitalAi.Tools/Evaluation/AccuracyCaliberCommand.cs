using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace HospitalAi.Tools;

/// <summary>
/// caliber：万条单名诊断口径的分层复算，是《精度验收口径规范》(docs/2026-10-08-精度验收口径规范.md)
/// §3 判据与 §4 口径表的计算实现。同一条命令同时产出：
///   L0 端到端交付口径（分母 = 全部测试诊断名，不剔除）
///   L1 范围内可判定口径（剔除 E1 状态接触码 / E2 外部原因码 / E3 索引无锚点 / E4 对照自相矛盾 / E5 对照缺失）
///   L2 知识库覆盖口径（主导词可召回率、链路出码率、回退占比）
///   以及逐条单独剔除的敏感度表（防止剔除面被悄悄扩大）。
/// 用法：
///   caliber --connection "&lt;HospitalAi conn&gt;" [--hospital &lt;guid&gt;] [--patient-prefix JZEY测试-]
///           [--reference-db WaterCloudNetDb_JZEY] [--attribution &lt;norec_stage.tsv&gt;]
///           [--report &lt;path&gt;] [--excluded &lt;path&gt;] [--dump &lt;path&gt;]
/// 对照码取同一 SQL Server 实例上的源库 DiagnosticSet_LCZD（同名编码），分两次查询、在内存里按名对齐，
/// 避免跨库字符串比较触发排序规则冲突。
/// </summary>
public static class AccuracyCaliberCommand
{
    // 本机 JZEY 单诊断名测试医院；换语料时用 --hospital 覆盖。
    private const string DefaultHospitalId = "868F2319-9A0C-46C8-B478-C78FB350237D";

    private static readonly string[] ExternalCauseChapters = ["V", "W", "X", "Y"];

    private sealed record Recommendation(int Rank, string Code, bool IsFallback);

    public static async Task<int> RunAsync(IConfiguration configuration)
    {
        var connectionString = ToolHost.ResolveConnectionString(configuration);
        var hospitalId = configuration["Hospital"] ?? DefaultHospitalId;
        var patientPrefix = configuration["patient-prefix"] ?? "JZEY测试-";
        var referenceDb = configuration["reference-db"] ?? "WaterCloudNetDb_JZEY";
        var attributionPath = configuration["attribution"];
        var reportPath = configuration["report"]
            ?? $"docs/superpowers/reports/accuracy-caliber-{DateTimeOffset.Now:yyyyMMdd-HHmm}.json";
        var excludedPath = configuration["excluded"];

        if (!Regex.IsMatch(referenceDb, "^[A-Za-z0-9_]+$"))
        {
            Console.Error.WriteLine($"--reference-db 只能是标识符字符：{referenceDb}");
            return 2;
        }

        var noAttribution = string.IsNullOrEmpty(attributionPath);
        if (!noAttribution && !File.Exists(attributionPath))
        {
            Console.Error.WriteLine($"归因表不存在：{attributionPath}");
            return 2;
        }

        var expectedByName = await LoadExpectedCodesAsync(connectionString, referenceDb);
        var ledger = await LoadLedgerAsync(connectionString, hospitalId, patientPrefix);
        var noAnchorNames = noAttribution
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : LoadAttribution(attributionPath!);

        var names = BuildNameRows(ledger, expectedByName, noAnchorNames);
        var report = Summarize(names, noAttribution);
        WriteConsole(report, noAttribution);
        await WriteReportAsync(report, reportPath);
        if (!string.IsNullOrEmpty(excludedPath))
        {
            await File.WriteAllLinesAsync(excludedPath, ExcludedLines(names), Encoding.UTF8);
            Console.WriteLine($"剔除清单已写入 {excludedPath}");
        }

        var dumpPath = configuration["dump"];
        if (!string.IsNullOrEmpty(dumpPath))
        {
            await File.WriteAllLinesAsync(dumpPath, LedgerLines(names), Encoding.UTF8);
            Console.WriteLine($"逐名台账已写入 {dumpPath}");
        }

        return 0;
    }

    private static async Task<Dictionary<string, SortedSet<string>>> LoadExpectedCodesAsync(
        string connectionString,
        string referenceDb)
    {
        var result = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"SELECT LTRIM(RTRIM(Name)), LTRIM(RTRIM(Code)) FROM {referenceDb}.dbo.DiagnosticSet_LCZD " +
            "WHERE Name IS NOT NULL AND Code IS NOT NULL AND LTRIM(RTRIM(Code)) <> ''",
            connection)
        {
            CommandTimeout = 600,
        };
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }

            var name = reader.GetString(0);
            var code = NormalizeCode(reader.GetString(1));
            var key = NormalizeName(name);
            if (key.Length == 0)
            {
                continue;
            }

            if (!result.TryGetValue(key, out var codes))
            {
                codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                result[key] = codes;
            }

            codes.Add(code);
        }

        return result;
    }

    private static async Task<List<(string Name, Recommendation? Rec)>> LoadLedgerAsync(
        string connectionString,
        string hospitalId,
        string patientPrefix)
    {
        const string sql = """
            SET NOCOUNT ON;
            WITH picked AS (
                SELECT i.id, i.original_text,
                       ROW_NUMBER() OVER (PARTITION BY i.original_text ORDER BY i.created_at, i.id) AS rn
                FROM coding_diagnosis_input i
                JOIN coding_task t ON t.id = i.coding_task_id
                JOIN visit v ON v.id = t.visit_id
                JOIN patient p ON p.id = v.patient_id
                WHERE i.source_type = 'FRONT_PAGE'
                  AND i.hospital_id = @hospital
                  AND p.display_name LIKE @prefix + N'%')
            SELECT p.original_text, r.rank, r.code,
                   CASE WHEN EXISTS (SELECT 1 FROM recommendation_evidence e
                                     WHERE e.coding_recommendation_id = r.id
                                       AND e.source_text LIKE N'%回退%')
                        THEN 1 ELSE 0 END AS is_fallback
            FROM picked p
            LEFT JOIN coding_recommendation r
                   ON r.diagnosis_input_id = p.id AND r.lifecycle_status = 'ACTIVE'
            ORDER BY p.original_text, r.rank;
            """;

        var rows = new List<(string Name, Recommendation? Rec)>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 600 };
        command.Parameters.AddWithValue("@hospital", Guid.Parse(hospitalId));
        command.Parameters.AddWithValue("@prefix", patientPrefix);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (reader.IsDBNull(1))
            {
                rows.Add((name, null));
                continue;
            }

            rows.Add((name, new Recommendation(
                reader.GetInt32(1),
                NormalizeCode(reader.GetString(2)),
                reader.GetInt32(3) == 1)));
        }

        return rows;
    }

    private static HashSet<string> LoadAttribution(string path)
    {
        var noAnchor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var columns = line.Split('\t');
            if (columns.Length < 2 || columns[1].Trim() != "无主导词召回")
            {
                continue;
            }

            noAnchor.Add(NormalizeName(columns[0]));
        }

        return noAnchor;
    }

    private sealed class NameRow
    {
        public required string Name { get; init; }
        public required List<Recommendation> Recs { get; init; }
        public required SortedSet<string> Expected { get; init; }
        public bool IsNoAnchor { get; init; }
        public bool ExcludeE1 { get; init; }
        public bool ExcludeE2 { get; init; }
        public bool ExcludeE4 { get; init; }
        public bool ExcludeE5 { get; init; }
        public string ExpectedCode => Expected.Count > 0 ? Expected.First() : string.Empty;
        public bool HasRec => Recs.Count > 0;
        // 对照码不唯一的名（源字典同义条目）按"任一对照码命中"判定，避免取码顺序左右指标。
        public bool CodingTop1Hit => Top1 is not null && Expected.Contains(Top1.Code);
        public bool CodingTop3Hit => Recs.Take(3).Any(item => Expected.Contains(item.Code));
        public bool CategoryTop1Hit => Top1 is not null && Expected.Any(item => Category(item) == Category(Top1.Code));
        public bool Excluded => ExcludeE1 || ExcludeE2 || IsNoAnchor || ExcludeE4 || ExcludeE5;
        public string ExclusionReason => ExcludeE1 ? "E1 状态与医疗接触码(Z)"
            : ExcludeE2 ? "E2 外部原因码(V/W/X/Y)"
            : IsNoAnchor ? "E3 索引无锚点(无主导词召回)"
            : ExcludeE4 ? "E4 对照码自相矛盾"
            : ExcludeE5 ? "E5 对照码缺失"
            : string.Empty;
        private Recommendation? Top1 => Recs.FirstOrDefault(item => item.Rank == 1);
    }

    private static List<NameRow> BuildNameRows(
        List<(string Name, Recommendation? Rec)> ledger,
        Dictionary<string, SortedSet<string>> expectedByName,
        HashSet<string> noAnchorNames)
    {
        var grouped = new Dictionary<string, (string Display, List<Recommendation> Recs)>(StringComparer.Ordinal);
        foreach (var (name, rec) in ledger)
        {
            if (!grouped.TryGetValue(name, out var entry))
            {
                entry = (name, []);
                grouped[name] = entry;
            }

            if (rec is not null)
            {
                entry.Recs.Add(rec);
            }
        }

        var rows = new List<NameRow>();
        foreach (var (name, entry) in grouped)
        {
            var key = NormalizeName(name);
            var expected = expectedByName.TryGetValue(key, out var codes)
                ? codes
                : new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var chapter = expected.Count > 0 ? Category(expected.First())[..1] : string.Empty;
            rows.Add(new NameRow
            {
                Name = name,
                Recs = entry.Recs.OrderBy(item => item.Rank).ToList(),
                Expected = expected,
                IsNoAnchor = noAnchorNames.Contains(key),
                ExcludeE1 = chapter == "Z",
                ExcludeE2 = ExternalCauseChapters.Contains(chapter),
                ExcludeE4 = expected.Count > 1,
                ExcludeE5 = expected.Count == 0,
            });
        }

        return rows;
    }

    private static Report Summarize(List<NameRow> names, bool noAttribution) =>
        new(
            Layers:
            [
                BuildLayer("L0", "端到端交付口径（全部诊断名）", names),
                BuildLayer("L0-E1", "单独剔除 Z 状态与医疗接触码", names.Where(item => !item.ExcludeE1).ToList()),
                BuildLayer("L0-E1-E2", "再剔除 V/W/X/Y 外部原因码", names
                    .Where(item => !item.ExcludeE1 && !item.ExcludeE2).ToList()),
                BuildLayer("L1", "范围内可判定口径（剔除 E1/E2/E3/E4/E5）",
                    names.Where(item => !item.Excluded).ToList()),
            ],
            Knowledge: KnowledgeMetrics(names, noAttribution),
            Counts: new CountMetrics(
                Total: names.Count,
                MissingExpected: names.Count(item => item.ExcludeE5),
                E1: names.Count(item => item.ExcludeE1),
                E2: names.Count(item => item.ExcludeE2),
                E3: noAttribution ? null : names.Count(item => item.IsNoAnchor),
                E4: names.Count(item => item.ExcludeE4),
                Excluded: names.Count(item => item.Excluded)),
            NoAttribution: noAttribution);

    private static List<KnowledgeMetric> KnowledgeMetrics(List<NameRow> names, bool noAttribution)
    {
        var recRows = names.SelectMany(item => item.Recs).ToList();
        var recallable = noAttribution ? names.Count : names.Count(item => !item.IsNoAnchor);
        var withRec = names.Count(item => item.HasRec);
        return
        [
            new("主导词可召回率", noAttribution
                ? "n/a（未提供 --attribution）"
                : Percent(names.Count(item => !item.IsNoAnchor), names.Count)),
            new("链路出码率（有推荐名/可召回名）", recallable == 0 ? "n/a" : Percent(withRec, recallable)),
            new("索引码回退条占比", recRows.Count == 0 ? "n/a" : Percent(recRows.Count(item => item.IsFallback), recRows.Count)),
            new("最优推荐为回退的名占比", withRec == 0 ? "n/a" : Percent(names.Count(item => item.HasRec && item.Recs[0].IsFallback), withRec)),
        ];
    }

    private static Layer BuildLayer(string id, string label, List<NameRow> rows)
    {
        var total = rows.Count;
        return new Layer(
            id,
            label,
            total,
            rows.Count(item => item.HasRec),
            rows.Count(item => item.CodingTop1Hit),
            rows.Count(item => item.CodingTop3Hit),
            rows.Count(item => item.CategoryTop1Hit));
    }

    private static void WriteConsole(Report report, bool noAttribution)
    {
        Console.WriteLine();
        Console.WriteLine("==== 精度分层口径复算（万条单名语料）====");
        Console.WriteLine($"语料 {report.Counts.Total} 名；" +
            $"剔除 E1(Z)={report.Counts.E1} E2(V/W/X/Y)={report.Counts.E2} " +
            $"E3(无锚点)={(report.Counts.E3?.ToString() ?? "未提供归因")} " +
            $"E4(对照矛盾)={report.Counts.E4} E5(对照缺失)={report.Counts.MissingExpected}" +
            $"，去重后合计 {report.Counts.Excluded}");
        if (noAttribution)
        {
            Console.WriteLine("注意：未提供 --attribution，L1 未剔除 E3（索引无锚点），数字偏保守。");
        }

        Console.WriteLine();
        Console.WriteLine("口径                    n     有推荐   Top1        Top3        类目Top1");
        foreach (var layer in report.Layers)
        {
            Console.WriteLine(
                $"{layer.Id,-6}{layer.Label,-20} {layer.Total,6} {layer.WithRec,8}  " +
                $"{layer.CodingTop1,5} ({Percent(layer.CodingTop1, layer.Total),7}) " +
                $"{layer.CodingTop3,5} ({Percent(layer.CodingTop3, layer.Total),7}) " +
                $"{layer.CategoryTop1,5} ({Percent(layer.CategoryTop1, layer.Total),7})");
        }

        Console.WriteLine();
        Console.WriteLine("==== L2 知识库覆盖口径 ====");
        foreach (var metric in report.Knowledge)
        {
            Console.WriteLine($"  {metric.Name}  {metric.Value}");
        }
    }

    private static IEnumerable<string> ExcludedLines(List<NameRow> names)
    {
        yield return "诊断名\t对照源编码\t剔除判据\t有推荐\tTop1\t类目Top1";
        foreach (var item in names.Where(item => item.Excluded).OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            yield return $"{item.Name}\t{string.Join("/", item.Expected)}\t{item.ExclusionReason}\t{(item.HasRec ? 1 : 0)}\t{(item.CodingTop1Hit ? 1 : 0)}\t{(item.CategoryTop1Hit ? 1 : 0)}";
        }
    }

    private static IEnumerable<string> LedgerLines(List<NameRow> names)
    {
        yield return "诊断名\t对照源编码\tTop1码\tTop1为索引码回退\tTop3码\t有推荐\tTop1命中\tTop3命中\t类目Top1命中\t剔除判据";
        foreach (var item in names.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            yield return string.Join('\t',
                item.Name,
                string.Join("/", item.Expected),
                item.Recs.FirstOrDefault(rec => rec.Rank == 1)?.Code ?? "",
                item.Recs.FirstOrDefault(rec => rec.Rank == 1) is null ? "" : (item.Recs.First(rec => rec.Rank == 1).IsFallback ? "回退" : "精确"),
                string.Join("/", item.Recs.Take(3).Select(rec => rec.Code)),
                item.HasRec ? 1 : 0,
                item.CodingTop1Hit ? 1 : 0,
                item.CodingTop3Hit ? 1 : 0,
                item.CategoryTop1Hit ? 1 : 0,
                item.ExclusionReason);
        }
    }

    private static async Task WriteReportAsync(Report report, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        Console.WriteLine($"报告已写入 {path}");
    }

    private static string NormalizeName(string value) =>
        new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

    // 命中判定口径与 evaluate-accuracy 一致：去空白、去大小写、联合码 † 归一为 +。
    private static string NormalizeCode(string value) =>
        new string(value.Where(character => !char.IsWhiteSpace(character)).ToArray())
            .Replace('†', '+')
            .ToUpperInvariant();

    private static string Category(string code) => code.Length >= 3 ? code[..3] : code;

    private static string Percent(int part, int total) =>
        total == 0 ? "n/a" : $"{Math.Round(100.0 * part / total, 1)}%";

    private sealed record Report(
        List<Layer> Layers,
        List<KnowledgeMetric> Knowledge,
        CountMetrics Counts,
        bool NoAttribution);

    private sealed record Layer(
        string Id,
        string Label,
        int Total,
        int WithRec,
        int CodingTop1,
        int CodingTop3,
        int CategoryTop1);

    private sealed record KnowledgeMetric(string Name, string Value);

    private sealed record CountMetrics(
        int Total,
        int MissingExpected,
        int E1,
        int E2,
        int? E3,
        int E4,
        int Excluded);
}
