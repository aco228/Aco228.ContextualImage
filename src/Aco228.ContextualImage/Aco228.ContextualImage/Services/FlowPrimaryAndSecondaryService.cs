using Aco228.Common.Helpers;
using Aco228.Common.Infrastructure;
using Aco228.Common.LocalStorage;
using Aco228.Common.Models;
using Aco228.ContextualImage.Infrastructure;
using Aco228.ContextualImage.Models;
using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Services;

public class FlowPrimaryAndSecondaryService : ContextualFlow, ITransient
{
    private static ManagedList<ManagedList<SKColor>?> Colors = new()
    {
        null,
        // new ManagedList<SKColor> { SKColors.Brown, SKColors.DarkBlue, SKColors.Indigo, SKColors.Chocolate, SKColors.DarkOliveGreen },
    };
    
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
            ShadowRadius = 1.1f,
            OutlineWidth = FloatHelper.Random(0.4f, 0.8f),
            Background = null,
        },
    };

    private static ManagedList<Func<TextElement>> SecondarTextVariations = new()
    {
        () => new TextElement
        {
            ShadowRadius = 1.5f, 
            Background = new()
            {
                Color = new(0,0,0,0),
                Opacity = FloatHelper.Random(0.7f, 0.9f),
                CornerRadius = FloatHelper.Random(0.01f, 0.9f),
                BackdropBlur = 0.9f,
                PaddingX = FloatHelper.Random(0.4f, 0.7f),
                PaddingY = FloatHelper.Random(0.35f, 0.4f),
            }
        },
        () => new TextElement
        {
            ShadowRadius = 1.5f,
        }
    };
    
    
    public override async Task<FileInfo> Run(
        string imagePath,
        string primaryText,
        string secondaryText,
        string aspectRatio,
        int width, int height,
        bool debug = false)
    {
        var font = FontManager.TakeNext();

        using var resizedCropped = ResizeAndGetBitmap(imagePath, aspectRatio, width, height, out var bitmap);
        using var skBitmap = bitmap;

        Rect focalPoint = Yolov8nHelper.FindFocalPoint(resizedCropped);

        var trueAccentColor = GetAccessColor(bitmap, out _, out _);
        var accentColorTemplate = Colors.Take()?.Take().ShiftHue(FloatHelper.Random(-20, 20));
        if (accentColorTemplate == null)
            accentColorTemplate = trueAccentColor.ShiftHue(FloatHelper.Random(-45, 45));

        var accentColor = accentColorTemplate.Value;
        
        var primaryElement = PrimaryTextVariations.Take()!();
        primaryElement.Text = RandomHelper.RandomChance<string>(() => primaryText, () => primaryText.ToUpperInvariant());
        primaryElement.Font = FontManager.FindBold(font);
        primaryElement.OutlineColor = accentColor.IsDark() ? accentColor.ShiftBrightness(25) : accentColor.ShiftBrightness(70);
        primaryElement.ShadowColor = accentColor.IsDark() ? accentColor.ShiftBrightness(25).WithAlpha(180) : accentColor.ShiftBrightness(95).WithAlpha(100);
        primaryElement.Color = accentColor.IsDark() ? accentColor.ShiftBrightness(95) : accentColor.ShiftBrightness(20);
        if(primaryElement.Background != null) 
            primaryElement.Background.Color = accentColor.IsDark() ? accentColor : accentColor.ShiftBrightness(70);

        var secondaryElement = SecondarTextVariations.Take()!();
        secondaryElement.Text = secondaryText;
        secondaryElement.Font = font;
        
        var placements = TextPlacementHelper.FindPlacements(resizedCropped, new List<TextPlacementRequest>
        {
            new TextPlacementRequest
            {
                Element = secondaryElement,
                MinFontSize = width * 0.042f,
                MaxFontSize = width * 0.048f,
            },
            new TextPlacementRequest
            {
                Element = primaryElement,
                MinFontSize = width * 0.059f,
                MaxFontSize = width * 0.07f,
            },
        }, focalPoint);

        PostProcessSecondaryText(placements, secondaryElement, bitmap);

        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        using var surfaceCanvas = surface.Canvas;
        surfaceCanvas.DrawBitmap(bitmap, 0, 0);
        DrawOverlay(accentColor.ShiftBrightness(70), surfaceCanvas, bitmap);
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