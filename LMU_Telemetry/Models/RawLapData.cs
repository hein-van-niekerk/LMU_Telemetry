using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LMU_Telemetry.Models;

/// <summary>One position sample recorded during a lap.</summary>
public class RawLapSample
{
    public float X { get; set; }
    public float Y { get; set; }
    public float LapDistance { get; set; }  // meters along lap, 0 if unavailable
    public double Time { get; set; }         // session elapsed time (s)
    public float Speed { get; set; }         // km/h
}

/// <summary>Detected data-quality issues in a raw recorded lap.</summary>
public enum LapValidationIssue
{
    None = 0,
    TooFewSamples,           // fewer than 50 samples captured
    PositionDiscontinuity,   // single-frame position jump above threshold
    ImplausibleSpeed,        // speed change > 300 km/h between consecutive frames
}

/// <summary>
/// A single recorded lap stored on disk for track-map generation.
/// Includes all raw position samples plus metadata and dev's keep/discard flag.
/// Kept permanently so maps can be regenerated with improved algorithms later.
/// </summary>
public class RawLapData
{
    public string TrackKey { get; set; } = string.Empty;  // track+layout identifier from LMU
    public int LapNumber { get; set; }
    public DateTime RecordedAt { get; set; }
    public double LapTime { get; set; } = -1;              // seconds; -1 = unknown
    public bool IsKept { get; set; } = true;               // dev's keep/discard flag
    public LapValidationIssue ValidationIssue { get; set; } = LapValidationIssue.None;
    public string ValidationNote { get; set; } = string.Empty;
    public List<RawLapSample> Samples { get; set; } = new();

    [JsonIgnore] public int SampleCount => Samples.Count;

    /// <summary>
    /// Set when loaded from disk. Not serialized — derived from the file path.
    /// </summary>
    [JsonIgnore] public string? FileName { get; set; }

    /// <summary>
    /// Validate data quality and set ValidationIssue/ValidationNote.
    /// Called automatically after a lap is finalized by DevLapRecorder.
    /// </summary>
    public void Validate()
    {
        if (Samples.Count < 50)
        {
            ValidationIssue = LapValidationIssue.TooFewSamples;
            ValidationNote = $"Only {Samples.Count} samples (need ≥ 50)";
            IsKept = false;
            return;
        }

        // Check for large position discontinuities.
        // 80 m per sample is implausible even at 300 km/h with a 60 Hz feed.
        const float maxJumpMeters = 80f;
        for (int i = 1; i < Samples.Count; i++)
        {
            float dx = Samples[i].X - Samples[i - 1].X;
            float dy = Samples[i].Y - Samples[i - 1].Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > maxJumpMeters)
            {
                ValidationIssue = LapValidationIssue.PositionDiscontinuity;
                ValidationNote = $"Position jump of {dist:F0} m at sample {i}";
                // Flag but don't auto-discard — let the dev decide
                return;
            }
        }

        // Check for implausible speed jumps
        for (int i = 1; i < Samples.Count; i++)
        {
            float dSpeed = MathF.Abs(Samples[i].Speed - Samples[i - 1].Speed);
            if (dSpeed > 300f)
            {
                ValidationIssue = LapValidationIssue.ImplausibleSpeed;
                ValidationNote = $"Speed jump of {dSpeed:F0} km/h at sample {i}";
                return;
            }
        }

        ValidationIssue = LapValidationIssue.None;
        ValidationNote = string.Empty;
    }
}
