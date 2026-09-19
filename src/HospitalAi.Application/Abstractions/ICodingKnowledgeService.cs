using HospitalAi.Contracts.CodingKnowledge;

namespace HospitalAi.Application.Abstractions;

/// <summary>
/// 编码知识库导入服务。
/// </summary>
public interface ICodingKnowledgeService
{
    Task<ImportCodeSystemResponse> ImportCodeSystemAsync(
        ImportCodeSystemRequest request,
        CancellationToken cancellationToken = default);

    Task<ImportCodingRulesResponse> ImportCodingRulesAsync(
        ImportCodingRulesRequest request,
        CancellationToken cancellationToken = default);
}
