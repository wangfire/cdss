using System.Text.RegularExpressions;
using HospitalAi.Domain.CodingTasks;

namespace HospitalAi.Application.Coding.Preprocessing;

/// <summary>
/// 抽取出的临床事实。negation / certainty / temporality 与原文值、来源位置必须一起返回，
/// 禁止只回原文。明确否定的事实不得生成编码 Candidate。
/// </summary>
public sealed record ExtractedFact(
    ClinicalFactType FactType,
    string FactName,
    string? NormalizedValue,
    string OriginalValue,
    bool Negation,
    FactCertainty Certainty,
    FactTemporality Temporality,
    decimal Confidence,
    Guid? SourceDocumentId,
    Guid? SourceSectionId,
    int SourceStart,
    int SourceEnd,
    string ExtractorVersion);

/// <summary>
/// V2.2-Lite 规则式临床事实抽取器（确定性规则 + 结构化解析）。
/// 没有医疗小模型时：只执行确定性规则；无法判断的文本标记低置信度，
/// 不伪造事实确定性，最终进入 NEED_REVIEW。
/// </summary>
public static class ClinicalFactExtractor
{
    public const string ExtractorVersion = "rule-extractor-v1";

    private static readonly string[] NegationWords =
        ["无", "未", "否认", "不", "除外", "排除", "未见", "阴性", "(-)", "(-)"];

    private static readonly string[] SuspectedWords =
        ["疑", "可能", "考虑", "待查", "待排", "倾向", "疑似", "不排除", "约"];

    private static readonly string[] ConfirmedWords =
        ["确诊", "明确为", "诊断", "诊断为", "提示", "证实", "病理"];

    private static readonly string[] RuledOutWords =
        ["排除", "否定", "除外", "未见", "(-)", "阴性"];

    private static readonly string[] PastWords = ["既往", "既往史", "曾", "多年", "旧", "复发"];

    private static readonly string[] FamilyWords = ["家族", "遗传", "母", "父"];

    private static readonly Regex NarrativeDuration = new(
        @"\d+\s*(天|日|周|月|年|小时)",
        RegexOptions.Compiled);

    private static readonly string[] NarrativeMarkers =
        ["经过", "不规则", "好转", "缓解", "加重", "入院", "就诊", "顺利", "恢复"];

    // "伴" 只允许出现在以疾病后缀结尾的真实诊断名里（"腰椎间盘突出伴神经根病"），
    // 否则是症状并列叙述（"间断左腰腹疼痛伴血尿"）。
    private static readonly string[] DiseaseNameSuffixes =
        ["病", "炎", "征", "瘤", "症", "骨折", "梗死", "出血", "衰竭", "结石", "感染",
            "增生", "狭窄", "不全", "脱垂", "畸形", "破裂", "穿孔", "溃疡", "栓塞",
            "硬化", "气胸", "积液", "损伤", "综合征", "中毒", "休克", "疝"];

    private static readonly (ClinicalFactType Type, string[] Headings)[] SectionMappings =
        [
            (ClinicalFactType.Diagnosis, ["出院诊断", "诊断", "初步诊断", "入院诊断", "修正诊断"]),
            (ClinicalFactType.Symptom, ["主诉", "现病史", "症状"]),
            (ClinicalFactType.Sign, ["体格检查", "查体", "体征"]),
            (ClinicalFactType.Examination, ["检查", "影像", "B超", "CT", "MRI", "超声"]),
            (ClinicalFactType.Laboratory, ["检验", "化验", "实验室"]),
            (ClinicalFactType.Procedure, ["手术", "操作", "介入"]),
            (ClinicalFactType.Medication, ["用药", "处方", "医嘱用药", "药物"]),
            (ClinicalFactType.Treatment, ["治疗", "处理", "放疗", "化疗"])
        ];

    /// <summary>
    /// 文书 / 章节标签：只用于从行首剥离，不参与条目类型判断。
    /// 单行病历常以 "入院记录：诊断：…" 嵌套开头，只剥一层会把 "入院记录…" 粘进 FactName。
    /// </summary>
    private static readonly string[] DocumentLabels =
        ["入院记录", "出院记录", "出院小结", "入院小结", "病案首页", "门诊病历", "急诊病历"];

    /// <summary>
    /// 标点切分：顿号 / 分号用于拆分并列诊断，句末标点用于把同一 chunk 内的多个句子分开。
    /// 缺少句末切分时，前一句的结尾会粘到后一个诊断名上，之后与编码标题做精确比较永远不相等。
    /// 不按逗号（，）切分：ICD 风格的诊断名本身含逗号（"脑血管疾病，其他特指的"），
    /// 拆开会产生 "其他特指的" 这类无法编码的碎片；并列条目一律以顿号 / 分号分隔。
    /// </summary>
    private static readonly char[] FactSeparators =
        ['、', ';', '；', '。', '!', '！', '?', '？'];

    /// <summary>
    /// 手术条目常写成 "【手术日期】2026-08-11 【手术名称】胆囊切除术"：
    /// 前导日期数字与内嵌 "手术名称" 标签会粘进 FactName，导致精确匹配永远失败。
    /// </summary>
    private static readonly Regex ProcedureLeadingNoise = new(
        @"^(?:\d{2,8}|手术名称|手术日期|手术时间|术中操作|术后诊断|麻醉方式|手术经过)+",
        RegexOptions.Compiled);

    private static readonly HashSet<string> SectionHeadings = SectionMappings
        .SelectMany(item => item.Headings)
        .ToHashSet(StringComparer.Ordinal);

    private static readonly Regex LeadingEnumeration = new(
        @"^\s*(?:[（(]?\d+[)）.、：:]|[①②③④⑤⑥⑦⑧⑨⑩])\s*",
        RegexOptions.Compiled);

    /// <summary>
    /// 医生常把院内编码直接写在诊断 / 手术名后面的括号里，如 "针刺（99.9200）"。
    /// 括号内容必须是"可选字母 + 数字开头 + 仅数字/字母/点"的编码形态才剥，
    /// 避免误删 "高血压（3级）" 这类有临床含义的限定语。
    /// </summary>
    private static readonly Regex TrailingCodeParenthesis = new(
        @"[\(（]\s*[A-Za-z]?\d[\d.A-Za-z]*\s*[\)）]\s*$",
        RegexOptions.Compiled);

    public static IReadOnlyList<ExtractedFact> Extract(
        IReadOnlyList<TextChunk> chunks,
        Guid? sourceDocumentId,
        CancellationToken cancellationToken = default)
    {
        var facts = new List<ExtractedFact>();
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var factType = ResolveFactType(chunk.Text);
            foreach (var item in ExtractFromChunk(factType, chunk, sourceDocumentId))
            {
                facts.Add(item);
            }
        }

        return facts;
    }

    private static ClinicalFactType ResolveFactType(string chunkText)
    {
        foreach (var (type, headings) in SectionMappings)
        {
            if (headings.Any(heading => chunkText.Contains(heading, StringComparison.Ordinal)))
            {
                return type;
            }
        }

        return ClinicalFactType.Examination;
    }

    /// <summary>
    /// 剥掉条目连续的"标签："前缀（文书标签 + 段落标题 + 条目编号），并回报最后一个命中的段落类型。
    /// 单行病历里 "入院记录：诊断：…" 是嵌套开头，只剥一层会把 "入院记录" 粘进 FactName；
    /// 段落类型要回报给调用方，后续无标签的条目（如 "；针刺（99.9200）"）沿用同一段落的类型。
    /// </summary>
    private static (ClinicalFactType? SectionType, string Content) SplitLeadingLabels(string text)
    {
        var value = text.Trim();
        ClinicalFactType? sectionType = null;

        while (true)
        {
            if (value.StartsWith('【'))
            {
                var end = value.IndexOf('】', StringComparison.Ordinal);
                if (end <= 0)
                {
                    break;
                }

                if (TryMatchSection(value[1..end], out var bracketType))
                {
                    sectionType = bracketType;
                }

                value = value[(end + 1)..].TrimStart();
                continue;
            }

            var colon = value.IndexOfAny(['：', ':']);
            if (colon <= 0)
            {
                break;
            }

            var prefix = value[..colon].Trim();
            if (prefix.Length == 0 || prefix.Length > 12)
            {
                break;
            }

            var isSection = TryMatchSection(prefix, out var prefixType);
            if (!isSection && !IsDocumentLabel(prefix))
            {
                break;
            }

            if (isSection)
            {
                sectionType = prefixType;
            }

            value = value[(colon + 1)..].TrimStart();
        }

        value = LeadingEnumeration.Replace(value, string.Empty).TrimStart();

        // 只剩标题本身时视为无内容。
        if (value.Length == 0 || SectionHeadings.Contains(value) || DocumentLabels.Contains(value))
        {
            return (sectionType, string.Empty);
        }

        return (sectionType, value);
    }

    private static bool TryMatchSection(string prefix, out ClinicalFactType factType)
    {
        foreach (var (type, headings) in SectionMappings)
        {
            if (headings.Any(heading => prefix.Contains(heading, StringComparison.Ordinal)))
            {
                factType = type;
                return true;
            }
        }

        factType = default;
        return false;
    }

    private static bool IsDocumentLabel(string prefix)
    {
        return DocumentLabels.Any(label => prefix.Contains(label, StringComparison.Ordinal));
    }

    private static IEnumerable<ExtractedFact> ExtractFromChunk(
        ClinicalFactType factType,
        TextChunk chunk,
        Guid? sourceDocumentId)
    {
        var lines = chunk.Text.Split('\n');
        var lineStart = 0;
        foreach (var line in lines)
        {
            // 游标顺序推进：同名条目在入院 / 出院段各出现一次时，
            // 从头 IndexOf 会把出院条目的位置错记成入院段的首次出现，证据段落隔离即失效。
            var cursor = lineStart;
            lineStart += line.Length + 1;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // 结构化诊断行按顿号 / 分号 / 句末标点拆分成多条事实。
            var parts = line
                .Split(FactSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToList();

            if (parts.Count == 0)
            {
                parts.Add(line.Trim());
            }

            // 段落类型按行内推进：条目自带的 "标签：" 会切换段落，
            // 无标签的后续条目（"；针刺（99.9200）"）沿用当前段落，否则手术条目会被误归为诊断。
            var sectionType = factType;
            foreach (var part in parts)
            {
                var (prefixType, content) = SplitLeadingLabels(part);
                if (prefixType.HasValue)
                {
                    sectionType = prefixType.Value;
                }

                if (content.Length == 0)
                {
                    continue;
                }

                var name = CleanFactName(TrailingCodeParenthesis.Replace(content, string.Empty).Trim());
                if (sectionType == ClinicalFactType.Procedure)
                {
                    name = ProcedureLeadingNoise.Replace(name, string.Empty);
                }

                if (name.Length == 0)
                {
                    continue;
                }

                if (sectionType == ClinicalFactType.Diagnosis && !LooksLikeDiagnosis(content))
                {
                    continue;
                }

                // 真实疾病名不会包含"诊断"二字，出现即为叙述性病历（"行胃镜检查明确诊断"），
                // 不能作为诊断条目送检索，否则产生语义噪声推荐。
                if (sectionType == ClinicalFactType.Diagnosis && name.Contains("诊断", StringComparison.Ordinal))
                {
                    continue;
                }

                // 主诉 / 现病史叙述句（"发热伴手足臀部皮疹3天"、"术后恢复可"）不是可编码事实。
                if (sectionType is ClinicalFactType.Diagnosis or ClinicalFactType.Procedure
                    && IsNarrativeSentence(name))
                {
                    continue;
                }

                var negation = ContainsAny(part, NegationWords);
                var certainty = DetermineCertainty(part, negation);
                var temporality = DetermineTemporality(part);
                var found = chunk.Text.IndexOf(part, cursor, StringComparison.Ordinal);
                if (found < 0)
                {
                    found = chunk.Text.IndexOf(part, StringComparison.Ordinal);
                }
                else
                {
                    cursor = found + part.Length;
                }

                var sentenceStart = chunk.StartPosition + found;
                yield return new ExtractedFact(
                    sectionType,
                    name,
                    name,
                    part,
                    negation,
                    certainty,
                    temporality,
                    DetermineConfidence(certainty, temporality, negation),
                    sourceDocumentId,
                    chunk.SectionId,
                    Math.Max(sentenceStart, chunk.StartPosition),
                    Math.Min(sentenceStart + part.Length, chunk.EndPosition),
                    ExtractorVersion);
            }
        }
    }

    private static string CleanFactName(string value)
    {
        return new string(value
            .Where(character => !char.IsPunctuation(character) && character != ' ')
            .ToArray());
    }

    private static bool LooksLikeDiagnosis(string text)
    {
        // 结构化诊断行通常包含"诊断："等前缀，或有编号 / 疾病特征词。
        // 长度下限取 2："咯血"、"便秘"、"腹泻" 这类两字诊断真实可编码，
        // 被 3 字门槛挡掉会让出院推荐拿不到出院段证据而回退挂到入院证据上。
        return text.Contains("诊断", StringComparison.Ordinal)
            || text.Contains("出院", StringComparison.Ordinal)
            || (text.Length >= 2 && text.Length <= 60
                && text.Any(character => char.IsLetterOrDigit(character) && character > '\u4e00'));
    }

    private static bool IsNarrativeSentence(string name)
    {
        if (NarrativeDuration.IsMatch(name))
        {
            return true;
        }

        if (ContainsAny(name, NarrativeMarkers))
        {
            return true;
        }

        return name.Contains('伴')
            && !DiseaseNameSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static FactCertainty DetermineCertainty(string text, bool negation)
    {
        if (negation && ContainsAny(text, RuledOutWords))
        {
            return FactCertainty.RuledOut;
        }

        if (negation)
        {
            return FactCertainty.RuledOut;
        }

        if (ContainsAny(text, SuspectedWords))
        {
            return FactCertainty.Suspected;
        }

        if (ContainsAny(text, ConfirmedWords))
        {
            return FactCertainty.Confirmed;
        }

        return FactCertainty.Unknown;
    }

    private static FactTemporality DetermineTemporality(string text)
    {
        if (ContainsAny(text, FamilyWords))
        {
            return FactTemporality.Family;
        }

        if (ContainsAny(text, PastWords))
        {
            return FactTemporality.Past;
        }

        return FactTemporality.Unknown;
    }

    private static decimal DetermineConfidence(
        FactCertainty certainty,
        FactTemporality temporality,
        bool negation)
    {
        if (certainty == FactCertainty.Confirmed && !negation)
        {
            return temporality == FactTemporality.Current ? 0.95m : 0.85m;
        }

        if (certainty == FactCertainty.Suspected)
        {
            return 0.45m;
        }

        if (certainty == FactCertainty.RuledOut)
        {
            return 0.8m;
        }

        // 无法判断的文本只标记低置信度，不伪造确定性。
        return 0.3m;
    }

    private static bool ContainsAny(string text, string[] words)
    {
        return words.Any(word => text.Contains(word, StringComparison.Ordinal));
    }
}
