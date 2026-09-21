using HospitalAi.Application.Abstractions;
using HospitalAi.Contracts.Models;

namespace HospitalAi.Infrastructure.ModelGateways;

/// <summary>
/// 默认模型网关：明确报告模型不可用。
/// Lite 阶段生产环境使用该实现，禁止返回任何伪造的模型输出。
/// </summary>
public sealed class UnavailableModelGateway : IModelGateway, IEmbeddingGateway, IRerankerGateway, IOcrGateway
{
    public const string ModelUnavailable = "MODEL_UNAVAILABLE";

    public Task<ModelGenerationResult> GenerateAsync(
        ModelGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ModelGenerationResult(
            Success: false,
            ContentJson: null,
            ErrorCode: ModelUnavailable,
            ModelCode: request.ModelCode,
            ModelVersion: null,
            PromptTokens: null,
            CompletionTokens: null,
            DurationMilliseconds: 0));
    }

    public Task<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EmbeddingResult(
            Success: false,
            Vectors: null,
            ErrorCode: ModelUnavailable,
            ModelCode: request.ModelCode,
            ModelVersion: null,
            DurationMilliseconds: 0));
    }

    public Task<RerankResult> RerankAsync(
        RerankRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RerankResult(
            Success: false,
            Items: null,
            ErrorCode: ModelUnavailable,
            ModelCode: request.ModelCode,
            ModelVersion: null,
            DurationMilliseconds: 0));
    }

    public Task<OcrResult> RecognizeAsync(
        OcrRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OcrResult(
            Success: false,
            Text: null,
            ErrorCode: ModelUnavailable,
            ModelCode: request.ModelCode,
            ModelVersion: null,
            DurationMilliseconds: 0));
    }
}
