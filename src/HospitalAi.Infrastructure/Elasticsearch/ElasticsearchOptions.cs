namespace HospitalAi.Infrastructure.Elasticsearch;

/// <summary>
/// Elasticsearch 连接配置。Lite 阶段只用于 BM25 召回，
/// 索引名按 类型-医院-知识/文档版本 固定，禁止跨版本查询。
/// </summary>
public sealed class ElasticsearchOptions
{
    public const string SectionName = "Elasticsearch";

    /// <summary>是否启用。未启用时注册 Unavailable 实现，业务只走 Exact 快路径。</summary>
    public bool Enabled { get; set; }

    /// <summary>ES 基地址，例如 http://localhost:9200。</summary>
    public string Url { get; set; } = "http://localhost:9200";

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>单次检索超时（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 5;

    public const string KnowledgeIndexPrefix = "coding-knowledge";

    public const string ChunkIndexPrefix = "medical-chunks";

    public static string KnowledgeIndexName(Guid hospitalId, string knowledgeVersion)
    {
        return $"{KnowledgeIndexPrefix}-{hospitalId:N}-{Sanitize(knowledgeVersion)}";
    }

    public static string ChunkIndexName(Guid hospitalId, string documentVersion)
    {
        return $"{ChunkIndexPrefix}-{hospitalId:N}-{Sanitize(documentVersion)}";
    }

    private static string Sanitize(string version)
    {
        return new string(version
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-')
            .ToArray());
    }
}
