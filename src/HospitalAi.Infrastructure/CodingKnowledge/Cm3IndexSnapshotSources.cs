using HospitalAi.Application.Coding.Cm3Index;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HospitalAi.Infrastructure.CodingKnowledge;

/// <summary>
/// cm3_index 全库内存快照来源（平台级共享，19671 行，全列只读加载一次）。
/// 单例缓存；索引库导入后由 ImportEndpoint 调 Invalidate 重建。
/// </summary>
public sealed class EfCm3IndexSnapshotSource(IServiceScopeFactory scopeFactory) : ICm3IndexSource
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Cm3IndexSnapshot? _snapshot;

    public Cm3IndexSnapshot GetSnapshot(CancellationToken cancellationToken = default)
    {
        if (_snapshot is { } cached)
        {
            return cached;
        }

        _gate.Wait(cancellationToken);
        try
        {
            return _snapshot ??= Build();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _gate.Wait();
        try
        {
            _snapshot = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Cm3IndexSnapshot Build()
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HospitalAiDbContext>();
        var rows = dbContext.Cm3Indexes
            .AsNoTracking()
            .OrderBy(item => item.SeqNo)
            .Select(item => new Cm3IndexRow(
                item.SeqNo,
                item.IndexName,
                item.IndexCode,
                item.IndexLevel,
                item.ParentSeqNo,
                item.LeadWordStatus))
            .ToList();
        return Cm3IndexSnapshotBuilder.Build(rows);
    }
}
