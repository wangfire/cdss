using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.Hospitals;

/// <summary>
/// 创建医院请求。
/// </summary>
public sealed record CreateHospitalRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name);

/// <summary>
/// 医院响应。
/// </summary>
public sealed record HospitalResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);
