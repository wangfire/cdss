using HospitalAi.Application.Coding.Scoring;
using HospitalAi.Domain.CodingTasks;

namespace HospitalAi.Application.Tests;

/// <summary>
/// LlmScore 判据换算单测：模型只有断言权，引用编号、回显编码、否定/时间性
/// 都要被流水线用证据原文与检索池复核后才计分（历史上这两条判据是硬编码常数）。
/// </summary>
public sealed class LlmConsistencyJudgeTests
{
    private const string Code = "S82.142A";
    private const string Title = "左胫骨平台粉碎性骨折";

    private static LlmConsistencyContext Context(
        bool negation = false,
        FactCertainty certainty = FactCertainty.Confirmed,
        FactTemporality temporality = FactTemporality.Current,
        IReadOnlyList<string>? evidenceTexts = null,
        IReadOnlyList<string>? retrievalCodes = null) =>
        new(
            CandidateCode: Code,
            CandidateTitle: Title,
            RetrievalCodes: retrievalCodes ?? [Code, "S82.200"],
            EvidenceTexts: evidenceTexts ??
            [
                $"【出院诊断】{Title}。",
                "【入院记录】患者自述无糖尿病病史。"
            ],
            FactNegation: negation,
            FactCertainty: certainty,
            FactTemporality: temporality);

    private static ModelConsistencyJudgement Judgement(
        string? echoedCode = Code,
        bool supported = true,
        IReadOnlyList<int>? cited = null,
        bool reportsViolation = false) =>
        new(
            EchoedCode: echoedCode,
            Supported: supported,
            CitedEvidenceIndexes: cited ?? [1],
            ReportsNegationOrTemporalViolation: reportsViolation);

    [Fact]
    public void 引用真实证据且回显池内候选码_四判据全过()
    {
        var input = LlmConsistencyJudge.Judge(Judgement(), Context());

        Assert.True(input.ModelAvailable);
        Assert.True(input.OutputValidated);
        Assert.True(input.EvidenceReferencesValid);
        Assert.True(input.CandidateExistsInRetrieval);
        Assert.True(input.ExplanationSupportedByEvidence);
        Assert.False(input.NegationOrTemporalViolation);
        Assert.Equal(1.0000m, LlmConsistencyScorer.Compute(input));
    }

    [Fact]
    public void 引用编号越界_证据引用维不得分()
    {
        var input = LlmConsistencyJudge.Judge(Judgement(cited: [7]), Context());

        Assert.False(input.EvidenceReferencesValid);
        Assert.Equal(0.6000m, LlmConsistencyScorer.Compute(input));
    }

    [Fact]
    public void 未引用任何证据_证据引用维不得分()
    {
        var input = LlmConsistencyJudge.Judge(Judgement(cited: []), Context());

        Assert.False(input.EvidenceReferencesValid);
    }

    [Fact]
    public void 被引证据未逐字出现该标题_不算有效引用()
    {
        // 编号 2 的证据写的是"无糖尿病病史"，与本候选标题无关。
        var input = LlmConsistencyJudge.Judge(Judgement(cited: [2]), Context());

        Assert.False(input.EvidenceReferencesValid);
    }

    [Fact]
    public void 回显码不在检索池_候选存在维不得分()
    {
        var input = LlmConsistencyJudge.Judge(
            Judgement(echoedCode: "Z99.900"),
            Context());

        Assert.False(input.CandidateExistsInRetrieval);
    }

    [Fact]
    public void 回显码在池内但不是当前候选_不得分()
    {
        // 模型答非所问：池内另一条编码不能替正在评估的候选背书。
        var input = LlmConsistencyJudge.Judge(
            Judgement(echoedCode: "S82.200"),
            Context());

        Assert.False(input.CandidateExistsInRetrieval);
    }

    [Fact]
    public void 回显码带dagger与空白_按统一口径归一后比对()
    {
        // 索引编码常写成 "G30.1† F00.1*"，与字典侧 "G30.1+ F00.1*" 必须视为同一条。
        const string crossCode = "G30.1† F00.1*";
        var input = LlmConsistencyJudge.Judge(
            Judgement(echoedCode: " G30.1+ F00.1* "),
            new LlmConsistencyContext(
                CandidateCode: crossCode,
                CandidateTitle: "阿尔茨海默病性痴呆(老年型)",
                RetrievalCodes: [crossCode],
                EvidenceTexts: ["阿尔茨海默病引起痴呆(老年型)"],
                FactNegation: false,
                FactCertainty: FactCertainty.Confirmed,
                FactTemporality: FactTemporality.Current));

        Assert.True(input.CandidateExistsInRetrieval);
    }

    [Fact]
    public void 结构化事实已判否定_模型自述不得翻案()
    {
        var input = LlmConsistencyJudge.Judge(Judgement(), Context(negation: true));

        Assert.True(input.NegationOrTemporalViolation);
        Assert.Equal(0.8000m, LlmConsistencyScorer.Compute(input));
    }

    [Fact]
    public void 确定性为已排除或时间性为既往_同样按违规处理()
    {
        Assert.True(LlmConsistencyJudge
            .Judge(Judgement(), Context(certainty: FactCertainty.RuledOut))
            .NegationOrTemporalViolation);
        Assert.True(LlmConsistencyJudge
            .Judge(Judgement(), Context(temporality: FactTemporality.Past))
            .NegationOrTemporalViolation);
        Assert.True(LlmConsistencyJudge
            .Judge(Judgement(), Context(temporality: FactTemporality.Family))
            .NegationOrTemporalViolation);
    }

    [Fact]
    public void 模型自报违规_即使事实未标记也计入违规()
    {
        var input = LlmConsistencyJudge.Judge(
            Judgement(reportsViolation: true),
            Context());

        Assert.True(input.NegationOrTemporalViolation);
    }

    [Fact]
    public void 模型判为不被支持_只扣解释维()
    {
        var input = LlmConsistencyJudge.Judge(Judgement(supported: false), Context());

        Assert.False(input.ExplanationSupportedByEvidence);
        Assert.Equal(0.8000m, LlmConsistencyScorer.Compute(input));
    }
}
