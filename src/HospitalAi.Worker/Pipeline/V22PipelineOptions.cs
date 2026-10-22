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

    /// <summary>诊断侧 ICD 索引两段式链路开关；关闭时诊断输入不召回（不回退字典直匹配）。</summary>
    public bool IcdIndexEnabled { get; set; } = true;

    /// <summary>
    /// 主导词召回保留条数（每条输入独立下钻）。万名离线双语料实测：8 席时真有产出的链路常排在
    /// 第 9~12 名（别名键 3 字的行按覆盖字数压过 2 字真主导词，如 瘢痕 之于 视网膜瘢痕），
    /// 12 席可吃到绝大部分（ICD10CAP Top1 +74/−1、JZEY +19/−3、两侧 becameEmpty 均为 0），
    /// 再往放宽只换来跨章借道（24 席 JZEY 流失翻到 8 条）。
    /// </summary>
    public int LeadWordTopK { get; set; } = 12;

    /// <summary>
    /// 手术侧 CM3 主导词召回保留条数。单独成项、维持 8：手术通道的 D_CM3 万名台账（Top1 41.64%）
    /// 与 288 条部位词头门控都是按 8 席验收的，与诊断侧共用一个值等于顺手改动一条已验收通道。
    /// </summary>
    public int ProcedureLeadWordTopK { get; set; } = 8;

    /// <summary>索引展开后诊断字典的编码体系标识。</summary>
    public string IndexDiagnosisCodeSystem { get; set; } = "ICD-10";

    /// <summary>手术侧 CM3 索引两段式链路开关；关闭时手术输入维持今天的 Exact+BM25 通道。</summary>
    public bool Cm3IndexEnabled { get; set; } = true;

    /// <summary>索引展开后手术字典的编码体系标识。</summary>
    public string IndexProcedureCodeSystem { get; set; } = "ICD-9-CM-3";

    /// <summary>向量语义召回开关（WP-B3-1）。模型文件缺失或检索失败时显式降级 DEGRADED.NO_VECTOR。</summary>
    public bool VectorEnabled { get; set; } = true;

    /// <summary>每条诊断输入的向量召回条数。</summary>
    public int VectorTopK { get; set; } = 6;

    /// <summary>向量候选入池的余弦相似度下限（低于该值的语义近似不参与推荐）。</summary>
    public double VectorMinScore { get; set; } = 0.45;

    /// <summary>交叉编码 Rerank 开关（WP-B3-2）。模型缺失或打分失败时显式降级 DEGRADED.NO_RERANK。</summary>
    public bool RerankEnabled { get; set; } = true;

    /// <summary>每条输入进入 Rerank 的候选条数。</summary>
    public int RerankTopK { get; set; } = 8;

    /// <summary>是否启用规则引擎。默认启用；关闭时 RuleScore 视为缺省不参与。</summary>
    public bool RuleEngineEnabled { get; set; } = true;

    /// <summary>事实 / 解释生成使用的模型 code（业务只引用 code）。</summary>
    public string ExplanationModelCode { get; set; } = HospitalAi.Contracts.Models.ModelCodes.Small;

    /// <summary>LlmScore 校验用的解释 Prompt 版本。</summary>
    public string PromptVersion { get; set; } = "v22-explanation-v2";
}
