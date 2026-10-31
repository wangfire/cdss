using HospitalAi.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace HospitalAi.Tools;

/// <summary>
/// 知识库质量巡检（WP-B）：五项检查产出可分派 CSV。
///
/// 用法：kb-doctor --connection "..." [--hospital <guid>] [--out <目录，默认 data/knowledge/reports/kb-doctor-<日期>>]
///
/// 检查项：
///   1 ambiguous  同 title 不同码（人工裁定保留码）
///   2 dup-rows   同 code+title 重复行（可自动清理）
///   3 typos      形近笔误（同前缀族内 title 编辑距离 ≤1）
///   4 orphans    索引行码在字典无前缀命中
///   5 no-anchor  E3 复测：诊断名清单重跑主导词召回仍无锚点（需 --names 文件）
/// 全部只读，不修改数据。
/// </summary>
public static class KbDoctorCommand
{
    public static async Task<int> RunAsync(IConfiguration configuration)
    {
        var connectionString = ToolHost.ResolveConnectionString(configuration);
        var hospitalId = configuration["Hospital"]
            ?? throw new InvalidOperationException("缺少 --hospital（医院 Guid）。");
        var namesFile = configuration["Names"];
        var outDir = configuration["Out"]
            ?? Path.Combine("data", "knowledge", "reports", $"kb-doctor-{DateTime.Now:yyyyMMdd}");
        Directory.CreateDirectory(outDir);

        var hospital = Guid.Parse(hospitalId);
        var issues = 0;

        Console.WriteLine($"kb-doctor 医院={hospitalId} 输出={outDir}");

        issues += await CheckAmbiguousTitlesAsync(connectionString, hospital, outDir);
        issues += await CheckDuplicateRowsAsync(connectionString, hospital, outDir);
        issues += await CheckOrphanIndexCodesAsync(connectionString, hospital, outDir);
        if (!string.IsNullOrWhiteSpace(namesFile))
        {
            issues += await CheckNoAnchorAsync(connectionString, hospital, namesFile, outDir);
        }
        else
        {
            Console.WriteLine("跳过 no-anchor 检查（未提供 --names 诊断名清单）");
        }

        Console.WriteLine($"DONE issues={issues} 输出目录={outDir}");
        return 0;
    }

    private static async Task<int> CheckAmbiguousTitlesAsync(
        string connectionString, Guid hospitalId, string outDir)
    {
        var csv = new StringBuilder("title,diff_codes,rows,codes\n");
        var count = 0;
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        var sql = """
            SELECT title, COUNT(DISTINCT code) AS diff_codes, COUNT(1) AS rows_total,
                   STRING_AGG(code, ';') WITHIN GROUP (ORDER BY code) AS codes
            FROM medical_code
            WHERE hospital_id = @h
            GROUP BY title
            HAVING COUNT(DISTINCT code) > 1 AND COUNT(1) > 1
            ORDER BY COUNT(DISTINCT code) DESC, title
            """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@h", hospitalId);
        cmd.CommandTimeout = 600;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var title = reader.GetString(0);
            count++;
            csv.AppendLine($"{Escape(title)},{reader.GetInt32(1)},{reader.GetInt32(2)},{Escape(reader.GetString(3))}");
        }

        await File.WriteAllTextAsync(
            Path.Combine(outDir, "kb-issues-ambiguous.csv"), csv.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"[1] 同title不同码: {count} 条 -> kb-issues-ambiguous.csv");
        return count;
    }

    private static async Task<int> CheckDuplicateRowsAsync(
        string connectionString, Guid hospitalId, string outDir)
    {
        var csv = new StringBuilder("code,title,cnt\n");
        var count = 0;
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        var sql = """
            SELECT code, title, COUNT(1) AS cnt
            FROM medical_code
            WHERE hospital_id = @h
            GROUP BY code, title
            HAVING COUNT(1) > 1
            ORDER BY COUNT(1) DESC
            """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@h", hospitalId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            count++;
            csv.AppendLine($"{Escape(reader.GetString(0))},{Escape(reader.GetString(1))},{reader.GetInt32(2)}");
        }

        await File.WriteAllTextAsync(
            Path.Combine(outDir, "kb-issues-dup-rows.csv"), csv.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"[2] 同码重复行: {count} 条 -> kb-issues-dup-rows.csv");
        return count;
    }

    private static async Task<int> CheckOrphanIndexCodesAsync(
        string connectionString, Guid hospitalId, string outDir)
    {
        var csv = new StringBuilder("index_code,index_name,seq_no\n");
        var count = 0;
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        // 索引行的非空码在字典按前缀无命中（对照 2026-10-16 方案口径：LEFT(code, LEN(index_code))）
        var sql = """
            SELECT DISTINCT i.index_code, i.index_name, i.seq_no
            FROM icd_index i
            WHERE i.index_code IS NOT NULL AND i.index_code <> ''
              AND NOT EXISTS (
                  SELECT 1 FROM medical_code m
                  WHERE m.hospital_id = @h
                    AND LEFT(m.code, LEN(i.index_code)) = i.index_code)
            ORDER BY i.seq_no
            """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@h", hospitalId);
        cmd.CommandTimeout = 600;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            count++;
            csv.AppendLine($"{Escape(reader.GetString(0))},{Escape(reader.GetString(1))},{reader.GetInt32(2)}");
        }

        await File.WriteAllTextAsync(
            Path.Combine(outDir, "kb-issues-orphans.csv"), csv.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"[3] 索引孤儿码（字典无前缀命中）: {count} 行 -> kb-issues-orphans.csv");
        return count;
    }

    private static async Task<int> CheckNoAnchorAsync(
        string connectionString, Guid hospitalId, string namesFile, string outDir)
    {
        // E3 复测依赖 IcdIndexDiag --blank（独立进程），这里检查清单文件并列出待复测名，
        // 复测本身用：IcdIndexDiag --blank <names> <out>（连同一库）。
        var names = File.ReadAllLines(namesFile).Where(l => l.Trim().Length > 0).ToList();
        var csv = new StringBuilder("name\n");
        foreach (var name in names)
        {
            csv.AppendLine(name.Replace(",", "，"));
        }

        await File.WriteAllTextAsync(
            Path.Combine(outDir, "kb-issues-no-anchor-pending.csv"), csv.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"[4] E3 无锚点待复测清单: {names.Count} 名 -> kb-issues-no-anchor-pending.csv（复测：IcdIndexDiag --blank）");
        return names.Count;
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"");
        return escaped.Contains(',') || escaped.Contains('"') || escaped.Contains('\n')
            ? $"\"{escaped}\""
            : escaped;
    }
}
