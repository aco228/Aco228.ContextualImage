using Aco228.Common.Helpers;
using Aco228.Common.Infrastructure;
using Aco228.ContextualImage.Infrastructure;
using Aco228.ContextualImage.Models;
using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Services;

public static class FlowPrimaryTextBlurService
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

    public static async Task Run(string path, string primaryText, string secondaryText, string aspectRatio)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Input image not found", path);

        using var mat = new Mat(fileInfo.FullName);
        if (mat.Empty())
            throw new InvalidOperationException("Failed to load image as Mat");

        Rect crop = SmartCropHelper.FindBestCrop(mat, aspectRatio);

        var font = FontManager.TakeNext();

        using var cropped = new Mat(mat, crop);
        if (cropped.Empty())
            throw new InvalidOperationException("Cropped Mat is empty");

        Rect focalPoint = Yolov8nHelper.FindFocalPoint(cropped);

        using var bitmap = SkHelper.MatToSkBitmap(cropped);
        if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
            throw new InvalidOperationException("Failed to convert cropped Mat to SKBitmap");

        var accentColor = GetAccessColor(bitmap, out var bgForText, out var shadowColor);

        // The blurred background IS the image — so text color derived from the same
        // palette will always blend in. Instead, pick text/outline colors purely on
        // contrast: if the background is dark, use bright text and a dark outline,
        // and vice versa. Use the accent color only for the outline/shadow to add
        // visual interest without sacrificing legibility.
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
                MinFontSize = crop.Width * 0.093f,
                MaxFontSize = crop.Width * 0.115f,
            },
        }, focalPoint);

        // Blurred background surface
        using var blurredSurface = CreateBlurredSurface(bitmap, sigma: 450f);  // 8-20 range safe
        using var canvas = blurredSurface.Canvas;

        DrawOverlay(bgForText, canvas, bitmap);
        DrawTexts(placements, cropped, bitmap, canvas);

        using var finalImage = blurredSurface.Snapshot();
        if (finalImage == null)
            throw new InvalidOperationException("Failed to snapshot final image");

        using var finalBitmap = SKBitmap.FromImage(finalImage);
        if (finalBitmap == null || finalBitmap.Width <= 0)
            throw new InvalidOperationException("Failed to create final bitmap from snapshot");

        using var result = new Mat();
        SkHelper.SkBitmapToMat(finalBitmap, result);

        Cv2.ImWrite("preview.jpg", result);
        Cv2.ImShow("Best Crop", result);
        Cv2.WaitKey(0);
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

    private static void DrawTexts(List<TextPlacement> placements, Mat cropped, SKBitmap bitmap, SKCanvas canvas)
    {
        if (placements == null || canvas == null || bitmap == null || bitmap.Width <= 0) return;

        var align = Aligns.Take();
        foreach (var placement in placements)
        {
            float dimAmount = Math.Max(0.4f, ImageEffectsHelper.CalculateDimAmount(cropped, placement.Bounds, placement.Element.Color));
            bool isBottom = placement.Bounds.Top > bitmap.Height / 2f;
            
            SkiaTextHelper.DrawTextGradient(canvas, placement.Bounds, dimAmount, isBottom);
            var left = (int)Math.Ceiling(canvas.LocalClipBounds.Width * 0.1);
            var top = (int) Math.Ceiling(canvas.LocalClipBounds.Height * 0.1);
            SkiaTextHelper.DrawText(canvas, placement.Element, new TextRenderOptions
            {
                HorizontalAlign = align,
                Bounds = new SKRect(
                    left,
                    top,
                    canvas.LocalClipBounds.Width - left,
                    canvas.LocalClipBounds.Height - top),
                MaxFontSize = placement.MaxFontSize,
            });
        }
    }

    private static void DrawOverlay(SKColor bgForText, SKCanvas canvas, SKBitmap bitmap)
    {
        if (canvas == null || bitmap == null || bitmap.Width <= 0) return;
        
        if(bgForText.IsDark())
            bgForText = bgForText.ShiftBrightness(90);
        else
            bgForText = bgForText.ShiftBrightness(10);

        using var overlayPaint = new SKPaint
        {
            Color = bgForText.WithAlpha((byte)(0.35f * 255)),
            BlendMode = SKBlendMode.SrcOver,
        };

        canvas.DrawRect(0, 0, bitmap.Width, bitmap.Height, overlayPaint);
    }

    private static SKColor GetAccessColor(SKBitmap bitmap, out SKColor bgForText, out SKColor shadowColor)
    {
        bgForText = SKColors.Black;
        shadowColor = SKColors.Transparent;

        if (bitmap == null || bitmap.Width <= 0)
            return SKColors.Gray;

        var palette = ImageColorPaletteHelper.ExtractPalette(bitmap);
        if (palette == null || palette.Count == 0)
            return SKColors.Gray;

        var dominantColor = palette.First();
        var accentColor = dominantColor.ShiftHue(FloatHelper.Random(-45, 45));

        bgForText = accentColor.ShiftBrightness(dominantColor.IsDark()
            ? FloatHelper.Random(30, 50)
            : FloatHelper.Random(15, 30));

        shadowColor = bgForText.IsDark()
            ? bgForText.ShiftBrightness(80).WithAlpha(180)
            : bgForText.ShiftBrightness(10).WithAlpha(180);

        return accentColor;
    }
}