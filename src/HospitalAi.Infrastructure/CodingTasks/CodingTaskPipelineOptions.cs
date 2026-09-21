using HospitalAi.Contracts.CodingTasks;
using Microsoft.Extensions.Configuration;

namespace HospitalAi.Infrastructure.CodingTasks;

/// <summary>
/// 编码任务流水线配置。<c>Coding:Pipeline</c> 节。
/// DefaultPipelineVersion 是灰度开关：新建任务缺省使用的版本，
/// 只影响新任务，不回改历史任务的 PipelineVersion。
/// </summary>
public sealed class CodingTaskPipelineOptions
{
    public const string SectionName = "Coding:Pipeline";

    /// <summary>新建编码任务缺省使用的流水线版本。</summary>
    public string DefaultPipelineVersion { get; set; } = PipelineVersions.Lite;

    public static CodingTaskPipelineOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new CodingTaskPipelineOptions();
        var section = configuration.GetSection(SectionName);
        var configured = section["DefaultPipelineVersion"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            options.DefaultPipelineVersion = configured;
        }

        return options;
    }

    /// <summary>
    /// 解析请求携带的版本；缺省或空白时回落到配置的默认版本。
    /// </summary>
    public string Resolve(string? requested)
    {
        return string.IsNullOrWhiteSpace(requested)
            ? DefaultPipelineVersion
            : requested;
    }
}
