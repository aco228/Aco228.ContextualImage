using SkiaSharp;

namespace Aco228.ContextualImage.Models;

public class TextElement
{
    public required string Text { get; set; }
    public required SKTypeface Font { get; set; }
    public float MinimumFontSize { get; set; }
    public float? OutlineWidth { get; set; }
    public SKColor? OutlineColor { get; set; }
    public SKColor Color { get; set; } = SKColors.Black;
    public float? ShadowRadius { get; set; }
    public SKColor? ShadowColor { get; set; }
    public TextElementBackground? Background { get; set; }
}

public class TextElementBackground
{
    public SKColor Color { get; set; } = SKColors.White;
    public float Opacity { get; set; } = 1f;
    public float BackdropBlur { get; set; } = 0f;
    public float PaddingX { get; set; } = 0.5f;
    public float PaddingY { get; set; } = 0.5f;
    public float CornerRadius { get; set; } = 0.6f;
}

public class TextRenderOptions
{
    /// <summary>Bounding rect in which the text must fit. Text will be auto-sized to fill it.</summary>
    public required SKRect Bounds { get; set; }

    /// <summary>Maximum font size to start sizing from.</summary>
    public float MaxFontSize { get; set; } = 120f;

    public SKTextAlign HorizontalAlign { get; set; } = SKTextAlign.Center;
    public SKFontStyleWeight FontWeight { get; set; } = SKFontStyleWeight.Normal;

    /// <summary>Line height multiplier relative to font size.</summary>
    public float LineHeightMultiplier { get; set; } = 1f;
}