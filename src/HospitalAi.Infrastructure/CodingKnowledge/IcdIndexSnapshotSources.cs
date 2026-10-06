using System.Collections.Concurrent;
using HospitalAi.Application.Coding.IcdIndex;
using HospitalAi.Application.Coding.Matching;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HospitalAi.Infrastructure.CodingKnowledge;

/// <summary>
/// icd_index 全库内存快照来源（平台级共享，51268 行，全列只读加载一次）。
/// 单例缓存；索引库导入后由 ImportEndpoint 调 Invalidate 重建。
/// </summary>
public sealed class EfIcdIndexSnapshotSource(IServiceScopeFactory scopeFactory) : IIcdIndexSource
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IcdIndexSnapshot? _snapshot;

    public IcdIndexSnapshot GetSnapshot(CancellationToken cancellationToken = default)
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

    private IcdIndexSnapshot Build()
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HospitalAiDbContext>();
        var rows = dbContext.IcdIndexes
            .AsNoTracking()
            .Select(item => new IcdIndexRow(
                item.SeqNo,
                item.IndexName,
                item.IndexCode,
                item.IndexLevel,
                item.ParentSeqNo,
                item.LeadWordStatus))
            .ToList();
        return IcdIndexSnapshotBuilder.Build(rows);
    }
}

/// <summary>
/// icd_term_synonym 全库内存快照来源（平台级共享，手术/诊断两类同义词组）。
/// 单例缓存；同义词维护后调 Invalidate 重建。
/// </summary>
public sealed class EfIcdTermSynonymSource(IServiceScopeFactory scopeFactory) : IIcdTermSynonymSource
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IcdTermSynonymSnapshot? _snapshot;

    public IcdTermSynonymSnapshot GetSnapshot(
        CancellationToken cancellationToken = default)
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

    private IcdTermSynonymSnapshot Build()
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HospitalAiDbContext>();
        var rows = dbContext.IcdTermSynonyms
            .AsNoTracking()
            .Where(item => item.IsEnabled)
            .Select(item => new IcdTermSynonymRow(
                item.TermType, item.CanonicalTerm, item.VariantTerm))
            .ToList();
        return IcdTermSynonymSnapshot.Build(rows);
    }
}

/// <summary>
/// medical_code 字典内存快照来源（按医院 + 编码体系缓存）。
/// 纯内存 ordinal 前缀比对，规避 SQL Server 大小写不敏感 collation 对编码展开的干扰。
/// </summary>
public sealed class EfIcdDictionarySource(IServiceScopeFactory scopeFactory) : IIcdDictionarySource
{
    private readonly ConcurrentDictionary<(Guid HospitalId, string CodeSystem), IcdDictionarySnapshot> _cache = new();

    public IcdDictionarySnapshot GetDictionary(
        Guid hospitalId,
        string codeSystemCode,
        CancellationToken cancellationToken = default)
    {
        return _cache.GetOrAdd(
            (hospitalId, codeSystemCode),
            key =>
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<HospitalAiDbContext>();
                // 历史导入数据存在错位：medical_code.hospital_id 与 code_system.hospital_id 分属不同医院。
                // 两条归属路径取并集，保证任一口径下的任务都能拿到完整字典。
                var codeSystemIds = dbContext.CodeSystems
                    .AsNoTracking()
                    .Where(item => item.HospitalId == key.HospitalId && item.Code == key.CodeSystem)
                    .Select(item => item.Id);
                var rows = dbContext.MedicalCodes
                    .AsNoTracking()
                    .Where(item => item.IsEnabled
                        && (item.HospitalId == key.HospitalId
                            || codeSystemIds.Contains(item.CodeSystemId)))
                    .Select(item => new IcdDictionaryRow(
                        item.CodeSystemCode, item.Code, item.Title, item.SearchText))
                    .ToList();
                return new IcdDictionarySnapshot(rows);
            });
    }

    public void Invalidate() => _cache.Clear();
}
