using Aco228.ContextualImage.Models;
using SkiaSharp;

namespace Aco228.ContextualImage.Infrastructure;

public static class SkiaTextHelper
{
    public static void DrawTextGradient(SKCanvas canvas, SKRect textBounds, float dimAmount, bool isBottomText,
        int imageHeight)
    {
        if (dimAmount <= 0f) return;

        SKRect gradientRect = new SKRect(0, 0, canvas.LocalClipBounds.Width, canvas.LocalClipBounds.Height);
        SKPoint startPoint, endPoint;

        if (isBottomText)
        {
            startPoint = new SKPoint(0, canvas.LocalClipBounds.Height);
            endPoint   = new SKPoint(0, textBounds.Top);
        }
        else
        {
            startPoint = new SKPoint(0, 0);
            endPoint   = new SKPoint(0, textBounds.Bottom);
        }

        using var shader = SKShader.CreateLinearGradient(
            startPoint, endPoint,
            new[] { SKColors.Black.WithAlpha((byte)(dimAmount * 255)), SKColors.Transparent },
            null, SKShaderTileMode.Clamp);

        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawRect(gradientRect, paint);
    }

    public static void DrawText(SKCanvas canvas, TextElement element, TextRenderOptions options)
    {
        float fontSize = FitFontSize(element, options);
        fontSize = Math.Max(fontSize, element.MinimumFontSize);

        using var font = new SKFont(element.Font, fontSize);
        font.Edging = SKFontEdging.SubpixelAntialias;

        var lines = BreakIntoLines(element.Text, font, options.Bounds.Width);
        float lineHeight = fontSize * options.LineHeightMultiplier;
        float totalHeight = lines.Count * lineHeight;
        float startY = options.Bounds.MidY - totalHeight / 2f + fontSize;

        var linePositions = lines.Select((line, i) =>
        {
            float y = startY + i * lineHeight;
            float x = options.HorizontalAlign switch
            {
                SKTextAlign.Left  => options.Bounds.Left,
                SKTextAlign.Right => options.Bounds.Right,
                _                 => options.Bounds.MidX
            };
            return (line, x, y);
        }).ToList();

        // Pass 1: background
        if (element.Background != null)
        {
            float padX   = font.Size * element.Background.PaddingX;
            float padY   = font.Size * element.Background.PaddingY;
            float radius = font.Size * element.Background.CornerRadius;
            float blur   = font.Size * element.Background.BackdropBlur;

            using var path = new SKPath();
            foreach (var (line, x, y) in linePositions)
            {
                SKRect lineBounds   = MeasureLineBounds(line, font, x, y, options.HorizontalAlign);
                SKRect paddedBounds = SKRect.Inflate(lineBounds, padX, padY);
                path.AddRoundRect(paddedBounds, radius, radius);
            }

            if (blur > 0f)
            {
                using var snapshot = canvas.Surface?.Snapshot();
                if (snapshot != null)
                {
                    using var blurFilter = SKImageFilter.CreateBlur(blur, blur);
                    using var blurPaint  = new SKPaint { ImageFilter = blurFilter };
                    canvas.Save();
                    canvas.ClipPath(path, SKClipOperation.Intersect, true);
                    canvas.DrawImage(snapshot, 0, 0, blurPaint);
                    canvas.Restore();
                }
            }

            path.FillType = SKPathFillType.Winding;
            var bgColor = element.Background.Color.WithAlpha((byte)(element.Background.Opacity * 255));
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            canvas.DrawPath(path, bgPaint);
        }

        // Pass 2: all shadows
        if (element.ShadowRadius.HasValue && element.ShadowColor.HasValue)
        {
            foreach (var (line, x, y) in linePositions)
                DrawShadow(canvas, line, font, x, y, options.HorizontalAlign,
                    element.ShadowRadius.Value, element.ShadowColor.Value);
        }

        // Pass 3: all outlines
        if (element.OutlineWidth.HasValue && element.OutlineColor.HasValue)
        {
            foreach (var (line, x, y) in linePositions)
                DrawOutline(canvas, line, font, x, y, options.HorizontalAlign,
                    element.OutlineWidth.Value, element.OutlineColor.Value);
        }

        // Pass 4: all fills
        foreach (var (line, x, y) in linePositions)
            DrawFill(canvas, line, font, x, y, options.HorizontalAlign, element.Color);
    }

    private static void DrawFill(SKCanvas canvas, string line, SKFont font,
        float x, float y, SKTextAlign align, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(line, x, y, align, font, paint);
    }

    private static void DrawOutline(SKCanvas canvas, string line, SKFont font,
        float x, float y, SKTextAlign align, float outlineWidth, SKColor outlineColor)
    {
        // Hard-code your desired inner color here (change this!)
        // Use the color your text normally has without outline.
        // White or black are common for overlaid text; adjust as needed.
        SKColor innerColor = SKColors.White;   // ← EDIT THIS to your actual fill color

        // Stroke paint – switch to Round join (this kills most spikes)
        using var strokePaint = new SKPaint
        {
            Color       = outlineColor,
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = outlineWidth * font.Size,
            StrokeJoin  = SKStrokeJoin.Round,     // ← This is the key change (Round vs Miter)
            StrokeCap   = SKStrokeCap.Round,      // Helps smooth tiny protrusions
            StrokeMiter = 1f,                     // Lower limit even if using Round (safety)
        };

        // Fill paint to cover internal artifacts
        using var fillPaint = new SKPaint
        {
            Color       = innerColor,
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
        };

        // Draw order: thick outline first, then sharp fill on top
        canvas.DrawText(line, x, y, align, font, strokePaint);
        canvas.DrawText(line, x, y, align, font, fillPaint);
    }

    private static void DrawShadow(SKCanvas canvas, string line, SKFont font,
        float x, float y, SKTextAlign align, float shadowRadius, SKColor shadowColor)
    {
        float sigma = shadowRadius * font.Size * 0.5f;

        using var blurPaint = new SKPaint
        {
            Color       = shadowColor,
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = shadowRadius * font.Size * 0.5f,
            StrokeJoin  = SKStrokeJoin.Round,
            MaskFilter  = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma),
        };

        canvas.DrawText(line, x, y, align, font, blurPaint);
    }

    private static float FitFontSize(TextElement element, TextRenderOptions options)
    {
        float lo = element.MinimumFontSize;
        float hi = options.MaxFontSize;

        for (int i = 0; i < 16; i++)
        {
            float mid = (lo + hi) / 2f;
            using var font = new SKFont(element.Font, mid);
            var lines = BreakIntoLines(element.Text, font, options.Bounds.Width);
            float totalHeight = lines.Count * mid * options.LineHeightMultiplier;

            bool fits = totalHeight <= options.Bounds.Height;
            if (fits) lo = mid;
            else hi = mid;
        }

        return lo;
    }

    public static List<string> BreakIntoLines(string text, SKFont font, float maxWidth)
    {
        var lines = new List<string>();

        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ');
            var current = new System.Text.StringBuilder();

            foreach (var word in words)
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                float w = font.MeasureText(candidate);

                if (w <= maxWidth)
                {
                    current.Clear();
                    current.Append(candidate);
                }
                else
                {
                    if (current.Length > 0)
                        lines.Add(current.ToString());
                    current.Clear();
                    current.Append(word);
                }
            }

            if (current.Length > 0)
                lines.Add(current.ToString());
        }

        return lines;
    }

    private static SKRect MeasureLineBounds(string line, SKFont font,
        float x, float y, SKTextAlign align)
    {
        float width = font.MeasureText(line, out SKRect textBounds);
        float left = align switch
        {
            SKTextAlign.Left  => x,
            SKTextAlign.Right => x - width,
            _                 => x - width / 2f
        };
        return new SKRect(left, y + textBounds.Top, left + width, y + textBounds.Bottom);
    }
}