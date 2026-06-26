using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics;
using MathNet.Numerics.Interpolation;
using DuckDB.NET.Data;
using LMU_Telemetry.Models;

namespace LMU_Telemetry.Services;

/// <summary>
/// Processes raw DuckDB telemetry data with proper time-alignment, resampling, and filtering
/// </summary>
public class TelemetryDataProcessor
{
    private const double MinValidTimestamp = 0.0;
    private const double MaxValidTimestamp = 86400.0; // 24 hours max
    private bool _hasGpsReference;
    private double _gpsRefLat;
    private double _gpsRefLon;
    
    /// <summary>
    /// Reads a channel's time series data from DuckDB
    /// </summary>
    public List<(double time, double value)> ReadChannelData(DuckDBConnection connection, string channelName)
    {
        var data = new List<(double, double)>();
        
        try
        {
            using var cmd = connection.CreateCommand();
            
            // Try reading with timestamp column
            try
            {
                cmd.CommandText = $"SELECT ts, value FROM \"{channelName}\" WHERE ts IS NOT NULL AND value IS NOT NULL ORDER BY ts";
                using var reader = cmd.ExecuteReader();
                
                while (reader.Read())
                {
                    double timestamp = Convert.ToDouble(reader.GetValue(0));
                    double value = Convert.ToDouble(reader.GetValue(1));
                    
                    // Validate timestamp
                    if (timestamp >= MinValidTimestamp && timestamp <= MaxValidTimestamp)
                    {
                        data.Add((timestamp, value));
                    }
                }
                
                if (data.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Channel '{channelName}': Read {data.Count} timestamped values");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Channel '{channelName}': No timestamp column ({ex.Message}), using synthetic timeline");
                
                // No timestamp column - read values and generate synthetic timeline
                cmd.CommandText = $"SELECT value FROM \"{channelName}\" WHERE value IS NOT NULL";
                using var reader = cmd.ExecuteReader();
                
                var channelInfo = TelemetryChannelConfig.PrimaryChannels.GetValueOrDefault(channelName) 
                    ?? TelemetryChannelConfig.SecondaryChannels.GetValueOrDefault(channelName);
                    
                double frequency = channelInfo?.Frequency ?? 60;
                int index = 0;
                
                while (reader.Read())
                {
                    double value = Convert.ToDouble(reader.GetValue(0));
                    double timestamp = index / frequency;
                    data.Add((timestamp, value));
                    index++;
                }
                
                if (data.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Channel '{channelName}': Generated {data.Count} synthetic timestamps at {frequency}Hz");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR reading channel '{channelName}': {ex.Message}");
        }
        
        return data;
    }
    
    /// <summary>
    /// Reads integer channel data (gear, lap, sector)
    /// </summary>
    public List<(double time, int value)> ReadChannelDataInt(DuckDBConnection connection, string channelName)
    {
        var data = new List<(double, int)>();
        
        try
        {
            using var cmd = connection.CreateCommand();
            
            try
            {
                cmd.CommandText = $"SELECT ts, value FROM \"{channelName}\" WHERE ts IS NOT NULL AND value IS NOT NULL ORDER BY ts";
                using var reader = cmd.ExecuteReader();
                
                while (reader.Read())
                {
                    double timestamp = Convert.ToDouble(reader.GetValue(0));
                    int value = Convert.ToInt32(reader.GetValue(1));
                    
                    if (timestamp >= MinValidTimestamp && timestamp <= MaxValidTimestamp)
                    {
                        data.Add((timestamp, value));
                    }
                }
            }
            catch
            {
                cmd.CommandText = $"SELECT value FROM \"{channelName}\" WHERE value IS NOT NULL";
                using var reader = cmd.ExecuteReader();
                
                var channelInfo = TelemetryChannelConfig.PrimaryChannels.GetValueOrDefault(channelName);
                double frequency = channelInfo?.Frequency ?? 60;
                int index = 0;
                
                while (reader.Read())
                {
                    int value = Convert.ToInt32(reader.GetValue(0));
                    double timestamp = index / frequency;
                    data.Add((timestamp, value));
                    index++;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read channel '{channelName}': {ex.Message}");
        }
        
        return data;
    }
    
    /// <summary>
    /// Creates a synchronized telemetry frame at a specific timestamp using interpolation
    /// </summary>
    public TelemetryFrame CreateFrameAtTime(double timestamp, Dictionary<string, IInterpolation> interpolators, Dictionary<string, List<(double, int)>> intChannels)
    {
        var speedValue = GetInterpolatedValue(interpolators, "GPS Speed", timestamp);
        var throttle = GetInterpolatedValue(interpolators, "Throttle Pos", timestamp);
        var brake = GetInterpolatedValue(interpolators, "Brake Pos", timestamp);
        
        // GPS Speed is in m/s, convert to km/h
        float speed = (float)(speedValue * 3.6);
        
        if (speed > 400)
            speed = 400; // clamp; don't zero (zeroing creates false speed spikes)
        
        var gearValue = GetIntValueAtTime(intChannels, "Gear", timestamp);
        
        // Log gear and brake correlation for debugging sync issues
        if (timestamp < 5.0 && (int)(timestamp * 10) % 10 == 0) // Log every 1s for first 5s
        {
            System.Diagnostics.Debug.WriteLine($"[SYNC] t={timestamp:F2}s: Gear={gearValue}, Brake={brake:F1}%, Throttle={throttle:F1}%, Speed={speed:F1}km/h");
        }
        
        double posX;
        double posY;
        bool hasWorldPosX = interpolators.ContainsKey("World Pos X");
        bool hasWorldPosY = interpolators.ContainsKey("World Pos Y") || interpolators.ContainsKey("World Pos Z");

        if (hasWorldPosX && hasWorldPosY)
        {
            posX = GetInterpolatedValue(interpolators, "World Pos X", timestamp);
            posY = interpolators.ContainsKey("World Pos Y")
                ? GetInterpolatedValue(interpolators, "World Pos Y", timestamp)
                : GetInterpolatedValue(interpolators, "World Pos Z", timestamp);
        }
        else
        {
            double lat = GetInterpolatedValue(interpolators, "GPS Latitude", timestamp);
            double lon = GetInterpolatedValue(interpolators, "GPS Longitude", timestamp);
            (posX, posY) = ConvertGpsToMeters(lat, lon);
        }

        var frame = new TelemetryFrame
        {
            Time = timestamp,
            PosX = (float)posX,
            PosY = (float)posY,
            Speed = speed,
            Throttle = (float)(throttle / 100.0), // % to 0-1
            Brake = (float)(brake / 100.0), // % to 0-1
            Steering = (float)GetInterpolatedValue(interpolators, "Steering Pos", timestamp),
            Rpm = (float)GetInterpolatedValue(interpolators, "Engine RPM", timestamp),
            Gear = gearValue,
            LapDistance = (float)GetInterpolatedValue(interpolators, "Lap Dist", timestamp),
            LapTime = (float)GetInterpolatedValue(interpolators, "Current Lap Time", timestamp),
            CurrentLap = GetIntValueAtTime(intChannels, "Lap", timestamp),
            Sector = GetIntValueAtTime(intChannels, "Current Sector", timestamp),
            ExtendedData = PopulateExtendedData(interpolators, intChannels, timestamp)
        };
        

        return frame;
    }

    /// <summary>
    /// Populate extended telemetry data from all available channels
    /// </summary>
    private Dictionary<string, object?> PopulateExtendedData(Dictionary<string, IInterpolation> interpolators, Dictionary<string, List<(double, int)>> intChannels, double timestamp)
    {
        var extendedData = new Dictionary<string, object?>();

        // All continuous channels to extract
        var continuousChannels = new[]
        {
            "Ambient Temperature", "Brake Pos", "Brake Pos Unfiltered", "Brake Thickness",
            "Brakes Air Temp", "Brakes Force", "Brakes Temp", "Clutch Pos", "Clutch Pos Unfiltered",
            "Clutch RPM", "Drag", "Engine Oil Temp", "Engine RPM", "Engine Water Temp",
            "FFB Output", "Front3rdDeflection", "FrontDownForce", "FrontRideHeight", "FrontWingHeight",
            "Fuel Level", "G Force Lat", "G Force Long", "G Force Vert",
            "GPS Latitude", "GPS Longitude", "GPS Speed", "GPS Time", "Ground Speed",
            "Lap Dist", "Lateral Acceleration", "Longitudinal Acceleration", "OverheatingState",
            "Path Lateral", "ReadDownForce", "Rear3rdDeflection", "RearRideHeight", "Regen Rate",
            "RideHeights", "SoC", "Steered Angle", "Steering Pos", "Steering Pos Unfiltered",
            "Steering Shaft Torque", "Susp Pos", "Throttle Pos", "Throttle Pos Unfiltered",
            "Time Behind Next", "Total Dist", "Track Edge", "Track Temperature", "Turbo Boost Pressure",
            "Tyres Wear", "TyresCarcassTemp", "TyresPressure", "TyresRimTemp", "TyresRubberTemp",
            "TyresTempCentre", "TyresTempLeft", "TyresTempRight", "Virtual Energy",
            "Wheel Speed", "Wind Heading", "Wind Speed", "Yaw Rate"
        };

        // Extract continuous channel values
        foreach (var channelName in continuousChannels)
        {
            try
            {
                var value = GetInterpolatedValue(interpolators, channelName, timestamp);
                extendedData[channelName] = value;
            }
            catch
            {
                // Channel not available
            }
        }

        // All event channels to extract
        var eventChannels = new[]
        {
            "ABS", "ABSLevel", "AntiStall Activated", "Best LapTime", "Best Sector1", "Best Sector2",
            "Brake Bias Rear", "Brake Migration", "CloudDarkness", "Current LapTime", "Current Sector",
            "Current Sector1", "Current Sector2", "Engine Max RPM", "Finish Status", "FrontFlapActivated",
            "FuelMixtureMap", "Gear", "Headlights State", "In Pits", "Lap", "Lap Time",
            "Last Sector1", "Last Sector2", "LastImpactMagnitude", "LaunchControlActive",
            "Minimum Path Wetness", "OffpathWetness", "RearFlapActivated", "RearFlapLegalStatus",
            "Sector1 Flag", "Sector2 Flag", "Sector3 Flag", "Speed Limiter", "SurfaceTypes",
            "TC", "TCCut", "TCLevel", "TCSlipAngle", "TyresCompound", "WheelsDetached", "Yellow Flag State"
        };

        // Extract event channel values
        foreach (var channelName in eventChannels)
        {
            try
            {
                var value = GetIntValueAtTime(intChannels, channelName, timestamp);
                extendedData[channelName] = value;
            }
            catch
            {
                // Channel not available
            }
        }

        return extendedData;
    }
    
    /// <summary>
    /// Gets interpolated value at a specific time, with fallback to 0
    /// </summary>
    private double GetInterpolatedValue(Dictionary<string, IInterpolation> interpolators, string channelName, double time)
    {
        if (interpolators.TryGetValue(channelName, out var interpolator))
        {
            try
            {
                return interpolator.Interpolate(time);
            }
            catch
            {
                // Time outside bounds
                return 0.0;
            }
        }
        return 0.0;
    }

    /// <summary>
    /// Gets interpolated value from the first available channel in priority order.
    /// </summary>
    private double GetInterpolatedValueFirstAvailable(Dictionary<string, IInterpolation> interpolators, double time, params string[] channelNames)
    {
        foreach (var channelName in channelNames)
        {
            if (interpolators.ContainsKey(channelName))
            {
                return GetInterpolatedValue(interpolators, channelName, time);
            }
        }

        return 0.0;
    }

    /// <summary>
    /// Converts GPS lat/lon degrees to local meters using the first observed point as origin.
    /// </summary>
    private (double x, double y) ConvertGpsToMeters(double latDeg, double lonDeg)
    {
        if (!_hasGpsReference)
        {
            _gpsRefLat = latDeg;
            _gpsRefLon = lonDeg;
            _hasGpsReference = true;
        }

        double latRad = _gpsRefLat * Math.PI / 180.0;
        double metersPerDegLat = 111_320.0;
        double metersPerDegLon = 111_320.0 * Math.Cos(latRad);

        double x = (lonDeg - _gpsRefLon) * metersPerDegLon;
        double y = (latDeg - _gpsRefLat) * metersPerDegLat;
        return (x, y);
    }
    
    /// <summary>
    /// Gets integer value at time using step function (no interpolation)
    /// </summary>
    private int GetIntValueAtTime(Dictionary<string, List<(double, int)>> intChannels, string channelName, double targetTime)
    {
        if (!intChannels.TryGetValue(channelName, out var data) || data.Count == 0)
            return 0;
            
        // Step function - find the most recent value at or before target time
        for (int i = data.Count - 1; i >= 0; i--)
        {
            if (targetTime >= data[i].Item1)
                return data[i].Item2;
        }
        
        return data[0].Item2;
    }
    
    /// <summary>
    /// Builds interpolators for continuous channels
    /// </summary>
    public Dictionary<string, IInterpolation> BuildInterpolators(Dictionary<string, List<(double, double)>> channelData)
    {
        var interpolators = new Dictionary<string, IInterpolation>();
        
        foreach (var (channelName, data) in channelData)
        {
            if (data.Count < 2)
            {
                System.Diagnostics.Debug.WriteLine($"Channel '{channelName}' has insufficient data ({data.Count} points)");
                continue;
            }
            
            try
            {
                // Extract times and values
                var times = data.Select(d => d.Item1).ToArray();
                var values = data.Select(d => d.Item2).ToArray();
                
                // Remove duplicates (keep first occurrence)
                var uniqueData = new List<(double time, double value)>();
                double lastTime = double.MinValue;
                
                for (int i = 0; i < times.Length; i++)
                {
                    if (Math.Abs(times[i] - lastTime) > 1e-9) // Not a duplicate
                    {
                        uniqueData.Add((times[i], values[i]));
                        lastTime = times[i];
                    }
                }
                
                if (uniqueData.Count < 2)
                {
                    System.Diagnostics.Debug.WriteLine($"Channel '{channelName}' has insufficient unique data points");
                    continue;
                }
                
                times = uniqueData.Select(d => d.time).ToArray();
                values = uniqueData.Select(d => d.value).ToArray();
                
                // Use linear interpolation (fast and suitable for high-frequency telemetry)
                var interpolator = LinearSpline.Interpolate(times, values);
                interpolators[channelName] = interpolator;
                
                System.Diagnostics.Debug.WriteLine($"Created interpolator for '{channelName}': {uniqueData.Count} points, range [{times.First():F3}s - {times.Last():F3}s]");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create interpolator for '{channelName}': {ex.Message}");
            }
        }
        
        return interpolators;
    }
    
    /// <summary>
    /// Determines the time range covered by all channels
    /// </summary>
    public (double minTime, double maxTime) GetTimeRange(Dictionary<string, List<(double, double)>> continuousData, Dictionary<string, List<(double, int)>> intData)
    {
        var allTimes = new List<double>();
        
        foreach (var data in continuousData.Values)
        {
            if (data.Count > 0)
            {
                allTimes.Add(data.First().Item1);
                allTimes.Add(data.Last().Item1);
            }
        }
        
        foreach (var data in intData.Values)
        {
            if (data.Count > 0)
            {
                allTimes.Add(data.First().Item1);
                allTimes.Add(data.Last().Item1);
            }
        }
        
        if (allTimes.Count == 0)
            return (0, 0);
            
        return (allTimes.Min(), allTimes.Max());
    }
    
    /// <summary>
    /// Validates and cleans channel data (removes outliers, invalid values)
    /// </summary>
    public List<(double, double)> ValidateAndCleanData(List<(double, double)> data, string channelName, double? minValue = null, double? maxValue = null)
    {
        if (data.Count == 0)
            return data;
        
        var cleaned = new List<(double, double)>();
        
        // Calculate statistics for outlier detection
        var values = data.Select(d => d.Item2).ToList();
        values.Sort();
        
        double median = values[values.Count / 2];
        double q1 = values[values.Count / 4];
        double q3 = values[values.Count * 3 / 4];
        double iqr = q3 - q1;
        
        // Use IQR method for outlier detection (1.5x IQR is standard, 3x for more tolerance)
        double lowerBound = minValue ?? (q1 - 3 * iqr);
        double upperBound = maxValue ?? (q3 + 3 * iqr);
        
        int outlierCount = 0;
        
        foreach (var point in data)
        {
            if (point.Item2 >= lowerBound && point.Item2 <= upperBound)
            {
                cleaned.Add(point);
            }
            else
            {
                outlierCount++;
            }
        }
        
        if (outlierCount > 0)
        {
            System.Diagnostics.Debug.WriteLine($"Channel '{channelName}': Removed {outlierCount} outliers (kept {cleaned.Count}/{data.Count} points)");
        }
        
        return cleaned;
    }
}
