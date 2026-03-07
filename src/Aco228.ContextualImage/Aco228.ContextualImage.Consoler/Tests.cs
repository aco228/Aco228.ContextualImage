using Aco228.Common.Helpers;
using Aco228.ContextualImage.Infrastructure;
using Aco228.ContextualImage.Models;
using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Consoler;

public static class Tests

{
    public static async Task TestSmartCrop2(
        string path,
        string primaryText,
        string secondaryText)
    {
        var fileInfo = new FileInfo(path);
        using var mat = new Mat(fileInfo.FullName);
        Rect crop = SmartCropHelper.FindBestCrop(mat, "4:5");

        var font = FontManager.TakeNext();

        using var cropped = new Mat(mat, crop);
        Rect focalPoint = Yolov8nHelper.FindFocalPoint(cropped);

        using var bitmap = SkHelper.MatToSkBitmap(cropped);

        var palette = ImageColorPaletteHelper.ExtractPalette(bitmap);
        var bgColor = palette.OrderByDescending(c =>
        {
            float r = c.Red / 255f, g = c.Green / 255f, b = c.Blue / 255f;
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            return max - min;
        }).First();

        var accentColor = bgColor
            .ShiftHue(FloatHelper.Random(-45, 45))
            .ShiftBrightness(FloatHelper.Random(20, 50)); 
        
        var bgForText = accentColor.IsDark() ? accentColor : accentColor.ShiftBrightness(95); //.ShiftBrightness(25);
        var shadowColor = accentColor.ShiftBrightness(10).WithAlpha(180);

        var primaryElement = new TextElement
        {
            Text = primaryText,
            Font = font,
            Color = accentColor.IsDark() ? accentColor.ShiftBrightness(90) : accentColor.ShiftBrightness(20),
            OutlineWidth = 0.025f,
            OutlineColor = accentColor.IsDark() ? accentColor.ShiftBrightness(5) : accentColor.ShiftBrightness(70),
            ShadowColor = accentColor.IsDark() ? accentColor.ShiftBrightness(5).WithAlpha(180) : accentColor.ShiftBrightness(95).WithAlpha(100),
            ShadowRadius = 0.1f,
            Background = new TextElementBackground
            {
                Color = accentColor.IsDark() ? accentColor : accentColor.ShiftBrightness(70),
                Opacity = FloatHelper.Random(0.7f, 0.9f),
                CornerRadius = FloatHelper.Random(0.01f, 0.9f),
                BackdropBlur = 0.9f,
                PaddingX = FloatHelper.Random(0.4f, 0.7f),
                PaddingY = FloatHelper.Random(0.35f, 0.4f),
            },
        };

        var secondaryElement = new TextElement
        {
            Text = secondaryText,
            Font = font,
            Color = SKColors.White,
            // OutlineColor = bgForText.ShiftBrightness(10),
            ShadowColor = shadowColor,
            ShadowRadius = 1.5f,
            Background = new()
            {
                Color = new(0,0,0,0),
                CornerRadius = 0,
                BackdropBlur = 0.9f,
            }
        };

        var placements = TextPlacementHelper.FindPlacements(cropped, new List<TextPlacementRequest>
        {
            new TextPlacementRequest
            {
                Element = secondaryElement,
                MinFontSize = crop.Width * 0.042f,
                MaxFontSize = crop.Width * 0.043f,
            },
            new TextPlacementRequest
            {
                Element = primaryElement,
                MinFontSize = crop.Width * 0.053f,
                MaxFontSize = crop.Width * 0.07f,
            },
        }, focalPoint);

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
                ? FloatHelper.Random(0.05f, 0.18f)
                : FloatHelper.Random(0.01f, 0.05f);

            secondaryElement.ShadowColor = isDarkZone
                ? zoneAvg.ShiftBrightness(5).WithAlpha(180)
                : zoneAvg.ShiftBrightness(95).WithAlpha(180);
        }

        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        using var surfaceCanvas = surface.Canvas;
        surfaceCanvas.DrawBitmap(bitmap, 0, 0);
        using var overlayPaint = new SKPaint
        {
            Color = bgForText.WithAlpha((byte)(0.08f * 255)),
            BlendMode = SKBlendMode.SrcOver,
        };
        surfaceCanvas.DrawRect(0, 0, bitmap.Width, bitmap.Height, overlayPaint);

        foreach (var placement in placements)
        {
            float dimAmount = Math.Max(0.4f, ImageEffectsHelper.CalculateDimAmount(cropped, placement.Bounds, placement.Element.Color));
            bool isBottom = placement.Bounds.Top > bitmap.Height / 2f;
            SkiaTextHelper.DrawTextGradient(surfaceCanvas, placement.Bounds, dimAmount, isBottom, bitmap.Height);

            SkiaTextHelper.DrawText(surfaceCanvas, placement.Element, new TextRenderOptions
            {
                Bounds = placement.Bounds,
                MaxFontSize = placement.MaxFontSize,
            });
        }

        using var finalImage = surface.Snapshot();
        using var finalBitmap = SKBitmap.FromImage(finalImage);

        using var result = new Mat();
        SkHelper.SkBitmapToMat(finalBitmap, result);
        Cv2.ImWrite("preview.jpg", result);
        Cv2.ImShow("Best Crop", result);
        Cv2.WaitKey(0);
    }
}