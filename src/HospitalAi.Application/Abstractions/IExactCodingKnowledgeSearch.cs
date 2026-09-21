using HospitalAi.Contracts.CodingKnowledge;

namespace HospitalAi.Application.Abstractions;

/// <summary>
/// Exact 精确检索查询。只检索当前医院、当前编码版本、启用编码；
/// 禁止遍历全部 MedicalCodes。
/// </summary>
public sealed record ExactCodingKnowledgeQuery(
    Guid HospitalId,
    string QueryText,
    string CodingVersion,
    int MaxResults = 20);

/// <summary>
/// SQL Server Exact 检索：诊断标准术语、同义词、组合编码的精确命中。
/// ES BM25 不可用时这是唯一允许的快路径，未命中即 NO_SAFE_RECOMMENDATION，
/// 不回退 SQL 全量扫描。
/// </summary>
public interface IExactCodingKnowledgeSearch
{
    Task<IReadOnlyList<CodingKnowledgeHit>> SearchAsync(
        ExactCodingKnowledgeQuery query,
        CancellationToken cancellationToken = default);
}
