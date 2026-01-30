using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace LMU_Telemetry.Models;

/// <summary>
/// Represents a calibration point linking GPS coordinates to map pixel coordinates
/// </summary>
public class CalibrationPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double MapX { get; set; }
    public double MapY { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// Handles conversion from GPS lat/long to track map coordinates using calibration points
/// </summary>
public class TrackCalibration
{
    private const double EarthRadiusMeters = 6371000.0;
    
    public double ReferenceLat { get; private set; }
    public double ReferenceLon { get; private set; }
    public List<CalibrationPoint> CalibrationPoints { get; private set; } = new();
    
    // Affine transform matrix: world (meters) → map (pixels)
    private Matrix _transform = Matrix.Identity;
    private bool _isCalibrated = false;
    
    // Manual rotation adjustment (degrees, applied after affine transform)
    private double _rotationDegrees = 0;
    private double _scale = 1.0;
    
    public TrackCalibration(double referenceLat, double referenceLon, double rotationDegrees = 0, double scale = 1.0)
    {
        ReferenceLat = referenceLat;
        ReferenceLon = referenceLon;
        _rotationDegrees = rotationDegrees;
        _scale = scale;
    }
    
    /// <summary>
    /// Add a calibration point (GPS → map pixel)
    /// </summary>
    public void AddCalibrationPoint(double lat, double lon, double mapX, double mapY, string name = "")
    {
        CalibrationPoints.Add(new CalibrationPoint 
        { 
            Latitude = lat, 
            Longitude = lon, 
            MapX = mapX, 
            MapY = mapY,
            Name = name
        });
        _isCalibrated = false; // Need to recompute
    }
    
    /// <summary>
    /// Compute the affine transform from calibration points using least squares
    /// Solves: [xm] = [a b tx] [xw]
    ///         [ym]   [c d ty] [yw]
    ///         [1 ]   [0 0  1] [1 ]
    /// </summary>
    public bool Calibrate()
    {
        if (CalibrationPoints.Count < 3)
        {
            System.Diagnostics.Debug.WriteLine("ERROR: Need at least 3 calibration points for affine transform");
            return false;
        }
        
        try
        {
            System.Diagnostics.Debug.WriteLine($"=== CALIBRATING WITH {CalibrationPoints.Count} POINTS ===");
            
            // Convert GPS to meters
            var worldPoints = new List<Point>();
            var mapPoints = new List<Point>();
            
            foreach (var cp in CalibrationPoints)
            {
                var (x, y) = LatLonToMeters(cp.Latitude, cp.Longitude);
                worldPoints.Add(new Point(x, y));
                mapPoints.Add(new Point(cp.MapX, cp.MapY));
                System.Diagnostics.Debug.WriteLine($"  {cp.Name}: GPS({cp.Latitude:F6},{cp.Longitude:F6}) -> Meters({x:F2},{y:F2}) -> Map({cp.MapX:F1},{cp.MapY:F1})");
            }
            
            // Solve affine transform using least squares
            // For each point: xm = a*xw + b*yw + tx
            //                 ym = c*xw + d*yw + ty
            // This is two separate linear regression problems
            
            int n = worldPoints.Count;
            
            // Compute means
            double meanXw = 0, meanYw = 0, meanXm = 0, meanYm = 0;
            for (int i = 0; i < n; i++)
            {
                meanXw += worldPoints[i].X;
                meanYw += worldPoints[i].Y;
                meanXm += mapPoints[i].X;
                meanYm += mapPoints[i].Y;
            }
            meanXw /= n;
            meanYw /= n;
            meanXm /= n;
            meanYm /= n;
            
            // Compute sums for least squares
            double sXwXw = 0, sXwYw = 0, sYwYw = 0;
            double sXwXm = 0, sYwXm = 0, sXwYm = 0, sYwYm = 0;
            
            for (int i = 0; i < n; i++)
            {
                double xw = worldPoints[i].X - meanXw;
                double yw = worldPoints[i].Y - meanYw;
                double xm = mapPoints[i].X - meanXm;
                double ym = mapPoints[i].Y - meanYm;
                
                sXwXw += xw * xw;
                sXwYw += xw * yw;
                sYwYw += yw * yw;
                sXwXm += xw * xm;
                sYwXm += yw * xm;
                sXwYm += xw * ym;
                sYwYm += yw * ym;
            }
            
            // Solve 2x2 system for a, b (x-mapping)
            double det = sXwXw * sYwYw - sXwYw * sXwYw;
            if (Math.Abs(det) < 1e-10)
            {
                System.Diagnostics.Debug.WriteLine("ERROR: Singular matrix - calibration points are collinear");
                return false;
            }
            
            double a = (sXwXm * sYwYw - sYwXm * sXwYw) / det;
            double b = (sXwXw * sYwXm - sXwYw * sXwXm) / det;
            double tx = meanXm - a * meanXw - b * meanYw;
            
            // Solve 2x2 system for c, d (y-mapping)
            double c = (sXwYm * sYwYw - sYwYm * sXwYw) / det;
            double d = (sXwXw * sYwYm - sXwYw * sXwYm) / det;
            double ty = meanYm - c * meanXw - d * meanYw;
            
            // Build WPF Matrix: [a c tx]
            //                   [b d ty]
            _transform = new Matrix(a, c, b, d, tx, ty);
            _isCalibrated = true;
            
            System.Diagnostics.Debug.WriteLine($"Affine transform matrix:");
            System.Diagnostics.Debug.WriteLine($"  [{a:F6}  {b:F6}  {tx:F2}]");
            System.Diagnostics.Debug.WriteLine($"  [{c:F6}  {d:F6}  {ty:F2}]");
            
            // Log the matrix values for debugging
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LMU_TrackMap_Debug.txt");
            try
            {
                System.IO.File.AppendAllText(logPath, $"  Matrix: a={a:F6}, b={b:F6}, c={c:F6}, d={d:F6}, tx={tx:F2}, ty={ty:F2}\n");
            }
            catch { }
            
            // Test with first calibration point
            var testWorld = worldPoints[0];
            var testMap = _transform.Transform(testWorld);
            var expectedMap = mapPoints[0];
            var error = Math.Sqrt(Math.Pow(testMap.X - expectedMap.X, 2) + Math.Pow(testMap.Y - expectedMap.Y, 2));
            System.Diagnostics.Debug.WriteLine($"Test point 0: World({testWorld.X:F2},{testWorld.Y:F2}) -> Map({testMap.X:F1},{testMap.Y:F1}), expected ({expectedMap.X:F1},{expectedMap.Y:F1}), error={error:F2}px");
            
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Calibration failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Convert lat/long to local meters (flat earth approximation - fine for circuit scale)
    /// </summary>
    public (double x, double y) LatLonToMeters(double lat, double lon)
    {
        var latRad = lat * Math.PI / 180.0;
        var lonRad = lon * Math.PI / 180.0;
        var lat0Rad = ReferenceLat * Math.PI / 180.0;
        var lon0Rad = ReferenceLon * Math.PI / 180.0;
        
        var x = (lonRad - lon0Rad) * Math.Cos(lat0Rad) * EarthRadiusMeters;
        var y = (latRad - lat0Rad) * EarthRadiusMeters;
        
        return (x, y);
    }
    
    /// <summary>
    /// Transform GPS coordinates to map pixel coordinates using the affine transform
    /// </summary>
    public Point TransformToMap(double lat, double lon)
    {
        if (!_isCalibrated)
        {
            System.Diagnostics.Debug.WriteLine("WARNING: Using uncalibrated transform");
            return new Point(0, 0);
        }
        
        // Convert to meters
        var (x, y) = LatLonToMeters(lat, lon);
        var worldPoint = new Point(x, y);
        
        // Apply affine transform
        var mapPoint = _transform.Transform(worldPoint);
        
        // Apply manual scale and rotation if specified
        if (Math.Abs(_scale - 1.0) > 0.01 || Math.Abs(_rotationDegrees) > 0.01)
        {
            // Rotate around center of map (approximate center)
            double cx = 250; // Approximate center X
            double cy = 160; // Approximate center Y
            
            // Scale first
            double dx = (mapPoint.X - cx) * _scale;
            double dy = (mapPoint.Y - cy) * _scale;
            
            // Then rotate
            if (Math.Abs(_rotationDegrees) > 0.01)
            {
                double radians = _rotationDegrees * Math.PI / 180.0;
                double cos = Math.Cos(radians);
                double sin = Math.Sin(radians);
                
                double rx = dx * cos - dy * sin;
                double ry = dx * sin + dy * cos;
                
                dx = rx;
                dy = ry;
            }
            
            mapPoint = new Point(dx + cx, dy + cy);
        }
        
        return mapPoint;
    }
}

/// <summary>
/// Predefined calibrations for known tracks
/// </summary>
public static class TrackCalibrations
{
    public static TrackCalibration GetSpaCalibration()
    {
        // Reference point: Use first calibration point as reference
        // Rotation: adjust this value (positive = counter-clockwise) to align trace with SVG
        var calibration = new TrackCalibration(
            referenceLat: 60.008762,
            referenceLon: -0.006296,
            rotationDegrees: -8,  // Negative = clockwise rotation
            scale: 1.05           // Slightly larger
        );
        
        // Calibration points from telemetry data
        calibration.AddCalibrationPoint(
            lat: 60.008762, lon: -0.006296,
            mapX: 7.5, mapY: 115.5,
            name: "Turn 1"
        );
        
        calibration.AddCalibrationPoint(
            lat: 59.994297, lon: 0.012594,
            mapX: 322.4, mapY: 2.4,
            name: "Crn after kemmel"
        );
        
        calibration.AddCalibrationPoint(
            lat: 59.992371, lon: -0.009997,
            mapX: 379.4, mapY: 125.2,
            name: "Annoying corner"
        );
        
        // Compute transformation using least squares (3 points)
        calibration.Calibrate();
        
        return calibration;
    }
}
