using HospitalAi.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HospitalAi.Infrastructure.Embeddings;

/// <summary>向量检索与本地嵌入的 DI 注册（WP-B3-1）。</summary>
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
}
