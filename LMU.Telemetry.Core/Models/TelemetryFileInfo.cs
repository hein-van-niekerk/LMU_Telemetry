using System;

namespace LMU.Telemetry.Core.Models;

public class TelemetryFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime RecordingDate { get; set; }
    public string TrackName { get; set; } = "Unknown Track";
    public string CarName { get; set; } = "Unknown Car";
    public int LapCount { get; set; }
    public TimeSpan Duration { get; set; }
    public long FileSize { get; set; }
    
    public string DisplayName => 
        $"{RecordingDate:yyyy-MM-dd HH:mm} - {TrackName} - {CarName} ({LapCount} laps)";
    
    public string FileSizeFormatted => 
        FileSize > 1024 * 1024 
            ? $"{FileSize / (1024.0 * 1024.0):F1} MB" 
            : $"{FileSize / 1024.0:F1} KB";
}
