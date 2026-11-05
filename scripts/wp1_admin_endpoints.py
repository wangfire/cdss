# -*- coding: utf-8 -*-
"""WP1 API：流水线配置 GET/PUT + 批量重跑端点（追加到 Program.cs）。

重跑实现：把医院全部终态任务（PENDING_REVIEW/ACCEPTED/MODIFIED/REJECTED/FAILED）
置回 Pending + 重新写 coding.task.created Outbox 消息 → Worker 重新消费整任务。
"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Api\Program.cs"
c = open(p, encoding="utf-8-sig").read()

if "/admin/pipeline-config" in c:
    print("endpoints already present")
    sys.exit(0)

anchor = '''// 工作台任务状态统计（仪表盘统计卡）：数据库端 GROUP BY，无全量拉取。
app.MapGet(
    "/api/v1/workbench/tasks/stats",'''
assert anchor in c, "stats anchor"

endpoints = '''// ===== 流水线运行时配置（工作台设置抽屉，WP-A 前端生产化） =====

// 流水线配置响应：三个开关 + 最近更新时间。
public sealed record PipelineConfigResponse(
    [property: JsonPropertyName("icdTitleDirect")] bool IcdTitleDirect,
    [property: JsonPropertyName("cm3TitleDirect")] bool Cm3TitleDirect,
    [property: JsonPropertyName("icdTitleReverse")] bool IcdTitleReverse,
    [property: JsonPropertyName("vectorEnabled")] bool VectorEnabled,
    [property: JsonPropertyName("rerankEnabled")] bool RerankEnabled,
    [property: JsonPropertyName("ruleEngineEnabled")] bool RuleEngineEnabled,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset? UpdatedAt);

public sealed record UpdatePipelineConfigRequest(
    [property: JsonPropertyName("icdTitleDirect")] bool IcdTitleDirect,
    [property: JsonPropertyName("cm3TitleDirect")] bool Cm3TitleDirect,
    [property: JsonPropertyName("icdTitleReverse")] bool IcdTitleReverse,
    [property: JsonPropertyName("vectorEnabled")] bool? VectorEnabled,
    [property: JsonPropertyName("rerankEnabled")] bool? RerankEnabled,
    [property: JsonPropertyName("ruleEngineEnabled")] bool? RuleEngineEnabled);

// 读取当前运行时覆盖配置（无行时返回 appsettings 基线）。
app.MapGet(
    "/api/v1/admin/pipeline-config",
    async (HospitalAiDbContext db, IOptions<V22PipelineOptions> options, CancellationToken cancellationToken) =>
    {
        var row = await db.PipelineRuntimeConfigs
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ConfigKey == "v22lite", cancellationToken);
        if (row is null)
        {
            var baseline = options.Value;
            return Results.Ok(ApiEnvelope.Create(new PipelineConfigResponse(
                baseline.IcdTitleDirectEnabled,
                baseline.Cm3TitleDirectEnabled,
                baseline.IcdTitleReverseEnabled,
                baseline.VectorEnabled,
                baseline.RerankEnabled,
                baseline.RuleEngineEnabled,
                null)));
        }

        using var doc = System.Text.Json.JsonDocument.Parse(row.ConfigJson);
        var root = doc.RootElement;
        var settings = options.Value;
        return Results.Ok(ApiEnvelope.Create(new PipelineConfigResponse(
            root.TryGetProperty("IcdTitleDirectEnabled", out var v1) ? v1.GetBoolean() : settings.IcdTitleDirectEnabled,
            root.TryGetProperty("Cm3TitleDirectEnabled", out var v2) ? v2.GetBoolean() : settings.Cm3TitleDirectEnabled,
            root.TryGetProperty("IcdTitleReverseEnabled", out var v3) ? v3.GetBoolean() : settings.IcdTitleReverseEnabled,
            root.TryGetProperty("VectorEnabled", out var v4) ? v4.GetBoolean() : settings.VectorEnabled,
            root.TryGetProperty("RerankEnabled", out var v5) ? v5.GetBoolean() : settings.RerankEnabled,
            root.TryGetProperty("RuleEngineEnabled", out var v6) ? v6.GetBoolean() : settings.RuleEngineEnabled,
            row.UpdatedAt)));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.AdminUserManage));

// 更新运行时覆盖配置：整行 upsert，下一次任务执行即生效（Worker 每任务读一次）。
app.MapPut(
    "/api/v1/admin/pipeline-config",
    async (
        UpdatePipelineConfigRequest request,
        HospitalAiDbContext db,
        IOptions<V22PipelineOptions> options,
        CancellationToken cancellationToken) =>
    {
        var baseline = options.Value;
        var configJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            IcdTitleDirectEnabled = request.IcdTitleDirect,
            Cm3TitleDirectEnabled = request.Cm3TitleDirect,
            IcdTitleReverseEnabled = request.IcdTitleReverse,
            VectorEnabled = request.VectorEnabled ?? baseline.VectorEnabled,
            RerankEnabled = request.RerankEnabled ?? baseline.RerankEnabled,
            RuleEngineEnabled = request.RuleEngineEnabled ?? baseline.RuleEngineEnabled,
        });
        var now = DateTimeOffset.UtcNow;
        var row = await db.PipelineRuntimeConfigs
            .SingleOrDefaultAsync(item => item.ConfigKey == "v22lite", cancellationToken);
        if (row is null)
        {
            row = new PipelineRuntimeConfigRecord
            {
                Id = Guid.NewGuid(),
                ConfigKey = "v22lite",
                ConfigJson = configJson,
                UpdatedAt = now
            };
            db.PipelineRuntimeConfigs.Add(row);
        }
        else
        {
            row.ConfigJson = configJson;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ApiEnvelope.Create(new PipelineConfigResponse(
            request.IcdTitleDirect,
            request.Cm3TitleDirect,
            request.IcdTitleReverse,
            request.VectorEnabled ?? baseline.VectorEnabled,
            request.RerankEnabled ?? baseline.RerankEnabled,
            request.RuleEngineEnabled ?? baseline.RuleEngineEnabled,
            row.UpdatedAt)));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.AdminUserManage));

// 批量重跑：医院全部终态任务（含待审核/已接受/已修改/已拒绝/失败）置回 Pending 并重新入队。
// 返回重跑任务数；旧推荐不删（新版本推荐落库后旧版自然 STALE）。
public sealed record PipelineRerunResponse(
    [property: JsonPropertyName("rerunCount")] int RerunCount);

app.MapPost(
    "/api/v1/admin/pipeline/rerun",
    async (
        HospitalAiDbContext db,
        IOutboxStore outbox,
        IRequestContext requestContext,
        CancellationToken cancellationToken) =>
    {
        var terminal = new[]
        {
            CodingTaskStatus.PendingReview, CodingTaskStatus.Accepted, CodingTaskStatus.Modified,
            CodingTaskStatus.Rejected, CodingTaskStatus.Failed
        };
        var tasks = await db.CodingTasks
            .Where(item => item.HospitalId == requestContext.HospitalId && terminal.Contains(item.Status))
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var task in tasks)
        {
            task.Status = CodingTaskStatus.Pending;
            task.ErrorCode = string.Empty;
            task.UpdatedAt = now;
            var message = new CodingTaskCreatedMessage(
                Guid.NewGuid(),
                task.HospitalId,
                task.Id,
                task.VisitId,
                task.PipelineVersion,
                Guid.NewGuid().ToString("N"),
                requestContext.IdempotencyKey);
            outbox.Add(OutboxMessage.Create(
                message.MessageId,
                task.HospitalId,
                "coding.task.created",
                System.Text.Json.JsonSerializer.Serialize(message, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ApiEnvelope.Create(new PipelineRerunResponse(tasks.Count)));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.AdminUserManage));

''' + anchor

c = c.replace(anchor, endpoints, 1)
open(p, "w", encoding="utf-8-sig").write(c)
print("admin endpoints added")
