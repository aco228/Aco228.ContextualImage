using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Aco228.ContextualImage.Helpers;

public static class SmartCropHelper
{
    public static Rect FindBestCrop(Mat mat, string aspectRatio)
    {
        var ratio = ParseAspectRatio(aspectRatio);
        var cropSize = CalculateCropSize(mat, ratio);
        
        // Calculate maximum possible positions
        int maxX = mat.Width - cropSize.Width;
        int maxY = mat.Height - cropSize.Height;
        
        if (maxX < 0 || maxY < 0)
        {
            throw new ArgumentException("Crop size larger than image dimensions");
        }
        
        // Convert to grayscale for processing
        using var gray = new Mat();
        Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
        
        // Apply Gaussian blur to reduce noise
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
        
        // Detect edges using Canny
        using var edges = new Mat();
        Cv2.Canny(blurred, edges, 50, 150);
        
        // Try multiple crop positions and score them
        var bestCrop = FindBestCropPosition(edges, cropSize, maxX, maxY);
        
        return bestCrop;
    }
    
    private static double ParseAspectRatio(string aspectRatio)
    {
        var parts = aspectRatio.Split(':');
        if (parts.Length != 2 || 
            !double.TryParse(parts[0], out double width) || 
            !double.TryParse(parts[1], out double height) ||
            width <= 0 || height <= 0)
        {
            throw new ArgumentException($"Invalid aspect ratio format: {aspectRatio}. Expected format: 'width:height' (e.g., '16:9', '1:1')");
        }
        
        return width / height;
    }
    
    private static Size CalculateCropSize(Mat mat, double ratio)
    {
        double imageRatio = (double)mat.Width / mat.Height;
        
        int cropWidth, cropHeight;
        
        if (imageRatio > ratio)
        {
            // Image is wider than target ratio, crop width
            cropHeight = mat.Height;
            cropWidth = (int)(cropHeight * ratio);
        }
        else
        {
            // Image is taller than target ratio, crop height
            cropWidth = mat.Width;
            cropHeight = (int)(cropWidth / ratio);
        }
        
        return new Size(cropWidth, cropHeight);
    }
    
    private static Rect FindBestCropPosition(Mat edges, Size cropSize, int maxX, int maxY)
    {
        // Define grid of positions to test (optimize for performance)
        int stepX = Math.Max(1, maxX / 10);
        int stepY = Math.Max(1, maxY / 10);
        
        Rect bestCrop = new Rect(0, 0, cropSize.Width, cropSize.Height);
        double bestScore = double.MinValue;
        
        // Try positions with emphasis on rule of thirds
        var candidatePositions = GetCandidatePositions(maxX, maxY, cropSize, stepX, stepY);
        
        foreach (var pos in candidatePositions)
        {
            var crop = new Rect(pos.X, pos.Y, cropSize.Width, cropSize.Height);
            double score = ScoreCrop(edges, crop);
            
            if (score > bestScore)
            {
                bestScore = score;
                bestCrop = crop;
            }
        }
        
        return bestCrop;
    }
    
    private static List<Point> GetCandidatePositions(int maxX, int maxY, Size cropSize, int stepX, int stepY)
    {
        var positions = new List<Point>();
        
        // Add rule of thirds positions
        int thirdX1 = (maxX * 1) / 3;
        int thirdX2 = (maxX * 2) / 3;
        int thirdY1 = (maxY * 1) / 3;
        int thirdY2 = (maxY * 2) / 3;
        
        // Rule of thirds intersection points
        positions.Add(new Point(thirdX1, thirdY1));
        positions.Add(new Point(thirdX1, thirdY2));
        positions.Add(new Point(thirdX2, thirdY1));
        positions.Add(new Point(thirdX2, thirdY2));
        
        // Center position
        positions.Add(new Point(maxX / 2, maxY / 2));
        
        // Edge positions
        positions.Add(new Point(0, 0));
        positions.Add(new Point(maxX, 0));
        positions.Add(new Point(0, maxY));
        positions.Add(new Point(maxX, maxY));
        
        // Grid sampling
        for (int x = 0; x <= maxX; x += stepX)
        {
            for (int y = 0; y <= maxY; y += stepY)
            {
                // Skip positions too close to already added ones
                bool tooClose = positions.Any(p => 
                    Math.Abs(p.X - x) < stepX / 2 && Math.Abs(p.Y - y) < stepY / 2);
                
                if (!tooClose)
                {
                    positions.Add(new Point(x, y));
                }
            }
        }
        
        return positions;
    }
    
    private static double ScoreCrop(Mat edges, Rect crop)
    {
        // Extract the crop region from edges
        using var cropRegion = new Mat(edges, crop);
        
        // Calculate edge density (amount of detail/activity in the region)
        double edgeDensity = Cv2.Mean(cropRegion).Val0 / 255.0;
        
        // Calculate center weight (prefer content near center)
        double centerWeight = CalculateCenterWeight(crop, edges.Width, edges.Height);
        
        // Penalize crops too close to edges (unless content is there)
        double edgePenalty = CalculateEdgePenalty(crop, edges.Width, edges.Height);
        
        // Combined score: prefer regions with moderate-high edge density, centered, not at extreme edges
        double score = edgeDensity * 0.5 + centerWeight * 0.3 - edgePenalty * 0.2;
        
        return score;
    }
    
    private static double CalculateCenterWeight(Rect crop, int imageWidth, int imageHeight)
    {
        // Calculate distance from center
        double cropCenterX = crop.X + crop.Width / 2.0;
        double cropCenterY = crop.Y + crop.Height / 2.0;
        double imageCenterX = imageWidth / 2.0;
        double imageCenterY = imageHeight / 2.0;
        
        double distance = Math.Sqrt(
            Math.Pow(cropCenterX - imageCenterX, 2) + 
            Math.Pow(cropCenterY - imageCenterY, 2)
        );
        
        double maxDistance = Math.Sqrt(
            Math.Pow(imageWidth / 2.0, 2) + 
            Math.Pow(imageHeight / 2.0, 2)
        );
        
        // Return weight: 1.0 at center, decreasing towards edges
        return 1.0 - (distance / maxDistance);
    }
    
    private static double CalculateEdgePenalty(Rect crop, int imageWidth, int imageHeight)
    {
        // Penalize being too close to image edges
        double penalty = 0;
        
        // Check distance from each edge
        penalty += crop.X < 50 ? (50 - crop.X) / 50.0 : 0; // Left edge
        penalty += crop.Y < 50 ? (50 - crop.Y) / 50.0 : 0; // Top edge
        penalty += (imageWidth - (crop.X + crop.Width)) < 50 ? (50 - (imageWidth - (crop.X + crop.Width))) / 50.0 : 0; // Right edge
        penalty += (imageHeight - (crop.Y + crop.Height)) < 50 ? (50 - (imageHeight - (crop.Y + crop.Height))) / 50.0 : 0; // Bottom edge
        
        return Math.Min(1.0, penalty);
    }
}
