namespace HospitalAi.Contracts.CodingTasks;

/// <summary>
/// V2.2 流水线版本常量。灰度切换只改变默认值，不回改历史任务。
/// </summary>
public static class PipelineVersions
{
    /// <summary>旧 MVP：文书切段 + SQL 字典包含匹配。仅保留用于历史回放，只读。</summary>
    public const string Legacy = "phase2-mvp-legacy";

    /// <summary>V2.2-Lite：无 GPU 可完整验收的 Exact + BM25 + 规则 + 评分 + 安全策略。</summary>
    public const string Lite = "phase1-v2.2-lite";

    /// <summary>V2.2-Full：向量、重排、模型接入后的完整管线。</summary>
    public const string Full = "phase1-v2.2-full";

    public static bool IsKnown(string? pipelineVersion)
    {
        return pipelineVersion is Legacy or Lite or Full;
    }

    public static bool IsV22(string? pipelineVersion)
    {
        return pipelineVersion is Lite or Full;
    }
}
