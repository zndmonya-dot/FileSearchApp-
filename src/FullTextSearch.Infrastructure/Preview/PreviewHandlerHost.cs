using System.Runtime.InteropServices;

namespace FullTextSearch.Infrastructure.Preview;

/// <summary>
/// Windows Shell Preview Handler をホストするためのクラス
/// </summary>
public class PreviewHandlerHost : IDisposable
{
    private IPreviewHandler? _previewHandler;
    private bool _disposed;

    // COM GUID for IPreviewHandler
    private static readonly Guid IPreviewHandlerGuid = new("8895b1c6-b41f-4c1c-a562-0d564250836f");

    /// <summary>
    /// プレビューハンドラを初期化してファイルを表示
    /// </summary>
    public bool Initialize(string filePath, IntPtr hwnd, RECT bounds)
    {
        try
        {
            Unload();

            var extension = Path.GetExtension(filePath);
            var clsid = GetPreviewHandlerCLSID(extension);

            if (clsid == Guid.Empty)
            {
                return false;
            }

            // COMオブジェクトを作成
            var type = Type.GetTypeFromCLSID(clsid);
            if (type == null)
            {
                return false;
            }

            var obj = Activator.CreateInstance(type);
            if (obj == null)
            {
                return false;
            }

            _previewHandler = obj as IPreviewHandler;
            if (_previewHandler == null)
            {
                Marshal.ReleaseComObject(obj);
                return false;
            }

            // ファイルを初期化
            if (obj is IInitializeWithFile initWithFile)
            {
                initWithFile.Initialize(filePath, 0);
            }
            else if (obj is IInitializeWithStream initWithStream)
            {
                var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var comStream = new ManagedIStream(stream);
                initWithStream.Initialize(comStream, 0);
            }
            else
            {
                Unload();
                return false;
            }

            // ウィンドウに関連付け
            _previewHandler.SetWindow(hwnd, ref bounds);
            _previewHandler.DoPreview();

            return true;
        }
        catch (Exception)
        {
            Unload();
            return false;
        }
    }

    /// <summary>
    /// プレビュー領域を更新
    /// </summary>
    public void SetBounds(RECT bounds)
    {
        _previewHandler?.SetRect(ref bounds);
    }

    /// <summary>
    /// プレビューをアンロード
    /// </summary>
    public void Unload()
    {
        if (_previewHandler != null)
        {
            try
            {
                _previewHandler.Unload();
                Marshal.ReleaseComObject(_previewHandler);
            }
            catch
            {
                // Ignore errors during unload
            }
            _previewHandler = null;
        }
    }

    /// <summary>
    /// 拡張子に対応するプレビューハンドラのCLSIDを取得
    /// </summary>
    private static Guid GetPreviewHandlerCLSID(string extension)
    {
        try
        {
            // レジストリからプレビューハンドラを検索
            using var extKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(extension);
            if (extKey == null)
            {
                return Guid.Empty;
            }

            // ShellExからプレビューハンドラを探す
            using var shellExKey = extKey.OpenSubKey("ShellEx");
            if (shellExKey == null)
            {
                // ProgIDを経由して探す
                var progId = extKey.GetValue(null) as string;
                if (string.IsNullOrEmpty(progId))
                {
                    return Guid.Empty;
                }

                using var progIdKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(progId);
                using var progIdShellEx = progIdKey?.OpenSubKey("ShellEx");
                using var progIdPreviewHandler = progIdShellEx?.OpenSubKey("{8895b1c6-b41f-4c1c-a562-0d564250836f}");
                var progIdClsid = progIdPreviewHandler?.GetValue(null) as string;

                return string.IsNullOrEmpty(progIdClsid) ? Guid.Empty : new Guid(progIdClsid);
            }

            using var previewHandlerKey = shellExKey.OpenSubKey("{8895b1c6-b41f-4c1c-a562-0d564250836f}");
            var clsidString = previewHandlerKey?.GetValue(null) as string;

            return string.IsNullOrEmpty(clsidString) ? Guid.Empty : new Guid(clsidString);
        }
        catch
        {
            return Guid.Empty;
        }
    }

    /// <summary>
    /// 指定した拡張子がプレビュー可能かどうか
    /// </summary>
    public static bool CanPreview(string extension)
    {
        return GetPreviewHandlerCLSID(extension) != Guid.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Unload();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

#region COM Interfaces

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("8895b1c6-b41f-4c1c-a562-0d564250836f")]
public interface IPreviewHandler
{
    void SetWindow(IntPtr hwnd, ref RECT rect);
    void SetRect(ref RECT rect);
    void DoPreview();
    void Unload();
    void SetFocus();
    void QueryFocus(out IntPtr phwnd);
    [PreserveSig]
    uint TranslateAccelerator(ref MSG pmsg);
}

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public POINT pt;
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("b7d14566-0509-4cce-a71f-0a554233bd9b")]
public interface IInitializeWithFile
{
    void Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
public interface IInitializeWithStream
{
    void Initialize(IStream pstream, uint grfMode);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("0000000c-0000-0000-C000-000000000046")]
public interface IStream
{
    void Read([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] pv, uint cb, out uint pcbRead);
    void Write([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] pv, uint cb, out uint pcbWritten);
    void Seek(long dlibMove, uint dwOrigin, out long plibNewPosition);
    void SetSize(long libNewSize);
    void CopyTo(IStream pstm, long cb, out long pcbRead, out long pcbWritten);
    void Commit(uint grfCommitFlags);
    void Revert();
    void LockRegion(long libOffset, long cb, uint dwLockType);
    void UnlockRegion(long libOffset, long cb, uint dwLockType);
    void Stat(out STATSTG pstatstg, uint grfStatFlag);
    void Clone(out IStream ppstm);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct STATSTG
{
    public string pwcsName;
    public uint type;
    public long cbSize;
    public System.Runtime.InteropServices.ComTypes.FILETIME mtime;
    public System.Runtime.InteropServices.ComTypes.FILETIME ctime;
    public System.Runtime.InteropServices.ComTypes.FILETIME atime;
    public uint grfMode;
    public uint grfLocksSupported;
    public Guid clsid;
    public uint grfStateBits;
    public uint reserved;
}

/// <summary>
/// .NETのStreamをIStreamにラップするクラス
/// </summary>
public class ManagedIStream : IStream
{
    private readonly Stream _stream;

    public ManagedIStream(Stream stream)
    {
        _stream = stream;
    }

    public void Read(byte[] pv, uint cb, out uint pcbRead)
    {
        pcbRead = (uint)_stream.Read(pv, 0, (int)cb);
    }

    public void Write(byte[] pv, uint cb, out uint pcbWritten)
    {
        _stream.Write(pv, 0, (int)cb);
        pcbWritten = cb;
    }

    public void Seek(long dlibMove, uint dwOrigin, out long plibNewPosition)
    {
        plibNewPosition = _stream.Seek(dlibMove, (SeekOrigin)dwOrigin);
    }

    public void SetSize(long libNewSize)
    {
        _stream.SetLength(libNewSize);
    }

    public void CopyTo(IStream pstm, long cb, out long pcbRead, out long pcbWritten)
    {
        throw new NotImplementedException();
    }

    public void Commit(uint grfCommitFlags)
    {
        _stream.Flush();
    }

    public void Revert()
    {
        throw new NotImplementedException();
    }

    public void LockRegion(long libOffset, long cb, uint dwLockType)
    {
        throw new NotImplementedException();
    }

    public void UnlockRegion(long libOffset, long cb, uint dwLockType)
    {
        throw new NotImplementedException();
    }

    public void Stat(out STATSTG pstatstg, uint grfStatFlag)
    {
        pstatstg = new STATSTG
        {
            cbSize = _stream.Length,
            type = 2 // STGTY_STREAM
        };
    }

    public void Clone(out IStream ppstm)
    {
        throw new NotImplementedException();
    }
}

#endregion

