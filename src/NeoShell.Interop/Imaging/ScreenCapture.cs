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

    /// <summary>
    /// Puts the picture on the clipboard as a bitmap, which every app can paste, and with <paramref name="png"/> as a
    /// PNG too, as Snipping Tool puts its snips (apps that take PNG keep a freeform snip's transparency).
    /// </summary>
    /// <param name="owner">A window of NeoShell's, which owns the clipboard while it's opened.</param>
    public static unsafe void CopyToClipboard(IconBitmap image, nint owner, byte[]? png = null)
    {
        // A bottom-up DIB: some apps still get top-down ones upside down.
        int rowSize = image.Width * 4;
        nint dib = Allocate(sizeof(Gdi32.BITMAPINFOHEADER) + rowSize * image.Height);
        byte* data = (byte*)Kernel32.GlobalLock(dib);
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
        Kernel32.GlobalUnlock(dib);

        nint pngData = 0;
        if (png is not null)
        {
            pngData = Allocate(png.Length);
            Marshal.Copy(png, 0, (nint)Kernel32.GlobalLock(pngData), png.Length);
            Kernel32.GlobalUnlock(pngData);
        }

        if (!User32.OpenClipboard(owner))
        {
            int error = Marshal.GetLastPInvokeError();
            Kernel32.GlobalFree(dib);
            if (pngData != 0)
                Kernel32.GlobalFree(pngData);
            throw new Win32Exception(error);
        }
        try
        {
            User32.EmptyClipboard();
            Set(User32.CF_DIB, dib);
            if (pngData != 0)
            {
                nint handedOver = pngData;
                pngData = 0;
                Set(User32.RegisterClipboardFormat("PNG"), handedOver);
            }
        }
        finally
        {
            User32.CloseClipboard();
            if (pngData != 0)
                Kernel32.GlobalFree(pngData);
        }
    }

    private static nint Allocate(int size)
    {
        nint memory = Kernel32.GlobalAlloc(Kernel32.GMEM_MOVEABLE, (nuint)size);
        return memory != 0 ? memory : throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    // On success the clipboard owns the memory.
    private static void Set(uint format, nint memory)
    {
        if (User32.SetClipboardData(format, memory) != 0)
            return;
        int error = Marshal.GetLastPInvokeError();
        Kernel32.GlobalFree(memory);
        throw new Win32Exception(error);
    }

    /// <summary>Writes the picture to a PNG file; <paramref name="transparent"/> keeps its (premultiplied) alpha.</summary>
    public static async Task SavePngAsync(IconBitmap image, string path, bool transparent = false)
    {
        using var file = new FileStream(path, FileMode.CreateNew);
        await WritePngAsync(image, file, transparent);
    }

    /// <summary>The picture as a PNG file's bytes; <paramref name="transparent"/> keeps its (premultiplied) alpha.</summary>
    public static async Task<byte[]> EncodePngAsync(IconBitmap image, bool transparent)
    {
        using var memory = new MemoryStream();
        await WritePngAsync(image, memory, transparent);
        return memory.ToArray();
    }

    private static async Task WritePngAsync(IconBitmap image, Stream target, bool transparent)
    {
        using IRandomAccessStream stream = target.AsRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8, transparent ? BitmapAlphaMode.Premultiplied : BitmapAlphaMode.Ignore,
            (uint)image.Width, (uint)image.Height, 96, 96, image.Pixels);
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
