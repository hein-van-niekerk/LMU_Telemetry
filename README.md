# LMU Telemetry

A Windows desktop app for analysing driving telemetry from **Le Mans Ultimate**. It replays recorded laps, shows where on track you apply throttle, brake and steering, and includes a developer tool for building track maps from your own laps.

This is a passion project and a work in progress. See [Status and roadmap](#status-and-roadmap) for what works and what doesn't yet.

Built with C#, WPF (.NET 8), view models for the newer parts of the UI, and DuckDB for reading and writing telemetry files.

![Analysis view showing a Spa-Francorchamps recording](docs/screenshot.png)

## Features

**Telemetry sources**
- **Recorded sessions:** pick a folder of `.duckdb` telemetry files. The app lists each recording with its track and car, then resamples the channels onto a common 60 Hz timeline.
- **Live telemetry:** reads the rFactor 2-style shared-memory map exposed by the game. The player's car is identified through the scoring data, because the order of the telemetry array is not guaranteed.
- **Save and load:** the rolling buffer can be saved to a `.duckdb` file and loaded again later.

**Analysis view**
- Playback with play/pause, 2x and 4x speed, previous/next lap and a scrub timeline.
- A 2D track view with the line coloured by driver input: red under braking, green on throttle and grey when coasting. Sector boundaries (S1, S2) are marked on the map. Click the map to jump to that moment, and zoom or pan to inspect a corner.
- A header with gear, RPM, speed, throttle and brake bars, and the current lap and sector with best and last lap times.
- A side panel with a steering wheel display (steering angle, throttle, brake), session details and a per-lap table of sector and lap times.
- Throttle, brake, speed and steering graphs along the bottom, synchronised with the playhead, plus a detailed data view.
- A mock telemetry generator for testing without the game (`_useMockData` in `MainWindow.xaml.cs`).

## Track map Dev Mode

Track maps are generated from laps you drive yourself, not from a picture of the circuit. Open Dev Mode with the **DEV** button, or with `Ctrl+Alt+D`. The button is shown in Debug builds and hidden in Release builds unless `IsDevModeEnabled` is set in the settings file.

1. **Record.** Capture laps live, or load a `.duckdb` recording and extract its laps. Each lap is checked automatically and flagged if it has too few samples, is an out-lap or partial lap, has a position jump, or has an implausible speed change. You decide which laps to keep.
2. **Generate and preview.** The generator resamples the kept laps to uniform spacing, then refines the centerline over a few passes. In each pass it measures every lap's sideways offset from the centerline and moves the centerline to the midpoint of the minimum and maximum offsets. The same measurement gives left and right track edges and width.
3. **Merge.** A candidate map can be aligned onto an existing one with iterative closest point (a 2D rigid fit), and the result reports where the two maps diverge. You can keep the existing centerline and attach the new width data, or use the new centerline.
4. **Library.** Save, import (JSON) and delete maps per track. Raw laps are kept on disk so maps can be regenerated later with a better algorithm.

The width values reflect how far apart your recorded laps are. Laps that stay on the racing line give a narrower track than the real one, so include laps that touch the kerbs if you need accurate edges.

The track name comes from the telemetry itself: the file's metadata for recordings and the game's track name for live sessions. Each name is used as the key for stored laps and maps.

### An earlier approach

The first attempt converted GPS points to metres, averaged several laps into a smooth centerline spline, and expressed every telemetry point as distance along that line plus a sideways offset. It proved too unreliable, so map generation now works from curated raw laps as described above.

## Getting started

**Requirements:** Windows 10/11 and the .NET 8 SDK. Le Mans Ultimate is only needed for live telemetry.

```powershell
git clone https://github.com/hein-van-niekerk/LMU_Telemetry.git
cd LMU_Telemetry/LMU_Telemetry
dotnet build
dotnet run
```

On launch, choose the folder that holds your `.duckdb` recordings and pick a session. For live telemetry, start a session in the game first so the shared-memory map exists.

**Where data is stored**
- Settings: `%APPDATA%\LMU_Telemetry\settings.json`
- Raw laps for map generation: `%APPDATA%\LMU_Telemetry\RawLaps\<track>\`
- Generated and imported track maps: the `TrackMaps` folder next to the project (falls back to `%APPDATA%` if it can't be found)

**File format.** Recordings are DuckDB files with one table per channel (`ts` and `value` columns) plus a `metadata` table of key/value pairs such as `TrackName` and `CarName`.

## Project structure

```
LMU_Telemetry/
  Telemetry/    Shared-memory reader, rFactor 2 struct layout, rolling buffer
  Services/     DuckDB reader/writer, channel processing and resampling, lap recorder
  Models/       Telemetry frames, track map generation, alignment, calibration, storage
  ViewModels/   Main, input, detailed data and Dev Mode view models
  Views/        WPF windows and controls, including Dev Mode and the detail view
  Rendering/    Canvas rendering for the track map and input graphs
  TrackMaps/    Generated and imported map files
```

## Status and roadmap

**Works:** playback and scrubbing of recorded sessions, live shared-memory reading, the analysis view, and the Dev Mode workflow for recording, generating, merging and storing maps.

**Still to do:**
- Record and calibrate a clean reference set of laps for each circuit. Only Spa-Francorchamps has a map in the repository so far.
- Split up `MainWindow.xaml.cs`, which still holds most of the playback and rendering logic.
- Add automated tests, starting with the pure functions for alignment, map generation and lap detection.
- Replace the remaining silent error handling with proper logging.
- Store long-term data in a database instead of reading it only from DuckDB files.
- Longer term: an AI coaching and car setup assistant.
