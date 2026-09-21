using HospitalAi.Application.Abstractions;

namespace HospitalAi.Infrastructure.Elasticsearch;

/// <summary>
/// Elasticsearch 不可用时的编码知识检索实现。
/// BM25 不可用允许 Exact 快路径；Exact 未命中必须 NO_SAFE_RECOMMENDATION，
/// 不得回退 SQL 全量扫描。
/// </summary>
public sealed class UnavailableCodingKnowledgeSearch : ICodingKnowledgeSearch
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<CodingKnowledgeHit>> SearchAsync(
        CodingKnowledgeSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CodingKnowledgeHit> empty = [];
        return Task.FromResult(empty);
    }
}
