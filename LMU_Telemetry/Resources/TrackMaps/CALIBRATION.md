# Track Map Calibration Guide

## The Problem

GPS coordinates (latitude/longitude) need to be aligned with your track map image. Without calibration, the racing line won't match the track.

## The Solution: Calibration Points

You need to identify **at least 2 known points** (3+ recommended) where you know both:
1. **GPS coordinates** (from your telemetry)
2. **Map pixel coordinates** (from your SVG/image)

## Step-by-Step Process

### 1. Place Your Track Map
Put `RaceCircuitSpa.svg` in `Resources/TrackMaps/` folder

### 2. Find Calibration Points in Your Telemetry

Load your DuckDB file and note the GPS coordinates at known track locations:
- Start/Finish line
- Eau Rouge apex
- Bus Stop chicane (or any other distinctive corner)

Example from telemetry:
```
Start/Finish: Lat=50.43722, Lon=5.97139
Eau Rouge:    Lat=50.43444, Lon=5.97778
Bus Stop:     Lat=50.43917, Lon=5.96500
```

### 3. Find the SAME Points on Your SVG Map

Open `RaceCircuitSpa.svg` in:
- Inkscape (free)
- Adobe Illustrator
- Or any SVG viewer with coordinate display

Hover over each known point and note the X,Y pixel coordinates:
```
Start/Finish: X=500, Y=300
Eau Rouge:    X=600, Y=500
Bus Stop:     X=400, Y=250
```

### 4. Update the Calibration File

Edit [Models/TrackCalibration.cs](../Models/TrackCalibration.cs) in the `GetSpaCalibration()` method:

```csharp
public static TrackCalibration GetSpaCalibration()
{
    var calibration = new TrackCalibration(
        referenceLat: 50.43722,  // Use Start/Finish as reference
        referenceLon: 5.97139
    );
    
    // Replace with YOUR actual values!
    calibration.AddCalibrationPoint(
        lat: 50.43722, lon: 5.97139,  // GPS from telemetry
        mapX: 500, mapY: 300,          // Pixels from SVG
        name: "Start/Finish"
    );
    
    calibration.AddCalibrationPoint(
        lat: 50.43444, lon: 5.97778,
        mapX: 600, mapY: 500,
        name: "Eau Rouge"
    );
    
    calibration.AddCalibrationPoint(
        lat: 50.43917, lon: 5.96500,
        mapX: 400, mapY: 250,
        name: "Bus Stop"
    );
    
    return calibration;
}
```

### 5. Rebuild and Test

```bash
dotnet build
```

The racing line should now align with the track map!

## How It Works

1. **Lat/Lon → Meters**: Converts curved earth coordinates to flat meters
2. **Affine Transform**: Computes scale, rotation, and translation from your calibration points
3. **Meters → Pixels**: Applies the transform to place telemetry exactly on the map

## Troubleshooting

**Racing line still doesn't match?**
- Your calibration points are wrong
- SVG coordinate system might be flipped (try negative Y values)
- You need more than 2 points for accuracy

**How to get better GPS coordinates from telemetry?**
- Find frames where the car is at known locations (e.g., crossing start/finish)
- Use the DuckDB reader to export specific lap sections
- Average multiple laps for more accurate reference points

**How accurate does the SVG need to be?**
- Doesn't need to be perfect
- Even hand-drawn track outlines work if you calibrate correctly
- What matters is identifying the SAME points in both systems

## For Other Tracks

To add calibration for other tracks, create a new method in `TrackCalibrations` class:

```csharp
public static TrackCalibration GetMonzaCalibration()
{
    var calibration = new TrackCalibration(
        referenceLat: 45.6156, 
        referenceLon: 9.2811
    );
    
    // Add your calibration points...
    
    return calibration;
}
```

Then update `LoadCalibrationForTrack()` in MainWindow.xaml.cs to load it.
