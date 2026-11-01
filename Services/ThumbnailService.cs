using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ClipVault.Services;

/// <summary>Gets the Windows Explorer thumbnail using installed Windows media codecs.
/// If Windows cannot generate a thumbnail, the UI shows a placeholder.</summary>
public static class ThumbnailService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; public NativeSize(int w, int h) { Width = w; Height = h; } }

    [Flags]
    private enum ThumbnailFlags : int
    {
        ResizeToFit = 0,
        BiggerSizeOk = 0x1,
        MemoryOnly = 0x2,
        IconOnly = 0x4,
        ThumbnailOnly = 0x8,
        InCacheOnly = 0x10
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, ThumbnailFlags flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);

    public static BitmapSource? TryGet(string path)
    {
        IntPtr bitmap = IntPtr.Zero;
        IShellItemImageFactory? factory = null;
        try
        {
            if (!System.IO.File.Exists(path)) return null;
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (factory.GetImage(new NativeSize(400, 240), ThumbnailFlags.ThumbnailOnly, out bitmap) != 0 || bitmap == IntPtr.Zero)
                return null;
            var result = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            result.Freeze();
            return result;
        }
        catch { return null; }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (factory is not null && Marshal.IsComObject(factory)) Marshal.ReleaseComObject(factory);
        }
    }
}
