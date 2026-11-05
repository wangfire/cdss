using HospitalAi.Contracts.CodingRecommendations;

namespace HospitalAi.Application.Abstractions;

/// <summary>
/// "编码录入"名称解析服务：诊断/手术名称 → 主导词全量匹配拆分 → 修饰词下钻 → 有效编码推荐。
/// 无状态即时解析（不落库、不建任务）；快照与字典按平台/医院隔离加载，见 NameCodingLookupService。
/// </summary>
public interface INameCodingLookupService
{
    Task<NameLookupResponse> LookupAsync(
        NameLookupRequest request,
        CancellationToken cancellationToken = default);
}
