using FullTextSearch.Infrastructure.Sudachi;
using Xunit;

namespace FullTextSearch.Tests;

public class SudachiNativeTests
{
    [Fact]
    public void Native_tokenize_mode_C_returns_surfaces()
    {
        if (!SudachiNative.TryEnsureInitialized())
        {
            // CI / 開発環境で DLL・辞書未ビルドの場合はスキップ相当（テスト失敗にしない）
            return;
        }

        using var ctx = new SudachiContextScope();
        Assert.NotEqual(IntPtr.Zero, ctx.Handle);

        var tokens = SudachiNative.Tokenize(ctx.Handle, "東京都に行きました");
        Assert.NotEmpty(tokens);
        Assert.Contains(tokens, t => t.Contains("東京", StringComparison.Ordinal) || t.Contains('東'));
    }

    private sealed class SudachiContextScope : IDisposable
    {
        public IntPtr Handle { get; } = SudachiNative.CreateContext();
        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                SudachiNative.DestroyContext(Handle);
        }
    }
}
