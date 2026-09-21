namespace HospitalAi.Domain.CodingTasks;

/// <summary>
/// V2.2 推荐结果结论。模型只能产出候选与解释，最终结论由策略与人工审核决定。
/// </summary>
public enum RecommendationOutcome
{
    Unknown = 0,
    HighConfidence = 1,
    NeedReview = 2,
    NoSafeRecommendation = 3,
    HumanRequired = 4,
    Legacy = 5
}

/// <summary>
/// V2.2 推荐结论的对外协议值映射。
/// </summary>
public static class RecommendationOutcomeExtensions
{
    public static string ToWireValue(this RecommendationOutcome outcome)
    {
        return outcome switch
        {
            RecommendationOutcome.HighConfidence => "HIGH_CONFIDENCE",
            RecommendationOutcome.NeedReview => "NEED_REVIEW",
            RecommendationOutcome.NoSafeRecommendation => "NO_SAFE_RECOMMENDATION",
            RecommendationOutcome.HumanRequired => "HUMAN_REQUIRED",
            RecommendationOutcome.Legacy => "LEGACY",
            _ => "UNKNOWN"
        };
    }

    public static bool TryParseWireValue(string? value, out RecommendationOutcome outcome)
    {
        outcome = value?.Trim().ToUpperInvariant() switch
        {
            "HIGH_CONFIDENCE" => RecommendationOutcome.HighConfidence,
            "NEED_REVIEW" => RecommendationOutcome.NeedReview,
            "NO_SAFE_RECOMMENDATION" => RecommendationOutcome.NoSafeRecommendation,
            "HUMAN_REQUIRED" => RecommendationOutcome.HumanRequired,
            "LEGACY" => RecommendationOutcome.Legacy,
            _ => RecommendationOutcome.Unknown
        };
        return outcome != RecommendationOutcome.Unknown;
    }
}
