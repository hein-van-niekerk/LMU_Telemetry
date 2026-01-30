using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LMU_Telemetry.Models;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace LMU_Telemetry.Views;

public partial class CalibrationHelper : Window
{
    private List<TelemetryFrame> _frames = new();
    private List<CalibrationPointData> _calibrationPoints = new();
    private BitmapImage? _trackMapImage;
    private double _mapWidth;
    private double _mapHeight;
    
    public CalibrationHelper(List<TelemetryFrame> frames, string trackMapPath)
    {
        InitializeComponent();
        _frames = frames;
        
        LoadTrackMap(trackMapPath);
        AnalyzeGpsData();
        FindSuggestedPoints();
    }
    
    private void LoadTrackMap(string path)
    {
        try
        {
            if (path.EndsWith(".svg"))
            {
                // Convert SVG to bitmap for display
                using var svg = new Svg.Skia.SKSvg();
                svg.Load(path);
                
                if (svg.Picture != null)
                {
                    var width = (int)svg.Picture.CullRect.Width;
                    var height = (int)svg.Picture.CullRect.Height;
                    
                    using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(width, height));
                    var canvas = surface.Canvas;
                    canvas.Clear(SkiaSharp.SKColors.White);
                    canvas.DrawPicture(svg.Picture);
                    canvas.Flush();
                    
                    using var skImage = surface.Snapshot();
                    using var data = skImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                    using var ms = new System.IO.MemoryStream(data.ToArray());
                    
                    _trackMapImage = new BitmapImage();
                    _trackMapImage.BeginInit();
                    _trackMapImage.StreamSource = ms;
                    _trackMapImage.CacheOption = BitmapCacheOption.OnLoad;
                    _trackMapImage.EndInit();
                    _trackMapImage.Freeze();
                    
                    _mapWidth = width;
                    _mapHeight = height;
                }
            }
            else
            {
                _trackMapImage = new BitmapImage(new Uri(path));
                _mapWidth = _trackMapImage.PixelWidth;
                _mapHeight = _trackMapImage.PixelHeight;
            }
            
            var image = new System.Windows.Controls.Image
            {
                Source = _trackMapImage,
                Stretch = Stretch.Uniform
            };
            PreviewCanvas.Children.Add(image);
            NoMapText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load track map:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void AnalyzeGpsData()
    {
        if (_frames.Count == 0) return;
        
        var minLat = _frames.Min(f => f.PosY);
        var maxLat = _frames.Max(f => f.PosY);
        var minLon = _frames.Min(f => f.PosX);
        var maxLon = _frames.Max(f => f.PosX);
        
        GpsRangeText.Text = $"Latitude:  {minLat:F6} to {maxLat:F6}\n" +
                           $"Longitude: {minLon:F6} to {maxLon:F6}\n\n" +
                           $"Total frames: {_frames.Count:N0}";
    }
    
    private void FindSuggestedPoints()
    {
        var suggestions = new List<SuggestedCalibrationPoint>();
        
        // Start/Finish (lap distance near 0 or max)
        var startFrame = _frames.Where(f => f.LapDistance < 100).OrderBy(f => f.LapDistance).FirstOrDefault();
        if (startFrame != null)
        {
            suggestions.Add(new SuggestedCalibrationPoint
            {
                Name = "Start/Finish Line",
                LapDist = startFrame.LapDistance,
                Lat = startFrame.PosY,
                Lon = startFrame.PosX,
                Frame = startFrame
            });
        }
        
        // Find distinctive corners by speed/throttle changes
        var maxLapDist = _frames.Max(f => f.LapDistance);
        var quarters = new[] { 0.25, 0.5, 0.75 };
        
        foreach (var quarter in quarters)
        {
            var targetDist = maxLapDist * quarter;
            var frame = _frames
                .Where(f => Math.Abs(f.LapDistance - targetDist) < 200)
                .OrderBy(f => f.Speed)
                .FirstOrDefault();
            
            if (frame != null)
            {
                suggestions.Add(new SuggestedCalibrationPoint
                {
                    Name = $"Corner near {targetDist:N0}m",
                    LapDist = frame.LapDistance,
                    Lat = frame.PosY,
                    Lon = frame.PosX,
                    Frame = frame
                });
            }
        }
        
        SuggestedPoints.ItemsSource = suggestions;
    }
    
    private void PreviewCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(PreviewCanvas);
        
        // Convert canvas position to image coordinates
        var scaleX = _mapWidth / PreviewCanvas.ActualWidth;
        var scaleY = _mapHeight / PreviewCanvas.ActualHeight;
        
        var mapX = pos.X * scaleX;
        var mapY = pos.Y * scaleY;
        
        ClickedPosText.Text = $"Clicked Map Position:\nX: {mapX:F1}\nY: {mapY:F1}";
        
        // Ask if user wants to create calibration point with these coordinates
        var result = MessageBox.Show(
            $"Create calibration point at:\nMap X: {mapX:F1}\nMap Y: {mapY:F1}\n\n" +
            "You'll need to enter GPS coordinates next.",
            "Add Calibration Point?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            var inputWindow = new CalibrationPointInput(mapX, mapY);
            if (inputWindow.ShowDialog() == true)
            {
                _calibrationPoints.Add(new CalibrationPointData
                {
                    Name = inputWindow.PointName,
                    Lat = inputWindow.Latitude,
                    Lon = inputWindow.Longitude,
                    MapX = mapX,
                    MapY = mapY
                });
                
                UpdateCalibrationCode();
                MessageBox.Show($"Calibration point '{inputWindow.PointName}' added!\n\nTotal points: {_calibrationPoints.Count}", 
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
    
    private void FindLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.DataContext is SuggestedCalibrationPoint point)
        {
            var result = MessageBox.Show(
                $"GPS Coordinates for {point.Name}:\n\n" +
                $"Latitude:  {point.Lat:F6}\n" +
                $"Longitude: {point.Lon:F6}\n\n" +
                $"Lap Distance: {point.LapDist:F0}m\n" +
                $"Speed: {point.Frame.Speed:F1} km/h\n\n" +
                "Click 'Yes' to copy these GPS coordinates, then click on the map where this location is.",
                point.Name,
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            
            if (result == MessageBoxResult.Yes)
            {
                // Store the GPS coordinates in clipboard for easy reference
                Clipboard.SetText($"Lat: {point.Lat:F6}, Lon: {point.Lon:F6}");
                MessageBox.Show("GPS coordinates copied! Now click on the track map at this location.", 
                    "Ready", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
    
    private void UpdateCalibrationCode()
    {
        if (_calibrationPoints.Count == 0)
        {
            CalibrationCodeText.Text = "// No calibration points added yet.\n// Click on the track map to add points.";
            return;
        }
        
        var code = "// Copy this to TrackCalibration.cs in GetSpaCalibration() method:\n\n";
        
        foreach (var point in _calibrationPoints)
        {
            code += $"calibration.AddCalibrationPoint(\n";
            code += $"    lat: {point.Lat:F6}, lon: {point.Lon:F6},\n";
            code += $"    mapX: {point.MapX:F1}, mapY: {point.MapY:F1}\n";
            code += $"); // {point.Name}\n\n";
        }
        
        code += $"\n// Total points: {_calibrationPoints.Count}\n";
        code += "// Compute transformation using:\n";
        if (_calibrationPoints.Count == 2)
        {
            code += "calibration.ComputeTransformTwoPoints();\n";
        }
        else if (_calibrationPoints.Count > 2)
        {
            code += "calibration.ComputeTransformLeastSquares();\n";
        }
        
        CalibrationCodeText.Text = code;
    }
    
    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(CalibrationCodeText.Text);
        MessageBox.Show("Calibration code copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public class SuggestedCalibrationPoint
{
    public string Name { get; set; } = "";
    public double LapDist { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public TelemetryFrame Frame { get; set; } = new();
}

public class CalibrationPointData
{
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double MapX { get; set; }
    public double MapY { get; set; }
}
