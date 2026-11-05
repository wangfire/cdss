namespace HospitalAi.Application.Coding.NameLookup;

/// <summary>
/// "编码录入"名称解析的标题通道开关（与 V2.2-Lite 流水线同一组运行时配置，
/// 来源 pipeline_runtime_config("v22lite")，基线 V22PipelineOptions，工作台设置抽屉统一管理）：
/// - IcdTitleDirect：诊断侧字典标题直达候选是否置顶参与；
/// - Cm3TitleDirect：手术侧字典标题直达候选是否置顶参与；
/// - IcdTitleReverse：诊断侧同族标题反查（IndexCodeExpander 展开前的同族反查）是否启用。
/// CM3 展开器没有独立的反查开关（流水线口径一致），故不设 Cm3TitleReverse。
/// </summary>
public sealed record NameLookupTitleChannels(
    bool IcdTitleDirect,
    bool Cm3TitleDirect,
    bool IcdTitleReverse)
{
    /// <summary>全开（与 V22PipelineOptions 基线默认一致）。</summary>
    public static readonly NameLookupTitleChannels AllEnabled = new(true, true, true);
}

/// <summary>标题通道开关的配置来源抽象：实现方负责读运行时覆盖并回落基线。</summary>
public interface ITitleChannelConfigSource
{
    NameLookupTitleChannels Get();
}
