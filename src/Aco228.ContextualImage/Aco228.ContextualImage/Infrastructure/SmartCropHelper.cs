using OpenCvSharp;

namespace Aco228.ContextualImage.Infrastructure;

public static class SmartCropHelper
{
    public static Rect FindBestCrop(Mat mat, string aspectRatio)
    {
        // Parse aspect ratio, e.g. "9:16", "16:9", "1:1"
        var parts = aspectRatio.Split(':');
        if (parts.Length != 2 || !double.TryParse(parts[0], out double arW) ||
            !double.TryParse(parts[1], out double arH))
            throw new ArgumentException($"Invalid aspect ratio format: '{aspectRatio}'. Expected format like '16:9'.");

        int srcW = mat.Width;
        int srcH = mat.Height;

        // Compute crop dimensions that fit within the source, preserving aspect ratio
        int cropW, cropH;
        if (srcW / arW < srcH / arH)
        {
            cropW = srcW;
            cropH = (int) Math.Round(srcW * arH / arW);
        }
        else
        {
            cropH = srcH;
            cropW = (int) Math.Round(srcH * arW / arH);
        }

        cropW = Math.Min(cropW, srcW);
        cropH = Math.Min(cropH, srcH);

        // Build saliency map (Spectral Residual)
        using var saliencyMap = ComputeSaliencyMap(mat);

        // Build face weight map
        using var faceMap = ComputeFaceWeightMap(mat);

        // Combine: saliency + face boost
        using var combinedMap = new Mat();
        Cv2.AddWeighted(saliencyMap, 0.5, faceMap, 0.5, 0, combinedMap);

        // Sliding window: find the crop position with highest total weight
        // Use integral image for O(1) window sum queries
        using var integral = new Mat();
        Cv2.Integral(combinedMap, integral, MatType.CV_64F);

        double bestScore = -1;
        int bestX = 0, bestY = 0;

        // Step size for performance (1px is fine for small images, increase for large)
        int step = Math.Max(1, Math.Min(srcW, srcH) / 200);

        for (int y = 0; y <= srcH - cropH; y += step)
        {
            for (int x = 0; x <= srcW - cropW; x += step)
            {
                double score = IntegralSum(integral, x, y, cropW, cropH);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        return new Rect(bestX, bestY, cropW, cropH);
    }
    
    public static Rect FindFocalPoint(Mat mat)
    {
        using var saliency = ComputeSaliencyMap(mat);
        using var blurred = new Mat();
        Cv2.GaussianBlur(saliency, blurred, new Size(51, 51), 0);

        // Find peak location
        Cv2.MinMaxLoc(blurred, out double minVal, out double maxVal, out _, out Point maxLoc);

        // Threshold at 60% of max — everything above this is "focal region"
        using var thresholded = new Mat();
        Cv2.Threshold(blurred, thresholded, maxVal * 0.6, 1.0, ThresholdTypes.Binary);

        thresholded.ConvertTo(thresholded, MatType.CV_8U, 255);

        // Find contours of the salient region
        Cv2.FindContours(thresholded, out Point[][] contours, out _, 
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
        {
            // Fallback: small rect around peak
            int fw = mat.Width / 4;
            int fh = mat.Height / 4;
            return new Rect(
                Math.Clamp(maxLoc.X - fw / 2, 0, mat.Width - fw),
                Math.Clamp(maxLoc.Y - fh / 2, 0, mat.Height - fh),
                fw, fh);
        }

        // Find the contour that contains the peak point
        var peakContour = contours
                              .OrderByDescending(c => Cv2.ContourArea(c))
                              .FirstOrDefault(c => Cv2.PointPolygonTest(c, new Point2f(maxLoc.X, maxLoc.Y), false) >= 0)
                          ?? contours.OrderByDescending(c => Cv2.ContourArea(c)).First();

        return Cv2.BoundingRect(peakContour);
    }

    public static Mat ComputeSaliencyMap(Mat src)
    {
        using var gray = new Mat();
        if (src.Channels() == 1)
            src.CopyTo(gray);
        else
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        int m = Cv2.GetOptimalDFTSize(gray.Rows);
        int n = Cv2.GetOptimalDFTSize(gray.Cols);

        using var padded = new Mat();
        Cv2.CopyMakeBorder(gray, padded, 0, m - gray.Rows, 0, n - gray.Cols, BorderTypes.Reflect);

        using var floatGray = new Mat();
        padded.ConvertTo(floatGray, MatType.CV_32F, 1.0 / 255.0);

        using var dft = new Mat();
        Cv2.Dft(floatGray, dft, DftFlags.ComplexOutput);

        Mat[] planes = Cv2.Split(dft);
        using var magnitude = new Mat();
        using var phase = new Mat();
        Cv2.CartToPolar(planes[0], planes[1], magnitude, phase);

        using var magnitudeShifted = new Mat();
        Cv2.Add(magnitude, new Scalar(1e-10), magnitudeShifted);

        using var logMag = new Mat();
        Cv2.Log(magnitudeShifted, logMag);

        using var smoothed = new Mat();
        Cv2.Blur(logMag, smoothed, new Size(3, 3));

        using var residual = new Mat();
        Cv2.Subtract(logMag, smoothed, residual);

        using var expResidual = new Mat();
        Cv2.Exp(residual, expResidual);

        Cv2.PolarToCart(expResidual, phase, planes[0], planes[1]);

        using var merged = new Mat();
        Cv2.Merge(planes, merged);

        using var reconstructed = new Mat();
        Cv2.Idft(merged, reconstructed, DftFlags.Scale | DftFlags.RealOutput);

        using var cropped = reconstructed[new Rect(0, 0, src.Width, src.Height)];

        using var squared = new Mat();
        Cv2.Multiply(cropped, cropped, squared);

        int ksize = Math.Max(3, (src.Width / 40) | 1);
        using var blurred = new Mat();
        Cv2.GaussianBlur(squared, blurred, new Size(ksize, ksize), 0);

        var result = new Mat();
        Cv2.Normalize(blurred, result, 0, 1, NormTypes.MinMax, MatType.CV_32F);

        foreach (var p in planes) p.Dispose();
        return result;
    }

    private static Mat ComputeFaceWeightMap(Mat src)
    {
        var result = new Mat(src.Rows, src.Cols, MatType.CV_32F, Scalar.All(0));

        // Try to load face cascade — returns empty map if not available
        string cascadePath = Path.Combine(
            AppContext.BaseDirectory,
            "haarcascade_frontalface_default.xml");

        if (!File.Exists(cascadePath))
            return result;

        using var cascade = new CascadeClassifier(cascadePath);
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(gray, gray);

        var faces = cascade.DetectMultiScale(gray, 1.1, 4, HaarDetectionTypes.ScaleImage,
            new Size(src.Width / 10, src.Height / 10));

        foreach (var face in faces)
        {
            // Expand face rect slightly
            int pad = (int) (face.Width * 0.3);
            var expanded = new Rect(
                Math.Max(0, face.X - pad),
                Math.Max(0, face.Y - pad),
                Math.Min(src.Width - face.X + pad, face.Width + pad * 2),
                Math.Min(src.Height - face.Y + pad, face.Height + pad * 2));

            // Draw a Gaussian blob over the face area
            using var mask = new Mat(src.Rows, src.Cols, MatType.CV_32F, Scalar.All(0));
            mask[expanded].SetTo(Scalar.All(1.0));

            int blobK = Math.Max(3, (expanded.Width / 2) | 1);
            Cv2.GaussianBlur(mask, mask, new Size(blobK, blobK), expanded.Width / 4.0);
            Cv2.Add(result, mask, result);
        }

        if (faces.Length > 0)
            Cv2.Normalize(result, result, 0, 1, NormTypes.MinMax, MatType.CV_32F);

        return result;
    }

    private static double IntegralSum(Mat integral, int x, int y, int w, int h)
    {
        // Integral image is (srcH+1) x (srcW+1)
        double br = integral.At<double>(y + h, x + w);
        double bl = integral.At<double>(y + h, x);
        double tr = integral.At<double>(y, x + w);
        double tl = integral.At<double>(y, x);
        return br - bl - tr + tl;
    }
}