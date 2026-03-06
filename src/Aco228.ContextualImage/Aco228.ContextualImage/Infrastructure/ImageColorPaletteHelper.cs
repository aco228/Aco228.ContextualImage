using OpenCvSharp;

namespace Aco228.ContextualImage.Infrastructure;

public static class ImageColorPaletteHelper
{
    public record DominantColor(byte R, byte G, byte B);

    public static List<DominantColor> ExtractDominantColors(Mat mat, int colorCount = 5)
    {
        // Make continuous copy
        using var continuous = mat.Clone();
    
        using var reshaped = continuous.Reshape(3, continuous.Width * continuous.Height);
        using var floatMat = new Mat();
        reshaped.ConvertTo(floatMat, MatType.CV_32F);

        using var labels  = new Mat();
        using var centers = new Mat();

        Cv2.Kmeans(
            floatMat,
            colorCount,
            labels,
            new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 10, 1.0),
            attempts: 3,
            KMeansFlags.PpCenters,
            centers
        );

        var colors = new List<DominantColor>();
        for (int i = 0; i < colorCount; i++)
        {
            float b = centers.At<float>(i, 0);
            float g = centers.At<float>(i, 1);
            float r = centers.At<float>(i, 2);
            colors.Add(new DominantColor((byte)r, (byte)g, (byte)b));
        }

        return colors;
    }
}