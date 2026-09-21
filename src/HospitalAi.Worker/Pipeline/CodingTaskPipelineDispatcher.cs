using HospitalAi.Contracts.CodingTasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HospitalAi.Worker.Pipeline;

/// <summary>
/// 按任务 PipelineVersion 选择 Runner 的分发器。
/// 规则：新任务使用配置的默认版本（V2.2-Lite 默认 phase1-v2.2-lite）；
/// 旧任务继续保留旧 Runner，仅用于历史回放；Legacy Runner 不得处理新 V2.2 任务；
/// 不能因“模型不可用”回退到 Legacy Runner。
/// </summary>
public sealed class CodingTaskPipelineDispatcher : ICodingTaskPipelineDispatcher
{
    private readonly IEnumerable<ICodingTaskPipelineRunner> _runners;
    private readonly PipelineDispatchOptions _options;
    private readonly ILogger<CodingTaskPipelineDispatcher> _logger;

    public CodingTaskPipelineDispatcher(
        IEnumerable<ICodingTaskPipelineRunner> runners,
        IOptions<PipelineDispatchOptions> options,
        ILogger<CodingTaskPipelineDispatcher> logger)
    {
        _runners = runners;
        _options = options.Value;
        _logger = logger;
    }

    public ICodingTaskPipelineRunner Select(string pipelineVersion)
    {
        var requested = string.IsNullOrWhiteSpace(pipelineVersion)
            ? (_options.DefaultPipelineVersion ?? PipelineVersions.Lite)
            : pipelineVersion.Trim();
        var normalized = PipelineVersions.IsKnown(requested)
            ? requested
            : _options.DefaultPipelineVersion ?? PipelineVersions.Lite;

        var runner = _runners.FirstOrDefault(item => Supports(item, normalized))
            ?? _runners.FirstOrDefault(item => Supports(item, PipelineVersions.Lite))
            ?? _runners.FirstOrDefault();

        if (runner is null)
        {
            throw new InvalidOperationException("未注册任何编码流水线执行器。");
        }

        if (!Supports(runner, normalized))
        {
            // 没有执行器声明处理该版本时禁止兜底执行：
            // 不能通过“模型不可用”或其他降级原因把 V2.2 任务回退到 Legacy Runner。
            throw new InvalidOperationException(
                $"PipelineVersion={normalized} 没有声明支持该版本的执行器，拒绝降级执行。");
        }

        _logger.LogInformation(
            "已选择编码流水线执行器，PipelineVersion={PipelineVersion}, Runner={Runner}",
            normalized,
            runner.GetType().Name);

        return runner;
    }

    /// <summary>
    /// 只认 Runner 自己声明的版本集合，不判断具体类型。
    /// 未声明能力的实现不参与任何版本匹配，只能作为显式兜底注册出现。
    /// </summary>
    private static bool Supports(ICodingTaskPipelineRunner runner, string pipelineVersion)
    {
        return runner is ICodingTaskPipelineCapabilities capabilities
            && capabilities.SupportedPipelineVersions.Any(
                version => string.Equals(version, pipelineVersion, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// 分发配置。灰度切换只改变默认 PipelineVersion，不修改历史任务。
/// </summary>
public sealed class PipelineDispatchOptions
{
    public const string SectionName = "Coding:Pipeline";

    /// <summary>新任务的默认 PipelineVersion。</summary>
    public string? DefaultPipelineVersion { get; set; }
}
