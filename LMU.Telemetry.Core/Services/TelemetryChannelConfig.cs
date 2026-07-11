using System.Collections.Generic;

namespace LMU.Telemetry.Core.Services;

/// <summary>
/// Configuration for a telemetry channel including its sampling frequency
/// </summary>
public class ChannelInfo
{
    public string Name { get; set; } = string.Empty;
    public int Frequency { get; set; }
    public ChannelType Type { get; set; }
}

public enum ChannelType
{
    Continuous,  // High-frequency sampled data
    Event        // Event-based discrete data
}

/// <summary>
/// Defines all available telemetry channels and their properties
/// </summary>
public static class TelemetryChannelConfig
{
    // Target resampling frequency for synchronized output (Hz)
    public const int TargetFrequency = 60;
    
    // Channels essential for visualization
    public static readonly Dictionary<string, ChannelInfo> PrimaryChannels = new()
    {
        ["GPS Latitude"] = new() { Name = "GPS Latitude", Frequency = 10, Type = ChannelType.Continuous },
        ["GPS Longitude"] = new() { Name = "GPS Longitude", Frequency = 10, Type = ChannelType.Continuous },
        ["World Pos X"] = new() { Name = "World Pos X", Frequency = 60, Type = ChannelType.Continuous },
        ["World Pos Y"] = new() { Name = "World Pos Y", Frequency = 60, Type = ChannelType.Continuous },
        ["World Pos Z"] = new() { Name = "World Pos Z", Frequency = 60, Type = ChannelType.Continuous },
        ["GPS Speed"] = new() { Name = "GPS Speed", Frequency = 10, Type = ChannelType.Continuous }, // Changed from Ground Speed
        ["Throttle Pos"] = new() { Name = "Throttle Pos", Frequency = 50, Type = ChannelType.Continuous },
        ["Brake Pos"] = new() { Name = "Brake Pos", Frequency = 50, Type = ChannelType.Continuous },
        ["Steering Pos"] = new() { Name = "Steering Pos", Frequency = 100, Type = ChannelType.Continuous },
        ["Engine RPM"] = new() { Name = "Engine RPM", Frequency = 100, Type = ChannelType.Continuous },
        ["Gear"] = new() { Name = "Gear", Frequency = 0, Type = ChannelType.Event },
        ["Lap Dist"] = new() { Name = "Lap Dist", Frequency = 10, Type = ChannelType.Continuous },
        ["Current Lap Time"] = new() { Name = "Current Lap Time", Frequency = 0, Type = ChannelType.Event }, // Changed to Event type with timestamp
        ["Lap"] = new() { Name = "Lap", Frequency = 0, Type = ChannelType.Event },
        ["Current Sector"] = new() { Name = "Current Sector", Frequency = 0, Type = ChannelType.Event }
    };
    
    // Additional channels available for analysis (optional)
    public static readonly Dictionary<string, ChannelInfo> SecondaryChannels = new()
    {
        ["GPS Speed"] = new() { Name = "GPS Speed", Frequency = 10, Type = ChannelType.Continuous },
        ["Steered Angle"] = new() { Name = "Steered Angle", Frequency = 100, Type = ChannelType.Continuous },
        ["G Force Lat"] = new() { Name = "G Force Lat", Frequency = 10, Type = ChannelType.Continuous },
        ["G Force Long"] = new() { Name = "G Force Long", Frequency = 10, Type = ChannelType.Continuous },
        ["Lateral Acceleration"] = new() { Name = "Lateral Acceleration", Frequency = 100, Type = ChannelType.Continuous },
        ["Longitudinal Acceleration"] = new() { Name = "Longitudinal Acceleration", Frequency = 100, Type = ChannelType.Continuous },
        ["Yaw Rate"] = new() { Name = "Yaw Rate", Frequency = 100, Type = ChannelType.Continuous },
        ["Wheel Speed"] = new() { Name = "Wheel Speed", Frequency = 100, Type = ChannelType.Continuous },
        ["Brake Thickness"] = new() { Name = "Brake Thickness", Frequency = 10, Type = ChannelType.Continuous },
        ["Brakes Temp"] = new() { Name = "Brakes Temp", Frequency = 50, Type = ChannelType.Continuous },
        ["TyresTempCentre"] = new() { Name = "TyresTempCentre", Frequency = 100, Type = ChannelType.Continuous },
        ["TyresPressure"] = new() { Name = "TyresPressure", Frequency = 10, Type = ChannelType.Continuous },
        ["Fuel Level"] = new() { Name = "Fuel Level", Frequency = 20, Type = ChannelType.Continuous },
        ["Engine Water Temp"] = new() { Name = "Engine Water Temp", Frequency = 7, Type = ChannelType.Continuous },
        ["Engine Oil Temp"] = new() { Name = "Engine Oil Temp", Frequency = 7, Type = ChannelType.Continuous }
    };
}
