using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Point = System.Windows.Point;

namespace LMU_Telemetry.Models;

/// <summary>
/// Handles persistence of generated track maps to/from JSON files.
/// Stores maps in the project's TrackMaps directory for permanent reference.
/// </summary>
public static class TrackMapStorage
{
    private static readonly string StorageDirectory;

    static TrackMapStorage()
    {
        // Store in project directory: {ProjectRoot}/TrackMaps/
        // Get the application's base directory
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        
        // Navigate up to project root (from bin/Debug/net8.0-windows/)
        var projectRoot = Directory.GetParent(appDir)?.Parent?.Parent?.Parent?.FullName;
        
        if (projectRoot != null)
        {
            StorageDirectory = System.IO.Path.Combine(projectRoot, "TrackMaps");
        }
        else
        {
            // Fallback to AppData if we can't find project root
            StorageDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                "LMU_Telemetry", 
                "TrackMaps");
        }
        
        // Ensure storage directory exists
        Directory.CreateDirectory(StorageDirectory);
    }
    
    /// <summary>
    /// Get the storage directory path for external reference.
    /// </summary>
    public static string GetStorageDirectory()
    {
        return StorageDirectory;
    }

    /// <summary>
    /// Save a generated track map to JSON file.
    /// </summary>
    public static void Save(GeneratedTrackMap trackMap, string trackName)
    {
        if (trackMap == null)
            throw new ArgumentNullException(nameof(trackMap));

        trackMap.TrackName = trackName;
        string filePath = GetTrackMapPath(trackName);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new PointJsonConverter() }
        };

        string json = JsonSerializer.Serialize(trackMap, options);
        File.WriteAllText(filePath, json);

        System.Diagnostics.Debug.WriteLine($"Saved track map to: {filePath}");
    }

    /// <summary>
    /// Load a track map from JSON file.
    /// </summary>
    public static GeneratedTrackMap? Load(string trackName)
    {
        string filePath = GetTrackMapPath(trackName);
        
        if (!File.Exists(filePath))
        {
            // Fallback to legacy location (bin/TrackMaps) for older generated maps
            string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrackMaps", Path.GetFileName(filePath));
            if (File.Exists(legacyPath))
            {
                filePath = legacyPath;
            }
            else
            {
                return null;
            }
        }

        try
        {
            string json = File.ReadAllText(filePath);
            
            var options = new JsonSerializerOptions
            {
                Converters = { new PointJsonConverter() }
            };

            var trackMap = JsonSerializer.Deserialize<GeneratedTrackMap>(json, options);
            System.Diagnostics.Debug.WriteLine($"Loaded track map from: {filePath}");
            
            return trackMap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load track map: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check if a track map exists for the given track name.
    /// </summary>
    public static bool Exists(string trackName)
    {
        return File.Exists(GetTrackMapPath(trackName));
    }

    /// <summary>
    /// Delete a saved track map.
    /// </summary>
    public static void Delete(string trackName)
    {
        string filePath = GetTrackMapPath(trackName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            System.Diagnostics.Debug.WriteLine($"Deleted track map: {filePath}");
        }
    }

    /// <summary>
    /// Get all saved track names.
    /// </summary>
    public static List<string> GetAllTrackNames()
    {
        var trackNames = new List<string>();
        
        foreach (var file in Directory.GetFiles(StorageDirectory, "*.json"))
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            trackNames.Add(fileName);
        }

        return trackNames;
    }

    /// <summary>
    /// Get file path for a track map.
    /// </summary>
    private static string GetTrackMapPath(string trackName)
    {
        // Sanitize track name for file system
        string safeTrackName = string.Join("_", trackName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(StorageDirectory, $"{safeTrackName}.json");
    }
}

/// <summary>
/// Custom JSON converter for System.Windows.Point.
/// </summary>
public class PointJsonConverter : JsonConverter<Point>
{
    public override Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        double x = 0, y = 0;
        
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return new Point(x, y);

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string propertyName = reader.GetString() ?? "";
                reader.Read();
                
                if (propertyName == "X")
                    x = reader.GetDouble();
                else if (propertyName == "Y")
                    y = reader.GetDouble();
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("X", value.X);
        writer.WriteNumber("Y", value.Y);
        writer.WriteEndObject();
    }
}
