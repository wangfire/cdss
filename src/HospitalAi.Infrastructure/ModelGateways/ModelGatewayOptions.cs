namespace HospitalAi.Infrastructure.ModelGateways;

/// <summary>
/// 模型网关配置。ModelMode 默认 Unavailable：模型不可用时返回明确错误，
/// 业务层据此标记 DEGRADED.NO_MODEL，不得伪造推荐或回退 Legacy Runner。
/// </summary>
public sealed class ModelGatewayOptions
{
    public const string SectionName = "ModelGateway";

    /// <summary>运行模式：Unavailable / Recording / OpenAiCompatible。</summary>
    public string ModelMode { get; set; } = nameof(HospitalAi.Contracts.Models.ModelMode.Unavailable);

    /// <summary>OpenAI 兼容端点基地址，模式为 OpenAiCompatible 时使用。</summary>
    public string? BaseUrl { get; set; }

    /// <summary>OpenAI 兼容端点 API Key，可空。</summary>
    public string? ApiKey { get; set; }

    /// <summary>model_code 到端点实际模型名的映射，例如 "embedding" -> "bge-m3"。</summary>
    public Dictionary<string, string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>单次模型调用超时（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 60;
}
