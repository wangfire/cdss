using HospitalAi.Application.Coding.Matching;
using HospitalAi.Domain.CodingTasks;

namespace HospitalAi.Application.Coding.Scoring;

/// <summary>
/// 模型自述的一致性判定原始字段（已通过 Schema 解析，字段语义由模型自己填写）。
/// </summary>
public sealed record ModelConsistencyJudgement(
    string? EchoedCode,
    bool Supported,
    IReadOnlyList<int> CitedEvidenceIndexes,
    bool ReportsNegationOrTemporalViolation);

/// <summary>
/// 一致性校验的流水线侧上下文：候选码、本输入检索池全部编码、按引用编号排序的证据原文、
/// 结构化事实的否定/确定性/时间性。证据原文列表为 1 基引用（下标 0 对应编号 1）。
/// </summary>
public sealed record LlmConsistencyContext(
    string CandidateCode,
    string CandidateTitle,
    IReadOnlyList<string> RetrievalCodes,
    IReadOnlyList<string> EvidenceTexts,
    bool FactNegation,
    FactCertainty FactCertainty,
    FactTemporality FactTemporality);

/// <summary>
/// 把模型自述换算成四判据：模型只有"断言权"，采信与否一律由流水线用检索池与证据原文复核。
/// 引用编号越界、回显码与所评候选不符或不在候选池、被引证据并未逐字出现该标题，
/// 都要相应扣分——否则 LlmScore 会退化成与内容无关的常数（实测曾恒为 0.8 却能改写 Top3）。
/// 否定-时间违规以病历结构化事实为准：事实已判为否定/排除/既往时模型自述无法翻案。
/// </summary>
public static class LlmConsistencyJudge
{
    public static LlmConsistencyInput Judge(
        ModelConsistencyJudgement judgement,
        LlmConsistencyContext context)
    {
        var cited = judgement.CitedEvidenceIndexes
            .Where(index => index >= 1 && index <= context.EvidenceTexts.Count)
            .Distinct()
            .ToList();
        var evidenceReferencesValid = cited.Count > 0
            && cited.Any(index => TextMatch.EvidenceSupportsTitle(
                context.EvidenceTexts[index - 1],
                context.CandidateTitle));

        // 回显码必须等于正在评估的候选，且该码确在本输入的检索池里：
        // 模型答非所问（或凭空给出池外编码）时这一维不得分。
        var echoed = NormalizeCode(judgement.EchoedCode);
        var candidateExistsInRetrieval = echoed.Length > 0
            && string.Equals(echoed, NormalizeCode(context.CandidateCode), StringComparison.Ordinal)
            && context.RetrievalCodes.Any(code => NormalizeCode(code) == echoed);

        return new LlmConsistencyInput(
            ModelAvailable: true,
            OutputValidated: true,
            EvidenceReferencesValid: evidenceReferencesValid,
            CandidateExistsInRetrieval: candidateExistsInRetrieval,
            ExplanationSupportedByEvidence: judgement.Supported,
            NegationOrTemporalViolation: context.FactNegation
                || context.FactCertainty == FactCertainty.RuledOut
                || context.FactTemporality is FactTemporality.Past or FactTemporality.Family
                || judgement.ReportsNegationOrTemporalViolation);
    }

    /// <summary>编码比对口径：去空白、† 归一为 +、统一大写（与推荐命中判定同一口径）。</summary>
    private static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(code.Length);
        foreach (var character in code)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character == '†' ? '+' : char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }
}
