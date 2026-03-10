using Aco228.Common.Helpers;
using Aco228.Common.Infrastructure;
using Aco228.Common.LocalStorage;
using Aco228.ContextualImage.Infrastructure;
using Aco228.ContextualImage.Models;
using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Services;

public class FlowPrimaryTextBlurService : ContextualFlow
{
    private static ManagedList<SKTextAlign> Aligns = new()
    {
        SKTextAlign.Center,
        SKTextAlign.Left,
        SKTextAlign.Right,
    };
    
    private static readonly ManagedList<Func<TextElement>> PrimaryTextVariations = new()
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
        => Run(imagePath, primaryText, aspectRatio, width, height, debug: debug);
        
    public async Task<FileInfo> Run(string imagePath, string primaryText, string aspectRatio, int width, int height, float blurSigma = 80f, bool debug = false)
    {
        var font = FontManager.TakeNext();

        using var cropped = ResizeAndGetBitmap(imagePath, aspectRatio, width, height, out var bitmap);
        using var skBitmap = bitmap;

        Rect focalPoint = Yolov8nHelper.FindFocalPoint(cropped);

        var accentColor = GetAccessColor(bitmap, out var bgForText, out var shadowColor);
        bool bgIsDark = accentColor.IsDark();

        // Text: maximum contrast against background
        SKColor textColor    = bgIsDark ? SKColors.White : SKColors.Black;

        // Outline: accent color pushed to opposite end of brightness from text
        SKColor outlineColor = bgIsDark
            ? accentColor.ShiftBrightness(FloatHelper.Random(5, 20))   // dark outline on bright text
            : accentColor.ShiftBrightness(FloatHelper.Random(75, 90));  // bright outline on dark text

        SKColor shadowCol = outlineColor.WithAlpha(180);

        var primaryElement = PrimaryTextVariations.Take()!();
        primaryElement.Text         = FloatHelper.RandomChance<string>(() => primaryText, () => primaryText.ToUpperInvariant());
        primaryElement.Font         = FontManager.FindBold(font);
        primaryElement.Color        = textColor;
        primaryElement.OutlineColor = outlineColor;
        primaryElement.ShadowColor  = shadowCol;

        if (primaryElement.Background != null)
            primaryElement.Background.Color = bgIsDark
                ? accentColor.ShiftBrightness(10)   // dark bg box
                : accentColor.ShiftBrightness(80);  // light bg box

        var placements = TextPlacementHelper.FindPlacements(cropped, new List<TextPlacementRequest>
        {
            new TextPlacementRequest
            {
                Element = primaryElement,
                MinFontSize = cropped.Width * 0.093f,
                MaxFontSize = cropped.Width * 0.115f,
            },
        }, focalPoint);

        // Blurred background surface
        using var surface = CreateBlurredSurface(bitmap, sigma: blurSigma);  // 8-20 range safe
        using var canvas = surface.Canvas;

        DrawOverlay(bgForText, canvas, bitmap);
        DrawTexts(placements, cropped, bitmap, canvas);
        
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

    private static SKSurface CreateBlurredSurface(SKBitmap source, float sigma = 12f)
    {
        if (source == null || source.Width <= 0 || source.Height <= 0)
            throw new ArgumentException("Invalid source bitmap for blur");

        sigma = Math.Clamp(sigma, 2f, 80f);  // prevent extreme values causing native issues

        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var surface = SKSurface.Create(info) ?? throw new InvalidOperationException("Failed to create SKSurface");

        using var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var filter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);

        using var paint = new SKPaint
        {
            ImageFilter = filter,
            IsAntialias = true
        };

        try
        {
            canvas.DrawBitmap(source, 0, 0, paint);
        }
        catch (Exception ex)
        {
            surface.Dispose();
            throw new InvalidOperationException("Blur draw failed (possible native crash)", ex);
        }

        return surface;
    }
}