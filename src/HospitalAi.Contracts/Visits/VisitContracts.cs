using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.Visits;

/// <summary>
/// 创建就诊请求。
/// </summary>
public sealed record CreateVisitRequest(
    [property: JsonPropertyName("patientId")] Guid PatientId,
    [property: JsonPropertyName("admissionAt")] DateTimeOffset AdmissionAt,
    [property: JsonPropertyName("dischargeAt")] DateTimeOffset? DischargeAt);

/// <summary>
/// 就诊响应。
/// </summary>
public sealed record VisitResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("hospitalId")] Guid HospitalId,
    [property: JsonPropertyName("patientId")] Guid PatientId,
    [property: JsonPropertyName("admissionAt")] DateTimeOffset AdmissionAt,
    [property: JsonPropertyName("dischargeAt")] DateTimeOffset? DischargeAt,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);
