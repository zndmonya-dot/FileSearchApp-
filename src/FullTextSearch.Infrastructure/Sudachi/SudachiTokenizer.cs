using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;

namespace FullTextSearch.Infrastructure.Sudachi;

// === 根本調査メモ（インデックス構築・差分更新の不具合） ===
// 1. コンポーネント再利用: Analyzer が TokenStreamComponents を再利用するため、2件目以降は
//    基底 Tokenizer の reader が SetReader で差し替わる。コンストラクタの _input は古いままなので、
//    Reset() でリフレクションにより基底の m_input（現在の reader）を取得して使用する。
// 2. Immense term: Lucene は 1 トークンあたり UTF-8 で最大 32766 バイト。超えると例外になる。
//    フォールバックで全文を 1 トークンにしていたため長文で発生。対策: SplitToMaxTermLength で
//    分割し、全トークンに TruncateOrSplitToken を適用。
// 3. スクリプト未検出: ResolveScriptPath() が null のとき早期 return すると _tokens が空のままになり、
//    そのドキュメントは content にトークン 0 個でインデックスされる。対策: scriptPath が null でも
//    全文を SplitToMaxTermLength で分割してフォールバックする。
// 4. 差分更新の DirectoryReader: Writer が開いたまま Open(directory) だと Windows でロック競合する
//    場合がある。LuceneIndexService で Open(writer) を優先し、失敗時のみ Open(directory) にフォールバック。

/// <summary>
/// SudachiPy（モード C）をサブプロセスで呼び出し、トークン列を返す Lucene Tokenizer。
/// 高速化のためストリームモード（共有プロセス再利用）を主経路とし、失敗時のみワンショットにフォールバック。
/// </summary>
public sealed class SudachiTokenizer : Tokenizer
{
    private readonly ICharTermAttribute _termAttr;
    private readonly TextReader _input;
    private List<string> _tokens = [];
    private int _index;

    public SudachiTokenizer(AttributeFactory factory, TextReader input)
        : base(factory, input)
    {
        _termAttr = AddAttribute<ICharTermAttribute>();
        _input = input;
    }

    public SudachiTokenizer(TextReader input)
        : base(AttributeFactory.DEFAULT_ATTRIBUTE_FACTORY, input)
    {
        _termAttr = AddAttribute<ICharTermAttribute>();
        _input = input;
    }

    public override sealed bool IncrementToken()
    {
        ClearAttributes();
        if (_tokens == null || _index >= _tokens.Count)
            return false;
        var term = _tokens[_index];
        if (string.IsNullOrEmpty(term))
        {
            _index++;
            return IncrementToken();
        }
        // 最終防御: 1 トークンが Lucene 制限超なら文字境界で分割して出力（immense term エラー防止）
        if (Encoding.UTF8.GetByteCount(term) > MaxTermUtf8Bytes)
        {
            var (first, rest) = SplitAtMaxUtf8Bytes(term, MaxTermUtf8Bytes);
            _tokens[_index] = rest;
            _termAttr.SetEmpty().Append(first);
            return true;
        }
        _index++;
        _termAttr.SetEmpty().Append(term);
        return true;
    }

    public override void Reset()
    {
        base.Reset();
        _tokens = [];
        _index = 0;
        // コンポーネント再利用時は基底の reader が差し替わるため、現在の reader を取得する
        var reader = GetCurrentReader() ?? _input;
        var text = ReadAll(reader);
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (text.Length > MaxInputCharsForTokenize)
            text = text.Substring(0, MaxInputCharsForTokenize);
        var scriptPath = ResolveScriptPath();
        if (string.IsNullOrEmpty(scriptPath))
        {
            // スクリプト未検出時もフォールバック: 全文を Lucene 制限内に分割してインデックス（検索は可能になる）
            _tokens = SplitToMaxTermLength(text);
            _tokens = _tokens.SelectMany(t => TruncateOrSplitToken(t)).ToList();
            return;
        }
        try
        {
            _tokens = InvokeSudachi(scriptPath, text);
            if (_tokens.Count == 0 && text.Length > 0)
                _tokens = SplitToMaxTermLength(text);
        }
        catch
        {
            if (text.Length > 0)
                _tokens = SplitToMaxTermLength(text);
        }
        _tokens = _tokens.SelectMany(t => TruncateOrSplitToken(t)).ToList();
    }

    /// <summary>Lucene の 1 トークンあたり最大バイト数（UTF-8）。超えると "immense term" でエラーになる。</summary>
    private const int MaxTermUtf8Bytes = 32765;

    /// <summary>1 ドキュメントあたり Sudachi に渡す最大文字数。超えると先頭のみ送りオーバーで落ちるのを防ぐ。</summary>
    private const int MaxInputCharsForTokenize = 500_000;

    /// <summary>バッチトークン化で一度に送る最大ドキュメント数。</summary>
    private const int MaxBatchDocuments = 40;

    /// <summary>バッチ時 1 ドキュメントあたりの最大文字数（ハイライト用なので先頭で十分）。</summary>
    private const int MaxCharsPerContentInBatch = 80_000;

    /// <summary>長い文字列を Lucene の制限以内に分割する。</summary>
    private static List<string> SplitToMaxTermLength(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= MaxTermUtf8Bytes) return [text];
        var list = new List<string>();
        for (var i = 0; i < bytes.Length; i += MaxTermUtf8Bytes)
        {
            var len = Math.Min(MaxTermUtf8Bytes, bytes.Length - i);
            list.Add(Encoding.UTF8.GetString(bytes, i, len));
        }
        return list;
    }

    /// <summary>1 トークンが最大長を超える場合は分割して返す。</summary>
    private static IEnumerable<string> TruncateOrSplitToken(string token)
    {
        if (string.IsNullOrEmpty(token)) yield break;
        if (Encoding.UTF8.GetByteCount(token) <= MaxTermUtf8Bytes)
        {
            yield return token;
            yield break;
        }
        foreach (var chunk in SplitToMaxTermLength(token))
            yield return chunk;
    }

    /// <summary>UTF-8 バイト数で文字境界の位置で分割し、(先頭 chunk, 残り) を返す。IncrementToken の最終防御用。</summary>
    private static (string first, string rest) SplitAtMaxUtf8Bytes(string s, int maxBytes)
    {
        if (string.IsNullOrEmpty(s)) return ("", "");
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length <= maxBytes) return (s, "");
        int i = maxBytes;
        while (i > 0 && (bytes[i] & 0xC0) == 0x80)
            i--;
        var first = Encoding.UTF8.GetString(bytes, 0, i);
        var rest = Encoding.UTF8.GetString(bytes, i, bytes.Length - i);
        return (first, rest);
    }

    private static FieldInfo? _inputFieldInfo;

    /// <summary>
    /// 基底 Tokenizer が保持する現在の TextReader をリフレクションで取得。
    /// FieldInfo は static キャッシュし、ドキュメントごとの GetField を避ける。
    /// </summary>
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

    /// <summary>ストリームモード時のドキュメント区切り（Python と一致）</summary>
    private const string StreamDelim = "---SUDACHI_DOC_END---";

    private static readonly object SharedProcessLock = new();
    private static Process? _sharedProcess;

    private static string? _cachedScriptPath;
    private static readonly object ScriptPathLock = new();

    /// <summary>スクリプトパスを解決。結果は static キャッシュし、ドキュメントごとの File.Exists を避ける。</summary>
    private static string? ResolveScriptPath()
    {
        if (_cachedScriptPath != null)
            return _cachedScriptPath;
        lock (ScriptPathLock)
        {
            if (_cachedScriptPath != null)
                return _cachedScriptPath;
            var baseDir = AppContext.BaseDirectory ?? "";
            var candidates = new[]
            {
                Path.Combine(baseDir, "sudachi_tokenize.py"),
                Path.Combine(baseDir, "tools", "sudachi_tokenize.py"),
                Path.Combine(Directory.GetCurrentDirectory(), "sudachi_tokenize.py"),
                Path.Combine(Directory.GetCurrentDirectory(), "tools", "sudachi_tokenize.py")
            };
            _cachedScriptPath = candidates.FirstOrDefault(File.Exists);
            return _cachedScriptPath;
        }
    }

    /// <summary>スクリプトパスキャッシュをクリア（テストや再検出用）。</summary>
    public static void ClearScriptPathCache()
    {
        lock (ScriptPathLock) { _cachedScriptPath = null; }
    }

    /// <summary>Sudachi C モード（Python スクリプト）が利用可能かどうか。</summary>
    public static bool IsAvailable()
    {
        return !string.IsNullOrEmpty(ResolveScriptPath());
    }

    /// <summary>高速化: インデックス初期化時に呼び、共有 Python プロセスを事前起動する。最初のドキュメントからストリームモードが使える。</summary>
    public static void Warmup()
    {
        var scriptPath = ResolveScriptPath();
        if (string.IsNullOrEmpty(scriptPath)) return;
        _ = InvokeSudachiStream(scriptPath, " ");
    }

    /// <summary>共有プロセスを破棄（エラー時・終了検知時）。ロック内で呼ぶ。</summary>
    private static void DisposeSharedProcess()
    {
        try { _sharedProcess?.Dispose(); }
        catch { /* ignore */ }
        _sharedProcess = null;
    }

    /// <summary>ストリームモードの共有プロセスで 1 ドキュメント分トークン化。失敗時は null を返し呼び出し側でワンショットにフォールバックする。</summary>
    private static List<string>? InvokeSudachiStream(string scriptPath, string text)
    {
        var python = FindPython();
        if (string.IsNullOrEmpty(python)) return null;

        lock (SharedProcessLock)
        {
            try
            {
                if (_sharedProcess == null || _sharedProcess.HasExited)
                {
                    DisposeSharedProcess();
                    var psi = new ProcessStartInfo
                    {
                        FileName = python,
                        ArgumentList = { scriptPath, "--stream" },
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardInputEncoding = Encoding.UTF8,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    _sharedProcess = Process.Start(psi);
                    if (_sharedProcess == null) return null;
                }

                var stdin = _sharedProcess.StandardInput;
                stdin.Write(text);
                stdin.Write('\n');
                stdin.Write(StreamDelim);
                stdin.Write('\n');
                stdin.Flush();

                var list = new List<string>();
                var stdout = _sharedProcess.StandardOutput;
                string? line;
                while ((line = stdout.ReadLine()) != null)
                {
                    var t = line.Trim();
                    if (t == StreamDelim)
                        break;
                    if (t.Length > 0)
                        list.Add(t);
                }
                return list;
            }
            catch
            {
                DisposeSharedProcess();
                return null;
            }
        }
    }

    /// <summary>検索ハイライト用: 複数ドキュメントの content を 1 回で Python に送り、ドキュメントごとのトークン列を返す。失敗時は null。件数・長さ制限でオーバーを防ぐ。</summary>
    public static List<List<string>>? InvokeSudachiBatch(IReadOnlyList<string> contents)
    {
        if (contents == null || contents.Count == 0)
            return [];
        var scriptPath = ResolveScriptPath();
        if (string.IsNullOrEmpty(scriptPath)) return null;
        var python = FindPython();
        if (string.IsNullOrEmpty(python)) return null;

        var n = Math.Min(contents.Count, MaxBatchDocuments);
        var toSend = new List<string>(n);
        for (var i = 0; i < n; i++)
        {
            var s = contents[i] ?? "";
            if (s.Length > MaxCharsPerContentInBatch)
                s = s.Substring(0, MaxCharsPerContentInBatch);
            toSend.Add(s);
        }

        lock (SharedProcessLock)
        {
            try
            {
                if (_sharedProcess == null || _sharedProcess.HasExited)
                {
                    DisposeSharedProcess();
                    var psi = new ProcessStartInfo
                    {
                        FileName = python,
                        ArgumentList = { scriptPath, "--stream" },
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardInputEncoding = Encoding.UTF8,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    _sharedProcess = Process.Start(psi);
                    if (_sharedProcess == null) return null;
                }

                var stdin = _sharedProcess.StandardInput;
                foreach (var text in toSend)
                {
                    stdin.Write(text);
                    stdin.Write('\n');
                    stdin.Write(StreamDelim);
                    stdin.Write('\n');
                }
                stdin.Flush();

                var result = new List<List<string>>();
                var current = new List<string>();
                var stdout = _sharedProcess.StandardOutput;
                string? line;
                while ((line = stdout.ReadLine()) != null)
                {
                    var t = line.Trim();
                    if (t == StreamDelim)
                    {
                        result.Add(current);
                        current = new List<string>();
                        if (result.Count >= toSend.Count)
                            break;
                        continue;
                    }
                    if (t.Length > 0)
                        current.Add(t);
                }
                if (current.Count > 0 || result.Count < toSend.Count)
                    result.Add(current);
                while (result.Count < toSend.Count)
                    result.Add([]);
                return result;
            }
            catch
            {
                DisposeSharedProcess();
                return null;
            }
        }
    }

    /// <summary>高速化: まず共有プロセス（ストリーム）で実行し、失敗時のみワンショット。</summary>
    private static List<string> InvokeSudachi(string scriptPath, string text)
    {
        var streamResult = InvokeSudachiStream(scriptPath, text);
        if (streamResult != null)
            return streamResult;
        return InvokeSudachiOneshot(scriptPath, text);
    }

    /// <summary>フォールバック用: 1 ドキュメントごとにプロセス起動（遅い）。</summary>
    private static List<string> InvokeSudachiOneshot(string scriptPath, string text)
    {
        if (text.Length > MaxInputCharsForTokenize)
            text = text.Substring(0, MaxInputCharsForTokenize);
        var python = FindPython();
        if (string.IsNullOrEmpty(python))
            return [];

        var psi = new ProcessStartInfo
        {
            FileName = python,
            ArgumentList = { scriptPath },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var list = new List<string>();
        using (var process = Process.Start(psi))
        {
            if (process == null)
                return [];
            using (var stdin = new StreamWriter(process.StandardInput.BaseStream, Encoding.UTF8) { AutoFlush = true })
            {
                stdin.Write(text);
                stdin.Flush();
            }
            using (var stdout = new StreamReader(process.StandardOutput.BaseStream, Encoding.UTF8))
            {
                string? line;
                while ((line = stdout.ReadLine()) != null)
                {
                    var t = line.Trim();
                    if (t.Length > 0)
                        list.Add(t);
                }
            }
            process.WaitForExit(TimeSpan.FromSeconds(30));
        }
        return list;
    }

    private static string? _cachedPython;
    private static readonly object PythonCacheLock = new();

    /// <summary>Python 実行ファイル名を検出。結果は static キャッシュし、フォールバック時の重複検出を避ける。</summary>
    private static string? FindPython()
    {
        if (_cachedPython != null)
            return _cachedPython;
        lock (PythonCacheLock)
        {
            if (_cachedPython != null)
                return _cachedPython;
            var candidates = new[] { "python", "python3", "py" };
            foreach (var name in candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = name,
                        ArgumentList = { "-c", "import sys; sys.exit(0)" },
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        p?.WaitForExit(5000);
                        if (p?.ExitCode == 0)
                        {
                            _cachedPython = name;
                            return name;
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }
            return null;
        }
    }
}
