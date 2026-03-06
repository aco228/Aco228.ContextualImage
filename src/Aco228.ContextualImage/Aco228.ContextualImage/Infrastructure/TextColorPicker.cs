using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Infrastructure;

public static class TextColorPicker
{
    public static (SKColor textColor, SKColor backgroundColor) PickFromImage(Mat mat, Rect textRect)
    {
        using var roi = new Mat(mat, textRect);
    
        Scalar avgBgr = Cv2.Mean(roi);
        float r = (float)avgBgr.Val2 / 255f;
        float g = (float)avgBgr.Val1 / 255f;
        float b = (float)avgBgr.Val0 / 255f;

        RgbToHsl(r, g, b, out float h, out float s, out float l);

        // Complementary hue — always opposite on the wheel
        float bgH = (h + 180f) % 360f;

        // Force strong saturation and opposite lightness
        float bgS = Math.Max(s, 0.5f);         // at least 50% saturated
        float bgL = l > 0.5f ? 0.25f : 0.75f; // flip lightness

        HslToRgb(bgH, bgS, bgL, out float pr, out float pg, out float pb);

        SKColor bgColor   = new SKColor((byte)(pr * 255), (byte)(pg * 255), (byte)(pb * 255), 230);
        SKColor textColor = bgL > 0.5f ? SKColors.Black : SKColors.White;

        return (textColor, bgColor);
    }

    private static void RgbToHsl(float r, float g, float b, out float h, out float s, out float l)
    {
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;

        l = (max + min) / 2f;

        if (delta == 0)
        {
            h = 0;
            s = 0;
            return;
        }

        s = l < 0.5f ? delta / (max + min) : delta / (2f - max - min);

        if      (max == r) h = ((g - b) / delta % 6f) * 60f;
        else if (max == g) h = ((b - r) / delta + 2f) * 60f;
        else               h = ((r - g) / delta + 4f) * 60f;

        if (h < 0) h += 360f;
    }

    private static void HslToRgb(float h, float s, float l, out float r, out float g, out float b)
    {
        float c = (1f - Math.Abs(2f * l - 1f)) * s;
        float x = c * (1f - Math.Abs(h / 60f % 2f - 1f));
        float m = l - c / 2f;

        (r, g, b) = (h switch
        {
            < 60  => (c, x, 0f),
            < 120 => (x, c, 0f),
            < 180 => (0f, c, x),
            < 240 => (0f, x, c),
            < 300 => (x, 0f, c),
            _     => (c, 0f, x)
        });

        r += m;
        g += m;
        b += m;
    }
}