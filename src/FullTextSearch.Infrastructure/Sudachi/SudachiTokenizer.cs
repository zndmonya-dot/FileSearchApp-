using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using FullTextSearch.Core;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;

namespace FullTextSearch.Infrastructure.Sudachi;

// SudachiPy（モード C）をサブプロセスで呼び出す Lucene Tokenizer。インデックス構築と検索ハイライトのトークン化に使用。
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
    /// <summary>Lucene の語（トークン）属性。</summary>
    private readonly ICharTermAttribute _termAttr;
    /// <summary>コンストラクタで渡された初期リーダー（<see cref="Reset"/> では基底の現在リーダーを優先）。</summary>
    private readonly TextReader _input;
    /// <summary><see cref="Reset"/> で Sudachi（またはフォールバック）が生成したトークン列。</summary>
    private List<string> _tokens = [];
    /// <summary><see cref="IncrementToken"/> が返す次のトークンのインデックス。</summary>
    private int _index;

    /// <summary>カスタム属性ファクトリと入力リーダーを指定して初期化する。</summary>
    public SudachiTokenizer(AttributeFactory factory, TextReader input)
        : base(factory, input)
    {
        _termAttr = AddAttribute<ICharTermAttribute>();
        _input = input;
    }

    /// <summary>既定の属性ファクトリで初期化する。</summary>
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
        if (_tokens == null || _index >= _tokens.Count)
            return false;
        var term = _tokens[_index];
        if (string.IsNullOrEmpty(term))
        {
            _index++;
            return IncrementToken();
        }
        // 最終防御: 1 トークンが Lucene 制限超なら文字境界で分割して出力（immense term エラー防止）
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

    /// <summary>1 ドキュメントあたり Sudachi に渡す最大文字数。超えると先頭のみ送りオーバーで落ちるのを防ぐ。</summary>
    private const int MaxInputCharsForTokenize = 500_000;

    /// <summary>
    /// SudachiPy 常駐プロセスのプール上限（同時並列数）。
    /// 1 プロセス ≈ 1 コアを占有するため、他アプリ稼働を想定して上限 2 に抑える（13 世代 i5 デスクトップ／16GB 想定）。
    /// 論理コアが少ない環境では ProcessorCount/4 に追従（最低 1）。
    /// </summary>
    public static readonly int PoolSize = Math.Max(1, Math.Min(2, Environment.ProcessorCount / 4));

    /// <summary>SudachiPy 常駐プロセスのプール（Borrow/Return）。<see cref="EnsurePool"/> 後に有効。</summary>
    private static BlockingCollection<SudachiProcessHandle>? _processPool;
    /// <summary><see cref="EnsurePool"/> の遅延初期化用ロック。</summary>
    private static readonly object PoolInitLock = new();

    /// <summary>1 つの常駐 SudachiPy プロセスとその専用ストリームをまとめるハンドル。</summary>
    private sealed class SudachiProcessHandle : IDisposable
    {
        public Process Process { get; }
        public StreamWriter StdIn => Process.StandardInput;
        public StreamReader StdOut => Process.StandardOutput;
        public bool IsAlive
        {
            get
            {
                try { return !Process.HasExited; } catch { return false; }
            }
        }
        public SudachiProcessHandle(Process p) { Process = p; }
        public void Dispose()
        {
            try { if (!Process.HasExited) Process.Kill(); } catch { /* ignore */ }
            try { Process.Dispose(); } catch { /* ignore */ }
        }
    }

    /// <summary><see cref="ResolveScriptPath"/> の結果キャッシュ用ロック。</summary>
    private static readonly object ScriptPathLock = new();
    /// <summary>検出済みの <c>sudachi_tokenize.py</c> パス（存在確認の繰り返しを避ける）。</summary>
    private static string? _cachedScriptPath;

    /// <summary><see cref="FindPython"/> の結果キャッシュ用ロック。</summary>
    private static readonly object PythonCacheLock = new();
    /// <summary>利用する Python コマンド名（<c>python</c> / <c>py</c> 等）。</summary>
    private static string? _cachedPython;

    /// <summary>ストリームモード 1 ドキュメントの読み取りタイムアウト（ミリ秒）。SudachiPy ハングを検知してプロセスを強制終了する。</summary>
    private const int StreamTimeoutMs = 60_000;

    /// <summary>ワンショットモードのタイムアウト（ミリ秒）。</summary>
    private const int OneshotTimeoutMs = 60_000;

    /// <summary>長い文字列を Lucene の制限以内に分割する。</summary>
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

    /// <summary>1 トークンが最大長を超える場合は分割して返す。</summary>
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

    /// <summary>リフレクション用: <see cref="Tokenizer"/> の内部入力フィールド（<see cref="GetCurrentReader"/> でキャッシュ）。</summary>
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

    /// <summary>リーダーから残りをすべて読み、1 文字列に連結する。</summary>
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

    /// <summary>高速化: インデックス初期化時に呼び、SudachiPy 常駐プロセスを事前起動する（プールを満たす）。最初のドキュメントからストリームモードが使える。</summary>
    public static void Warmup()
    {
        EnsurePool();
    }

    /// <summary>
    /// SudachiPy プロセスを 1 つ起動して <see cref="SudachiProcessHandle"/> として返す。失敗時は null。
    /// プール充填と障害時の補充の双方で使用する。
    /// </summary>
    private static SudachiProcessHandle? StartProcess(string scriptPath, string python)
    {
        try
        {
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
            var p = Process.Start(psi);
            if (p == null) return null;
            try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { /* 権限不足時は無視 */ }
            p.ErrorDataReceived += (_, _) => { };
            p.BeginErrorReadLine();
            return new SudachiProcessHandle(p);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// プールを遅延初期化する。スクリプト／Python を解決できなければ <see cref="_processPool"/> は null のまま（呼び出し側でワンショットにフォールバック）。
    /// 二重初期化を避けるため二重チェックロックで保護する。
    /// </summary>
    private static void EnsurePool()
    {
        if (_processPool != null) return;
        lock (PoolInitLock)
        {
            if (_processPool != null) return;
            var scriptPath = ResolveScriptPath();
            var python = FindPython();
            if (string.IsNullOrEmpty(scriptPath) || string.IsNullOrEmpty(python)) return;
            var pool = new BlockingCollection<SudachiProcessHandle>(PoolSize);
            for (int i = 0; i < PoolSize; i++)
            {
                var h = StartProcess(scriptPath!, python!);
                if (h != null) pool.Add(h);
            }
            if (pool.Count == 0) { pool.Dispose(); return; }
            _processPool = pool;
        }
    }

    /// <summary>
    /// プールから 1 つ Borrow し、使い終えたら Return する。失敗ハンドル（プロセス死／タイムアウト）は破棄して新規プロセスで補充する。
    /// </summary>
    private static List<string>? UseProcess(Func<SudachiProcessHandle, List<string>?> work)
    {
        EnsurePool();
        if (_processPool == null) return null;

        SudachiProcessHandle? handle = null;
        bool replace = false;
        try
        {
            // 取得タイムアウトはストリーム側のタイムアウトと同等。詰まったプロセスがあっても補充ロジックで自然回復する。
            if (!_processPool.TryTake(out handle, StreamTimeoutMs)) return null;
            if (handle == null) return null;

            if (!handle.IsAlive)
            {
                replace = true;
                return null;
            }

            try
            {
                return work(handle);
            }
            catch
            {
                replace = true;
                return null;
            }
        }
        finally
        {
            if (handle != null)
            {
                if (replace || !handle.IsAlive)
                {
                    handle.Dispose();
                    var scriptPath = ResolveScriptPath();
                    var python = FindPython();
                    if (!string.IsNullOrEmpty(scriptPath) && !string.IsNullOrEmpty(python))
                    {
                        var fresh = StartProcess(scriptPath!, python!);
                        if (fresh != null && _processPool != null)
                        {
                            try { _processPool.Add(fresh); } catch { fresh.Dispose(); }
                        }
                    }
                }
                else
                {
                    try { _processPool?.Add(handle); } catch { handle.Dispose(); }
                }
            }
        }
    }

    /// <summary>
    /// プール内の 1 プロセスを使ってストリームモードで 1 ドキュメントをトークン化。失敗時は null（呼び出し側でワンショットにフォールバック）。
    /// 並列インデックス追加時、複数スレッドが別プロセスで並行実行できる。
    /// </summary>
    private static List<string>? InvokeSudachiStream(string scriptPath, string text)
    {
        return UseProcess(handle =>
        {
            using var watchdog = new Timer(_ => { try { handle.Process.Kill(); } catch { /* ignore */ } }, null, StreamTimeoutMs, Timeout.Infinite);
            handle.StdIn.Write(text);
            handle.StdIn.Write('\n');
            handle.StdIn.Write(StreamDelim);
            handle.StdIn.Write('\n');
            handle.StdIn.Flush();

            var list = new List<string>();
            bool completed = false;
            string? line;
            while ((line = handle.StdOut.ReadLine()) != null)
            {
                var t = line.Trim();
                if (t == StreamDelim) { completed = true; break; }
                if (t.Length > 0) list.Add(t);
            }
            watchdog.Change(Timeout.Infinite, Timeout.Infinite);
            return completed ? list : null;
        });
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
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { /* 権限不足時は無視 */ }
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            using var watchdog = new Timer(_ => { try { process.Kill(); } catch { } }, null, OneshotTimeoutMs, Timeout.Infinite);
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
            watchdog.Change(Timeout.Infinite, Timeout.Infinite);
            process.WaitForExit(TimeSpan.FromSeconds(30));
        }
        return list;
    }

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
