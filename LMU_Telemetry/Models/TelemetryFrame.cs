using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LMU_Telemetry.Models
{
    public sealed class TelemetryFrame
    {
        public double Time { get; init; }          // Session time (s)
        public float PosX { get; init; }            // World or track space
        public float PosY { get; init; }

        public float Speed { get; init; }           // km/h
        public float Throttle { get; init; }        // 0–1
        public float Brake { get; init; }           // 0–1
        public float Steering { get; init; }        // -1–1

        public int Gear { get; init; }
        public float Rpm { get; init; }
        
        // Lap/Sector data
        public int CurrentLap { get; init; }        // Current lap number
        public int Sector { get; init; }            // Current sector (0, 1, 2, or 3)
        public float LapDistance { get; init; }     // Distance around current lap (meters)
        public float LapTime { get; init; }         // Current lap time (seconds)

        // Extended telemetry values (channels not mapped to fixed properties)
        public Dictionary<string, object?> ExtendedData { get; init; } = new();
    }
}
