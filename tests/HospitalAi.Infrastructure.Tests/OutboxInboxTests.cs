using HospitalAi.Infrastructure.Outbox;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Domain.CodingTasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Infrastructure.Tests;

public sealed class OutboxInboxTests
{
    [Fact]
    public async Task 业务写入和Outbox写入在同一事务中_回滚后都不存在()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using (var context = database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var hospitalId = Guid.NewGuid();

            context.Hospitals.Add(new HospitalRecord
            {
                Id = hospitalId,
                Code = "TASK5-ROLLBACK",
                Name = "Task5 Rollback Hospital",
                Status = "ACTIVE",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            var outboxStore = new OutboxStore(context);
            outboxStore.Add(OutboxMessage.Create(
                hospitalId,
                "coding.task.created",
                """{"taskId":"00000000-0000-0000-0000-000000000001"}"""));

            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verifyContext = database.CreateContext();
        Assert.Empty(await verifyContext.Hospitals.ToListAsync());
        Assert.Empty(await verifyContext.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Inbox重复消息_只允许第一次开始处理()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        var message = InboxMessage.Create(
            hospitalId,
            Guid.NewGuid(),
            "CodingTaskCreatedConsumer");

        await using var context = database.CreateContext();
        context.Hospitals.Add(new HospitalRecord
        {
            Id = hospitalId,
            Code = "TASK5-INBOX",
            Name = "Task5 Inbox Hospital",
            Status = "ACTIVE",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var inboxStore = new InboxStore(context);

        var firstAccepted = await inboxStore.TryBeginAsync(message);
        var secondAccepted = await inboxStore.TryBeginAsync(message);

        Assert.True(firstAccepted);
        Assert.False(secondAccepted);
        Assert.Equal(1, await context.InboxMessages.CountAsync());
    }

    [Fact]
    public async Task OutboxPublisher_发布成功后标记消息()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        var message = OutboxMessage.Create(
            hospitalId,
            "coding.task.created",
            """{"taskId":"00000000-0000-0000-0000-000000000002"}""");

        await using var context = database.CreateContext();
        context.Hospitals.Add(new HospitalRecord
        {
            Id = hospitalId,
            Code = "TASK5-PUBLISH",
            Name = "Task5 Publish Hospital",
            Status = "ACTIVE",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        new OutboxStore(context).Add(message);
        await context.SaveChangesAsync();

        var eventBus = new RecordingEventBus();
        var publisher = new OutboxPublisher(context, eventBus);

        var publishedCount = await publisher.PublishPendingAsync();

        var savedMessage = await context.OutboxMessages
            .SingleAsync(item => item.Id == message.Id);
        Assert.Equal(1, publishedCount);
        Assert.Single(eventBus.Messages);
        Assert.NotNull(savedMessage.PublishedAt);
    }

    [Fact]
    public async Task Inbox并发重复插入_只有一个消费者获得处理权()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        var message = InboxMessage.Create(
            hospitalId,
            Guid.NewGuid(),
            "CodingTaskCreatedConsumer");

        await using (var seedContext = database.CreateContext())
        {
            seedContext.Hospitals.Add(new HospitalRecord
            {
                Id = hospitalId,
                Code = "TASK5-CONCURRENT",
                Name = "Task5 Concurrent Hospital",
                Status = "ACTIVE",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var results = await Task.WhenAll(
            new InboxStore(firstContext).TryBeginAsync(message),
            new InboxStore(secondContext).TryBeginAsync(message));

        Assert.Single(results, accepted => accepted);
        Assert.Single(results, accepted => !accepted);

        await using var verifyContext = database.CreateContext();
        Assert.Equal(1, await verifyContext.InboxMessages.CountAsync());
    }

    [Fact]
    public void CodingTaskIdempotencyKey_由任务Id和Pipeline版本稳定生成()
    {
        var taskId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var first = CodingTaskIdempotencyKey.Create(taskId, "pipeline-v1");
        var same = CodingTaskIdempotencyKey.Create(taskId, "pipeline-v1");
        var differentVersion = CodingTaskIdempotencyKey.Create(taskId, "pipeline-v2");

        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:pipeline-v1", first);
        Assert.Equal(first, same);
        Assert.NotEqual(first, differentVersion);
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
                    "运行 Infrastructure 集成测试前必须设置 HOSPITAL_AI_TEST_CONNECTION_STRING。");
            }

            var databaseName = $"HospitalAi_Task5_{Guid.NewGuid():N}";
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

    private sealed class RecordingEventBus : IEventBus
    {
        public List<(Guid MessageId, string MessageType, string PayloadJson)> Messages { get; } = [];

        public Task PublishAsync(
            Guid messageId,
            Guid hospitalId,
            string messageType,
            string payloadJson,
            CancellationToken cancellationToken = default)
        {
            Messages.Add((messageId, messageType, payloadJson));
            return Task.CompletedTask;
        }
    }
}
