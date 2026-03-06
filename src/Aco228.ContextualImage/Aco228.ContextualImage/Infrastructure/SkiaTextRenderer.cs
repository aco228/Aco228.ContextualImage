using System.Runtime.InteropServices;
using System.Text;
using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Infrastructure;

public static class SkiaTextRenderer
{
    public static void DrawTextWithBackground(
        Mat mat,
        Rect textRect,
        string text,
        SKColor textColor,
        SKColor backgroundColor,
        float cornerRadius = 8f,
        int padding = 12,
        float blurSigma = 12f)
    {
        using var skBitmap = MatToSkBitmap(mat);

        // Snapshot original BEFORE any drawing
        using var originalBitmap = skBitmap.Copy();

        using var canvas = new SKCanvas(skBitmap);
        var font = FontManager.TakeNext();

        using var paint = new SKPaint
        {
            Color = textColor,
            IsAntialias = true,
            Typeface = font,
        };

        paint.TextSize = FitFontSize(paint, text, textRect.Width - padding * 2, textRect.Height - padding * 2);
        var lines = WrapText(paint, text, textRect.Width - padding * 2);

        float lineHeight = paint.TextSize * 1.3f;
        float totalTextHeight = lines.Count * lineHeight;
        float startY = textRect.Y + (textRect.Height - totalTextHeight) / 2f;

        // First pass — per-line blur + overlay, always from original snapshot
        float y = startY;
        foreach (var line in lines)
        {
            float lineWidth = paint.MeasureText(line);
            float bgX = textRect.X + (textRect.Width - lineWidth) / 2f - padding;
            float bgY = y;
            float bgW = lineWidth + padding * 2;
            float bgH = lineHeight;

            var lineRect = SKRect.Create(bgX, bgY, bgW, bgH);

            // Clip to this line's rect so blur doesn't leak into adjacent lines
            canvas.Save();
            canvas.ClipRect(lineRect);

            // Blur from original snapshot (not the already-modified bitmap)
            using var blurFilter = SKImageFilter.CreateBlur(blurSigma, blurSigma);
            using var blurPaint = new SKPaint { ImageFilter = blurFilter };
            canvas.DrawBitmap(originalBitmap, 0, 0, blurPaint);

            canvas.Restore();

            // Overlay on top of blur
            using var overlayPaint = new SKPaint
            {
                Color = new SKColor(
                    backgroundColor.Red,
                    backgroundColor.Green,
                    backgroundColor.Blue,
                    220
                ),
                IsAntialias = true
            };
            canvas.DrawRoundRect(bgX, bgY, bgW, bgH, cornerRadius, cornerRadius, overlayPaint);

            y += lineHeight;
        }

        // Second pass — draw text
        y = startY;
        foreach (var line in lines)
        {
            float lineWidth = paint.MeasureText(line);
            float textX = textRect.X + (textRect.Width - lineWidth) / 2f;
            canvas.DrawText(line, textX, y + paint.TextSize, paint);
            y += lineHeight;
        }

        canvas.Flush();
        SkBitmapToMat(skBitmap, mat);
    }

    private static float FitFontSize(SKPaint paint, string text, float maxWidth, float maxHeight)
    {
        float fontSize = 60f;
        while (fontSize > 10)
        {
            paint.TextSize = fontSize;
            var lines = WrapText(paint, text, maxWidth);
            float totalHeight = lines.Count * fontSize * 1.3f;
            float maxLineWidth = lines.Max(l => paint.MeasureText(l));
            if (maxLineWidth <= maxWidth && totalHeight <= maxHeight)
                break;
            fontSize -= 1f;
        }
        return fontSize;
    }

    private static List<string> WrapText(SKPaint paint, string text, float maxWidth)
    {
        var lines = new List<string>();

        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ');
            var current = new StringBuilder();

            foreach (var word in words)
            {
                string test = current.Length == 0 ? word : $"{current} {word}";
                if (paint.MeasureText(test) > maxWidth && current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    current.Append(word);
                }
                else
                {
                    current.Clear();
                    current.Append(test);
                }
            }

            if (current.Length > 0)
                lines.Add(current.ToString());
        }

        return lines;
    }
    
    private static unsafe SKBitmap MatToSkBitmap(Mat mat)
    {
        var bitmap = new SKBitmap(mat.Width, mat.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var matBgra = new Mat();
        Cv2.CvtColor(mat, matBgra, ColorConversionCodes.BGR2BGRA);
        long byteCount = mat.Width * mat.Height * 4;
        Buffer.MemoryCopy((void*)matBgra.Data, (void*)bitmap.GetPixels(), byteCount, byteCount);
        return bitmap;
    }

    private static unsafe void SkBitmapToMat(SKBitmap bitmap, Mat mat)
    {
        using var matBgra = new Mat(mat.Size(), MatType.CV_8UC4);
        long byteCount = mat.Width * mat.Height * 4;
        Buffer.MemoryCopy((void*)bitmap.GetPixels(), (void*)matBgra.Data, byteCount, byteCount);
        Cv2.CvtColor(matBgra, mat, ColorConversionCodes.BGRA2BGR);
    }
}