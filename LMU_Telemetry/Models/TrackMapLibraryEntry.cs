using System;

namespace LMU_Telemetry.Models;

/// <summary>How the map was produced.</summary>
public enum TrackMapSource
{
    /// <summary>Imported from an external file (e.g. OSM-derived JSON).</summary>
    Imported = 0,
    /// <summary>Generated from raw lap telemetry recorded inside the app.</summary>
    Generated = 1,
}

/// <summary>
/// Lightweight summary of one entry in the track-map library, shown in the
/// Dev Mode "LIBRARY" tab without loading the full GeneratedTrackMap.
/// </summary>
public class TrackMapLibraryEntry
{
    /// <summary>Track + layout key (matches file name without .json).</summary>
    public string TrackKey { get; set; } = string.Empty;

    /// <summary>Full path to the .json file on disk.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>How this map was created.</summary>
    public TrackMapSource Source { get; set; } = TrackMapSource.Imported;

    /// <summary>Number of centerline points.</summary>
    public int PointCount { get; set; }

    /// <summary>Total centerline length in meters.</summary>
    public double TotalLength { get; set; }

    /// <summary>Number of recorded laps used to generate this map (0 for imported maps).</summary>
    public int GeneratedFromLapCount { get; set; }

    /// <summary>When the map was generated or last modified.</summary>
    public DateTime GeneratedDateTime { get; set; }

    /// <summary>Human-readable summary for display.</summary>
    public string Summary =>
        Source == TrackMapSource.Generated
            ? $"{PointCount} pts · {TotalLength:F0} m · from {GeneratedFromLapCount} lap(s) · {GeneratedDateTime:yyyy-MM-dd HH:mm}"
            : $"{PointCount} pts · {TotalLength:F0} m · imported · {GeneratedDateTime:yyyy-MM-dd HH:mm}";
}
