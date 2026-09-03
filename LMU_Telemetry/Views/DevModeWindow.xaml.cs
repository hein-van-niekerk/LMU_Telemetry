using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LMU_Telemetry.Models;
using LMU_Telemetry.Services;
using LMU_Telemetry.ViewModels;
using Microsoft.Win32;
using Point = System.Windows.Point;

namespace LMU_Telemetry.Views;

/// <summary>
/// Code-behind for the Dev Mode multi-tab window.
/// </summary>
public partial class DevModeWindow : Window
{
    private readonly DevModeViewModel _vm;
    private readonly DevLapRecorder _recorder;
    private GeneratedTrackMap? _existingMap;  // currently active map for overlay

    public DevModeWindow(DevModeViewModel vm, DevLapRecorder recorder)
    {
        _vm       = vm;
        _recorder = recorder;

        InitializeComponent();

        DataContext = _vm;

        // Bind list sources
        LapListBox.ItemsSource     = _vm.Laps;
        LibraryListBox.ItemsSource = _vm.LibraryEntries;

        // Initial data load
        _vm.RefreshLibrary();

        // Reflect current track key
        UpdateTrackKeyDisplay();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DevModeViewModel.CurrentTrackKey))
                UpdateTrackKeyDisplay();
            if (e.PropertyName == nameof(DevModeViewModel.StatusMessage))
                StatusBar.Text = _vm.StatusMessage;
            if (e.PropertyName == nameof(DevModeViewModel.CandidateMap))
                RefreshPreviewCanvas();
        };
    }

    // -----------------------------------------------------------------------
    // Track key display
    // -----------------------------------------------------------------------

    private void UpdateTrackKeyDisplay()
    {
        string key = _vm.CurrentTrackKey;
        TrackKeyText.Text = string.IsNullOrEmpty(key) ? "— (no session)" : key;
    }

    /// <summary>
    /// Called by MainWindow when the active track key changes.
    /// </summary>
    public void NotifyTrackKeyChanged(string trackKey, GeneratedTrackMap? existingMap)
    {
        _existingMap = existingMap;
        _vm.SetTrackKey(trackKey);
    }

    // -----------------------------------------------------------------------
    // RECORD tab handlers
    // -----------------------------------------------------------------------

    private void StartStopRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording)
        {
            _recorder.StopRecording();
            _vm.IsRecording = false;
            StartStopRecordButton.Content = "▶ START RECORDING";
            RecordingStatusText.Text = "Stopped";
        }
        else
        {
            _recorder.StartRecording();
            _vm.IsRecording = true;
            StartStopRecordButton.Content = "⏹ STOP RECORDING";
            RecordingStatusText.Text = "Recording…";
        }
    }

    private void RefreshLapsButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.RefreshLapList();
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
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#141414")),
        };

        var selector = new TelemetryFileSelector();
        selector.FileSelected += (_, fileInfo) =>
        {
            selectorWindow.Close();
            ImportLapsFromRecording(fileInfo);
        };

        selectorWindow.Content = selector;
        selectorWindow.ShowDialog();
    }

    private void ImportLapsFromRecording(TelemetryFileInfo fileInfo)
    {
        try
        {
            StatusBar.Text = "Loading recording…";

            var reader = new DuckDBTelemetryReader();
            var frames = reader.LoadTelemetryData(fileInfo.FilePath);

            if (frames.Count == 0)
            {
                MessageBox.Show("No telemetry data found in that file.", "Load Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusBar.Text = "Ready.";
                return;
            }

            string trackKey = fileInfo.TrackName;
            if (string.IsNullOrWhiteSpace(trackKey) || trackKey == "Unknown Track")
            {
                trackKey = PromptTrackKey(_vm.CurrentTrackKey) ?? string.Empty;
                if (string.IsNullOrEmpty(trackKey))
                {
                    StatusBar.Text = "Ready.";
                    return;
                }
            }

            var imported = _recorder.ImportLapsFromFrames(frames, trackKey);

            // Refresh the list from disk for this track (also updates the header).
            _vm.SetTrackKey(trackKey);
            _vm.RefreshLapList();

            MessageBox.Show(
                $"Imported {imported.Count} lap(s) from \"{fileInfo.FileName}\" for track \"{trackKey}\".",
                "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load recording:\n{ex.Message}", "Load Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusBar.Text = "Ready.";
        }
    }

    private void DeleteLapButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLap == null) return;
        var result = MessageBox.Show(
            $"Delete lap {_vm.SelectedLap.Lap.LapNumber} from disk?\nThis cannot be undone.",
            "Delete Lap", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            _vm.DeleteSelectedLap();
    }

    private void LapListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm.SelectedLap = LapListBox.SelectedItem as ViewModels.LapListItem;
    }

    private void LapKeepCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is ViewModels.LapListItem item)
            _vm.SaveLapKeepFlag(item);
    }

    // -----------------------------------------------------------------------
    // GENERATE & PREVIEW tab handlers
    // -----------------------------------------------------------------------

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        string? error = _vm.GenerateCandidate();
        if (error != null)
        {
            MessageBox.Show(error, "Generation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SaveCandidateButton.IsEnabled    = true;
        DiscardCandidateButton.IsEnabled = true;
        CandidateInfoText.Text = _vm.StatusMessage;

        // Merge is only offered once we know there's something to merge with.
        // Refresh the reference overlay from disk too — MainWindow only pushes
        // _existingMap when it has one loaded itself, which may be stale/null
        // even though a map does exist on disk for this track key.
        bool hasExisting = _vm.HasExistingMapForCurrentTrack;
        if (hasExisting)
            _existingMap = TrackMapStorage.Load(_vm.CurrentTrackKey);
        MergeCandidateButton.Visibility     = hasExisting ? Visibility.Visible : Visibility.Collapsed;
        MergeCandidateButton.IsEnabled      = hasExisting;
        UseNewCenterlineCheckBox.Visibility = hasExisting ? Visibility.Visible : Visibility.Collapsed;
        UseNewCenterlineCheckBox.IsChecked  = false;

        UpdateAlignmentInfoText();
        RefreshPreviewCanvas();

        // Non-blocking quality warning — candidate is already generated and usable either way.
        if (_vm.LastGenerationWarning != null)
            MessageBox.Show(_vm.LastGenerationWarning, "Candidate Quality", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void SaveCandidateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.HasExistingMapForCurrentTrack)
        {
            var confirm = MessageBox.Show(
                $"A map already exists for \"{_vm.CurrentTrackKey}\".\n\n" +
                "This will REPLACE it outright with the new candidate (no alignment).\n" +
                "Use \"Merge with Existing\" instead if you want to keep the verified centerline.\n\n" +
                "Continue?",
                "Replace Existing Map", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        string? error = _vm.SaveCandidate();
        if (error != null)
        {
            MessageBox.Show(error, "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResetCandidateControls("Saved to library.");
    }

    private void MergeCandidateButton_Click(object sender, RoutedEventArgs e)
    {
        bool useNewCenterline = UseNewCenterlineCheckBox.IsChecked == true;
        string? error = _vm.MergeCandidateWithExisting(useNewCenterline);
        if (error != null)
        {
            MessageBox.Show(error, "Merge Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResetCandidateControls(_vm.StatusMessage);
    }

    private void DiscardCandidateButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.DiscardCandidate();
        ResetCandidateControls("—");
    }

    private void ResetCandidateControls(string candidateInfoText)
    {
        SaveCandidateButton.IsEnabled       = false;
        DiscardCandidateButton.IsEnabled    = false;
        MergeCandidateButton.Visibility     = Visibility.Collapsed;
        MergeCandidateButton.IsEnabled      = false;
        UseNewCenterlineCheckBox.Visibility = Visibility.Collapsed;
        CandidateInfoText.Text = candidateInfoText;
        UpdateAlignmentInfoText();
        RefreshPreviewCanvas();
    }

    /// <summary>Show per-point divergence from the last auto-alignment against an existing map, if any.</summary>
    private void UpdateAlignmentInfoText()
    {
        var alignment = _vm.LastAlignment;
        if (alignment == null)
        {
            AlignmentInfoText.Visibility = Visibility.Collapsed;
            AlignmentInfoText.Text = "";
            return;
        }

        string text = $"Aligned to existing map:\n" +
            $"  avg divergence {alignment.AverageDivergenceMeters:F2} m, max {alignment.MaxDivergenceMeters:F2} m";
        if (alignment.HighDivergenceSegments.Count > 0)
        {
            text += $"\n  ⚠ {alignment.HighDivergenceSegments.Count} high-divergence stretch(es) — " +
                    "possible stitching error or off-line laps:";
            foreach (var seg in alignment.HighDivergenceSegments.Take(5))
                text += $"\n    @ {seg.LapDistance:F0} m: {seg.DivergenceMeters:F1} m off";
            if (alignment.HighDivergenceSegments.Count > 5)
                text += $"\n    (+{alignment.HighDivergenceSegments.Count - 5} more)";
        }

        AlignmentInfoText.Text = text;
        AlignmentInfoText.Visibility = Visibility.Visible;
    }

    private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshPreviewCanvas();
    }

    // -----------------------------------------------------------------------
    // Preview canvas rendering
    // -----------------------------------------------------------------------

    private void RefreshPreviewCanvas()
    {
        PreviewCanvas.Children.Clear();

        double w = PreviewCanvas.ActualWidth;
        double h = PreviewCanvas.ActualHeight;
        if (w < 10 || h < 10) return;

        // Draw existing map in dark grey (reference)
        if (_existingMap != null && _existingMap.Points.Count > 1)
            DrawPolyline(_existingMap.GetPositions(), "#444444", 1.5, w, h, null);

        // Draw candidate in orange (new map). Once an alignment has been
        // computed against the existing map, show the ICP-aligned positions
        // instead of the raw ones — that's what Merge will actually compare
        // and attach, so the preview should reflect it.
        if (_vm.CandidateMap != null && _vm.CandidateMap.Points.Count > 1)
        {
            var positions = _vm.LastAlignment != null && _vm.LastAlignment.AlignedCandidate.Count > 1
                ? _vm.LastAlignment.AlignedCandidate
                : _vm.CandidateMap.GetPositions();
            DrawPolyline(positions, "#FFA040", 2.5, w, h, "#3A2000");
        }
    }

    private void DrawPolyline(
        List<Point> worldPts,
        string strokeHex,
        double thickness,
        double canvasW,
        double canvasH,
        string? fillHex)
    {
        if (worldPts.Count < 2) return;

        // Compute bounding box
        double minX = worldPts.Min(p => p.X);
        double maxX = worldPts.Max(p => p.X);
        double minY = worldPts.Min(p => p.Y);
        double maxY = worldPts.Max(p => p.Y);

        double rangeX = maxX - minX;
        double rangeY = maxY - minY;
        if (rangeX < 1 || rangeY < 1) return;

        double margin = 20;
        double scaleX = (canvasW - margin * 2) / rangeX;
        double scaleY = (canvasH - margin * 2) / rangeY;
        double scale  = Math.Min(scaleX, scaleY);

        double offX = margin + (canvasW - margin * 2 - rangeX * scale) / 2;
        double offY = margin + (canvasH - margin * 2 - rangeY * scale) / 2;

        Point Project(Point p) =>
            new(offX + (p.X - minX) * scale,
                canvasH - (offY + (p.Y - minY) * scale));

        var poly = new Polyline
        {
            Stroke          = (Brush)new BrushConverter().ConvertFromString(strokeHex)!,
            StrokeThickness = thickness,
            StrokeLineJoin  = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        };

        foreach (var p in worldPts)
            poly.Points.Add(Project(p));

        PreviewCanvas.Children.Add(poly);
    }

    // -----------------------------------------------------------------------
    // LIBRARY tab handlers
    // -----------------------------------------------------------------------

    private void RefreshLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.RefreshLibrary();
    }

    private void ImportMapButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title  = "Import Track Map JSON",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        // Prompt for track key
        string? key = PromptTrackKey(_vm.CurrentTrackKey);
        if (key == null) return;

        string? error = _vm.ImportMap(dialog.FileName, key);
        if (error != null)
            MessageBox.Show(error, "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void DeleteLibraryEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLibraryEntry == null) return;
        var result = MessageBox.Show(
            $"Delete map \"{_vm.SelectedLibraryEntry.TrackKey}\" from the library?\nThis cannot be undone.",
            "Delete Map", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            _vm.DeleteSelectedLibraryEntry();
    }

    private void LibraryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm.SelectedLibraryEntry = LibraryListBox.SelectedItem as TrackMapLibraryEntry;
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    private static string? PromptTrackKey(string defaultKey)
    {
        // Simple inline prompt via an InputBox-style dialog built from a Window
        var win = new Window
        {
            Title  = "Track Key",
            Width  = 420,
            Height = 140,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#141414")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };

        var tb = new TextBox
        {
            Text       = defaultKey,
            Foreground = Brushes.White,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444444")),
            Margin     = new Thickness(12, 12, 12, 6),
            Padding    = new Thickness(4),
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 12,
        };

        var ok = new Button
        {
            Content    = "OK",
            Width      = 80,
            Height     = 26,
            Margin     = new Thickness(12, 0, 0, 10),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A5A")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A6A9A")),
        };
        ok.Click += (_, _) => { win.DialogResult = true; };

        var panel = new StackPanel();
        var label = new TextBlock
        {
            Text       = "Enter the track key (e.g. bahrain_endurance):",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
            Margin     = new Thickness(12, 10, 12, 2),
            FontSize   = 11,
        };
        panel.Children.Add(label);
        panel.Children.Add(tb);
        panel.Children.Add(ok);
        win.Content = panel;

        bool? result = win.ShowDialog();
        return result == true && !string.IsNullOrWhiteSpace(tb.Text) ? tb.Text.Trim() : null;
    }
}
