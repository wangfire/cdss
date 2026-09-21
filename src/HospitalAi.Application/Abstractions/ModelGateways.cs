using HospitalAi.Contracts.Models;

namespace HospitalAi.Application.Abstractions;

/// <summary>
/// 统一文本模型网关。业务代码只声明 model_code，不绑定具体模型名称与推理框架。
/// </summary>
public interface IModelGateway
{
    Task<ModelGenerationResult> GenerateAsync(
        ModelGenerationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 统一向量模型网关。Lite 阶段无真实 embedding 服务，实现返回明确失败。
/// </summary>
public interface IEmbeddingGateway
{
    Task<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 统一重排模型网关。Lite 阶段不接入，实现返回明确失败。
/// </summary>
public interface IRerankerGateway
{
    Task<RerankResult> RerankAsync(
        RerankRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 统一 OCR 网关。
/// </summary>
public interface IOcrGateway
{
    Task<OcrResult> RecognizeAsync(
        OcrRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 模型路由器。按 model_code 查询模型是否可用，不可用必须给出可记录的原因。
/// </summary>
public interface IModelRouter
{
    Task<ModelRouteDecision> ResolveAsync(
        string modelCode,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 版本化 JSON Schema 校验器。未通过校验的模型输出不得进入业务。
/// </summary>
public interface IJsonSchemaValidator
{
    JsonSchemaValidationResult Validate(
        string schemaJson,
        string json);
}
