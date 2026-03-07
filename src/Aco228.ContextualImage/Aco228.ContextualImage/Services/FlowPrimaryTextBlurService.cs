using Aco228.Common.Helpers;
using Aco228.Common.Infrastructure;
using Aco228.ContextualImage.Infrastructure;
using Aco228.ContextualImage.Models;
using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Services;

public static class FlowPrimaryTextBlurService
{
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

        var primaryElement = PrimaryTextVariations.Take()!();
        primaryElement.Text = primaryText;
        primaryElement.Font = font;
        primaryElement.OutlineColor = accentColor.IsDark() ? accentColor.ShiftBrightness(5) : accentColor.ShiftBrightness(70);
        primaryElement.ShadowColor = accentColor.IsDark() ? accentColor.ShiftBrightness(5).WithAlpha(180) : accentColor.ShiftBrightness(95).WithAlpha(100);
        primaryElement.Color = accentColor.IsDark() ? accentColor.ShiftBrightness(90) : accentColor.ShiftBrightness(20);

        if (primaryElement.Background != null)
            primaryElement.Background.Color = accentColor.IsDark() ? accentColor : accentColor.ShiftBrightness(70);

        var placements = TextPlacementHelper.FindPlacements(cropped, new List<TextPlacementRequest>
        {
            new TextPlacementRequest
            {
                Element = primaryElement,
                MinFontSize = crop.Width * 0.063f,
                MaxFontSize = crop.Width * 0.085f,
            },
        }, focalPoint);

        // Blurred background surface
        using var blurredSurface = CreateBlurredSurface(bitmap, sigma: 12f);  // 8-20 range safe
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

        sigma = Math.Clamp(sigma, 2f, 25f);  // prevent extreme values causing native issues

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

        foreach (var placement in placements)
        {
            float dimAmount = Math.Max(0.4f, ImageEffectsHelper.CalculateDimAmount(cropped, placement.Bounds, placement.Element.Color));
            bool isBottom = placement.Bounds.Top > bitmap.Height / 2f;

            SkiaTextHelper.DrawTextGradient(canvas, placement.Bounds, dimAmount, isBottom, bitmap.Height);

            SkiaTextHelper.DrawText(canvas, placement.Element, new TextRenderOptions
            {
                Bounds = placement.Bounds,
                MaxFontSize = placement.MaxFontSize,
            });
        }
    }

    private static void DrawOverlay(SKColor bgForText, SKCanvas canvas, SKBitmap bitmap)
    {
        if (canvas == null || bitmap == null || bitmap.Width <= 0) return;

        using var overlayPaint = new SKPaint
        {
            Color = bgForText.WithAlpha((byte)(0.08f * 255)),
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