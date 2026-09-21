using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Application.Security;
using HospitalAi.Contracts.Security;
using HospitalAi.Infrastructure.Security;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Application.Tests;

public sealed class RbacServiceTests
{
    [Fact]
    public async Task CreateUserAsync_创建用户并返回相同医院内的幂等结果()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        await database.SeedHospitalAsync(hospitalId);
        await using var context = database.CreateContext();
        var service = CreateService(context, hospitalId);

        var request = new CreateUserRequest("op-001", "张三");
        var first = await service.CreateUserAsync(request);
        var second = await service.CreateUserAsync(request);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("张三", first.DisplayName);
        Assert.Equal("ACTIVE", first.Status);
        Assert.Equal(1, await context.AppUsers.CountAsync());
    }

    [Fact]
    public async Task CreateRoleAsync_创建角色并在重复代码时幂等()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        await database.SeedHospitalAsync(hospitalId);
        await using var context = database.CreateContext();
        var service = CreateService(context, hospitalId);

        var request = new CreateRoleRequest("ROLE_CODER", "编码员", "负责病案编码");
        var first = await service.CreateRoleAsync(request);
        var second = await service.CreateRoleAsync(request);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("编码员", first.Name);
        Assert.Equal(1, await context.AppRoles.CountAsync());
    }

    [Fact]
    public async Task AssignRoleAsync_为用户分配角色并可查询()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        await database.SeedHospitalAsync(hospitalId);
        await using var context = database.CreateContext();
        var service = CreateService(context, hospitalId);

        var user = await service.CreateUserAsync(new CreateUserRequest("op-001", "张三"));
        var role = await service.CreateRoleAsync(new CreateRoleRequest("ROLE_CODER", "编码员"));

        await service.AssignRoleAsync(user.Id, role.Id);

        var roles = await service.GetRolesByUserAsync(user.Id);
        Assert.Single(roles);
        Assert.Equal("ROLE_CODER", roles.Single().Code);
        Assert.Equal(1, await context.AppUserRoles.CountAsync());
    }

    [Fact]
    public async Task AssignRoleAsync_跨医院资源访问抛出资源未找到()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        var otherHospitalId = Guid.NewGuid();
        await database.SeedHospitalAsync(hospitalId);
        await database.SeedHospitalAsync(otherHospitalId);
        await using var context = database.CreateContext();
        var service = CreateService(context, hospitalId);

        await using var otherContext = database.CreateContext();
        var userInOtherHospital = await new SqlServerSecurityService(
            otherContext,
            new FixedRequestContext(otherHospitalId)).CreateUserAsync(
                new CreateUserRequest("op-002", "李四"));

        await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => service.AssignRoleAsync(userInOtherHospital.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task SetUserStatusAsync_停用用户后状态变更()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        await database.SeedHospitalAsync(hospitalId);
        await using var context = database.CreateContext();
        var service = CreateService(context, hospitalId);

        var user = await service.CreateUserAsync(new CreateUserRequest("op-001", "张三"));

        await service.SetUserStatusAsync(user.Id, "DISABLED");

        var updated = await context.AppUsers.SingleAsync(item => item.Id == user.Id);
        Assert.Equal("DISABLED", updated.Status);
    }

    private static ISecurityService CreateService(
        HospitalAiDbContext context,
        Guid hospitalId)
    {
        return new SqlServerSecurityService(context, new FixedRequestContext(hospitalId));
    }

    private sealed class FixedRequestContext(
        Guid HospitalId,
        string? RequestId = null,
        string? TraceId = null,
        string? UserId = null) : IRequestContext
    {
        public Guid HospitalId { get; } = HospitalId;

        public string RequestId { get; } = RequestId ?? Guid.NewGuid().ToString("N");

        public string TraceId { get; } = TraceId ?? Guid.NewGuid().ToString("N");

        public string? IdempotencyKey { get; } = null;

        public string? UserId { get; } = UserId;
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
                    "运行 RBAC 集成测试前必须设置 HOSPITAL_AI_TEST_CONNECTION_STRING。");
            }

            var databaseName = $"HospitalAi_Rbac_{Guid.NewGuid():N}";
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

        public async Task SeedHospitalAsync(Guid hospitalId)
        {
            var now = DateTimeOffset.UtcNow;
            await using var context = CreateContext();
            if (await context.Hospitals.AnyAsync(item => item.Id == hospitalId))
            {
                return;
            }

            context.Hospitals.Add(new HospitalRecord
            {
                Id = hospitalId,
                Code = $"RBAC-{hospitalId:N}"[..32],
                Name = "RBAC Test Hospital",
                Status = "ACTIVE",
                CreatedAt = now,
                UpdatedAt = now
            });
            await context.SaveChangesAsync();
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
                END
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
