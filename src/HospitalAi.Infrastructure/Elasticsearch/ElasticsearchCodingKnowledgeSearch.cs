using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HospitalAi.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace HospitalAi.Infrastructure.Elasticsearch;

/// <summary>
/// Elasticsearch BM25 编码知识检索（ES REST API，HttpClient 直连）。
/// 查询必须携带 hospital_id / coding_version / knowledge_version / is_active 过滤，
/// 检索字段为 code / title / search_text / synonym / code_type / document_type / normalized_term。
/// ES 返回 _score 按批次内最高分归一化到 [0,1]，供 RetrievalScore 计算。
/// </summary>
public sealed class ElasticsearchCodingKnowledgeSearch : ICodingKnowledgeSearch
{
    private readonly HttpClient _httpClient;
    private readonly ElasticsearchOptions _options;
    private readonly ILogger<ElasticsearchCodingKnowledgeSearch> _logger;

    public ElasticsearchCodingKnowledgeSearch(
        HttpClient httpClient,
        ElasticsearchOptions options,
        Microsoft.Extensions.Logging.ILogger<ElasticsearchCodingKnowledgeSearch> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.Url.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 5);
        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password ?? string.Empty}"));
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public bool IsAvailable => _options.Enabled;

    public async Task<IReadOnlyList<CodingKnowledgeHit>> SearchAsync(
        CodingKnowledgeSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            IReadOnlyList<CodingKnowledgeHit> empty = [];
            return empty;
        }

        var index = ElasticsearchOptions.KnowledgeIndexName(query.HospitalId, query.KnowledgeVersion);
        var payload = new
        {
            size = query.MaxResults,
            query = new
            {
                @bool = new
                {
                    filter = new object[]
                    {
                        new { term = new { hospital_id = query.HospitalId.ToString("N") } },
                        new { term = new { coding_version = query.CodingVersion } },
                        new { term = new { knowledge_version = query.KnowledgeVersion } },
                        new { term = new { document_version = query.DocumentVersion } },
                        new { term = new { is_active = true } }
                    },
                    must = new[]
                    {
                        new
                        {
                            multi_match = new
                            {
                                query = query.QueryText,
                                type = "best_fields",
                                fields = new[]
                                {
                                    "code^3", "title^2", "synonym^2", "normalized_term^2",
                                    "search_text", "code_type", "document_type"
                                }
                            }
                        }
                    }
                }
            }
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient
                .PostAsJsonAsync($"{index}/_search", payload, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "ES BM25 检索失败，Status={StatusCode}, Index={Index}",
                    (int)response.StatusCode,
                    index);
                return [];
            }

            var body = await response.Content
                .ReadFromJsonAsync<EsSearchResponse>(JsonOptions)
                .ConfigureAwait(false);
            var hits = body?.Hits?.Hits;
            if (hits is null || hits.Count == 0)
            {
                return [];
            }

            var maxScore = hits.Max(item => item.Score);
            return hits
                .Where(item => !string.IsNullOrWhiteSpace(item.Source.Code))
                .Select(item => new CodingKnowledgeHit(
                    item.Source.CodeSystemCode,
                    item.Source.Code,
                    item.Source.Title,
                    item.Source.CodeType,
                    NormalizeScore(item.Score, maxScore),
                    item.Source.SearchText,
                    "BM25"))
                .ToList();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "ES BM25 检索异常，Index={Index}, ElapsedMs={ElapsedMs}",
                index,
                stopwatch.ElapsedMilliseconds);
            return [];
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static double NormalizeScore(double score, double maxScore)
    {
        if (maxScore <= 0)
        {
            return 0;
        }

        return Math.Round(score / maxScore, 4);
    }

    private sealed class EsSearchResponse
    {
        public EsHitsSection? Hits { get; set; }
    }

    private sealed class EsHitsSection
    {
        public List<EsHit> Hits { get; set; } = [];
    }

    private sealed class EsHit
    {
        // ES 原生字段名带下划线前缀，必须显式映射，否则反序列化静默得到空对象。
        [JsonPropertyName("_score")]
        public double Score { get; set; }

        [JsonPropertyName("_source")]
        public EsSource Source { get; set; } = new();
    }

    private sealed class EsSource
    {
        [JsonPropertyName("code_system_code")]
        public string CodeSystemCode { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("code_type")]
        public string CodeType { get; set; } = string.Empty;

        [JsonPropertyName("search_text")]
        public string SearchText { get; set; } = string.Empty;
    }
}
