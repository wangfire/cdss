namespace HospitalAi.Application.Abstractions;

/// <summary>
/// 交叉编码重排（Rerank，WP-B3-2）：对（查询，候选文本）逐对精排，
/// 返回 [0,1] 相关性分。与向量双塔相比是全交叉注意力，代价更高、精度更高，
/// 仅对候选池 Top-K 使用。
/// 不可用时 Score 返回 null，由调用方显式打 DEGRADED.NO_RERANK，禁止伪造精排分。
/// </summary>
public interface IReranker
{
    bool IsAvailable { get; }

    string ModelCode { get; }

    /// <summary>
    /// 对 documents 逐个与 query 交叉编码打分。
    /// 返回与 documents 等长的相关性分数数组（sigmoid 后 [0,1]）；模型不可用时返回 null。
    /// </summary>
    double[]? Score(string query, IReadOnlyList<string> documents);
}
