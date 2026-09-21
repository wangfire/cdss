namespace HospitalAi.Domain.CodingTasks;

/// <summary>
/// V2.2-Lite 支持的 Clinical Fact 类型。Fact 由规则 / 结构化解析产生，
/// 未接入医疗小模型前只执行确定性规则，不伪造确定性。
/// </summary>
public enum ClinicalFactType
{
    Diagnosis = 0,
    Symptom = 1,
    Sign = 2,
    Examination = 3,
    Laboratory = 4,
    Procedure = 5,
    Medication = 6,
    Treatment = 7
}

/// <summary>
/// Fact 的确定性等级。用于区分"明确记载"与"考虑 / 可能"等推测性描述。
/// </summary>
public enum FactCertainty
{
    Unknown = 0,
    Confirmed = 1,
    Suspected = 2,
    RuledOut = 3
}

/// <summary>
/// Fact 的时间性。既往史与家族史不得当作本次就诊的诊断依据。
/// </summary>
public enum FactTemporality
{
    Unknown = 0,
    Current = 1,
    Past = 2,
    Family = 3
}

/// <summary>
/// V2.2-Lite 证据等级与初始权重：正式诊断最强，推测性上下文最弱。
/// </summary>
public enum EvidenceLevel
{
    E = 0,
    D = 1,
    C = 2,
    B = 3,
    A = 4
}

/// <summary>
/// V2.2-Lite 支持的结构化质量问题类型。
/// </summary>
public enum QualityIssueType
{
    EvidenceInsufficient = 0,
    RuleViolation = 1,
    GranularityInsufficient = 2,
    /// 模型输出经 Schema 重试校验仍不合法。评分必须按未校验口径处理，不得采信该次输出。
    LlmOutputInvalid = 3
}
