using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Coding.Cm3Index;
using HospitalAi.Application.Coding.IcdIndex;
using HospitalAi.Application.Coding.NameLookup;
using HospitalAi.Contracts.CodingRecommendations;
using Microsoft.Extensions.Logging;

namespace HospitalAi.Infrastructure.CodingLookup;

/// <summary>
/// "编码录入"名称解析服务实现：按请求类型加载平台级索引快照与医院字典快照
/// （与流水线共用同一组单例缓存，导入端点失效重建），再委托
/// <see cref="NameCodingLookupEngine"/> 完成拆分/下钻/展开。
/// 快照加载失败按显式降级处理（不回退模糊搜索），降级标记随响应返回。
/// </summary>
public sealed class NameCodingLookupService(
    IIcdIndexSource icdIndexSource,
    ITumorIndexSource tumorIndexSource,
    IIcdDictionarySource dictionarySource,
    ICm3IndexSource cm3IndexSource,
    ITitleChannelConfigSource titleChannelConfigSource,
    IRequestContext requestContext,
    ILogger<NameCodingLookupService> logger) : INameCodingLookupService
{
    public Task<NameLookupResponse> LookupAsync(
        NameLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var isProcedure = request.Kind == NameCodingLookupEngine.ProcedureKind;
        IcdIndexSnapshot? icdSnapshot = null;
        TumorIndexSnapshot? tumorSnapshot = null;
        IcdDictionarySnapshot? icdDictionary = null;
        Cm3IndexSnapshot? cm3Snapshot = null;
        IcdDictionarySnapshot? cm3Dictionary = null;
        try
        {
            if (isProcedure)
            {
                cm3Snapshot = cm3IndexSource.GetSnapshot(cancellationToken);
                cm3Dictionary = dictionarySource.GetDictionary(
                    requestContext.HospitalId,
                    NameCodingLookupEngine.ProcedureCodeSystem,
                    cancellationToken);
            }
            else
            {
                icdSnapshot = icdIndexSource.GetSnapshot(cancellationToken);
                icdDictionary = dictionarySource.GetDictionary(
                    requestContext.HospitalId,
                    NameCodingLookupEngine.DiagnosisCodeSystem,
                    cancellationToken);
                try
                {
                    tumorSnapshot = tumorIndexSource.GetSnapshot(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 肿瘤表缺失只影响形态学部位链路，主链路照常（口径同 V22LiteCodingPipelineRunner）。
                    logger.LogWarning(ex, "肿瘤索引快照加载失败，编码录入按无部位链路执行");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "编码录入快照/字典加载失败，本次返回空推荐。Kind={Kind}", request.Kind);
        }

        var response = NameCodingLookupEngine.Lookup(
            request.Kind, request.Text, request.TopK,
            icdSnapshot, tumorSnapshot, icdDictionary,
            cm3Snapshot, cm3Dictionary,
            titleChannelConfigSource.Get());
        return Task.FromResult(response);
    }
}
