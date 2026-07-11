using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using LMU.Telemetry.Core.Models;
using Point = System.Windows.Point;

namespace LMU_Telemetry.Models;

/// <summary>
/// MoTeC-style track coordinate system.
/// Defines a centerline spline and converts GPS to track coordinates (s, d).
/// s = distance along centerline
/// d = lateral offset from centerline
/// </summary>
public class TrackCenterline
{
    private List<Point> _centerlinePoints;  // Smoothed centerline in meters
    private List<double> _cumulativeDistances;  // Distance along track at each centerline point
    private double _trackLength;
    
    public double TrackLength => _trackLength;
    public int PointCount => _centerlinePoints.Count;
    
    public TrackCenterline()
    {
        _centerlinePoints = new List<Point>();
        _cumulativeDistances = new List<double>();
        _trackLength = 0;
    }
    
    /// <summary>
    /// Build centerline from multiple laps of world coordinate data.
    /// Averages positions across laps to create a stable reference.
    /// </summary>
    public void BuildFromLaps(List<List<TelemetryFrame>> laps)
    {
        if (laps.Count == 0 || laps.All(lap => lap.Count == 0))
        {
            throw new ArgumentException("No valid laps provided");
        }
        
        // Extract world coordinates from all laps
        // PosX and PosY are already in world meters from the game engine
        var lapsInMeters = new List<List<(double lapDist, Point pos)>>();
        
        foreach (var lap in laps)
        {
            var lapMeters = new List<(double lapDist, Point pos)>();
            
            foreach (var frame in lap)
            {
                // Use world coordinates directly (already in meters)
                var worldPos = new Point(frame.PosX, frame.PosY);
                lapMeters.Add((frame.LapDistance, worldPos));
            }
            
            lapsInMeters.Add(lapMeters);
        }
        
        // Find max lap distance to determine binning
        double maxLapDist = lapsInMeters.SelectMany(lap => lap).Max(p => p.lapDist);
        
        // Bin by lap distance and average positions
        // Use bins every 10 meters for reasonable resolution
        double binSize = 10.0;
        int numBins = (int)Math.Ceiling(maxLapDist / binSize);
        
        var binnedPositions = new List<Point>();
        var binnedDistances = new List<double>();
        
        for (int i = 0; i < numBins; i++)
        {
            double binStart = i * binSize;
            double binEnd = binStart + binSize;
            double binCenter = binStart + binSize / 2.0;
            
            // Collect all points in this bin across all laps
            var pointsInBin = new List<Point>();
            
            foreach (var lap in lapsInMeters)
            {
                var points = lap.Where(p => p.lapDist >= binStart && p.lapDist < binEnd)
                               .Select(p => p.pos)
                               .ToList();
                pointsInBin.AddRange(points);
            }
            
            // Average if we have points
            if (pointsInBin.Count > 0)
            {
                double avgX = pointsInBin.Average(p => p.X);
                double avgY = pointsInBin.Average(p => p.Y);
                
                binnedPositions.Add(new Point(avgX, avgY));
                binnedDistances.Add(binCenter);
            }
        }
        
        // Smooth the binned positions
        _centerlinePoints = SmoothPath(binnedPositions, windowSize: 5);
        
        // Compute cumulative distances along smoothed centerline
        ComputeCumulativeDistances();
        
        LogCenterlineInfo();
    }
    
    /// <summary>
    /// Smooth a path using moving average filter.
    /// </summary>
    private List<Point> SmoothPath(List<Point> points, int windowSize)
    {
        if (points.Count < windowSize)
        {
            return new List<Point>(points);  // Not enough points to smooth
        }
        
        var smoothed = new List<Point>();
        int halfWindow = windowSize / 2;
        
        for (int i = 0; i < points.Count; i++)
        {
            int startIdx = Math.Max(0, i - halfWindow);
            int endIdx = Math.Min(points.Count - 1, i + halfWindow);
            
            double sumX = 0;
            double sumY = 0;
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
    /// Compute cumulative distance along the centerline.
    /// </summary>
    private void ComputeCumulativeDistances()
    {
        _cumulativeDistances.Clear();
        _cumulativeDistances.Add(0);
        
        double totalDist = 0;
        
        for (int i = 1; i < _centerlinePoints.Count; i++)
        {
            var p1 = _centerlinePoints[i - 1];
            var p2 = _centerlinePoints[i];
            
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            
            totalDist += dist;
            _cumulativeDistances.Add(totalDist);
        }
        
        _trackLength = totalDist;
    }
    
    /// <summary>
    /// Convert world coordinates (X, Y in meters) to track coordinates (s, d).
    /// s = distance along track
    /// d = lateral offset (positive = right of centerline)
    /// </summary>
    public (double s, double d) WorldToTrackCoordinates(double worldX, double worldY)
    {
        var posMeters = new Point(worldX, worldY);
        
        // Find nearest point on centerline
        double minDist = double.MaxValue;
        int nearestIdx = 0;
        
        for (int i = 0; i < _centerlinePoints.Count; i++)
        {
            var cp = _centerlinePoints[i];
            double dx = posMeters.X - cp.X;
            double dy = posMeters.Y - cp.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            
            if (dist < minDist)
            {
                minDist = dist;
                nearestIdx = i;
            }
        }
        
        // Distance along track at nearest point
        double s = _cumulativeDistances[nearestIdx];
        
        // Compute lateral offset
        // Use perpendicular distance with sign
        double d = ComputeLateralOffset(posMeters, nearestIdx);
        
        return (s, d);
    }
    
    /// <summary>
    /// Compute lateral offset from centerline.
    /// Positive = right of centerline (in direction of travel).
    /// </summary>
    private double ComputeLateralOffset(Point pos, int nearestIdx)
    {
        // Get tangent direction at this point
        Point tangent;
        
        if (nearestIdx == 0)
        {
            // Use forward difference
            tangent = new Point(
                _centerlinePoints[1].X - _centerlinePoints[0].X,
                _centerlinePoints[1].Y - _centerlinePoints[0].Y
            );
        }
        else if (nearestIdx == _centerlinePoints.Count - 1)
        {
            // Use backward difference
            tangent = new Point(
                _centerlinePoints[nearestIdx].X - _centerlinePoints[nearestIdx - 1].X,
                _centerlinePoints[nearestIdx].Y - _centerlinePoints[nearestIdx - 1].Y
            );
        }
        else
        {
            // Use central difference
            tangent = new Point(
                _centerlinePoints[nearestIdx + 1].X - _centerlinePoints[nearestIdx - 1].X,
                _centerlinePoints[nearestIdx + 1].Y - _centerlinePoints[nearestIdx - 1].Y
            );
        }
        
        // Normalize tangent
        double tangentLength = Math.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
        if (tangentLength > 0)
        {
            tangent = new Point(tangent.X / tangentLength, tangent.Y / tangentLength);
        }
        
        // Normal is perpendicular to tangent (rotate 90° clockwise for right = positive)
        Point normal = new Point(tangent.Y, -tangent.X);
        
        // Vector from centerline to position
        var cp = _centerlinePoints[nearestIdx];
        Point offset = new Point(pos.X - cp.X, pos.Y - cp.Y);
        
        // Dot product gives signed lateral offset
        double lateralOffset = offset.X * normal.X + offset.Y * normal.Y;
        
        return lateralOffset;
    }
    
    /// <summary>
    /// Convert track coordinates (s, d) back to world meters (X, Y).
    /// Used for rendering the track and telemetry.
    /// </summary>
    public Point TrackToWorldMeters(double s, double d)
    {
        // Find centerline point at distance s
        int idx = FindIndexAtDistance(s);
        
        if (idx < 0 || idx >= _centerlinePoints.Count)
        {
            // Out of bounds, return first or last point
            idx = Math.Clamp(idx, 0, _centerlinePoints.Count - 1);
        }
        
        var centerPoint = _centerlinePoints[idx];
        
        // Get tangent direction
        Point tangent;
        
        if (idx == 0)
        {
            tangent = new Point(
                _centerlinePoints[1].X - _centerlinePoints[0].X,
                _centerlinePoints[1].Y - _centerlinePoints[0].Y
            );
        }
        else if (idx == _centerlinePoints.Count - 1)
        {
            tangent = new Point(
                _centerlinePoints[idx].X - _centerlinePoints[idx - 1].X,
                _centerlinePoints[idx].Y - _centerlinePoints[idx - 1].Y
            );
        }
        else
        {
            tangent = new Point(
                _centerlinePoints[idx + 1].X - _centerlinePoints[idx - 1].X,
                _centerlinePoints[idx + 1].Y - _centerlinePoints[idx - 1].Y
            );
        }
        
        // Normalize tangent
        double tangentLength = Math.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
        if (tangentLength > 0)
        {
            tangent = new Point(tangent.X / tangentLength, tangent.Y / tangentLength);
        }
        
        // Normal is perpendicular (90° clockwise)
        Point normal = new Point(tangent.Y, -tangent.X);
        
        // Add lateral offset
        return new Point(
            centerPoint.X + normal.X * d,
            centerPoint.Y + normal.Y * d
        );
    }
    
    /// <summary>
    /// Find index of centerline point at given distance s.
    /// </summary>
    private int FindIndexAtDistance(double s)
    {
        // Binary search would be better for large datasets, but linear is fine for now
        for (int i = 0; i < _cumulativeDistances.Count - 1; i++)
        {
            if (s >= _cumulativeDistances[i] && s <= _cumulativeDistances[i + 1])
            {
                // Return closest
                double d1 = Math.Abs(s - _cumulativeDistances[i]);
                double d2 = Math.Abs(s - _cumulativeDistances[i + 1]);
                return d1 < d2 ? i : i + 1;
            }
        }
        
        // Out of range
        if (s < _cumulativeDistances[0])
            return 0;
        return _cumulativeDistances.Count - 1;
    }
    
    /// <summary>
    /// Get all centerline points in world meters for rendering.
    /// </summary>
    public List<Point> GetCenterlinePoints()
    {
        return new List<Point>(_centerlinePoints);
    }
    
    private void LogCenterlineInfo()
    {
        var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LMU_TrackCenterline_Debug.txt");
        using var writer = new System.IO.StreamWriter(logPath, false);
        
        writer.WriteLine("=== TRACK CENTERLINE ===");
        writer.WriteLine($"Points: {_centerlinePoints.Count}");
        writer.WriteLine($"Track Length: {_trackLength:F1} meters");
        writer.WriteLine($"Avg point spacing: {_trackLength / _centerlinePoints.Count:F1} meters");
        writer.WriteLine();
        
        writer.WriteLine("First 10 centerline points:");
        for (int i = 0; i < Math.Min(10, _centerlinePoints.Count); i++)
        {
            writer.WriteLine($"  [{i}] s={_cumulativeDistances[i]:F1}m, pos=({_centerlinePoints[i].X:F1}, {_centerlinePoints[i].Y:F1})");
        }
        
        System.Diagnostics.Debug.WriteLine($"Track centerline built: {_centerlinePoints.Count} points, {_trackLength:F1}m total length");
        System.Diagnostics.Debug.WriteLine($"Centerline debug log: {logPath}");
    }
}
