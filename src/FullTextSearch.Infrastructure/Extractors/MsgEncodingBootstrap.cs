// MsgReader 等が Windows-1252 / Shift_JIS 等を使う前にコードページを登録する。
using System.Text;

namespace FullTextSearch.Infrastructure.Extractors;

/// <summary>
/// .NET Core 以降では <see cref="Encoding.GetEncoding(string)"/> で Windows コードページを使う前に
/// <see cref="CodePagesEncodingProvider"/> の登録が必要。
/// </summary>
public static class MsgEncodingBootstrap
{
    private static int _registered;

    /// <summary>Windows コードページ（1252 / Shift_JIS 等）を .NET ランタイムへ登録する。</summary>
    public static void EnsureRegistered()
    {
        if (Interlocked.CompareExchange(ref _registered, 1, 0) != 0)
            return;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
