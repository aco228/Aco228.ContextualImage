using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Infrastructure;

public static class ImageEffectsHelper
{
    public static float CalculateDimAmount(Mat croppedMat, SKRect textBounds, SKColor textColor)
    {
        // Convert bounds to OpenCV rect
        var region = new Rect(
            (int) textBounds.Left,
            (int) textBounds.Top,
            (int) textBounds.Width,
            (int) textBounds.Height);

        region = new Rect(
            Math.Clamp(region.X, 0, croppedMat.Width),
            Math.Clamp(region.Y, 0, croppedMat.Height),
            Math.Clamp(region.Width, 1, croppedMat.Width - region.X),
            Math.Clamp(region.Height, 1, croppedMat.Height - region.Y));

        using var regionMat = new Mat(croppedMat, region);
        using var gray = new Mat();
        Cv2.CvtColor(regionMat, gray, ColorConversionCodes.BGR2GRAY);

        // Average brightness of background (0-255)
        var mean = Cv2.Mean(gray);
        float bgBrightness = (float) mean.Val0 / 255f; // 0=dark, 1=bright

        // Text brightness
        float textBrightness = (textColor.Red * 0.299f + textColor.Green * 0.587f + textColor.Blue * 0.114f) / 255f;

        // Target contrast ratio — we want at least 0.4 difference in brightness
        float targetContrast = 0.4f;
        float currentContrast = Math.Abs(bgBrightness - textBrightness);

        if (currentContrast >= targetContrast)
            return 0f; // already enough contrast, no dimming needed

        // How much to dim to reach target contrast
        float dimAmount = targetContrast - currentContrast;

        // If text is white, we need to darken bg. If text is dark, we need to brighten.
        // For now assume we always dim (darken) since text is usually light
        return Math.Clamp(dimAmount, 0f, 0.6f); // never dim more than 60%
    }

    public static SKBitmap ApplyColorGradeForText(SKBitmap bitmap, List<TextPlacement> placements)
    {
        float imgBrightness = SampleImageBrightness(bitmap);
        float textBrightness = placements
            .Select(p => (p.Element.Color.Red * 0.299f + p.Element.Color.Green * 0.587f + p.Element.Color.Blue * 0.114f) / 255f)
            .Average();

        // Very subtle brightness adjustment only — no saturation/contrast changes
        float brightness = textBrightness > 0.5f
            ? Math.Clamp(1f - (imgBrightness * 0.15f), 0.85f, 1f)  // slightly darken if text is bright
            : Math.Clamp(1f + ((1f - imgBrightness) * 0.1f), 1f, 1.1f); // slightly brighten if text is dark

        var result = new SKBitmap(bitmap.Width, bitmap.Height);
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(bitmap, 0, 0);

        if (brightness < 1f)
        {
            byte alpha = (byte)((1f - brightness) * 255);
            using var paint = new SKPaint { Color = SKColors.Black.WithAlpha(alpha) };
            canvas.DrawRect(0, 0, bitmap.Width, bitmap.Height, paint);
        }
        else if (brightness > 1f)
        {
            byte alpha = (byte)((brightness - 1f) * 255);
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
            canvas.DrawRect(0, 0, bitmap.Width, bitmap.Height, paint);
        }

        return result;
    }

    private static float SampleImageBrightness(SKBitmap bitmap)
    {
        long total = 0;
        int count = 0;
        int step = 20;

        for (int y = 0; y < bitmap.Height; y += step)
        for (int x = 0; x < bitmap.Width; x += step)
        {
            var c = bitmap.GetPixel(x, y);
            total += (int)(c.Red * 0.299f + c.Green * 0.587f + c.Blue * 0.114f);
            count++;
        }

        return count == 0 ? 0.5f : (float)total / count / 255f;
    }
}