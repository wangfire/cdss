using HospitalAi.Application.Abstractions;
using HospitalAi.Contracts.Models;
using Microsoft.Extensions.Options;

namespace HospitalAi.Infrastructure.ModelGateways;

/// <summary>
/// 静态模型路由器：按 model_code 与配置的 ModelMode 判断可用性。
/// 不可用时必须给出可记录到 Trace 的原因。
/// </summary>
public sealed class StaticModelRouter : IModelRouter
{
    private readonly ModelGatewayOptions _options;

    public StaticModelRouter(IOptions<ModelGatewayOptions> options)
    {
        _options = options.Value;
    }

    public Task<ModelRouteDecision> ResolveAsync(
        string modelCode,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Models.TryGetValue(modelCode, out var modelName)
            || string.IsNullOrWhiteSpace(modelName))
        {
            return Task.FromResult(new ModelRouteDecision(
                modelCode,
                Available: false,
                Reason: UnavailableModelGateway.ModelUnavailable));
        }

        var mode = _options.ModelMode;
        if (string.Equals(mode, nameof(ModelMode.Unavailable), StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ModelRouteDecision(
                modelCode,
                Available: false,
                Reason: "MODEL_MODE_UNAVAILABLE"));
        }

        return Task.FromResult(new ModelRouteDecision(modelCode, Available: true, Reason: null));
    }
}
