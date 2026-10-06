namespace HospitalAi.Infrastructure.Elasticsearch;

/// <summary>
/// ES 已启用但检索请求失败（网络异常 / 非成功状态码）。
/// 调用方必须把该异常映射为显式降级（如 DEGRADED.NO_BM25），
/// 不得当作"知识库无命中"吞掉——否则知识库故障会被伪装成正常空结果。
/// </summary>
public sealed class ElasticsearchUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
