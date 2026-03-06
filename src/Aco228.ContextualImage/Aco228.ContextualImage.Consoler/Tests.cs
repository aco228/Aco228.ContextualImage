using Aco228.Common.Helpers;
using Aco228.ContextualImage.Helpers;
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
        Rect crop = SmartCropHelper.FindBestCrop(mat, "4:5");
        
        
        using var src = Cv2.ImRead(fileInfo.FullName);
        using var preview = src.Clone();
        Cv2.Rectangle(preview, crop,     new Scalar(0, 255, 0),   3); // green  = crop
        Cv2.ImWrite("preview.jpg", preview);
        
        Cv2.ImShow("Best Crop", preview);
        Cv2.WaitKey(0);
    }
}