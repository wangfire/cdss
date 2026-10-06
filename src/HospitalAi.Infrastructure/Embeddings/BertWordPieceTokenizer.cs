namespace HospitalAi.Infrastructure.Embeddings;

/// <summary>
/// BERT WordPiece 分词器（bge-small-zh 词表）：
/// 基础清洗（去控制字符）→ 小写化 + 去重音 → CJK 单字切分 → 标点独立切分 → 贪心 WordPiece。
/// 与 HuggingFace BertTokenizer 的常用路径对齐，足够语义检索场景使用。
/// </summary>
public sealed class BertWordPieceTokenizer
{
    public const string ClsToken = "[CLS]";
    public const string SepToken = "[SEP]";
    public const string UnkToken = "[UNK]";

    private readonly Dictionary<string, int> _vocab;

    public int MaxLength { get; }

    public BertWordPieceTokenizer(string vocabPath, int maxLength = 64)
    {
        _vocab = File.ReadAllLines(vocabPath)
            .Select((token, index) => (token, index))
            .ToDictionary(pair => pair.token, pair => pair.index);
        MaxLength = maxLength;
    }

    /// <summary>编码为 [CLS] tokens [SEP]，返回 input_ids 与 attention_mask。</summary>
    public (long[] InputIds, long[] AttentionMask) Encode(string text)
    {
        var ids = new List<long>(MaxLength) { _vocab[ClsToken] };
        var bodyLimit = MaxLength - 2;
        foreach (var token in BasicTokenize(text))
        {
            if (ids.Count - 1 >= bodyLimit)
            {
                break;
            }

            if (_vocab.TryGetValue(token, out var id))
            {
                ids.Add(id);
                continue;
            }

            foreach (var piece in WordPiece(token))
            {
                if (ids.Count - 1 >= bodyLimit)
                {
                    break;
                }

                ids.Add(piece);
            }
        }

        ids.Add(_vocab[SepToken]);
        var inputIds = ids.ToArray();
        var attentionMask = new long[inputIds.Length];
        Array.Fill(attentionMask, 1L);
        return (inputIds, attentionMask);
    }

    /// <summary>贪心最长匹配；整词无法完全覆盖时整词置 [UNK]（与 BERT WordPiece 一致）。</summary>
    private IEnumerable<int> WordPiece(string token)
    {
        var results = new List<int>();
        var start = 0;
        while (start < token.Length)
        {
            var end = token.Length;
            int? matched = null;
            while (start < end)
            {
                var substr = start == 0 ? token[start..end] : "##" + token[start..end];
                if (_vocab.TryGetValue(substr, out var id))
                {
                    matched = id;
                    break;
                }

                end--;
            }

            if (matched is null)
            {
                return [_vocab[UnkToken]];
            }

            results.Add(matched.Value);
            start = end;
        }

        return results;
    }

    private static List<string> BasicTokenize(string text)
    {
        var cleaned = new string(text.Where(ch => !char.IsControl(ch)).ToArray());
        var lowered = LowercaseAndStripAccents(cleaned);
        var spaced = SpaceAroundCjk(lowered);
        var results = new List<string>();
        foreach (var raw in spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var piece in SplitPunctuation(raw))
            {
                if (piece.Length > 0)
                {
                    results.Add(piece);
                }
            }
        }

        return results;
    }

    private static string LowercaseAndStripAccents(string text)
    {
        var formD = text.Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    // BERT 基础分词：CJK 字符（含扩展 A/兼容区）逐字独立，两侧补空格。
    private static string SpaceAroundCjk(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length * 2);
        foreach (var ch in text)
        {
            if (IsCjk(ch))
            {
                builder.Append(' ').Append(ch).Append(' ');
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static bool IsCjk(char ch)
    {
        return ch is >= '\u4E00' and <= '\u9FFF'
            or >= '\u3400' and <= '\u4DBF'
            or >= '\uF900' and <= '\uFAFF';
    }

    // BERT _is_punctuation：ASCII 标点区段 + Unicode P* 类别，独立成 token。
    private static IEnumerable<string> SplitPunctuation(string token)
    {
        var start = 0;
        for (var i = 0; i < token.Length; i++)
        {
            if (IsPunctuation(token[i]))
            {
                if (i > start)
                {
                    yield return token[start..i];
                }

                yield return token[i..(i + 1)];
                start = i + 1;
            }
        }

        if (start < token.Length)
        {
            yield return token[start..];
        }
    }

    private static bool IsPunctuation(char ch)
    {
        if ((ch >= 33 && ch <= 47) || (ch >= 58 && ch <= 64) || (ch >= 91 && ch <= 96) || (ch >= 123 && ch <= 126))
        {
            return true;
        }

        return char.GetUnicodeCategory(ch) is System.Globalization.UnicodeCategory.OtherPunctuation
            or System.Globalization.UnicodeCategory.DashPunctuation
            or System.Globalization.UnicodeCategory.OpenPunctuation
            or System.Globalization.UnicodeCategory.ClosePunctuation
            or System.Globalization.UnicodeCategory.InitialQuotePunctuation
            or System.Globalization.UnicodeCategory.FinalQuotePunctuation;
    }
}
