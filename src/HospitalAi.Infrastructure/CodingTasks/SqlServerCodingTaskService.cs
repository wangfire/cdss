using System.Text.Json;
using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.Outbox;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Infrastructure.CodingTasks;

/// <summary>
/// 基于 SQL Server 的编码任务应用服务。
/// </summary>
public sealed class SqlServerCodingTaskService(
    HospitalAiDbContext dbContext,
    IRequestContext requestContext,
    CodingTaskPipelineOptions pipelineOptions) : ICodingTaskService
{
    public async Task<CodingTaskResponse> CreateAsync(
        CreateCodingTaskRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ValidationException("Idempotency-Key 不能为空。");
        }

        var pipelineVersion = pipelineOptions.Resolve(request.PipelineVersion);

        var existing = await dbContext.CodingTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.HospitalId == requestContext.HospitalId
                    && item.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is not null)
        {
            return await ToResponseAsync(existing, cancellationToken);
        }

        var visit = await dbContext.Visits
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.VisitId
                    && item.HospitalId == requestContext.HospitalId,
                cancellationToken);

        if (visit is null)
        {
            throw new ResourceNotFoundException("就诊记录不存在。");
        }

        var task = CodingTask.Create(
            requestContext.HospitalId,
            request.VisitId,
            pipelineVersion);
        var now = DateTimeOffset.UtcNow;
        var taskRecord = new CodingTaskRecord
        {
            Id = task.Id,
            HospitalId = task.HospitalId,
            VisitId = task.VisitId,
            PipelineVersion = task.PipelineVersion,
            IdempotencyKey = idempotencyKey,
            Status = task.Status,
            RetryCount = task.RetryCount,
            StartedAt = task.StartedAt,
            CompletedAt = task.CompletedAt,
            ErrorCode = task.ErrorCode,
            CreatedAt = now,
            UpdatedAt = now
        };
        var traceRecord = new PipelineTraceRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = requestContext.HospitalId,
            CodingTaskId = task.Id,
            TraceId = requestContext.TraceId,
            Status = "PENDING",
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.CodingTasks.Add(taskRecord);
        dbContext.PipelineTraces.Add(traceRecord);
        var messageId = Guid.NewGuid();
        var message = new CodingTaskCreatedMessage(
            messageId,
            requestContext.HospitalId,
            task.Id,
            request.VisitId,
            pipelineVersion,
            requestContext.TraceId,
            requestContext.RequestId);
        new OutboxStore(dbContext).Add(OutboxMessage.Create(
            messageId,
            requestContext.HospitalId,
            "coding.task.created",
            JsonSerializer.Serialize(
                message,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            var concurrent = await dbContext.CodingTasks
                .AsNoTracking()
                .SingleAsync(
                    item => item.HospitalId == requestContext.HospitalId
                        && item.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            return await ToResponseAsync(concurrent, cancellationToken);
        }

        return ToResponse(taskRecord, requestContext.TraceId);
    }

    public async Task<CodingTaskResponse?> GetAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();

        var record = await dbContext.CodingTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == taskId
                    && item.HospitalId == requestContext.HospitalId,
                cancellationToken);

        return record is null
            ? null
            : await ToResponseAsync(record, cancellationToken);
    }

    private async Task<CodingTaskResponse> ToResponseAsync(
        CodingTaskRecord record,
        CancellationToken cancellationToken)
    {
        var traceId = await dbContext.PipelineTraces
            .AsNoTracking()
            .Where(item => item.CodingTaskId == record.Id && item.HospitalId == record.HospitalId)
            .Select(item => item.TraceId)
            .SingleOrDefaultAsync(cancellationToken);

        return ToResponse(record, traceId ?? string.Empty);
    }

    private static CodingTaskResponse ToResponse(
        CodingTaskRecord record,
        string traceId)
    {
        return new CodingTaskResponse(
            record.Id,
            record.HospitalId,
            record.VisitId,
            record.PipelineVersion,
            record.Status.ToWireValue(),
            traceId,
            record.CreatedAt,
            record.CompletedAt);
    }

    private void EnsureHospital()
    {
        if (requestContext.HospitalId == Guid.Empty)
        {
            throw new ValidationException("X-Hospital-Id 不能为空。");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException sqlException
            && (sqlException.Number == 2601 || sqlException.Number == 2627);
    }
}
