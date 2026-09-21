using HospitalAi.Contracts.CodingKnowledge;

namespace HospitalAi.Application.Abstractions;

/// <summary>
/// 编码知识检索命中项。
/// </summary>
public sealed record CodingKnowledgeHit(
    string CodeSystemCode,
    string Code,
    string Title,
    string CodeType,
    double Score,
    string MatchedText,
    string RecallSource);

/// <summary>
/// V2.2-Lite 编码知识检索：Exact 快照在 SQL Server，BM25 在 Elasticsearch。
/// Elasticsearch 不可用时只允许 Exact 快路径，禁止 SQL 全量扫描兜底。
/// </summary>
public interface ICodingKnowledgeSearch
{
    /// <summary>检索服务是否可用。不可用时 Trace 与推荐必须标记降级。</summary>
    bool IsAvailable { get; }

    /// <summary>BM25 检索。查询必须携带 hospital_id、coding_version、knowledge_version 过滤。</summary>
    Task<IReadOnlyList<CodingKnowledgeHit>> SearchAsync(
        CodingKnowledgeSearchQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 编码知识检索查询。
/// </summary>
public sealed record CodingKnowledgeSearchQuery(
    Guid HospitalId,
    string QueryText,
    string CodingVersion,
    string KnowledgeVersion,
    string DocumentVersion,
    string? ContentHash,
    int MaxResults = 50);
