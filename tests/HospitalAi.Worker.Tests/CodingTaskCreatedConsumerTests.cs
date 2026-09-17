using HospitalAi.Domain.CodingTasks;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker.Consumers;
using HospitalAi.Worker.Pipeline;
using HospitalAi.Worker.Retry;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HospitalAi.Worker.Tests;

public sealed class CodingTaskCreatedConsumerTests
{
    [Fact]
    public async Task 成功处理_任务成功并写入Trace步骤和审计()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await database.SeedTaskAsync("task7-success");
        var runner = new RecordingPipelineRunner();
        var consumer = database.CreateConsumer(runner);

        await consumer.ProcessAsync(seeded.Message);

        await using var context = database.CreateContext();
        var task = await context.CodingTasks.SingleAsync(item => item.Id == seeded.TaskId);
        var trace = await context.PipelineTraces
            .Include(item => item.Steps)
            .SingleAsync(item => item.CodingTaskId == seeded.TaskId);

        Assert.Equal(CodingTaskStatus.Success, task.Status);
        Assert.Equal(1, runner.Attempts);
        Assert.Equal("SUCCESS", trace.Status);
        Assert.Single(trace.Steps);
        Assert.Equal("SUCCESS", trace.Steps.Single().Status);
        Assert.Contains(
            await context.AuditLogs.Where(item => item.ResourceId == seeded.TaskId).ToListAsync(),
            item => item.Result == "SUCCESS");
    }

    [Fact]
    public async Task 瞬时失败_按5秒30秒180秒重试后成功()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await database.SeedTaskAsync("task7-retry");
        var runner = new RecordingPipelineRunner(failuresBeforeSuccess: 3);
        var delay = new RecordingRetryDelay();
        var consumer = database.CreateConsumer(runner, delay);

        await consumer.ProcessAsync(seeded.Message);

        await using var context = database.CreateContext();
        var task = await context.CodingTasks.SingleAsync(item => item.Id == seeded.TaskId);

        Assert.Equal(CodingTaskStatus.Success, task.Status);
        Assert.Equal(3, task.RetryCount);
        Assert.Equal(4, runner.Attempts);
        Assert.Equal(
            new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(180) },
            delay.Delays);
    }

    [Fact]
    public async Task 重试耗尽_任务进入Failed并保留错误码()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await database.SeedTaskAsync("task7-failed");
        var runner = new RecordingPipelineRunner(failuresBeforeSuccess: 4);
        var delay = new RecordingRetryDelay();
        var consumer = database.CreateConsumer(runner, delay);

        await consumer.ProcessAsync(seeded.Message);

        await using var context = database.CreateContext();
        var task = await context.CodingTasks.SingleAsync(item => item.Id == seeded.TaskId);
        var trace = await context.PipelineTraces.SingleAsync(item => item.CodingTaskId == seeded.TaskId);

        Assert.Equal(CodingTaskStatus.Failed, task.Status);
        Assert.Equal(4, task.RetryCount);
        Assert.Equal("PIPELINE_ERROR", task.ErrorCode);
        Assert.Equal("FAILED", trace.Status);
        Assert.Equal(
            new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(180) },
            delay.Delays);
        Assert.Contains(
            await context.AuditLogs.Where(item => item.ResourceId == seeded.TaskId).ToListAsync(),
            item => item.Result == "FAILED");
    }

    [Fact]
    public async Task 重复消息_Inbox只允许处理一次()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await database.SeedTaskAsync("task7-inbox");
        var runner = new RecordingPipelineRunner();
        var consumer = database.CreateConsumer(runner);

        await consumer.ProcessAsync(seeded.Message);
        await consumer.ProcessAsync(seeded.Message);

        Assert.Equal(1, runner.Attempts);
        await using var context = database.CreateContext();
        Assert.Equal(1, await context.InboxMessages.CountAsync());
    }

    [Fact]
    public async Task 未完成Inbox记录_允许恢复处理()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await database.SeedTaskAsync("task7-inbox-recover");
        var runner = new RecordingPipelineRunner();

        await using (var context = database.CreateContext())
        {
            var now = DateTimeOffset.UtcNow.AddMinutes(-10);
            context.InboxMessages.Add(new InboxMessageRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = seeded.HospitalId,
                MessageId = seeded.Message.MessageId,
                ConsumerName = "CodingTaskCreatedConsumer",
                ReceivedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
            await context.SaveChangesAsync();
        }

        var consumer = database.CreateConsumer(runner);

        await consumer.ProcessAsync(seeded.Message);

        Assert.Equal(1, runner.Attempts);
        await using var verifyContext = database.CreateContext();
        var inbox = await verifyContext.InboxMessages.SingleAsync();
        Assert.NotNull(inbox.ProcessedAt);
    }

    [Fact]
    public void RetryPolicy_提供5秒30秒180秒退避()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), RetryPolicy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(30), RetryPolicy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(180), RetryPolicy.GetDelay(3));
    }

    private sealed class RecordingPipelineRunner(int failuresBeforeSuccess = 0)
        : ICodingTaskPipelineRunner
    {
        public int Attempts { get; private set; }

        public Task RunAsync(
            CodingTaskCreatedMessage message,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts <= failuresBeforeSuccess)
            {
                throw new InvalidOperationException("PIPELINE_ERROR");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRetryDelay : IRetryDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
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
                    "运行 Worker 集成测试前必须设置 HOSPITAL_AI_TEST_CONNECTION_STRING。");
            }

            var databaseName = $"HospitalAi_Task7_{Guid.NewGuid():N}";
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

        public CodingTaskCreatedConsumer CreateConsumer(
            ICodingTaskPipelineRunner runner,
            IRetryDelay? delay = null)
        {
            return new CodingTaskCreatedConsumer(
                CreateContext(),
                runner,
                delay ?? new RecordingRetryDelay(),
                NullLogger<CodingTaskCreatedConsumer>.Instance);
        }

        public async Task<SeededTask> SeedTaskAsync(string suffix)
        {
            var now = DateTimeOffset.UtcNow;
            var hospitalId = Guid.NewGuid();
            var patientId = Guid.NewGuid();
            var visitId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var traceId = $"trace-{suffix}-{Guid.NewGuid():N}";
            var messageId = Guid.NewGuid();

            await using var context = CreateContext();
            context.Hospitals.Add(new HospitalRecord
            {
                Id = hospitalId,
                Code = $"H7-{Guid.NewGuid():N}"[..16],
                Name = "Task7 Test Hospital",
                Status = "ACTIVE",
                CreatedAt = now,
                UpdatedAt = now
            });
            context.Patients.Add(new PatientRecord
            {
                Id = patientId,
                HospitalId = hospitalId,
                SourceSystem = "TEST",
                SourcePatientId = Guid.NewGuid().ToString("N"),
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
            context.CodingTasks.Add(new CodingTaskRecord
            {
                Id = taskId,
                HospitalId = hospitalId,
                VisitId = visitId,
                PipelineVersion = "pipeline-v1",
                Status = CodingTaskStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            });
            context.PipelineTraces.Add(new PipelineTraceRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = hospitalId,
                CodingTaskId = taskId,
                TraceId = traceId,
                Status = "PENDING",
                StartedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
            await context.SaveChangesAsync();

            return new SeededTask(
                hospitalId,
                taskId,
                new CodingTaskCreatedMessage(
                    messageId,
                    hospitalId,
                    taskId,
                    visitId,
                    "pipeline-v1",
                    traceId));
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

    private sealed record SeededTask(
        Guid HospitalId,
        Guid TaskId,
        CodingTaskCreatedMessage Message);
}
