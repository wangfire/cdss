using System.Text.Json;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HospitalAi.Worker.Tests;

/// <summary>
/// 终态写回补偿用例：进程中断遗留的 Running 任务必须被重新入队并跑完，
/// 未超时的在跑任务不得被抢走。
/// 需要 SQL Server（HOSPITAL_AI_TEST_CONNECTION_STRING）。
/// </summary>
public sealed class StrandedCodingTaskRecoveryTests
{
    private const string Fracture = "左胫骨平台粉碎性骨折";
    private const string FractureCode = "S82.142A";

    private static StrandedCodingTaskRecovery CreateRecovery() =>
        new(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StrandedCodingTaskRecovery>.Instance);

    /// <summary>把任务置成"进程中断遗留"形态：Running + 已推进到 AI 阶段 + 超过僵死阈值。</summary>
    private static async Task MakeStrandedAsync(
        V22TestDatabase database,
        Guid taskId,
        int minutesAgo)
    {
        var strandedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo);
        await using var context = database.CreateContext();
        await context.CodingTasks
            .Where(item => item.Id == taskId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, CodingTaskStatus.Running)
                .SetProperty(item => item.CodingStage, CodingStage.AiRecommending)
                .SetProperty(item => item.UpdatedAt, strandedAt));
    }

    [Fact]
    public async Task 遗留Running任务_补偿入队后可被消费者跑完()
    {
        await using var database = await V22TestDatabase.CreateAsync();
        var (hospitalId, taskId, _) = await database.SeedV22TaskAsync($"【出院诊断】{Fracture}。");
        await database.SeedCodeAsync(hospitalId, "ICD-10", FractureCode, Fracture);
        await database.SeedIcdIndexRowAsync(900001, Fracture, FractureCode);
        await MakeStrandedAsync(database, taskId, minutesAgo: 30);

        await using (var recoveryContext = database.CreateContext())
        {
            Assert.Equal(1, await CreateRecovery().RecoverStrandedBatchAsync(recoveryContext));
        }

        await using (var verifyContext = database.CreateContext())
        {
            var task = await verifyContext.CodingTasks.SingleAsync(item => item.Id == taskId);
            Assert.Equal(CodingTaskStatus.Pending, task.Status);

            var pending = await verifyContext.OutboxMessages
                .Where(item => item.PublishedAt == null)
                .SingleAsync();
            Assert.Equal("coding.task.created", pending.MessageType);

            // 补偿投递的消息必须能被消费者正常接管。
            var message = JsonSerializer.Deserialize<CodingTaskCreatedMessage>(
                pending.PayloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(task.Id, message.TaskId);
            await database.CreateConsumer().ProcessAsync(message);
        }

        await using var finalContext = database.CreateContext();
        var finalTask = await finalContext.CodingTasks.SingleAsync(item => item.Id == taskId);
        Assert.NotEqual(CodingTaskStatus.Pending, finalTask.Status);
        Assert.NotEqual(CodingTaskStatus.Running, finalTask.Status);
        Assert.NotEmpty(await finalContext.CodingRecommendations.ToListAsync());
    }

    [Fact]
    public async Task 未超僵死阈值的Running任务_补偿不得抢走()
    {
        await using var database = await V22TestDatabase.CreateAsync();
        var (_, taskId, _) = await database.SeedV22TaskAsync("【出院诊断】任意诊断。");
        await MakeStrandedAsync(database, taskId, minutesAgo: 1);

        await using var context = database.CreateContext();
        Assert.Equal(0, await CreateRecovery().RecoverStrandedBatchAsync(context));

        var task = await context.CodingTasks.SingleAsync(item => item.Id == taskId);
        Assert.Equal(CodingTaskStatus.Running, task.Status);
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }
}
