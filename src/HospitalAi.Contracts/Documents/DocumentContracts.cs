using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.Documents;

/// <summary>
/// 创建医疗文书请求。文书正文在本阶段只通过引用和哈希传递。
/// </summary>
public sealed record CreateDocumentRequest(
    [property: JsonPropertyName("visitId")] Guid VisitId,
    [property: JsonPropertyName("documentType")] string DocumentType,
    [property: JsonPropertyName("contentReference")] string ContentReference,
    [property: JsonPropertyName("contentHash")] string ContentHash,
    [property: JsonPropertyName("version")] int Version);

/// <summary>
/// 医疗文书响应。
/// </summary>
public sealed record DocumentResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("visitId")] Guid VisitId,
    [property: JsonPropertyName("documentType")] string DocumentType,
    [property: JsonPropertyName("contentReference")] string ContentReference,
    [property: JsonPropertyName("contentHash")] string ContentHash,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);
