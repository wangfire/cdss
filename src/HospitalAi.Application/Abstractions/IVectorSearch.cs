namespace HospitalAi.Application.Abstractions;

/// <summary>向量语义召回命中项。Score 为归一化余弦相似度 [0,1]。</summary>
public sealed record VectorSearchHit(
    string CodeSystemCode,
    string Code,
    string Title,
    double Score);

/// <summary>
/// 医学编码向量语义检索（WP-B3-1，决策 2：CPU 小模型先行）。
/// 不可用时 IsAvailable=false 且检索返回空，由调用方显式打 DEGRADED.NO_VECTOR，
/// 禁止伪造语义分数或随机向量。
/// </summary>
public interface IVectorSearch
{
    bool IsAvailable { get; }

    /// <summary>
    /// 按语义相似度召回编码候选。queryText 为临床名称（如"急性心梗"），
    /// 匹配目标为该医院 / 编码体系下已构建向量的编码标题。
    /// </summary>
    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        Guid hospitalId,
        string codeSystemCode,
        string queryText,
        int topK,
        CancellationToken cancellationToken = default);
}
