using System;
using System.Collections.Generic;
using LMU_Telemetry.Models;

namespace LMU_Telemetry
{
    public class FakeTelemetryGenerator
    {
        private double _time;
        private double _trackProgress; // 0..1 around the lap
        private int _lap = 0;
        private double _lapStartTime = 0;
        private readonly Random _rng = new(42);

        // Simulated "engine state" that evolves slowly
        private double _oilTemp  = 80.0;
        private double _waterTemp = 75.0;
        private double _fuel     = 45.0;

        // Tyre pressures that slowly drift
        private readonly double[] _tyrePressBase = { 180.0, 180.5, 179.8, 180.2 }; // kPa
        private readonly double[] _tyrePressOffset = new double[4];

        public TelemetryFrame Next()
        {
            _time += 0.016; // 60 Hz
            _trackProgress += 0.0014; // ~11.4 s lap
            _fuel -= 0.000004; // fuel consumption

            if (_trackProgress > 1.0)
            {
                _trackProgress -= 1.0;
                _lap++;
                _lapStartTime = _time;
            }

            double angle = _trackProgress * Math.PI * 2;

            // Position — figure-8
            float posX = (float)(Math.Sin(angle) * 200f);
            float posY = (float)(Math.Sin(angle * 2) * 100f);

            // Dynamic driving inputs
            double cornerFactor = Math.Abs(Math.Cos(angle * 3));
            float speed   = (float)(80 + cornerFactor * 190);          // 80–270 km/h
            float throttle = (float)Math.Clamp(cornerFactor > 0.5 ? cornerFactor : cornerFactor * 0.4, 0, 1);
            float brake    = (float)Math.Clamp(cornerFactor < 0.3 ? (1 - cornerFactor / 0.3) * 0.9 : 0, 0, 1);
            float steering = (float)(Math.Sin(angle * 2) * 0.55);
            float clutch   = brake > 0.05f ? 0f : 0f; // always released in motion

            int gear = speed switch { < 70 => 1, < 110 => 2, < 150 => 3, < 190 => 4, < 230 => 5, _ => 6 };
            float rpm    = 2000f + (speed / 270f) * 7000f;
            float rpmMax = 9200f;

            // Slow thermal drift
            _oilTemp   = Math.Clamp(_oilTemp   + (_rng.NextDouble() - 0.49) * 0.05 + (speed / 270.0) * 0.01, 95, 115);
            _waterTemp = Math.Clamp(_waterTemp + (_rng.NextDouble() - 0.49) * 0.04 + (speed / 270.0) * 0.01, 80, 98);

            // Lap timing
            double lapTime = _time - _lapStartTime;
            int sector = lapTime switch { < 3.8 => 0, < 7.6 => 1, _ => 2 };
            double sect1Time = sector >= 1 ? 3.72 + _rng.NextDouble() * 0.1 : 0;
            double sect2Time = sector >= 2 ? 3.78 + _rng.NextDouble() * 0.1 : 0;
            float lapDist = (float)(_trackProgress * 1800);  // ~1800 m lap

            // G-forces derived from position & steering
            double latG  = -steering * (speed / 270.0) * 3.8; // up to ~3.8G lat
            double longG = throttle > 0.1 ? throttle * 0.8 : -brake * 4.2;
            double vertG = 1.0 + Math.Sin(angle * 5) * 0.08;  // ride bumps

            // Tyre temperatures: base + speed heat + brake heat per corner
            double brakeHeat = brake * 180.0;
            double speedHeat = (speed / 270.0) * 40.0;
            double[] tyreTemps =
            {
                75 + speedHeat + (brake > 0.1 ? brakeHeat * 0.6 : 0) + _rng.NextDouble() * 3,  // FL
                75 + speedHeat + (brake > 0.1 ? brakeHeat * 0.6 : 0) + _rng.NextDouble() * 3,  // FR
                75 + speedHeat + _rng.NextDouble() * 2,  // RL
                75 + speedHeat + _rng.NextDouble() * 2,  // RR
            };
            // Tyre pressures drift slowly
            for (int i = 0; i < 4; i++)
                _tyrePressOffset[i] = Math.Clamp(_tyrePressOffset[i] + (_rng.NextDouble() - 0.5) * 0.02, -2, 2);
            double[] tyrePressures =
            {
                _tyrePressBase[0] + _tyrePressOffset[0] + (tyreTemps[0] - 75) * 0.4,
                _tyrePressBase[1] + _tyrePressOffset[1] + (tyreTemps[1] - 75) * 0.4,
                _tyrePressBase[2] + _tyrePressOffset[2] + (tyreTemps[2] - 75) * 0.4,
                _tyrePressBase[3] + _tyrePressOffset[3] + (tyreTemps[3] - 75) * 0.4,
            };

            // Brake temps
            double[] brakeTemps =
            {
                150 + brake * 450 + _rng.NextDouble() * 20,
                150 + brake * 450 + _rng.NextDouble() * 20,
                120 + brake * 300 + _rng.NextDouble() * 15,
                120 + brake * 300 + _rng.NextDouble() * 15,
            };

            var ext = new Dictionary<string, object?>
            {
                // Speed channels
                ["Ground Speed"]               = (double)speed,
                ["GPS Speed"]                  = (double)(speed + _rng.NextDouble() * 1.2 - 0.6),

                // Acceleration
                ["Longitudinal Acceleration"]  = longG,
                ["Lateral Acceleration"]       = latG,
                ["G Force Lat"]                = latG,
                ["G Force Long"]               = longG,
                ["G Force Vert"]               = vertG,

                // Yaw & slip
                ["Yaw Rate"]                   = latG * 18.0 + (_rng.NextDouble() - 0.5) * 2.0,
                ["TCSlipAngle"]                = latG * 4.0 + (_rng.NextDouble() - 0.5) * 0.5,
                ["TC"]                         = throttle > 0.85 && speed < 130 ? 1.0 : 0.0,
                ["TCLevel"]                    = 3.0,
                ["ABS"]                        = brake > 0.7 ? 1.0 : 0.0,
                ["LaunchControlActive"]        = 0.0,
                ["Speed Limiter"]              = 0.0,
                ["In Pits"]                    = 0.0,
                ["Current Sector1"]            = sect1Time,
                ["Current Sector2"]            = sect2Time,

                // Tyre temps (array)
                ["TyresTempCentre"]            = tyreTemps,
                ["TyresTempInner"]             = new double[] { tyreTemps[0] + 4, tyreTemps[1] + 4, tyreTemps[2] + 3, tyreTemps[3] + 3 },
                ["TyresTempOuter"]             = new double[] { tyreTemps[0] - 4, tyreTemps[1] - 4, tyreTemps[2] - 3, tyreTemps[3] - 3 },
                ["TyresPressure"]              = tyrePressures,

                // Wear (0–1, very slow)
                ["TyresWear"]                  = new double[] { 0.97, 0.97, 0.98, 0.98 },

                // Brake temps
                ["Brake Temp FL"]              = brakeTemps[0],
                ["Brake Temp FR"]              = brakeTemps[1],
                ["Brake Temp RL"]              = brakeTemps[2],
                ["Brake Temp RR"]              = brakeTemps[3],
                ["Brake Air Temp FL"]          = 28.0 + speed * 0.05,
                ["Brake Air Temp FR"]          = 28.0 + speed * 0.05,
                ["Brake Disc Thickness FL"]    = 28.4 - _time * 0.0001,
                ["Brake Disc Thickness FR"]    = 28.4 - _time * 0.0001,

                // Aero / ride height (mm)
                ["Ride Height Front"]          = 42.0 + (speed / 270.0) * -8.0 + _rng.NextDouble() * 1.5,
                ["Ride Height Rear"]           = 68.0 + (speed / 270.0) * -12.0 + _rng.NextDouble() * 1.5,
                ["Front Downforce"]            = (speed * speed) * 0.0012,
                ["Rear Downforce"]             = (speed * speed) * 0.0018,
                ["Drag"]                       = (speed * speed) * 0.0005,

                // Environment
                ["Ambient Temperature"]        = 22.5,
                ["Track Temperature"]          = 38.0 + Math.Sin(angle) * 1.5,
                ["Wind Speed"]                 = 3.2 + _rng.NextDouble() * 1.0,
                ["Wind Heading"]               = 215.0,
                ["Humidity"]                   = 55.0,
                ["Rain Intensity"]             = 0.0,
            };

            return new TelemetryFrame
            {
                Time          = _time,
                PosX          = posX,
                PosY          = posY,
                Speed         = speed,
                Throttle      = throttle,
                Brake         = brake,
                Clutch        = clutch,
                Steering      = steering,
                Gear          = gear,
                Rpm           = rpm,
                RpmMax        = rpmMax,
                EngineOilTemp   = (float)_oilTemp,
                EngineWaterTemp = (float)_waterTemp,
                Fuel          = (float)Math.Max(0, _fuel),
                CurrentLap    = _lap,
                Sector        = sector,
                LapDistance   = lapDist,
                LapTime       = (float)lapTime,
                LastLapTime   = _lap > 0 ? 11.45f : 0f,
                BestLapTime   = _lap > 0 ? 11.30f : 0f,
                SteeringWheelRangeVisual   = 300f,
                SteeringWheelRangePhysical = 360f,
                ExtendedData  = ext,
            };
        }
    }
}
