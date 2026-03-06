using Aco228.Common.Helpers;
using Aco228.ContextualImage.Infrastructure;
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
        Rect crop = SmartCropHelper.FindBestCrop(fileInfo, "1:1");
        
        var primaryFontSize = FloatHelper.Random(0.09f, 0.13f);
        var secondaryFontSize = primaryFontSize * 0.7f;
        var font = FontManager.TakeNext();
        Console.WriteLine($"Font: {font.FamilyName} ({primaryText})");
        
        Rect primaryRect = TextPlacementHelper.FindBestTextRect(fileInfo, crop, primaryText, font, fontSizeVw: primaryFontSize, padding: 1);
        // Rect secondaryRect = TextPlacementHelper.FindSecondaryTextRect(crop, primaryRect, secondaryText, font, fontSizeVw: secondaryFontSize, gap: 8);
        
        using var src = Cv2.ImRead(fileInfo.FullName);
        using var preview = src.Clone();
        
        var (textColor, bgColor) = TextColorPicker.PickFromImage(preview, primaryRect);
        
        SkiaTextRenderer.DrawTextWithBackground(
            preview,
            primaryRect,
            primaryText,
            textColor: textColor,
            backgroundColor: bgColor,
            cornerRadius: FloatHelper.Random(1f, 12f),
            padding: new Random().Next(0, 24),
            blurSigma: FloatHelper.Random(1f, 24f)
        );
        
        // var (textColor2, bgColor2) = TextColorPicker.PickFromImage(preview, secondaryRect);
        // SkiaTextRenderer.DrawTextWithBackground(
        //     preview,
        //     secondaryRect,
        //     secondaryText,
        //     textColor: textColor2,
        //     backgroundColor: bgColor2,
        //     cornerRadius: FloatHelper.Random(1f, 12f),
        //     padding: new Random().Next(0, 24),
        //     blurSigma: FloatHelper.Random(1f, 24f)
        // );
        
        Cv2.Rectangle(preview, crop,     new Scalar(0, 255, 0),   3); // green  = crop
        Cv2.Rectangle(preview, primaryRect, new Scalar(0, 165, 255), 2); // orange = text area
        Cv2.ImWrite("preview.jpg", preview);
        
        Cv2.ImShow("Best Crop", preview);
        Cv2.WaitKey(0);
    }
}