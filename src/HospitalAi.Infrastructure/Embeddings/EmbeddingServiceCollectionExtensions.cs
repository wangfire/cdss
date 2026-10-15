using HospitalAi.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HospitalAi.Infrastructure.Embeddings;

/// <summary>向量检索、本地嵌入与交叉编码重排的 DI 注册（WP-B3-1 / WP-B3-2）。</summary>
public static class EmbeddingServiceCollectionExtensions
{
    public static IServiceCollection AddVectorSearch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmbeddingOptions>(
            configuration.GetSection(EmbeddingOptions.SectionName));
        services.AddSingleton<IEmbeddingProvider, OnnxEmbeddingProvider>();
        services.AddSingleton<IVectorSearch, InMemoryVectorSearch>();
        return services;
    }

    public static IServiceCollection AddRerank(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RerankOptions>(
            configuration.GetSection(RerankOptions.SectionName));
        services.AddSingleton<IReranker, OnnxReranker>();
        return services;
    }
}
