using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LMU_Telemetry.Models;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using UserControl = System.Windows.Controls.UserControl;

namespace LMU_Telemetry.Views
{
    public partial class DetailedDataView : UserControl
    {
        // ── build-once flag + value-TextBlock registry ─────────────────────────
        private bool _built = false;
        private readonly Dictionary<string, TextBlock> _vals = new();

        // Summary stats — recomputed when frame count changes
        private int _summaryFrameCount = -1;

        public DetailedDataView()
        {
            InitializeComponent();
        }

        // Called every frame from UpdateTelemetryDisplay in MainWindow.
        public void PushFrame(TelemetryFrame frame, IReadOnlyList<TelemetryFrame> allFrames)
        {
            if (frame == null) return;

            if (!_built)
            {
                BuildChannelsPanel(ChannelsPanel);
                BuildEventsPanel(EventsPanel);
                BuildSummaryPanel(SummaryPanel);
                _built = true;
            }

            UpdateChannels(frame);
            UpdateEvents(frame);
            UpdateSummary(frame, allFrames);
        }

        // =========================================================================
        // CHANNELS tab — live sensor readings
        // =========================================================================

        private void BuildChannelsPanel(StackPanel panel)
        {
            panel.Children.Clear();

            AddSection(panel, "ENGINE");
            AddRow(panel, "RPM",         "rpm",         "#569CD6");
            AddRow(panel, "Max RPM",     "rpm_max",     "#3A6A9A");
            AddRow(panel, "Oil Temp",    "oil_temp",    "#FFD700");
            AddRow(panel, "Water Temp",  "water_temp",  "#4EC9B0");
            AddRow(panel, "Fuel",        "fuel",        "#CE9178");

            AddSection(panel, "INPUTS");
            AddRowWithBar(panel, "Throttle", "throttle",  Color.FromRgb(0, 200, 83));
            AddRowWithBar(panel, "Brake",    "brake",     Color.FromRgb(244, 67, 54));
            AddRow(panel, "Clutch",   "clutch",   "#888888");
            AddRow(panel, "Steering", "steering", "#00B8D4");

            AddSection(panel, "MOTION");
            AddRow(panel, "G Lat",    "g_lat",  "#FF8C00");
            AddRow(panel, "G Long",   "g_long", "#FFC864");
            AddRow(panel, "G Vert",   "g_vert", "#C8A040");
            AddRow(panel, "Yaw Rate", "yaw",    "#A0C0E0");

            AddSection(panel, "TYRES");
            AddRow(panel, "Temp FL",   "tyre_t_fl",  "#FF6060");
            AddRow(panel, "Temp FR",   "tyre_t_fr",  "#FF6060");
            AddRow(panel, "Temp RL",   "tyre_t_rl",  "#FF9040");
            AddRow(panel, "Temp RR",   "tyre_t_rr",  "#FF9040");
            AddRow(panel, "Press FL",  "tyre_p_fl",  "#80C0FF");
            AddRow(panel, "Press FR",  "tyre_p_fr",  "#80C0FF");

            AddSection(panel, "ENVIRONMENT");
            AddRow(panel, "Air Temp",    "air_temp",   "#80CCFF");
            AddRow(panel, "Track Temp",  "track_temp", "#FF9060");
            AddRow(panel, "Wind Speed",  "wind_spd",   "#A0A0C0");
            AddRow(panel, "Wind Dir",    "wind_dir",   "#A0A0C0");
        }

        private void UpdateChannels(TelemetryFrame f)
        {
            Set("rpm",       f.Rpm > 0          ? $"{f.Rpm:F0}" : "—");
            Set("rpm_max",   f.RpmMax > 0        ? $"{f.RpmMax:F0}" : "—");
            Set("oil_temp",  f.EngineOilTemp > 0 ? $"{f.EngineOilTemp:F1} °C" : "—");
            Set("water_temp",f.EngineWaterTemp > 0 ? $"{f.EngineWaterTemp:F1} °C" : "—");
            Set("fuel",      f.Fuel >= 0         ? $"{f.Fuel:F1} L" : "—");

            Set("throttle", $"{f.Throttle * 100:F0}%");
            Set("brake",    $"{f.Brake * 100:F0}%");
            Set("clutch",   $"{f.Clutch * 100:F0}%");
            Set("steering", $"{f.Steering:+0.000;-0.000;0.000}");

            SetExt(f, "g_lat",  "G Force Lat",  v => $"{v:+0.00;-0.00} G");
            SetExt(f, "g_long", "G Force Long", v => $"{v:+0.00;-0.00} G");
            SetExt(f, "g_vert", "G Force Vert", v => $"{v:+0.00;-0.00} G");
            SetExt(f, "yaw",    "Yaw Rate",     v => $"{v:F2} °/s");

            SetExtIdx(f, "tyre_t_fl", "TyresTempCentre", 0, v => $"{v:F1} °C");
            SetExtIdx(f, "tyre_t_fr", "TyresTempCentre", 1, v => $"{v:F1} °C");
            SetExtIdx(f, "tyre_t_rl", "TyresTempCentre", 2, v => $"{v:F1} °C");
            SetExtIdx(f, "tyre_t_rr", "TyresTempCentre", 3, v => $"{v:F1} °C");
            SetExtIdx(f, "tyre_p_fl", "TyresPressure",   0, v => $"{v:F1} kPa");
            SetExtIdx(f, "tyre_p_fr", "TyresPressure",   1, v => $"{v:F1} kPa");

            SetExt(f, "air_temp",   "Ambient Temperature", v => $"{v:F1} °C");
            SetExt(f, "track_temp", "Track Temperature",   v => $"{v:F1} °C");
            SetExt(f, "wind_spd",   "Wind Speed",          v => $"{v:F1} m/s");
            SetExt(f, "wind_dir",   "Wind Heading",        v => $"{v:F0} °");
        }

        // =========================================================================
        // EVENTS tab — driver aids, timing, status
        // =========================================================================

        private void BuildEventsPanel(StackPanel panel)
        {
            panel.Children.Clear();

            AddSection(panel, "SAFETY SYSTEMS");
            AddRow(panel, "ABS",           "abs",    "#FF6060");
            AddRow(panel, "TC",            "tc",     "#FF9040");
            AddRow(panel, "TC Level",      "tc_lvl", "#FFC060");
            AddRow(panel, "Launch Ctrl",   "lc",     "#60C060");

            AddSection(panel, "TIMING");
            AddRow(panel, "Lap Time",   "lap_time",  "#CCCCCC");
            AddRow(panel, "Last Lap",   "last_lap",  "#888888");
            AddRow(panel, "Best Lap",   "best_lap",  "#F0C040");
            AddRow(panel, "Sector 1",   "sect1",     "#C586C0");
            AddRow(panel, "Sector 2",   "sect2",     "#C586C0");

            AddSection(panel, "STATUS");
            AddRow(panel, "Lap",          "lap_num",  "#CCCCCC");
            AddRow(panel, "Sector",       "sector",   "#CCCCCC");
            AddRow(panel, "Lap Dist",     "lap_dist", "#888888");
            AddRow(panel, "In Pits",      "in_pits",  "#60C0FF");
            AddRow(panel, "Speed Lim",    "spd_lim",  "#FF9040");
        }

        private void UpdateEvents(TelemetryFrame f)
        {
            // Safety systems — from ExtendedData; show ON/OFF or level
            SetExtOnOff(f, "abs",    "ABS");
            SetExtOnOff(f, "tc",     "TC");
            SetExt(f, "tc_lvl", "TCLevel",            v => v > 0 ? $"Level {v:F0}" : "OFF");
            SetExtOnOff(f, "lc",     "LaunchControlActive");

            // Timing
            Set("lap_time",  f.LapTime > 0         ? FmtTime(f.LapTime)   : "—");
            Set("last_lap",  f.LastLapTime > 0      ? FmtTime(f.LastLapTime) : "—");
            Set("best_lap",  f.BestLapTime > 0      ? FmtTime(f.BestLapTime) : "—");
            SetExt(f, "sect1", "Current Sector1", v => v > 0 ? FmtTime((float)v) : "—");
            SetExt(f, "sect2", "Current Sector2", v => v > 0 ? FmtTime((float)v) : "—");

            // Status
            Set("lap_num",  (f.CurrentLap + 1).ToString());
            Set("sector",   (f.Sector + 1).ToString());
            Set("lap_dist", f.LapDistance > 0 ? $"{f.LapDistance:F0} m" : "—");
            SetExtOnOff(f, "in_pits", "In Pits");
            SetExtOnOff(f, "spd_lim", "Speed Limiter");
        }

        // =========================================================================
        // SUMMARY tab — session statistics computed from all frames
        // =========================================================================

        private void BuildSummaryPanel(StackPanel panel)
        {
            panel.Children.Clear();

            AddSection(panel, "SESSION");
            AddRow(panel, "Frames",   "s_frames",   "#888888");
            AddRow(panel, "Duration", "s_duration", "#888888");

            AddSection(panel, "SPEED");
            AddRow(panel, "Max Speed",  "s_maxspd",  "#E0E0E0");
            AddRow(panel, "Avg Speed",  "s_avgspd",  "#888888");

            AddSection(panel, "ENGINE");
            AddRow(panel, "Max RPM",    "s_maxrpm",  "#569CD6");

            AddSection(panel, "G-FORCE");
            AddRow(panel, "Max Lat G",   "s_max_lat",  "#FF8C00");
            AddRow(panel, "Max Brake G", "s_max_brk",  "#F44336");

            AddSection(panel, "LAPS");
            AddRow(panel, "Best Lap",    "s_best",  "#F0C040");
            AddRow(panel, "Last Lap",    "s_last",  "#CCCCCC");
            AddRow(panel, "Avg Lap",     "s_avg",   "#888888");
        }

        private void UpdateSummary(TelemetryFrame frame, IReadOnlyList<TelemetryFrame> allFrames)
        {
            if (allFrames == null || allFrames.Count == 0) return;

            // Guard: only recompute when frame count changes
            if (allFrames.Count == _summaryFrameCount)
            {
                // Still update lap times from cached computation
                return;
            }
            _summaryFrameCount = allFrames.Count;

            double duration = allFrames[^1].Time - allFrames[0].Time;
            Set("s_frames",   $"{allFrames.Count:N0}");
            Set("s_duration", FmtTime((float)duration));

            float maxSpd = allFrames.Max(f => f.Speed);
            float avgSpd = allFrames.Count > 0 ? allFrames.Average(f => f.Speed) : 0;
            Set("s_maxspd", $"{maxSpd:F0} km/h");
            Set("s_avgspd", $"{avgSpd:F0} km/h");

            float maxRpm = allFrames.Max(f => f.Rpm);
            Set("s_maxrpm", $"{maxRpm:F0}");

            // G-force from ExtendedData
            var latGs  = allFrames.Select(f => ExtDouble(f, "G Force Lat")).Where(v => v.HasValue).Select(v => Math.Abs(v!.Value)).ToList();
            var longGs = allFrames.Select(f => ExtDouble(f, "G Force Long")).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            Set("s_max_lat", latGs.Count  > 0 ? $"{latGs.Max():F2} G"  : "—");
            Set("s_max_brk", longGs.Count > 0 ? $"{longGs.Min():F2} G" : "—");

            // Lap time stats
            var lapBounds = new Dictionary<int, (double first, double last)>();
            foreach (var f in allFrames)
            {
                int lap = f.CurrentLap;
                if (!lapBounds.TryGetValue(lap, out var b)) lapBounds[lap] = (f.Time, f.Time);
                else if (f.Time > b.last) lapBounds[lap] = (b.first, f.Time);
            }
            var sortedLaps = lapBounds.OrderBy(kv => kv.Key).ToList();
            var lapTimes = new List<float>();
            for (int i = 0; i < sortedLaps.Count - 1; i++)
            {
                var (first, last) = sortedLaps[i].Value;
                float dur = (float)(last - first);
                if (dur > 10f && dur < 600f) lapTimes.Add(dur);
            }
            Set("s_best", lapTimes.Count > 0 ? FmtTime(lapTimes.Min())        : "—");
            Set("s_last", lapTimes.Count > 0 ? FmtTime(lapTimes[^1])          : "—");
            Set("s_avg",  lapTimes.Count > 0 ? FmtTime(lapTimes.Average())    : "—");
        }

        // =========================================================================
        // XAML builder helpers
        // =========================================================================

        private void AddSection(StackPanel panel, string title)
        {
            // Section header with accent bar
            var grid = new Grid { Margin = new Thickness(0, 10, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bar = new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                Width = 3,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(bar, 0);
            grid.Children.Add(bar);

            var lbl = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                FontSize = 9, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 1);
            grid.Children.Add(lbl);

            panel.Children.Add(grid);
        }

        private void AddRow(StackPanel panel, string label, string key, string colorHex)
        {
            var color = HexColor(colorHex);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nameText = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                FontSize = 10, FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(12, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameText, 0);
            row.Children.Add(nameText);

            var valText = new TextBlock
            {
                Text = "—",
                Foreground = new SolidColorBrush(color),
                FontSize = 11, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(4, 2, 12, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valText, 1);
            row.Children.Add(valText);

            _vals[key] = valText;
            panel.Children.Add(row);
        }

        private void AddRowWithBar(StackPanel panel, string label, string key, Color barColor)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nameText = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                FontSize = 10, FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(12, 2, 4, 0)
            };
            Grid.SetColumn(nameText, 0);
            row.Children.Add(nameText);

            var valText = new TextBlock
            {
                Text = "—",
                Foreground = new SolidColorBrush(barColor),
                FontSize = 11, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(4, 2, 12, 0)
            };
            Grid.SetColumn(valText, 1);
            row.Children.Add(valText);

            _vals[key] = valText;
            panel.Children.Add(row);
        }

        // =========================================================================
        // Value update helpers
        // =========================================================================

        private void Set(string key, string val)
        {
            if (_vals.TryGetValue(key, out var tb)) tb.Text = val;
        }

        private void SetExt(TelemetryFrame f, string key, string extKey, Func<double, string> fmt)
        {
            var v = ExtDouble(f, extKey);
            Set(key, v.HasValue ? fmt(v.Value) : "—");
        }

        private void SetExtIdx(TelemetryFrame f, string key, string extKey, int idx, Func<double, string> fmt)
        {
            if (f.ExtendedData.TryGetValue(extKey, out var raw) && raw is double[] arr && idx < arr.Length)
                Set(key, fmt(arr[idx]));
            else
                Set(key, "—");
        }

        private void SetExtOnOff(TelemetryFrame f, string key, string extKey)
        {
            var v = ExtDouble(f, extKey);
            Set(key, v.HasValue ? (v.Value > 0.5 ? "ON" : "OFF") : "—");
        }

        private static double? ExtDouble(TelemetryFrame f, string key)
        {
            if (f.ExtendedData.TryGetValue(key, out var raw) && raw != null)
            {
                if (raw is double d) return d;
                if (raw is float  fv) return fv;
                if (double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                                    NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
            }
            return null;
        }

        private static string FmtTime(float secs)
        {
            if (secs <= 0) return "—";
            var ts = TimeSpan.FromSeconds(secs);
            return ts.TotalMinutes >= 1
                ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}.{ts.Milliseconds:D3}"
                : $"{ts.Seconds}.{ts.Milliseconds:D3}";
        }

        private static Color HexColor(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                return Color.FromRgb(
                    Convert.ToByte(hex[0..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16));
            }
            catch { return Color.FromRgb(200, 200, 200); }
        }
    }
}
