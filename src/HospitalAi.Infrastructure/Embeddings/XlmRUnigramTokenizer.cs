using System.Text.Json;

namespace HospitalAi.Infrastructure.Embeddings;

/// <summary>
/// XLM-R SentencePiece Unigram 分词器（bge-reranker 系）：
/// 解析 HuggingFace tokenizer.json 的 Unigram 词表（piece + score），
/// NFKC 归一化 + Metaspace（空格→▁，前缀补 ▁）+ Viterbi 最优切分，
/// 未知单字回退 &lt;unk&gt;。仅覆盖交叉编码检索所需路径。
/// </summary>
public sealed class XlmRUnigramTokenizer
{
    private readonly string[] _idToPiece;
    private readonly Dictionary<string, int> _pieceToId;
    private readonly float[] _scores;
    private readonly int _unkId;
    private readonly int _bosId;
    private readonly int _eosId;
    private readonly int _maxPieceLength;
    private readonly float _unkPenalty;

    public int MaxLength { get; }

    public XlmRUnigramTokenizer(string tokenizerJsonPath, int maxLength = 96)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(tokenizerJsonPath));
        var model = document.RootElement.GetProperty("model");
        _unkId = model.GetProperty("unk_id").GetInt32();

        var pieces = new List<(string Piece, float Score)>();
        foreach (var item in model.GetProperty("vocab").EnumerateArray())
        {
            var array = item.EnumerateArray().ToArray();
            pieces.Add((array[0].GetString()!, (float)array[1].GetDouble()));
        }

        _idToPiece = new string[pieces.Count];
        _pieceToId = new Dictionary<string, int>(pieces.Count, StringComparer.Ordinal);
        _scores = new float[pieces.Count];
        var minScore = float.MaxValue;
        for (var i = 0; i < pieces.Count; i++)
        {
            _idToPiece[i] = pieces[i].Piece;
            _pieceToId[pieces[i].Piece] = i;
            _scores[i] = pieces[i].Score;
            if (pieces[i].Score < minScore)
            {
                minScore = pieces[i].Score;
            }

            var length = pieces[i].Piece.Length;
            if (length > _maxPieceLength)
            {
                _maxPieceLength = length;
            }
        }

        _unkPenalty = minScore - 10f;

        // 特殊词元 id 从 added_tokens 取（<s> / </s>），避免硬编码。
        _bosId = _pieceToId.TryGetValue("<s>", out var bos) ? bos : 0;
        _eosId = _pieceToId.TryGetValue("</s>", out var eos) ? eos : 2;

        MaxLength = maxLength;
    }

    /// <summary>编码为 &lt;s&gt; pieces &lt;/s&gt;，超长截断。</summary>
    public (long[] InputIds, long[] AttentionMask) Encode(string text)
    {
        var body = ViterbiEncode(Normalize(text));
        var limit = MaxLength - 2;
        var ids = new List<long>(MaxLength) { _bosId };
        foreach (var id in body)
        {
            if (ids.Count - 1 >= limit)
            {
                break;
            }

            ids.Add(id);
        }

        ids.Add(_eosId);
        var inputIds = ids.ToArray();
        var attentionMask = new long[inputIds.Length];
        Array.Fill(attentionMask, 1L);
        return (inputIds, attentionMask);
    }

    private static string Normalize(string text)
    {
        var cleaned = new string(text.Where(ch => !char.IsControl(ch)).ToArray());
        // XLM-R 用 precompiled charsmap（≈ NMT-NFKC），此处以 NFKC 近似。
        var normalized = cleaned.Normalize(System.Text.NormalizationForm.FormKC);
        var spaced = string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return "▁" + spaced.Replace(' ', '▁');
    }

    /// <summary>Unigram Viterbi 最优切分；孤立未知字符按 unk 惩罚分走通路径。</summary>
    private List<int> ViterbiEncode(string text)
    {
        var length = text.Length;
        var best = new float[length + 1];
        var backId = new int[length + 1];
        var backLen = new int[length + 1];
        Array.Fill(best, float.NegativeInfinity);
        best[0] = 0f;

        for (var start = 0; start < length; start++)
        {
            if (float.IsNegativeInfinity(best[start]))
            {
                continue;
            }

            var maxEnd = Math.Min(length, start + _maxPieceLength);
            for (var end = start + 1; end <= maxEnd; end++)
            {
                if (_pieceToId.TryGetValue(text[start..end], out var id))
                {
                    var candidate = best[start] + _scores[id];
                    if (candidate > best[end])
                    {
                        best[end] = candidate;
                        backId[end] = id;
                        backLen[end] = end - start;
                    }
                }
            }

            // HF Unigram 语义：词表外单字走 unk 边；词表内单字只有词表边（含更长 piece 竞争）。
            if (!_pieceToId.ContainsKey(text[start..(start + 1)]))
            {
                var unkCandidate = best[start] + _unkPenalty;
                if (unkCandidate > best[start + 1])
                {
                    best[start + 1] = unkCandidate;
                    backId[start + 1] = _unkId;
                    backLen[start + 1] = 1;
                }
            }
        }

        var result = new List<int>();
        var position = length;
        while (position > 0)
        {
            result.Add(backId[position]);
            position -= backLen[position];
        }

        result.Reverse();
        return result;
    }
}
