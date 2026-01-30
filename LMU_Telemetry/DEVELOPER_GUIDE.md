# Quick Reference: Using the Telemetry Data Processor

## Architecture

```
DuckDBTelemetryReader (reads files, manages connection)
    ↓
TelemetryDataProcessor (processes & synchronizes data)
    ↓
TelemetryFrame[] (60Hz synchronized output)
```

## Key Components

### 1. TelemetryChannelConfig
```csharp
// Configure which channels to load
TelemetryChannelConfig.PrimaryChannels   // Essential channels
TelemetryChannelConfig.SecondaryChannels // Additional analysis channels
TelemetryChannelConfig.TargetFrequency   // Output frequency (60Hz default)
```

### 2. TelemetryDataProcessor
```csharp
var processor = new TelemetryDataProcessor();

// Read a channel
var data = processor.ReadChannelData(connection, "GPS Latitude");

// Validate and clean
data = processor.ValidateAndCleanData(data, "GPS Latitude");

// Build interpolators (for smooth resampling)
var interpolators = processor.BuildInterpolators(channelData);

// Create synchronized frame
var frame = processor.CreateFrameAtTime(timestamp, interpolators, intChannels);
```

### 3. DuckDBTelemetryReader (Simplified)
```csharp
var reader = new DuckDBTelemetryReader(pathToTelemetryFolder);
var recordings = reader.GetAvailableRecordings();
var frames = reader.LoadTelemetryData(recordings[0].FilePath);
// frames are now perfectly synchronized at 60Hz!
```

## Adding New Channels

### Step 1: Add to TelemetryChannelConfig.cs
```csharp
public static readonly Dictionary<string, ChannelInfo> PrimaryChannels = new()
{
    // ... existing channels ...
    ["Your Channel"] = new() { 
        Name = "Your Channel", 
        Frequency = 100, // Hz
        Type = ChannelType.Continuous 
    },
};
```

### Step 2: Add to TelemetryFrame.cs (if needed)
```csharp
public sealed class TelemetryFrame
{
    // ... existing properties ...
    public float YourNewField { get; init; }
}
```

### Step 3: Update CreateFrameAtTime in TelemetryDataProcessor.cs
```csharp
return new TelemetryFrame
{
    // ... existing fields ...
    YourNewField = (float)GetInterpolatedValue(interpolators, "Your Channel", timestamp),
};
```

## Data Flow Example

### Input: Raw DuckDB Channels
```
GPS Latitude:  [10Hz]  t=0.0, t=0.1, t=0.2, ...
GPS Longitude: [10Hz]  t=0.0, t=0.1, t=0.2, ...
Ground Speed:  [100Hz] t=0.00, t=0.01, t=0.02, ...
Throttle Pos:  [50Hz]  t=0.00, t=0.02, t=0.04, ...
Brake Pos:     [50Hz]  t=0.00, t=0.02, t=0.04, ...
Steering Pos:  [100Hz] t=0.00, t=0.01, t=0.02, ...
```

### Processing Steps
1. **Read** all channels with timestamps
2. **Validate** (remove nulls, outliers)
3. **Build interpolators** (LinearSpline)
4. **Resample** to 60Hz timeline

### Output: Synchronized Frames
```
Frame 0:  t=0.000s  [GPS: 48.123456, 11.654321] [Speed: 127.3 km/h] [Throttle: 87%] [Brake: 0%]
Frame 1:  t=0.017s  [GPS: 48.123458, 11.654323] [Speed: 128.1 km/h] [Throttle: 89%] [Brake: 0%]
Frame 2:  t=0.033s  [GPS: 48.123460, 11.654325] [Speed: 128.9 km/h] [Throttle: 91%] [Brake: 0%]
...
```

## Debugging Tips

### Enable Debug Output
Debug output is automatically written to Debug console in Visual Studio:
- Channel reading progress
- Interpolator creation status
- First 3 frames for verification
- Error messages with details

### Validate Synchronization
```csharp
using LMU_Telemetry.Services;

var frames = reader.LoadTelemetryData(filePath);
var report = TelemetrySyncValidator.GenerateSyncReport(frames);
System.Diagnostics.Debug.WriteLine(report);
```

### Common Issues

**Problem**: "No channel data found"
- **Solution**: Check that DuckDB file has the expected table names
- **Tip**: Use a DuckDB viewer extension to inspect file structure

**Problem**: "Failed to create interpolators"
- **Solution**: Channels might have < 2 data points
- **Tip**: Check debug output for channel sample counts

**Problem**: GPS track looks jerky
- **Solution**: 10Hz GPS is upsampled to 60Hz using linear interpolation
- **Tip**: This is expected behavior; consider GPS smoothing if needed

**Problem**: Inputs don't match track position
- **Solution**: This shouldn't happen anymore! All data is synchronized
- **Tip**: Use TelemetrySyncValidator to verify

## Performance Notes

- **Memory**: ~400 bytes per frame (60 frames/second)
- **Processing**: < 2 seconds for a 30-minute session
- **Interpolation**: O(log n) lookup per channel per frame
- **Recommended**: Load sessions < 2 hours to keep memory < 1GB

## Testing

```csharp
// Quick validation
var frames = reader.LoadTelemetryData(testFile);
Assert.IsTrue(frames.Count > 0);
Assert.IsTrue(TelemetrySyncValidator.ValidateFrame(frames[0]));

// Full report
var report = TelemetrySyncValidator.GenerateSyncReport(frames);
Console.WriteLine(report);
```
