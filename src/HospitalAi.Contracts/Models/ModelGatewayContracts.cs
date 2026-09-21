using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.Models;

/// <summary>
/// 模型网关运行模式。业务代码只依赖 model_code，不绑定具体模型名称。
/// </summary>
public enum ModelMode
{
    /// <summary>默认：模型不可用，返回明确错误，不伪造推荐。</summary>
    Unavailable = 0,

    /// <summary>测试：RecordingModelGateway 固定返回可校验 JSON。</summary>
    Recording = 1,

    /// <summary>本地 OpenAI 兼容端点（vLLM / Ollama）。</summary>
    OpenAiCompatible = 2
}

/// <summary>
/// 统一模型角色编码。
/// </summary>
public static class ModelCodes
{
    public const string Small = "medical-small";
    public const string Reasoning = "medical-reasoning";
    public const string Embedding = "embedding";
    public const string Reranker = "reranker";
    public const string Ocr = "ocr";
}

/// <summary>
/// 文本生成请求。
/// </summary>
public sealed record ModelGenerationRequest(
    string ModelCode,
    string Prompt,
    string? SystemPrompt = null,
    double Temperature = 0.0,
    int MaxTokens = 2048,
    string? ResponseSchemaJson = null);

/// <summary>
/// 文本生成结果。每次调用都带模型版本、token 与耗时信息，供 Trace 落库。
/// </summary>
public sealed record ModelGenerationResult(
    bool Success,
    string? ContentJson,
    string? ErrorCode,
    string ModelCode,
    string? ModelVersion,
    int? PromptTokens,
    int? CompletionTokens,
    long DurationMilliseconds);

/// <summary>
/// 嵌入请求。
/// </summary>
public sealed record EmbeddingRequest(
    string ModelCode,
    IReadOnlyList<string> Texts);

/// <summary>
/// 嵌入结果。
/// </summary>
public sealed record EmbeddingResult(
    bool Success,
    IReadOnlyList<float[]>? Vectors,
    string? ErrorCode,
    string ModelCode,
    string? ModelVersion,
    long DurationMilliseconds);

/// <summary>
/// 重排候选项。
/// </summary>
public sealed record RerankItem(
    string Id,
    string Text,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// 重排请求。
/// </summary>
public sealed record RerankRequest(
    string ModelCode,
    string Query,
    IReadOnlyList<RerankItem> Items,
    int TopN);

/// <summary>
/// 重排结果条目。
/// </summary>
public sealed record RerankResultItem(
    string Id,
    double Score);

/// <summary>
/// 重排响应。
/// </summary>
public sealed record RerankResult(
    bool Success,
    IReadOnlyList<RerankResultItem>? Items,
    string? ErrorCode,
    string ModelCode,
    string? ModelVersion,
    long DurationMilliseconds);

/// <summary>
/// OCR 请求。
/// </summary>
public sealed record OcrRequest(
    string ModelCode,
    string ContentReference);

/// <summary>
/// OCR 结果。
/// </summary>
public sealed record OcrResult(
    bool Success,
    string? Text,
    string? ErrorCode,
    string ModelCode,
    string? ModelVersion,
    long DurationMilliseconds);

/// <summary>
/// JSON Schema 校验结果。
/// </summary>
public sealed record JsonSchemaValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors)
{
    public static JsonSchemaValidationResult Valid { get; } = new(true, []);

    public static JsonSchemaValidationResult Invalid(IReadOnlyList<string> errors) => new(false, errors);
}

/// <summary>
/// 模型路由决策。Lite 阶段按 model_code 静态映射，Full 阶段再引入动态路由。
/// </summary>
public sealed record ModelRouteDecision(
    string ModelCode,
    bool Available,
    string? Reason);
