using System.Diagnostics.Metrics;
using HospitalAi.Contracts.CodingTasks;
using OpenTelemetry.Metrics;

namespace HospitalAi.Worker.Observability;

/// <summary>
/// Worker 流水线指标，使用 System.Diagnostics Meter 暴露给 OpenTelemetry。
/// 在编码任务消费者中记录处理结果，供 Prometheus 抓取。
/// </summary>
public static class PipelineMetrics
{
    private static readonly Meter Meter = new("HospitalAi.Worker");

    private static readonly Counter<long> ProcessingCompleted =
        Meter.CreateCounter<long>(
            "hospitalai.worker.processing_completed",
            "count",
            "编码任务处理完成次数（按结果分类）");

    private static readonly Counter<long> PipelineExecuted =
        Meter.CreateCounter<long>(
            "hospitalai.worker.pipeline_executed",
            "count",
            "编码流水线执行次数");

    private static readonly Counter<long> PipelineFailed =
        Meter.CreateCounter<long>(
            "hospitalai.worker.pipeline_failed",
            "count",
            "编码流水线执行失败次数");

    /// <summary>
    /// 记录一次流水线执行结果。
    /// </summary>
    public static void RecordProcessingCompleted(string result, CodingTaskCreatedMessage message)
    {
        ProcessingCompleted.Add(1, new KeyValuePair<string, object?>(
            "result",
            result),
            new KeyValuePair<string, object?>("pipelineVersion", message.PipelineVersion));
    }

    /// <summary>
    /// 记录一次流水线执行成功。
    /// </summary>
    public static void RecordPipelineExecuted(CodingTaskCreatedMessage message)
    {
        PipelineExecuted.Add(1, new KeyValuePair<string, object?>(
            "pipelineVersion",
            message.PipelineVersion));
    }

    /// <summary>
    /// 记录一次流水线执行失败。
    /// </summary>
    public static void RecordPipelineFailed(string errorCode, CodingTaskCreatedMessage message)
    {
        PipelineFailed.Add(1, new KeyValuePair<string, object?>(
            "errorCode",
            errorCode),
            new KeyValuePair<string, object?>("pipelineVersion", message.PipelineVersion));
    }
}
