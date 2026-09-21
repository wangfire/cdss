namespace HospitalAi.Application.Coding.Scoring;

/// <summary>
/// Policy 判定输入。degradedFlags 例如 DEGRADED.NO_MODEL / DEGRADED.NO_VECTOR。
/// </summary>
public sealed record PolicyInput(
    bool HasEvidence,
    bool RuleBlocked,
    bool GranularityInsufficient,
    bool ModelAvailable,
    bool ModelOutputValidated,
    bool UnresolvedConflict,
    bool QualityGateFailed,
    IReadOnlyList<string> DegradedFlags);

/// <summary>
/// Policy 判定结果。结果集合：
/// HIGH_CONFIDENCE / NEED_REVIEW / NO_SAFE_RECOMMENDATION / HUMAN_REQUIRED。
/// </summary>
public sealed record PolicyResult(
    string Outcome,
    string Reason,
    string RiskLevel,
    decimal EvidenceSufficiency);

/// <summary>
/// V2.2-Lite 推荐 Policy。
/// HIGH_CONFIDENCE 必须同时满足：
/// FinalScore >= 0.85、EvidenceScore >= 0.75、Margin >= 0.10、Rule = PASS、
/// 存在有效 Evidence、模型可用且输出通过校验。
/// 模型不可用、无 Evidence、Rule 阻断、未解决冲突、粒度不足、
/// 使用 DEGRADED.NO_MODEL、使用 DEGRADED.NO_VECTOR 且证据不足时禁止高置信。
/// </summary>
public static class RecommendationPolicy
{
    public static PolicyResult Evaluate(
        ScoringOptions options,
        PolicyInput input,
        decimal finalScore,
        decimal evidenceScore,
        decimal margin,
        bool rulePass)
    {
        var evidenceSufficiency = Math.Round(
            input.HasEvidence ? Math.Max(0m, Math.Min(1m, evidenceScore)) : 0m,
            4);

        if (input.QualityGateFailed)
        {
            return new PolicyResult(
                "HUMAN_REQUIRED", "数据质量门禁未通过，需人工处理文书。", "HIGH", evidenceSufficiency);
        }

        if (!input.HasEvidence || input.RuleBlocked || evidenceSufficiency <= 0)
        {
            return new PolicyResult(
                "NO_SAFE_RECOMMENDATION", "缺少有效证据或命中阻断规则，不给出安全推荐。", "HIGH", evidenceSufficiency);
        }

        if (input.UnresolvedConflict)
        {
            return new PolicyResult(
                "HUMAN_REQUIRED", "存在未解决的编码冲突，需人工裁定。", "HIGH", evidenceSufficiency);
        }

        if (input.GranularityInsufficient)
        {
            return new PolicyResult(
                "NEED_REVIEW", "候选粒度不足（缺少最终编码位或解剖部位），需人工确认。", "MEDIUM", evidenceSufficiency);
        }

        var degradedNoModel = input.DegradedFlags.Contains("DEGRADED.NO_MODEL", StringComparer.Ordinal);
        var degradedNoVector = input.DegradedFlags.Contains("DEGRADED.NO_VECTOR", StringComparer.Ordinal);
        if (degradedNoModel || !input.ModelAvailable || !input.ModelOutputValidated)
        {
            return new PolicyResult(
                "NEED_REVIEW", "模型不可用或输出未通过校验，降级为人工复核。", "MEDIUM", evidenceSufficiency);
        }

        if (degradedNoVector && evidenceSufficiency < options.HighConfidenceEvidenceScore)
        {
            return new PolicyResult(
                "NEED_REVIEW", "无向量召回且证据不足，降级为人工复核。", "MEDIUM", evidenceSufficiency);
        }

        var highConfidence = finalScore >= options.HighConfidenceFinalScore
            && evidenceSufficiency >= options.HighConfidenceEvidenceScore
            && margin >= options.MinimumMargin
            && rulePass;

        return highConfidence
            ? new PolicyResult("HIGH_CONFIDENCE", "全部高置信条件满足。", "LOW", evidenceSufficiency)
            : new PolicyResult(
                "NEED_REVIEW",
                "未满足高置信条件（FinalScore / EvidenceScore / Margin / Rule）。",
                "MEDIUM",
                evidenceSufficiency);
    }
}
