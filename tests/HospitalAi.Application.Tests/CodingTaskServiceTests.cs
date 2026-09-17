using HospitalAi.Application.Abstractions;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.CodingTasks;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HospitalAi.Application.Tests;

public sealed class CodingTaskServiceTests
{
    [Fact]
    public async Task CreateAsync_创建待处理任务并写入Outbox()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        var visitId = await database.SeedVisitAsync(hospitalId);
        await using var context = database.CreateContext();
        var service = CreateService(context, hospitalId, "trace-create");

        var response = await service.CreateAsync(
            new(visitId, "pipeline-v1"),
            "idem-create");

        var savedTask = await context.CodingTasks.SingleAsync(item => item.Id == response.Id);
        var outbox = await context.OutboxMessages.SingleAsync();

        Assert.Equal(hospitalId, response.HospitalId);
        Assert.Equal(visitId, response.VisitId);
        Assert.Equal("PENDING", response.Status);
        Assert.Equal("trace-create", response.TraceId);
        Assert.Equal(CodingTaskStatus.Pending, savedTask.Status);
        Assert.Equal("coding.task.created", outbox.MessageType);

        var message = JsonSerializer.Deserialize<CodingTaskCreatedMessage>(
            outbox.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(message);
        Assert.Equal(outbox.Id, message.MessageId);
        Assert.Equal(hospitalId, message.HospitalId);
        Assert.Equal(response.Id, message.TaskId);
        Assert.Equal(visitId, message.VisitId);
        Assert.Equal("pipeline-v1", message.PipelineVersion);
        Assert.Equal("trace-create", message.TraceId);
        Assert.NotNull(message.RequestId);
    }

    [Fact]
    public async Task CreateAsync_重复IdempotencyKey返回第一次结果()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        var visitId = await database.SeedVisitAsync(hospitalId);
        await using var context = database.CreateContext();
        var service = CreateService(context, hospitalId, "trace-idem");

        var first = await service.CreateAsync(new(visitId, "pipeline-v1"), "idem-repeat");
        var second = await service.CreateAsync(new(visitId, "pipeline-v1"), "idem-repeat");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.TraceId, second.TraceId);
        Assert.Equal(1, await context.CodingTasks.CountAsync());
        Assert.Equal(1, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task GetAsync_跨医院查询返回Null()
    {
        await using var database = await TestDatabase.CreateAsync();
        var firstHospitalId = Guid.NewGuid();
        var secondHospitalId = Guid.NewGuid();
        var visitId = await database.SeedVisitAsync(firstHospitalId);

        await using (var createContext = database.CreateContext())
        {
            var createService = CreateService(createContext, firstHospitalId, "trace-first");
            var created = await createService.CreateAsync(new(visitId, "pipeline-v1"), "idem-first");

            await using var queryContext = database.CreateContext();
            var queryService = CreateService(queryContext, secondHospitalId, "trace-second");

            Assert.Null(await queryService.GetAsync(created.Id));
        }
    }

    private static ICodingTaskService CreateService(
        HospitalAiDbContext context,
        Guid hospitalId,
        string traceId)
    {
        return new SqlServerCodingTaskService(
            context,
            new TestRequestContext(hospitalId, traceId));
    }

    private sealed record TestRequestContext(
        Guid HospitalId,
        string TraceId) : IRequestContext
    {
        public string RequestId { get; } = Guid.NewGuid().ToString("N");

        public string? IdempotencyKey { get; } = null;

        public string? UserId { get; } = null;
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _databaseName;
        private readonly string _masterConnectionString;
        private readonly string _connectionString;

        private TestDatabase(
            string databaseName,
            string masterConnectionString,
            string connectionString)
        {
            _databaseName = databaseName;
            _masterConnectionString = masterConnectionString;
            _connectionString = connectionString;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var template = Environment.GetEnvironmentVariable(
                "HOSPITAL_AI_TEST_CONNECTION_STRING");

            if (string.IsNullOrWhiteSpace(template))
            {
                throw new InvalidOperationException(
                    "运行 Application 集成测试前必须设置 HOSPITAL_AI_TEST_CONNECTION_STRING。");
            }

            var databaseName = $"HospitalAi_Task6_{Guid.NewGuid():N}";
            var builder = new SqlConnectionStringBuilder(template)
            {
                InitialCatalog = databaseName
            };
            var masterBuilder = new SqlConnectionStringBuilder(builder.ConnectionString)
            {
                InitialCatalog = "master"
            };

            var database = new TestDatabase(
                databaseName,
                masterBuilder.ConnectionString,
                builder.ConnectionString);

            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public HospitalAiDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<HospitalAiDbContext>()
                .UseSqlServer(_connectionString)
                .Options;

            return new HospitalAiDbContext(options);
        }

        public async Task<Guid> SeedVisitAsync(Guid hospitalId)
        {
            var now = DateTimeOffset.UtcNow;
            var patientId = Guid.NewGuid();
            var visitId = Guid.NewGuid();

            await using var context = CreateContext();
            context.Hospitals.Add(new HospitalRecord
            {
                Id = hospitalId,
                Code = $"HOSP-{hospitalId:N}"[..32],
                Name = "Task6 Test Hospital",
                Status = "ACTIVE",
                CreatedAt = now,
                UpdatedAt = now
            });
            context.Patients.Add(new PatientRecord
            {
                Id = patientId,
                HospitalId = hospitalId,
                SourceSystem = "TEST",
                SourcePatientId = patientId.ToString("N"),
                CreatedAt = now,
                UpdatedAt = now
            });
            context.Visits.Add(new VisitRecord
            {
                Id = visitId,
                HospitalId = hospitalId,
                PatientId = patientId,
                AdmissionAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
            await context.SaveChangesAsync();

            return visitId;
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new SqlConnection(_masterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{_databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_databaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
