using System;
using System.Collections.Generic;
using LMU_Telemetry.Models;

namespace LMU_Telemetry.Services;

/// <summary>
/// Records raw lap position data from live telemetry for track-map generation.
/// Wire <see cref="FeedFrame"/> into the TelemetryService's NewFrameReceived
/// event.  <see cref="LapRecorded"/> fires when a complete lap is finalised
/// and saved to disk.
/// </summary>
public class DevLapRecorder
{
    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------

    private bool _isRecording = false;
    private int _lastLap = -1;
    private List<RawLapSample> _currentSamples = new();
    private string _currentTrackKey = string.Empty;
    private int _currentLapNumber = 0;
    private DateTime _lapStartTime = DateTime.MinValue;

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>True while the recorder is actively capturing frames.</summary>
    public bool IsRecording => _isRecording;

    /// <summary>
    /// Fires on the calling thread (usually a background timer thread) when a
    /// complete lap has been validated and saved to disk.
    /// </summary>
    public event EventHandler<RawLapData>? LapRecorded;

    /// <summary>Begin capturing frames.</summary>
    public void StartRecording()
    {
        _isRecording = true;
        _lastLap = -1;
        _currentSamples.Clear();
        System.Diagnostics.Debug.WriteLine("[DevLapRecorder] Recording started.");
    }

    /// <summary>Stop capturing frames.  Discards any in-progress lap.</summary>
    public void StopRecording()
    {
        _isRecording = false;
        _currentSamples.Clear();
        System.Diagnostics.Debug.WriteLine("[DevLapRecorder] Recording stopped (in-progress lap discarded).");
    }

    /// <summary>
    /// Feed one telemetry frame.  Call this from every NewFrameReceived event.
    /// Thread-safe (no UI thread required) but not re-entrant.
    /// </summary>
    public void FeedFrame(Models.TelemetryFrame frame, string trackKey)
    {
        if (!_isRecording) return;

        int lapNow = frame.CurrentLap;

        // ---- Track key change: discard everything and start fresh ----
        if (trackKey != _currentTrackKey && !string.IsNullOrEmpty(trackKey))
        {
            _currentTrackKey = trackKey;
            _currentSamples.Clear();
            _lastLap = lapNow;
            _lapStartTime = DateTime.UtcNow;
        }

        // ---- Lap boundary: finalise previous lap ----
        if (_lastLap >= 0 && lapNow > _lastLap && lapNow == _lastLap + 1)
        {
            // Finalise the samples we've been building
            FinaliseCurrentLap(lapTime: frame.LastLapTime);

            // Start fresh for the new lap
            _currentSamples.Clear();
            _currentLapNumber = lapNow;
            _lapStartTime = DateTime.UtcNow;
        }
        else if (_lastLap == -1)
        {
            // First frame ever
            _currentLapNumber = lapNow;
            _lapStartTime = DateTime.UtcNow;
        }

        _lastLap = lapNow;

        // ---- Accumulate sample ----
        _currentSamples.Add(new RawLapSample
        {
            X           = frame.PosX,
            Y           = frame.PosY,
            LapDistance = frame.LapDistance,
            Time        = frame.Time,
            Speed       = frame.Speed,
        });
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private void FinaliseCurrentLap(float lapTime)
    {
        if (_currentSamples.Count == 0 || string.IsNullOrEmpty(_currentTrackKey)) return;

        var lap = new RawLapData
        {
            TrackKey    = _currentTrackKey,
            LapNumber   = _currentLapNumber,
            RecordedAt  = _lapStartTime.ToLocalTime(),
            LapTime     = lapTime > 0 ? lapTime : -1,
            Samples     = new List<RawLapSample>(_currentSamples),
        };

        lap.Validate();

        try
        {
            RawLapStorage.Save(lap);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DevLapRecorder] Failed to save lap: {ex.Message}");
        }

        System.Diagnostics.Debug.WriteLine(
            $"[DevLapRecorder] Lap {lap.LapNumber} finalised: {lap.SampleCount} samples, " +
            $"issue={lap.ValidationIssue}, kept={lap.IsKept}");

        LapRecorded?.Invoke(this, lap);
    }
}
