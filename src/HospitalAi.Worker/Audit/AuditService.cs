using HospitalAi.Infrastructure.SqlServer;

namespace HospitalAi.Worker.Audit;

/// <summary>
/// 写入任务处理审计，不记录患者姓名、文书正文或模型 Prompt。
/// </summary>
public sealed class AuditService(HospitalAiDbContext dbContext)
{
    public void Add(
        Guid hospitalId,
        Guid taskId,
        string action,
        string result,
        string requestId,
        DateTimeOffset now)
    {
        dbContext.AuditLogs.Add(new AuditLogRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            ResourceType = "coding_task",
            ResourceId = taskId,
            Action = action,
            Result = result,
            RequestId = requestId,
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}
