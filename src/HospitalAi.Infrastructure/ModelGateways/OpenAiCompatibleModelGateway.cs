using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using HospitalAi.Application.Abstractions;
using HospitalAi.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HospitalAi.Infrastructure.ModelGateways;

/// <summary>
/// OpenAI 兼容（/v1/chat/completions、/v1/embeddings）模型网关。
/// 业务代码只传 model_code，实际模型名由配置映射，不绑定推理框架。
/// 任何网络或解析失败都返回明确错误，不返回伪造内容。
/// </summary>
public sealed class OpenAiCompatibleModelGateway : IModelGateway, IEmbeddingGateway, IRerankerGateway, IOcrGateway
{
    private readonly HttpClient _httpClient;
    private readonly ModelGatewayOptions _options;
    private readonly ILogger<OpenAiCompatibleModelGateway> _logger;

    public OpenAiCompatibleModelGateway(
        HttpClient httpClient,
        IOptions<ModelGatewayOptions> options,
        ILogger<OpenAiCompatibleModelGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        var timeout = _options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 60;
        _httpClient.Timeout = TimeSpan.FromSeconds(timeout);
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public async Task<ModelGenerationResult> GenerateAsync(
        ModelGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = ResolveModelName(request.ModelCode);
        if (model is null)
        {
            return UnavailableGeneration(request.ModelCode);
        }

        var payload = new
        {
            model,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            messages = BuildMessages(request),
            response_format = new { type = "json_object" }
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient
                .PostAsJsonAsync("v1/chat/completions", payload, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content
                .ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken)
                .ConfigureAwait(false);
            var content = body?.Choices is { Count: > 0 } ? body.Choices[0].Message?.Content : null;
            if (string.IsNullOrWhiteSpace(content))
            {
                return FailedGeneration(request.ModelCode, model, "MODEL_EMPTY_CONTENT", stopwatch.ElapsedMilliseconds);
            }

            var parsed = JsonDocument.Parse(content);
            var compact = parsed.RootElement.Clone();
            return new ModelGenerationResult(
                Success: true,
                ContentJson: compact.GetRawText(),
                ErrorCode: null,
                ModelCode: request.ModelCode,
                ModelVersion: body?.Model ?? model,
                PromptTokens: body?.Usage?.PromptTokens,
                CompletionTokens: body?.Usage?.CompletionTokens,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Chat 调用失败，ModelCode={ModelCode}", request.ModelCode);
            return FailedGeneration(request.ModelCode, model, "MODEL_CALL_FAILED", stopwatch.ElapsedMilliseconds);
        }
    }

    public async Task<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = ResolveModelName(request.ModelCode);
        if (model is null)
        {
            return new EmbeddingResult(
                Success: false, Vectors: null, ErrorCode: UnavailableModelGateway.ModelUnavailable,
                ModelCode: request.ModelCode, ModelVersion: null, DurationMilliseconds: 0);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var payload = new { model, input = request.Texts };
            using var response = await _httpClient
                .PostAsJsonAsync("v1/embeddings", payload, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content
                .ReadFromJsonAsync<EmbeddingResponse>(cancellationToken)
                .ConfigureAwait(false);
            var vectors = body?.Data?
                .OrderBy(item => item.Index)
                .Select(item => item.Embedding)
                .ToList();
            if (vectors is null || vectors.Count != request.Texts.Count)
            {
                return new EmbeddingResult(
                    Success: false, Vectors: null, ErrorCode: "EMBEDDING_RESULT_MISMATCH",
                    ModelCode: request.ModelCode, ModelVersion: body?.Model ?? model,
                    DurationMilliseconds: stopwatch.ElapsedMilliseconds);
            }

            return new EmbeddingResult(
                Success: true, Vectors: vectors, ErrorCode: null,
                ModelCode: request.ModelCode, ModelVersion: body?.Model ?? model,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Embedding 调用失败，ModelCode={ModelCode}", request.ModelCode);
            return new EmbeddingResult(
                Success: false, Vectors: null, ErrorCode: "EMBEDDING_CALL_FAILED",
                ModelCode: request.ModelCode, ModelVersion: model,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds);
        }
    }

    public async Task<RerankResult> RerankAsync(
        RerankRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = ResolveModelName(request.ModelCode);
        if (model is null)
        {
            return new RerankResult(
                Success: false, Items: null, ErrorCode: UnavailableModelGateway.ModelUnavailable,
                ModelCode: request.ModelCode, ModelVersion: null, DurationMilliseconds: 0);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var payload = new
            {
                model,
                query = request.Query,
                documents = request.Items.Select(item => item.Text).ToArray(),
                top_n = request.TopN
            };
            using var response = await _httpClient
                .PostAsJsonAsync("v1/rerank", payload, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content
                .ReadFromJsonAsync<RerankResponse>(cancellationToken)
                .ConfigureAwait(false);
            if (body?.Results is null)
            {
                return new RerankResult(
                    Success: false, Items: null, ErrorCode: "RERANK_RESULT_MISMATCH",
                    ModelCode: request.ModelCode, ModelVersion: model,
                    DurationMilliseconds: stopwatch.ElapsedMilliseconds);
            }

            var items = body.Results
                .Where(result => result.Index >= 0 && result.Index < request.Items.Count)
                .Select(result => new RerankResultItem(
                    request.Items[result.Index].Id,
                    result.RelevanceScore))
                .ToList();
            return new RerankResult(
                Success: true, Items: items, ErrorCode: null,
                ModelCode: request.ModelCode, ModelVersion: model,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Rerank 调用失败，ModelCode={ModelCode}", request.ModelCode);
            return new RerankResult(
                Success: false, Items: null, ErrorCode: "RERANK_CALL_FAILED",
                ModelCode: request.ModelCode, ModelVersion: model,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds);
        }
    }

    public Task<OcrResult> RecognizeAsync(
        OcrRequest request,
        CancellationToken cancellationToken = default)
    {
        // Lite 阶段未接入 OCR 端点，明确失败而不是返回空文本。
        return Task.FromResult(new OcrResult(
            Success: false, Text: null, ErrorCode: "OCR_NOT_CONFIGURED",
            ModelCode: request.ModelCode, ModelVersion: null, DurationMilliseconds: 0));
    }

    private string? ResolveModelName(string modelCode)
    {
        return _options.Models.TryGetValue(modelCode, out var mapped)
            ? mapped
            : null;
    }

    private static object[] BuildMessages(ModelGenerationRequest request)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new { role = "system", content = request.SystemPrompt });
        }

        messages.Add(new { role = "user", content = request.Prompt });
        return messages.ToArray();
    }

    private static ModelGenerationResult UnavailableGeneration(string modelCode)
    {
        return new ModelGenerationResult(
            Success: false, ContentJson: null, ErrorCode: UnavailableModelGateway.ModelUnavailable,
            ModelCode: modelCode, ModelVersion: null, PromptTokens: null, CompletionTokens: null,
            DurationMilliseconds: 0);
    }

    private static ModelGenerationResult FailedGeneration(
        string modelCode,
        string modelVersion,
        string errorCode,
        long durationMilliseconds)
    {
        return new ModelGenerationResult(
            Success: false, ContentJson: null, ErrorCode: errorCode,
            ModelCode: modelCode, ModelVersion: modelVersion, PromptTokens: null,
            CompletionTokens: null, DurationMilliseconds: durationMilliseconds);
    }

    private sealed class ChatCompletionResponse
    {
        public string? Model { get; set; }
        public List<ChatChoice>? Choices { get; set; }
        public UsageInfo? Usage { get; set; }
    }

    private sealed class ChatChoice
    {
        public MessageBody? Message { get; set; }
    }

    private sealed class MessageBody
    {
        public string? Content { get; set; }
    }

    private sealed class UsageInfo
    {
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
    }

    private sealed class EmbeddingResponse
    {
        public string? Model { get; set; }
        public List<EmbeddingItem>? Data { get; set; }
    }

    private sealed class EmbeddingItem
    {
        public int Index { get; set; }
        public float[] Embedding { get; set; } = [];
    }

    private sealed class RerankResponse
    {
        public List<RerankItemResponse>? Results { get; set; }
    }

    private sealed class RerankItemResponse
    {
        public int Index { get; set; }
        public double RelevanceScore { get; set; }
    }
}
