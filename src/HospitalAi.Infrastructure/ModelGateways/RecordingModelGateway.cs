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
    public const string RecordingVersion = "recording-v2";

    /// <summary>
    /// 一致性校验的固定"空自述"：不回显编码、不引用任何证据、判为不被支持。
    /// Recording 模式没有真实模型，因此只能声明"什么都没有"，
    /// 不得伪造引用或编码来把 LlmScore 抬高（那会让桩网关看起来像接了模型）。
    /// </summary>
    public Task<ModelGenerationResult> GenerateAsync(
        ModelGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = """
            {"code": "", "supported": false, "cited_evidence": [], "negation_or_temporal_violation": false}
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
