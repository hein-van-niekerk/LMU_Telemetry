using System.Collections.Generic;
using System.Linq;

namespace LMU.Telemetry.Core.Services;

/// <summary>
/// Maps canonical channel names to possible alternative names in the database
/// </summary>
public static class ChannelNameMapper
{
    // Map of canonical name -> possible alternative names
    private static readonly Dictionary<string, string[]> ChannelAliases = new()
    {
        ["Brake Pos"] = new[] { "Brake Pos", "Brake Position", "Brake", "Brakes Pos" },
        ["GPS Speed"] = new[] { "GPS Speed", "Speed", "Velocity" }, // Changed from Ground Speed
        ["Throttle Pos"] = new[] { "Throttle Pos", "Throttle Position", "Throttle" },
        ["Steering Pos"] = new[] { "Steering Pos", "Steering Position", "Steering" },
        ["GPS Latitude"] = new[] { "GPS Latitude", "Latitude", "Lat" },
        ["GPS Longitude"] = new[] { "GPS Longitude", "Longitude", "Lon", "Long" },
        ["World Pos X"] = new[] { "World Pos X", "World Position X", "Position X", "PosX", "World X", "Car Pos X", "Vehicle Pos X" },
        ["World Pos Y"] = new[] { "World Pos Y", "World Position Y", "Position Y", "PosY", "World Y", "Car Pos Y", "Vehicle Pos Y" },
        ["World Pos Z"] = new[] { "World Pos Z", "World Position Z", "Position Z", "PosZ", "World Z", "Car Pos Z", "Vehicle Pos Z" },
        ["Engine RPM"] = new[] { "Engine RPM", "RPM", "EngineRPM" },
        ["Gear"] = new[] { "Gear" },
        ["Lap Dist"] = new[] { "Lap Dist", "Lap Distance", "LapDist" },
        ["Current Lap Time"] = new[] { "Current Lap Time", "Current LapTime", "Lap Time", "LapTime" },
        ["Lap"] = new[] { "Lap", "Current Lap", "CurrentLap" },
        ["Current Sector"] = new[] { "Current Sector", "Sector", "CurrentSector" }
    };
    
    /// <summary>
    /// Finds the actual channel name in the database that matches the canonical name
    /// </summary>
    public static string? FindChannelName(string canonicalName, List<string> availableChannels)
    {
        if (!ChannelAliases.TryGetValue(canonicalName, out var aliases))
        {
            // No aliases defined, try exact match
            return availableChannels.Contains(canonicalName) ? canonicalName : null;
        }
        
        // Try each alias in order
        foreach (var alias in aliases)
        {
            if (availableChannels.Contains(alias))
            {
                if (alias != canonicalName)
                {
                    System.Diagnostics.Debug.WriteLine($"Channel mapping: '{canonicalName}' -> '{alias}'");
                }
                return alias;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets all possible names for a canonical channel
    /// </summary>
    public static string[] GetAliases(string canonicalName)
    {
        return ChannelAliases.GetValueOrDefault(canonicalName) ?? new[] { canonicalName };
    }
}
