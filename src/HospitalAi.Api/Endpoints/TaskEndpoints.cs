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
/// Task 相关端点。由 Program.cs 拆分而来（纯代码搬移，行为不变）。
/// </summary>
public static class TaskEndpoints
{
    /// <summary>MapTaskEndpoints（由 Program.cs 搬移）。</summary>
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
    app.MapPost(
        "/api/v1/coding-tasks",
        async (
            CreateCodingTaskRequest request,
            ICodingTaskService service,
            IRequestContext requestContext,
            CancellationToken cancellationToken) =>
        {
            var response = await service.CreateAsync(
                request,
                requestContext.IdempotencyKey ?? string.Empty,
                cancellationToken);
            return Results.Created($"/api/v1/coding-tasks/{response.Id}", ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReviewer);

    app.MapGet(
        "/api/v1/coding-tasks/{taskId:guid}",
        async (
            Guid taskId,
            ICodingTaskService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.GetAsync(taskId, cancellationToken);
            return response is null
                ? Results.NotFound(ApiEnvelope.Create(new { message = "资源不存在或已被删除。" }))
                : Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReader);

    app.MapGet(
        "/api/v1/traces/{traceId}",
        async (
            string traceId,
            ITraceQueryService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.GetAsync(traceId, cancellationToken);
            return response is null
                ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "TRACE_NOT_FOUND" }))
                : Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReader);

    app.MapGet(
        "/api/v1/coding-tasks/{taskId:guid}/traces",
        async (
            Guid taskId,
            ITraceQueryService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ListRunsAsync(taskId, 20, cancellationToken);
            return Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReader);

        return app;
    }
}