using Aco228.ContextualImage.Models;
using SkiaSharp;

namespace Aco228.ContextualImage.Infrastructure;

public static class SkiaTextHelper
{
    public static void DrawTextGradient(
        SKCanvas canvas, 
        SKRect textBounds, 
        float dimAmount, 
        bool isBottomText)
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

        using var paint = new SKPaint { Shader = shader, IsAntialias = true, BlendMode = SKBlendMode.Darken };
        canvas.DrawRect(gradientRect, paint);
    }

    public static void DrawText(SKCanvas canvas, TextElement element, TextRenderOptions options)
    {
        // FitFontSize already respects MinimumFontSize as its lower bound,
        // so no clamping needed here — clamping up would cause lines to overflow bounds.
        float fontSize = FitFontSize(element, options);

        using var font = new SKFont(element.Font, fontSize);
        font.Edging = SKFontEdging.SubpixelAntialias;

        var lines = BreakIntoLines(element.Text, font, options.Bounds.Width);

        // Per-line font size: if a single word is wider than bounds, shrink only that line.
        var lineFontSizes = lines.Select(line =>
        {
            float lineW = font.MeasureText(line);
            if (lineW <= options.Bounds.Width)
                return fontSize;
            // Scale down proportionally so the word exactly fits
            float scaled = fontSize * (options.Bounds.Width / lineW);
            return Math.Max(scaled, Math.Max(element.MinimumFontSize, 1f));
        }).ToList();

        float lineHeight  = fontSize * options.LineHeightMultiplier;
        float totalHeight = lines.Count * lineHeight;
        float startY      = options.Bounds.MidY - totalHeight / 2f + fontSize;

        // Each entry: (line text, x, y, per-line font size)
        var linePositions = lines.Select((line, i) =>
        {
            float y = startY + i * lineHeight;
            float x = options.HorizontalAlign switch
            {
                SKTextAlign.Left  => options.Bounds.Left,
                SKTextAlign.Right => options.Bounds.Right,
                _                 => options.Bounds.MidX
            };
            return (line, x, y, lineFontSizes[i]);
        }).ToList();

        // Pass 1: background
        if (element.Background != null)
        {
            using var path = new SKPath();
            foreach (var (line, x, y, lineFontSize) in linePositions)
            {
                using var lineFont  = new SKFont(element.Font, lineFontSize) { Edging = SKFontEdging.SubpixelAntialias };
                float padX          = lineFontSize * element.Background.PaddingX;
                float padY          = lineFontSize * element.Background.PaddingY;
                float radius        = lineFontSize * element.Background.CornerRadius;
                SKRect lineBounds   = MeasureLineBounds(line, lineFont, x, y, options.HorizontalAlign);
                SKRect paddedBounds = SKRect.Inflate(lineBounds, padX, padY);
                path.AddRoundRect(paddedBounds, radius, radius);
            }

            float blur = fontSize * element.Background.BackdropBlur;
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

        // Pass 2: shadows
        if (element.ShadowRadius.HasValue && element.ShadowColor.HasValue)
        {
            foreach (var (line, x, y, lineFontSize) in linePositions)
            {
                using var lineFont = new SKFont(element.Font, lineFontSize) { Edging = SKFontEdging.SubpixelAntialias };
                DrawShadow(canvas, line, lineFont, x, y, options.HorizontalAlign,
                    element.ShadowRadius.Value, element.ShadowColor.Value);
            }
        }

        // Pass 3: outlines
        if (element.OutlineWidth.HasValue && element.OutlineColor.HasValue)
        {
            foreach (var (line, x, y, lineFontSize) in linePositions)
            {
                using var lineFont = new SKFont(element.Font, lineFontSize) { Edging = SKFontEdging.SubpixelAntialias };
                DrawOutline(canvas, line, lineFont, x, y, options.HorizontalAlign,
                    element.OutlineWidth.Value, element.OutlineColor.Value);
            }
        }

        // Pass 4: fills
        foreach (var (line, x, y, lineFontSize) in linePositions)
        {
            using var lineFont = new SKFont(element.Font, lineFontSize) { Edging = SKFontEdging.SubpixelAntialias };
            DrawFill(canvas, line, lineFont, x, y, options.HorizontalAlign, element.Color);
        }
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
        using var strokePaint = new SKPaint
        {
            Color       = outlineColor,
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = outlineWidth * font.Size,
            StrokeJoin  = SKStrokeJoin.Round,
            StrokeCap   = SKStrokeCap.Round,
        };
        canvas.DrawText(line, x, y, align, font, strokePaint);
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
        // Ensure minimum is at least 1px so binary search never converges near zero
        float lo = Math.Max(element.MinimumFontSize, 1f);
        float hi = options.MaxFontSize;

        // If even the minimum font size doesn't fit, return the minimum anyway —
        // the caller clamps to MinimumFontSize so we must not go below it.
        {
            using var minFont = new SKFont(element.Font, lo);
            var minLines      = BreakIntoLines(element.Text, minFont, options.Bounds.Width);
            float minHeight   = minLines.Count * lo * options.LineHeightMultiplier;
            if (minHeight > options.Bounds.Height)
                return lo;
        }

        for (int i = 0; i < 20; i++)
        {
            float mid = (lo + hi) / 2f;
            using var font    = new SKFont(element.Font, mid);
            var lines         = BreakIntoLines(element.Text, font, options.Bounds.Width);
            float totalHeight = lines.Count * mid * options.LineHeightMultiplier;

            if (totalHeight <= options.Bounds.Height) lo = mid;
            else                                      hi = mid;
        }

        return lo;
    }

    public static List<string> BreakIntoLines(string text, SKFont font, float maxWidth)
    {
        var lines = new List<string>();

        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;

            // First check if the whole paragraph fits on one line
            string full = string.Join(" ", words);
            if (font.MeasureText(full) <= maxWidth)
            {
                lines.Add(full);
                continue;
            }

            // Greedy pass: find how many lines are needed and what words go on each
            var greedyLines = GreedySplit(words, font, maxWidth);

            // If only 2 lines produced, try to balance them so neither line has
            // a single word while the other has many (e.g. "4 words / 1 word" → "2 / 3")
            if (greedyLines.Count == 2)
            {
                var balanced = BalanceTwoLines(words, font, maxWidth);
                if (balanced != null)
                    greedyLines = balanced;
            }

            lines.AddRange(greedyLines);
        }

        return lines;
    }

    private static List<string> GreedySplit(string[] words, SKFont font, float maxWidth)
    {
        var lines   = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate) <= maxWidth)
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

        return lines;
    }

    // For a 2-line break, find the split point that minimises the difference in
    // pixel width between the two lines (i.e. makes them as equal-length as possible),
    // while ensuring both lines still fit within maxWidth.
    private static List<string>? BalanceTwoLines(string[] words, SKFont font, float maxWidth)
    {
        if (words.Length < 2) return null;

        List<string>? best      = null;
        float         bestDiff  = float.MaxValue;

        for (int split = 1; split < words.Length; split++)
        {
            string line1 = string.Join(" ", words, 0, split);
            string line2 = string.Join(" ", words, split, words.Length - split);

            if (font.MeasureText(line1) > maxWidth) break; // adding more words only makes it wider
            if (font.MeasureText(line2) > maxWidth) continue;

            float diff = Math.Abs(font.MeasureText(line1) - font.MeasureText(line2));
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best     = new List<string> { line1, line2 };
            }
        }

        return best;
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