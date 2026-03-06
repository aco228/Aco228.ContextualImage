using Aco228.ContextualImage.Models;
using SkiaSharp;

namespace Aco228.ContextualImage.Infrastructure;

public static class SkiaTextHelper
{
    /// <summary>
    /// Renders a TextElement onto the canvas inside the given bounds,
    /// auto-fitting font size, with optional outline, shadow, and background.
    /// </summary>
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

        // Pass 1: single unified background rect
        // Pass 1: single unified background path (per-line boxes merged seamlessly)
        if (element.Background != null)
        {
            float padX = font.Size * element.Background.PaddingX;
            float padY = font.Size * element.Background.PaddingY;
            float radius = font.Size * element.Background.CornerRadius;

            using var path = new SKPath();
            foreach (var (line, x, y) in linePositions)
            {
                SKRect lineBounds = MeasureLineBounds(line, font, x, y, options.HorizontalAlign);
                SKRect paddedBounds = SKRect.Inflate(lineBounds, padX, padY);
                path.AddRoundRect(paddedBounds, radius, radius);
            }

            // Draw backdrop blur first
            if (element.Background.BackdropBlur > 0f)
            {
                using var snapshot = canvas.Surface?.Snapshot();
                if (snapshot != null)
                {
                    using var blurFilter = SKImageFilter.CreateBlur(element.Background.BackdropBlur, element.Background.BackdropBlur);
                    using var blurPaint = new SKPaint { ImageFilter = blurFilter };
                    canvas.Save();
                    canvas.ClipPath(path, SKClipOperation.Intersect, true);
                    canvas.DrawImage(snapshot, 0, 0, blurPaint);
                    canvas.Restore();
                }
            }

            // Draw fill using EvenOdd fill type so overlapping areas merge
            path.FillType = SKPathFillType.Winding;
            var bgColor = element.Background.Color.WithAlpha((byte)(element.Background.Opacity * 255));
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
            canvas.DrawPath(path, bgPaint);
        }

        // Pass 2: all text
        foreach (var (line, x, y) in linePositions)
        {
            if (element.ShadowRadius.HasValue && element.ShadowColor.HasValue)
                DrawShadow(canvas, line, font, x, y, options.HorizontalAlign,
                    element.ShadowRadius.Value, element.ShadowColor.Value);

            if (element.OutlineWidth.HasValue && element.OutlineColor.HasValue)
                DrawOutline(canvas, line, font, x, y, options.HorizontalAlign,
                    element.OutlineWidth.Value, element.OutlineColor.Value);

            DrawFill(canvas, line, font, x, y, options.HorizontalAlign, element.Color);
        }
    }

    // -------------------------------------------------------------------------
    // Private rendering steps
    // -------------------------------------------------------------------------

    private static void DrawFill(SKCanvas canvas, string line, SKFont font,
        float x, float y, SKTextAlign align, SKColor color)
    {
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
        };
        canvas.DrawText(line, x, y, align, font, paint);
    }

    private static void DrawOutline(SKCanvas canvas, string line, SKFont font,
        float x, float y, SKTextAlign align, float outlineWidth, SKColor outlineColor)
    {
        using var paint = new SKPaint
        {
            Color = outlineColor,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = outlineWidth * 2f, // stroke is centered on path edge
            StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawText(line, x, y, align, font, paint);
    }

    private static void DrawShadow(SKCanvas canvas, string line, SKFont font,
        float x, float y, SKTextAlign align, float shadowRadius, SKColor shadowColor)
    {
        using var paint = new SKPaint
        {
            Color = shadowColor,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(shadowRadius, shadowRadius),
        };
        // Slight offset for drop-shadow feel
        float offset = shadowRadius * 0.5f;
        canvas.DrawText(line, x + offset, y + offset, align, font, paint);
    }

    private static void DrawBackground(SKCanvas canvas, TextElementBackground bg,
        SKRect lineBounds, SKFont font)
    {
        float padX = font.Size * bg.PaddingX;
        float padY = font.Size * bg.PaddingY;
        float radius = font.Size * bg.CornerRadius;

        var bgRect = SKRect.Inflate(lineBounds, padX, padY);

        // Backdrop blur: snapshot the canvas content behind the rect, blur it, draw it clipped
        if (bg.BackdropBlur > 0f)
        {
            using var snapshot = canvas.Surface?.Snapshot();
            if (snapshot != null)
            {
                using var blurFilter = SKImageFilter.CreateBlur(bg.BackdropBlur, bg.BackdropBlur);
                using var blurPaint = new SKPaint { ImageFilter = blurFilter };

                using var clipPath = new SKPath();
                clipPath.AddRoundRect(bgRect, radius, radius);

                canvas.Save();
                canvas.ClipPath(clipPath, SKClipOperation.Intersect, true);
                canvas.DrawImage(snapshot, 0, 0, blurPaint);
                canvas.Restore();
            }
        }

        // Solid background fill on top of blurred region
        var bgColor = bg.Color.WithAlpha((byte) (bg.Opacity * 255));
        using var bgPaint = new SKPaint
        {
            Color = bgColor,
            IsAntialias = true,
        };
        canvas.DrawRoundRect(bgRect, radius, radius, bgPaint);
    }

    // -------------------------------------------------------------------------
    // Layout helpers
    // -------------------------------------------------------------------------

    /// <summary>Binary-search for the largest font size that fits all text within bounds.</summary>
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

    private static List<string> BreakIntoLines(string text, SKFont font, float maxWidth)
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

                    // Check if the single word itself exceeds maxWidth — if so, just add it anyway
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
            SKTextAlign.Left   => x,
            SKTextAlign.Right  => x - width,
            _                  => x - width / 2f
        };
        return new SKRect(left, y + textBounds.Top, left + width, y + textBounds.Bottom);
    }
}