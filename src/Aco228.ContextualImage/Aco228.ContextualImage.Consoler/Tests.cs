using Aco228.Common.Helpers;
using Aco228.ContextualImage.Infrastructure;
using Aco228.ContextualImage.Models;
using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Consoler;

public static class Tests
{
    public static async Task TestSmartCrop(
        string path,
        string primaryText,
        string secondaryText)
    {
        var fileInfo = new FileInfo(path);
        using var mat = new Mat(fileInfo.FullName);
        Rect crop = SmartCropHelper.FindBestCrop(mat, "1:1");

        var font = FontManager.TakeNext();

        Console.WriteLine($"Image size: {mat.Width}x{mat.Height}");
        Console.WriteLine($"Crop: {crop}");

        using var cropped = new Mat(mat, crop);

        using var bitmap = SkHelper.MatToSkBitmap(cropped);
        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        using var surfaceCanvas = surface.Canvas;
        surfaceCanvas.DrawBitmap(bitmap, 0, 0);

        var primaryElement = new TextElement
        {
            Text = primaryText,
            Font = font,
            Color = SKColors.White,
            OutlineWidth = 2f,
            OutlineColor = SKColors.Black,
            ShadowColor = SKColors.Black,
            ShadowRadius = 8f,
            Background = new TextElementBackground
            {
                Color = SKColors.Blue,
                Opacity = FloatHelper.Random(0.1f, 1f),
                CornerRadius = FloatHelper.Random(0.1f, 1f),
                BackdropBlur = FloatHelper.Random(0.5f, 15f),
            },
        };

        SkiaTextHelper.DrawText(surfaceCanvas, primaryElement, new TextRenderOptions
        {
            Bounds = new SKRect(20, crop.Height * 0.6f, crop.Width - 20, crop.Height * 0.85f),
            MaxFontSize = crop.Width * 0.07f,
        });

        using var finalImage = surface.Snapshot();
        using var finalBitmap = SKBitmap.FromImage(finalImage);

        using var result = new Mat();
        SkHelper.SkBitmapToMat(finalBitmap, result);

        Cv2.ImWrite("preview.jpg", result);
        Cv2.ImShow("Best Crop", result);
        Cv2.WaitKey(0);
    }
}