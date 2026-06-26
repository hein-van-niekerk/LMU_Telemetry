using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LMU_Telemetry.Models;

using MediaColor = System.Windows.Media.Color;
using WpfUserControl = System.Windows.Controls.UserControl;
using InputMouseEventArgs = System.Windows.Input.MouseEventArgs;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPoint = System.Windows.Point;
using ShapeRectangle = System.Windows.Shapes.Rectangle;

namespace LMU_Telemetry.Views
{
    public partial class DetailedAnalysisView : WpfUserControl
    {
        private enum XAxisMode
        {
            Time,
            Distance
        }

        private readonly List<TelemetryFrame> _frames = new();
        private XAxisMode _axisMode = XAxisMode.Time;
        private double? _hoverNormalizedX;

        public DetailedAnalysisView()
        {
            InitializeComponent();
            AttachHoverSync();
        }

        public void PushFrame(TelemetryFrame frame, IReadOnlyList<TelemetryFrame> source)
        {
            if (source != null && source.Count > 0)
            {
                _frames.Clear();
                int start = Math.Max(0, source.Count - 1200);
                for (int i = start; i < source.Count; i++)
                {
                    _frames.Add(source[i]);
                }
            }

            Redraw(frame);
        }

        private void Redraw(TelemetryFrame? latest)
        {
            if (_frames.Count == 0) return;

            DrawSpeedAccel();
            DrawGForce();
            DrawYawSlip();
            DrawTyres(latest);
            DrawBrakes();
            DrawAero(latest);
            DrawEnvironment(latest);
        }

        private void DrawSpeedAccel()
        {
            ClearCanvas(SpeedAccelCanvas);
            var canvas = SpeedAccelCanvas;
            if (canvas == null || _frames.Count == 0) return;

            DrawSeries(canvas, f => GetExtended(f, "Ground Speed"), Colors.CornflowerBlue, 1.4);
            DrawSeries(canvas, f => GetExtended(f, "GPS Speed"), Colors.DarkSlateBlue, 1, opacity: 0.5);
            DrawSeries(canvas, f => GetExtended(f, "Longitudinal Acceleration"), Colors.LightGreen, 1, -6, 6);
            DrawSeries(canvas, f => GetExtended(f, "Lateral Acceleration"), Colors.Orange, 1, -6, 6);
            DrawCrosshair(canvas);
        }

        private void DrawGForce()
        {
            ClearCanvas(GForceCanvas);
            var canvas = GForceCanvas;
            if (canvas == null || _frames.Count == 0) return;

            var points = _frames
                .Select(f => (lat: GetExtended(f, "G Force Lat"), lon: GetExtended(f, "G Force Long"), frame: f))
                .Where(x => x.lat.HasValue && x.lon.HasValue)
                .ToList();

            if (points.Count < 2) return;

            double minX = points.Min(p => p.lon!.Value);
            double maxX = points.Max(p => p.lon!.Value);
            double minY = points.Min(p => p.lat!.Value);
            double maxY = points.Max(p => p.lat!.Value);
            if (Math.Abs(maxX - minX) < 0.01) { maxX = minX + 0.01; }
            if (Math.Abs(maxY - minY) < 0.01) { maxY = minY + 0.01; }

            foreach (var p in points)
            {
                double normX = (p.lon!.Value - minX) / (maxX - minX);
                double normY = 1 - (p.lat!.Value - minY) / (maxY - minY);
                double x = normX * canvas.ActualWidth;
                double y = normY * canvas.ActualHeight;
                var ellipse = new Ellipse
                {
                    Width = 3,
                    Height = 3,
                    Fill = new SolidColorBrush(ColorBlend(Colors.LightBlue, Colors.Orange, SpeedLerp(p.frame)))
                };
                Canvas.SetLeft(ellipse, x);
                Canvas.SetTop(ellipse, y);
                canvas.Children.Add(ellipse);
            }

            DrawCrosshair(canvas);
        }

        private void DrawYawSlip()
        {
            ClearCanvas(YawSlipCanvas);
            var canvas = YawSlipCanvas;
            if (canvas == null || _frames.Count == 0) return;

            DrawSeries(canvas, f => GetExtended(f, "Yaw Rate"), Colors.LightSkyBlue, 1.2);
            DrawSeries(canvas, f => GetExtended(f, "TCSlipAngle"), Colors.Khaki, 1.2, -10, 10);
            DrawMarker(canvas, f => TcActive(f), Colors.OrangeRed, 0.015);
            DrawMarker(canvas, f => OverRotation(f), Colors.Red, 0.015);
            DrawCrosshair(canvas);
        }

        private void DrawTyres(TelemetryFrame? latest)
        {
            if (TyreGrid == null) return;
            TyreGrid.Children.Clear();
            var tyres = new[] { "FL", "FR", "RL", "RR" };
            foreach (var tyre in tyres)
            {
                TyreGrid.Children.Add(CreateTyreCard(latest, tyre));
            }

            if (TyreHeatBar != null)
            {
                double avgTemp = 0;
                if (latest != null && latest.ExtendedData.TryGetValue("TyresTempCentre", out var raw) && raw is double[] arr && arr.Length > 0)
                    avgTemp = arr.Average();
                var temp = avgTemp > 0 ? avgTemp : GetExtended(latest, "TyresTempCentre") ?? 0;
                double t = Math.Clamp(temp / 150.0, 0, 1);
                TyreHeatBar.Fill = new LinearGradientBrush(Colors.DarkBlue, Colors.OrangeRed, 0)
                {
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Colors.Blue, 0),
                        new GradientStop(Colors.Green, 0.35),
                        new GradientStop(Colors.OrangeRed, 0.75),
                        new GradientStop(Colors.Red, 1)
                    }
                };
                TyreHeatBar.Opacity = 0.7 + 0.3 * t;
            }
        }

        private void DrawBrakes()
        {
            ClearCanvas(BrakeCanvas);
            var canvas = BrakeCanvas;
            if (canvas == null || _frames.Count == 0) return;

            DrawSeries(canvas, f => GetExtended(f, "Brake Temp FL"), Colors.OrangeRed, 1.2);
            DrawSeries(canvas, f => GetExtended(f, "Brake Temp FR"), Colors.Tomato, 1.0, opacity: 0.7);
            DrawSeries(canvas, f => GetExtended(f, "Brake Air Temp FL"), Colors.LightBlue, 1.0);
            DrawSeries(canvas, f => GetExtended(f, "Brake Disc Thickness FL"), Colors.MediumPurple, 1.0);
            DrawCrosshair(canvas);
        }

        private void DrawAero(TelemetryFrame? latest)
        {
            ClearCanvas(AeroCanvas);
            var canvas = AeroCanvas;
            if (canvas == null || _frames.Count == 0) return;

            DrawSeries(canvas, f => GetExtended(f, "Ride Height Front"), Colors.LightSkyBlue, 1.2);
            DrawSeries(canvas, f => GetExtended(f, "Ride Height Rear"),  Colors.MediumPurple, 1.2);
            DrawSeries(canvas, f => GetExtended(f, "Front Downforce"),   Colors.LightGreen,   1.0);
            DrawSeries(canvas, f => GetExtended(f, "Rear Downforce"),    Colors.Orange,        1.0);
            DrawSeries(canvas, f => GetExtended(f, "Ground Speed"), Colors.Gray, 0.7, opacity: 0.4);
            DrawCrosshair(canvas);
        }

        private void DrawEnvironment(TelemetryFrame? latest)
        {
            if (EnvironmentGrid == null) return;
            EnvironmentGrid.Children.Clear();
            EnvironmentGrid.Children.Add(CreateInfoCard("Ambient", FormatValue(GetExtended(latest, "Ambient Temperature"), "°C")));
            EnvironmentGrid.Children.Add(CreateInfoCard("Track", FormatValue(GetExtended(latest, "Track Temperature"), "°C")));
            EnvironmentGrid.Children.Add(CreateInfoCard("Wind Spd", FormatValue(GetExtended(latest, "Wind Speed"), "m/s")));
            EnvironmentGrid.Children.Add(CreateInfoCard("Wind Dir", FormatValue(GetExtended(latest, "Wind Heading"), "°")));
        }

        private void DrawSeries(Canvas canvas, Func<TelemetryFrame, double?> selector, MediaColor color, double thickness, double? minY = null, double? maxY = null, double opacity = 1.0)
        {
            var points = _frames.Select(f => (f, value: selector(f))).Where(p => p.value.HasValue).ToList();
            if (points.Count < 2) return;

            (double minX, double maxX) = GetAxisBounds();
            if (Math.Abs(maxX - minX) < 1e-4) return;

            double actualMinY = minY ?? points.Min(p => p.value!.Value);
            double actualMaxY = maxY ?? points.Max(p => p.value!.Value);
            if (Math.Abs(actualMaxY - actualMinY) < 1e-4)
            {
                actualMaxY = actualMinY + 1;
            }

            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(color) { Opacity = opacity },
                StrokeThickness = thickness
            };

            foreach (var (frame, value) in points)
            {
                double xVal = GetAxisValue(frame);
                double nx = (xVal - minX) / (maxX - minX);
                double ny = 1 - ((value!.Value - actualMinY) / (actualMaxY - actualMinY));
                double x = nx * GetCanvasWidth(canvas);
                double y = ny * GetCanvasHeight(canvas);
                polyline.Points.Add(new MediaPoint(x, y));
            }

            canvas.Children.Add(polyline);
        }

        private void DrawStepSeries(Canvas canvas, Func<TelemetryFrame, double?> selector, MediaColor color, double thickness, double? minY = null, double? maxY = null)
        {
            var points = _frames.Select(f => (f, value: selector(f))).Where(p => p.value.HasValue).ToList();
            if (points.Count < 2) return;

            (double minX, double maxX) = GetAxisBounds();
            if (Math.Abs(maxX - minX) < 1e-4) return;
            double actualMinY = minY ?? points.Min(p => p.value!.Value);
            double actualMaxY = maxY ?? points.Max(p => p.value!.Value);
            if (Math.Abs(actualMaxY - actualMinY) < 1e-4) actualMaxY = actualMinY + 1;

            var geometry = new StreamGeometry();
            using var ctx = geometry.Open();
            bool started = false;
            foreach (var (frame, value) in points)
            {
                double xVal = GetAxisValue(frame);
                double nx = (xVal - minX) / (maxX - minX);
                double ny = 1 - ((value!.Value - actualMinY) / (actualMaxY - actualMinY));
                double x = nx * GetCanvasWidth(canvas);
                double y = ny * GetCanvasHeight(canvas);
                if (!started)
                {
                    ctx.BeginFigure(new MediaPoint(x, y), false, false);
                    started = true;
                }
                else
                {
                    ctx.LineTo(new MediaPoint(x, y), true, false);
                }
            }

            var path = new Path
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                Data = geometry
            };
            canvas.Children.Add(path);
        }

        private void DrawMarker(Canvas canvas, Func<TelemetryFrame, bool> predicate, MediaColor color, double heightRatio)
        {
            if (_frames.Count < 2) return;
            (double minX, double maxX) = GetAxisBounds();
            if (Math.Abs(maxX - minX) < 1e-4) return;

            foreach (var frame in _frames.Where(predicate))
            {
                double nx = (GetAxisValue(frame) - minX) / (maxX - minX);
                var line = new Line
                {
                    X1 = nx * GetCanvasWidth(canvas),
                    X2 = nx * GetCanvasWidth(canvas),
                    Y1 = GetCanvasHeight(canvas) * (1 - heightRatio),
                    Y2 = GetCanvasHeight(canvas),
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 1
                };
                canvas.Children.Add(line);
            }
        }

        private void DrawPointHighlights(Canvas canvas, Func<TelemetryFrame, bool> predicate, MediaColor color, double sizeRatio)
        {
            if (_frames.Count < 2) return;
            (double minX, double maxX) = GetAxisBounds();
            double size = GetCanvasHeight(canvas) * sizeRatio;
            foreach (var frame in _frames.Where(predicate))
            {
                double nx = (GetAxisValue(frame) - minX) / (maxX - minX);
                double x = nx * GetCanvasWidth(canvas) - size / 2;
                double y = GetCanvasHeight(canvas) * 0.15;
                var rect = new ShapeRectangle
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(color),
                    Opacity = 0.8
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                canvas.Children.Add(rect);
            }
        }

        private void DrawOverlapHighlights(Canvas canvas, Func<TelemetryFrame, bool> predicate, MediaColor color, double heightRatio)
        {
            if (_frames.Count < 2) return;
            (double minX, double maxX) = GetAxisBounds();
            if (Math.Abs(maxX - minX) < 1e-4) return;
            double y = GetCanvasHeight(canvas) * (1 - heightRatio);
            foreach (var frame in _frames.Where(predicate))
            {
                double nx = (GetAxisValue(frame) - minX) / (maxX - minX);
                var line = new Line
                {
                    X1 = nx * GetCanvasWidth(canvas),
                    X2 = nx * GetCanvasWidth(canvas),
                    Y1 = y,
                    Y2 = GetCanvasHeight(canvas),
                    Stroke = new SolidColorBrush(color) { Opacity = 0.5 },
                    StrokeThickness = 2
                };
                canvas.Children.Add(line);
            }
        }

        private void ClearCanvas(Canvas? canvas)
        {
            if (canvas == null) return;
            canvas.Children.Clear();
        }

        private void DrawCrosshair(Canvas canvas)
        {
            if (!_hoverNormalizedX.HasValue) return;
            double x = GetCanvasWidth(canvas) * _hoverNormalizedX.Value;
            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 0,
                Y2 = GetCanvasHeight(canvas),
                Stroke = new SolidColorBrush(MediaColor.FromArgb(60, 255, 255, 255)),
                StrokeThickness = 1
            };
            canvas.Children.Add(line);
        }

        private double GetCanvasWidth(Canvas canvas)
        {
            if (canvas.ActualWidth > 1) return canvas.ActualWidth;
            if (canvas.RenderSize.Width > 1) return canvas.RenderSize.Width;
            return 800; // fallback to a reasonable width so data can render before layout finalizes
        }

        private double GetCanvasHeight(Canvas canvas)
        {
            if (canvas.ActualHeight > 1) return canvas.ActualHeight;
            if (canvas.RenderSize.Height > 1) return canvas.RenderSize.Height;
            return 240; // fallback height
        }

        private void AttachHoverSync()
        {
            var canvases = new[] { SpeedAccelCanvas, GForceCanvas, YawSlipCanvas, BrakeCanvas, AeroCanvas };
            foreach (var canvas in canvases)
            {
                if (canvas == null) continue;
                canvas.MouseMove += Canvas_MouseMove;
                canvas.MouseLeave += Canvas_MouseLeave;
            }
        }

        private void Canvas_MouseMove(object sender, InputMouseEventArgs e)
        {
            if (sender is Canvas canvas)
            {
                double norm = e.GetPosition(canvas).X / Math.Max(1, canvas.ActualWidth);
                _hoverNormalizedX = Math.Clamp(norm, 0, 1);
                Redraw(_frames.LastOrDefault());
            }
        }

        private void Canvas_MouseLeave(object sender, InputMouseEventArgs e)
        {
            _hoverNormalizedX = null;
            Redraw(_frames.LastOrDefault());
        }

        private (double min, double max) GetAxisBounds()
        {
            if (_frames.Count == 0) return (0, 1);
            double min = GetAxisValue(_frames.First());
            double max = GetAxisValue(_frames.Last());
            if (Math.Abs(max - min) < 1e-6) max = min + 1;
            return (min, max);
        }

        private double GetAxisValue(TelemetryFrame frame)
        {
            return _axisMode == XAxisMode.Time ? frame.Time : frame.LapDistance;
        }

        private double? GetExtended(TelemetryFrame? frame, string key)
        {
            if (frame == null) return null;
            if (frame.ExtendedData.TryGetValue(key, out var raw) && raw != null)
            {
                if (raw is double d) return d;
                if (raw is float f) return f;
                if (raw is double[] arr && arr.Length > 0) return arr[0];
                if (double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var parsed)) return parsed;
            }
            return null;
        }

        private bool TcActive(TelemetryFrame frame)
        {
            var slip = GetExtended(frame, "TCSlipAngle");
            return slip.HasValue && slip.Value > 2.5;
        }

        private bool OverRotation(TelemetryFrame frame)
        {
            var yaw = GetExtended(frame, "Yaw Rate");
            return yaw.HasValue && Math.Abs(yaw.Value) > 15;
        }

        private static double? GetExtendedIdx(TelemetryFrame? frame, string key, int idx)
        {
            if (frame == null) return null;
            if (frame.ExtendedData.TryGetValue(key, out var raw) && raw is double[] arr && idx < arr.Length)
                return arr[idx];
            return null;
        }

        private UIElement CreateTyreCard(TelemetryFrame? frame, string label)
        {
            int idx = label switch { "FL" => 0, "FR" => 1, "RL" => 2, "RR" => 3, _ => 0 };
            var temp     = GetExtendedIdx(frame, "TyresTempCentre", idx);
            var pressure = GetExtendedIdx(frame, "TyresPressure",   idx);
            var wear     = GetExtendedIdx(frame, "TyresWear",       idx);

            var border = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 24)),
                Margin = new Thickness(4),
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(4)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = label, Foreground = MediaBrushes.White, FontWeight = FontWeights.Bold });
            stack.Children.Add(new TextBlock { Text = $"Temp: {FormatValue(temp, "°C")}", Foreground = MediaBrushes.LightGray, FontSize = 11 });
            stack.Children.Add(new TextBlock { Text = $"Press: {FormatValue(pressure, "kPa")}", Foreground = MediaBrushes.LightGray, FontSize = 11 });
            stack.Children.Add(new TextBlock { Text = $"Wear: {FormatValue(wear, "%")}", Foreground = MediaBrushes.LightGray, FontSize = 11 });
            border.Child = stack;
            return border;
        }

        private UIElement CreateInfoCard(string title, string value)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 24)),
                Margin = new Thickness(4),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(4)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, Foreground = MediaBrushes.White, FontWeight = FontWeights.Bold, FontSize = 12 });
            stack.Children.Add(new TextBlock { Text = value, Foreground = MediaBrushes.LightGray, FontSize = 11 });
            border.Child = stack;
            return border;
        }

        private string FormatValue(double? value, string unit)
        {
            return value.HasValue ? $"{value.Value:F1} {unit}" : "--";
        }

        private MediaColor ColorBlend(MediaColor a, MediaColor b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return MediaColor.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        private double SpeedLerp(TelemetryFrame frame)
        {
            var speed = GetExtended(frame, "Ground Speed") ?? frame.Speed;
            return Math.Clamp(speed / 300.0, 0, 1);
        }

        private void AxisModeChanged(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is string tag)
            {
                _axisMode = tag == "Distance" ? XAxisMode.Distance : XAxisMode.Time;
                Redraw(_frames.LastOrDefault());
            }
        }

        private void VisibilityToggleChanged(object sender, RoutedEventArgs e)
        {
            Redraw(_frames.LastOrDefault());
        }
    }
}
