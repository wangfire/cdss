using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HospitalAi.Contracts.CodingKnowledge;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HospitalAi.Infrastructure.Elasticsearch;

/// <summary>
/// 编码知识 Elasticsearch 索引构建器。按医院 + 知识版本建索引，
/// BM25 字段含 code / title / search_text / synonym / code_type / document_type /
/// normalized_term，并预留 dense_vector 字段（Full 阶段启用）。
/// </summary>
public sealed class ElasticsearchCodingKnowledgeIndexer : ICodingKnowledgeIndexer
{
    public ElasticsearchCodingKnowledgeIndexer(
        HospitalAiDbContext dbContext,
        HttpClient httpClient,
        ElasticsearchOptions options,
        ILogger<ElasticsearchCodingKnowledgeIndexer> logger)
    {
        this.dbContext = dbContext;
        this.httpClient = httpClient;
        this.options = options;
        this.logger = logger;
    }

    private readonly HospitalAiDbContext dbContext;
    private readonly HttpClient httpClient;
    private readonly ElasticsearchOptions options;
    private readonly ILogger<ElasticsearchCodingKnowledgeIndexer> logger;
    public async Task<ElasticsearchIndexResult> EnsureIndexAsync(
        Guid hospitalId,
        string knowledgeVersion,
        CancellationToken cancellationToken = default)
    {
        var index = ElasticsearchOptions.KnowledgeIndexName(hospitalId, knowledgeVersion);
        var mapping = new
        {
            settings = new { number_of_shards = 1, number_of_replicas = 0 },
            mappings = new
            {
                properties = new Dictionary<string, object>
                {
                    ["hospital_id"] = new { type = "keyword" },
                    ["coding_version"] = new { type = "keyword" },
                    ["knowledge_version"] = new { type = "keyword" },
                    ["document_version"] = new { type = "keyword" },
                    ["is_active"] = new { type = "boolean" },
                    ["code_system_code"] = new { type = "keyword" },
                    ["code"] = new { type = "text", analyzer = "standard", boost = 3.0 },
                    // 中文标题必须按词切分（ik）：逐字切分会让 "糖尿病腰骶神经根神经丛病"
                    // 这种仅共享单字的条目在 BM25 中压过真正相关的腰椎条目。
                    ["title"] = new { type = "text", analyzer = "ik_max_word", search_analyzer = "ik_smart", boost = 2.0 },
                    ["search_text"] = new { type = "text", analyzer = "ik_max_word", search_analyzer = "ik_smart" },
                    ["synonym"] = new { type = "text", analyzer = "ik_max_word", search_analyzer = "ik_smart", boost = 2.0 },
                    ["code_type"] = new { type = "keyword" },
                    ["document_type"] = new { type = "keyword" },
                    ["normalized_term"] = new { type = "text", analyzer = "ik_max_word", search_analyzer = "ik_smart", boost = 2.0 },
                    ["embedding"] = new { type = "dense_vector", dims = 0, index = false, enabled = false }
                }
            }
        };

        var response = await httpClient.PutAsJsonAsync(index, mapping, cancellationToken);
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 400)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("创建 ES 索引失败，Index={Index}, Status={StatusCode}", index, (int)response.StatusCode);
            return new ElasticsearchIndexResult(index, false, 0, body);
        }

        return new ElasticsearchIndexResult(index, true, 0, null);
    }

    public async Task<ElasticsearchIndexResult> RebuildAsync(
        Guid hospitalId,
        string knowledgeVersion,
        string documentVersion,
        CancellationToken cancellationToken = default)
    {
        var ensured = await EnsureIndexAsync(hospitalId, knowledgeVersion, cancellationToken);
        if (!ensured.Success)
        {
            return ensured;
        }

        var index = ensured.IndexName;
        var codes = await dbContext.MedicalCodes
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId && item.IsEnabled)
            .Select(item => new
            {
                item.CodeSystemCode,
                item.Code,
                item.Title,
                item.CodeType,
                item.SearchText
            })
            .ToListAsync(cancellationToken);

        var synonyms = await dbContext.TermSynonyms
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId)
            .Select(item => new
            {
                item.Term,
                item.NormalizedTerm,
                item.CodeSystemCode,
                item.Code,
                item.EntityType
            })
            .ToListAsync(cancellationToken);

        var synonymsByCode = synonyms
            .GroupBy(item => $"{item.CodeSystemCode}:{item.Code}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var operations = new StringBuilder();
        var indexed = 0;
        foreach (var code in codes)
        {
            synonymsByCode.TryGetValue($"{code.CodeSystemCode}:{code.Code}", out var related);
            var document = new Dictionary<string, object>
            {
                ["hospital_id"] = hospitalId.ToString("N"),
                ["coding_version"] = knowledgeVersion,
                ["knowledge_version"] = knowledgeVersion,
                ["document_version"] = documentVersion,
                ["is_active"] = true,
                ["code_system_code"] = code.CodeSystemCode,
                ["code"] = code.Code,
                ["title"] = code.Title,
                ["search_text"] = code.SearchText,
                ["synonym"] = related?.Select(item => item.Term).Distinct().ToArray() ?? [],
                ["document_type"] = string.Empty,
                ["normalized_term"] = related?.Select(item => item.NormalizedTerm).Distinct().ToArray() ?? [],
                ["code_type"] = code.CodeType
            };

            var id = $"{code.CodeSystemCode}:{code.Code}";
            operations.Append("{\"index\":{\"_id\":\"").Append(id).Append("\"}}\n");
            operations.Append(JsonSerializer.Serialize(document)).Append('\n');
            indexed++;
        }

        var coveredCodes = codes
            .Select(code => $"{code.CodeSystemCode}:{code.Code}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var synonym in synonyms.Where(item =>
            !coveredCodes.Contains($"{item.CodeSystemCode}:{item.Code}")))
        {
            var document = new Dictionary<string, object>
            {
                ["hospital_id"] = hospitalId.ToString("N"),
                ["coding_version"] = knowledgeVersion,
                ["knowledge_version"] = knowledgeVersion,
                ["document_version"] = documentVersion,
                ["is_active"] = true,
                ["code_system_code"] = synonym.CodeSystemCode,
                ["code"] = synonym.Code,
                ["title"] = synonym.NormalizedTerm,
                ["search_text"] = synonym.Term,
                ["synonym"] = new[] { synonym.Term },
                ["document_type"] = synonym.EntityType,
                ["normalized_term"] = new[] { synonym.NormalizedTerm },
                ["code_type"] = "SYNONYM"
            };

            operations.Append("{\"index\":{\"_id\":\"synonym:")
                .Append(synonym.CodeSystemCode).Append(':').Append(synonym.Code).Append("\"}}\n");
            operations.Append(JsonSerializer.Serialize(document)).Append('\n');
            indexed++;
        }

        if (operations.Length == 0)
        {
            return new ElasticsearchIndexResult(index, true, 0, null);
        }

        using var content = new StringContent(operations.ToString(), Encoding.UTF8, "application/x-ndjson");
        var bulkResponse = await httpClient.PostAsync($"{index}/_bulk?refresh=true", content, cancellationToken);
        if (!bulkResponse.IsSuccessStatusCode)
        {
            var body = await bulkResponse.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("ES bulk 索引失败，Index={Index}, Status={StatusCode}", index, (int)bulkResponse.StatusCode);
            return new ElasticsearchIndexResult(index, false, 0, body);
        }

        return new ElasticsearchIndexResult(index, true, indexed, null);
    }
}

/// <summary>
/// 索引构建结果。失败时返回 ES 响应体，便于排障与回滚。
/// </summary>
public sealed record ElasticsearchIndexResult(
    string IndexName,
    bool Success,
    int IndexedCount,
    string? ErrorBody);
