namespace HospitalAi.Infrastructure.SqlServer;

/// <summary>
/// 医学编码标题向量（medical_code_embedding）。
/// 由 tools/HospitalAi.Tools 的 embed 命令离线构建（bge-small-zh ONNX，CPU），
/// 向量为 L2 归一化后的 float32 小端字节序列；余弦相似度 = 点积。
/// </summary>
public sealed class MedicalCodeEmbeddingRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public string CodeSystemCode { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string ModelCode { get; set; } = string.Empty;

    /// <summary>嵌入时的编码标题快照（向量检索直接返回，避免联表）。</summary>
    public string Title { get; set; } = string.Empty;

    public int Dim { get; set; }

    public byte[] Vector { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
