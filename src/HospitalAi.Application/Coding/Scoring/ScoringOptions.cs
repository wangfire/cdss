namespace HospitalAi.Application.Coding.Scoring;

/// <summary>
/// 评分配置。七维初始权重：
/// Exact 0.15 / Semantic 0.15 / Retrieval 0.10 / Rerank 0.10 /
/// Rule 0.15 / Evidence 0.20 / LLM 0.15。
/// 权重必须配置化，不得写死在业务代码中。
/// </summary>
public sealed class ScoringOptions
{
    public const string SectionName = "Coding:Scoring";

    public const int DimensionCount = 7;

    public decimal ExactWeight { get; set; } = 0.15m;

    public decimal SemanticWeight { get; set; } = 0.15m;

    public decimal RetrievalWeight { get; set; } = 0.10m;

    public decimal RerankWeight { get; set; } = 0.10m;

    public decimal RuleWeight { get; set; } = 0.15m;

    public decimal EvidenceWeight { get; set; } = 0.20m;

    public decimal LlmWeight { get; set; } = 0.15m;

    /// <summary>HIGH_CONFIDENCE 的 FinalScore 下限。</summary>
    public decimal HighConfidenceFinalScore { get; set; } = 0.85m;

    /// <summary>HIGH_CONFIDENCE 的 EvidenceScore 下限。</summary>
    public decimal HighConfidenceEvidenceScore { get; set; } = 0.75m;

    /// <summary>HIGH_CONFIDENCE 的候选 Margin 下限。</summary>
    public decimal MinimumMargin { get; set; } = 0.10m;

    public IDictionary<string, decimal> GetWeights()
    {
        return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["exact"] = ExactWeight,
            ["semantic"] = SemanticWeight,
            ["retrieval"] = RetrievalWeight,
            ["rerank"] = RerankWeight,
            ["rule"] = RuleWeight,
            ["evidence"] = EvidenceWeight,
            ["llm"] = LlmWeight
        };
    }
}

/// <summary>
/// 单条候选的七维输入。缺失维度必须是 null 而不是默认值，
/// 评分按实际参与维度重新归一化。
/// </summary>
public sealed record CandidateScoreInput(
    decimal? ExactScore,
    decimal? SemanticScore,
    decimal? RetrievalScore,
    decimal? RerankScore,
    decimal RuleScore,
    decimal EvidenceScore,
    decimal? LlmScore);

/// <summary>
/// 评分输出：FinalScore、参与的维度集合与评分口径。
/// </summary>
public sealed record ScoreComputation(
    decimal FinalScore,
    IReadOnlyList<string> ParticipatingDimensions,
    string ScoreProfile);

/// <summary>
/// 七维评分器。缺失维度不填默认值，按实际参与维度重新归一化；
/// score_profile 记录参与的口径，跨口径分数不横向比较。
/// </summary>
public static class SevenDimensionScorer
{
    public static ScoreComputation Compute(
        ScoringOptions options,
        CandidateScoreInput input)
    {
        var weights = options.GetWeights();
        var values = new List<(string Dimension, decimal Score, decimal Weight)>();
        AddDimension(values, "exact", input.ExactScore, weights);
        AddDimension(values, "semantic", input.SemanticScore, weights);
        AddDimension(values, "retrieval", input.RetrievalScore, weights);
        AddDimension(values, "rerank", input.RerankScore, weights);
        AddDimension(values, "rule", input.RuleScore, weights);
        AddDimension(values, "evidence", input.EvidenceScore, weights);
        AddDimension(values, "llm", input.LlmScore, weights);

        if (values.Count == 0)
        {
            return new ScoreComputation(0m, [], "EMPTY");
        }

        var totalWeight = values.Sum(item => item.Weight);
        if (totalWeight <= 0)
        {
            return new ScoreComputation(0m, [], "EMPTY");
        }

        var weighted = values.Sum(item => item.Weight * item.Score);
        var final = Math.Round(weighted / totalWeight, 4);
        var dimensions = values.Select(item => item.Dimension).ToList();
        return new ScoreComputation(
            final,
            dimensions,
            string.Join('+', dimensions));
    }

    private static void AddDimension(
        List<(string Dimension, decimal Score, decimal Weight)> values,
        string dimension,
        decimal? score,
        IDictionary<string, decimal> weights)
    {
        if (score is null || !weights.TryGetValue(dimension, out var weight) || weight <= 0)
        {
            return;
        }

        values.Add((dimension, Math.Max(0m, Math.Min(1m, score.Value)), weight));
    }
}

/// <summary>
/// LlmScore 一致性校验输入。LlmScore 不使用模型自报置信度，
/// 必须由可判定的一致性校验构成。
/// </summary>
public sealed record LlmConsistencyInput(
    bool ModelAvailable,
    bool OutputValidated,
    bool EvidenceReferencesValid,
    bool CandidateExistsInRetrieval,
    bool ExplanationSupportedByEvidence,
    bool NegationOrTemporalViolation);

/// <summary>
/// LlmScore 计算口径：
/// Evidence 引用有效性 40% / 候选存在于检索结果 20% /
/// 解释可被证据支持 20% / 不违反否定-时间关系 20%。
/// 模型不可用或输出未通过校验时 LlmScore 为空，不伪造。
/// </summary>
public static class LlmConsistencyScorer
{
    public static decimal? Compute(LlmConsistencyInput input)
    {
        if (!input.ModelAvailable || !input.OutputValidated)
        {
            return null;
        }

        var score = 0m;
        score += input.EvidenceReferencesValid ? 0.40m : 0m;
        score += input.CandidateExistsInRetrieval ? 0.20m : 0m;
        score += input.ExplanationSupportedByEvidence ? 0.20m : 0m;
        score += input.NegationOrTemporalViolation ? 0m : 0.20m;
        return Math.Round(Math.Max(0m, Math.Min(1m, score)), 4);
    }
}
