using HospitalAi.Domain.Common;

namespace HospitalAi.Domain.CodingTasks;

/// <summary>
/// 生成编码任务在同一 Pipeline 版本下的稳定幂等键。
/// </summary>
public static class CodingTaskIdempotencyKey
{
    public static string Create(Guid taskId, string pipelineVersion)
    {
        if (taskId == Guid.Empty)
        {
            throw new DomainException("任务标识不能为空。");
        }

        if (string.IsNullOrWhiteSpace(pipelineVersion))
        {
            throw new DomainException("Pipeline 版本不能为空。");
        }

        return $"{taskId:N}:{pipelineVersion}";
    }
}
