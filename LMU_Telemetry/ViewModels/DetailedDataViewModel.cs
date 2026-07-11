using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LMU.Telemetry.Core.Models;

namespace LMU_Telemetry.ViewModels
{
    /// <summary>
    /// ViewModel for detailed telemetry data display.
    /// Displays channels and events from the telemetry configuration.
    /// </summary>
    public class DetailedDataViewModel : ObservableObject
    {
        private readonly TelemetryFrame? _currentFrame;
        private readonly List<List<TelemetryFrame>>? _allFrames;

        public DetailedDataViewModel(TelemetryFrame? currentFrame = null, List<List<TelemetryFrame>>? allFrames = null)
        {
            _currentFrame = currentFrame;
            _allFrames = allFrames ?? new List<List<TelemetryFrame>>();
        }

        /// <summary>
        /// Build UI elements for channels tab
        /// </summary>
        public void BuildChannelsPanel(StackPanel panel)
        {
            if (panel == null) return;
            panel.Children.Clear();

            var channels = new List<(string Name, double Frequency)>
            {
                ("Ambient Temperature", 1),
                ("Brake Pos", 50),
                ("Brake Pos Unfiltered", 50),
                ("Brake Thickness", 10),
                ("Brakes Air Temp", 50),
                ("Brakes Force", 50),
                ("Brakes Temp", 50),
                ("Clutch Pos", 50),
                ("Clutch Pos Unfiltered", 50),
                ("Clutch RPM", 100),
                ("Drag", 100),
                ("Engine Oil Temp", 7),
                ("Engine RPM", 100),
                ("Engine Water Temp", 7),
                ("FFB Output", 100),
                ("Front3rdDeflection", 100),
                ("FrontDownForce", 100),
                ("FrontRideHeight", 100),
                ("FrontWingHeight", 100),
                ("Fuel Level", 20),
                ("G Force Lat", 10),
                ("G Force Long", 10),
                ("G Force Vert", 10),
                ("GPS Latitude", 10),
                ("GPS Longitude", 10),
                ("GPS Speed", 10),
                ("GPS Time", 100),
                ("Ground Speed", 100),
                ("Lap Dist", 10),
                ("Lateral Acceleration", 100),
                ("Longitudinal Acceleration", 100),
                ("OverheatingState", 2),
                ("Path Lateral", 10),
                ("ReadDownForce", 100),
                ("Rear3rdDeflection", 100),
                ("RearRideHeight", 100),
                ("Regen Rate", 100),
                ("RideHeights", 100),
                ("SoC", 20),
                ("Steered Angle", 100),
                ("Steering Pos", 100),
                ("Steering Pos Unfiltered", 100),
                ("Steering Shaft Torque", 100),
                ("Susp Pos", 100),
                ("Throttle Pos", 50),
                ("Throttle Pos Unfiltered", 50),
                ("Time Behind Next", 2),
                ("Total Dist", 10),
                ("Track Edge", 10),
                ("Track Temperature", 1),
                ("Turbo Boost Pressure", 100),
                ("Tyres Wear", 10),
                ("TyresCarcassTemp", 5),
                ("TyresPressure", 10),
                ("TyresRimTemp", 50),
                ("TyresRubberTemp", 10),
                ("TyresTempCentre", 100),
                ("TyresTempLeft", 100),
                ("TyresTempRight", 100),
                ("Virtual Energy", 20),
                ("Wheel Speed", 100),
                ("Wind Heading", 1),
                ("Wind Speed", 1),
                ("Yaw Rate", 100)
            };

            AddCategoryHeader(panel, "🌡️ ENVIRONMENTAL");
            AddChannelGroup(panel, channels.Where(ch => new[] { "Ambient Temperature", "Track Temperature", "Wind Speed", "Wind Heading" }
                .Contains(ch.Name)).ToList());

            AddCategoryHeader(panel, "🏎️ CHASSIS & SUSPENSION");
            AddChannelGroup(panel, channels.Where(ch => ch.Name.Contains("Ride") || ch.Name.Contains("Deflection") || ch.Name.Contains("Susp")).ToList());

            AddCategoryHeader(panel, "⚙️ ENGINE & POWERTRAIN");
            AddChannelGroup(panel, channels.Where(ch => new[] { "Clutch RPM", "Drag", "FFB Output", "Turbo Boost Pressure", "Regen Rate", "Virtual Energy" }
                .Contains(ch.Name)).ToList());

            AddCategoryHeader(panel, "🛞 TYRES");
            AddChannelGroup(panel, channels.Where(ch => ch.Name.StartsWith("Tyres") || ch.Name.StartsWith("Wheel")).ToList());

            AddCategoryHeader(panel, "📍 AERODYNAMICS & DOWNFORCE");
            AddChannelGroup(panel, channels.Where(ch => ch.Name.Contains("DownForce") || ch.Name.Contains("Wing")).ToList());

            AddCategoryHeader(panel, "📊 MOTION & ACCELERATION");
            AddChannelGroup(panel, channels.Where(ch => ch.Name.Contains("G Force") || ch.Name.Contains("Acceleration") || ch.Name.Contains("Yaw")).ToList());

            AddCategoryHeader(panel, "🛰️ GPS & POSITIONING");
            AddChannelGroup(panel, channels.Where(ch => ch.Name.Contains("GPS") || ch.Name.Contains("Path") || ch.Name.Contains("Track Edge")).ToList());

            AddCategoryHeader(panel, "🔋 ENERGY & THERMAL");
            AddChannelGroup(panel, channels.Where(ch => ch.Name.Contains("Temp") || ch.Name.Contains("Level") || ch.Name.Contains("SoC")).ToList());
        }

        /// <summary>
        /// Build UI elements for events tab
        /// </summary>
        public void BuildEventsPanel(StackPanel panel)
        {
            if (panel == null) return;
            panel.Children.Clear();

            var events = new List<(string Key, string DisplayName)>
            {
                ("ABS", "Anti-lock Braking System"),
                ("ABSLevel", "ABS Level"),
                ("AntiStall Activated", "Anti-Stall System Activated"),
                ("Best LapTime", "Best Lap Time"),
                ("Best Sector1", "Best Sector 1 Time"),
                ("Best Sector2", "Best Sector 2 Time"),
                ("Brake Bias Rear", "Brake Bias (Rear)"),
                ("Brake Migration", "Brake Migration"),
                ("CloudDarkness", "Cloud Darkness Level"),
                ("Current LapTime", "Current Lap Time"),
                ("Current Sector", "Current Sector"),
                ("Current Sector1", "Current Sector 1 Time"),
                ("Current Sector2", "Current Sector 2 Time"),
                ("Engine Max RPM", "Engine Maximum RPM"),
                ("Finish Status", "Finish Status"),
                ("FrontFlapActivated", "Front Flap Activated"),
                ("FuelMixtureMap", "Fuel Mixture Map"),
                ("Gear", "Gear Selection"),
                ("Headlights State", "Headlights State"),
                ("In Pits", "In Pits Indicator"),
                ("Lap", "Lap Number"),
                ("Lap Time", "Lap Time"),
                ("Last Sector1", "Last Sector 1 Time"),
                ("Last Sector2", "Last Sector 2 Time"),
                ("LastImpactMagnitude", "Last Impact Magnitude"),
                ("LaunchControlActive", "Launch Control Active"),
                ("Minimum Path Wetness", "Minimum Path Wetness"),
                ("OffpathWetness", "Off-path Wetness"),
                ("RearFlapActivated", "Rear Flap Activated"),
                ("RearFlapLegalStatus", "Rear Flap Legal Status"),
                ("Sector1 Flag", "Sector 1 Flag Status"),
                ("Sector2 Flag", "Sector 2 Flag Status"),
                ("Sector3 Flag", "Sector 3 Flag Status"),
                ("Speed Limiter", "Speed Limiter"),
                ("SurfaceTypes", "Surface Types"),
                ("TC", "Traction Control"),
                ("TCCut", "Traction Control Cut"),
                ("TCLevel", "Traction Control Level"),
                ("TCSlipAngle", "Traction Control Slip Angle"),
                ("TyresCompound", "Tyres Compound"),
                ("WheelsDetached", "Wheels Detached"),
                ("Yellow Flag State", "Yellow Flag State")
            };

            AddCategoryHeader(panel, "🛡️ SAFETY SYSTEMS");
            AddEventGroup(panel, new[] { "ABS", "ABSLevel", "TC", "TCCut", "TCLevel", "TCSlipAngle", "AntiStall Activated", "LaunchControlActive" }
                .Select(k => events.FirstOrDefault(e => e.Key == k)).ToList());

            AddCategoryHeader(panel, "⏱️ TIMING & SECTORS");
            AddEventGroup(panel, new[] { "Best LapTime", "Best Sector1", "Best Sector2", "Current LapTime", "Current Sector", "Current Sector1", "Current Sector2", "Last Sector1", "Last Sector2", "Lap", "Lap Time" }
                .Select(k => events.FirstOrDefault(e => e.Key == k)).ToList());

            AddCategoryHeader(panel, "🏁 RACE STATUS");
            AddEventGroup(panel, new[] { "In Pits", "Finish Status", "Speed Limiter", "Sector1 Flag", "Sector2 Flag", "Sector3 Flag", "Yellow Flag State" }
                .Select(k => events.FirstOrDefault(e => e.Key == k)).ToList());

            AddCategoryHeader(panel, "🎛️ VEHICLE SETTINGS");
            AddEventGroup(panel, new[] { "Brake Bias Rear", "Engine Max RPM", "FuelMixtureMap", "Gear", "Headlights State", "RearFlapLegalStatus", "TyresCompound" }
                .Select(k => events.FirstOrDefault(e => e.Key == k)).ToList());

            AddCategoryHeader(panel, "🌍 ENVIRONMENTAL & IMPACTS");
            AddEventGroup(panel, new[] { "CloudDarkness", "LastImpactMagnitude", "Minimum Path Wetness", "OffpathWetness", "SurfaceTypes", "WheelsDetached" }
                .Select(k => events.FirstOrDefault(e => e.Key == k)).ToList());

            AddCategoryHeader(panel, "🚗 AERODYNAMIC SYSTEMS");
            AddEventGroup(panel, new[] { "FrontFlapActivated", "RearFlapActivated", "Brake Migration" }
                .Select(k => events.FirstOrDefault(e => e.Key == k)).ToList());
        }

        /// <summary>
        /// Build UI elements for summary tab
        /// </summary>
        public void BuildSummaryPanel(StackPanel panel)
        {
            if (panel == null) return;
            panel.Children.Clear();

            AddCategoryHeader(panel, "📊 DATA OVERVIEW");

            if (_currentFrame != null)
            {
                var summaryText = new TextBlock
                {
                    Text = $"Session Time: {_currentFrame.Time:F2}s\n" +
                           $"Current Lap: {_currentFrame.CurrentLap}\n" +
                           $"Sector: {_currentFrame.Sector}\n" +
                           $"Position: ({_currentFrame.PosX:F1}, {_currentFrame.PosY:F1})",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                    FontSize = 11,
                    LineHeight = 18,
                    Margin = new Thickness(5, 0, 5, 10)
                };
                panel.Children.Add(summaryText);
            }

            AddCategoryHeader(panel, "📈 STATISTICS");
            var statsText = new TextBlock
            {
                Text = "Available Channels: 69\nAvailable Events: 47\nSampling Rates: 1Hz - 100Hz\nDisplay Mode: Real-time Updates",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 200, 255)),
                FontSize = 11,
                LineHeight = 18,
                Margin = new Thickness(5, 0, 5, 10)
            };
            panel.Children.Add(statsText);

            AddCategoryHeader(panel, "💡 INFORMATION");
            var infoText = new TextBlock
            {
                Text = "This detailed data panel provides access to all available telemetry channels and events.\n\n" +
                       "Each category groups related data for easy navigation.\n\n" +
                       "This tab does not affect existing functionality.",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160)),
                FontSize = 10,
                LineHeight = 16,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5, 0, 5, 10)
            };
            panel.Children.Add(infoText);
        }

        private void AddCategoryHeader(StackPanel panel, string title)
        {
            var header = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 215, 0)),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 5)
            };
            panel.Children.Add(header);

            var separator = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(separator);
        }

        private void AddChannelGroup(StackPanel panel, List<(string Name, double Frequency)> channels)
        {
            if (channels.Count == 0) return;

            foreach (var (name, frequency) in channels)
            {
                var item = new TextBlock
                {
                    Text = $"• {name}",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                    FontSize = 11,
                    Margin = new Thickness(10, 2, 5, 2)
                };
                panel.Children.Add(item);

                var frequencyText = new TextBlock
                {
                    Text = $"  ↳ Sampling: {frequency:F0}Hz",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 160, 180)),
                    FontSize = 9,
                    Margin = new Thickness(15, 0, 5, 4)
                };
                panel.Children.Add(frequencyText);
            }

            var spacer = new Border
            {
                Height = 5,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
                Margin = new Thickness(0, 3, 0, 3)
            };
            panel.Children.Add(spacer);
        }

        private void AddEventGroup(StackPanel panel, List<(string Key, string DisplayName)> events)
        {
            if (events.Count == 0) return;

            foreach (var (key, displayName) in events.Where(e => e.Key != null).OrderBy(e => e.DisplayName))
            {
                var item = new TextBlock
                {
                    Text = $"• {displayName}",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                    FontSize = 11,
                    Margin = new Thickness(10, 3, 5, 3)
                };
                panel.Children.Add(item);
            }

            var spacer = new Border
            {
                Height = 5,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
                Margin = new Thickness(0, 3, 0, 3)
            };
            panel.Children.Add(spacer);
        }
    }
}
