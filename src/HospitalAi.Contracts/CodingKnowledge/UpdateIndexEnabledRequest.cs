using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.CodingKnowledge;

/// <summary>
/// 索引行停用/启用请求（WP-C 知识库治理）。enabled=true 启用、false 停用。
/// </summary>
public sealed record UpdateIndexEnabledRequest(
    [property: JsonPropertyName("enabled")] bool Enabled);
