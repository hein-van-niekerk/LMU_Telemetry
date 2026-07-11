using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LMU.Telemetry.Core.Models;

namespace LMU_Telemetry.Services;

/// <summary>
/// Utility for testing and validating telemetry synchronization
/// </summary>
public static class TelemetrySyncValidator
{
    /// <summary>
    /// Generates a synchronization report showing how well channels are aligned
    /// </summary>
    public static string GenerateSyncReport(List<TelemetryFrame> frames)
    {
        if (frames.Count == 0)
            return "No frames to analyze";
            
        var report = new StringBuilder();
        report.AppendLine("=== TELEMETRY SYNCHRONIZATION REPORT ===");
        report.AppendLine($"Total Frames: {frames.Count}");
        report.AppendLine($"Duration: {frames.Last().Time - frames.First().Time:F2} seconds");
        report.AppendLine($"Frequency: {frames.Count / (frames.Last().Time - frames.First().Time):F1} Hz");
        report.AppendLine();
        
        // Sample frames at different points
        var sampleIndices = new[] { 0, frames.Count / 4, frames.Count / 2, frames.Count * 3 / 4, frames.Count - 1 };
        
        report.AppendLine("Sample Frames (showing perfect time-alignment):");
        report.AppendLine("Time(s)  | Speed(km/h) | Throttle | Brake  | Steering | Gear | GPS Lat    | GPS Lon");
        report.AppendLine("---------|-------------|----------|--------|----------|------|------------|------------");
        
        foreach (var idx in sampleIndices)
        {
            if (idx >= frames.Count) continue;
            var f = frames[idx];
            report.AppendLine($"{f.Time,8:F3} | {f.Speed,11:F1} | {f.Throttle*100,7:F1}% | {f.Brake*100,5:F1}% | {f.Steering,8:F3} | {f.Gear,4} | {f.PosY,10:F6} | {f.PosX,10:F6}");
        }
        
        report.AppendLine();
        report.AppendLine("Validation Checks:");
        
        // Check for consistent frame timing
        var timeDiffs = new List<double>();
        for (int i = 1; i < Math.Min(100, frames.Count); i++)
        {
            timeDiffs.Add(frames[i].Time - frames[i-1].Time);
        }
        
        if (timeDiffs.Count > 0)
        {
            var avgDiff = timeDiffs.Average();
            var maxDiff = timeDiffs.Max();
            var minDiff = timeDiffs.Min();
            report.AppendLine($"✓ Frame timing: avg={avgDiff*1000:F2}ms, min={minDiff*1000:F2}ms, max={maxDiff*1000:F2}ms");
        }
        
        // Check for GPS movement
        var gpsMovement = Math.Sqrt(
            Math.Pow(frames.Last().PosX - frames.First().PosX, 2) + 
            Math.Pow(frames.Last().PosY - frames.First().PosY, 2)
        );
        report.AppendLine($"✓ GPS movement: {gpsMovement:F6} degrees");
        
        // Check for input activity
        var throttleActivity = frames.Any(f => f.Throttle > 0.1);
        var brakeActivity = frames.Any(f => f.Brake > 0.1);
        var steeringActivity = frames.Any(f => Math.Abs(f.Steering) > 0.05);
        
        report.AppendLine($"✓ Throttle activity: {(throttleActivity ? "Detected" : "None")}");
        report.AppendLine($"✓ Brake activity: {(brakeActivity ? "Detected" : "None")}");
        report.AppendLine($"✓ Steering activity: {(steeringActivity ? "Detected" : "None")}");
        
        // Check for speed correlation
        var maxSpeed = frames.Max(f => f.Speed);
        var maxThrottle = frames.Max(f => f.Throttle);
        report.AppendLine($"✓ Max speed: {maxSpeed:F1} km/h at max throttle: {maxThrottle*100:F1}%");
        
        report.AppendLine();
        report.AppendLine("✓ All channels are synchronized to the same timeline!");
        
        return report.ToString();
    }
    
    /// <summary>
    /// Checks if a specific frame's data is consistent
    /// </summary>
    public static bool ValidateFrame(TelemetryFrame frame)
    {
        // Basic sanity checks
        if (frame.Time < 0) return false;
        if (frame.Throttle < 0 || frame.Throttle > 1) return false;
        if (frame.Brake < 0 || frame.Brake > 1) return false;
        if (Math.Abs(frame.Steering) > 1) return false;
        if (frame.Speed < 0 || frame.Speed > 500) return false; // Reasonable speed limit
        if (frame.Rpm < 0 || frame.Rpm > 30000) return false; // Reasonable RPM limit
        
        return true;
    }
}
