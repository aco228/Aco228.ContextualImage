using OpenCvSharp;

namespace Aco228.ContextualImage.Infrastructure;

public class SmartCropHelper
{
    private static Mat ComputeSaliencyMap(Mat source)
    {
        Mat gray = new Mat();
        Mat blurred = new Mat();
        Mat saliency = new Mat();

        // Convert to grayscale
        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);

        // High-frequency saliency: difference between original and blurred
        Cv2.GaussianBlur(gray, blurred, new Size(51, 51), 0);
        Cv2.Absdiff(gray, blurred, saliency);

        // Normalize to [0, 255]
        Cv2.Normalize(saliency, saliency, 0, 255, NormTypes.MinMax);

        return saliency;
    }

    /// <summary>
    /// Finds the best crop rectangle for a target size within the source image.
    /// </summary>
    public static Rect FindBestCrop(FileInfo fileInfo, string aspectRatio)
    {
        var parts = aspectRatio.Split(':');
        if (parts.Length != 2 || !double.TryParse(parts[0], out double w) || !double.TryParse(parts[1], out double h))
            throw new ArgumentException($"Invalid aspect ratio format '{aspectRatio}'. Expected format: '4:5'");

        double targetAspectRatio = w / h;

        using var source = new Mat(fileInfo.FullName);

        int cropW, cropH;
        double sourceAspect = (double)source.Width / source.Height;

        if (sourceAspect > targetAspectRatio)
        {
            cropH = source.Height;
            cropW = (int)(cropH * targetAspectRatio);
        }
        else
        {
            cropW = source.Width;
            cropH = (int)(cropW / targetAspectRatio);
        }

        Mat saliency = ComputeSaliencyMap(source);
        return FindBestCropWindow(saliency, cropW, cropH);
    }

    private static Rect FindBestCropWindow(Mat saliency, int cropW, int cropH)
    {
        int srcW = saliency.Width;
        int srcH = saliency.Height;

        if (cropW > srcW || cropH > srcH)
            throw new ArgumentException("Crop size exceeds image size.");

        Rect bestRect = new Rect(0, 0, cropW, cropH);
        double bestScore = -1;

        int stepX = Math.Max(1, cropW / 20); // ~20 steps horizontally
        int stepY = Math.Max(1, cropH / 20); // ~20 steps vertically

        for (int y = 0; y <= srcH - cropH; y += stepY)
        {
            for (int x = 0; x <= srcW - cropW; x += stepX)
            {
                var rect = new Rect(x, y, cropW, cropH);
                Mat roi = new Mat(saliency, rect);

                double score = ComputeWindowScore(roi, cropW, cropH);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestRect = rect;
                }
            }
        }

        return bestRect;
    }

    private static double ComputeWindowScore(Mat roi, int cropW, int cropH)
    {
        // Base score: mean saliency in window
        Scalar mean = Cv2.Mean(roi);
        double score = mean.Val0;

        // Bonus: saliency near rule-of-thirds points
        int[] thirdsX = { cropW / 3, 2 * cropW / 3 };
        int[] thirdsY = { cropH / 3, 2 * cropH / 3 };
        int radius = Math.Min(cropW, cropH) / 8;

        foreach (int tx in thirdsX)
        foreach (int ty in thirdsY)
        {
            var roiRect = new Rect(
                Math.Max(0, tx - radius),
                Math.Max(0, ty - radius),
                Math.Min(roi.Width  - Math.Max(0, tx - radius), radius * 2),
                Math.Min(roi.Height - Math.Max(0, ty - radius), radius * 2)
            );
            if (roiRect.Width > 0 && roiRect.Height > 0)
            {
                Mat thirdsRegion = new Mat(roi, roiRect);
                Scalar thirdsMean = Cv2.Mean(thirdsRegion);
                score += thirdsMean.Val0 * 0.3; // 30% weight bonus
            }
        }

        return score;
    }
}