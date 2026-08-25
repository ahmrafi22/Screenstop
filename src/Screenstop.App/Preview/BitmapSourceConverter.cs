using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace Screenstop.App.Preview;

internal static class BitmapSourceConverter
{
    public static BitmapSource? FromFile(string path, int maxDimension)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(path);
            if (bitmap is null)
            {
                return null;
            }

            double scale = Math.Min(1.0, (double)maxDimension / Math.Max(bitmap.Width, bitmap.Height));
            int width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            int height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using var resized = bitmap.Resize(info, SKFilterQuality.Medium);
            if (resized is null)
            {
                return null;
            }

            var writeable = new WriteableBitmap(resized.Width, resized.Height, 96, 96, PixelFormats.Bgra32, null);
            writeable.Lock();
            try
            {
                var buffer = new byte[resized.ByteCount];
                Marshal.Copy(resized.GetPixels(), buffer, 0, buffer.Length);
                Marshal.Copy(buffer, 0, writeable.BackBuffer, buffer.Length);
                writeable.AddDirtyRect(new Int32Rect(0, 0, writeable.PixelWidth, writeable.PixelHeight));
            }
            finally
            {
                writeable.Unlock();
            }

            return writeable;
        }
        catch
        {
            return null;
        }
    }
}
