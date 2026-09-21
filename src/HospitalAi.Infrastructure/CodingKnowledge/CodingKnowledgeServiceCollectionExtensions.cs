using HospitalAi.Application.Abstractions;
using HospitalAi.Infrastructure.Elasticsearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HospitalAi.Infrastructure.CodingKnowledge;

/// <summary>
/// 编码知识检索 DI：Exact（SQL）与 BM25（Elasticsearch）。
/// ES 未启用时 BM25 注册 Unavailable 实现，业务链路只走 Exact 快路径。
/// </summary>
public static class CodingKnowledgeServiceCollectionExtensions
{
    public static IServiceCollection AddCodingKnowledgeSearch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IExactCodingKnowledgeSearch, SqlServerExactCodingKnowledgeSearch>();

        services.Configure<ElasticsearchOptions>(options =>
        {
            configuration.GetSection(ElasticsearchOptions.SectionName).Bind(options);
        });

        var enabled = configuration.GetValue<bool>($"{ElasticsearchOptions.SectionName}:Enabled");
        if (enabled)
        {
            // 类型化客户端必须带 BaseAddress，否则索引器/检索使用相对 URI 会抛异常。
            services.AddHttpClient(nameof(ElasticsearchCodingKnowledgeSearch),
                (sp, client) => ConfigureClient(sp, client));
            services.AddHttpClient<ElasticsearchCodingKnowledgeSearch>(
                nameof(ElasticsearchCodingKnowledgeSearch));
            services.AddHttpClient(nameof(ElasticsearchCodingKnowledgeIndexer),
                (sp, client) => ConfigureClient(sp, client));
            services.AddHttpClient<ElasticsearchCodingKnowledgeIndexer>(
                nameof(ElasticsearchCodingKnowledgeIndexer));

            // 两者构造依赖 scoped 服务（DbContext 等），必须注册为 Scoped。
            services.AddScoped<ICodingKnowledgeSearch>(
                serviceProvider => ActivatorUtilities.CreateInstance<ElasticsearchCodingKnowledgeSearch>(
                    serviceProvider,
                    serviceProvider
                        .GetRequiredService<System.Net.Http.IHttpClientFactory>()
                        .CreateClient(nameof(ElasticsearchCodingKnowledgeSearch)),
                    serviceProvider.GetRequiredService<IOptions<ElasticsearchOptions>>().Value));
            services.AddScoped<ICodingKnowledgeIndexer>(
                serviceProvider => ActivatorUtilities.CreateInstance<ElasticsearchCodingKnowledgeIndexer>(
                    serviceProvider,
                    serviceProvider
                        .GetRequiredService<System.Net.Http.IHttpClientFactory>()
                        .CreateClient(nameof(ElasticsearchCodingKnowledgeIndexer)),
                    serviceProvider.GetRequiredService<IOptions<ElasticsearchOptions>>().Value));
        }
        else
        {
            services.AddSingleton<ICodingKnowledgeSearch, UnavailableCodingKnowledgeSearch>();
        }

        return services;
    }

    private static void ConfigureClient(
        IServiceProvider serviceProvider,
        System.Net.Http.HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;
        client.BaseAddress = new Uri(options.Url);
        // 单次请求超时下限 60 秒：索引重建 bulk 请求可能远超检索超时。
        client.Timeout = TimeSpan.FromSeconds(Math.Max(options.TimeoutSeconds, 60));
        if (!string.IsNullOrEmpty(options.Username))
        {
            var auth = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        }
    }
}
