using System;
using System.Collections.Generic;

namespace LMU_Telemetry.Models
{
    // FR-17: Session and lap handling
    public class SessionState
    {
        public SessionType Type { get; set; }
        public double SessionTime { get; set; }
        public double RemainingTime { get; set; }
        public int MaxLaps { get; set; }
        public string TrackName { get; set; } = string.Empty;
        public List<LapInfo> Laps { get; set; } = new();
        public int CurrentLapNumber { get; set; }
    }

    public enum SessionType
    {
        Unknown,
        Practice,
        Qualifying,
        Race,
        TestDay
    }

    // FR-16: Lap boundary detection
    public class LapInfo
    {
        public int LapNumber { get; set; }
        public double LapTime { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public bool IsValid { get; set; }
        public bool IsBestLap { get; set; }
        public List<TelemetryFrame> Frames { get; set; } = new();
    }
}
