using SkiaSharp;

namespace Aco228.ContextualImage.Models;

public class TextElement
{
    public required string Text { get; set; }
    public TextElementBackground? Background { get; set; }
    public float Padding { get; set; }
    public SKColor Color { get; set; }
}

public class TextElementBackground
{
    public SKColor Color { get; set; }
    public float CornerRadius { get; set; }   
    public float Padding { get; set; }
    public float BlurSigma { get; set; }   
}