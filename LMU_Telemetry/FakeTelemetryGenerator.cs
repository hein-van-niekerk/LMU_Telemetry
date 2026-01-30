using System;
using LMU_Telemetry.Models;

namespace LMU_Telemetry
{
    public class FakeTelemetryGenerator
    {
        private double _time;
        private double _trackProgress; // 0 to 1 around the track

        public TelemetryFrame Next()
        {
            _time += 0.016; // 60Hz
            _trackProgress += 0.002; // ~8 second lap
            
            if (_trackProgress > 1.0)
                _trackProgress = 0;

            // Create a more interesting track shape (figure-8 style)
            var angle = _trackProgress * Math.PI * 2;
            var radius = 200f;
            
            // Figure-8 parametric equations
            var posX = (float)(Math.Sin(angle) * radius);
            var posY = (float)(Math.Sin(angle * 2) * radius / 2);

            // Calculate speed based on "corner" complexity
            var cornerFactor = Math.Abs(Math.Cos(angle * 3));
            var speed = (float)(100 + cornerFactor * 150); // 100-250 km/h

            // Throttle and brake based on speed
            var throttle = cornerFactor > 0.5f ? 1.0f : (float)cornerFactor * 0.5f;
            var brake = cornerFactor < 0.3f ? (1.0f - (float)cornerFactor / 0.3f) * 0.8f : 0f;

            // Steering follows the track curvature
            var steering = (float)Math.Sin(angle * 2) * 0.6f;

            // Gear changes with speed
            var gear = speed switch
            {
                < 80 => 2,
                < 120 => 3,
                < 160 => 4,
                < 200 => 5,
                _ => 6
            };

            // RPM correlates with speed and gear
            var rpm = 3000 + (speed / 250f) * 6000;

            return new TelemetryFrame
            {
                Time = _time,
                PosX = posX,
                PosY = posY,
                Speed = speed,
                Throttle = Math.Clamp(throttle, 0f, 1f),
                Brake = Math.Clamp(brake, 0f, 1f),
                Steering = Math.Clamp(steering, -1f, 1f),
                Gear = gear,
                Rpm = (float)rpm
            };
        }
    }
}
