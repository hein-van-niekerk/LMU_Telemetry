using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LMU_Telemetry.Models;

/// <summary>
/// Persists raw lap recordings to {AppData}/LMU_Telemetry/RawLaps/{TrackKey}/.
/// Files are never deleted automatically — kept for map re-generation with
/// improved algorithms later.
/// </summary>
public static class RawLapStorage
{
    private static readonly string RootDirectory;

    static RawLapStorage()
    {
        RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LMU_Telemetry",
            "RawLaps");
        Directory.CreateDirectory(RootDirectory);
    }

    // ------------------------------------------------------------------
    // Queries
    // ------------------------------------------------------------------

    /// <summary>All track keys that have at least one saved lap.</summary>
    public static IReadOnlyList<string> GetAllTrackKeys()
    {
        var keys = new List<string>();
        if (!Directory.Exists(RootDirectory)) return keys;
        foreach (var dir in Directory.GetDirectories(RootDirectory))
            keys.Add(Path.GetFileName(dir)!);
        return keys;
    }

    /// <summary>Load all saved laps for a track key, newest first.</summary>
    public static List<RawLapData> LoadAll(string trackKey)
    {
        var laps = new List<RawLapData>();
        string dir = TrackDirectory(trackKey);
        if (!Directory.Exists(dir)) return laps;

        var options = JsonOptions();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var lap = JsonSerializer.Deserialize<RawLapData>(json, options);
                if (lap != null)
                {
                    lap.FileName = Path.GetFileName(file);
                    laps.Add(lap);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RawLapStorage] Failed to load {file}: {ex.Message}");
            }
        }

        laps.Sort((a, b) => b.RecordedAt.CompareTo(a.RecordedAt));
        return laps;
    }

    // ------------------------------------------------------------------
    // Mutations
    // ------------------------------------------------------------------

    /// <summary>
    /// Save a lap to disk.  Returns the file name that was written.
    /// </summary>
    public static string Save(RawLapData lap)
    {
        string dir = TrackDirectory(lap.TrackKey);
        Directory.CreateDirectory(dir);

        string safeName = $"{lap.RecordedAt:yyyyMMdd_HHmmss}_lap{lap.LapNumber}.json";
        string path = Path.Combine(dir, safeName);

        string json = JsonSerializer.Serialize(lap, JsonOptions());
        File.WriteAllText(path, json);

        lap.FileName = safeName;
        System.Diagnostics.Debug.WriteLine($"[RawLapStorage] Saved lap → {path}");
        return safeName;
    }

    /// <summary>Delete one lap file.  <paramref name="lap"/> must have FileName set.</summary>
    public static void Delete(RawLapData lap)
    {
        if (string.IsNullOrEmpty(lap.FileName)) return;
        string path = Path.Combine(TrackDirectory(lap.TrackKey), lap.FileName);
        if (File.Exists(path))
        {
            File.Delete(path);
            System.Diagnostics.Debug.WriteLine($"[RawLapStorage] Deleted {path}");
        }
    }

    /// <summary>Delete ALL laps for a track key (irreversible).</summary>
    public static void DeleteAll(string trackKey)
    {
        string dir = TrackDirectory(trackKey);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string TrackDirectory(string trackKey)
    {
        // Sanitize key for use as a directory name
        string safe = string.Join("_", trackKey.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(RootDirectory, safe);
    }

    private static JsonSerializerOptions JsonOptions() =>
        new JsonSerializerOptions { WriteIndented = false };
}
