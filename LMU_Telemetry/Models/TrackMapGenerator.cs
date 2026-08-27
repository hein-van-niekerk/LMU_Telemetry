using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Point = System.Windows.Point;

namespace LMU_Telemetry.Models;

/// <summary>
/// Generates a canonical track map from multiple laps using statistical averaging and smoothing.
/// Implements the algorithm: normalize → resample → average → smooth → calculate curvature.
/// </summary>
public class TrackMapGenerator
{
    /// <summary>
    /// Generate a canonical track map from multiple laps.
    /// </summary>
    /// <param name="laps">List of laps, each containing telemetry frames</param>
    /// <param name="resamplePointCount">Number of points to resample each lap to (default: 500)</param>
    /// <param name="smoothingWindowSize">Moving average window for smoothing (default: 15)</param>
    /// <returns>Generated track map</returns>
    public static GeneratedTrackMap Generate(List<List<TelemetryFrame>> laps, int resamplePointCount = 500, int smoothingWindowSize = 15)
    {
        if (laps == null || laps.Count == 0)
            throw new ArgumentException("At least one lap is required");

        // Filter out incomplete laps
        laps = laps.Where(lap => lap.Count > 100).ToList();
        
        if (laps.Count == 0)
            throw new ArgumentException("No valid laps found (need at least 100 frames per lap)");

        System.Diagnostics.Debug.WriteLine($"=== GENERATING TRACK MAP ===");
        System.Diagnostics.Debug.WriteLine($"Input: {laps.Count} laps");
        System.Diagnostics.Debug.WriteLine($"Resampling to {resamplePointCount} points per lap");

        // Step 1: Convert laps to world coordinates
        var worldLaps = ConvertLapsToWorldCoordinates(laps);
        
        // Step 2: Resample all laps to same number of points along distance (skip normalization)
        var resampledLaps = ResampleLapsAlongDistance(worldLaps, resamplePointCount);
        
        // Step 4: Average across all laps
        var averagedPoints = AverageAcrossLaps(resampledLaps);
        
        // Step 5: Smooth the centerline
        var smoothedPoints = SmoothPath(averagedPoints, smoothingWindowSize);
        
        // Step 6: Calculate heading and curvature (keep in normalized frame)
        var trackPoints = CalculateHeadingAndCurvature(smoothedPoints);
        
        System.Diagnostics.Debug.WriteLine($"Generated track map with {trackPoints.Count} points");
        
        // Step 7: Detect corners from curvature
        // Disabled: corner numbers were not working correctly
        // var corners = DetectCorners(trackPoints);
        var corners = new List<Corner>();
        
        return new GeneratedTrackMap
        {
            Points = trackPoints,
            Corners = corners,
            GeneratedFromLapCount = laps.Count,
            TotalLength = CalculateTotalLength(trackPoints),
            GeneratedDateTime = DateTime.Now
        };
    }

    /// <summary>
    /// Convert telemetry frames to world coordinates (extract X, Y from PosX, PosY)
    /// </summary>
    private static List<List<Point>> ConvertLapsToWorldCoordinates(List<List<TelemetryFrame>> laps)
    {
        return laps.Select(lap =>
            lap.Select(frame => new Point(frame.PosX, frame.PosY)).ToList()
        ).ToList();
    }

    /// <summary>
    /// Normalize all laps to common reference frame:
    /// 1. Translate so first point is at origin (0, 0)
    /// 2. Rotate so initial heading points in same direction
    /// </summary>
    private static (List<List<Point>> NormalizedLaps, Point ReferenceStart, double ReferenceAngle) NormalizeLapsToCommonFrame(List<List<Point>> laps)
    {
        if (laps.Count == 0 || laps[0].Count < 2)
            return (laps, new Point(0, 0), 0);

        var normalized = new List<List<Point>>();

        // Use first lap as reference
        var referenceLap = laps[0];
        var referenceStart = referenceLap[0];
        var referenceDirection = new Point(
            referenceLap[10].X - referenceStart.X,
            referenceLap[10].Y - referenceStart.Y
        );
        double referenceAngle = Math.Atan2(referenceDirection.Y, referenceDirection.X);

        foreach (var lap in laps)
        {
            if (lap.Count < 2) continue;

            var lapStart = lap[0];
            var lapDirection = new Point(
                lap[Math.Min(10, lap.Count - 1)].X - lapStart.X,
                lap[Math.Min(10, lap.Count - 1)].Y - lapStart.Y
            );
            double lapAngle = Math.Atan2(lapDirection.Y, lapDirection.X);
            double rotationAngle = referenceAngle - lapAngle;

            var normalizedLap = new List<Point>();
            
            foreach (var point in lap)
            {
                // Translate to origin
                double x = point.X - lapStart.X;
                double y = point.Y - lapStart.Y;
                
                // Rotate to align with reference direction
                double cosTheta = Math.Cos(rotationAngle);
                double sinTheta = Math.Sin(rotationAngle);
                double xRotated = x * cosTheta - y * sinTheta;
                double yRotated = x * sinTheta + y * cosTheta;
                
                normalizedLap.Add(new Point(xRotated, yRotated));
            }
            
            normalized.Add(normalizedLap);
        }

        return (normalized, referenceStart, referenceAngle);
    }

    private static List<Point> ApplyInverseNormalization(List<Point> points, Point referenceStart, double referenceAngle)
    {
        if (points.Count == 0)
            return points;

        double cosTheta = Math.Cos(referenceAngle);
        double sinTheta = Math.Sin(referenceAngle);

        return points.Select(p =>
        {
            // Rotate back to world frame
            double x = p.X * cosTheta - p.Y * sinTheta;
            double y = p.X * sinTheta + p.Y * cosTheta;

            // Translate back to reference start
            return new Point(x + referenceStart.X, y + referenceStart.Y);
        }).ToList();
    }

    /// <summary>
    /// Resample each lap to have the same number of points along distance.
    /// This ensures all laps align spatially for averaging.
    /// </summary>
    private static List<List<Point>> ResampleLapsAlongDistance(List<List<Point>> laps, int targetPointCount)
    {
        var resampled = new List<List<Point>>();

        foreach (var lap in laps)
        {
            if (lap.Count < 2) continue;

            // Calculate cumulative distance along this lap
            var distances = new List<double> { 0 };
            for (int i = 1; i < lap.Count; i++)
            {
                double dx = lap[i].X - lap[i - 1].X;
                double dy = lap[i].Y - lap[i - 1].Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                distances.Add(distances[i - 1] + dist);
            }

            double totalDistance = distances.Last();
            if (totalDistance < 1) continue; // Skip degenerate laps

            // Resample at uniform distance intervals
            var resampledLap = new List<Point>();
            double deltaS = totalDistance / (targetPointCount - 1);

            for (int k = 0; k < targetPointCount; k++)
            {
                double targetDistance = k * deltaS;
                
                // Find segment containing this distance
                int segmentIdx = 0;
                for (int i = 0; i < distances.Count - 1; i++)
                {
                    if (targetDistance >= distances[i] && targetDistance <= distances[i + 1])
                    {
                        segmentIdx = i;
                        break;
                    }
                }

                // Interpolate position along segment
                double segmentStart = distances[segmentIdx];
                double segmentEnd = distances[Math.Min(segmentIdx + 1, distances.Count - 1)];
                double segmentLength = segmentEnd - segmentStart;
                
                double t = segmentLength > 0 
                    ? (targetDistance - segmentStart) / segmentLength 
                    : 0;
                
                var p1 = lap[segmentIdx];
                var p2 = lap[Math.Min(segmentIdx + 1, lap.Count - 1)];
                
                double x = p1.X + t * (p2.X - p1.X);
                double y = p1.Y + t * (p2.Y - p1.Y);
                
                resampledLap.Add(new Point(x, y));
            }

            resampled.Add(resampledLap);
        }

        return resampled;
    }

    /// <summary>
    /// Average point positions across all laps at each resampled location.
    /// </summary>
    private static List<Point> AverageAcrossLaps(List<List<Point>> resampledLaps)
    {
        if (resampledLaps.Count == 0 || resampledLaps[0].Count == 0)
            return new List<Point>();

        int pointCount = resampledLaps[0].Count;
        var averaged = new List<Point>();

        for (int k = 0; k < pointCount; k++)
        {
            double sumX = 0, sumY = 0;
            int count = 0;

            foreach (var lap in resampledLaps)
            {
                if (k < lap.Count)
                {
                    sumX += lap[k].X;
                    sumY += lap[k].Y;
                    count++;
                }
            }

            if (count > 0)
            {
                averaged.Add(new Point(sumX / count, sumY / count));
            }
        }

        return averaged;
    }

    /// <summary>
    /// Smooth the path using moving average filter.
    /// </summary>
    private static List<Point> SmoothPath(List<Point> points, int windowSize)
    {
        if (points.Count == 0 || windowSize < 1)
            return new List<Point>(points);

        var smoothed = new List<Point>();
        int halfWindow = windowSize / 2;

        for (int i = 0; i < points.Count; i++)
        {
            int startIdx = Math.Max(0, i - halfWindow);
            int endIdx = Math.Min(points.Count - 1, i + halfWindow);
            
            double sumX = 0, sumY = 0;
            int count = 0;

            for (int j = startIdx; j <= endIdx; j++)
            {
                sumX += points[j].X;
                sumY += points[j].Y;
                count++;
            }

            smoothed.Add(new Point(sumX / count, sumY / count));
        }

        return smoothed;
    }

    /// <summary>
    /// Calculate heading angle and curvature for each point.
    /// </summary>
    private static List<TrackPoint> CalculateHeadingAndCurvature(List<Point> points)
    {
        var trackPoints = new List<TrackPoint>();
        
        if (points.Count < 3)
        {
            // Not enough points for derivatives
            foreach (var point in points)
            {
                trackPoints.Add(new TrackPoint
                {
                    Position = point,
                    Heading = 0,
                    Curvature = 0
                });
            }
            return trackPoints;
        }

        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            
            // Calculate heading (tangent direction)
            double heading;
            if (i == 0)
            {
                // Forward difference
                heading = Math.Atan2(
                    points[i + 1].Y - points[i].Y,
                    points[i + 1].X - points[i].X
                );
            }
            else if (i == points.Count - 1)
            {
                // Backward difference
                heading = Math.Atan2(
                    points[i].Y - points[i - 1].Y,
                    points[i].X - points[i - 1].X
                );
            }
            else
            {
                // Central difference
                heading = Math.Atan2(
                    points[i + 1].Y - points[i - 1].Y,
                    points[i + 1].X - points[i - 1].X
                );
            }

            // Calculate curvature (rate of change of heading)
            double curvature = 0;
            if (i > 0 && i < points.Count - 1)
            {
                // First derivatives (velocity)
                double dx = (points[i + 1].X - points[i - 1].X) / 2.0;
                double dy = (points[i + 1].Y - points[i - 1].Y) / 2.0;
                
                // Second derivatives (acceleration)
                double ddx = points[i + 1].X - 2 * points[i].X + points[i - 1].X;
                double ddy = points[i + 1].Y - 2 * points[i].Y + points[i - 1].Y;
                
                // Curvature formula: κ = |x'y'' - y'x''| / (x'² + y'²)^(3/2)
                double numerator = Math.Abs(dx * ddy - dy * ddx);
                double denominator = Math.Pow(dx * dx + dy * dy, 1.5);
                
                if (denominator > 1e-6)
                {
                    curvature = numerator / denominator;
                }
            }

            trackPoints.Add(new TrackPoint
            {
                Position = point,
                Heading = heading,
                Curvature = curvature
            });
        }

        return trackPoints;
    }

    /// <summary>
    /// Calculate total track length from points.
    /// </summary>
    private static double CalculateTotalLength(List<TrackPoint> points)
    {
        double totalLength = 0;
        for (int i = 1; i < points.Count; i++)
        {
            double dx = points[i].Position.X - points[i - 1].Position.X;
            double dy = points[i].Position.Y - points[i - 1].Position.Y;
            totalLength += Math.Sqrt(dx * dx + dy * dy);
        }
        return totalLength;
    }

    /// <summary>
    /// Detect corners from track points based on curvature peaks.
    /// </summary>
    private static List<Corner> DetectCorners(List<TrackPoint> trackPoints)
    {
        var corners = new List<Corner>();
        if (trackPoints.Count < 10) return corners;

        // Find local maxima of curvature (high curvature = tight corner)
        var cornerIndices = new List<(int index, double curvature)>();
        double curvatureThreshold = 0.005; // Minimum curvature to consider as corner

        for (int i = 3; i < trackPoints.Count - 3; i++)
        {
            double curvature = trackPoints[i].Curvature;
            
            // Check if this is a local maximum and above threshold
            if (curvature > curvatureThreshold &&
                curvature > trackPoints[i - 1].Curvature &&
                curvature > trackPoints[i - 2].Curvature &&
                curvature > trackPoints[i + 1].Curvature &&
                curvature > trackPoints[i + 2].Curvature)
            {
                cornerIndices.Add((i, curvature));
            }
        }

        // Filter to remove adjacent peaks (same corner detected multiple times)
        var filteredCorners = new List<(int index, double curvature)>();
        int minDistance = 20; // Minimum points between corners

        foreach (var (index, curvature) in cornerIndices)
        {
            if (filteredCorners.Count == 0 || 
                index - filteredCorners.Last().index >= minDistance)
            {
                filteredCorners.Add((index, curvature));
            }
        }

        // Create Corner objects with sequential numbering
        double lapDistance = 0;
        for (int i = 0; i < filteredCorners.Count; i++)
        {
            int idx = filteredCorners[i].index;
            var point = trackPoints[idx];

            // Calculate distance along track up to this point
            if (idx > 0)
            {
                for (int j = 1; j <= idx; j++)
                {
                    double dx = trackPoints[j].Position.X - trackPoints[j - 1].Position.X;
                    double dy = trackPoints[j].Position.Y - trackPoints[j - 1].Position.Y;
                    lapDistance += Math.Sqrt(dx * dx + dy * dy);
                }
            }

            corners.Add(new Corner
            {
                Number = i + 1,
                Position = point.Position,
                Curvature = point.Curvature,
                LapDistance = lapDistance
            });

            lapDistance = 0; // Reset for next calculation
        }

        System.Diagnostics.Debug.WriteLine($"Detected {corners.Count} corners on track");
        return corners;
    }
}

/// <summary>
/// Represents a point on the generated track map with geometric properties.
/// </summary>
public class TrackPoint
{
    public Point Position { get; set; }      // X, Y coordinates in meters
    public double Heading { get; set; }      // Heading angle in radians
    public double Curvature { get; set; }    // Curvature (1/radius of turn)
    public double? LeftWidth { get; set; }   // metres left of centreline (null if unknown)
    public double? RightWidth { get; set; }  // metres right of centreline (null if unknown)
}

/// <summary>
/// Container for a generated track map with metadata.
/// </summary>
public class GeneratedTrackMap
{
    public List<TrackPoint> Points { get; set; } = new();
    public List<Corner> Corners { get; set; } = new();
    public int GeneratedFromLapCount { get; set; }
    public double TotalLength { get; set; }
    public DateTime GeneratedDateTime { get; set; }
    public string TrackName { get; set; } = "Unknown";
    
    /// <summary>
    /// Get just the positions for rendering.
    /// </summary>
    public List<Point> GetPositions()
    {
        return Points.Select(p => p.Position).ToList();
    }
}

/// <summary>
/// Represents a detected corner on the track.
/// </summary>
public class Corner
{
    public int Number { get; set; }
    public Point Position { get; set; }
    public double Curvature { get; set; }
    public double LapDistance { get; set; }
}
