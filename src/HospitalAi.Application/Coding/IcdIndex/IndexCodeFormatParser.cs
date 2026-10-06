namespace HospitalAi.Application.Coding.IcdIndex;

public enum IndexCodeFormatKind
{
    Empty,
    Single,
    DaggerPair,
    Morphology,
    Alternatives,
    Conditional,
    CategoryUnspecified,
    CodeRange,
}

/// <summary>
/// 索引编码列值解析结果（《ICD索引编码列值格式全解析》7 类，含嵌套）。
/// 判定优先级：@ → ; → : → †/* → .- → X-Y → 单值。
/// </summary>
public sealed class ParsedIndexCode
{
    public required IndexCodeFormatKind Kind { get; init; }

    public required string Raw { get; init; }

    /// <summary>Single / CategoryUnspecified / DaggerPair / CodeRange 展开用的类目前缀。</summary>
    public string? Prefix { get; init; }

    /// <summary>DaggerPair：病因码（†前）与表现码（*前）。</summary>
    public string? DaggerCode { get; init; }

    public string? AsteriskCode { get; init; }

    /// <summary>Morphology：M 形态码与 C 部位码。</summary>
    public string? MorphologyCode { get; init; }

    public string? SiteCode { get; init; }

    /// <summary>Alternatives：分号分隔的候选编码组（元素可再含 †*）。</summary>
    public IReadOnlyList<ParsedIndexCode>? Alternatives { get; init; }

    /// <summary>Conditional：@ 前触发码（可再为范围/替代组）与 @ 后附加码。</summary>
    public ParsedIndexCode? Trigger { get; init; }

    public ParsedIndexCode? AddOn { get; init; }

    /// <summary>CodeRange：起止类目（含），如 D65~D69。</summary>
    public string? RangeStart { get; init; }

    public string? RangeEnd { get; init; }

    public static ParsedIndexCode Parse(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new ParsedIndexCode { Kind = IndexCodeFormatKind.Empty, Raw = text };
        }

        var atIndex = text.IndexOf('@');
        if (atIndex > 0)
        {
            return new ParsedIndexCode
            {
                Kind = IndexCodeFormatKind.Conditional,
                Raw = text,
                Trigger = Parse(text[..atIndex]),
                AddOn = Parse(text[(atIndex + 1)..]),
            };
        }

        var parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 1)
        {
            return new ParsedIndexCode
            {
                Kind = IndexCodeFormatKind.Alternatives,
                Raw = text,
                Alternatives = parts.Select(Parse).ToList(),
            };
        }

        var colonIndex = text.IndexOf(':');
        if (colonIndex > 0)
        {
            return new ParsedIndexCode
            {
                Kind = IndexCodeFormatKind.Morphology,
                Raw = text,
                MorphologyCode = text[..colonIndex].Trim(),
                SiteCode = text[(colonIndex + 1)..].Trim(),
                Prefix = text[(colonIndex + 1)..].Trim().TrimEnd('*'),
            };
        }

        var daggerIndex = text.IndexOf('†');
        if (daggerIndex >= 0)
        {
            var dagger = text[..daggerIndex].Trim();
            var rest = text[(daggerIndex + 1)..].Trim();
            var asterisk = rest.TrimEnd('*').Trim();
            return new ParsedIndexCode
            {
                Kind = IndexCodeFormatKind.DaggerPair,
                Raw = text,
                DaggerCode = dagger,
                AsteriskCode = asterisk.Length > 0 ? asterisk : null,
                Prefix = dagger,
            };
        }

        if (text.EndsWith(".-", StringComparison.Ordinal) || text.EndsWith(".—", StringComparison.Ordinal))
        {
            var prefix = text[..^2].Trim();
            return new ParsedIndexCode
            {
                Kind = IndexCodeFormatKind.CategoryUnspecified,
                Raw = text,
                Prefix = prefix,
            };
        }

        var rangeMatch = RangeRegex.Match(text);
        if (rangeMatch.Success)
        {
            return new ParsedIndexCode
            {
                Kind = IndexCodeFormatKind.CodeRange,
                Raw = text,
                RangeStart = rangeMatch.Groups[1].Value,
                RangeEnd = rangeMatch.Groups[2].Value,
                Prefix = rangeMatch.Groups[1].Value.Length >= 3
                    ? rangeMatch.Groups[1].Value[..3]
                    : rangeMatch.Groups[1].Value,
            };
        }

        var code = text.TrimEnd('*', '†').Trim();
        return new ParsedIndexCode
        {
            Kind = IndexCodeFormatKind.Single,
            Raw = text,
            Prefix = code,
        };
    }

    private static readonly System.Text.RegularExpressions.Regex RangeRegex =
        new(@"^([A-Z]\d{2}(?:\.\d+)?)-([A-Z]\d{2}(?:\.\d+)?)$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 范围端点展开为逐类目前缀（D65-D69 → D65..D69；I00-I99 只展开类目字母+两位数字）。
    /// 范围过宽（超过 maxCategories）返回空集合，由调用方回退原始码。
    /// </summary>
    public static IReadOnlyList<string> RangePrefixes(string start, string end, int maxCategories = 40)
    {
        if (start.Length < 3 || end.Length < 3 || start[0] != end[0])
        {
            return [];
        }

        if (!int.TryParse(start.AsSpan(1, 2), out var startNumber)
            || !int.TryParse(end.AsSpan(1, 2), out var endNumber)
            || endNumber < startNumber)
        {
            return [];
        }

        if (endNumber - startNumber + 1 > maxCategories)
        {
            return [];
        }

        var prefixes = new List<string>();
        for (var number = startNumber; number <= endNumber; number++)
        {
            prefixes.Add($"{start[0]}{number:00}");
        }

        return prefixes;
    }
}
