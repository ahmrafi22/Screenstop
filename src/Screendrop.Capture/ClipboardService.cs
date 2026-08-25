using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Screendrop.Capture;

public static class ClipboardService
{
    private const int DibHeaderSize = 40;

    private static uint? _pngFormat;

    public static void SetImage(SKBitmap bitmap)
    {
        if (bitmap is null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        uint pngFormat = _pngFormat ??= NativeMethods.RegisterClipboardFormatW("PNG");

        byte[] pngBytes = EncodePng(bitmap);
        byte[] dibBytes = BuildDib(bitmap);

        if (!TryOpenClipboard(out int attempts))
        {
            throw new InvalidOperationException($"Could not open the clipboard after {attempts} attempts.");
        }

        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                throw new InvalidOperationException("Could not clear the clipboard.");
            }

            SetDataBlock(pngFormat, pngBytes);
            SetDataBlock(NativeMethods.CF_DIB, dibBytes);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool TryOpenClipboard(out int attempts)
    {
        for (attempts = 1; attempts <= 20; attempts++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static void SetDataBlock(uint format, byte[] bytes)
    {
        IntPtr handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("GlobalAlloc failed.");
        }

        try
        {
            IntPtr ptr = NativeMethods.GlobalLock(handle);
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException("GlobalLock failed.");
            }

            try
            {
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }

            if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
            {
                throw new InvalidOperationException($"SetClipboardData failed for format {format}.");
            }
        }
        catch
        {
            NativeMethods.GlobalFree(handle);
            throw;
        }
    }

    internal static byte[] BuildDib(SKBitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int rowBytes = checked(width * 4);

        var dib = new byte[DibHeaderSize + (rowBytes * height)];

        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0, 4), DibHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8, 4), height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14, 2), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16, 4), 0);

        var pixels = new byte[bitmap.ByteCount];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);

        for (int y = 0; y < height; y++)
        {
            int sourceRow = (height - 1 - y) * bitmap.RowBytes;
            int targetRow = DibHeaderSize + (y * rowBytes);
            Array.Copy(pixels, sourceRow, dib, targetRow, rowBytes);
        }

        return dib;
    }
}
