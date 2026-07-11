using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DuckDB.NET.Data;
using LMU.Telemetry.Core.Models;

namespace LMU.Telemetry.Core.Services;

public class DuckDBTelemetryReader
{
    private string _telemetryPath = string.Empty;
    private readonly TelemetryDataProcessor _processor;

    public DuckDBTelemetryReader()
    {
        _processor = new TelemetryDataProcessor();
    }

    public DuckDBTelemetryReader(string customPath)
    {
        _telemetryPath = customPath;
        _processor = new TelemetryDataProcessor();
    }

    public List<TelemetryFileInfo> GetAvailableRecordings()
    {
        var recordings = new List<TelemetryFileInfo>();

        if (string.IsNullOrEmpty(_telemetryPath) || !Directory.Exists(_telemetryPath))
        {
            return recordings;
        }

        try
        {
            var dbFiles = Directory.GetFiles(_telemetryPath, "*.duckdb");

            foreach (var filePath in dbFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var telemetryInfo = new TelemetryFileInfo
                    {
                        FilePath = filePath,
                        FileName = fileInfo.Name,
                        RecordingDate = fileInfo.LastWriteTime,
                        FileSize = fileInfo.Length
                    };

                    // Try to extract metadata from database
                    try
                    {
                        ExtractMetadata(filePath, telemetryInfo);
                    }
                    catch (Exception metaEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not extract metadata from {filePath}: {metaEx.Message}");
                        // Continue anyway with basic file info
                    }

                    recordings.Add(telemetryInfo);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing file {filePath}: {ex.Message}");
                }
            }

            // Sort by date descending (newest first)
            return recordings.OrderByDescending(r => r.RecordingDate).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error scanning directory {_telemetryPath}: {ex.Message}");
            throw new Exception($"Failed to scan telemetry folder: {ex.Message}", ex);
        }
    }

    private void ExtractMetadata(string filePath, TelemetryFileInfo info)
    {
        try
        {
            using var connection = new DuckDBConnection($"Data Source={filePath};");
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM metadata WHERE key IN ('TrackName', 'CarName')";
            
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var value = reader.GetString(1);
                
                if (key == "TrackName")
                {
                    info.TrackName = value;
                }
                else if (key == "CarName")
                {
                    info.CarName = value;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Metadata extraction failed for {filePath}: {ex.Message}");
            // Leave defaults
        }
    }

    public List<TelemetryFrame> LoadTelemetryData(string filePath)
    {
        var frames = new List<TelemetryFrame>();
        
        // Create debug log file
        var logPath = Path.Combine(Path.GetTempPath(), "LMU_Telemetry_Debug.txt");
        
        using (var logWriter = new System.IO.StreamWriter(logPath, false))
        {
            logWriter.AutoFlush = true; // Force immediate write
            
            void Log(string message)
            {
                var output = $"[{DateTime.Now:HH:mm:ss}] {message}";
                Console.WriteLine(output);
                System.Diagnostics.Debug.WriteLine(output);
                logWriter.WriteLine(output);
            }

            try
        {
            using var connection = new DuckDBConnection($"Data Source={filePath};");
            connection.Open();

            Log($"=== LOADING TELEMETRY ===");
            Log($"File: {filePath}");
            Log($"Debug log: {logPath}");
            Log($"To view debug output: View -> Output -> Select 'C#' from dropdown");
            Log($"Or open temp file: {logPath}");
            Log("");
            
            // DEBUG: List all available channels in the database
            Log("=== AVAILABLE CHANNELS IN DATABASE ===");
            var availableChannels = new List<string>();
            try
            {
                using var listCmd = connection.CreateCommand();
                listCmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema='main' ORDER BY table_name";
                using var listReader = listCmd.ExecuteReader();
                while (listReader.Read())
                {
                    var tableName = listReader.GetString(0);
                    availableChannels.Add(tableName);
                    // Show important channels
                    if (tableName.Contains("Brake") || tableName.Contains("Speed") || tableName.Contains("Throttle") || tableName.Contains("Dist") || tableName.Contains("Gear"))
                    {
                        Log($"  *** {tableName} ***");
                    }
                }
                Log($"Total channels available: {availableChannels.Count}");
            }
            catch (Exception ex)
            {
                Log($"Could not list channels: {ex.Message}");
            }
            
            // Read all primary channels
            Log("=== READING PRIMARY CHANNELS ===");
            var continuousData = new Dictionary<string, List<(double, double)>>();
            var intData = new Dictionary<string, List<(double, int)>>();            
            // Track time ranges per channel to detect timing offsets
            var channelTimeRanges = new Dictionary<string, (double min, double max, int count)>();            
            // Read continuous channels with name mapping
            foreach (var channelName in TelemetryChannelConfig.PrimaryChannels.Keys)
            {
                var channelInfo = TelemetryChannelConfig.PrimaryChannels[channelName];
                
                // Try to find the actual channel name in the database, fallback to original name
                var actualChannelName = availableChannels.Count > 0 
                    ? (ChannelNameMapper.FindChannelName(channelName, availableChannels) ?? channelName)
                    : channelName;
                
                if (channelInfo.Type == ChannelType.Continuous)
                {
                    var data = _processor.ReadChannelData(connection, actualChannelName);
                    if (data.Count > 0)
                    {
                        // Temporarily skip validation to debug
                        // var cleaned = _processor.ValidateAndCleanData(data, actualChannelName).Select(d => d).ToList();
                        
                        // Store with canonical name for later lookup
                        continuousData[channelName] = data;
                        
                        // Track time range
                        channelTimeRanges[channelName] = (data[0].Item1, data[data.Count - 1].Item1, data.Count);
                        
                        // Show sample values for debugging
                        if (data.Count >= 3)
                        {
                            var first = data[0];
                            var mid = data[data.Count / 2];
                            var last = data[data.Count - 1];
                            Log($"  {channelName} [{actualChannelName}]: {data.Count} points - First: t={first.Item1:F3}s v={first.Item2:F2}, Mid: t={mid.Item1:F3}s v={mid.Item2:F2}, Last: t={last.Item1:F3}s v={last.Item2:F2}");
                        }
                        else
                        {
                            Log($"  {channelName} [{actualChannelName}]: {data.Count} points");
                        }
                    }
                    else
                    {
                        Log($"  {channelName} [{actualChannelName}]: NO DATA FOUND!");
                    }
                }
                else if (channelInfo.Type == ChannelType.Event)
                {
                    var data = _processor.ReadChannelDataInt(connection, actualChannelName);
                    if (data.Count > 0)
                    {
                        // Store with canonical name
                        intData[channelName] = data;
                        
                        // Track time range
                        channelTimeRanges[channelName] = (data[0].Item1, data[data.Count - 1].Item1, data.Count);
                        
                        // Log gear data in detail for debugging
                        if (channelName == "Gear" && data.Count >= 10)
                        {
                            Log($"  === GEAR DATA DETAILS ===");
                            Log($"  {channelName} [{actualChannelName}]: {data.Count} events");
                            Log($"  First 10 gear changes:");
                            for (int i = 0; i < Math.Min(10, data.Count); i++)
                            {
                                Log($"    t={data[i].Item1:F3}s -> Gear {data[i].Item2}");
                            }
                            Log($"  Time range: {data[0].Item1:F3}s to {data[data.Count-1].Item1:F3}s");
                            Log($"  Time gap between first two: {(data.Count > 1 ? (data[1].Item1 - data[0].Item1) : 0):F3}s");
                            
                            // Check timing vs throttle data to detect sync offset
                            if (continuousData.ContainsKey("Throttle Pos"))
                            {
                                Log($"  === CHECKING GEAR/THROTTLE TIMING ===");
                                var throttleData = continuousData["Throttle Pos"];
                                
                                // Sample a few gear changes and see what throttle was doing around that time
                                for (int i = 1; i < Math.Min(6, data.Count); i++)
                                {
                                    double gearTime = data[i].Item1;
                                    int prevGear = data[i-1].Item2;
                                    int newGear = data[i].Item2;
                                    
                                    // Skip neutral transitions (gear 0)
                                    if (prevGear == 0 || newGear == 0)
                                        continue;
                                    
                                    // Find throttle values at finer intervals around gear change
                                    Log($"    Shift: {prevGear}->{newGear} at t={gearTime:F3}s");
                                    for (double offset = -0.05; offset <= 0.15; offset += 0.01)
                                    {
                                        var targetTime = gearTime + offset;
                                        var throttlePoint = throttleData.Where(t => Math.Abs(t.Item1 - targetTime) < 0.005).FirstOrDefault();
                                        if (throttlePoint != default)
                                        {
                                            Log($"      {offset:+0.00;-0.00}s: Throttle={throttlePoint.Item2:F1}%");
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            Log($"  {channelName} [{actualChannelName}]: {data.Count} events");
                        }
                    }
                    else
                    {
                        Log($"  {channelName} [{actualChannelName}]: NO EVENT DATA FOUND!");
                    }
                }
            }
            
            if (continuousData.Count == 0)
            {
                Log("ERROR: No channel data found!");
                return frames;
            }
            
            Log($"Successfully read {continuousData.Count} continuous channels, {intData.Count} event channels");
            
            // Analyze time alignment across all channels
            Log("=== CHANNEL TIME ALIGNMENT ===");
            var allTimes = channelTimeRanges.OrderBy(kvp => kvp.Value.min).ToList();
            var globalMin = allTimes.First().Value.min;
            var globalMax = allTimes.Max(kvp => kvp.Value.max);
            
            Log($"Global time range: {globalMin:F3}s to {globalMax:F3}s");
            Log("Channel time offsets from global start:");
            foreach (var kvp in allTimes)
            {
                var offset = kvp.Value.min - globalMin;
                var endOffset = globalMax - kvp.Value.max;
                Log($"  {kvp.Key}: starts {offset:+0.000;-0.000}s, ends {endOffset:+0.000;-0.000}s early, {kvp.Value.count} points");
            }
            
            // CRITICAL: Normalize all channels to start at globalMin (t=0 for synchronized playback)
            Log($"=== NORMALIZING ALL CHANNELS TO START AT t=0 ===");
            
            // Shift continuous channels
            foreach (var channelName in continuousData.Keys.ToList())
            {
                var data = continuousData[channelName];
                var originalStart = data[0].Item1;
                var shift = originalStart - globalMin;
                
                if (Math.Abs(shift) > 0.001) // Only shift if there's a meaningful offset
                {
                    Log($"  Shifting {channelName} by {-shift:F3}s (was {originalStart:F3}s, now {globalMin:F3}s)");
                    continuousData[channelName] = data.Select(d => (d.Item1 - shift, d.Item2)).ToList();
                }
            }
            
            // Shift event channels
            foreach (var channelName in intData.Keys.ToList())
            {
                var data = intData[channelName];
                var originalStart = data[0].Item1;
                var shift = originalStart - globalMin;
                
                if (Math.Abs(shift) > 0.001) // Only shift if there's a meaningful offset
                {
                    Log($"  Shifting {channelName} by {-shift:F3}s (was {originalStart:F3}s, now {globalMin:F3}s)");
                    intData[channelName] = data.Select(d => (d.Item1 - shift, d.Item2)).ToList();
                }
            }
            
            // Log lap event data details
            if (intData.ContainsKey("Lap"))
            {
                var lapEvents = intData["Lap"];
                Log($"=== LAP EVENTS DETAIL ===");
                Log($"Total lap events: {lapEvents.Count}");
                foreach (var evt in lapEvents)
                {
                    Log($"  t={evt.Item1:F3}s -> Lap {evt.Item2}");
                }
            }
            
            // Determine time range
            var (minTime, maxTime) = _processor.GetTimeRange(continuousData, intData);
            double duration = maxTime - minTime;
            
            Log($"Time range: {minTime:F3}s to {maxTime:F3}s (duration: {duration:F1}s)");
            
            // Detect actual lap boundaries from Lap Dist resets and build corrected lap data
            List<double> lapBoundaryTimes = new List<double> { minTime }; // Start of recording is start of first lap
            
            if (continuousData.ContainsKey("Lap Dist"))
            {
                var lapDistData = continuousData["Lap Dist"];
                Log($"=== LAP BOUNDARIES FROM LAP DIST RESETS ===");
                
                // Calculate track length as the max distance seen
                double maxLapDist = lapDistData.Max(d => d.Item2);
                // Threshold for detecting lap reset: 70% of track length
                double resetThreshold = maxLapDist * 0.7;
                Log($"Track length: {maxLapDist:F0}m, reset threshold: {resetThreshold:F0}m");
                
                for (int i = 1; i < lapDistData.Count; i++)
                {
                    double prevDist = lapDistData[i - 1].Item2;
                    double currDist = lapDistData[i].Item2;
                    double currTime = lapDistData[i].Item1;
                    
                    // If distance decreased significantly (crossed finish line), it's a lap boundary
                    if (prevDist - currDist > resetThreshold)
                    {
                        lapBoundaryTimes.Add(currTime);
                        Log($"  Lap boundary #{lapBoundaryTimes.Count - 1}: t={currTime:F3}s (dist: {prevDist:F0}m -> {currDist:F0}m)");
                    }
                }
                Log($"Total lap boundaries found: {lapBoundaryTimes.Count - 1}");
                
                // Replace game's incorrect lap data with corrected lap boundaries
                if (lapBoundaryTimes.Count > 1)
                {
                    Log($"REPLACING GAME LAP DATA with corrected lap boundaries based on Lap Dist resets");
                    var correctedLapData = new List<(double, int)>();
                    for (int i = 0; i < lapBoundaryTimes.Count; i++)
                    {
                        correctedLapData.Add((lapBoundaryTimes[i], i));
                        Log($"  Corrected: t={lapBoundaryTimes[i]:F3}s -> Lap {i}");
                    }
                    intData["Lap"] = correctedLapData;
                }
            }
            
            Console.WriteLine($"Time range: {minTime:F3}s to {maxTime:F3}s (duration: {duration:F2}s)");
            System.Diagnostics.Debug.WriteLine($"Time range: {minTime:F3}s to {maxTime:F3}s (duration: {duration:F2}s)");
            
            if (duration <= 0)
            {
                Log("ERROR: Invalid time range!");
                Console.WriteLine("ERROR: Invalid time range!");
                System.Diagnostics.Debug.WriteLine("ERROR: Invalid time range!");
                return frames;
            }
            
            // Build interpolators for continuous channels
            Log("Building interpolators...");
            System.Diagnostics.Debug.WriteLine("Building interpolators...");
            var interpolators = _processor.BuildInterpolators(continuousData);
            
            if (interpolators.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("ERROR: Failed to create any interpolators!");
                return frames;
            }
            
            // Create synchronized frames at target frequency
            int targetFrequency = TelemetryChannelConfig.TargetFrequency;
            int numFrames = (int)(duration * targetFrequency) + 1;
            
            Log($"Generating {numFrames} synchronized frames at {targetFrequency}Hz...");
            System.Diagnostics.Debug.WriteLine($"Generating {numFrames} synchronized frames at {targetFrequency}Hz...");
            
            for (int i = 0; i < numFrames; i++)
            {
                double time = minTime + (i / (double)targetFrequency);
                var frame = _processor.CreateFrameAtTime(time, interpolators, intData);
                frames.Add(frame);
                
                // Log first few frames for verification
                if (i < 3)
                {
                    System.Diagnostics.Debug.WriteLine($"  Frame {i}: Time={time:F3}s, Speed={frame.Speed:F1}km/h, Throttle={frame.Throttle*100:F1}%, Brake={frame.Brake*100:F1}%, GPS=({frame.PosX:F6},{frame.PosY:F6})");
                }
            }
            
            // Post-process: Calculate proper lap times (reset each lap)
            Log("Calculating per-lap times...");
            System.Diagnostics.Debug.WriteLine("Calculating per-lap times...");
            
            // Log lap distribution in generated frames
            var frameLapCounts = frames.GroupBy(f => f.CurrentLap).OrderBy(g => g.Key).ToList();
            Log($"Generated frames lap distribution:");
            System.Diagnostics.Debug.WriteLine($"Generated frames lap distribution:");
            foreach (var group in frameLapCounts)
            {
                var firstIdx = frames.FindIndex(f => f.CurrentLap == group.Key);
                var lastIdx = frames.FindLastIndex(f => f.CurrentLap == group.Key);
                var msg = $"  Lap {group.Key}: {group.Count()} frames (index {firstIdx}-{lastIdx}, t={frames[firstIdx].Time:F2}s-{frames[lastIdx].Time:F2}s)";
                Log(msg);
                System.Diagnostics.Debug.WriteLine(msg);
            }
            
            // Sample gear values at different times to verify sync
            Log("=== GEAR SYNC VERIFICATION ===");
            var sampleTimes = new[] { 15.0, 25.0, 30.0, 50.0, 100.0, 150.0 };
            foreach (var t in sampleTimes)
            {
                var frame = frames.FirstOrDefault(f => Math.Abs(f.Time - t) < 0.1);
                if (frame != null)
                {
                    Log($"  t={frame.Time:F2}s: Gear={frame.Gear}, Speed={frame.Speed:F0}km/h, RPM={frame.Rpm:F0}, Throttle={frame.Throttle*100:F0}%");
                }
            }
            
            double lapStartTime = minTime;
            int currentLap = frames[0].CurrentLap;
            
            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                
                // Detect lap change
                if (frame.CurrentLap != currentLap)
                {
                    System.Diagnostics.Debug.WriteLine($"Lap change in frames: {currentLap} → {frame.CurrentLap} at frame {i} (t={frame.Time:F2}s)");
                    currentLap = frame.CurrentLap;
                    lapStartTime = frame.Time;
                }
                
                // Calculate lap time from start of current lap
                var lapTime = (float)(frame.Time - lapStartTime);
                
                // Create new frame with corrected lap time
                frames[i] = new TelemetryFrame
                {
                    Time = frame.Time,
                    PosX = frame.PosX,
                    PosY = frame.PosY,
                    Speed = frame.Speed,
                    Throttle = frame.Throttle,
                    Brake = frame.Brake,
                    Steering = frame.Steering,
                    Rpm = frame.Rpm,
                    Gear = frame.Gear,
                    LapDistance = frame.LapDistance,
                    LapTime = lapTime, // Corrected lap time
                    CurrentLap = frame.CurrentLap,
                    Sector = frame.Sector
                };
            }

            Console.WriteLine($"Successfully loaded {frames.Count} synchronized frames");
            System.Diagnostics.Debug.WriteLine($"Successfully loaded {frames.Count} synchronized frames");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR loading telemetry: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine($"Error loading telemetry: {ex.Message}");
            throw new Exception($"Failed to load telemetry data: {ex.Message}", ex);
        }
        } // End using logWriter block

        return frames;
    }

    public string GetTelemetryPath() => _telemetryPath;

    public bool TelemetryPathExists() => Directory.Exists(_telemetryPath);

    public void SetCustomPath(string path)
    {
        _telemetryPath = path;
    }
}
