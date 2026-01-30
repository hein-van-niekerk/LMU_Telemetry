namespace LMU_Telemetry.Properties
{
    internal sealed class Settings
    {
        private static Settings? _defaultInstance;

        public static Settings Default
        {
            get
            {
                if (_defaultInstance == null)
                {
                    _defaultInstance = new Settings();
                }
                return _defaultInstance;
            }
        }

        public string LastTelemetryFolder { get; set; } = string.Empty;

        public void Save()
        {
            // Persist to user settings file
            var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            var settingsPath = System.IO.Path.Combine(appData, "LMU_Telemetry", "settings.txt");
            
            try
            {
                var directory = System.IO.Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }
                
                System.IO.File.WriteAllText(settingsPath, LastTelemetryFolder);
            }
            catch { }
        }

        private Settings()
        {
            // Load from user settings file
            var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            var settingsPath = System.IO.Path.Combine(appData, "LMU_Telemetry", "settings.txt");
            
            try
            {
                if (System.IO.File.Exists(settingsPath))
                {
                    LastTelemetryFolder = System.IO.File.ReadAllText(settingsPath);
                }
            }
            catch { }
        }
    }
}
