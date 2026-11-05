// 一次性工具：把 medical_code（诊断侧 ICD-10）的标题/同义词用 bge-small-zh 嵌入后灌入 medical_code_embedding。
// 向量快照按（hospital_id, model_code, code_system_code）三键加载（InMemoryVectorSearch.LoadSnapshot），
// 因此必须按这三键写齐。模型文件按程序目录向上回溯解析（OnnxEmbeddingProvider.ResolvePath）。
using System.Data;
using HospitalAi.Infrastructure.Embeddings;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

var connectionString = args.Length > 0
    ? args[0]
    : Environment.GetEnvironmentVariable("HOSPITAL_AI_CONNECTION_STRING")
        ?? throw new InvalidOperationException("缺连接串：传参或设 HOSPITAL_AI_CONNECTION_STRING");

var provider = new OnnxEmbeddingProvider(Options.Create(new EmbeddingOptions()));
if (!provider.IsAvailable)
{
    Console.Error.WriteLine("嵌入模型不可用：检查 models/bge-small-zh-v1.5/（model.onnx + vocab.txt）");
    return 1;
}
Console.WriteLine($"嵌入模型就绪：{provider.ModelCode}");

var rows = new List<(Guid Hospital, string CodeSystem, string Code, string Title, string Text)>();
await using (var conn = new SqlConnection(connectionString))
{
    await conn.OpenAsync();
    const string Sql = """
        SELECT hospital_id, code_system_code, code, title, search_text
        FROM medical_code
        WHERE code_system_code = 'ICD-10' AND is_enabled = 1
        ORDER BY hospital_id, code
        """;
    await using var cmd = new SqlCommand(Sql, conn);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var title = reader.GetString(3);
        var searchText = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
        // 嵌入文本用 search_text（标题+同义词，提升口语/简称召回），落库 title 仍存标准标题
        rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), title,
            searchText.Length > 0 ? searchText : title));
    }
}
Console.WriteLine($"待嵌入 {rows.Count} 行（medical_code ICD-10 enabled）");

await using (var conn = new SqlConnection(connectionString))
{
    await conn.OpenAsync();
    await using var cmd = new SqlCommand("DELETE FROM medical_code_embedding WHERE model_code = @m", conn);
    cmd.Parameters.AddWithValue("@m", provider.ModelCode);
    Console.WriteLine($"清除旧向量 {await cmd.ExecuteNonQueryAsync()} 行");
}

var failed = 0;
var total = 0;
var sw = System.Diagnostics.Stopwatch.StartNew();
foreach (var hospitalGroup in rows.GroupBy(row => row.Hospital))
{
    var batch = new DataTable();
    batch.Columns.Add("id", typeof(Guid));
    batch.Columns.Add("hospital_id", typeof(Guid));
    batch.Columns.Add("code_system_code", typeof(string));
    batch.Columns.Add("code", typeof(string));
    batch.Columns.Add("model_code", typeof(string));
    batch.Columns.Add("title", typeof(string));
    batch.Columns.Add("dim", typeof(int));
    batch.Columns.Add("vector", typeof(byte[]));
    batch.Columns.Add("created_at", typeof(DateTimeOffset));
    batch.Columns.Add("updated_at", typeof(DateTimeOffset));

    var pending = 0;
    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();
    using var bulk = new SqlBulkCopy(conn)
    {
        DestinationTableName = "medical_code_embedding",
        BatchSize = 2000,
    };
    foreach (var (hospital, codeSystem, code, title, text) in hospitalGroup)
    {
        var vector = provider.Embed(text);
        if (vector is null)
        {
            failed++;
            continue;
        }

        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        var now = DateTimeOffset.UtcNow;
        batch.Rows.Add(Guid.NewGuid(), hospital, codeSystem, code, provider.ModelCode, title,
            vector.Length, bytes, now, now);
        total++;
        if (++pending >= 2000)
        {
            await bulk.WriteToServerAsync(batch);
            pending = 0;
            batch.Rows.Clear();
            Console.WriteLine($"[{hospitalGroup.Key}] {total}/{rows.Count} 行，{sw.Elapsed.TotalSeconds:F0}s");
        }
    }

    if (batch.Rows.Count > 0)
    {
        await bulk.WriteToServerAsync(batch);
    }
}

sw.Stop();
Console.WriteLine($"完成：写入 {total} 行，嵌入失败 {failed} 行，耗时 {sw.Elapsed.TotalMinutes:F1} 分钟");
return failed > 0 ? 2 : 0;
