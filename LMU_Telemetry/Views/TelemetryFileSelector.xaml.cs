using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LMU.Telemetry.Core.Models;
using LMU.Telemetry.Core.Services;
using WinForms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace LMU_Telemetry.Views;

public partial class TelemetryFileSelector : System.Windows.Controls.UserControl
{
    private readonly DuckDBTelemetryReader _reader;
    private List<TelemetryFileInfo> _recordings;
    private string? _lastFolderPath;
    
    public event EventHandler<TelemetryFileInfo>? FileSelected;

    public TelemetryFileSelector()
    {
        InitializeComponent();
        _reader = new DuckDBTelemetryReader();
        _recordings = new List<TelemetryFileInfo>();
        
        // Try to load last used folder from settings
        _lastFolderPath = Properties.Settings.Default.LastTelemetryFolder;
        
        Loaded += TelemetryFileSelector_Loaded;
    }

    private void TelemetryFileSelector_Loaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastFolderPath) && Directory.Exists(_lastFolderPath))
        {
            _reader.SetCustomPath(_lastFolderPath);
            LoadRecordings();
        }
        else
        {
            PathInfoText.Text = "Click 'Select Folder' to choose your telemetry directory";
            PathInfoText.Foreground = System.Windows.Media.Brushes.LightGray;
        }
    }

    private void LoadRecordings()
    {
        try
        {
            var currentPath = _reader.GetTelemetryPath();
            
            if (string.IsNullOrEmpty(currentPath) || !_reader.TelemetryPathExists())
            {
                PathInfoText.Text = "Click 'Select Folder' to choose your telemetry directory";
                PathInfoText.Foreground = System.Windows.Media.Brushes.LightGray;
                FileListBox.ItemsSource = null;
                return;
            }

            PathInfoText.Text = $"📂 {currentPath}";
            PathInfoText.Foreground = System.Windows.Media.Brushes.LightGray;

            _recordings = _reader.GetAvailableRecordings();
            FileListBox.ItemsSource = _recordings;

            if (_recordings.Count == 0)
            {
                PathInfoText.Text += "\n\n⚠️ No .duckdb files found in this folder.";
                PathInfoText.Foreground = System.Windows.Media.Brushes.Orange;
            }
            else
            {
                PathInfoText.Text += $"\n\n✅ Found {_recordings.Count} recording(s)";
            }
        }
        catch (Exception ex)
        {
            PathInfoText.Text = $"❌ Error loading recordings: {ex.Message}";
            PathInfoText.Foreground = System.Windows.Media.Brushes.Red;
        }
    }

    private void FileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadButton.IsEnabled = FileListBox.SelectedItem != null;
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select Folder Containing .duckdb Telemetry Files",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            
            if (!string.IsNullOrEmpty(_lastFolderPath) && Directory.Exists(_lastFolderPath))
            {
                dialog.SelectedPath = _lastFolderPath;
            }

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                _lastFolderPath = dialog.SelectedPath;
                _reader.SetCustomPath(_lastFolderPath);
                
                try
                {
                    // Save to settings
                    Properties.Settings.Default.LastTelemetryFolder = _lastFolderPath;
                    Properties.Settings.Default.Save();
                }
                catch (Exception saveEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save settings: {saveEx.Message}");
                }
                
                LoadRecordings();
            }
        }
        catch (Exception ex)
        {
            PathInfoText.Text = $"❌ Error selecting folder: {ex.Message}";
            PathInfoText.Foreground = System.Windows.Media.Brushes.Red;
            MessageBox.Show($"Error selecting folder:\n{ex.Message}\n\nType: {ex.GetType().Name}", 
                           "Folder Selection Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileListBox.SelectedItem is TelemetryFileInfo selectedFile)
        {
            FileSelected?.Invoke(this, selectedFile);
        }
    }

    public TelemetryFileInfo? GetSelectedFile()
    {
        return FileListBox.SelectedItem as TelemetryFileInfo;
    }
}
