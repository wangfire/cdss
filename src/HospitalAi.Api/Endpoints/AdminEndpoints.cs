using HospitalAi.Api;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Worker.Pipeline;
using HospitalAi.Infrastructure.Outbox;
using HospitalAi.Api.Observability;
using HospitalAi.Api.Security;
using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Application.Coding.NameLookup;
using HospitalAi.Application.Security;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Contracts.CodingKnowledge;
using HospitalAi.Contracts.CodingRecommendations;
using HospitalAi.Contracts.Documents;
using HospitalAi.Contracts.Hospitals;
using HospitalAi.Contracts.Patients;
using HospitalAi.Contracts.Security;
using HospitalAi.Contracts.Visits;
using HospitalAi.Infrastructure.CodingKnowledge;
using HospitalAi.Infrastructure.CodingLookup;
using HospitalAi.Infrastructure.CodingRecommendations;
using HospitalAi.Infrastructure.CodingTasks;
using HospitalAi.Infrastructure.Observability;
using HospitalAi.Infrastructure.ReferenceData;
using HospitalAi.Infrastructure.RabbitMq;
using HospitalAi.Infrastructure.Security;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Infrastructure.Tracing;
using HospitalAi.Worker;
using OpenTelemetry.Exporter.Prometheus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using System.Threading.RateLimiting;

namespace HospitalAi.Api.Endpoints;

/// <summary>
/// Admin 相关端点。由 Program.cs 拆分而来（纯代码搬移，行为不变）。
/// </summary>
public static class AdminEndpoints
{
    /// <summary>MapAdminEndpoints（由 Program.cs 搬移）。</summary>
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
    // 读取当前运行时覆盖配置（无行时返回 appsettings 基线）。
    app.MapGet(
        "/api/v1/admin/pipeline-config",
        async (HospitalAiDbContext db, IOptions<V22PipelineOptions> options, CancellationToken cancellationToken) =>
        {
            var row = await db.PipelineRuntimeConfigs
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.ConfigKey == HospitalAi.Worker.Pipeline.V22PipelineOptions.RuntimeConfigKey, cancellationToken);
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
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.UserManage));

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
                .SingleOrDefaultAsync(item => item.ConfigKey == HospitalAi.Worker.Pipeline.V22PipelineOptions.RuntimeConfigKey, cancellationToken);
            if (row is null)
            {
                row = new PipelineRuntimeConfigRecord
                {
                    Id = Guid.NewGuid(),
                    ConfigKey = HospitalAi.Worker.Pipeline.V22PipelineOptions.RuntimeConfigKey,
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
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.UserManage));

    // 批量重跑：医院全部终态任务（含待审核/已接受/已修改/已拒绝/失败）置回 Pending 并重新入队。
    // 返回重跑任务数；旧推荐不删（新版本推荐落库后旧版自然 STALE）。

    app.MapPost(
        "/api/v1/admin/pipeline/rerun",
        async (
            [Microsoft.AspNetCore.Mvc.FromServices] HospitalAiDbContext db,
            [Microsoft.AspNetCore.Mvc.FromServices] OutboxStore outbox,
            [Microsoft.AspNetCore.Mvc.FromServices] IRequestContext requestContext,
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
                // 复用任务原 TraceId：Consumer 按 hospital_id + task_id + trace_id 定位既有
                // PipelineTrace，新 GUID 会导致"编码任务缺少 Pipeline Trace"异常而空转。
                var originalTraceId = await db.PipelineTraces
                    .Where(item => item.CodingTaskId == task.Id && item.HospitalId == task.HospitalId)
                    .OrderBy(item => item.CreatedAt)
                    .Select(item => item.TraceId)
                    .FirstOrDefaultAsync(cancellationToken) ?? Guid.NewGuid().ToString("N");
                var message = new CodingTaskCreatedMessage(
                    Guid.NewGuid(),
                    task.HospitalId,
                    task.Id,
                    task.VisitId,
                    task.PipelineVersion,
                    originalTraceId,
                    requestContext.IdempotencyKey);
                outbox.Add(OutboxMessage.Create(
                    message.MessageId,
                    task.HospitalId,
                    CodingTaskCreatedMessage.MessageType,
                    System.Text.Json.JsonSerializer.Serialize(message, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))));
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ApiEnvelope.Create(new PipelineRerunResponse(tasks.Count)));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.UserManage));

    // ===== 知识库索引（Elasticsearch 未启用时返回明确失败，不回退 SQL 全量扫描）=====
    app.MapPost(
        "/api/v1/admin/knowledge/index/rebuild",
        async (
            Guid? hospitalId,
            HospitalAi.Infrastructure.Elasticsearch.ICodingKnowledgeIndexer? indexer,
            IRequestContext requestContext,
            IOptions<HospitalAi.Worker.Pipeline.V22PipelineOptions> pipelineOptions,
            CancellationToken cancellationToken) =>
        {
            var targetHospitalId = hospitalId ?? requestContext.HospitalId;
            if (targetHospitalId == Guid.Empty)
            {
                return Results.BadRequest(ApiEnvelope.Create<object>(new { error = "HOSPITAL_NOT_FOUND" }));
            }

            if (indexer is null)
            {
                return Results.Json(
                    ApiEnvelope.Create<object>(new { error = "ELASTICSEARCH_DISABLED" }),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var settings = pipelineOptions.Value;
            var result = await indexer.RebuildAsync(
                targetHospitalId,
                settings.KnowledgeVersion,
                settings.DocumentVersion,
                cancellationToken);
            return result.Success
                ? Results.Ok(ApiEnvelope.Create(new { index = result.IndexName, indexed = result.IndexedCount }))
                : Results.Json(
                    ApiEnvelope.Create<object>(new { error = "INDEX_REBUILD_FAILED", detail = result.ErrorBody }),
                    statusCode: StatusCodes.Status502BadGateway);
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeIndexRebuild));

        return app;
    }
}
