using HospitalAi.Application.Coding.NameLookup;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HospitalAi.Api;

/// <summary>
/// "编码录入"标题通道开关的配置来源：与 GET/PUT /api/v1/admin/pipeline-config 同一存储
/// （pipeline_runtime_config 行 "v22lite" 的 JSON 覆盖），行缺失或键缺失时回落
/// V22PipelineOptions 基线。工作台设置抽屉改开关后，编码录入下一次解析即生效。
/// </summary>
public sealed class RuntimeTitleChannelConfigSource(HospitalAiDbContext dbContext, IOptions<V22PipelineOptions> baseline) : ITitleChannelConfigSource
{
    public NameLookupTitleChannels Get()
    {
        var defaults = baseline.Value;
        var row = dbContext.PipelineRuntimeConfigs
            .AsNoTracking()
            .SingleOrDefault(item => item.ConfigKey == "v22lite");
        if (row is null)
        {
            return new NameLookupTitleChannels(
                defaults.IcdTitleDirectEnabled,
                defaults.Cm3TitleDirectEnabled,
                defaults.IcdTitleReverseEnabled);
        }

        using var doc = System.Text.Json.JsonDocument.Parse(row.ConfigJson);
        var root = doc.RootElement;
        return new NameLookupTitleChannels(
            ReadChannel(root, "IcdTitleDirectEnabled", defaults.IcdTitleDirectEnabled),
            ReadChannel(root, "Cm3TitleDirectEnabled", defaults.Cm3TitleDirectEnabled),
            ReadChannel(root, "IcdTitleReverseEnabled", defaults.IcdTitleReverseEnabled));
    }

    /// <summary>键存在且为布尔取其值；键缺失或非布尔回落基线（与 admin GET 端点同口径）。</summary>
    private static bool ReadChannel(System.Text.Json.JsonElement root, string name, bool fallback)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => fallback,
        };
    }
}
