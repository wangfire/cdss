using HospitalAi.Infrastructure.Embeddings;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HospitalAi.Tools;

/// <summary>
/// embed：为医学编码标题离线构建语义向量（bge-small-zh ONNX，CPU——决策 2）。
/// 用法：dotnet run --project tools/HospitalAi.Tools -- embed
///   --connection "..." --hospital &lt;guid&gt; [--code-system ICD-10] [--model &lt;path&gt;] [--vocab &lt;path&gt;]
/// 默认重建目标医院全部编码体系的向量（先删后插，单事务分批提交）。
/// </summary>
public static class EmbeddingCommand
{
    public static async Task<int> RunAsync(IConfiguration configuration)
    {
        var connectionString = ToolHost.ResolveConnectionString(configuration);
        var hospitalText = configuration["hospital"]
            ?? throw new ArgumentException("缺少 --hospital 医院Id。");
        var hospitalId = Guid.Parse(hospitalText);
        var codeSystem = configuration["code-system"];

        var options = new EmbeddingOptions
        {
            ModelPath = configuration["model"]
                ?? configuration["Embedding:ModelPath"]
                ?? "models/bge-small-zh-v1.5/model.onnx",
            VocabPath = configuration["vocab"]
                ?? configuration["Embedding:VocabPath"]
                ?? "models/bge-small-zh-v1.5/vocab.txt",
            ModelCode = configuration["Embedding:ModelCode"] ?? "bge-small-zh-v1.5",
        };
        var provider = new OnnxEmbeddingProvider(Options.Create(options));
        if (!provider.IsAvailable)
        {
            Console.WriteLine("嵌入模型不可用：请检查 --model / --vocab 路径。");
            return 1;
        }

        var dbContextOptions = new DbContextOptionsBuilder<HospitalAiDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var dbContext = new HospitalAiDbContext(dbContextOptions);

        var codes = await dbContext.MedicalCodes
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId && item.IsEnabled)
            .Where(item => codeSystem == null || item.CodeSystemCode == codeSystem)
            .OrderBy(item => item.CodeSystemCode)
            .ThenBy(item => item.Code)
            .Select(item => new { item.CodeSystemCode, item.Code, item.Title })
            .ToListAsync();
        Console.WriteLine($"待嵌入编码 {codes.Count} 条（医院 {hospitalId}，模型 {provider.ModelCode}）。");

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var existing = dbContext.MedicalCodeEmbeddings.Where(item =>
            item.HospitalId == hospitalId
            && item.ModelCode == provider.ModelCode
            && (codeSystem == null || item.CodeSystemCode == codeSystem));
        dbContext.MedicalCodeEmbeddings.RemoveRange(await existing.ToListAsync());
        await dbContext.SaveChangesAsync();

        var batch = new List<MedicalCodeEmbeddingRecord>(500);
        var done = 0;
        var skipped = 0;
        var dim = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var code in codes)
        {
            var title = (code.Title ?? string.Empty).Trim();
            if (title.Length == 0)
            {
                skipped++;
                continue;
            }

            var vector = provider.Embed(title);
            if (vector is null)
            {
                Console.WriteLine("嵌入调用失败，中止（未提交已完成部分）。");
                return 1;
            }

            dim = vector.Length;
            batch.Add(new MedicalCodeEmbeddingRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = hospitalId,
                CodeSystemCode = code.CodeSystemCode,
                Code = code.Code,
                ModelCode = provider.ModelCode,
                Title = title.Length > 300 ? title[..300] : title,
                Dim = dim,
                Vector = ToBytes(vector),
                CreatedAt = now,
                UpdatedAt = now
            });
            done++;
            if (batch.Count >= 500)
            {
                dbContext.MedicalCodeEmbeddings.AddRange(batch);
                await dbContext.SaveChangesAsync();
                batch.Clear();
                Console.WriteLine($"  {done}/{codes.Count}");
            }
        }

        if (batch.Count > 0)
        {
            dbContext.MedicalCodeEmbeddings.AddRange(batch);
            await dbContext.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        Console.WriteLine($"完成：嵌入 {done} 条（跳过空标题 {skipped}），维度 {dim}。");
        return 0;
    }

    private static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
