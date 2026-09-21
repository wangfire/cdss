namespace HospitalAi.Infrastructure.Elasticsearch;

/// <summary>
/// 编码知识索引构建入口。Elasticsearch 未启用时该服务未注册，
/// 调用方必须把“索引不可用”作为显式失败处理，不得回退 SQL 全量扫描。
/// </summary>
public interface ICodingKnowledgeIndexer
{
    Task<ElasticsearchIndexResult> EnsureIndexAsync(
        Guid hospitalId,
        string knowledgeVersion,
        CancellationToken cancellationToken = default);

    Task<ElasticsearchIndexResult> RebuildAsync(
        Guid hospitalId,
        string knowledgeVersion,
        string documentVersion,
        CancellationToken cancellationToken = default);
}
