using Aco228.Common.Helpers;
using Aco228.Common.Infrastructure;
using Aco228.ContextualImage.Infrastructure;
using Aco228.ContextualImage.Models;
using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Services;

public interface IContextualFlow
{
    
}

public class ContextualFlow : IContextualFlow
{
    private static ManagedList<SKTextAlign> Aligns = new()
    {
        SKTextAlign.Center,
        SKTextAlign.Left,
        SKTextAlign.Right,
    };
    
    protected void DrawTexts(List<TextPlacement> placements, Mat cropped, SKBitmap bitmap, SKCanvas surfaceCanvas)
    {
        var align = Aligns.Take();
        foreach (var placement in placements)
        {
            float dimAmount = Math.Max(0.4f, ImageEffectsHelper.CalculateDimAmount(cropped, placement.Bounds, placement.Element.Color));
            bool isBottom = placement.Bounds.Top > bitmap.Height / 2f;
            SkiaTextHelper.DrawTextGradient(surfaceCanvas, placement.Bounds, dimAmount, isBottom);

            SkiaTextHelper.DrawText(surfaceCanvas, placement.Element, new TextRenderOptions
            {
                Bounds = placement.Bounds,
                MaxFontSize = placement.MaxFontSize,
                HorizontalAlign = align,
            });
        }
    }
    
    protected void DrawOverlay(SKColor bgForText, SKCanvas surfaceCanvas, SKBitmap bitmap)
    {
        using var overlayPaint = new SKPaint
        {
            Color = bgForText.WithAlpha((byte)(0.06f * 255)),
            BlendMode = SKBlendMode.SrcOver,
        };
        surfaceCanvas.DrawRect(0, 0, bitmap.Width, bitmap.Height, overlayPaint);
    }
    
    protected void PostProcessSecondaryText(List<TextPlacement> placements, TextElement secondaryElement, SKBitmap bitmap)
    {
        var secondaryPlacement = placements.FirstOrDefault(p => p.Element == secondaryElement);
        if (secondaryPlacement != null)
        {
            var bounds = secondaryPlacement.Bounds;
            var zonePalette = ImageColorPaletteHelper.ExtractPalette(bitmap,
                (int)bounds.Left, (int)bounds.Top, (int)bounds.Width, (int)bounds.Height);

            var zoneAvg = zonePalette.First();
            float zoneBrightness = zoneAvg.Red * 0.299f + zoneAvg.Green * 0.587f + zoneAvg.Blue * 0.114f;

            bool isDarkZone = zoneBrightness <= 128f;

            secondaryElement.Color = isDarkZone
                ? zoneAvg.ShiftBrightness(90)
                : zoneAvg.ShiftBrightness(5);

            // Outline always opposite of text color for maximum contrast
            secondaryElement.OutlineColor = isDarkZone
                ? zoneAvg.ShiftBrightness(5)
                : zoneAvg.ShiftBrightness(95);

            secondaryElement.OutlineWidth = isDarkZone
                ? FloatHelper.Random(0.06f, 0.19f)
                : FloatHelper.Random(0.03f, 0.05f);
            
            if(secondaryElement.Background != null)
                secondaryElement.Background.Color = isDarkZone ? zoneAvg : zoneAvg.ShiftBrightness(70);
            
            secondaryElement.ShadowColor = isDarkZone
                ? zoneAvg.ShiftBrightness(5).WithAlpha(180)
                : zoneAvg.ShiftBrightness(95).WithAlpha(180);
        }
    }
    
    protected SKColor GetAccessColor(SKBitmap bitmap)
    {
        var palette = ImageColorPaletteHelper.ExtractPalette(bitmap);
        var dominantColor = palette.First();
        var accentColor = dominantColor.ShiftHue(FloatHelper.Random(-45, 45));
        return accentColor;
    }
}