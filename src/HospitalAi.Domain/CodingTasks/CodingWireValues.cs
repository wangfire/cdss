namespace HospitalAi.Domain.CodingTasks;

/// <summary>
/// V2.2-Lite 领域枚举的对外协议值映射。API 只暴露稳定的字符串，不暴露枚举序号。
/// </summary>
public static class CodingWireValues
{
    public static string ToWireValue(this CodingStage stage)
    {
        return stage switch
        {
            CodingStage.Processing => "PROCESSING",
            CodingStage.Ready => "READY",
            CodingStage.AiRecommending => "AI_RECOMMENDING",
            CodingStage.CoderReview => "CODER_REVIEW",
            CodingStage.FinalPending => "FINAL_PENDING",
            CodingStage.Finalized => "FINALIZED",
            CodingStage.HumanRequired => "HUMAN_REQUIRED",
            _ => "IMPORTED"
        };
    }

    public static string ToWireValue(this ClinicalFactType factType)
    {
        return factType switch
        {
            ClinicalFactType.Symptom => "SYMPTOM",
            ClinicalFactType.Sign => "SIGN",
            ClinicalFactType.Examination => "EXAMINATION",
            ClinicalFactType.Laboratory => "LABORATORY",
            ClinicalFactType.Procedure => "PROCEDURE",
            ClinicalFactType.Medication => "MEDICATION",
            ClinicalFactType.Treatment => "TREATMENT",
            _ => "DIAGNOSIS"
        };
    }

    public static string ToWireValue(this FactCertainty certainty)
    {
        return certainty switch
        {
            FactCertainty.Confirmed => "CONFIRMED",
            FactCertainty.Suspected => "SUSPECTED",
            FactCertainty.RuledOut => "RULED_OUT",
            _ => "UNKNOWN"
        };
    }

    public static string ToWireValue(this FactTemporality temporality)
    {
        return temporality switch
        {
            FactTemporality.Current => "CURRENT",
            FactTemporality.Past => "PAST",
            FactTemporality.Family => "FAMILY",
            _ => "UNKNOWN"
        };
    }

    public static string ToWireValue(this QualityIssueType issueType)
    {
        return issueType switch
        {
            QualityIssueType.RuleViolation => "RULE_VIOLATION",
            QualityIssueType.GranularityInsufficient => "GRANULARITY_INSUFFICIENT",
            QualityIssueType.LlmOutputInvalid => "LLM_OUTPUT_INVALID",
            _ => "EVIDENCE_INSUFFICIENT"
        };
    }
}
