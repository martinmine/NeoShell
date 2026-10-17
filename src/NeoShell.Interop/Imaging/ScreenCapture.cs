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
    /// A window's own pixels, as DWM composes it, whatever covers it, cut to what's drawn of it (without the invisible
    /// resize borders), opaque. Blocking while the window's thread draws: call it off the UI thread.
    /// </summary>
    public static IconBitmap CaptureWindow(nint hwnd)
    {
        if (!User32.GetWindowRect(hwnd, out User32.RECT window))
            throw new Win32Exception("The window is gone.");
        RectInt32 bounds = window.ToRectInt32();
        RectInt32 visible = Windowing.TopLevelWindows.GetVisibleBounds(hwnd);
        nint screen = User32.GetDC(0);
        nint memory = Gdi32.CreateCompatibleDC(screen);
        nint bitmap = Gdi32.CreateCompatibleBitmap(screen, bounds.Width, bounds.Height);
        try
        {
            nint previous = Gdi32.SelectObject(memory, bitmap);
            bool printed = User32.PrintWindow(hwnd, memory, User32.PW_RENDERFULLCONTENT);
            Gdi32.SelectObject(memory, previous);
            if (!printed || IconBitmap.Read(bitmap) is not { } image)
                throw new Win32Exception("The window could not be drawn.");
            return Opaque(Crop(image, visible.X - bounds.X, visible.Y - bounds.Y, visible.Width, visible.Height));
        }
        finally
        {
            Gdi32.DeleteObject(bitmap);
            Gdi32.DeleteDC(memory);
            User32.ReleaseDC(0, screen);
        }
    }

    /// <summary>Part of a picture; the part is kept inside it.</summary>
    public static IconBitmap Crop(IconBitmap image, int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, image.Width);
        y = Math.Clamp(y, 0, image.Height);
        width = Math.Clamp(width, 0, image.Width - x);
        height = Math.Clamp(height, 0, image.Height - y);
        byte[] pixels = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
            Array.Copy(image.Pixels, ((y + row) * image.Width + x) * 4, pixels, row * width * 4, width * 4);
        return new IconBitmap(width, height, pixels);
    }

    private static IconBitmap Opaque(IconBitmap image)
    {
        for (int i = 3; i < image.Pixels.Length; i += 4)
            image.Pixels[i] = 255;
        return image;
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

    /// <summary>The picture as an opaque PNG file's bytes, scaled down (Fant, as photo viewers do) to the size given.</summary>
    public static async Task<byte[]> EncodeScaledPngAsync(IconBitmap image, int width, int height)
    {
        using var memory = new MemoryStream();
        await WritePngAsync(image, memory, transparent: false, width, height);
        return memory.ToArray();
    }

    private static async Task WritePngAsync(IconBitmap image, Stream target, bool transparent, int? width = null, int? height = null)
    {
        using IRandomAccessStream stream = target.AsRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8, transparent ? BitmapAlphaMode.Premultiplied : BitmapAlphaMode.Ignore,
            (uint)image.Width, (uint)image.Height, 96, 96, image.Pixels);
        if (width is { } w && height is { } h && (w != image.Width || h != image.Height))
        {
            encoder.BitmapTransform.ScaledWidth = (uint)w;
            encoder.BitmapTransform.ScaledHeight = (uint)h;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
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
