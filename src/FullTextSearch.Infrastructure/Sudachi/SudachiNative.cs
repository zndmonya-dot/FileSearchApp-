using System.Runtime.InteropServices;
using System.Text;

namespace FullTextSearch.Infrastructure.Sudachi;

/// <summary>
/// Sudachi ネイティブ DLL（sudachi.rs / モード C）への P/Invoke。
/// 1 コンテキスト = 1 スレッド専用（Sudachi Tokenizer はスレッドセーフではない）。
/// </summary>
internal static class SudachiNative
{
    private const string DllName = "sudachi_ffi";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern IntPtr sudachi_create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string resourceDir,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dictPath);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void sudachi_destroy(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int sudachi_tokenize(
        IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
        byte[]? outBuf,
        nuint outCap,
        out nuint outLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr sudachi_last_error();

    /// <summary>辞書・設定ファイルのディレクトリ（char.def / sudachi.json / system_core.dic）。</summary>
    internal static string ResourceDirectory { get; private set; } = "";

    internal static string DictionaryPath { get; private set; } = "";

    private static readonly object InitLock = new();
    private static bool _initialized;
    private static string? _initError;

    /// <summary>ネイティブ DLL と辞書パスを解決する。失敗時は <see cref="_initError"/> に理由を格納。</summary>
    internal static bool TryEnsureInitialized()
    {
        if (_initialized) return _initError == null;
        lock (InitLock)
        {
            if (_initialized) return _initError == null;
            _initialized = true;
            try
            {
                var (resDir, dictPath) = ResolvePaths();
                ResourceDirectory = resDir;
                DictionaryPath = dictPath;
                if (!File.Exists(Path.Combine(resDir, "char.def")))
                    throw new FileNotFoundException("Sudachi char.def not found", Path.Combine(resDir, "char.def"));
                if (!File.Exists(dictPath))
                    throw new FileNotFoundException("Sudachi dictionary not found", dictPath);
                NativeLibrary.Load(DllName, typeof(SudachiNative).Assembly, DllImportSearchPath.ApplicationDirectory);
            }
            catch (Exception ex)
            {
                _initError = ex.Message;
            }
        }
        return _initError == null;
    }

    internal static string? InitializationError => _initError;

    internal static IntPtr CreateContext()
    {
        if (!TryEnsureInitialized())
            return IntPtr.Zero;
        return sudachi_create(ResourceDirectory, DictionaryPath);
    }

    internal static void DestroyContext(IntPtr ctx)
    {
        if (ctx != IntPtr.Zero)
            sudachi_destroy(ctx);
    }

    internal static List<string> Tokenize(IntPtr ctx, string text)
    {
        if (ctx == IntPtr.Zero || string.IsNullOrEmpty(text))
            return [];

        nuint required;
        var code = sudachi_tokenize(ctx, text, null, 0, out required);
        if (code != -4)
            ThrowOnError(code);

        var buf = new byte[(int)required];
        code = sudachi_tokenize(ctx, text, buf, (nuint)buf.Length, out required);
        if (code != 0)
            ThrowOnError(code);

        var payload = Encoding.UTF8.GetString(buf, 0, (int)required - 1);
        if (payload.Length == 0)
            return [];

        var lines = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length > 0)
                list.Add(t);
        }
        return list;
    }

    private static void ThrowOnError(int code)
    {
        var msg = Marshal.PtrToStringUTF8(sudachi_last_error()) ?? $"sudachi_tokenize failed ({code})";
        throw new InvalidOperationException(msg);
    }

    private static (string resourceDir, string dictPath) ResolvePaths()
    {
        var baseDir = AppContext.BaseDirectory ?? "";
        var candidates = new[]
        {
            Path.Combine(baseDir, "sudachi", "resources"),
            Path.Combine(baseDir, "tools", "sudachi", "resources"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "tools", "sudachi", "resources")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tools", "sudachi", "resources")),
        };
        foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;
            var dict = Path.Combine(dir, "system_core.dic");
            if (File.Exists(dict))
                return (dir, dict);
        }
        var fallback = Path.GetFullPath(Path.Combine(baseDir, "sudachi", "resources"));
        return (fallback, Path.Combine(fallback, "system_core.dic"));
    }
}
