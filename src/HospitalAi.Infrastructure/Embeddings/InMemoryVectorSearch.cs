using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HospitalAi.Application.Abstractions;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HospitalAi.Infrastructure.Embeddings;

/// <summary>
/// 医学编码向量语义检索：嵌入向量常驻内存（约 5.1 万行 × 512 维 ≈ 100MB），
/// 余弦相似度（向量已归一化，点积即余弦）暴力扫描 + Top-K，CPU 毫秒级。
/// 快照按（医院, 编码体系, 模型）缓存，15 分钟过期以承接工具重灌。
/// 模型不可用时 IsAvailable=false，由 Runner 显式打 DEGRADED.NO_VECTOR。
/// </summary>
public sealed class InMemoryVectorSearch(
    IServiceScopeFactory scopeFactory,
    IEmbeddingProvider embeddingProvider,
    ILogger<InMemoryVectorSearch> logger) : IVectorSearch
{
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromMinutes(15);

    private sealed record SnapshotEntry(string CodeSystemCode, string Code, string Title, float[] Vector);

    private sealed record Snapshot(DateTimeOffset LoadedAt, SnapshotEntry[] Entries);

    private readonly ConcurrentDictionary<(Guid Hospital, string CodeSystem, string Model), Lazy<Snapshot>> _snapshots = new();

    public bool IsAvailable => embeddingProvider.IsAvailable;

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        Guid hospitalId,
        string codeSystemCode,
        string queryText,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || topK <= 0)
        {
            return [];
        }

        var queryVector = embeddingProvider.Embed(queryText);
        if (queryVector is null)
        {
            return [];
        }

        var snapshot = await GetSnapshotAsync(hospitalId, codeSystemCode, cancellationToken);
        if (snapshot.Entries.Length == 0)
        {
            return [];
        }

        var ranked = new List<(SnapshotEntry Entry, double Score)>(snapshot.Entries.Length);
        foreach (var entry in snapshot.Entries)
        {
            double dot = 0;
            var vector = entry.Vector;
            for (var i = 0; i < vector.Length; i++)
            {
                dot += (double)queryVector[i] * vector[i];
            }

            ranked.Add((entry, dot));
        }

        return ranked
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .Take(topK)
            .Select(item => new VectorSearchHit(
                item.Entry.CodeSystemCode,
                item.Entry.Code,
                item.Entry.Title,
                Math.Round(item.Score, 6)))
            .ToList();
    }

    private async Task<Snapshot> GetSnapshotAsync(
        Guid hospitalId,
        string codeSystemCode,
        CancellationToken cancellationToken)
    {
        var key = (hospitalId, codeSystemCode, embeddingProvider.ModelCode);
        var lazy = _snapshots.GetOrAdd(key, _ => new Lazy<Snapshot>(() => LoadSnapshot(key).GetAwaiter().GetResult()));
        if (lazy.Value.LoadedAt < DateTimeOffset.UtcNow - SnapshotLifetime)
        {
            var reloaded = new Lazy<Snapshot>(() => LoadSnapshot(key).GetAwaiter().GetResult());
            var swapped = _snapshots.TryUpdate(key, reloaded, lazy);
            if (swapped)
            {
                return reloaded.Value;
            }
        }

        return lazy.Value;
    }

    private async Task<Snapshot> LoadSnapshot((Guid Hospital, string CodeSystem, string Model) key)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HospitalAiDbContext>();
        var rows = await dbContext.MedicalCodeEmbeddings
            .AsNoTracking()
            .Where(item => item.HospitalId == key.Hospital
                && item.ModelCode == key.Model
                && item.CodeSystemCode == key.CodeSystem)
            .Select(item => new { item.CodeSystemCode, item.Code, item.Title, item.Vector })
            .ToListAsync();

        var entries = rows
            .Select(row => new SnapshotEntry(
                row.CodeSystemCode,
                row.Code,
                row.Title,
                MemoryMarshal.Cast<byte, float>(row.Vector).ToArray()))
            .ToArray();
        logger.LogInformation(
            "向量快照已加载：hospital={HospitalId}, codeSystem={CodeSystem}, model={Model}, entries={Count}",
            key.Hospital,
            key.CodeSystem,
            key.Model,
            entries.Length);
        return new Snapshot(DateTimeOffset.UtcNow, entries);
    }
}
