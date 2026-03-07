using SkiaSharp;

namespace Aco228.ContextualImage.Infrastructure;

public static class ColorHelper
{
    public static SKColor ShiftHue(this SKColor color, float degrees)
    {
        RgbToHsl(color.Red, color.Green, color.Blue, out float h, out float s, out float l);
        h = (h + degrees) % 360f;
        if (h < 0) h += 360f;
        HslToRgb(h, s, l, out byte r, out byte g, out byte b);
        return new SKColor(r, g, b, color.Alpha);
    }

    private static void RgbToHsl(byte rByte, byte gByte, byte bByte, out float h, out float s, out float l)
    {
        float r = rByte / 255f, g = gByte / 255f, b = bByte / 255f;
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;

        l = (max + min) / 2f;

        if (delta == 0f) { h = 0f; s = 0f; return; }

        s = l < 0.5f ? delta / (max + min) : delta / (2f - max - min);

        if (max == r)      h = ((g - b) / delta + (g < b ? 6f : 0f)) * 60f;
        else if (max == g) h = ((b - r) / delta + 2f) * 60f;
        else               h = ((r - g) / delta + 4f) * 60f;
    }

    private static void HslToRgb(float h, float s, float l, out byte r, out byte g, out byte b)
    {
        if (s == 0f)
        {
            r = g = b = (byte)(l * 255f);
            return;
        }

        float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
        float p = 2f * l - q;
        float hNorm = h / 360f;

        r = (byte)(HueToRgb(p, q, hNorm + 1f / 3f) * 255f);
        g = (byte)(HueToRgb(p, q, hNorm) * 255f);
        b = (byte)(HueToRgb(p, q, hNorm - 1f / 3f) * 255f);
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }
    
    public static SKColor ShiftBrightness(this SKColor color, float brightness)
    {
        RgbToHsl(color.Red, color.Green, color.Blue, out float h, out float s, out float l);
        l = Math.Clamp(brightness / 100f, 0f, 1f);
        HslToRgb(h, s, l, out byte r, out byte g, out byte b);
        return new SKColor(r, g, b, color.Alpha);
    }
    
    public static bool IsDark(this SKColor color)
    {
        float brightness = color.Red * 0.299f + color.Green * 0.587f + color.Blue * 0.114f;
        return brightness < 115f;
    }
}