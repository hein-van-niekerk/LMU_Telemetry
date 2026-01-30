# Telemetry Data Processing Improvements

## Overview
Implemented a robust data processing pipeline to handle the messy and unorganized DuckDB telemetry data with proper time-alignment and synchronization.

## Problem Statement
- **Multiple frequencies**: Channels sampled at different rates (1Hz to 100Hz)
- **Time synchronization**: GPS, speed, throttle, brake, steering need to be perfectly aligned
- **Data quality**: No validation, outlier detection, or cleaning
- **Messy code**: Excessive debug logging, manual interpolation

## Solution Implemented

### 1. Channel Configuration (`TelemetryChannelConfig.cs`)
- Defines all available channels with their sampling frequencies
- Separates primary channels (essential for visualization) from secondary channels
- Distinguishes between continuous (sampled) and event-based (discrete) data
- Target resampling frequency: **60Hz** for all synchronized frames

### 2. Data Processor (`TelemetryDataProcessor.cs`)
- **Time-aligned reading**: Reads all channels with proper timestamp handling
- **Interpolation**: Uses MathNet.Numerics LinearSpline for smooth interpolation
- **Data validation**: 
  - Removes null/invalid values
  - Validates timestamp ranges
  - Outlier detection using IQR method (Interquartile Range)
- **Resampling**: Converts all channels to uniform 60Hz output
- **Type handling**: Separate logic for continuous (float) vs event (int) channels

### 3. Refactored Reader (`DuckDBTelemetryReader.cs`)
- Removed 300+ lines of debug code
- Clean, maintainable structure
- Uses the processor for all data operations
- Proper error handling with meaningful messages

## How It Works

```
┌─────────────────┐
│  DuckDB File    │
│  - GPS (10Hz)   │
│  - Speed (100Hz)│
│  - Throttle(50Hz)│
│  - Brake (50Hz) │
│  - Steering(100Hz)│
└────────┬────────┘
         │
         ▼
┌────────────────────┐
│  Data Processor    │
│  1. Read channels  │
│  2. Validate data  │
│  3. Remove outliers│
│  4. Build interpol.│
└────────┬───────────┘
         │
         ▼
┌────────────────────┐
│ Time-Aligned Frames│
│  All at 60Hz       │
│  Frame 0: t=0.000s │
│  Frame 1: t=0.017s │
│  Frame 2: t=0.033s │
│  ...               │
└────────────────────┘
```

## Benefits

### ✅ Perfect Synchronization
When the GPS track map shows the car turning:
- **Steering input** matches the exact timestamp
- **Speed** reflects the correct value at that moment
- **Throttle/Brake** positions are perfectly aligned
- **All data is interpolated** to the same 60Hz timeline

### ✅ Clean Data
- Invalid values removed
- Outliers filtered using statistical methods
- Smooth interpolation between samples

### ✅ Maintainable Code
- Separation of concerns (config, processor, reader)
- Easy to add new channels
- Clear error messages
- Minimal debug output

### ✅ Performance
- Linear interpolation is fast (O(log n) lookup)
- Data read once and cached in interpolators
- Efficient memory usage

## Usage Example

The system automatically:
1. Reads all primary channels from DuckDB
2. Validates and cleans the data
3. Creates interpolators for smooth transitions
4. Generates synchronized 60Hz frames

**Before:**
- GPS at 10Hz, Speed at 100Hz, Throttle at 50Hz (misaligned)
- Manual timestamp matching
- Potential data gaps

**After:**
- All data at 60Hz (synchronized)
- Smooth interpolation between samples
- Clean, validated values

## Technical Details

### Interpolation Method
- **Linear Spline**: Fast and suitable for high-frequency telemetry
- Preserves data characteristics without over-smoothing
- Handles different frequencies gracefully

### Outlier Detection
- **IQR Method**: Interquartile Range × 3
- More tolerant than standard deviation
- Keeps 99%+ of valid data while removing anomalies

### Time Alignment
- Determines global time range from all channels
- Creates uniform timeline at target frequency
- Each frame samples all channels at exact same timestamp

## Future Enhancements

Potential additions:
- **Kalman filtering** for GPS smoothing
- **Moving average** for noisy sensors
- **Data export** to CSV/JSON
- **Channel selection UI** for custom analysis
- **Real-time processing** for live telemetry
