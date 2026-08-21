using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LMU_Telemetry.Rendering;
using LMU_Telemetry.Models;
using LMU.Telemetry.Core.Models;
using LMU.Telemetry.Core.Services;
using LMU.Telemetry.Core.Telemetry;
using LMU.Analysis.Engine.Timing;
using LMU_Telemetry.ViewModels;
using LMU_Telemetry.Views;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;

namespace LMU_Telemetry;

public partial class MainWindow : Window
{
    private readonly TrackRenderer _trackRenderer;
    private readonly InputRenderer _inputRenderer;
    private readonly MainViewModel _viewModel;
    private readonly DuckDBTelemetryReader _duckDbReader;
    private readonly PlaybackController _playbackController;
    private bool _isDragging = false;
    private bool _isInputGraphDragging = false;
    private bool _isPaused = true;
    private List<TelemetryFrame>? _cachedTransformedFrames = null;
    private double _zoomFactor = 1.0;
    private const double ZoomMin = 0.5;
    private const double ZoomMax = 4.0;
    private const double PanSensitivity = 1.6;
    private double _zoomAnchorX = 0.5; // normalized anchor (0-1)
    private double _zoomAnchorY = 0.5;
    private bool _showTrackMap = true;
    private bool _isPanning = false;
    private Point _lastPanPoint = new Point(0, 0);
    private double _panOffsetX = 0;
    private double _panOffsetY = 0;
    private int _lastLapNumber = 0; // Track lap changes for resetting driven path
    private System.Windows.Shapes.Polygon? _carArrow = null; // Track current car arrow to prevent ghosting
    private List<LapTimingInfo>? _timingCache = null;
    private int _timingCacheFrameCount = -1;
    private readonly Dictionary<string, string> _embeddedTrackMaps = new();
    private TrackCenterline? _trackCenterline = null;
    private GeneratedTrackMap? _generatedTrackMap = null;
    private string? _currentTrackName = null;
    private string? _currentReplayFilePath = null; // Store the loaded replay file path
    private readonly string _logFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LMU_TrackMap_Debug.txt");
    private bool _extendedChannelsLogged = false;

    // Cached canvas-space bounds for TransformFrameToCanvas (invalidated when frame count changes)
    private int _boundsCacheFrameCount = -1;
    private float _boundsMinX, _boundsMaxX, _boundsMinY, _boundsMaxY;

    // Cached best/last lap times computed from buffer frames (DuckDB replay doesn't store them on frames)
    private int _lapTimeCacheFrameCount = -1;
    private float _cachedBestLapTime = 0;
    private float _cachedLastLapTime = 0;

    // Side panel cached WPF controls — built once, values updated each frame.
    // Eliminates ~35 object allocations per render frame.
    private bool _sidePanelBuilt = false;
    private TextBlock? _sideSessionText;
    private TextBlock? _sideGearText;
    private ColumnDefinition? _sideSpeedFilled;
    private ColumnDefinition? _sideSpeedEmpty;
    private TextBlock? _sideSpeedLabel;
    private ColumnDefinition? _sideThrottleFilled;
    private ColumnDefinition? _sideThrottleEmpty;
    private TextBlock? _sideThrottleLabel;
    private ColumnDefinition? _sideBrakeFilled;
    private ColumnDefinition? _sideBrakeEmpty;
    private TextBlock? _sideBrakeLabel;
    private TextBlock? _sideSteeringText;
    private TextBlock? _sidePosText;
    private ContentControl? _sideTimingHost;
    private TelemetryDetailsWindow? _detailWindow;

    public MainWindow()
    {
        _trackRenderer = new TrackRenderer();
        _inputRenderer = new InputRenderer();
        _viewModel = new MainViewModel();
        _duckDbReader = new DuckDBTelemetryReader();
        
        // Clear old log and initialize
        try
        {
            System.IO.File.WriteAllText(_logFilePath, $"=== TRACK MAP SYSTEM INITIALIZED {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        }
        catch { }
        
        Log($"Log file: {_logFilePath}");
        
        LoadEmbeddedTrackMaps();
        DataContext = _viewModel;

        InitializeComponent();

        // Replay playback (timer, speed multiplier, pause) - LMU.Telemetry.Core.Telemetry.PlaybackController
        _playbackController = new PlaybackController(_viewModel.Buffer);
        _playbackController.FrameAdvanceRequested += OnPlaybackFrameAdvanceRequested;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    // Ctrl+Shift+D opens Developer Mode (track map generation + corner curation).
    // Deliberately not a visible menu item - a dev tool, not a user-facing feature.
    private DeveloperWindow? _developerWindow;
    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (_developerWindow == null || !_developerWindow.IsLoaded)
            {
                _developerWindow = new DeveloperWindow { Owner = this };
            }
            _developerWindow.Show();
            _developerWindow.Activate();
            e.Handled = true;
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateControlsState();
        
        // Clear cache when canvas size changes
        if (TrackCanvas != null)
        {
            TrackCanvas.SizeChanged += (s, args) =>
            {
                _cachedTransformedFrames = null; _boundsCacheFrameCount = -1;
                UpdateCanvasZoomTransform();
                if (_viewModel.Buffer.HasData)
                {
                    UpdateDisplay();
                }
            };
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _detailWindow?.ForceClose();
        _playbackController.Dispose();
    }

    private void OpenDetailWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_detailWindow == null)
        {
            _detailWindow = new TelemetryDetailsWindow { Owner = this };
        }
        _detailWindow.Show();
        _detailWindow.Activate();
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Check if we have any data to play
            if (!_viewModel.Buffer.HasData)
            {
                MessageBox.Show("No telemetry data loaded.\n\nPlease load a recording first using the 'Load Recording' button.",
                               "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isPaused = !_isPaused;

            if (_isPaused)
            {
                _playbackController.Pause();
            }
            else
            {
                _playbackController.Play();
            }

            UpdatePlayPauseButton();
            UpdateControlsState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in PlayPauseButton_Click: {ex.Message}");
            Log($"ERROR in Play/Pause: {ex.Message}");
            MessageBox.Show($"An error occurred while trying to play/pause:\n\n{ex.Message}",
                           "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _isPaused = true;
            UpdatePlayPauseButton();
        }
    }

    private void Speed2xButton_Click(object sender, RoutedEventArgs e)
    {
        if (_playbackController.SpeedMultiplier == 2)
        {
            _playbackController.SetSpeedMultiplier(1); // Toggle back to 1x
            Speed2xButton.Background = new SolidColorBrush(Color.FromRgb(85, 85, 85));
        }
        else
        {
            _playbackController.SetSpeedMultiplier(2);
            Speed2xButton.Background = new SolidColorBrush(Color.FromRgb(100, 200, 100));
            Speed4xButton.Background = new SolidColorBrush(Color.FromRgb(85, 85, 85));
        }
    }

    private void Speed4xButton_Click(object sender, RoutedEventArgs e)
    {
        if (_playbackController.SpeedMultiplier == 4)
        {
            _playbackController.SetSpeedMultiplier(1); // Toggle back to 1x
            Speed4xButton.Background = new SolidColorBrush(Color.FromRgb(85, 85, 85));
        }
        else
        {
            _playbackController.SetSpeedMultiplier(4);
            Speed4xButton.Background = new SolidColorBrush(Color.FromRgb(100, 200, 100));
            Speed2xButton.Background = new SolidColorBrush(Color.FromRgb(85, 85, 85));
        }
    }

    private void PrevLapButton_Click(object sender, RoutedEventArgs e)
    {
        JumpToLapOffset(-1);
    }

    private void NextLapButton_Click(object sender, RoutedEventArgs e)
    {
        JumpToLapOffset(1);
    }

    private void JumpToLapOffset(int lapOffset)
    {
        if (!_viewModel.Buffer.HasData || _viewModel.CurrentFrame == null) return;

        var frames = _viewModel.Buffer.Frames;
        var currentLap = _viewModel.CurrentFrame.CurrentLap;
        var targetLap = currentLap + lapOffset;

        if (!frames.Any(f => f.CurrentLap == targetLap)) return;

        var targetDistance = _viewModel.CurrentFrame.LapDistance;
        var targetIndex = FindClosestLapDistanceIndex(frames, targetLap, targetDistance);

        if (targetIndex >= 0)
        {
            _viewModel.ScrubToIndex(targetIndex);
            UpdateDisplay();
        }
    }

    private int FindClosestLapDistanceIndex(IReadOnlyList<TelemetryFrame> frames, int lapNumber, float targetDistance)
    {
        bool useDistance = targetDistance > 0 && frames.Any(f => f.CurrentLap == lapNumber && f.LapDistance > 0);

        int bestIndex = -1;
        double bestDiff = double.MaxValue;

        for (int i = 0; i < frames.Count; i++)
        {
            if (frames[i].CurrentLap != lapNumber) continue;

            if (!useDistance)
            {
                return i; // First frame of lap
            }

            var diff = Math.Abs(frames[i].LapDistance - targetDistance);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    // PlaybackController ticks on a background timer (see LMU.Telemetry.Core.Telemetry.PlaybackController)
    // and asks us to scrub to nextIndex - marshal to the UI thread.
    private void OnPlaybackFrameAdvanceRequested(object? sender, int nextIndex)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_isPaused || !_viewModel.Buffer.HasData) return;

            _viewModel.ScrubToIndex(nextIndex);
            UpdateDisplay();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void UpdatePlayPauseButton()
    {
        if (_isPaused)
        {
            PlayPauseIcon.Data = Geometry.Parse("M1,0 L1,10 L9,5 Z");
            PlayPauseIcon.Fill = new SolidColorBrush(Color.FromRgb(0x4A, 0xC2, 0x6E)); // green play
            StatusText.Text = "PAUSED";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0));
        }
        else
        {
            PlayPauseIcon.Data = Geometry.Parse("M1,0 L3.2,0 L3.2,10 L1,10 Z M5.8,0 L8,0 L8,10 L5.8,10 Z");
            PlayPauseIcon.Fill = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)); // grey pause
            StatusText.Text = "PLAYING";
            StatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
        }
    }

    private void UpdateControlsState()
    {
        FrameCountText.Text = $"{_viewModel.Buffer.Frames.Count:N0} frames";
    }

    private void TrackCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (TrackCanvas != null && _viewModel.Buffer.HasData)
        {
            if (e.ChangedButton == MouseButton.Right && _zoomFactor > 1.0)
            {
                _isPanning = true;
                _lastPanPoint = e.GetPosition(TrackCanvas);
                TrackCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            _isDragging = true;
            _isPaused = true;
            _viewModel.IsLiveMode = false;
            TrackCanvas.CaptureMouse();
            UpdatePlayPauseButton();

            DragPlayerToPosition(GetLogicalCanvasPosition(e));
        }
    }

    private void TrackCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && TrackCanvas != null && _zoomFactor > 1.0)
        {
            var current = e.GetPosition(TrackCanvas);
            var dx = current.X - _lastPanPoint.X;
            var dy = current.Y - _lastPanPoint.Y;
            _lastPanPoint = current;

            _panOffsetX += dx * PanSensitivity;
            _panOffsetY += dy * PanSensitivity;
            ClampPanOffsets();
            UpdateCanvasZoomTransform();
            e.Handled = true;
            return;
        }

        if (_isDragging && TrackCanvas != null && _viewModel.Buffer.HasData)
        {
            DragPlayerToPosition(GetLogicalCanvasPosition(e));
        }
    }

    private void TrackCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            TrackCanvas?.ReleaseMouseCapture();
            e.Handled = true;
        }

        if (_isDragging)
        {
            _isDragging = false;
            TrackCanvas?.ReleaseMouseCapture();
        }
    }

    private void TrackCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_viewModel.Buffer.HasData) return;

        if (TrackCanvas != null && TrackCanvas.ActualWidth > 0 && TrackCanvas.ActualHeight > 0)
        {
            var pos = e.GetPosition(TrackCanvas);
            _zoomAnchorX = Math.Clamp(pos.X / TrackCanvas.ActualWidth, 0.0, 1.0);
            _zoomAnchorY = Math.Clamp(pos.Y / TrackCanvas.ActualHeight, 0.0, 1.0);
        }

        var delta = e.Delta > 0 ? 0.1 : -0.1;
        var newZoom = Math.Clamp(_zoomFactor + delta, ZoomMin, ZoomMax);

        if (Math.Abs(newZoom - _zoomFactor) < 0.0001) return;

        _zoomFactor = newZoom;
        ClampPanOffsets();
        UpdateCanvasZoomTransform();
        _cachedTransformedFrames = null; _boundsCacheFrameCount = -1; _lapTimeCacheFrameCount = -1; // Recalculate positions (but render transform handles zoom)
        UpdateDisplay();
    }

    private void TrackMapToggle_Checked(object sender, RoutedEventArgs e)
    {
        _showTrackMap = TrackMapToggle?.IsChecked == true;
        ClearCanvas();
        _cachedTransformedFrames = null; _boundsCacheFrameCount = -1;
        UpdateDisplay();
    }

    private void DragPlayerToPosition(Point canvasPosition)
    {
        if (!_viewModel.Buffer.HasData || _viewModel.CurrentFrame == null) return;

        var frames = _viewModel.Buffer.Frames;
        if (frames.Count == 0) return;

        // Rebuild cache if missing or stale
        if (_cachedTransformedFrames == null || _cachedTransformedFrames.Count != frames.Count)
            _cachedTransformedFrames = TransformFramesToCanvas(frames);

        var cached = _cachedTransformedFrames;
        int safeCount = Math.Min(frames.Count, cached.Count);
        int currentLap = _viewModel.CurrentFrame.CurrentLap;

        // Collect indices that belong to the current lap only
        var lapIndices = new List<int>(safeCount / 4);
        for (int i = 0; i < safeCount; i++)
        {
            if (frames[i].CurrentLap == currentLap)
                lapIndices.Add(i);
        }
        if (lapIndices.Count == 0) return;

        // Subsample for drag performance
        int step = Math.Max(1, lapIndices.Count / 2000);

        double minDistSq = double.MaxValue;
        int closestIndex = lapIndices[0];

        for (int k = 0; k < lapIndices.Count; k += step)
        {
            int i = lapIndices[k];
            var tf = cached[i];
            double dx = tf.PosX - canvasPosition.X;
            double dy = tf.PosY - canvasPosition.Y;
            double distSq = dx * dx + dy * dy;
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                closestIndex = i;
            }
        }

        if (Math.Sqrt(minDistSq) < 200)
        {
            _viewModel.ScrubToIndex(closestIndex);
            UpdateDisplay();
        }
    }

    private Point GetLogicalCanvasPosition(MouseEventArgs e)
    {
        if (TrackCanvas == null) return new Point(0, 0);

        var pos = e.GetPosition(TrackCanvas);
        try
        {
            var rt = TrackCanvas.RenderTransform;
            if (rt != null && rt != Transform.Identity)
            {
                var inverse = rt.Inverse;
                if (inverse != null)
                    return inverse.Transform(pos);
            }
        }
        catch
        {
            // Fall through — return raw position
        }
        return pos;
    }

    private void UpdateDisplay()
    {
        try
        {            
            if (TrackCanvas != null && _viewModel.Buffer.HasData)
            {
                // Cache transformed frames for performance (post-session analysis only now - no live mode)
                if (_cachedTransformedFrames == null)
                {
                    _cachedTransformedFrames = TransformFramesToCanvas(_viewModel.Buffer.Frames);

                    // Initialize lap number to force first draw
                    if (_viewModel.CurrentFrame != null)
                    {
                        _lastLapNumber = _viewModel.CurrentFrame.CurrentLap - 1; // Make it different so first draw triggers
                    }
                }
                if (_viewModel.CurrentFrame != null && _cachedTransformedFrames != null)
                {
                    var currentLap = _viewModel.CurrentFrame.CurrentLap;
                    var currentIndex = _viewModel.Buffer.CurrentIndex;
                    var currentTime = _viewModel.CurrentFrame.Time;

                    // Check if we need to redraw the lap path
                    if (currentLap != _lastLapNumber)
                    {
                        System.Diagnostics.Debug.WriteLine($"LAP CHANGE: {_lastLapNumber} → {currentLap} (frame {currentIndex}, t={currentTime:F2}s)");

                        _lastLapNumber = currentLap;

                        // ALWAYS clear canvas when lap changes
                        ClearCanvas();
                        DrawCenterline();
                        _carArrow = null; // Reset car arrow reference

                        // Draw ONLY the current lap path
                        _trackRenderer.DrawCompleteLap(TrackCanvas, _cachedTransformedFrames, currentLap);

                        // Draw sector and start/finish markers
                        _trackRenderer.DrawSectorMarkers(TrackCanvas, _cachedTransformedFrames, currentLap);
                    }

                    // Draw the car with heading (remove old arrow first to prevent ghosting)
                    if (_carArrow != null)
                    {
                        TrackCanvas.Children.Remove(_carArrow);
                        _carArrow = null;
                    }

                    if (_cachedTransformedFrames != null && currentIndex < _cachedTransformedFrames.Count)
                    {
                        var transformedCurrent = _cachedTransformedFrames[currentIndex];
                        TelemetryFrame? transformedPrevious = currentIndex > 0 ? _cachedTransformedFrames[currentIndex - 1] : null;
                        _carArrow = _trackRenderer.DrawCar(TrackCanvas, transformedCurrent, transformedPrevious);
                    }

                    // Update time slider
                    UpdateTimeSlider();
                }
            }

            UpdateTelemetryDisplay();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update error: {ex.Message}");
        }
    }

    private List<TelemetryFrame> TransformFramesToCanvas(IReadOnlyList<TelemetryFrame> frames)
    {
        if (!frames.Any()) return new List<TelemetryFrame>();

        var canvasWidth = TrackCanvas?.ActualWidth ?? 0;
        var canvasHeight = TrackCanvas?.ActualHeight ?? 0;

        if (canvasWidth == 0 || canvasHeight == 0) return new List<TelemetryFrame>();

        // If we have old track centerline, use track coordinates (legacy)
        if (_trackCenterline != null && _trackCenterline.PointCount > 0)
        {
            // Convert each frame: world coords → (s, d) → world meters → canvas
            var worldPoints = frames.Select(f => {
                // World to track coordinates
                var (s, d) = _trackCenterline.WorldToTrackCoordinates(f.PosX, f.PosY);
                
                // Track coordinates back to world meters
                var worldPos = _trackCenterline.TrackToWorldMeters(s, d);
                
                return (WorldX: worldPos.X, WorldY: worldPos.Y, Frame: f);
            }).ToList();
            
            // Find bounds of world coordinates
            double minX = worldPoints.Min(p => p.WorldX);
            double maxX = worldPoints.Max(p => p.WorldX);
            double minY = worldPoints.Min(p => p.WorldY);
            double maxY = worldPoints.Max(p => p.WorldY);
            
            // Compute scale to fit canvas
            double rangeX = maxX - minX;
            double rangeY = maxY - minY;
            
            if (rangeX < 1) rangeX = 1;
            if (rangeY < 1) rangeY = 1;
            
            double scaleX = canvasWidth / rangeX;
            double scaleY = canvasHeight / rangeY;
            double scale = Math.Min(scaleX, scaleY) * 0.9; // 90% to leave margin
            
            // Center on canvas
            double offsetX = (canvasWidth - rangeX * scale) / 2;
            double offsetY = (canvasHeight - rangeY * scale) / 2;
            
            
            return worldPoints.Select(p => {
                var rawX = offsetX + (p.WorldX - minX) * scale;
                var rawY = offsetY + (maxY - p.WorldY) * scale; // Flip Y for screen coordinates
                var zoomed = ApplyZoom(rawX, rawY, canvasWidth, canvasHeight);

                return new TelemetryFrame
                {
                    Time = p.Frame.Time,
                    PosX = (float)zoomed.X,
                    PosY = (float)zoomed.Y,
                    Speed = p.Frame.Speed,
                    Throttle = p.Frame.Throttle,
                    Brake = p.Frame.Brake,
                    Steering = p.Frame.Steering,
                    Gear = p.Frame.Gear,
                    Rpm = p.Frame.Rpm,
                    CurrentLap = p.Frame.CurrentLap,
                    LapDistance = p.Frame.LapDistance,
                    LapTime = p.Frame.LapTime,
                    Sector = p.Frame.Sector
                };
            }).ToList();
        }
        // Fallback: Auto-fit (no track map or centerline built yet) - shared with
        // Developer Mode via TrackRenderer.AutoFitTransform so both render identically.
        var autoFitted = TrackRenderer.AutoFitTransform(frames, canvasWidth, canvasHeight);

        return autoFitted.Select(f =>
        {
            var zoomed = ApplyZoom(f.PosX, f.PosY, canvasWidth, canvasHeight);
            return new TelemetryFrame
            {
                Time = f.Time,
                PosX = (float)zoomed.X,
                PosY = (float)zoomed.Y,
                Speed = f.Speed,
                Throttle = f.Throttle,
                Brake = f.Brake,
                Steering = f.Steering,
                Gear = f.Gear,
                Rpm = f.Rpm,
                CurrentLap = f.CurrentLap,
                LapDistance = f.LapDistance,
                LapTime = f.LapTime,
                Sector = f.Sector
            };
        }).ToList();
    }

    private Point ApplyZoom(double x, double y, double canvasWidth, double canvasHeight)
    {
        // Zoom now handled by TrackCanvas.RenderTransform so coordinates stay in data space here
        return new Point(x, y);
    }

    private void ClampPanOffsets()
    {
        if (TrackCanvas == null) return;
        if (_zoomFactor <= 1.0)
        {
            _panOffsetX = 0;
            _panOffsetY = 0;
            return;
        }

        // Limit panning so content cannot move fully off-screen
        double maxOffsetX = (TrackCanvas.ActualWidth * (_zoomFactor - 1.0)) / 2.0;
        double maxOffsetY = (TrackCanvas.ActualHeight * (_zoomFactor - 1.0)) / 2.0;

        _panOffsetX = Math.Clamp(_panOffsetX, -maxOffsetX, maxOffsetX);
        _panOffsetY = Math.Clamp(_panOffsetY, -maxOffsetY, maxOffsetY);
    }

    private void UpdateCanvasZoomTransform()
    {
        if (TrackCanvas == null || TrackCanvas.ActualWidth <= 0 || TrackCanvas.ActualHeight <= 0)
        {
            return;
        }

        var anchorX = TrackCanvas.ActualWidth * _zoomAnchorX;
        var anchorY = TrackCanvas.ActualHeight * _zoomAnchorY;

        var matrix = Matrix.Identity;
        matrix.Translate(-anchorX, -anchorY);
        matrix.Scale(_zoomFactor, _zoomFactor);
        matrix.Translate(anchorX + _panOffsetX, anchorY + _panOffsetY);

        TrackCanvas.RenderTransform = new MatrixTransform(matrix);
    }

    private TelemetryFrame TransformFrameToCanvas(TelemetryFrame frame)
    {
        var buffer = _viewModel.Buffer;
        if (!buffer.HasData) return frame;

        var canvasWidth = TrackCanvas?.ActualWidth ?? 0;
        var canvasHeight = TrackCanvas?.ActualHeight ?? 0;

        if (canvasWidth == 0 || canvasHeight == 0) return frame;

        // Recompute bounds only when frame count changes (avoids 4 LINQ passes per render).
        if (buffer.Frames.Count != _boundsCacheFrameCount)
        {
            _boundsMinX = buffer.Frames.Min(f => f.PosX);
            _boundsMaxX = buffer.Frames.Max(f => f.PosX);
            _boundsMinY = buffer.Frames.Min(f => f.PosY);
            _boundsMaxY = buffer.Frames.Max(f => f.PosY);
            _boundsCacheFrameCount = buffer.Frames.Count;
        }
        var minX = _boundsMinX;
        var maxX = _boundsMaxX;
        var minY = _boundsMinY;
        var maxY = _boundsMaxY;

        if (maxX == minX) maxX = minX + 1;
        if (maxY == minY) maxY = minY + 1;

        var scaleX = canvasWidth / (maxX - minX);
        var scaleY = canvasHeight / (maxY - minY);
        var scale = Math.Min(scaleX, scaleY) * 0.9;

        var offsetX = (canvasWidth - (maxX - minX) * scale) / 2;
        var offsetY = (canvasHeight - (maxY - minY) * scale) / 2;

        var canvasX = offsetX + (frame.PosX - minX) * scale;
        var canvasY = offsetY + (maxY - frame.PosY) * scale;
        var zoomed = ApplyZoom(canvasX, canvasY, canvasWidth, canvasHeight);

        return new TelemetryFrame
        {
            Time = frame.Time,
            PosX = (float)zoomed.X,
            PosY = (float)zoomed.Y,
            Speed = frame.Speed,
            Throttle = frame.Throttle,
            Brake = frame.Brake,
            Steering = frame.Steering,
            Gear = frame.Gear,
            Rpm = frame.Rpm,
            CurrentLap = frame.CurrentLap,
            LapDistance = frame.LapDistance,
            LapTime = frame.LapTime,
            Sector = frame.Sector
        };
    }

    private void UpdateTelemetryDisplay()
    {
        try
        {
            if (_viewModel.CurrentFrame != null)
            {
                var frame = _viewModel.CurrentFrame;

                UpdateDataStrip(frame);
                UpdateSidePanel(frame);

                if (PedalCanvas != null && PedalCanvas.ActualWidth > 0)
                    _inputRenderer.DrawPedals(PedalCanvas, frame);

                if (InputGraphCanvas != null && InputGraphCanvas.ActualWidth > 0)
                    _inputRenderer.DrawInputGraphs(InputGraphCanvas, ChannelLabelsPanel,
                                                   _viewModel.Buffer.Frames, _viewModel.Buffer.CurrentIndex);

                if (_detailWindow?.IsVisible == true)
                    _detailWindow.PushFrame(frame, _viewModel.Buffer.Frames);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateTelemetryDisplay error: {ex.Message}");
        }
    }

    // Updates the data strip (row 1) with zero allocation — modifies existing XAML elements in-place.
    private void UpdateDataStrip(TelemetryFrame frame)
    {
        // Gear
        string gearStr = frame.Gear switch { -1 => "R", 0 => "N", _ => frame.Gear.ToString() };
        DsGear.Text = gearStr;

        // RPM bar
        double rpmMax = frame.RpmMax > 100 ? frame.RpmMax : 11000;
        double rpmRatio = Math.Clamp(frame.Rpm / rpmMax, 0, 1);
        DsRpmFilled.Width = new GridLength(rpmRatio, GridUnitType.Star);
        DsRpmEmpty.Width  = new GridLength(Math.Max(0.001, 1 - rpmRatio), GridUnitType.Star);
        DsRpmText.Text    = $"{frame.Rpm:F0}";
        DsRpmMax.Text     = $"/ {rpmMax:F0}";

        // Speed
        DsSpeed.Text = $"{frame.Speed:F0}";

        // Throttle mini bar
        double tRatio = Math.Clamp(frame.Throttle, 0, 1);
        DsThrottleFilled.Width = new GridLength(tRatio, GridUnitType.Star);
        DsThrottleEmpty.Width  = new GridLength(Math.Max(0.001, 1 - tRatio), GridUnitType.Star);
        DsThrottlePct.Text     = $"{tRatio * 100:F0}%";

        // Brake mini bar
        double bRatio = Math.Clamp(frame.Brake, 0, 1);
        DsBrakeFilled.Width = new GridLength(bRatio, GridUnitType.Star);
        DsBrakeEmpty.Width  = new GridLength(Math.Max(0.001, 1 - bRatio), GridUnitType.Star);
        DsBrakePct.Text     = $"{bRatio * 100:F0}%";

        // Lap / sector — rF2 lap numbers start at 0 (outlap), display 1-indexed
        int displayLap = frame.CurrentLap + 1;
        string lapStr = $"LAP {displayLap}";
        if (frame.Sector >= 0 && frame.Sector <= 2) lapStr += $"  ·  S{frame.Sector + 1}";
        DsLapSector.Text = lapStr;

        // Compute best/last from the buffer when frame count changes.
        // DuckDB replay frames don't carry BestLapTime/LastLapTime; we derive from timestamps.
        ComputeLapTimesIfNeeded(_viewModel.Buffer.Frames);

        float bestTime = _cachedBestLapTime > 0 ? _cachedBestLapTime
                       : frame.BestLapTime  > 0 ? frame.BestLapTime : 0;
        float lastTime = _cachedLastLapTime > 0 ? _cachedLastLapTime
                       : frame.LastLapTime  > 0 ? frame.LastLapTime : 0;

        DsBestLap.Text = bestTime > 0
            ? $"BEST  {TimeSpan.FromSeconds(bestTime):m\\:ss\\.fff}"
            : "BEST  —";
        DsLastLap.Text = lastTime > 0
            ? $"LAST  {TimeSpan.FromSeconds(lastTime):m\\:ss\\.fff}"
            : "LAST  —";
    }

    // Computes best and last completed lap times from the frame buffer.
    // Only re-runs when the frame count changes (cheap guard).
    private void ComputeLapTimesIfNeeded(IReadOnlyList<TelemetryFrame> frames)
    {
        if (frames.Count == _lapTimeCacheFrameCount) return;
        _lapTimeCacheFrameCount = frames.Count;

        // Gather the first and last timestamp seen per lap number
        var lapBounds = new Dictionary<int, (double first, double last)>();
        foreach (var f in frames)
        {
            int lap = f.CurrentLap;
            if (!lapBounds.TryGetValue(lap, out var b))
                lapBounds[lap] = (f.Time, f.Time);
            else if (f.Time > b.last)
                lapBounds[lap] = (b.first, f.Time);
        }

        // A lap is "complete" only if the next lap number also exists in the data.
        var sortedLaps = lapBounds.OrderBy(kv => kv.Key).ToList();
        var completedTimes = new List<float>();
        for (int i = 0; i < sortedLaps.Count - 1; i++)
        {
            var (first, last) = sortedLaps[i].Value;
            float dur = (float)(last - first);
            if (dur > 10f && dur < 600f) // sanity: 10 s — 10 min
                completedTimes.Add(dur);
        }

        _cachedBestLapTime = completedTimes.Count > 0 ? completedTimes.Min() : 0;
        _cachedLastLapTime = completedTimes.Count > 0 ? completedTimes[^1]  : 0;
    }

    // Build the side-panel controls exactly once; thereafter only update values.
    private void EnsureSidePanelBuilt()
    {
        if (_sidePanelBuilt || TelemetryDataPanel == null) return;
        _sidePanelBuilt = true;
        TelemetryDataPanel.Children.Clear();

        // SESSION
        TelemetryDataPanel.Children.Add(SidePanelHeader("SESSION", "#569CD6"));
        _sideSessionText = SidePanelValue("");
        TelemetryDataPanel.Children.Add(_sideSessionText);
        TelemetryDataPanel.Children.Add(SidePanelSeparator());

        // TIMING
        TelemetryDataPanel.Children.Add(SidePanelHeader("TIMING", "#C586C0"));
        _sideTimingHost = new ContentControl();
        TelemetryDataPanel.Children.Add(_sideTimingHost);
        TelemetryDataPanel.Children.Add(SidePanelSeparator());

        // ENGINE
        TelemetryDataPanel.Children.Add(SidePanelHeader("ENGINE", "#FFD700"));
        _sideGearText = SidePanelValue("");
        TelemetryDataPanel.Children.Add(_sideGearText);
        (_sideSpeedFilled, _sideSpeedEmpty, _sideSpeedLabel) = BuildBarStrip(Color.FromRgb(0, 150, 255));
        TelemetryDataPanel.Children.Add(WrapBarStrip(_sideSpeedFilled, _sideSpeedEmpty, _sideSpeedLabel));
        TelemetryDataPanel.Children.Add(SidePanelSeparator());

        // CONTROLS
        TelemetryDataPanel.Children.Add(SidePanelHeader("CONTROLS", "#FF6B9D"));
        (_sideThrottleFilled, _sideThrottleEmpty, _sideThrottleLabel) = BuildBarStrip(Color.FromRgb(0, 200, 0));
        TelemetryDataPanel.Children.Add(WrapBarStrip(_sideThrottleFilled, _sideThrottleEmpty, _sideThrottleLabel));
        (_sideBrakeFilled, _sideBrakeEmpty, _sideBrakeLabel) = BuildBarStrip(Color.FromRgb(220, 50, 50));
        TelemetryDataPanel.Children.Add(WrapBarStrip(_sideBrakeFilled, _sideBrakeEmpty, _sideBrakeLabel));
        _sideSteeringText = SidePanelValue("");
        TelemetryDataPanel.Children.Add(_sideSteeringText);
        TelemetryDataPanel.Children.Add(SidePanelSeparator());

        // POSITION
        TelemetryDataPanel.Children.Add(SidePanelHeader("POSITION", "#569CD6"));
        _sidePosText = SidePanelValue("");
        TelemetryDataPanel.Children.Add(_sidePosText);
        TelemetryDataPanel.Children.Add(SidePanelSeparator());
    }

    // Update only the values inside the already-built side panel — zero allocations in steady state.
    private void UpdateSidePanel(TelemetryFrame frame)
    {
        EnsureSidePanelBuilt();

        // SESSION
        var lap = $"Lap {frame.CurrentLap}";
        if (frame.Sector > 0 && frame.Sector <= 3) lap += $"  S{frame.Sector}";
        if (frame.LapDistance > 0) lap += $"  {frame.LapDistance:F0}m";
        _sideSessionText!.Text = $"{frame.Time:F2}s  |  {lap}";

        // TIMING (rebuild only when lap count changes)
        var timing = CreateTimingPanel();
        _sideTimingHost!.Content = timing;

        // ENGINE
        string gearLabel = frame.Gear switch { -1 => "R", 0 => "N", _ => frame.Gear.ToString() };
        _sideGearText!.Text = $"Gear: {gearLabel}  |  RPM: {frame.Rpm:F0}";
        UpdateBarStrip(_sideSpeedFilled!, _sideSpeedEmpty!, _sideSpeedLabel!, frame.Speed / 300.0, $"Speed: {frame.Speed:F1} km/h");

        // CONTROLS
        UpdateBarStrip(_sideThrottleFilled!, _sideThrottleEmpty!, _sideThrottleLabel!, frame.Throttle, $"Throttle: {frame.Throttle * 100:F0}%");
        UpdateBarStrip(_sideBrakeFilled!, _sideBrakeEmpty!, _sideBrakeLabel!, frame.Brake, $"Brake: {frame.Brake * 100:F0}%");
        _sideSteeringText!.Text = $"Steer: {frame.Steering:+0.00;-0.00;0.00}";

        // POSITION
        _sidePosText!.Text = $"X: {frame.PosX:F1}  Y: {frame.PosY:F1}";
    }

    // --- Side panel builder helpers (called once) ---------------------------

    private static TextBlock SidePanelHeader(string title, string colorHex)
        => new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(ColorFromHexStatic(colorHex)),
            FontSize = 11, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 5, 0, 3)
        };

    private static TextBlock SidePanelValue(string text)
        => new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            FontSize = 11, Margin = new Thickness(5, 0, 0, 2)
        };

    private static Border SidePanelSeparator()
        => new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            Margin = new Thickness(0, 5, 0, 0)
        };

    private static (ColumnDefinition filled, ColumnDefinition empty, TextBlock label) BuildBarStrip(Color barColor)
    {
        var filled = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        var empty  = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        var label  = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        return (filled, empty, label);
    }

    private static Border WrapBarStrip(ColumnDefinition filled, ColumnDefinition empty, TextBlock label)
    {
        // Pre-build the Grid with the colored fill border.
        var fillColor = new SolidColorBrush(Color.FromRgb(0, 150, 255)); // overridden per call site
        var grid = new Grid { Margin = new Thickness(0) };
        grid.ColumnDefinitions.Add(filled);
        grid.ColumnDefinitions.Add(empty);

        var fillBorder = new Border { Background = new SolidColorBrush(Color.FromRgb(0, 150, 255)), CornerRadius = new CornerRadius(2) };
        Grid.SetColumn(fillBorder, 0);
        grid.Children.Add(fillBorder);
        Grid.SetColumn(label, 0);
        Grid.SetColumnSpan(label, 2);
        grid.Children.Add(label);

        return new Border
        {
            Margin = new Thickness(5, 3, 0, 3),
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4),
            Child = grid
        };
    }

    private static void UpdateBarStrip(ColumnDefinition filled, ColumnDefinition empty, TextBlock label,
                                       double value, string text)
    {
        value = Math.Clamp(value, 0, 1);
        filled.Width = new GridLength(value, GridUnitType.Star);
        empty.Width  = new GridLength(1 - value, GridUnitType.Star);
        label.Text   = text;
    }

    private static Color ColorFromHexStatic(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            return Color.FromRgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        return Colors.White;
    }

    private UIElement CreateTimingPanel()
    {
        var frames = _viewModel.Buffer.Frames;
        if (frames.Count == 0)
        {
            return new TextBlock
            {
                Text = "No timing data",
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                FontSize = 11,
                Margin = new Thickness(5, 0, 0, 3)
            };
        }

        if (_timingCache == null || _timingCacheFrameCount != frames.Count)
        {
            _timingCache = LapTimingCalculator.BuildTimingCache(frames);
            _timingCacheFrameCount = frames.Count;
        }

        var table = new StackPanel { Margin = new Thickness(5, 0, 0, 3) };

        // Header row
        table.Children.Add(CreateTimingRow("Lap", "S1", "S2", "S3", "Lap", isHeader: true));

        foreach (var lap in _timingCache)
        {
            table.Children.Add(CreateTimingRow(
                $"{lap.LapNumber}",
                FormatTime(lap.S1),
                FormatTime(lap.S2),
                FormatTime(lap.S3),
                FormatTime(lap.LapTime),
                isHeader: false));
        }

        return new ScrollViewer
        {
            Content = table,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Height = 140
        };
    }

    private UIElement CreateTimingRow(string lap, string s1, string s2, string s3, string lapTime, bool isHeader)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fg = isHeader ? new SolidColorBrush(Color.FromRgb(180, 180, 180))
                          : new SolidColorBrush(Color.FromRgb(200, 200, 200));
        var weight = isHeader ? FontWeights.Bold : FontWeights.Normal;

        grid.Children.Add(CreateTimingCell(lap, 0, fg, weight));
        grid.Children.Add(CreateTimingCell(s1, 1, fg, weight));
        grid.Children.Add(CreateTimingCell(s2, 2, fg, weight));
        grid.Children.Add(CreateTimingCell(s3, 3, fg, weight));
        grid.Children.Add(CreateTimingCell(lapTime, 4, fg, weight, TextAlignment.Right));

        return grid;
    }

    private UIElement CreateTimingCell(string text, int column, System.Windows.Media.Brush foreground, FontWeight weight, TextAlignment alignment = TextAlignment.Left)
    {
        var cell = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = 11,
            FontWeight = weight,
            TextAlignment = alignment,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(cell, column);
        return cell;
    }

    private static string FormatTime(TimeSpan? time)
    {
        if (!time.HasValue) return "--:--,---";
        var ts = time.Value;
        var minutes = (int)ts.TotalMinutes;
        return $"{minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";
    }

    private void SaveRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        var frames = _viewModel.Buffer.Frames;
        if (frames.Count == 0)
        {
            MessageBox.Show("Buffer is empty. Drive in LMU to record telemetry first.",
                            "Nothing to Save", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Telemetry Recording",
            Filter = "DuckDB telemetry (*.duckdb)|*.duckdb",
            FileName = $"LMU_{DateTime.Now:yyyyMMdd_HHmmss}.duckdb"
        };
        if (dlg.ShowDialog(this) != true) return;

        string trackName = frames[0].ExtendedData.TryGetValue("TrackName", out var tn) ? tn?.ToString() ?? "" : "";
        string carName   = frames[0].ExtendedData.TryGetValue("VehicleName", out var cn) ? cn?.ToString() ?? "" : "";

        // Optional: attach the car setup (.svm) used for this session - for the
        // future coaching agent to correlate setup choices with driving performance.
        // Same file-browse pattern as loading a telemetry recording.
        CarSetup? setup = null;
        var attachSetup = MessageBox.Show(
            "Attach the car setup (.svm) file used for this session?\n\nOptional - you can save without one.",
            "Attach Setup File", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (attachSetup == MessageBoxResult.Yes)
        {
            var setupDlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select the car setup file used for this session",
                Filter = "LMU setup files (*.svm)|*.svm"
            };
            if (setupDlg.ShowDialog(this) == true)
            {
                try
                {
                    setup = SvmSetupReader.Parse(setupDlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not parse setup file:\n{ex.Message}\n\nSaving without it.",
                                   "Setup Parse Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        try
        {
            StatusText.Text = "Saving…";
            StatusText.Foreground = new SolidColorBrush(Colors.Yellow);

            DuckDBTelemetryWriter.Write(dlg.FileName, frames, trackName, carName, setup);

            StatusText.Text = $"Saved: {System.IO.Path.GetFileName(dlg.FileName)}";
            StatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Save failed";
            StatusText.Foreground = new SolidColorBrush(Colors.Red);
            MessageBox.Show($"Save failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        var selectorWindow = new Window
        {
            Title = "Load Telemetry Recording",
            Width = 600,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(37, 37, 38))
        };

        var selector = new TelemetryFileSelector();
        selector.FileSelected += (s, fileInfo) =>
        {
            selectorWindow.Close();
            LoadRecordingFromFile(fileInfo);
        };

        selectorWindow.Content = selector;
        selectorWindow.ShowDialog();
    }

    private void LoadRecordingFromFile(TelemetryFileInfo fileInfo)
    {
        try
        {
            StatusText.Text = "Loading...";
            StatusText.Foreground = new SolidColorBrush(Colors.Yellow);

            List<TelemetryFrame> frames;
            try
            {
                System.Diagnostics.Debug.WriteLine($"Starting to load telemetry from: {fileInfo.FilePath}");
                
                // Load telemetry data from DuckDB file
                frames = _duckDbReader.LoadTelemetryData(fileInfo.FilePath);
                
                System.Diagnostics.Debug.WriteLine($"Loaded {frames.Count} frames");
            }
            catch (Exception loadEx)
            {
                var errorDetails = $"Error reading DuckDB file:\n{loadEx.Message}";
                if (loadEx.InnerException != null)
                {
                    errorDetails += $"\n\nInner Exception:\n{loadEx.InnerException.Message}";
                }
                errorDetails += $"\n\nStack Trace:\n{loadEx.StackTrace}";
                
                System.Diagnostics.Debug.WriteLine(errorDetails);
                
                MessageBox.Show(errorDetails, 
                               "Database Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Load failed";
                StatusText.Foreground = new SolidColorBrush(Colors.Red);
                return;
            }

            if (frames.Count == 0)
            {
                MessageBox.Show("No telemetry data found in the file.\n\nThe database might be empty or use an unsupported schema.", 
                               "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText.Text = "No data";
                StatusText.Foreground = new SolidColorBrush(Colors.Orange);
                return;
            }

            // Log available extended channels once for diagnostics
            if (!_extendedChannelsLogged)
            {
                LogAvailableExtendedChannels(frames);
                _extendedChannelsLogged = true;
            }

            System.Diagnostics.Debug.WriteLine("Clearing buffer and adding frames");

            // Clear buffer and load new data (use AddRange to bypass size limit)
            _viewModel.Buffer.Clear();
            _viewModel.Buffer.AddRange(frames);

            // Debug loaded data
            System.Diagnostics.Debug.WriteLine($"Loaded {frames.Count} frames");
            if (frames.Count > 0)
            {
                var first = frames[0];
                var last = frames[frames.Count - 1];
                System.Diagnostics.Debug.WriteLine($"First frame: Time={first.Time:F2}s, Speed={first.Speed:F1}km/h, GPS=({first.PosX:F6},{first.PosY:F6})");
                System.Diagnostics.Debug.WriteLine($"Last frame: Time={last.Time:F2}s, Speed={last.Speed:F1}km/h, GPS=({last.PosX:F6},{last.PosY:F6})");
            }
            
            // Debug what's actually in the buffer after adding
            System.Diagnostics.Debug.WriteLine($"Buffer now has {_viewModel.Buffer.Frames.Count} frames");
            if (_viewModel.Buffer.Frames.Count > 0)
            {
                var bufFirst = _viewModel.Buffer.Frames[0];
                var bufLast = _viewModel.Buffer.Frames[_viewModel.Buffer.Frames.Count - 1];
                System.Diagnostics.Debug.WriteLine($"Buffer first: Time={bufFirst.Time:F2}s, Speed={bufFirst.Speed:F1}km/h, GPS=({bufFirst.PosX:F6},{bufFirst.PosY:F6})");
                System.Diagnostics.Debug.WriteLine($"Buffer last: Time={bufLast.Time:F2}s, Speed={bufLast.Speed:F1}km/h, GPS=({bufLast.PosX:F6},{bufLast.PosY:F6})");
            }

            System.Diagnostics.Debug.WriteLine("Loaded recording ready for playback");

            _isPaused = true;
            _playbackController.Pause(); // Make sure playback is stopped initially
            _cachedTransformedFrames = null; _boundsCacheFrameCount = -1; _lapTimeCacheFrameCount = -1; _sliderMarkerFrameCount = -1; // Clear cache to force redraw
            _carArrow = null; // Clear car arrow reference
            _timingCache = null;
            _timingCacheFrameCount = -1;
            _lastLapNumber = -1; // Force initial lap draw
            
            _viewModel.ScrubToIndex(0);
            
            // Debug current frame after scrubbing
            if (_viewModel.CurrentFrame != null)
            {
                var curr = _viewModel.CurrentFrame;
                System.Diagnostics.Debug.WriteLine($"After ScrubToIndex(0): CurrentIndex={_viewModel.Buffer.CurrentIndex}, Time={curr.Time:F2}s, Speed={curr.Speed:F1}km/h, GPS=({curr.PosX:F6},{curr.PosY:F6})");
            }

            System.Diagnostics.Debug.WriteLine("Updating display");

            // Update display
            UpdatePlayPauseButton();
            UpdateDisplay();
            
            // Show status of what was drawn
            if (_viewModel.CurrentFrame != null)
            {
                var status = $"Canvas has {TrackCanvas.Children.Count} elements\nCurrent Lap: {_viewModel.CurrentFrame.CurrentLap}\nLast Lap Number: {_lastLapNumber}";
                System.Diagnostics.Debug.WriteLine(status);
            }
            
            // Analyze lap distribution
            var lapCounts = frames.GroupBy(f => f.CurrentLap).OrderBy(g => g.Key).Select(g => $"Lap {g.Key}: {g.Count()} frames").ToList();
            var lapInfo = string.Join("\n", lapCounts);

            StatusText.Text = $"Loaded: {fileInfo.FileName}";
            StatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
            LapInfoText.Text = $"{frames.Count} frames • {fileInfo.Duration:mm\\:ss}";
            UpdateControlsState();
            
            // Store current track name (extract from file or metadata)
            _currentTrackName = fileInfo.TrackName ?? System.IO.Path.GetFileNameWithoutExtension(fileInfo.FileName);
            _currentReplayFilePath = fileInfo.FilePath; // Store the replay file path
            
            // Try to load pre-generated track map
            bool hasTrackMap = LoadTrackMapIfExists();

            // If a map was loaded after the first draw, force a redraw so it appears immediately
            if (hasTrackMap && _showTrackMap)
            {
                ClearCanvas();
                _cachedTransformedFrames = null; _boundsCacheFrameCount = -1;
                DrawCenterline();
                UpdateDisplay();
            }
            
            // Show message if no track map exists (commented out to avoid interruption)
            // if (!hasTrackMap)
            // {
            //     MessageBox.Show($"No track map found for '{_currentTrackName}'.\n\n" +
            //                    "Click the 'Generate Track Map' button to create a permanent reference map.\n" +
            //                    "You'll need to select a DuckDB file with at least 9 laps (first lap will be ignored as pit exit).",
            //                    "Track Map Required", MessageBoxButton.OK, MessageBoxImage.Information);
            // }

            var debugPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LMU_Telemetry_Debug.txt");
            string trackMapStatus = _generatedTrackMap != null 
                ? $"Track map: {_generatedTrackMap.Points.Count} points ({_generatedTrackMap.TotalLength:F0}m)" 
                : "Track map: Not generated (click Generate Track Map button)";
            
            // Removed excessive MessageBox - user can see status in status bar
            // MessageBox.Show($"Successfully loaded {frames.Count} frames from:\n{fileInfo.DisplayName}\n\n" +
            //                $"Lap Distribution:\n{lapInfo}\n\n" +
            //                $"Canvas Elements: {TrackCanvas.Children.Count}\n" +
            //                $"Current Lap: {_viewModel.CurrentFrame?.CurrentLap}\n" +
            //                $"{trackMapStatus}\n\n" +
            //                $"Debug file: {debugPath}",
            //                "Recording Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            var errorDetails = $"Unexpected error loading recording:\n{ex.Message}\n\nType: {ex.GetType().Name}";
            if (ex.InnerException != null)
            {
                errorDetails += $"\n\nInner Exception:\n{ex.InnerException.Message}";
            }
            errorDetails += $"\n\nStack Trace:\n{ex.StackTrace}";
            
            System.Diagnostics.Debug.WriteLine(errorDetails);
            
            MessageBox.Show(errorDetails, 
                           "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Load failed";
            StatusText.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
    private void ClearCanvas()
    {
        if (TrackCanvas == null) return;
        TrackCanvas.Children.Clear();
    }
    
    private void DrawCenterline()
    {
        if (TrackCanvas == null) return;
        if (!_showTrackMap) return;
        
        var canvasWidth = TrackCanvas.ActualWidth;
        var canvasHeight = TrackCanvas.ActualHeight;
        
        System.Diagnostics.Debug.WriteLine($"DrawCenterline: Canvas size: {canvasWidth}x{canvasHeight}, TrackMap: {(_generatedTrackMap != null ? "YES" : "NO")}");
        
        if (canvasWidth == 0 || canvasHeight == 0)
        {
            System.Diagnostics.Debug.WriteLine("DrawCenterline: Canvas not sized yet, scheduling retry");
            // Canvas not sized yet, retry after layout
            Dispatcher.BeginInvoke(new Action(() => DrawCenterline()), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }
        
        // Draw generated track map if available
        if (_generatedTrackMap != null && _generatedTrackMap.Points.Count > 0)
        {
            var trackPositions = _generatedTrackMap.GetPositions();
            
            // Apply same 80-degree rotation as telemetry fallback
            const double rotationRadians = (Math.PI / 2.0) - (10.0 * Math.PI / 180.0);
            var cosTheta = Math.Cos(rotationRadians);
            var sinTheta = Math.Sin(rotationRadians);
            
            var rotatedTrack = trackPositions.Select(p => {
                var x = p.X;
                var y = p.Y;
                return new Point(
                    x * cosTheta - y * sinTheta,
                    x * sinTheta + y * cosTheta
                );
            }).ToList();
            
            // Find bounds after rotation
            double minX = rotatedTrack.Min(p => p.X);
            double maxX = rotatedTrack.Max(p => p.X);
            double minY = rotatedTrack.Min(p => p.Y);
            double maxY = rotatedTrack.Max(p => p.Y);
            
            double rangeX = maxX - minX;
            double rangeY = maxY - minY;
            
            if (rangeX < 1) rangeX = 1;
            if (rangeY < 1) rangeY = 1;
            
            double scaleX = canvasWidth / rangeX;
            double scaleY = canvasHeight / rangeY;
            double scale = Math.Min(scaleX, scaleY) * 0.9;
            
            double offsetX = (canvasWidth - rangeX * scale) / 2;
            double offsetY = (canvasHeight - rangeY * scale) / 2;
            
            // Apply same canvas transformation as telemetry, then zoom around anchor
            var basePoints = rotatedTrack.Select(p => new Point(
                offsetX + (p.X - minX) * scale,
                offsetY + (maxY - p.Y) * scale
            )).ToList();

            // Draw black border
            var borderPolyline = new Polyline
            {
                Stroke = System.Windows.Media.Brushes.Black,
                StrokeThickness = 6.615, // +10.25% total (another 5% bump)
                Points = new PointCollection(basePoints)
            };

            // Draw white track map
            var trackPolyline = new Polyline
            {
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 4.41, // +10.25% total (another 5% bump)
                Points = new PointCollection(basePoints)
            };
            
            // Insert at bottom so telemetry draws on top
            TrackCanvas.Children.Insert(0, borderPolyline);
            TrackCanvas.Children.Insert(1, trackPolyline);
            
            // Draw corner numbers if available
            if (_generatedTrackMap.Corners.Count > 0)
            {
                DrawCornerLabels(rotatedTrack, minX, minY, rangeX, rangeY, scale, offsetX, offsetY, maxY);
            }
            
            return;
        }
        
        // Fallback to old centerline (legacy)
        if (_trackCenterline == null || _cachedTransformedFrames == null || _cachedTransformedFrames.Count == 0)
            return;
        
        // Get centerline points and transform them to canvas coordinates just like telemetry
        var centerlinePoints = _trackCenterline.GetCenterlinePoints();
        
        // Find bounds of centerline in world coordinates
        double clMinX = centerlinePoints.Min(p => p.X);
        double clMaxX = centerlinePoints.Max(p => p.X);
        double clMinY = centerlinePoints.Min(p => p.Y);
        double clMaxY = centerlinePoints.Max(p => p.Y);
        
        double clRangeX = clMaxX - clMinX;
        double clRangeY = clMaxY - clMinY;
        
        if (clRangeX < 1) clRangeX = 1;
        if (clRangeY < 1) clRangeY = 1;
        
        double clScaleX = canvasWidth / clRangeX;
        double clScaleY = canvasHeight / clRangeY;
        double clScale = Math.Min(clScaleX, clScaleY) * 0.9;
        
        double clOffsetX = (canvasWidth - clRangeX * clScale) / 2;
        double clOffsetY = (canvasHeight - clRangeY * clScale) / 2;
        
        // Draw centerline as polyline
        var clBasePoints = centerlinePoints.Select(p => new Point(
            clOffsetX + (p.X - clMinX) * clScale,
            clOffsetY + (clMaxY - p.Y) * clScale
        )).ToList();

        var clPolyline = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), // Semi-transparent white
            StrokeThickness = 2,
            Points = new PointCollection(clBasePoints)
        };
        
        TrackCanvas.Children.Insert(0, clPolyline); // Insert at bottom so telemetry draws on top
    }
    
    private bool _isUpdatingSlider = false;
    private int _sliderMarkerFrameCount = -1; // rebuild markers only when frame count changes

    private void UpdateTimeSlider()
    {
        if (_viewModel.CurrentFrame == null || TimeSlider == null || _isUpdatingSlider) return;

        _isUpdatingSlider = true;

        var frames = _viewModel.Buffer.Frames;
        if (frames.Count > 0)
        {
            TimeSlider.Maximum = frames.Count - 1;
            TimeSlider.Value   = _viewModel.Buffer.CurrentIndex;
            TimeSlider.IsEnabled = true;

            // Rebuild the lap/sector markers only when the buffer size changes
            if (frames.Count != _sliderMarkerFrameCount)
            {
                _sliderMarkerFrameCount = frames.Count;
                RebuildSliderMarkers(frames);
            }
        }

        var time = _viewModel.CurrentFrame.Time;
        if (TimeText != null)
            TimeText.Text = $"{(int)(time / 60)}:{(int)(time % 60):D2}.{(int)((time % 1) * 1000):D3}";

        _isUpdatingSlider = false;
    }

    private void RebuildSliderMarkers(IReadOnlyList<TelemetryFrame> frames)
    {
        if (SliderMarkersCanvas == null || frames.Count == 0) return;

        SliderMarkersCanvas.Children.Clear();

        // Wait until the canvas has been laid out so ActualWidth is valid.
        // If not yet available, defer to next layout pass.
        SliderMarkersCanvas.Dispatcher.InvokeAsync(() =>
        {
            SliderMarkersCanvas.Children.Clear();
            double w = SliderMarkersCanvas.ActualWidth;
            if (w < 1) return;

            int total = frames.Count;

            // Track which laps and sectors we've already marked so we draw each boundary once.
            int prevLap    = frames[0].CurrentLap;
            int prevSector = frames[0].Sector;

            for (int i = 1; i < total; i++)
            {
                var f = frames[i];
                double x = (double)i / (total - 1) * w;

                bool lapChange    = f.CurrentLap != prevLap;
                bool sectorChange = f.Sector     != prevSector && !lapChange;

                if (lapChange)
                {
                    // Solid white line — lap boundary, full height
                    var line = new System.Windows.Shapes.Line
                    {
                        X1 = x, Y1 = 0, X2 = x, Y2 = 20,
                        Stroke          = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                        StrokeThickness = 2,
                        IsHitTestVisible = false,
                    };
                    SliderMarkersCanvas.Children.Add(line);
                    prevLap    = f.CurrentLap;
                    prevSector = f.Sector;
                }
                else if (sectorChange)
                {
                    // Dashed line — sector boundary, clearly visible yellow-white
                    var line = new System.Windows.Shapes.Line
                    {
                        X1 = x, Y1 = 1, X2 = x, Y2 = 19,
                        Stroke          = new SolidColorBrush(Color.FromArgb(200, 220, 200, 100)),
                        StrokeThickness = 1.5,
                        StrokeDashArray = new DoubleCollection { 3, 2 },
                        IsHitTestVisible = false,
                    };
                    SliderMarkersCanvas.Children.Add(line);
                    prevSector = f.Sector;
                }
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Draw corner number labels on the track map.
    /// </summary>
    private void DrawCornerLabels(List<Point> rotatedTrack, double minX, double minY, double rangeX, double rangeY, double scale, double offsetX, double offsetY, double maxY)
    {
        if (_generatedTrackMap?.Corners == null) return;
        
        foreach (var corner in _generatedTrackMap.Corners)
        {
            // Apply the same 80-degree rotation as the track map
            const double rotationRadians = (Math.PI / 2.0) - (10.0 * Math.PI / 180.0);
            var cosTheta = Math.Cos(rotationRadians);
            var sinTheta = Math.Sin(rotationRadians);
            
            var x = corner.Position.X;
            var y = corner.Position.Y;
            var rotatedX = x * cosTheta - y * sinTheta;
            var rotatedY = x * sinTheta + y * cosTheta;

            // Transform to canvas coordinates
            double canvasX = offsetX + (rotatedX - minX) * scale;
            double canvasY = offsetY + (maxY - rotatedY) * scale;

            // Draw corner number circle
            var circle = new Ellipse
            {
                Width = 24,
                Height = 24,
                Fill = System.Windows.Media.Brushes.Yellow,
                Stroke = System.Windows.Media.Brushes.Black,
                StrokeThickness = 2
            };
            Canvas.SetLeft(circle, canvasX - 12);
            Canvas.SetTop(circle, canvasY - 12);
            TrackCanvas.Children.Add(circle);

            // Draw corner number text
            var text = new TextBlock
            {
                Text = corner.Number.ToString(),
                Foreground = System.Windows.Media.Brushes.Black,
                FontSize = 12,
                FontWeight = System.Windows.FontWeights.Bold,
                TextAlignment = System.Windows.TextAlignment.Center,
                Width = 24,
                Height = 24,
                LineHeight = 24
            };
            Canvas.SetLeft(text, canvasX - 12);
            Canvas.SetTop(text, canvasY - 12);
            TrackCanvas.Children.Add(text);
        }
    }
    
    /// <summary>
    /// Load pre-generated track map if it exists for the current track.
    /// Returns true if map was loaded, false otherwise.
    /// Shows/hides Generate and Delete buttons appropriately.
    /// </summary>
    private bool LoadTrackMapIfExists()
    {
        if (string.IsNullOrEmpty(_currentTrackName))
        {
            System.Diagnostics.Debug.WriteLine("LoadTrackMapIfExists: No track name set");
            GenerateTrackMapButton.Visibility = Visibility.Collapsed;
            DeleteTrackMapButton.Visibility = Visibility.Collapsed;
            return false;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"LoadTrackMapIfExists: Attempting to load track map for '{_currentTrackName}'");
            _generatedTrackMap = TrackMapStorage.Load(_currentTrackName);
            
            if (_generatedTrackMap != null)
            {
                System.Diagnostics.Debug.WriteLine($"✓ Loaded track map: {_generatedTrackMap.Points.Count} points");
                Log($"✓ Loaded pre-generated track map: {_generatedTrackMap.Points.Count} points, {_generatedTrackMap.TotalLength:F1}m");
                Log($"  Generated from {_generatedTrackMap.GeneratedFromLapCount} laps on {_generatedTrackMap.GeneratedDateTime:yyyy-MM-dd HH:mm}");
                
                // Hide Generate, show Delete
                GenerateTrackMapButton.Visibility = Visibility.Collapsed;
                DeleteTrackMapButton.Visibility = Visibility.Visible;
                
                // Force redraw with track map
                _cachedTransformedFrames = null; _boundsCacheFrameCount = -1;
                return true;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"No track map found for '{_currentTrackName}'");
                Log($"No pre-generated track map found for '{_currentTrackName}'");
                Log("Use Developer Mode (Ctrl+Shift+D) to build one by hand");

                // GenerateTrackMapButton stays Collapsed - it runs the same automatic
                // averaging/smoothing pipeline that's now considered unreliable and was
                // dropped from Developer Mode in favor of hand-placed corners on the raw
                // driven path. Leaving this button live would let it silently overwrite a
                // hand-curated map with the broken auto-generated one.
                DeleteTrackMapButton.Visibility = Visibility.Collapsed;
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"ERROR loading track map: {ex.Message}");
            _generatedTrackMap = null;

            DeleteTrackMapButton.Visibility = Visibility.Collapsed;
            return false;
        }
    }

    /// <summary>
    /// Generate Track Map button click handler.
    /// Uses the currently loaded replay file for track map generation.
    /// First lap (pit exit) is ignored, remaining laps are used.
    /// </summary>
    private void GenerateTrackMapButton_Click(object sender, RoutedEventArgs e)
    {
        // Check if we have a replay loaded
        if (string.IsNullOrEmpty(_currentReplayFilePath))
        {
            MessageBox.Show("No replay file loaded.\n\nPlease load a recording first, then click Generate Track Map.",
                           "No Replay Loaded", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Confirm using the current file
        var result = MessageBox.Show(
            $"Generate track map from the currently loaded replay file?\n\n" +
            $"File: {System.IO.Path.GetFileName(_currentReplayFilePath)}\n" +
            $"Track: {_currentTrackName}\n\n" +
            "The first lap (pit exit) will be ignored.",
            "Generate Track Map",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        GenerateTrackMapFromFile(_currentReplayFilePath);
    }

    /// <summary>
    /// Generate track map from a DuckDB file.
    /// </summary>
    private void GenerateTrackMapFromFile(string sourceFilePath)
    {
        try
        {
            StatusText.Text = "Generating track map...";
            StatusText.Foreground = new SolidColorBrush(Colors.Yellow);
            GenerateTrackMapButton.IsEnabled = false;

            // Don't log during generation to avoid file lock issues
            // Log("=== GENERATING CANONICAL TRACK MAP ===");
            // Log($"Source file: {sourceFilePath}");
            
            // Load all frames from selected DuckDB
            var frames = _duckDbReader.LoadTelemetryData(sourceFilePath);
            
            if (frames.Count == 0)
            {
                MessageBox.Show("No telemetry data found in file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                GenerateTrackMapButton.IsEnabled = true;
                StatusText.Text = "No data";
                StatusText.Foreground = new SolidColorBrush(Colors.Red);
                return;
            }
            
            // Group frames by lap
            var allLaps = frames.GroupBy(f => f.CurrentLap)
                                .OrderBy(g => g.Key)
                                .Select(g => g.ToList())
                                .Where(lap => lap.Count > 100) // Filter out incomplete laps
                                .ToList();
            
            // Don't log to avoid file lock issues during generation
            // Log($"Found {allLaps.Count} valid laps from {frames.Count} total frames");
            
            if (allLaps.Count < 2)
            {
                MessageBox.Show($"At least 2 laps are required for track map generation.\n\n" +
                               $"Found: {allLaps.Count} laps\n" +
                               $"Note: First lap is always ignored as pit exit\n\n" +
                               "Please select a file with more laps and try again.", 
                               "Insufficient Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                GenerateTrackMapButton.IsEnabled = true;
                StatusText.Text = "Not enough laps";
                StatusText.Foreground = new SolidColorBrush(Colors.Orange);
                return;
            }
            
            // Skip first lap (pit exit), use remaining laps for generation
            var lapsForGeneration = allLaps.Skip(1).ToList();
            int lapCount = lapsForGeneration.Count;
            
            // Warn if less than recommended but proceed anyway
            if (allLaps.Count < 9)
            {
                var result = MessageBox.Show(
                    $"Recommendation: 9+ laps for best results\n\n" +
                    $"Found: {allLaps.Count} laps ({lapCount} will be used after ignoring pit exit)\n\n" +
                    "You can proceed with fewer laps, but the track map may be less accurate.\n\n" +
                    "Continue with {lapCount} lap(s)?",
                    "Fewer Laps Than Recommended",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                {
                    GenerateTrackMapButton.IsEnabled = true;
                    StatusText.Text = "Cancelled";
                    StatusText.Foreground = new SolidColorBrush(Colors.Gray);
                    return;
                }
            }
            
            // Don't log to avoid file lock issues during generation
            // Log($"Using {lapCount} lap(s) for generation (ignoring lap 1 as pit exit)");
            // Skip detailed lap logging to avoid file lock
            // foreach (var lap in lapsForGeneration)
            // {
            //     var lapNum = lap.First().CurrentLap;
            //     Log($"  Lap {lapNum}: {lap.Count} frames");
            // }
            
            // Determine track name from file or prompt
            string trackName = _currentTrackName ?? string.Empty;
            if (string.IsNullOrEmpty(trackName))
            {
                trackName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
                // Could add a dialog here to let user enter track name
            }
            
            // Generate track map using advanced algorithm
            // Skipping log calls to avoid file lock issues
            // Log("Running track map generation algorithm...");
            // Log("  Step 1: Normalize laps to common reference frame (translation + rotation)");
            // Log("  Step 2: Resample to uniform distance intervals (500 points)");
            // Log($"  Step 3: Average across {lapCount} lap(s)");
            // Log("  Step 4: Apply smoothing filter (window size 15)");
            // Log("  Step 5: Calculate heading and curvature");
            
            var trackMap = TrackMapGenerator.Generate(lapsForGeneration, resamplePointCount: 500, smoothingWindowSize: 15);
            
            // Save to permanent storage in project directory
            TrackMapStorage.Save(trackMap, trackName);
            
            // Load it into current session if it matches
            if (!string.IsNullOrEmpty(_currentTrackName) && _currentTrackName == trackName)
            {
                _generatedTrackMap = trackMap;
                _cachedTransformedFrames = null; _boundsCacheFrameCount = -1; // Force redraw
                
                // Update button visibility
                GenerateTrackMapButton.Visibility = Visibility.Collapsed;
                DeleteTrackMapButton.Visibility = Visibility.Visible;
            }
            else
            {
                // Track name didn't match current - load it anyway and show message
                _generatedTrackMap = trackMap;
                _cachedTransformedFrames = null; _boundsCacheFrameCount = -1;
                GenerateTrackMapButton.Visibility = Visibility.Collapsed;
                DeleteTrackMapButton.Visibility = Visibility.Visible;
            }
            
            Log($"✓ Track map generated and saved!");
            Log($"  Track name: {trackName}");
            Log($"  Points: {trackMap.Points.Count}");
            Log($"  Total length: {trackMap.TotalLength:F1}m");
            Log($"  Generated from: {lapCount} lap(s) (pit exit ignored)");
            Log($"  Storage: {TrackMapStorage.GetStorageDirectory()}");
            
            StatusText.Text = "Track map generated";
            StatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
            
            MessageBox.Show($"Track map successfully generated!\n\n" +
                           $"Track: {trackName}\n" +
                           $"Points: {trackMap.Points.Count}\n" +
                           $"Track length: {trackMap.TotalLength:F0} meters\n" +
                           $"Generated from: {lapCount} lap(s) (pit exit ignored)\n\n" +
                           $"The track map has been saved permanently and will be used automatically " +
                           $"for all future sessions on this track.",
                           "Generation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            
            // Redraw if current session matches
            if (_generatedTrackMap != null)
            {
                UpdateDisplay();
            }
        }
        catch (Exception ex)
        {
            // Skip logging to avoid file lock
            // Log($"ERROR generating track map: {ex.Message}");
            // Log($"Stack trace: {ex.StackTrace}");
            MessageBox.Show($"Failed to generate track map:\n\n{ex.Message}\n\nSee debug log for details.", 
                           "Generation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Generation failed";
            StatusText.Foreground = new SolidColorBrush(Colors.Red);
        }
        finally
        {
            GenerateTrackMapButton.IsEnabled = true;
        }
    }
    
    /// <summary>
    /// Delete Track Map button click handler.
    /// Removes the saved track map for the current track to allow regeneration.
    /// </summary>
    private void DeleteTrackMapButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentTrackName))
        {
            MessageBox.Show("No track loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to delete the track map for '{_currentTrackName}'?\n\n" +
            "This will remove the permanent reference map and you'll need to generate a new one.\n\n" +
            "This action cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            Log($"=== DELETING TRACK MAP: {_currentTrackName} ===");
            
            // Delete from storage
            TrackMapStorage.Delete(_currentTrackName);
            
            // Clear from current session
            _generatedTrackMap = null;
            _cachedTransformedFrames = null; _boundsCacheFrameCount = -1;
            
            // GenerateTrackMapButton stays Collapsed - see LoadTrackMapIfExists for why.
            DeleteTrackMapButton.Visibility = Visibility.Collapsed;

            Log($"✓ Track map deleted successfully");
            Log("Use Developer Mode (Ctrl+Shift+D) to build a new one by hand");
            
            StatusText.Text = "Track map deleted";
            StatusText.Foreground = new SolidColorBrush(Colors.Orange);
            
            MessageBox.Show(
                $"Track map for '{_currentTrackName}' has been deleted.\n\n" +
                "You can now generate a new track map using the 'Generate Track Map' button.",
                "Deleted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            
            // Redraw without track map
            UpdateDisplay();
        }
        catch (Exception ex)
        {
            Log($"ERROR deleting track map: {ex.Message}");
            MessageBox.Show(
                $"Failed to delete track map:\n\n{ex.Message}",
                "Delete Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    
    // DEPRECATED: Old automatic centerline building - replaced by manual track map generation
    private void BuildTrackCenterline(List<TelemetryFrame> frames)
    {
        try
        {
            Log("=== BUILDING TRACK CENTERLINE (MoTeC-style) ===");
            
            if (frames.Count == 0)
            {
                Log("No frames to build centerline from");
                return;
            }
            
            // Group frames by lap
            var laps = frames.GroupBy(f => f.CurrentLap)
                            .OrderBy(g => g.Key)
                            .Select(g => g.ToList())
                            .Where(lap => lap.Count > 100) // Filter out very short laps (off-track, invalid data)
                            .ToList();
            
            Log($"Found {laps.Count} laps with >100 frames");
            foreach (var lap in laps)
            {
                var lapNum = lap.First().CurrentLap;
                Log($"  Lap {lapNum}: {lap.Count} frames");
            }
            
            if (laps.Count == 0)
            {
                Log("No valid laps found (all < 100 frames)");
                return;
            }
            
            Log($"Found {laps.Count} laps with >100 frames each");
            
            // Build centerline from all valid laps
            // LMU provides world coordinates in meters, no GPS conversion needed
            _trackCenterline = new TrackCenterline();
            _trackCenterline.BuildFromLaps(laps);
            
            // Force redraw with new centerline
            _cachedTransformedFrames = null; _boundsCacheFrameCount = -1;
            
            Log($"✓ Centerline built: {_trackCenterline.PointCount} points, {_trackCenterline.TrackLength:F1}m total length");
            Log("All telemetry will now use track coordinates (s, d) instead of GPS→SVG");
        }
        catch (Exception ex)
        {
            Log($"ERROR building centerline: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Centerline build failed: {ex}");
            _trackCenterline = null;
        }
    }
    
    private void Log(string message)
    {
        var output = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        System.Diagnostics.Debug.WriteLine(output);
        
        // Try to write to log file with retry and sharing
        int maxRetries = 3;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                // Open file with FileShare.ReadWrite to allow concurrent access
                using (var fileStream = new System.IO.FileStream(_logFilePath, 
                    System.IO.FileMode.Append, 
                    System.IO.FileAccess.Write, 
                    System.IO.FileShare.ReadWrite))
                {
                    using (var writer = new System.IO.StreamWriter(fileStream))
                    {
                        writer.Write(output);
                        writer.Flush();
                    }
                }
                return; // Success
            }
            catch (System.IO.IOException) when (i < maxRetries - 1)
            {
                // Retry after a short delay
                System.Threading.Thread.Sleep(10);
            }
            catch
            {
                // Final attempt failed or non-IO error, give up
                return;
            }
        }
    }

    private void LogAvailableExtendedChannels(IEnumerable<TelemetryFrame> frames)
    {
        try
        {
            var keys = frames
                .SelectMany(f => f.ExtendedData?.Keys ?? Enumerable.Empty<string>())
                .GroupBy(k => k)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .ToList();

            var lines = new List<string>
            {
                $"=== ExtendedData channels ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ===",
                $"Frames inspected: {frames.Count()}"
            };

            foreach (var g in keys)
            {
                lines.Add($"{g.Key} : {g.Count()} frames");
            }

            if (keys.Count == 0)
            {
                lines.Add("(no ExtendedData entries found)");
            }

            System.IO.File.AppendAllLines(_logFilePath, lines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to log extended channels: {ex.Message}");
        }
    }
    
    private void LoadEmbeddedTrackMaps()
    {
        try
        {
            // Scan the Resources/TrackMaps folder for files
            var trackMapsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "TrackMaps");
            Log($"Looking for track maps in: {trackMapsPath}");
            
            if (System.IO.Directory.Exists(trackMapsPath))
            {
                var files = System.IO.Directory.GetFiles(trackMapsPath, "*.*")
                    .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg") || f.EndsWith(".svg"))
                    .ToList();

                Log($"Found {files.Count} track map files");
                
                foreach (var file in files)
                {
                    var trackName = System.IO.Path.GetFileNameWithoutExtension(file);
                    _embeddedTrackMaps[trackName] = file; // Use direct file path
                    Log($"  - {trackName}: {file}");
                }
            }
            else
            {
                Log($"ERROR: Track maps directory does not exist: {trackMapsPath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading embedded track maps: {ex.Message}");
        }
    }

    // SVG and Calibration code removed - using centerline system

    private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isUpdatingSlider && _viewModel.Buffer.HasData && TimeSlider != null)
            {
                var index = (int)TimeSlider.Value;
                if (index != _viewModel.Buffer.CurrentIndex)
                {
                    _viewModel.ScrubToIndex(index);
                    UpdateDisplay();
                }
            }
        }

    private void InputGraphCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (InputGraphCanvas != null && _viewModel.Buffer.HasData)
        {
            _isInputGraphDragging = true;
            _isPaused = true;
            _viewModel.IsLiveMode = false;
            InputGraphCanvas.CaptureMouse();
            UpdatePlayPauseButton();
            
            // Navigate to position based on cursor position in canvas
            DragInputGraphToPosition(e.GetPosition(InputGraphCanvas));
        }
    }

    private void InputGraphCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isInputGraphDragging && InputGraphCanvas != null && _viewModel.Buffer.HasData)
        {
            DragInputGraphToPosition(e.GetPosition(InputGraphCanvas));
        }
    }

    private void InputGraphCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isInputGraphDragging)
        {
            _isInputGraphDragging = false;
            InputGraphCanvas?.ReleaseMouseCapture();
        }
    }

    private void DragInputGraphToPosition(Point canvasPosition)
    {
        if (!_viewModel.Buffer.HasData) return;

        var frames = _viewModel.Buffer.Frames;
        if (frames.Count == 0) return;

        double canvasWidth = InputGraphCanvas.ActualWidth;
        if (canvasWidth <= 0) return;

        // Match the InputRenderer's calculation
        // The yellow line is positioned at 80% of the visible frame range
        int visibleFrames = 600; // 10 seconds @ 60Hz
        double currentPositionRatio = 0.8; // Yellow line at 80% position
        int framesBeforeCurrent = (int)(visibleFrames * currentPositionRatio);
        
        // Calculate which frame is at the clicked X position
        // frameOffsetInWindow is where the click maps in the visible window
        double frameOffsetInWindow = (canvasPosition.X / canvasWidth) * visibleFrames;
        
        // Adjust to account for the current position being at 80% of the window
        int frameOffsetFromCurrent = (int)frameOffsetInWindow - framesBeforeCurrent;
        
        int newIndex = _viewModel.Buffer.CurrentIndex + frameOffsetFromCurrent;
        newIndex = Math.Clamp(newIndex, 0, frames.Count - 1);

        _viewModel.ScrubToIndex(newIndex);
    }}