using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace NeoShell.Interop.Imaging;

/// <summary>Screenshots: what's on screen as pixels, onto the clipboard and into PNG files.</summary>
public static class ScreenCapture
{
    private static readonly Guid FOLDERID_Screenshots = new("b7bede81-df94-4682-a7d8-57a52620b86f");

    /// <summary>The pixels shown in <paramref name="area"/> (screen pixels), opaque.</summary>
    public static IconBitmap Capture(RectInt32 area)
    {
        nint screen = User32.GetDC(0);
        nint memory = Gdi32.CreateCompatibleDC(screen);
        nint bitmap = Gdi32.CreateCompatibleBitmap(screen, area.Width, area.Height);
        try
        {
            nint previous = Gdi32.SelectObject(memory, bitmap);
            bool copied = Gdi32.BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, Gdi32.SRCCOPY | Gdi32.CAPTUREBLT);
            Gdi32.SelectObject(memory, previous);
            if (!copied || IconBitmap.Read(bitmap) is not { } image)
                throw new Win32Exception("Could not copy the screen.");

            // The screen has no alpha; GDI leaves it at 0, which would make the picture transparent.
            for (int i = 3; i < image.Pixels.Length; i += 4)
                image.Pixels[i] = 255;
            return image;
        }
        finally
        {
            Gdi32.DeleteObject(bitmap);
            Gdi32.DeleteDC(memory);
            User32.ReleaseDC(0, screen);
        }
    }

    /// <summary>Puts the picture on the clipboard as a bitmap, which every app can paste.</summary>
    /// <param name="owner">A window of NeoShell's, which owns the clipboard while it's opened.</param>
    public static unsafe void CopyToClipboard(IconBitmap image, nint owner)
    {
        // A bottom-up DIB: some apps still get top-down ones upside down.
        int rowSize = image.Width * 4;
        nint memory = Kernel32.GlobalAlloc(Kernel32.GMEM_MOVEABLE, (nuint)(sizeof(Gdi32.BITMAPINFOHEADER) + rowSize * image.Height));
        if (memory == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        byte* data = (byte*)Kernel32.GlobalLock(memory);
        *(Gdi32.BITMAPINFOHEADER*)data = new Gdi32.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Gdi32.BITMAPINFOHEADER),
            biWidth = image.Width,
            biHeight = image.Height,
            biPlanes = 1,
            biBitCount = 32,
            biSizeImage = (uint)(rowSize * image.Height),
        };
        byte* pixels = data + sizeof(Gdi32.BITMAPINFOHEADER);
        for (int row = 0; row < image.Height; row++)
            Marshal.Copy(image.Pixels, row * rowSize, (nint)(pixels + (image.Height - 1 - row) * rowSize), rowSize);
        Kernel32.GlobalUnlock(memory);

        if (!User32.OpenClipboard(owner))
        {
            int error = Marshal.GetLastPInvokeError();
            Kernel32.GlobalFree(memory);
            throw new Win32Exception(error);
        }
        try
        {
            User32.EmptyClipboard();
            // On success the clipboard owns the memory.
            if (User32.SetClipboardData(User32.CF_DIB, memory) == 0)
            {
                int error = Marshal.GetLastPInvokeError();
                Kernel32.GlobalFree(memory);
                throw new Win32Exception(error);
            }
        }
        finally
        {
            User32.CloseClipboard();
        }
    }

    /// <summary>Writes the picture to a PNG file.</summary>
    public static async Task SavePngAsync(IconBitmap image, string path)
    {
        using var file = new FileStream(path, FileMode.CreateNew);
        using IRandomAccessStream stream = file.AsRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)image.Width, (uint)image.Height, 96, 96, image.Pixels);
        await encoder.FlushAsync();
    }

    /// <summary>Pictures\Screenshots, where Windows saves screenshots; created if it doesn't exist yet.</summary>
    public static unsafe string ScreenshotsFolder()
    {
        const uint KF_FLAG_CREATE = 0x8000;
        int result = Shell32.SHGetKnownFolderPath(FOLDERID_Screenshots, KF_FLAG_CREATE, 0, out char* path);
        if (result != 0)
            Marshal.ThrowExceptionForHR(result);
        try
        {
            return new string(path);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)path);
        }
    }
}
