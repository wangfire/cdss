using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HospitalAi.Infrastructure.Embeddings;

/// <summary>嵌入模型配置（models/ 目录随仓库根分发，路径按程序目录向上回溯解析）。</summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    public string ModelPath { get; set; } = "models/bge-small-zh-v1.5/model.onnx";

    public string VocabPath { get; set; } = "models/bge-small-zh-v1.5/vocab.txt";

    public string ModelCode { get; set; } = "bge-small-zh-v1.5";

    public int MaxLength { get; set; } = 64;

    /// <summary>
    /// ONNX 推理线程上限。默认 4：不设限时 ORT 会按逻辑处理器数（本机 32）开线程，
    /// 与同机 Ollama 推理互相抢核，并发跑批时单请求耗时从 ~1.8s 恶化到 ~9s。
    /// </summary>
    public int IntraOpThreads { get; set; } = 4;
}

/// <summary>本地嵌入提供者抽象：不可用时 Embed 返回 null，调用方据此显式降级。</summary>
public interface IEmbeddingProvider
{
    bool IsAvailable { get; }

    string ModelCode { get; }

    /// <summary>返回 L2 归一化后的标题向量；模型不可用时返回 null。</summary>
    float[]? Embed(string text);
}

/// <summary>
/// bge-small-zh ONNX CPU 嵌入（决策 2：CPU 小模型先行，GPU POC 后仅替换模型文件）。
/// CLS 池化 + L2 归一化（BGE 检索口径）；首次调用惰性加载，加载失败永久标记不可用。
/// </summary>
public sealed class OnnxEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingOptions _options;
    private readonly SemaphoreSlim _inferLock = new(1, 1);
    private volatile bool _unavailable;
    private InferenceSession? _session;
    private BertWordPieceTokenizer? _tokenizer;
    private string _inputIdsName = "input_ids";
    private string _attentionMaskName = "attention_mask";
    private string _tokenTypeIdsName = "token_type_ids";
    private string _outputName = "last_hidden_state";
    private bool _hasTokenTypeIds;
    private bool _initialized;

    public OnnxEmbeddingProvider(IOptions<EmbeddingOptions> options)
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

    public float[]? Embed(string text)
    {
        EnsureInitialized();
        if (_unavailable || _session is null || _tokenizer is null || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var (inputIds, attentionMask) = _tokenizer.Encode(text);
        var sequenceLength = inputIds.Length;
        var tokenTypeIds = new long[sequenceLength];

        _inferLock.Wait();
        try
        {
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputIdsName, new DenseTensor<long>(inputIds, [1, sequenceLength])),
                NamedOnnxValue.CreateFromTensor(_attentionMaskName, new DenseTensor<long>(attentionMask, [1, sequenceLength])),
            };
            if (_hasTokenTypeIds)
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(_tokenTypeIdsName, new DenseTensor<long>(tokenTypeIds, [1, sequenceLength])));
            }

            using var results = _session.Run(inputs);
            var output = results.First(value => value.Name == _outputName);
            var hidden = (DenseTensor<float>)output.Value;
            // CLS 池化：hidden 形状 [1, seq, dim]，取 [0, 0, *]。
            var dim = hidden.Dimensions[^1];
            var vector = new float[dim];
            for (var i = 0; i < dim; i++)
            {
                vector[i] = hidden[0, 0, i];
            }

            Normalize(vector);
            return vector;
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

        var modelPath = ResolvePath(_options.ModelPath);
        var vocabPath = ResolvePath(_options.VocabPath);
        if (modelPath is null || vocabPath is null)
        {
            _unavailable = true;
            _initialized = true;
            return;
        }

        try
        {
            _session = new InferenceSession(modelPath, CreateSessionOptions(_options.IntraOpThreads));
            _tokenizer = new BertWordPieceTokenizer(vocabPath, _options.MaxLength);
            _inputIdsName = _session.InputMetadata.Keys.First(name => name.Contains("input_ids", StringComparison.OrdinalIgnoreCase));
            _attentionMaskName = _session.InputMetadata.Keys.First(name => name.Contains("attention", StringComparison.OrdinalIgnoreCase));
            _hasTokenTypeIds = _session.InputMetadata.Keys.Any(name => name.Contains("token_type", StringComparison.OrdinalIgnoreCase));
            if (_hasTokenTypeIds)
            {
                _tokenTypeIdsName = _session.InputMetadata.Keys.First(name => name.Contains("token_type", StringComparison.OrdinalIgnoreCase));
            }

            _outputName = _session.OutputMetadata.Keys.First(name => name.Contains("hidden", StringComparison.OrdinalIgnoreCase));
            _initialized = true;
        }
        catch (Exception)
        {
            _unavailable = true;
            _initialized = true;
        }
    }

    /// <summary>
    /// 限线程会话选项：嵌入与精排都用同一口径，避免默认按核数开线程与 Ollama 抢 CPU。
    /// </summary>
    internal static SessionOptions CreateSessionOptions(int intraOpThreads)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        if (intraOpThreads > 0)
        {
            options.IntraOpNumThreads = intraOpThreads;
            options.InterOpNumThreads = intraOpThreads;
        }

        return options;
    }

    /// <summary>相对路径解析：从程序目录向上回溯（API/Worker/工具的内容根深度不同）。</summary>
    internal static string? ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return File.Exists(path) ? path : null;
        }

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, path);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return File.Exists(path) ? path : null;
    }

    private static void Normalize(float[] vector)
    {
        var sum = 0f;
        foreach (var value in vector)
        {
            sum += value * value;
        }

        var norm = MathF.Sqrt(sum);
        if (norm <= 0f)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }
    }
}
