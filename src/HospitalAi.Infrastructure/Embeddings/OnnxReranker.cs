using HospitalAi.Application.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HospitalAi.Infrastructure.Embeddings;

/// <summary>Rerank 模型配置（bge-reranker 系 XLM-R 交叉编码，ONNX 量化，CPU）。</summary>
public sealed class RerankOptions
{
    public const string SectionName = "Rerank";

    public string ModelPath { get; set; } = "models/bge-reranker-base/model_quantized.onnx";

    public string TokenizerPath { get; set; } = "models/bge-reranker-base/tokenizer.json";

    public string ModelCode { get; set; } = "bge-reranker-base";

    public int MaxLength { get; set; } = 96;
}

/// <summary>
/// bge-reranker ONNX CPU 交叉编码（WP-B3-2，决策 2：CPU 小模型先行）。
/// (query, doc) 逐对编码 → logit → sigmoid ∈ [0,1]。
/// 批量填充 [N, L] 一次前向；首次调用惰性加载，失败永久标记不可用。
/// </summary>
public sealed class OnnxReranker : IReranker
{
    private readonly RerankOptions _options;
    private readonly SemaphoreSlim _inferLock = new(1, 1);
    private volatile bool _unavailable;
    private InferenceSession? _session;
    private XlmRUnigramTokenizer? _tokenizer;
    private string _inputIdsName = "input_ids";
    private string _attentionMaskName = "attention_mask";
    private bool _hasTokenTypeIds;
    private string _tokenTypeIdsName = "token_type_ids";
    private string _outputName = "logits";
    private bool _initialized;

    public OnnxReranker(IOptions<RerankOptions> options)
    {
        _options = options.Value;
    }

    public string ModelCode => _options.ModelCode;

    public bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return !_unavailable;
        }
    }

    public double[]? Score(string query, IReadOnlyList<string> documents)
    {
        EnsureInitialized();
        if (_unavailable || _session is null || _tokenizer is null || documents.Count == 0)
        {
            return null;
        }

        var encoded = documents
            .Select(document => _tokenizer.Encode(query + "</s>" + document))
            .ToList();
        var batch = encoded.Count;
        var sequenceLength = encoded.Max(item => item.InputIds.Length);

        var inputIds = new long[batch * sequenceLength];
        var attentionMask = new long[batch * sequenceLength];
        var tokenTypeIds = new long[batch * sequenceLength];
        for (var i = 0; i < batch; i++)
        {
            var (ids, mask) = encoded[i];
            Array.Copy(ids, 0, inputIds, i * sequenceLength, ids.Length);
            Array.Copy(mask, 0, attentionMask, i * sequenceLength, mask.Length);
            // 右侧填充：pad 位 attention_mask=0。
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputIdsName, new DenseTensor<long>(inputIds, [batch, sequenceLength])),
            NamedOnnxValue.CreateFromTensor(_attentionMaskName, new DenseTensor<long>(attentionMask, [batch, sequenceLength])),
        };
        if (_hasTokenTypeIds)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(_tokenTypeIdsName, new DenseTensor<long>(tokenTypeIds, [batch, sequenceLength])));
        }

        _inferLock.Wait();
        try
        {
            using var results = _session.Run(inputs);
            var output = results.First(value => value.Name == _outputName);
            var logits = (DenseTensor<float>)output.Value;
            var scores = new double[batch];
            for (var i = 0; i < batch; i++)
            {
                var logit = logits.Dimensions.Length == 2 ? logits[i, 0] : logits[i];
                scores[i] = 1.0 / (1.0 + Math.Exp(-logit));
            }

            return scores;
        }
        catch (Exception)
        {
            _unavailable = true;
            return null;
        }
        finally
        {
            _inferLock.Release();
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized || _unavailable)
        {
            return;
        }

        var modelPath = OnnxEmbeddingProvider.ResolvePath(_options.ModelPath);
        var tokenizerPath = OnnxEmbeddingProvider.ResolvePath(_options.TokenizerPath);
        if (modelPath is null || tokenizerPath is null)
        {
            _unavailable = true;
            _initialized = true;
            return;
        }

        try
        {
            _session = new InferenceSession(modelPath);
            _tokenizer = new XlmRUnigramTokenizer(tokenizerPath, _options.MaxLength);
            _inputIdsName = _session.InputMetadata.Keys.First(name => name.Contains("input_ids", StringComparison.OrdinalIgnoreCase));
            _attentionMaskName = _session.InputMetadata.Keys.First(name => name.Contains("attention", StringComparison.OrdinalIgnoreCase));
            _hasTokenTypeIds = _session.InputMetadata.Keys.Any(name => name.Contains("token_type", StringComparison.OrdinalIgnoreCase));
            if (_hasTokenTypeIds)
            {
                _tokenTypeIdsName = _session.InputMetadata.Keys.First(name => name.Contains("token_type", StringComparison.OrdinalIgnoreCase));
            }

            _outputName = _session.OutputMetadata.Keys.FirstOrDefault(name => name.Contains("logits", StringComparison.OrdinalIgnoreCase))
                ?? _session.OutputMetadata.Keys.First();
            _initialized = true;
        }
        catch (Exception)
        {
            _unavailable = true;
            _initialized = true;
        }
    }
}
