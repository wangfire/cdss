using HospitalAi.Application.Abstractions;
using HospitalAi.Contracts.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HospitalAi.Infrastructure.ModelGateways;

/// <summary>
/// 模型网关 DI 注册。按 ModelMode 选择实现，默认 Unavailable；
/// 业务侧只引用 IModelGateway / IModelRouter 等接口，不绑定推理框架。
/// </summary>
public static class ModelGatewayServiceCollectionExtensions
{
    public static IServiceCollection AddModelGateways(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ModelGatewayOptions>(options =>
        {
            configuration.GetSection(ModelGatewayOptions.SectionName).Bind(options);
        });

        var mode = configuration.GetValue<string>($"{ModelGatewayOptions.SectionName}:ModelMode");
        var modeValue = Enum.TryParse<ModelMode>(mode, ignoreCase: true, out var parsed)
            ? parsed
            : ModelMode.Unavailable;

        if (modeValue == ModelMode.OpenAiCompatible)
        {
            services.AddHttpClient<OpenAiCompatibleModelGateway>();
            services.AddScoped<IModelGateway>(
                sp => sp.GetRequiredService<OpenAiCompatibleModelGateway>());
            services.AddScoped<IEmbeddingGateway>(
                sp => sp.GetRequiredService<OpenAiCompatibleModelGateway>());
            services.AddScoped<IRerankerGateway>(
                sp => sp.GetRequiredService<OpenAiCompatibleModelGateway>());
            services.AddScoped<IOcrGateway>(
                sp => sp.GetRequiredService<OpenAiCompatibleModelGateway>());
        }
        else if (modeValue == ModelMode.Recording)
        {
            services.AddSingleton<IModelGateway, RecordingModelGateway>();
            services.AddSingleton<IEmbeddingGateway, RecordingModelGateway>();
            services.AddSingleton<IRerankerGateway, RecordingModelGateway>();
            services.AddSingleton<IOcrGateway, RecordingModelGateway>();
        }
        else
        {
            services.AddSingleton<IModelGateway, UnavailableModelGateway>();
            services.AddSingleton<IEmbeddingGateway, UnavailableModelGateway>();
            services.AddSingleton<IRerankerGateway, UnavailableModelGateway>();
            services.AddSingleton<IOcrGateway, UnavailableModelGateway>();
        }

        services.AddSingleton<IJsonSchemaValidator, SimpleJsonSchemaValidator>();
        services.AddSingleton<IModelRouter, StaticModelRouter>();
        return services;
    }
}
