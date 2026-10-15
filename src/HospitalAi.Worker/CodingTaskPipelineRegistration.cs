using HospitalAi.Infrastructure.CodingKnowledge;
using HospitalAi.Infrastructure.Embeddings;
using HospitalAi.Infrastructure.ModelGateways;
using HospitalAi.Infrastructure.RabbitMq;
using HospitalAi.Worker.Consumers;
using HospitalAi.Worker.Pipeline;
using HospitalAi.Worker.Retry;
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

        // ICD 索引两段式链路：平台级索引树快照 + 医院字典快照（单例内存缓存，导入后失效重建）。
        services.AddSingleton<Application.Coding.IcdIndex.IIcdIndexSource, EfIcdIndexSnapshotSource>();
        services.AddSingleton<Application.Coding.IcdIndex.IIcdDictionarySource, EfIcdDictionarySource>();
        services.AddSingleton<Application.Coding.Matching.IIcdTermSynonymSource, EfIcdTermSynonymSource>();
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
        // WP-B3-1：本地 ONNX 嵌入 + 内存向量检索（CPU 小模型先行，决策 2）。
        services.AddVectorSearch(configuration);
        // WP-B3-2：交叉编码 Rerank（bge-reranker ONNX）。
        services.AddRerank(configuration);
        services.AddScoped<ICodingTaskPipelineDispatcher, CodingTaskPipelineDispatcher>();

        // WP-B2-3（决策 1）：病例录入只落任务 + Outbox，不再在 HTTP 请求内同步执行流水线。
        // RabbitMQ 禁用（本地/单机）时注册进程内兜底派发器，与 MQ 消费者按配置互斥；
        // 消费者与其重试等待器在此统一注册，供派发器复用同一套处理逻辑。
        services.AddScoped<IRetryDelay, TaskRetryDelay>();
        services.AddScoped<CodingTaskCreatedConsumer>();
        var rabbitMqEnabled = bool.TryParse(
            configuration[$"{RabbitMqOptions.SectionName}:Enabled"],
            out var parsedRabbitMqEnabled)
            && parsedRabbitMqEnabled;
        if (!rabbitMqEnabled)
        {
            services.AddHostedService<PendingCodingTaskDispatcher>();
        }
        else
        {
            // 只有 MQ 链路会留下"消息已 ack、任务却卡在 Running"的中断遗留行，补偿也只挂在这条链路上。
            services.AddHostedService<StrandedCodingTaskRecovery>();
        }

        return services;
    }
}
