using System.Security.Cryptography;
using System.Text;

namespace HospitalAi.Application.Coding.Preprocessing;

/// <summary>
/// 文档切片（Chunk）。保留原文位置与稳定 content_hash，
/// 供 Evidence 溯源与 ES medical-chunks 索引复用。
/// </summary>
public sealed record TextChunk(
    Guid? SectionId,
    int StartPosition,
    int EndPosition,
    string Text,
    string ContentHash);

/// <summary>
/// V2.2-Lite Chunk 切分器：按标题、换行、句号、分号切分；合并过短片段；
/// 超长片段按粗略 token 长度继续拆分。禁止在切分中截断诊断短语。
/// 禁止直接把旧 DocumentSection 当作推荐证据（Section 只作为定位锚点）。
/// </summary>
public static class DocumentChunker
{
    private const int MinChunkLength = 24;
    private const int MaxChunkLength = 480;
    private static readonly char[] SentenceSeparators = ['。', '；', '!', '！', '?', '？', '\n'];
    private static readonly char[] HeadingEndings = [':', '：', '】', '」', '」'];

    public static IReadOnlyList<TextChunk> Chunk(string text, Func<string, Guid?>? sectionResolver = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var spans = new List<(int Start, int End)>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (!SentenceSeparators.Contains(text[index]))
            {
                continue;
            }

            spans.Add((start, index + 1));
            start = index + 1;
        }

        if (start < text.Length)
        {
            spans.Add((start, text.Length));
        }

        var merged = new List<(int Start, int End)>();
        foreach (var span in spans)
        {
            var length = span.End - span.Start;
            if (merged.Count > 0
                && length < MinChunkLength
                && merged[^1].End - merged[^1].Start < MaxChunkLength)
            {
                merged[^1] = (merged[^1].Start, span.End);
                continue;
            }

            merged.Add(span);
        }

        var chunks = new List<TextChunk>();
        foreach (var span in merged)
        {
            if (span.End - span.Start <= MaxChunkLength)
            {
                chunks.Add(CreateChunk(text, span, sectionResolver));
                continue;
            }

            // 超长片段按逗号等次级切分点继续拆分，保持诊断短语完整。
            var cursor = span.Start;
            var softStart = span.Start;
            for (var index = span.Start; index < span.End; index++)
            {
                if (index == span.End - 1 || index - softStart >= MaxChunkLength)
                {
                    chunks.Add(CreateChunk(text, (softStart, index + 1), sectionResolver));
                    softStart = index + 1;
                    cursor = index + 1;
                }
            }

            if (softStart < span.End && softStart > cursor)
            {
                chunks.Add(CreateChunk(text, (softStart, span.End), sectionResolver));
            }
        }

        return chunks.Where(chunk => chunk.Text.Length > 0).ToList();
    }

    public static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>按文本内容猜测所属 Section（标题匹配），用于定位锚点。</summary>
    public static Guid? ResolveSection(
        string chunkText,
        IReadOnlyDictionary<string, Guid> sectionsByHeading)
    {
        foreach (var (heading, id) in sectionsByHeading)
        {
            if (chunkText.Contains(heading, StringComparison.Ordinal))
            {
                return id;
            }
        }

        return null;
    }

    public static bool LooksLikeHeading(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 30)
        {
            return false;
        }

        return HeadingEndings.Any(trimmed.EndsWith)
            || trimmed.StartsWith('【')
            || trimmed.StartsWith('[');
    }

    private static TextChunk CreateChunk(
        string text,
        (int Start, int End) span,
        Func<string, Guid?>? sectionResolver)
    {
        var chunkText = text[span.Start..span.End].Trim();
        var adjustedStart = span.Start + (text[span.Start..span.End].Length - chunkText.Length);
        var sectionId = sectionResolver?.Invoke(chunkText);
        return new TextChunk(
            sectionId,
            adjustedStart,
            span.End,
            chunkText,
            ComputeHash(chunkText));
    }
}
