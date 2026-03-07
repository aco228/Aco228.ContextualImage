using SkiaSharp;

namespace Aco228.ContextualImage.Infrastructure;

public class ImageColorPaletteHelper
{
    public static List<SKColor> ExtractPalette(SKBitmap bitmap, int x, int y, int width, int height, int colorCount = 3,
        int step = 5)
    {
        // clamp to bitmap bounds
        x = Math.Clamp(x, 0, bitmap.Width - 1);
        y = Math.Clamp(y, 0, bitmap.Height - 1);
        width = Math.Clamp(width, 1, bitmap.Width - x);
        height = Math.Clamp(height, 1, bitmap.Height - y);

        var pixels = new List<(float R, float G, float B)>();
        for (int py = y; py < y + height; py += step)
        for (int px = x; px < x + width; px += step)
        {
            var c = bitmap.GetPixel(px, py);
            if (c.Alpha < 128) continue;
            pixels.Add((c.Red, c.Green, c.Blue));
        }

        if (pixels.Count == 0)
            return new List<SKColor> { SKColors.Gray };

        var clusters = MedianCut(pixels, (int) Math.Ceiling(Math.Log2(colorCount)));
        return clusters
            .Take(colorCount)
            .Select(g =>
            {
                var r = (byte) g.Average(p => p.R);
                var gr = (byte) g.Average(p => p.G);
                var b = (byte) g.Average(p => p.B);
                return new SKColor(r, gr, b);
            })
            .ToList();
    }

    public static List<SKColor> ExtractPalette(SKBitmap bitmap, int colorCount = 5, int step = 10)
    {
        var pixels = new List<(float R, float G, float B)>();

        for (int y = 0; y < bitmap.Height; y += step)
        for (int x = 0; x < bitmap.Width; x += step)
        {
            var c = bitmap.GetPixel(x, y);
            if (c.Alpha < 128) continue;
            pixels.Add((c.Red, c.Green, c.Blue));
        }

        if (pixels.Count == 0)
            return new List<SKColor> { SKColors.Gray };

        var clusters = MedianCut(pixels, (int) Math.Ceiling(Math.Log2(colorCount)));
        return clusters
            .Take(colorCount)
            .Select(g =>
            {
                var r = (byte) g.Average(p => p.R);
                var gr = (byte) g.Average(p => p.G);
                var b = (byte) g.Average(p => p.B);
                return new SKColor(r, gr, b);
            })
            .ToList();
    }

    private static List<List<(float R, float G, float B)>> MedianCut(
        List<(float R, float G, float B)> pixels, int depth)
    {
        if (depth == 0 || pixels.Count == 0)
            return new List<List<(float R, float G, float B)>> { pixels };

        float rRange = pixels.Max(p => p.R) - pixels.Min(p => p.R);
        float gRange = pixels.Max(p => p.G) - pixels.Min(p => p.G);
        float bRange = pixels.Max(p => p.B) - pixels.Min(p => p.B);

        var sorted = rRange >= gRange && rRange >= bRange
            ? pixels.OrderBy(p => p.R).ToList()
            : gRange >= bRange
                ? pixels.OrderBy(p => p.G).ToList()
                : pixels.OrderBy(p => p.B).ToList();

        int mid = sorted.Count / 2;
        var result = new List<List<(float R, float G, float B)>>();
        result.AddRange(MedianCut(sorted.Take(mid).ToList(), depth - 1));
        result.AddRange(MedianCut(sorted.Skip(mid).ToList(), depth - 1));
        return result;
    }
}