namespace HospitalAi.Worker.Pipeline;

/// <summary>
/// V2.2-Lite 流水线配置。版本号与阈值全部配置化，禁止写死在业务代码中。
/// </summary>
public sealed class V22PipelineOptions
{
    public const string SectionName = "Coding:V22Lite";

    /// <summary>知识库版本，用于 Exact 过滤与 ES 索引名。</summary>
    public string KnowledgeVersion { get; set; } = "v1";

    /// <summary>编码体系版本（当前生效 CodeSystem.Version）。</summary>
    public string CodingVersion { get; set; } = "v1";

    /// <summary>文档版本标识，用于 ES medical-chunks 索引。</summary>
    public string DocumentVersion { get; set; } = "v1";

    /// <summary>本次评分使用的规则版本。</summary>
    public string RuleVersion { get; set; } = "v1";

    /// <summary>同一诊断输入进入评分的最大候选数。</summary>
    public int MaxCandidatesPerInput { get; set; } = 3;

    /// <summary>是否启用规则引擎。默认启用；关闭时 RuleScore 视为缺省不参与。</summary>
    public bool RuleEngineEnabled { get; set; } = true;

    /// <summary>事实 / 解释生成使用的模型 code（业务只引用 code）。</summary>
    public string ExplanationModelCode { get; set; } = HospitalAi.Contracts.Models.ModelCodes.Small;

    /// <summary>LlmScore 校验用的解释 Prompt 版本。</summary>
    public string PromptVersion { get; set; } = "v22-explanation-v1";
}
