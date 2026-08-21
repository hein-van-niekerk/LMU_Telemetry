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
using LMU_Telemetry.Rendering;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace LMU_Telemetry.Views;

/// <summary>
/// Developer Mode: load a .duckdb recording, look at the RAW driven path for a chosen
/// lap (rendered the same way MainWindow renders it - TrackRenderer.AutoFitTransform +
/// TrackRenderer.DrawTrack, no statistical averaging/smoothing/curvature generation),
/// and hand-place/name corners directly on that real line. Accuracy-over-automation:
/// nothing here is auto-detected - opened via Ctrl+Shift+D in MainWindow.
/// </summary>
public partial class DeveloperWindow : Window
{
    private List<TelemetryFrame>? _loadedFrames;
    private List<TelemetryFrame> _currentLapFrames = new(); // raw, world-space
    private GeneratedTrackMap? _currentMap;                  // Points = raw lap positions (no curvature)
    private double[] _cumulativeDistance = Array.Empty<double>();

    // Canvas-space copy of _currentMap.Points (same index correspondence), rebuilt each
    // draw - lets hit-testing work directly in screen space like MainWindow's drag-to-scrub,
    // with no inverse-transform math needed.
    private List<TelemetryFrame> _canvasFrames = new();

    private Corner? _selectedCorner;
    private bool _isDraggingCorner;
    private bool _addCornerMode;

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

            // Populate the lap selector - same >100-frame filter the old generator used,
            // so partial/aborted laps don't clutter the list.
            var laps = frames
                .GroupBy(f => f.CurrentLap)
                .Where(g => g.Count() > 100)
                .OrderBy(g => g.Key)
                .ToList();

            LapSelector.ItemsSource = laps.Select(g => $"Lap {g.Key}  ({g.Count()} frames)").ToList();
            LapSelector.Tag = laps.Select(g => g.ToList()).ToList(); // stash the actual frame lists
            LapSelector.IsEnabled = laps.Count > 0;
            AddCornerToggle.IsEnabled = false;
            SaveButton.IsEnabled = false;

            if (laps.Count > 0)
            {
                LapSelector.SelectedIndex = 0; // triggers SelectionChanged -> draws the lap
            }
            else
            {
                MapInfoText.Text = "No laps with enough frames (>100) to show.";
            }

            StatusText.Text = "Loaded";
            StatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Load failed: {ex.Message}";
            StatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
        }
    }

    // --- 2. Show raw driven path for the selected lap ------------------------

    private void LapSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LapSelector.SelectedIndex < 0 || LapSelector.Tag is not List<List<TelemetryFrame>> lapFrameLists) return;
        if (LapSelector.SelectedIndex >= lapFrameLists.Count) return;

        _currentLapFrames = lapFrameLists[LapSelector.SelectedIndex];

        // Preserve corners across a lap switch by keeping the existing list if one
        // exists - corners are named track features, not tied to which lap you're
        // looking at right now.
        var existingCorners = _currentMap?.Corners ?? new List<Corner>();

        _currentMap = new GeneratedTrackMap
        {
            Points = _currentLapFrames.Select(f => new TrackPoint
            {
                Position = new Point(f.PosX, f.PosY),
                Heading = 0,
                Curvature = 0
            }).ToList(),
            Corners = existingCorners,
            GeneratedFromLapCount = 1,
            TotalLength = 0,
            GeneratedDateTime = DateTime.Now
        };
        _currentMap.TotalLength = CalculateRawLength(_currentMap.Points);

        RebuildCumulativeDistances();
        RenumberCorners();

        MapInfoText.Text = $"{_currentMap.Points.Count} raw points, {_currentMap.TotalLength:F0}m - unmodified, no smoothing/averaging";
        AddCornerToggle.IsEnabled = true;
        SaveButton.IsEnabled = true;
        ClearSelection();
        DrawTrackMap();
    }

    private static double CalculateRawLength(List<TrackPoint> points)
    {
        double total = 0;
        for (int i = 1; i < points.Count; i++)
        {
            var dx = points[i].Position.X - points[i - 1].Position.X;
            var dy = points[i].Position.Y - points[i - 1].Position.Y;
            total += Math.Sqrt(dx * dx + dy * dy);
        }
        return total;
    }

    // --- 3. Edit corners -----------------------------------------------------

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
            var idx = FindNearestCanvasPointIndex(clickPos);
            if (idx < 0) return;

            var newCorner = new Corner
            {
                Position = _currentMap.Points[idx].Position, // world-space, from the raw lap
                Curvature = 0,
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

        var idx = FindNearestCanvasPointIndex(e.GetPosition(MapCanvas));
        if (idx < 0) return;

        _selectedCorner.Position = _currentMap.Points[idx].Position;
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
        DrawTrackMap();
    }

    private void ClearSelection()
    {
        _selectedCorner = null;
        SelectedCornerPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateSelectedCornerDetail()
    {
        if (_selectedCorner == null) return;
        SelectedCornerDetail.Text = $"pos=({_selectedCorner.Position.X:F1}, {_selectedCorner.Position.Y:F1})";
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

    // --- 4. Save --------------------------------------------------------------

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
    /// LapDistance as the segment distance from the previous corner.
    /// </summary>
    private void RenumberCorners()
    {
        if (_currentMap == null || _currentMap.Points.Count == 0) return;

        var ordered = _currentMap.Corners
            .Select(c => (Corner: c, Index: FindNearestWorldPointIndex(c.Position)))
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

    private int FindNearestWorldPointIndex(Point worldPos)
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

    // Nearest point search directly in canvas/screen space, against the same
    // transformed frames that were actually drawn - avoids needing to invert
    // TrackRenderer.AutoFitTransform's rotate+scale+offset math.
    private int FindNearestCanvasPointIndex(Point canvasPos)
    {
        int best = -1;
        double bestDistSq = double.MaxValue;
        for (int i = 0; i < _canvasFrames.Count; i++)
        {
            var dx = _canvasFrames[i].PosX - canvasPos.X;
            var dy = _canvasFrames[i].PosY - canvasPos.Y;
            var d = dx * dx + dy * dy;
            if (d < bestDistSq) { bestDistSq = d; best = i; }
        }
        return best;
    }

    // --- Drawing ---------------------------------------------------------------

    private void DrawTrackMap()
    {
        MapCanvas.Children.Clear();
        _canvasFrames = new List<TelemetryFrame>();
        if (_currentMap == null || _currentLapFrames.Count < 2) return;

        // Force a layout pass so ActualWidth/Height are current - see the fix in an
        // earlier commit for why this matters (canvas size can read stale/0 otherwise).
        MapCanvas.UpdateLayout();
        var canvasWidth = MapCanvas.ActualWidth;
        var canvasHeight = MapCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            CornerCountText.Text = $"Corners: {_currentMap.Corners.Count} (canvas not ready - resize the window to redraw)";
            return;
        }

        // Same transform + same renderer MainWindow uses for the raw driven path.
        _canvasFrames = TrackRenderer.AutoFitTransform(_currentLapFrames, canvasWidth, canvasHeight);
        _trackRenderer.DrawTrack(MapCanvas, _canvasFrames);

        // Corner markers - _currentMap.Points and _canvasFrames share index
        // correspondence (both built from _currentLapFrames in the same order).
        foreach (var corner in _currentMap.Corners)
        {
            var idx = FindNearestWorldPointIndex(corner.Position);
            if (idx < 0 || idx >= _canvasFrames.Count) continue;
            var canvasPos = new Point(_canvasFrames[idx].PosX, _canvasFrames[idx].PosY);
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

    private readonly TrackRenderer _trackRenderer = new();
}
