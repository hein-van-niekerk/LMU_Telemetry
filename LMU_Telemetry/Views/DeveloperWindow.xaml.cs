using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using LMU.Telemetry.Core.Models;
using LMU.Telemetry.Core.Services;
using LMU_Telemetry.Models;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace LMU_Telemetry.Views;

/// <summary>
/// Developer Mode: generate track maps from a chosen .duckdb recording, hand-curate
/// corner positions/names, and save the result. Accuracy-over-automation companion
/// to the automatic curvature-peak detector - opened via Ctrl+Shift+D in MainWindow.
/// </summary>
public partial class DeveloperWindow : Window
{
    private List<TelemetryFrame>? _loadedFrames;
    private GeneratedTrackMap? _currentMap;
    private double[] _cumulativeDistance = Array.Empty<double>();

    private Corner? _selectedCorner;
    private bool _isDraggingCorner;
    private bool _addCornerMode;

    // World-space bounds -> canvas transform, recomputed whenever the map or canvas size changes.
    private double _minX, _maxX, _minY, _maxY, _scale, _offsetX, _offsetY;

    private readonly Dictionary<Corner, Ellipse> _markerByCorner = new();

    public DeveloperWindow()
    {
        InitializeComponent();
        SizeChanged += (_, __) => DrawTrackMap();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Nothing to persist automatically - unsaved edits are lost on close, same as
        // any other "Save" workflow in the app. No special handling needed.
    }

    // --- 1. Load ----------------------------------------------------------

    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "DuckDB recordings (*.duckdb)|*.duckdb",
            Title = "Select a telemetry recording to build a track map from"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var reader = new DuckDBTelemetryReader();
            var frames = reader.LoadTelemetryData(dlg.FileName);

            if (frames.Count == 0)
            {
                StatusText.Text = "No frames in that file";
                StatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
                return;
            }

            _loadedFrames = frames;
            LoadedFileText.Text = $"{System.IO.Path.GetFileName(dlg.FileName)}  ({frames.Count:N0} frames)";

            // Track name comes from the recording's own metadata, same lookup the
            // file selector uses - not guessed from the file name.
            var folderReader = new DuckDBTelemetryReader(System.IO.Path.GetDirectoryName(dlg.FileName) ?? ".");
            var info = folderReader.GetAvailableRecordings()
                .FirstOrDefault(r => string.Equals(r.FilePath, dlg.FileName, StringComparison.OrdinalIgnoreCase));
            TrackNameBox.Text = info?.TrackName is { Length: > 0 } tn && tn != "Unknown Track" ? tn : "";

            GenerateButton.IsEnabled = true;
            StatusText.Text = "Loaded";
            StatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Load failed: {ex.Message}";
            StatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
        }
    }

    // --- 2. Generate --------------------------------------------------------

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedFrames == null) return;

        try
        {
            var laps = _loadedFrames
                .GroupBy(f => f.CurrentLap)
                .OrderBy(g => g.Key)
                .Select(g => g.ToList())
                .ToList();

            _currentMap = TrackMapGenerator.Generate(laps);
            RebuildCumulativeDistances();
            RenumberCorners();

            MapInfoText.Text = $"{_currentMap.Points.Count} points, {_currentMap.TotalLength:F0}m, from {_currentMap.GeneratedFromLapCount} laps";
            ThresholdSlider.IsEnabled = true;
            AddCornerToggle.IsEnabled = true;
            SaveButton.IsEnabled = true;
            ClearSelection();
            DrawTrackMap();

            StatusText.Text = "Map generated";
            StatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Generate failed: {ex.Message}";
            StatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
        }
    }

    // --- 3. Corner detection threshold --------------------------------------

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ThresholdLabel.Text = $"Curvature threshold: {ThresholdSlider.Value:F4}";
        if (_currentMap == null) return;

        _currentMap.Corners = TrackMapGenerator.DetectCorners(_currentMap.Points, ThresholdSlider.Value, minDistance: 20);
        RenumberCorners();
        ClearSelection();
        DrawTrackMap();
    }

    // --- 4. Edit corners -----------------------------------------------------

    private void AddCornerToggle_Checked(object sender, RoutedEventArgs e)
    {
        _addCornerMode = true;
        AddCornerToggle.Content = "Add Corner Mode: ON (click track to add)";
    }

    private void AddCornerToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _addCornerMode = false;
        AddCornerToggle.Content = "Add Corner Mode: OFF";
    }

    private void MapCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_currentMap == null) return;
        var clickPos = e.GetPosition(MapCanvas);

        if (e.OriginalSource is Ellipse ellipse && ellipse.Tag is Corner clickedCorner)
        {
            SelectCorner(clickedCorner);
            _isDraggingCorner = true;
            MapCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (_addCornerMode)
        {
            var (nearestIdx, worldPos) = FindNearestTrackPoint(clickPos);
            if (nearestIdx < 0) return;

            var newCorner = new Corner
            {
                Position = worldPos,
                Curvature = _currentMap.Points[nearestIdx].Curvature,
                Name = null
            };
            _currentMap.Corners.Add(newCorner);
            RenumberCorners();
            DrawTrackMap();
            SelectCorner(newCorner);
        }
    }

    private void MapCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingCorner || _selectedCorner == null || _currentMap == null) return;

        var (nearestIdx, worldPos) = FindNearestTrackPoint(e.GetPosition(MapCanvas));
        if (nearestIdx < 0) return;

        _selectedCorner.Position = worldPos;
        _selectedCorner.Curvature = _currentMap.Points[nearestIdx].Curvature;
        RenumberCorners();
        DrawTrackMap();
        UpdateSelectedCornerDetail();
    }

    private void MapCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isDraggingCorner)
        {
            _isDraggingCorner = false;
            MapCanvas.ReleaseMouseCapture();
        }
    }

    private void SelectCorner(Corner corner)
    {
        _selectedCorner = corner;
        SelectedCornerPanel.Visibility = Visibility.Visible;
        SelectedCornerHeader.Text = $"Selected: Corner #{corner.Number}";
        CornerNameBox.Text = corner.Name ?? "";
        UpdateSelectedCornerDetail();
        HighlightSelectedMarker();
    }

    private void ClearSelection()
    {
        _selectedCorner = null;
        SelectedCornerPanel.Visibility = Visibility.Collapsed;
        HighlightSelectedMarker();
    }

    private void UpdateSelectedCornerDetail()
    {
        if (_selectedCorner == null) return;
        SelectedCornerDetail.Text = $"curvature={_selectedCorner.Curvature:F5}   pos=({_selectedCorner.Position.X:F1}, {_selectedCorner.Position.Y:F1})";
    }

    private void CornerNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_selectedCorner == null) return;
        _selectedCorner.Name = CornerNameBox.Text;
        DrawTrackMap(); // refresh the on-map label
    }

    private void DeleteCornerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCorner == null || _currentMap == null) return;
        _currentMap.Corners.Remove(_selectedCorner);
        RenumberCorners();
        ClearSelection();
        DrawTrackMap();
    }

    // --- 5. Save --------------------------------------------------------------

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMap == null) return;

        var trackName = TrackNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(trackName))
        {
            StatusText.Text = "Enter a track name before saving";
            StatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
            return;
        }

        try
        {
            TrackMapStorage.Save(_currentMap, trackName);
            StatusText.Text = $"Saved '{trackName}' ({_currentMap.Corners.Count} corners)";
            StatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
            StatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
        }
    }

    // --- Geometry helpers -------------------------------------------------------

    private void RebuildCumulativeDistances()
    {
        if (_currentMap == null) { _cumulativeDistance = Array.Empty<double>(); return; }

        var points = _currentMap.Points;
        _cumulativeDistance = new double[points.Count];
        for (int i = 1; i < points.Count; i++)
        {
            var dx = points[i].Position.X - points[i - 1].Position.X;
            var dy = points[i].Position.Y - points[i - 1].Position.Y;
            _cumulativeDistance[i] = _cumulativeDistance[i - 1] + Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// Renumbers corners sequentially by lap position and recomputes each corner's
    /// LapDistance as the segment distance from the previous corner - matching the
    /// shape CornerDetector.DetectCorners already produces, so hand-added/moved/
    /// deleted corners stay consistent with detector output.
    /// </summary>
    private void RenumberCorners()
    {
        if (_currentMap == null || _currentMap.Points.Count == 0) return;

        var ordered = _currentMap.Corners
            .Select(c => (Corner: c, Index: FindNearestPointIndex(c.Position)))
            .OrderBy(t => t.Index)
            .ToList();

        double prevDist = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            var (corner, idx) = ordered[i];
            var dist = idx >= 0 && idx < _cumulativeDistance.Length ? _cumulativeDistance[idx] : 0;
            corner.Number = i + 1;
            corner.LapDistance = dist - prevDist;
            prevDist = dist;
        }

        _currentMap.Corners = ordered.Select(t => t.Corner).ToList();
    }

    private int FindNearestPointIndex(Point worldPos)
    {
        if (_currentMap == null) return -1;
        int best = -1;
        double bestDistSq = double.MaxValue;
        var points = _currentMap.Points;
        for (int i = 0; i < points.Count; i++)
        {
            var dx = points[i].Position.X - worldPos.X;
            var dy = points[i].Position.Y - worldPos.Y;
            var d = dx * dx + dy * dy;
            if (d < bestDistSq) { bestDistSq = d; best = i; }
        }
        return best;
    }

    private (int index, Point worldPos) FindNearestTrackPoint(Point canvasPos)
    {
        if (_currentMap == null) return (-1, default);
        var worldClick = CanvasToWorld(canvasPos);
        int idx = FindNearestPointIndex(worldClick);
        return idx < 0 ? (-1, default) : (idx, _currentMap.Points[idx].Position);
    }

    // --- Canvas transform + drawing ---------------------------------------------

    private void ComputeTransform()
    {
        if (_currentMap == null || _currentMap.Points.Count == 0) return;

        var canvasWidth = MapCanvas.ActualWidth;
        var canvasHeight = MapCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0) return;

        var points = _currentMap.Points;
        _minX = points.Min(p => p.Position.X);
        _maxX = points.Max(p => p.Position.X);
        _minY = points.Min(p => p.Position.Y);
        _maxY = points.Max(p => p.Position.Y);

        var rangeX = Math.Max(1, _maxX - _minX);
        var rangeY = Math.Max(1, _maxY - _minY);

        _scale = Math.Min(canvasWidth / rangeX, canvasHeight / rangeY) * 0.85;
        _offsetX = (canvasWidth - rangeX * _scale) / 2;
        _offsetY = (canvasHeight - rangeY * _scale) / 2;
    }

    private Point WorldToCanvas(Point world)
    {
        var x = _offsetX + (world.X - _minX) * _scale;
        var y = _offsetY + (_maxY - world.Y) * _scale; // flip Y for screen coordinates
        return new Point(x, y);
    }

    private Point CanvasToWorld(Point canvas)
    {
        if (_scale <= 0) return default;
        var x = (canvas.X - _offsetX) / _scale + _minX;
        var y = _maxY - (canvas.Y - _offsetY) / _scale;
        return new Point(x, y);
    }

    private void DrawTrackMap()
    {
        MapCanvas.Children.Clear();
        _markerByCorner.Clear();
        if (_currentMap == null || _currentMap.Points.Count < 2) return;

        ComputeTransform();
        if (_scale <= 0) return;

        // Centerline
        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
            StrokeThickness = 2
        };
        foreach (var p in _currentMap.Points)
        {
            polyline.Points.Add(WorldToCanvas(p.Position));
        }
        MapCanvas.Children.Add(polyline);

        // Corner markers
        foreach (var corner in _currentMap.Corners)
        {
            var canvasPos = WorldToCanvas(corner.Position);
            bool selected = corner == _selectedCorner;

            var marker = new Ellipse
            {
                Width = selected ? 14 : 10,
                Height = selected ? 14 : 10,
                Fill = new SolidColorBrush(selected ? Color.FromRgb(255, 215, 0) : Color.FromRgb(255, 100, 100)),
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Tag = corner,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Canvas.SetLeft(marker, canvasPos.X - marker.Width / 2);
            Canvas.SetTop(marker, canvasPos.Y - marker.Height / 2);
            MapCanvas.Children.Add(marker);
            _markerByCorner[corner] = marker;

            var label = new TextBlock
            {
                Text = string.IsNullOrEmpty(corner.Name) ? $"#{corner.Number}" : $"#{corner.Number} {corner.Name}",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, canvasPos.X + 8);
            Canvas.SetTop(label, canvasPos.Y - 8);
            MapCanvas.Children.Add(label);
        }

        CornerCountText.Text = $"Corners: {_currentMap.Corners.Count}";
    }

    private void HighlightSelectedMarker() => DrawTrackMap();
}
