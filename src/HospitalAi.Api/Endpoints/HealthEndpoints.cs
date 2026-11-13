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
/// Health 相关端点。由 Program.cs 拆分而来（纯代码搬移，行为不变）。
/// </summary>
public static class HealthEndpoints
{
    /// <summary>MapHealthEndpoints（由 Program.cs 搬移）。</summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
    app.MapGet(
        "/api/v1/health",
        (IRequestContext requestContext) => Results.Ok(new
        {
            status = "ok",
            traceId = requestContext.TraceId
        }));

    app.MapGet(
        "/api/v1/health/live",
        (IRequestContext requestContext) => Results.Ok(new
        {
            status = "ok",
            traceId = requestContext.TraceId
        }));

    app.MapGet(
        "/api/v1/health/ready",
        async (
            ReadinessProbe readinessProbe,
            CancellationToken cancellationToken) =>
        {
            var result = await readinessProbe.CheckAsync(cancellationToken);
            return result.Status == "healthy"
                ? Results.Ok(result)
                : Results.Json(
                    result,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }
}