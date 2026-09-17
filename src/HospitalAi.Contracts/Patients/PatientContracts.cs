using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.Patients;

/// <summary>
/// 创建患者请求。医院标识从请求上下文获取，不由客户端重复提交。
/// </summary>
public sealed record CreatePatientRequest(
    [property: JsonPropertyName("sourceSystem")] string SourceSystem,
    [property: JsonPropertyName("sourcePatientId")] string SourcePatientId,
    [property: JsonPropertyName("displayName")] string? DisplayName);

/// <summary>
/// 患者响应。
/// </summary>
public sealed record PatientResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("hospitalId")] Guid HospitalId,
    [property: JsonPropertyName("sourceSystem")] string SourceSystem,
    [property: JsonPropertyName("sourcePatientId")] string SourcePatientId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);
