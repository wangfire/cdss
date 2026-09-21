using System.Text;
using HospitalAi.Application.Abstractions;
using HospitalAi.Contracts.Models;

namespace HospitalAi.Infrastructure.ModelGateways;

/// <summary>
/// Recording 模式模型网关：仅在测试环境使用。
/// 返回固定、可被 Schema 校验的 JSON；不模拟真实模型能力，
/// 因此测试断言只能覆盖链路行为，不得据此宣称模型效果。
/// </summary>
public sealed class RecordingModelGateway : IModelGateway, IEmbeddingGateway, IRerankerGateway, IOcrGateway
{
    public const string RecordingVersion = "recording-v1";

    public Task<ModelGenerationResult> GenerateAsync(
        ModelGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        // 固定返回空解释；调用方必须按“无模型解释”处理。
        var payload = """
            {"explanation": "", "supported": false}
            """;
        return Task.FromResult(new ModelGenerationResult(
            Success: true,
            ContentJson: payload,
            ErrorCode: null,
            ModelCode: request.ModelCode,
            ModelVersion: RecordingVersion,
            PromptTokens: null,
            CompletionTokens: null,
            DurationMilliseconds: 1));
    }

    public Task<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EmbeddingResult(
            Success: false,
            Vectors: null,
            ErrorCode: "RECORDING_MODE_NO_EMBEDDING",
            ModelCode: request.ModelCode,
            ModelVersion: RecordingVersion,
            DurationMilliseconds: 0));
    }

    public Task<RerankResult> RerankAsync(
        RerankRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RerankResult(
            Success: false,
            Items: null,
            ErrorCode: "RECORDING_MODE_NO_RERANK",
            ModelCode: request.ModelCode,
            ModelVersion: RecordingVersion,
            DurationMilliseconds: 0));
    }

    public Task<OcrResult> RecognizeAsync(
        OcrRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OcrResult(
            Success: false,
            Text: null,
            ErrorCode: "RECORDING_MODE_NO_OCR",
            ModelCode: request.ModelCode,
            ModelVersion: RecordingVersion,
            DurationMilliseconds: 0));
    }
}
