using System.Reflection;
using System.Text;
using FullTextSearch.Core;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;

namespace FullTextSearch.Infrastructure.Sudachi;

// Sudachi ネイティブ（sudachi.rs / モード C）をプロセス内で呼び出す Lucene Tokenizer。
// === 調査メモ ===
// 1. Analyzer 再利用時は Reset() で基底 Tokenizer の reader をリフレクション取得する。
// 2. Immense term: Lucene UTF-8 32766 バイト制限 → SplitToMaxTermLength / TruncateOrSplitToken。
// 3. Sudachi Context はスレッド専用（ThreadLocal）。Warmup() で DLL・辞書を事前ロードする。

/// <summary>
/// Sudachi（モード C）をネイティブ DLL 経由で呼び出し、トークン列を返す Lucene Tokenizer。
/// </summary>
public sealed class SudachiTokenizer : Tokenizer
{
    private readonly ICharTermAttribute _termAttr;
    private readonly TextReader _input;
    private List<string> _tokens = [];
    private int _index;

    private static readonly ThreadLocal<IntPtr> ThreadContext = new(CreateThreadContext, trackAllValues: true);

    /// <summary>指定ファクトリと入力でトークナイザを初期化する。</summary>
    public SudachiTokenizer(AttributeFactory factory, TextReader input)
        : base(factory, input)
    {
        _termAttr = AddAttribute<ICharTermAttribute>();
        _input = input;
    }

    /// <summary>既定ファクトリと入力でトークナイザを初期化する。</summary>
    public SudachiTokenizer(TextReader input)
        : base(AttributeFactory.DEFAULT_ATTRIBUTE_FACTORY, input)
    {
        _termAttr = AddAttribute<ICharTermAttribute>();
        _input = input;
    }

    /// <inheritdoc />
    public override sealed bool IncrementToken()
    {
        ClearAttributes();
        if (_index >= _tokens.Count)
            return false;
        var term = _tokens[_index];
        if (string.IsNullOrEmpty(term))
        {
            _index++;
            return IncrementToken();
        }
        if (Encoding.UTF8.GetByteCount(term) > ContentLimits.LuceneMaxTermUtf8Bytes)
        {
            var (first, rest) = SplitAtMaxUtf8Bytes(term, ContentLimits.LuceneMaxTermUtf8Bytes);
            _tokens[_index] = rest;
            _termAttr.SetEmpty().Append(first);
            return true;
        }
        _index++;
        _termAttr.SetEmpty().Append(term);
        return true;
    }

    /// <inheritdoc />
    public override void Reset()
    {
        base.Reset();
        _tokens = [];
        _index = 0;
        var reader = GetCurrentReader() ?? _input;
        var text = ReadAll(reader);
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            if (SudachiNative.TryEnsureInitialized())
            {
                var ctx = ThreadContext.Value;
                if (ctx != IntPtr.Zero)
                {
                    _tokens = TokenizeText(ctx, text);
                }
            }
            if (_tokens.Count == 0 && text.Length > 0)
                _tokens = SplitToMaxTermLength(text);
        }
        catch
        {
            if (text.Length > 0)
                _tokens = SplitToMaxTermLength(text);
        }
        _tokens = _tokens.SelectMany(TruncateOrSplitToken).ToList();
    }

    /// <summary>
    /// 推奨インデックス並列度の目安。スレッドごとの Sudachi コンテキストで並列化（論理コア半分、最大 8）。
    /// </summary>
    public static readonly int PoolSize = Math.Max(2, Math.Min(8, Environment.ProcessorCount / 2));

    /// <summary>辞書・ネイティブ DLL を事前ロードし、呼び出しスレッドのコンテキストを確保する。</summary>
    public static void Warmup()
    {
        if (!SudachiNative.TryEnsureInitialized())
            return;
        _ = ThreadContext.Value;
    }

    private static IntPtr CreateThreadContext()
    {
        if (!SudachiNative.TryEnsureInitialized())
            return IntPtr.Zero;
        return SudachiNative.CreateContext();
    }

    private static List<string> TokenizeText(IntPtr ctx, string text)
    {
        if (text.Length <= ContentLimits.SudachiTokenizeChunkChars)
            return SudachiNative.Tokenize(ctx, text);

        var all = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var chunkEnd = Math.Min(start + ContentLimits.SudachiTokenizeChunkChars, text.Length);
            if (chunkEnd < text.Length)
            {
                var splitAt = text.LastIndexOf('\n', chunkEnd - 1, chunkEnd - start);
                if (splitAt > start)
                    chunkEnd = splitAt + 1;
            }

            all.AddRange(SudachiNative.Tokenize(ctx, text[start..chunkEnd]));
            start = chunkEnd;
        }
        return all;
    }

    private static List<string> SplitToMaxTermLength(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= ContentLimits.LuceneMaxTermUtf8Bytes) return [text];
        var list = new List<string>();
        for (var i = 0; i < bytes.Length; i += ContentLimits.LuceneMaxTermUtf8Bytes)
        {
            var len = Math.Min(ContentLimits.LuceneMaxTermUtf8Bytes, bytes.Length - i);
            list.Add(Encoding.UTF8.GetString(bytes, i, len));
        }
        return list;
    }

    private static IEnumerable<string> TruncateOrSplitToken(string token)
    {
        if (string.IsNullOrEmpty(token)) yield break;
        if (Encoding.UTF8.GetByteCount(token) <= ContentLimits.LuceneMaxTermUtf8Bytes)
        {
            yield return token;
            yield break;
        }
        foreach (var chunk in SplitToMaxTermLength(token))
            yield return chunk;
    }

    private static (string first, string rest) SplitAtMaxUtf8Bytes(string s, int maxBytes)
    {
        if (string.IsNullOrEmpty(s)) return ("", "");
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length <= maxBytes) return (s, "");
        var i = maxBytes;
        while (i > 0 && (bytes[i] & 0xC0) == 0x80)
            i--;
        return (Encoding.UTF8.GetString(bytes, 0, i), Encoding.UTF8.GetString(bytes, i, bytes.Length - i));
    }

    private static FieldInfo? _inputFieldInfo;

    private TextReader? GetCurrentReader()
    {
        var field = _inputFieldInfo ??= typeof(Tokenizer).GetField("m_input", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? typeof(Tokenizer).GetField("input", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? typeof(Tokenizer).GetField("m_reader", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(this) as TextReader;
    }

    private static string ReadAll(TextReader reader)
    {
        var sb = new StringBuilder();
        var buf = new char[4096];
        int n;
        while ((n = reader.Read(buf, 0, buf.Length)) > 0)
            sb.Append(buf, 0, n);
        return sb.ToString();
    }
}
