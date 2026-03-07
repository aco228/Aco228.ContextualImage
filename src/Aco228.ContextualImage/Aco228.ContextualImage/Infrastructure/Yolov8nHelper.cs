using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace Aco228.ContextualImage.Infrastructure;

public static class Yolov8nHelper
{
    private const float ConfidenceThreshold = 0.3f;

    public static Rect FindFocalPoint(Mat mat)
    {
        string modelPath = Path.Combine(AppContext.BaseDirectory, "yolov8s.onnx");
        using var net = CvDnn.ReadNetFromOnnx(modelPath);

        using var blob = CvDnn.BlobFromImage(mat, 1.0 / 255.0, new Size(640, 640), swapRB: true, crop: false);
        net.SetInput(blob);

        using var output = net.Forward();

        float scaleX = (float) mat.Width / 640f;
        float scaleY = (float) mat.Height / 640f;

        output.Reshape(1, 1).GetArray(out float[] rawData);

        int rows = 8400;
        int cols = 84;

        var detections = new List<Rect>();

        for (int i = 0; i < rows; i++)
        {
            float maxClassConf = 0f;
            for (int c = 4; c < cols; c++)
            {
                float v = rawData[c * rows + i];
                if (v > maxClassConf) maxClassConf = v;
            }

            if (maxClassConf < ConfidenceThreshold) continue;

            float cx = rawData[0 * rows + i] * scaleX;
            float cy = rawData[1 * rows + i] * scaleY;
            float w  = rawData[2 * rows + i] * scaleX;
            float h  = rawData[3 * rows + i] * scaleY;

            int x = Math.Clamp((int) (cx - w / 2), 0, mat.Width);
            int y = Math.Clamp((int) (cy - h / 2), 0, mat.Height);
            int rw = Math.Clamp((int) w, 1, mat.Width - x);
            int rh = Math.Clamp((int) h, 1, mat.Height - y);

            detections.Add(new Rect(x, y, rw, rh));
        }

        if (detections.Count == 0)
            return new Rect(mat.Width / 4, mat.Height / 4, mat.Width / 2, mat.Height / 2);

        // Union of all detections
        int left   = detections.Min(d => d.Left);
        int top    = detections.Min(d => d.Top);
        int right  = detections.Max(d => d.Right);
        int bottom = detections.Max(d => d.Bottom);

        return new Rect(
            Math.Clamp(left, 0, mat.Width),
            Math.Clamp(top, 0, mat.Height),
            Math.Clamp(right - left, 1, mat.Width),
            Math.Clamp(bottom - top, 1, mat.Height));
    }
}