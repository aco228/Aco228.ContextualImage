using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Infrastructure;

public class SkHelper
{
    public static SKBitmap MatToSkBitmap(Mat mat)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(mat, rgb, ColorConversionCodes.BGR2BGRA);

        var bitmap = new SKBitmap(rgb.Width, rgb.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = bitmap.GetPixels();
        System.Runtime.InteropServices.Marshal.Copy(rgb.Data, new byte[rgb.Total() * 4], 0, (int)(rgb.Total() * 4));

        unsafe
        {
            Buffer.MemoryCopy(
                (void*)rgb.Data,
                (void*)pixels,
                (long)(rgb.Total() * 4),
                (long)(rgb.Total() * 4));
        }

        return bitmap;
    }

    public static void SkBitmapToMat(SKBitmap bitmap, Mat dst)
    {
        using var tmp = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4);
        unsafe
        {
            Buffer.MemoryCopy(
                (void*)bitmap.GetPixels(),
                (void*)tmp.Data,
                (long)(bitmap.Width * bitmap.Height * 4),
                (long)(bitmap.Width * bitmap.Height * 4));
        }
        Cv2.CvtColor(tmp, dst, ColorConversionCodes.BGRA2BGR);
    }
}