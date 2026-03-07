using Aco228.ContextualImage.Models;
using OpenCvSharp;
using SkiaSharp;

namespace Aco228.ContextualImage.Infrastructure;

public class TextPlacementRequest
{
    public required TextElement Element { get; set; }
    public float MinFontSize { get; set; } = 12f;
    public float MaxFontSize { get; set; } = 80f;
}

public class TextPlacement
{
    public required TextElement Element { get; set; }
    public required SKRect Bounds { get; set; }
    public float MaxFontSize { get; set; }
}

public static class TextPlacementHelper
{
    public static List<TextPlacement> FindPlacements(Mat croppedMat, List<TextPlacementRequest> requests,
        Rect focalPoint)
    {
        int w = croppedMat.Width;
        int h = croppedMat.Height;

        int marginX = (int) (w * 0.05f);
        int marginY = (int) (h * 0.05f);
        int slotW = w - marginX * 2;

        // Sort largest first
        var sortedRequests = requests.OrderByDescending(r => r.MaxFontSize).ToList();

        // Calculate minimum total height needed (all requests at MinFontSize)
        int totalMinH = sortedRequests.Sum(r =>
        {
            using var f = new SKFont(r.Element.Font, r.MinFontSize);
            var lines = SkiaTextHelper.BreakIntoLines(r.Element.Text, f, slotW);
            return (int) (lines.Count * r.MinFontSize * 1.2f);
        });

        // Shrink focal point if it leaves no room for text
        // Ensure at least totalMinH + 2*marginY is available outside focal point
        int fpTop    = Math.Max(focalPoint.Y, marginY);
        int fpBottom = Math.Min(focalPoint.Y + focalPoint.Height, h - marginY);

        int topStripH    = fpTop - marginY;
        int bottomStripH = h - marginY - fpBottom;

        // If focal point is too greedy, shrink it from both sides proportionally
        int totalAvailable = topStripH + bottomStripH;
        if (totalAvailable < totalMinH)
        {
            int deficit = totalMinH - totalAvailable;
            int shrinkTop    = deficit / 2;
            int shrinkBottom = deficit - shrinkTop;
            fpTop    = Math.Max(marginY, fpTop - shrinkTop);
            fpBottom = Math.Min(h - marginY, fpBottom + shrinkBottom);
            topStripH    = fpTop - marginY;
            bottomStripH = h - marginY - fpBottom;
        }

        // Build strips — each is (top, bottom)
        var bottomStrip = (top: fpBottom, bottom: h - marginY, height: bottomStripH);
        var topStrip    = (top: marginY,  bottom: fpTop,        height: topStripH);

        // Assign requests to strips — largest to biggest strip, rest stack in remaining space
        // If one strip can't fit anything, put everything in the other
        var placements = new List<TextPlacement>();
        int bottomCursor = bottomStrip.bottom; // fill bottom strip from bottom up
        int topCursor    = topStrip.top;       // fill top strip from top down

        for (int i = 0; i < sortedRequests.Count; i++)
        {
            var request = sortedRequests[i];

            // Decide which strip: largest gets biggest, rest get whatever fits
            bool useBottom;
            if (i == 0)
                useBottom = bottomStrip.height >= topStrip.height;
            else
                useBottom = bottomCursor - bottomStrip.top > topStrip.bottom - topCursor;

            // Fit largest font in available space of chosen strip
            int availableH = useBottom ? bottomCursor - bottomStrip.top : topStrip.bottom - topCursor;

            // If chosen strip has no room, switch to other
            if (availableH < (int) (request.MinFontSize * 1.2f))
            {
                useBottom = !useBottom;
                availableH = useBottom ? bottomCursor - bottomStrip.top : topStrip.bottom - topCursor;
            }

            // If still no room, force into bottom
            if (availableH < (int) (request.MinFontSize * 1.2f))
            {
                useBottom = true;
                availableH = h - marginY - bottomStrip.top;
            }

            float chosenFontSize = request.MinFontSize;
            int chosenSlotH;

// Always use at least MinFontSize regardless of available space
            {
                using var minFont = new SKFont(request.Element.Font, request.MinFontSize);
                var minLines = SkiaTextHelper.BreakIntoLines(request.Element.Text, minFont, slotW);
                float minBgPad = request.Element.Background != null ? request.Element.Background.PaddingY * request.MinFontSize * 2f : 0f;
                chosenSlotH = (int)(minLines.Count * request.MinFontSize * 1.2f + minBgPad);
            }

            for (float fontSize = request.MaxFontSize; fontSize >= request.MinFontSize; fontSize -= 1f)
            {
                using var font = new SKFont(request.Element.Font, fontSize);
                var lines = SkiaTextHelper.BreakIntoLines(request.Element.Text, font, slotW);
                float bgPaddingY = request.Element.Background != null ? request.Element.Background.PaddingY * fontSize * 2f : 0f;
                int slotH = (int)(lines.Count * fontSize * 1.2f + bgPaddingY);

                if (slotH <= availableH)
                {
                    chosenFontSize = fontSize;
                    chosenSlotH = slotH;
                    break;
                }
            }

            float boundsTop, boundsBottom;
            if (useBottom)
            {
                boundsBottom = bottomCursor;
                boundsTop = bottomCursor - chosenSlotH;
                bottomCursor = (int) boundsTop;
                // Snap very bottom placement to image edge
                if (boundsBottom >= h - marginY - 5)
                    boundsBottom = h - marginY;
            }
            else
            {
                boundsTop = topCursor;
                boundsBottom = topCursor + chosenSlotH;
                topCursor = (int) boundsBottom;
            }

            placements.Add(new TextPlacement
            {
                Element = request.Element,
                Bounds = new SKRect(marginX, boundsTop, w - marginX, boundsBottom),
                MaxFontSize = chosenFontSize,
            });
        }

        return placements
            .OrderBy(p => requests.IndexOf(requests.First(r => r.Element == p.Element)))
            .ToList();
    }

    private static bool Overlaps(SKRect a, SKRect b)
        => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    // Reuse from SmartCropHelper — or move to shared helper
    private static Mat ComputeSaliencyMap(Mat src)
        => SmartCropHelper.ComputeSaliencyMap(src); // make internal method internal/public

    private static double IntegralSum(Mat integral, int x, int y, int w, int h)
        => integral.At<double>(y + h, x + w)
           - integral.At<double>(y + h, x)
           - integral.At<double>(y, x + w)
           + integral.At<double>(y, x);
}