# OSM/Telem Corridor Pipeline

Technical documentation for the experimental track map generation pipeline introduced in the `experimental/osm-telem-corridor` branch.

---

## Overview

The game uses an internal pseudo-GPS coordinate system (Spa is placed at roughly 60°N, 0°E instead of its real 50.43°N, 5.97°E). OpenStreetMap uses real-world coordinates. This pipeline finds the rigid-body + scale transform between the two frames and uses it to build a physically accurate track map expressed in the game's own coordinate system.

---

## Directory structure

```
LMU_Telemetry/
├── Reference/                  # OSM polylines for each circuit (input)
│   ├── spa_osm_stitched.json
│   ├── monza_stitched.json
│   └── ...
├── TrackMaps/                  # Generated corridor JSONs (output, committed)
│   └── Circuit de Spa-Francorchamps.json
└── scripts/
    ├── track_registry.py       # Maps LMU track names → OSM filenames
    ├── build_corridor.py       # Single-track pipeline (CLI)
    ├── build_all_corridors.py  # Batch runner for all available telemetry
    └── build_spa_corridor.py   # Original Spa-specific script (kept for reference)
```

---

## Algorithm

### 1. Arc-offset detection

The OSM polyline starts at an arbitrary point on the circuit — not at the game's lap distance zero (start/finish line). Before alignment can be solved, we need to know the arc offset: how many metres into the OSM polyline the game's lap starts.

A coarse scan (100 m steps) followed by a fine scan (25 m steps, ±200 m around the coarse best) tests every candidate offset by:

- Pairing 30–70 (OSM arc, telem LapDist) point pairs using `telem_LD = (OSM_arc + offset) % total`
- Solving a Procrustes similarity transform for each candidate
- Scoring by mean distance from the transformed OSM to the nearest telemetry GPS point

The offset that minimises the mean distance wins.

### 2. Procrustes similarity transform

With the correct arc offset, 70 evenly spaced point pairs are built and the Kabsch/Procrustes algorithm finds the optimal 2D similarity transform:

```
T(p) = s · R · p + t
```

Where `s` is a uniform scale factor (expected ≈ 1.0 since both frames use metres), `R` is a 2×2 rotation matrix, and `t` is a translation vector. The SVD of the cross-covariance matrix is used with a determinant sign check to prevent reflections.

Spa result: scale = 1.000245, rotation = 2.59°, mean alignment = 3.1 m.

### 3. Resampling and curvature

The aligned OSM is resampled to uniform 3 m arc-length spacing. Heading and curvature are computed at every point using central differences:

- **Heading**: `atan2(y[i+1] − y[i−1], x[i+1] − x[i−1])` (matches the C# formula)
- **Curvature**: `|x′y″ − y′x″| / (x′² + y′²)^1.5` (unsigned, 1/m)

Because OSM has ~21 m point spacing, the raw parametric curvature is zero within each linear segment and concentrated in narrow spikes at original vertices. A Gaussian filter (σ = 20 m, wrap mode) spreads these into a smooth, physically realistic profile.

### 4. Width estimation

Each telemetry GPS point is converted from LapDist to OSM arc position:

```
osm_arc = (LapDist − arc_offset) % total_arc
```

The point is projected onto the nearest OSM centreline segment. The signed lateral offset (positive = left of centreline, negative = right) is accumulated into 2 m arc-length bins. The bin maximum becomes `LeftWidth` and the negated bin minimum becomes `RightWidth`. Bins where the car never crossed to one side are set to null.

Width coverage improves with more laps. With a single partial lap, roughly 30–60% of bins have data for each side; linear interpolation fills the gaps for all output points.

---

## Output schema

Each `TrackMaps/*.json` extends the existing schema with two new nullable fields:

```json
{
  "TrackName": "Circuit de Spa-Francorchamps",
  "TotalLength": 7004.4,
  "GeneratedDateTime": "2026-01-28T...",
  "GeneratedFromLapCount": 1,
  "ArcOffset": 2300.0,
  "Scale": 1.000245,
  "MeanAlignmentM": 3.1,
  "Points": [
    {
      "Position": { "X": 969.53, "Y": -1474.83 },
      "Heading": 1.234,
      "Curvature": 0.00412,
      "LeftWidth": 8.3,
      "RightWidth": 6.1
    }
  ]
}
```

`LeftWidth` and `RightWidth` are `double?` in C# — null where no tyre-track data exists for that side.

---

## Running manually

### Prerequisites

```
pip install numpy scipy duckdb
```

### Single track

```bash
python build_corridor.py path/to/telemetry.duckdb
# OSM and output path auto-detected from track_registry.py

# Override explicitly:
python build_corridor.py path/to/telemetry.duckdb \
    --osm ../Reference/monza_stitched.json \
    --output ../TrackMaps/Monza.json \
    --track-name "Autodromo Nazionale Monza"
```

### All tracks at once

```bash
python build_all_corridors.py --telem-dir "C:/Users/User/Downloads/Telemetry"
# Processes every .duckdb in the folder that has a registry entry
# Reports what's ready and what's still waiting for telemetry
```

---

## Adding a new track

1. **Get the OSM file** — a closed-loop `points_local_xy_m` JSON in the same format as the existing Reference files.

2. **Copy it** to `LMU_Telemetry/Reference/yourtrack_stitched.json`.

3. **Find the exact LMU track name** by recording any session at that track, then reading its metadata:

   ```bash
   python -c "
   import duckdb
   c = duckdb.connect(r'path/to/your.duckdb', read_only=True)
   print(c.execute(\"SELECT value FROM metadata WHERE key='TrackName'\").fetchone())
   "
   ```

4. **Add the mapping** to `track_registry.py`:

   ```python
   "Exact LMU Track Name": "yourtrack_stitched.json",
   ```

5. The next time you open telemetry for that track in the app, the map generates automatically.

---

## In-app integration

`MainWindow.xaml.cs` calls `TryAutoGenerateCorridorAsync()` whenever a telemetry file is opened and no pre-built map exists for that track. It:

- Finds the Python executable on PATH (`python`, `python3`, or `py`)
- Locates `build_corridor.py` relative to the project root
- Runs the pipeline in a background task, logging key progress lines
- Reloads and renders the map when complete — no user interaction needed

If Python is not installed, or the track has no OSM entry in the registry, the existing manual "Generate Track Map" button is shown as a fallback.
