using System;
using System.IO;
using System.Text.Json;

namespace LMU_Telemetry.Properties
{
    internal sealed class Settings
    {
        private static Settings? _defaultInstance;
        private static readonly string SettingsPath;

        static Settings()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            SettingsPath = Path.Combine(appData, "LMU_Telemetry", "settings.json");
        }

        public static Settings Default
        {
            get
            {
                if (_defaultInstance == null)
                    _defaultInstance = Load();
                return _defaultInstance;
            }
        }

        // ---- Persisted properties ----
        public string LastTelemetryFolder { get; set; } = string.Empty;

        /// <summary>
        /// Developer mode flag. When true, the DEV MODE button is shown in the
        /// toolbar and the DevModeWindow is accessible. Not visible in normal use.
        /// Toggle via the Settings dialog (or Ctrl+Alt+D key combo).
        /// </summary>
        public bool IsDevModeEnabled { get; set; } = false;

        // Public constructor required for System.Text.Json deserialization.
        public Settings() { }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath)!;
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { /* non-critical */ }
        }

        private static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    // Try JSON format (new)
                    try
                    {
                        var s = JsonSerializer.Deserialize<Settings>(json);
                        if (s != null) return s;
                    }
                    catch
                    {
                        // Legacy: plain-text file contained only the folder path
                        var s = new Settings();
                        s.LastTelemetryFolder = json.Trim();
                        return s;
                    }
                }
            }
            catch { }
            return new Settings();
        }
    }
}
