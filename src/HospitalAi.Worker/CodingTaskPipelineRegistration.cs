using HospitalAi.Infrastructure.CodingKnowledge;
using HospitalAi.Infrastructure.ModelGateways;
using HospitalAi.Worker.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HospitalAi.Worker;

/// <summary>
/// 编码流水线 DI 注册。同时注册 Legacy Runner（历史回放）与 V2.2-Lite Runner，
/// 消费方只依赖 ICodingTaskPipelineDispatcher，由 Dispatcher 按任务 PipelineVersion 选择。
/// </summary>
public static class CodingTaskPipelineRegistration
{
    public static IServiceCollection AddCodingTaskPipeline(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<V22PipelineOptions>(options =>
        {
            configuration.GetSection(V22PipelineOptions.SectionName).Bind(options);
        });
        services.Configure<PipelineDispatchOptions>(options =>
        {
            configuration.GetSection(PipelineDispatchOptions.SectionName).Bind(options);
        });
        // 与 API 共用同一份灰度配置：新建编码任务缺省使用的 PipelineVersion。
        services.AddSingleton(
            _ => Infrastructure.CodingTasks.CodingTaskPipelineOptions.FromConfiguration(configuration));

        // Legacy Runner：仅用于历史回放（旧版本任务）。
        services.AddScoped<SqlServerCodingRecommendationPipelineRunner>();
        services.AddScoped<ICodingTaskPipelineRunner>(
            serviceProvider => serviceProvider.GetRequiredService<SqlServerCodingRecommendationPipelineRunner>());

        // V2.2-Lite Runner：新任务默认执行器。
        services.AddScoped<V22LiteCodingPipelineRunner>();
        services.AddScoped<ICodingTaskPipelineRunner>(
            serviceProvider => serviceProvider.GetRequiredService<V22LiteCodingPipelineRunner>());
        services.AddScoped<Application.Abstractions.IV22RecommendationExecutor, V22LiteRecommendationExecutor>();

        services.AddModelGateways(configuration);
        services.AddCodingKnowledgeSearch(configuration);
        services.AddScoped<ICodingTaskPipelineDispatcher, CodingTaskPipelineDispatcher>();
        return services;
    }
}
