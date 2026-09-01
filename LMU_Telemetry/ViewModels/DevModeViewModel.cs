using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using LMU_Telemetry.Models;

namespace LMU_Telemetry.ViewModels;

// ---------------------------------------------------------------------------
// Lap list item wrapper (RECORD tab)
// ---------------------------------------------------------------------------

/// <summary>Wraps a <see cref="RawLapData"/> for display in the lap list.</summary>
public class LapListItem : ObservableObject
{
    private bool _isKept;

    public RawLapData Lap { get; }

    public bool IsKept
    {
        get => _isKept;
        set
        {
            if (SetProperty(ref _isKept, value))
                Lap.IsKept = value;
        }
    }

    /// <summary>Human-readable one-liner for the list.</summary>
    public string DisplayText
    {
        get
        {
            string lapTime = Lap.LapTime > 0
                ? TimeSpan.FromSeconds(Lap.LapTime).ToString(@"m\:ss\.fff")
                : "—";
            string issue = Lap.ValidationIssue == LapValidationIssue.None
                ? string.Empty
                : $"  ⚠ {Lap.ValidationIssue}";
            return $"Lap {Lap.LapNumber}  {lapTime}  ({Lap.SampleCount} pts){issue}";
        }
    }

    public LapListItem(RawLapData lap)
    {
        Lap = lap;
        _isKept = lap.IsKept;
    }
}

// ---------------------------------------------------------------------------
// Main ViewModel
// ---------------------------------------------------------------------------

/// <summary>
/// ViewModel for the DevModeWindow.  Handles recording state, lap list,
/// candidate generation and the track-map library.
/// </summary>
public class DevModeViewModel : ObservableObject
{
    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------

    private string _currentTrackKey = string.Empty;
    private bool _isRecording;
    private string _statusMessage = "Ready.";
    private GeneratedTrackMap? _candidateMap;
    private TrackMapLibraryEntry? _selectedLibraryEntry;
    private LapListItem? _selectedLap;

    // -----------------------------------------------------------------------
    // Observable collections
    // -----------------------------------------------------------------------

    public ObservableCollection<LapListItem> Laps { get; } = new();
    public ObservableCollection<TrackMapLibraryEntry> LibraryEntries { get; } = new();

    // -----------------------------------------------------------------------
    // Bindable properties
    // -----------------------------------------------------------------------

    public string CurrentTrackKey
    {
        get => _currentTrackKey;
        set => SetProperty(ref _currentTrackKey, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        set => SetProperty(ref _isRecording, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>The candidate map built by GenerateCandidate, not yet saved.</summary>
    public GeneratedTrackMap? CandidateMap
    {
        get => _candidateMap;
        set
        {
            SetProperty(ref _candidateMap, value);
            OnPropertyChanged(nameof(HasCandidate));
        }
    }

    public bool HasCandidate => _candidateMap != null;

    public TrackMapLibraryEntry? SelectedLibraryEntry
    {
        get => _selectedLibraryEntry;
        set => SetProperty(ref _selectedLibraryEntry, value);
    }

    public LapListItem? SelectedLap
    {
        get => _selectedLap;
        set => SetProperty(ref _selectedLap, value);
    }

    // -----------------------------------------------------------------------
    // Track key / lap management
    // -----------------------------------------------------------------------

    /// <summary>
    /// Call this when the main window detects a new track key so the lap list
    /// is refreshed for the correct circuit.
    /// </summary>
    public void SetTrackKey(string trackKey)
    {
        if (trackKey == _currentTrackKey) return;
        CurrentTrackKey = trackKey;
        RefreshLapList();
    }

    /// <summary>
    /// Called by DevLapRecorder.LapRecorded — adds the new lap to the list on
    /// the UI thread.
    /// </summary>
    public void OnLapRecorded(RawLapData lap)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Laps.Insert(0, new LapListItem(lap));
            StatusMessage = $"Lap {lap.LapNumber} recorded ({lap.SampleCount} pts). Issue: {lap.ValidationIssue}.";
        });
    }

    /// <summary>Reload the lap list from disk for the current track key.</summary>
    public void RefreshLapList()
    {
        Laps.Clear();
        if (string.IsNullOrEmpty(_currentTrackKey)) return;

        var saved = RawLapStorage.LoadAll(_currentTrackKey);
        foreach (var lap in saved)
            Laps.Add(new LapListItem(lap));

        StatusMessage = $"Loaded {Laps.Count} lap(s) for "{_currentTrackKey}".";
    }

    /// <summary>Persist the IsKept flag for a single lap back to disk.</summary>
    public void SaveLapKeepFlag(LapListItem item)
    {
        try
        {
            // Re-save the whole lap (small JSON, fast enough)
            RawLapStorage.Save(item.Lap);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving lap flag: {ex.Message}";
        }
    }

    /// <summary>Delete the selected lap from disk and the list.</summary>
    public void DeleteSelectedLap()
    {
        if (_selectedLap == null) return;
        try
        {
            RawLapStorage.Delete(_selectedLap.Lap);
            Laps.Remove(_selectedLap);
            SelectedLap = null;
            StatusMessage = "Lap deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error deleting lap: {ex.Message}";
        }
    }

    // -----------------------------------------------------------------------
    // Map generation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Generate a candidate map from all kept laps for the current track key.
    /// Returns an error message, or null on success.
    /// </summary>
    public string? GenerateCandidate()
    {
        if (string.IsNullOrEmpty(_currentTrackKey))
            return "No track key — start a session in LMU first.";

        var kept = Laps
            .Where(item => item.IsKept)
            .Select(item => item.Lap)
            .ToList();

        if (kept.Count == 0)
            return "No kept laps to generate from.";

        try
        {
            StatusMessage = "Generating…";
            var map = TrackMapGenerator.GenerateFromRawLaps(kept, _currentTrackKey);
            CandidateMap = map;
            StatusMessage = $"Candidate ready: {map.Points.Count} pts, {map.TotalLength:F0} m, from {map.GeneratedFromLapCount} lap(s).";
            return null; // success
        }
        catch (Exception ex)
        {
            StatusMessage = $"Generation failed: {ex.Message}";
            return ex.Message;
        }
    }

    /// <summary>
    /// Commit the candidate map to the permanent library, replacing any
    /// existing map for the current track key.
    /// </summary>
    public string? SaveCandidate()
    {
        if (_candidateMap == null) return "No candidate to save.";

        try
        {
            TrackMapStorage.Save(_candidateMap, _currentTrackKey);
            CandidateMap = null;
            RefreshLibrary();
            StatusMessage = $"Map saved for "{_currentTrackKey}".";
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            return ex.Message;
        }
    }

    /// <summary>Discard the candidate without saving.</summary>
    public void DiscardCandidate()
    {
        CandidateMap = null;
        StatusMessage = "Candidate discarded.";
    }

    // -----------------------------------------------------------------------
    // Library management
    // -----------------------------------------------------------------------

    public void RefreshLibrary()
    {
        LibraryEntries.Clear();
        foreach (var entry in TrackMapStorage.GetLibraryEntries())
            LibraryEntries.Add(entry);
    }

    /// <summary>
    /// Delete the selected library entry's map file.
    /// Returns an error message, or null on success.
    /// </summary>
    public string? DeleteSelectedLibraryEntry()
    {
        if (_selectedLibraryEntry == null) return "Nothing selected.";

        try
        {
            TrackMapStorage.Delete(_selectedLibraryEntry.TrackKey);
            LibraryEntries.Remove(_selectedLibraryEntry);
            SelectedLibraryEntry = null;
            StatusMessage = "Library entry deleted.";
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete failed: {ex.Message}";
            return ex.Message;
        }
    }

    /// <summary>
    /// Import an external JSON map file into the library under
    /// <paramref name="trackKey"/>.
    /// </summary>
    public string? ImportMap(string filePath, string trackKey)
    {
        if (string.IsNullOrWhiteSpace(filePath))  return "No file selected.";
        if (string.IsNullOrWhiteSpace(trackKey))  return "Track key is required.";

        try
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                Converters = { new PointJsonConverter() }
            };
            string json = System.IO.File.ReadAllText(filePath);
            var map = System.Text.Json.JsonSerializer.Deserialize<GeneratedTrackMap>(json, options);
            if (map == null) return "Could not deserialise the file.";

            map.Source    = TrackMapSource.Imported;
            map.TrackName = trackKey;
            map.GeneratedDateTime = System.IO.File.GetLastWriteTime(filePath);

            TrackMapStorage.Save(map, trackKey);
            RefreshLibrary();
            StatusMessage = $"Imported "{trackKey}" from file.";
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
            return ex.Message;
        }
    }
}
