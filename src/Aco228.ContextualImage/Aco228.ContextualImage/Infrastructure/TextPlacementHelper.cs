using System.Text;
using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Infrastructure;

public static class TextPlacementHelper
{
    public static Rect FindBestTextRect(
        FileInfo fileInfo,
        Rect cropRect,
        string text,
        SKTypeface font,
        float fontSizeVw = 0.05f,
        int padding = 12,
        IEnumerable<Rect> excludeRects = null)
    {
        using var source = new Mat(fileInfo.FullName);
        Mat saliency = ComputeSaliencyMap(source);
        using var cropSaliency = new Mat(saliency, cropRect);

        float minFontSize = cropRect.Width * fontSizeVw;
        Size textSize = MeasureText(text, font, minFontSize, cropRect.Width - padding * 2, padding);

        return FindLowestSaliencyPosition(cropSaliency, cropRect, textSize, padding, excludeRects);
    }
    
    public static Rect FindSecondaryTextRect(
        Rect cropRect,
        Rect primaryRect,
        string text,
        SKTypeface font,
        float fontSizeVw = 0.04f,
        int gap = 8,
        int padding = 12)
    {
        float minFontSize = cropRect.Width * fontSizeVw;

        using var paint = new SKPaint
        {
            TextSize = minFontSize,
            Typeface = font
        };

        var lines = WrapText(paint, text, primaryRect.Width - padding * 2);
        float lineHeight = minFontSize * 1.3f;
        int h = (int)(lines.Count * lineHeight + padding * 2);
        int w = primaryRect.Width; // same width as primary

        // Try below first, then above
        bool spaceBelow = primaryRect.Y + primaryRect.Height + gap + h <= cropRect.Y + cropRect.Height - padding;
        bool spaceAbove = primaryRect.Y - gap - h >= cropRect.Y + padding;

        int x = primaryRect.X; // left-aligned with primary
        int y;

        if (spaceBelow)
            y = primaryRect.Y + primaryRect.Height + gap;
        else if (spaceAbove)
            y = primaryRect.Y - gap - h;
        else
            y = primaryRect.Y + primaryRect.Height + gap; // fallback, let it clip

        return new Rect(x, y, w, h);
    }

    private static Rect FindLowestSaliencyPosition(
        Mat cropSaliency,
        Rect cropRect,
        Size textSize,
        int padding,
        IEnumerable<Rect> excludeRects)
    {
        int w = textSize.Width;
        int h = textSize.Height;

        int maxX = cropRect.Width  - w - padding;
        int maxY = cropRect.Height - h - padding;

        if (maxX < padding || maxY < padding)
            return new Rect(cropRect.X + padding, cropRect.Y + padding, w, h);

        Rect bestRect    = new Rect(cropRect.X + padding, cropRect.Y + padding, w, h);
        double bestScore = double.MaxValue;

        int stepX = Math.Max(1, w / 10);
        int stepY = Math.Max(1, h / 10);

        var exclusions = excludeRects?.ToList() ?? new List<Rect>();

        for (int y = padding; y <= maxY; y += stepY)
        for (int x = padding; x <= maxX; x += stepX)
        {
            var candidate = new Rect(cropRect.X + x, cropRect.Y + y, w, h);

            // Skip if overlaps any existing text rect
            if (exclusions.Any(e => Overlaps(candidate, e)))
                continue;

            var localRect = new Rect(x, y, w, h);
            using var roi = new Mat(cropSaliency, localRect);
            double score = Cv2.Mean(roi).Val0;

            if (score < bestScore)
            {
                bestScore = score;
                bestRect  = candidate;
            }
        }

        return bestRect;
    }
    
    private static bool Overlaps(Rect a, Rect b)
    {
        return a.X < b.X + b.Width  &&
               a.X + a.Width  > b.X &&
               a.Y < b.Y + b.Height &&
               a.Y + a.Height > b.Y;
    }

    private static Size MeasureText(string text, SKTypeface typeface, float minFontSize, float maxWidth, int padding)
    {
        var font = new SKFont(typeface, minFontSize);

        var lines = WrapText(font, text, maxWidth);
        float lineHeight = minFontSize * 1.3f;

        float totalH = lines.Count * lineHeight + padding * 2;
        float totalW = lines.Max(l => font.MeasureText(l)) + padding * 2;

        return new Size((int)totalW, (int)totalH);
    }

    private static List<string> WrapText(SKFont font, string text, float maxWidth)
    {
        var lines = new List<string>();

        foreach (var paragraph in text.Split('\n'))
        {
            var words   = paragraph.Split(' ');
            var current = new StringBuilder();

            foreach (var word in words)
            {
                string test = current.Length == 0 ? word : $"{current} {word}";
                if (font.MeasureText(test) > maxWidth && current.Length > 0)
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

    private static List<string> WrapText(SKPaint paint, string text, float maxWidth)
    {
        var lines = new List<string>();

        foreach (var paragraph in text.Split('\n'))
        {
            var words   = paragraph.Split(' ');
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

    private static Mat ComputeSaliencyMap(Mat source)
    {
        using var gray    = new Mat();
        using var blurred = new Mat();
        var saliency      = new Mat();

        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, blurred, new Size(51, 51), 0);
        Cv2.Absdiff(gray, blurred, saliency);
        Cv2.Normalize(saliency, saliency, 0, 255, NormTypes.MinMax);

        return saliency;
    }
}