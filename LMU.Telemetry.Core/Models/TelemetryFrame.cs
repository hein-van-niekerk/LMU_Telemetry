using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LMU.Telemetry.Core.Models
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
        public float Clutch { get; init; }          // 0–1 (unfiltered clutch input)
        public float RpmMax { get; init; }          // Engine max RPM (rev limit)

        // Steering wheel rotation range (degrees, lock-to-lock) straight from
        // the sim — used to render the on-screen wheel at the exact real angle.
        public float SteeringWheelRangeVisual { get; init; }    // what the in-game wheel uses
        public float SteeringWheelRangePhysical { get; init; }  // physical steering lock

        // Powertrain / fluids (real values, 0 when not reported by the car)
        public float EngineWaterTemp { get; init; } // °C
        public float EngineOilTemp { get; init; }   // °C
        public float Fuel { get; init; }            // litres remaining

        // Lap/Sector data
        public int CurrentLap { get; init; }        // Current lap number
        public int Sector { get; init; }            // Current sector (0, 1, 2, or 3)
        public float LapDistance { get; init; }     // Distance around current lap (meters)
        public float LapTime { get; init; }         // Current lap time (seconds)
        public float LastLapTime { get; init; }     // Previous completed lap time (s, -1 if none)
        public float BestLapTime { get; init; }     // Best lap time so far (s, -1 if none)

        // Extended telemetry values (channels not mapped to fixed properties)
        public Dictionary<string, object?> ExtendedData { get; init; } = new();
    }
}
