using Aco228.Common.Helpers;
using Aco228.Common.Infrastructure;
using Aco228.ContextualImage.Infrastructure;
using Aco228.ContextualImage.Models;
using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Services;

public static class FlowPrimaryTextService
{
    private static ManagedList<Func<TextElement>> PrimaryTextVariations = new()
    {
        () => new TextElement
        {
            OutlineWidth = 0.025f,
            ShadowRadius = 0.1f,
            Background = new TextElementBackground
            {
                Opacity = FloatHelper.Random(0.7f, 0.9f),
                CornerRadius = FloatHelper.Random(0.01f, 0.9f),
                BackdropBlur = 0.9f,
                PaddingX = FloatHelper.Random(0.4f, 0.7f),
                PaddingY = FloatHelper.Random(0.35f, 0.4f),
            },
        },
        () => new TextElement
        {
            OutlineWidth = FloatHelper.Random(0.4f, 0.8f),
            ShadowRadius = FloatHelper.Random(0.4f, 1f),
            Background = null,
        },
    };

    public static async Task Run(string path,
        string primaryText,
        string secondaryText,
        string aspectRatio)
    {
        var fileInfo = new FileInfo(path);
        using var mat = new Mat(fileInfo.FullName);
        Rect crop = SmartCropHelper.FindBestCrop(mat, aspectRatio);

        var font = FontManager.TakeNext();

        using var cropped = new Mat(mat, crop);
        Rect focalPoint = Yolov8nHelper.FindFocalPoint(cropped);

        using var bitmap = SkHelper.MatToSkBitmap(cropped);

        var accentColor = GetAccessColor(bitmap, out var bgForText, out var shadowColor);

        var primaryElement = PrimaryTextVariations.Take()!();
        primaryElement.Text = FloatHelper.RandomChance<string>(() => primaryText, () => primaryText.ToUpperInvariant());
        primaryElement.Font = FontManager.FindBold(font);
        primaryElement.OutlineColor = accentColor.IsDark() ? accentColor.ShiftBrightness(5) : accentColor.ShiftBrightness(80);
        primaryElement.ShadowColor = accentColor.IsDark() ? accentColor.ShiftBrightness(5).WithAlpha(180) : accentColor.ShiftBrightness(95).WithAlpha(100);
        primaryElement.Color = accentColor.IsDark() ? accentColor.ShiftBrightness(95) : accentColor.ShiftBrightness(20);
        if(primaryElement.Background != null) 
            primaryElement.Background.Color = accentColor.IsDark() ? accentColor.ShiftBrightness(35) : accentColor.ShiftBrightness(70);
        

        var placements = TextPlacementHelper.FindPlacements(cropped, new List<TextPlacementRequest>
        {
            new TextPlacementRequest
            {
                Element = primaryElement,
                MinFontSize = crop.Width * 0.063f,
                MaxFontSize = crop.Width * 0.085f,
            },
        }, focalPoint);

        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        using var surfaceCanvas = surface.Canvas;
        surfaceCanvas.DrawBitmap(bitmap, 0, 0);
        DrawOverlay(bgForText, surfaceCanvas, bitmap);
        DrawTexts(placements, cropped, bitmap, surfaceCanvas);

        using var finalImage = surface.Snapshot();
        using var finalBitmap = SKBitmap.FromImage(finalImage);

        using var result = new Mat();
        SkHelper.SkBitmapToMat(finalBitmap, result);
        Cv2.ImWrite("preview.jpg", result);
        Cv2.ImShow("Best Crop", result);
        Cv2.WaitKey(0);
    }

    private static void DrawTexts(List<TextPlacement> placements, Mat cropped, SKBitmap bitmap, SKCanvas surfaceCanvas)
    {
        foreach (var placement in placements)
        {
            float dimAmount = Math.Max(0.4f, ImageEffectsHelper.CalculateDimAmount(cropped, placement.Bounds, placement.Element.Color));
            bool isBottom = placement.Bounds.Top > bitmap.Height / 2f;
            SkiaTextHelper.DrawTextGradient(surfaceCanvas, placement.Bounds, dimAmount, isBottom);

            SkiaTextHelper.DrawText(surfaceCanvas, placement.Element, new TextRenderOptions
            {
                Bounds = placement.Bounds,
                MaxFontSize = placement.MaxFontSize,
            });
        }
    }

    private static void DrawOverlay(SKColor bgForText, SKCanvas surfaceCanvas, SKBitmap bitmap)
    {
        using var overlayPaint = new SKPaint
        {
            Color = bgForText.WithAlpha((byte)(0.08f * 255)),
            BlendMode = SKBlendMode.SrcOver,
        };
        surfaceCanvas.DrawRect(0, 0, bitmap.Width, bitmap.Height, overlayPaint);
    }

    private static void PostProcessSecondaryText(List<TextPlacement> placements, TextElement secondaryElement, SKBitmap bitmap)
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
                ? FloatHelper.Random(0.05f, 0.18f)
                : FloatHelper.Random(0.01f, 0.05f);
            

            if(secondaryElement.Background != null)
                secondaryElement.Background.Color = isDarkZone ? zoneAvg : zoneAvg.ShiftBrightness(70);
            
            secondaryElement.ShadowColor = isDarkZone
                ? zoneAvg.ShiftBrightness(5).WithAlpha(180)
                : zoneAvg.ShiftBrightness(95).WithAlpha(180);
        }
    }

    private static SKColor GetAccessColor(SKBitmap bitmap, out SKColor bgForText, out SKColor shadowColor)
    {
        var palette = ImageColorPaletteHelper.ExtractPalette(bitmap);

        // Most dominant color (first in palette = largest cluster)
        var dominantColor = palette.First();

        // Shift hue slightly to create accent, keep it analogous
        var accentColor = dominantColor.ShiftHue(FloatHelper.Random(-45, 45));

        // bgForText: saturated mid-dark version of accent — visible on any image
        bgForText = accentColor.ShiftBrightness(dominantColor.IsDark()
            ? FloatHelper.Random(30, 50)   // image is dark → use mid tone
            : FloatHelper.Random(15, 30)); // image is light → use darker tone

        // text color on bgForText should contrast against it
        // shadowColor: opposite brightness direction from bgForText, semi-transparent
        shadowColor = bgForText.IsDark()
            ? bgForText.ShiftBrightness(80).WithAlpha(180)
            : bgForText.ShiftBrightness(10).WithAlpha(180);

        return accentColor;
    }
}