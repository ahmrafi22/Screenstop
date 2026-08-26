using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Screendrop.Core.Annotations;
using Screendrop.Rendering;
using SkiaSharp;

namespace Screendrop.App.Preview;

internal static class BitmapSourceConverter
{
    public static BitmapSource? FromFile(string path, int maxDimension)
    {
        try
        {
            using var bitmap = DecodeDownsampled(path, maxDimension);
            return bitmap is null ? null : ToBitmapSource(bitmap);
        }
        catch
        {
            return null;
        }
    }

    /// Like FromFile, but applies the image's annotation sidecar (if any) so
    /// the card thumbnail reflects the user's edits. Composites at thumbnail
    /// resolution - normalized coordinates make that safe and cheap.
    public static BitmapSource? FromFileComposited(string path, int maxDimension)
    {
        try
        {
            using var bitmap = DecodeDownsampled(path, maxDimension);
            if (bitmap is null)
            {
                return null;
            }

            var document = AnnotationDocument.Load(path);
            if (document is null || document.Annotations.Count == 0)
            {
                return ToBitmapSource(bitmap);
            }

            using var composited = AnnotationRenderer.Render(bitmap, document);
            return ToBitmapSource(composited);
        }
        catch
        {
            return null;
        }
    }

    /// Mac parity (ScreenshotImageLoader.downsampledImage): produce a bitmap
    /// no larger than maxDimension. Codecs with native scaling (JPEG, WebP)
    /// decode at a reduced scale directly; codecs without it (PNG - which is
    /// what the pipeline stages) decode full size and scale down afterwards.
    private static SKBitmap? DecodeDownsampled(string path, int maxDimension)
    {
        using (var stream = File.OpenRead(path))
        using (var codec = SKCodec.Create(stream))
        {
            if (codec is not null)
            {
                var fullInfo = codec.Info;
                if (fullInfo.Width > 0 && fullInfo.Height > 0)
                {
                    var scaledSize = codec.GetScaledDimensions((float)maxDimension / Math.Max(fullInfo.Width, fullInfo.Height));
                    var targetInfo = new SKImageInfo(scaledSize.Width, scaledSize.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                    var scaled = new SKBitmap(targetInfo);

                    var result = codec.GetPixels(targetInfo, scaled.GetPixels());
                    if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
                    {
                        if (Math.Max(scaled.Width, scaled.Height) <= maxDimension)
                        {
                            return scaled;
                        }

                        // The codec's smallest supported scale is still too
                        // large; resize the scaled decode down to size.
                        using (scaled)
                        {
                            return Resize(scaled, maxDimension);
                        }
                    }

                    scaled.Dispose();
                }
            }
        }

        // No codec scaling available (PNG): full decode, then resize.
        using var full = SKBitmap.Decode(path);
        if (full is null)
        {
            return null;
        }

        if (Math.Max(full.Width, full.Height) <= maxDimension)
        {
            // Hand back an owned copy sized for the card; the caller disposes.
            return full.Copy();
        }

        return Resize(full, maxDimension);
    }

    private static SKBitmap? Resize(SKBitmap source, int maxDimension)
    {
        double scale = (double)maxDimension / Math.Max(source.Width, source.Height);
        var info = new SKImageInfo(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)),
            SKColorType.Bgra8888,
            SKAlphaType.Opaque);
        return source.Resize(info, SKFilterQuality.Medium);
    }

    private static BitmapSource ToBitmapSource(SKBitmap bitmap)
    {
        var writeable = new WriteableBitmap(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null);
        writeable.Lock();
        try
        {
            var buffer = new byte[bitmap.ByteCount];
            Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);
            Marshal.Copy(buffer, 0, writeable.BackBuffer, buffer.Length);
            writeable.AddDirtyRect(new Int32Rect(0, 0, writeable.PixelWidth, writeable.PixelHeight));
        }
        finally
        {
            writeable.Unlock();
        }

        return writeable;
    }
}
