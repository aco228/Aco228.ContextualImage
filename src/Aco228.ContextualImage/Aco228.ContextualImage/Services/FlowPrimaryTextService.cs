using Aco228.Common.Helpers;
using Aco228.Common.Infrastructure;
using Aco228.Common.LocalStorage;
using Aco228.ContextualImage.Infrastructure;
using Aco228.ContextualImage.Models;
using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Services;

public class FlowPrimaryTextService : ContextualFlow
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

    public override Task<FileInfo> Run(string imagePath, string primaryText, string secondaryText, string aspectRatio, int width, int height, bool debug = false)
        => Run(imagePath, primaryText, aspectRatio, width, height, debug);

    public async Task<FileInfo> Run(string imagePath, string primaryText, string aspectRatio, int width, int height, bool debug = false)
    {
        var font = FontManager.TakeNext();

        using var resizedCropped = ResizeAndGetBitmap(imagePath, aspectRatio, width, height, out var bitmap);
        using var skBitmap = bitmap;

        Rect focalPoint = Yolov8nHelper.FindFocalPoint(resizedCropped);

        var accentColor = GetAccessColor(bitmap, out var bgForText, out _);

        var primaryElement = PrimaryTextVariations.Take()!();
        primaryElement.Text = RandomHelper.RandomChance<string>(() => primaryText, () => primaryText.ToUpperInvariant());
        primaryElement.Font = FontManager.FindBold(font);
        primaryElement.OutlineColor = accentColor.IsDark() ? accentColor.ShiftBrightness(5) : accentColor.ShiftBrightness(80);
        primaryElement.ShadowColor = accentColor.IsDark() ? accentColor.ShiftBrightness(5).WithAlpha(180) : accentColor.ShiftBrightness(95).WithAlpha(100);
        primaryElement.Color = accentColor.IsDark() ? accentColor.ShiftBrightness(95) : accentColor.ShiftBrightness(20);
        if(primaryElement.Background != null) 
            primaryElement.Background.Color = accentColor.IsDark() ? accentColor.ShiftBrightness(35) : accentColor.ShiftBrightness(70);
        

        var placements = TextPlacementHelper.FindPlacements(resizedCropped, new List<TextPlacementRequest>
        {
            new TextPlacementRequest
            {
                Element = primaryElement,
                MinFontSize = resizedCropped.Width * 0.063f,
                MaxFontSize = resizedCropped.Width * 0.085f,
            },
        }, focalPoint);

        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        using var surfaceCanvas = surface.Canvas;
        surfaceCanvas.DrawBitmap(bitmap, 0, 0);
        DrawOverlay(bgForText, surfaceCanvas, bitmap);
        DrawTexts(placements, resizedCropped, bitmap, surfaceCanvas);

        using var finalImage = surface.Snapshot();
        using var finalBitmap = SKBitmap.FromImage(finalImage);

        using var result = new Mat();
        SkHelper.SkBitmapToMat(finalBitmap, result);

        var filePath = StorageManager.Instance.GetTempFolder().GetPathForFile(IdHelper.GetId() + ".jpg");
        Cv2.ImWrite(filePath, result);

        if (debug)
        {
            Cv2.ImShow("Best Crop", result);
            Cv2.WaitKey(0);
        }

        return new FileInfo(filePath);
    }
}