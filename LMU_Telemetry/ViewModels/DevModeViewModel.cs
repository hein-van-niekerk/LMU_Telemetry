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
    private string? _lastGenerationWarning;
    private AlignmentResult? _lastAlignment;

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
        set
        {
            if (SetProperty(ref _currentTrackKey, value))
                OnPropertyChanged(nameof(HasExistingMapForCurrentTrack));
        }
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

    /// <summary>
    /// Non-blocking quality warning from the last GenerateCandidate() call
    /// (e.g. too few laps, or laps too similar in line). Null when the
    /// candidate looks healthy.
    /// </summary>
    public string? LastGenerationWarning
    {
        get => _lastGenerationWarning;
        set => SetProperty(ref _lastGenerationWarning, value);
    }

    /// <summary>
    /// Result of auto-aligning the current candidate against an existing map
    /// for this track key, if one exists. Null when there's no existing map
    /// to align against, or no candidate yet.
    /// </summary>
    public AlignmentResult? LastAlignment
    {
        get => _lastAlignment;
        set => SetProperty(ref _lastAlignment, value);
    }

    /// <summary>True when a map is already on file for the current track key (offers the Merge path).</summary>
    public bool HasExistingMapForCurrentTrack =>
        !string.IsNullOrEmpty(_currentTrackKey) && TrackMapStorage.Exists(_currentTrackKey);

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
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
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

        StatusMessage = $"Loaded {Laps.Count} lap(s) for \"{_currentTrackKey}\".";
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

            // --- Non-blocking quality warnings (count and line-variance) ---
            var warnings = new List<string>();
            if (kept.Count < 5)
                warnings.Add($"Only {kept.Count} lap(s) used — 5+ with varied racing lines is recommended for a good envelope.");

            double avgWidth = map.Points.Count > 0 ? map.Points.Average(p => p.Width) : 0;
            if (avgWidth > 0 && avgWidth < 3.0)
                warnings.Add($"Average detected width is only {avgWidth:F1} m — kept laps may be too similar in line; try varying inside/outside/kerbs.");

            LastGenerationWarning = warnings.Count > 0 ? string.Join("\n", warnings) : null;

            // --- Auto-align against an existing map for this track, if one exists ---
            var existingMap = TrackMapStorage.Load(_currentTrackKey);
            LastAlignment = (existingMap != null && existingMap.Points.Count > 1)
                ? TrackMapAligner.Align(map.GetPositions(), existingMap.GetPositions())
                : null;

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
            LastAlignment = null;
            LastGenerationWarning = null;
            RefreshLibrary();
            OnPropertyChanged(nameof(HasExistingMapForCurrentTrack));
            StatusMessage = $"Map saved for \"{_currentTrackKey}\".";
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
        LastAlignment = null;
        LastGenerationWarning = null;
        StatusMessage = "Candidate discarded.";
    }

    /// <summary>
    /// Commit the candidate by aligning it onto the existing map for this
    /// track key and merging. Default behaviour keeps the existing map's
    /// centerline (presumed already visually verified) and attaches the
    /// newly-derived width/kerb data; <paramref name="useNewCenterline"/>
    /// overrides that to use the (ICP-aligned) candidate's own centerline
    /// instead. Requires GenerateCandidate() to have already populated
    /// LastAlignment (i.e. an existing map was present at generation time).
    /// </summary>
    public string? MergeCandidateWithExisting(bool useNewCenterline)
    {
        if (_candidateMap == null) return "No candidate to merge.";
        if (string.IsNullOrEmpty(_currentTrackKey)) return "No track key set.";

        var existingMap = TrackMapStorage.Load(_currentTrackKey);
        if (existingMap == null || existingMap.Points.Count < 2)
            return "No existing map for this track — use Save instead.";

        try
        {
            var alignment = _lastAlignment ?? TrackMapAligner.Align(_candidateMap.GetPositions(), existingMap.GetPositions());
            LastAlignment = alignment;

            GeneratedTrackMap merged = useNewCenterline
                ? BuildMergedMap_UseCandidateCenterline(_candidateMap, existingMap, alignment)
                : BuildMergedMap_KeepExistingCenterline(_candidateMap, existingMap, alignment);

            TrackMapStorage.Save(merged, _currentTrackKey);
            CandidateMap = null;
            LastAlignment = null;
            LastGenerationWarning = null;
            RefreshLibrary();
            OnPropertyChanged(nameof(HasExistingMapForCurrentTrack));

            string divergenceNote = alignment.HighDivergenceSegments.Count > 0
                ? $", {alignment.HighDivergenceSegments.Count} flagged stretch(es)"
                : "";
            StatusMessage = $"Merged with existing map for \"{_currentTrackKey}\" " +
                $"(avg divergence {alignment.AverageDivergenceMeters:F2} m, max {alignment.MaxDivergenceMeters:F2} m{divergenceNote}).";
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Merge failed: {ex.Message}";
            return ex.Message;
        }
    }

    /// <summary>Default merge: existing centerline stays, candidate's aligned width/edges attach via nearest-point mapping.</summary>
    private static GeneratedTrackMap BuildMergedMap_KeepExistingCenterline(
        GeneratedTrackMap candidate, GeneratedTrackMap existing, AlignmentResult alignment)
    {
        var points = new List<TrackPoint>(existing.Points.Count);
        foreach (var existingPt in existing.Points)
        {
            int nearest = NearestIndex(alignment.AlignedCandidate, existingPt.Position);
            var src = (nearest >= 0 && nearest < candidate.Points.Count) ? candidate.Points[nearest] : null;

            points.Add(new TrackPoint
            {
                Position  = existingPt.Position,
                Heading   = existingPt.Heading,
                Curvature = existingPt.Curvature,
                Width     = src?.Width ?? 0,
                LeftEdge  = src?.LeftEdge ?? 0,
                RightEdge = src?.RightEdge ?? 0,
            });
        }

        return new GeneratedTrackMap
        {
            Points = points,
            Corners = existing.Corners,
            GeneratedFromLapCount = candidate.GeneratedFromLapCount,
            TotalLength = existing.TotalLength,
            GeneratedDateTime = DateTime.Now,
            TrackName = existing.TrackName,
            Source = TrackMapSource.Merged,
            LayoutKey = existing.LayoutKey,
            RawLapManifest = candidate.RawLapManifest,
        };
    }

    /// <summary>Override merge: use the candidate's own (ICP-aligned) centerline and width/edges as-is.</summary>
    private static GeneratedTrackMap BuildMergedMap_UseCandidateCenterline(
        GeneratedTrackMap candidate, GeneratedTrackMap existing, AlignmentResult alignment)
    {
        var points = new List<TrackPoint>(candidate.Points.Count);
        for (int i = 0; i < candidate.Points.Count; i++)
        {
            var src = candidate.Points[i];
            points.Add(new TrackPoint
            {
                Position  = i < alignment.AlignedCandidate.Count ? alignment.AlignedCandidate[i] : src.Position,
                Heading   = src.Heading,
                Curvature = src.Curvature,
                Width     = src.Width,
                LeftEdge  = src.LeftEdge,
                RightEdge = src.RightEdge,
            });
        }

        return new GeneratedTrackMap
        {
            Points = points,
            Corners = existing.Corners,
            GeneratedFromLapCount = candidate.GeneratedFromLapCount,
            TotalLength = candidate.TotalLength,
            GeneratedDateTime = DateTime.Now,
            TrackName = existing.TrackName,
            Source = TrackMapSource.Merged,
            LayoutKey = existing.LayoutKey,
            RawLapManifest = candidate.RawLapManifest,
        };
    }

    private static int NearestIndex(List<System.Windows.Point> points, System.Windows.Point target)
    {
        int best = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            double dx = points[i].X - target.X, dy = points[i].Y - target.Y;
            double d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
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
            StatusMessage = $"Imported \"{trackKey}\" from file.";
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
            return ex.Message;
        }
    }
}
