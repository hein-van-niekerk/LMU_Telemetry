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

        RefreshPreviewCanvas();
    }

    private void SaveCandidateButton_Click(object sender, RoutedEventArgs e)
    {
        string? error = _vm.SaveCandidate();
        if (error != null)
        {
            MessageBox.Show(error, "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SaveCandidateButton.IsEnabled    = false;
        DiscardCandidateButton.IsEnabled = false;
        CandidateInfoText.Text = "Saved to library.";
        RefreshPreviewCanvas();
    }

    private void DiscardCandidateButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.DiscardCandidate();
        SaveCandidateButton.IsEnabled    = false;
        DiscardCandidateButton.IsEnabled = false;
        CandidateInfoText.Text = "—";
        RefreshPreviewCanvas();
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

        // Draw candidate in orange (new map)
        if (_vm.CandidateMap != null && _vm.CandidateMap.Points.Count > 1)
            DrawPolyline(_vm.CandidateMap.GetPositions(), "#FFA040", 2.5, w, h, "#3A2000");
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
            $"Delete map "{_vm.SelectedLibraryEntry.TrackKey}" from the library?\nThis cannot be undone.",
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
