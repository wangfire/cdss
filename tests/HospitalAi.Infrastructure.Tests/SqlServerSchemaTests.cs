using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Infrastructure.Tests;

public sealed class SqlServerSchemaTests
{
    [Fact]
    public void Model_包含阶段一核心表()
    {
        using var context = CreateContext();

        var tableNames = context.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expectedTables = new[]
        {
            "hospital",
            "visit",
            "coding_task",
            "outbox_message",
            "inbox_message",
            "pipeline_trace",
            "pipeline_trace_step",
            "audit_log"
        };

        Assert.True(expectedTables.All(tableNames.Contains));
    }

    [Fact]
    public void Model_核心实体使用Guid主键和rowversion并发列()
    {
        using var context = CreateContext();

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var primaryKey = entityType.FindPrimaryKey();

            Assert.NotNull(primaryKey);
            Assert.Single(primaryKey!.Properties);
            Assert.Equal(typeof(Guid), primaryKey.Properties[0].ClrType);
        }

        var rowVersionProperties = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => property.Name == "RowVersion")
            .ToArray();

        Assert.NotEmpty(rowVersionProperties);
        Assert.All(
            rowVersionProperties,
            property =>
            {
                Assert.Equal(typeof(byte[]), property.ClrType);
                Assert.True(property.IsConcurrencyToken);
                Assert.True(property.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate);
            });
    }

    [Fact]
    public void Model_核心业务表包含医院隔离字段()
    {
        using var context = CreateContext();

        var tenantScopedTables = new[]
        {
            "visit",
            "coding_task",
            "outbox_message",
            "inbox_message",
            "pipeline_trace",
            "pipeline_trace_step",
            "audit_log"
        };

        foreach (var tableName in tenantScopedTables)
        {
            var entityType = context.Model.GetEntityTypes()
                .Single(entityType => entityType.GetTableName() == tableName);

            Assert.NotNull(entityType.FindProperty("HospitalId"));
            Assert.Equal(typeof(Guid), entityType.FindProperty("HospitalId")!.ClrType);
        }
    }

    [Fact]
    public void Model_配置就诊和编码任务的显式外键()
    {
        using var context = CreateContext();

        var visit = context.Model.GetEntityTypes()
            .Single(entityType => entityType.GetTableName() == "visit");
        var codingTask = context.Model.GetEntityTypes()
            .Single(entityType => entityType.GetTableName() == "coding_task");

        Assert.Contains(
            visit.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "hospital");
        Assert.Contains(
            visit.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "patient");
        Assert.Contains(
            codingTask.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "visit");
    }

    [Fact]
    public void Model_为Inbox配置消息和消费者的唯一索引()
    {
        using var context = CreateContext();

        var inbox = context.Model.GetEntityTypes()
            .Single(entityType => entityType.GetTableName() == "inbox_message");
        var uniqueIndex = inbox.GetIndexes()
            .Single(index => index.IsUnique);

        Assert.Equal(
            new[] { "MessageId", "ConsumerName" },
            uniqueIndex.Properties.Select(property => property.Name).ToArray());
    }

    private static HospitalAiDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<HospitalAiDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=HospitalAiSchemaTests;Trusted_Connection=True;")
            .Options;

        return new HospitalAiDbContext(options);
    }
}
