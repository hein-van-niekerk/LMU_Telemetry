using System;

namespace LMU.Telemetry.Core.Models
{
    // FR-3: Additional car state information
    public class CarState
    {
        public string VehicleName { get; set; } = string.Empty;
        public float Fuel { get; set; }
        public float EngineWaterTemp { get; set; }
        public float EngineOilTemp { get; set; }
        public float FrontRideHeight { get; set; }
        public float RearRideHeight { get; set; }
        public bool InPits { get; set; }
        public bool OnTrack { get; set; }
        public DamageState Damage { get; set; } = new();
    }

    public class DamageState
    {
        public float FrontAero { get; set; }
        public float RearAero { get; set; }
        public bool HasDamage => FrontAero > 0 || RearAero > 0;
    }
}
