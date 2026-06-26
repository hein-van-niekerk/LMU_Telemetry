using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LMU_Telemetry.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Image = System.Windows.Controls.Image;
using Rectangle = System.Windows.Shapes.Rectangle;
using FontFamily = System.Windows.Media.FontFamily;

namespace LMU_Telemetry.Rendering
{
    public class InputRenderer
    {
        // --- Constants ----------------------------------------------------------

        private static readonly BitmapImage? WheelImage = LoadWheelImage();

        // Only real single-gear shifts trigger the pop; multi-gear jumps (mock data
        // or missed events) are suppressed to avoid showing backwards animations.
        private const int MaxShiftGapForPop = 1;
        private const double ShiftPopSeconds = 0.55;
        private const double FallbackLockToLockDeg = 540.0;

        // Palette used throughout both drawers.
        private static readonly Color ColThrottle = Color.FromRgb(0, 200, 83);   // #00C853
        private static readonly Color ColBrake     = Color.FromRgb(244, 67, 54);  // #F44336
        private static readonly Color ColSteering  = Color.FromRgb(0, 184, 212);  // #00B8D4
        private static readonly Color ColAccent    = Color.FromRgb(86, 156, 214); // #569CD6
        private static readonly Color ColGrid      = Color.FromRgb(38, 38, 38);
        private static readonly Color ColBg        = Color.FromRgb(18, 18, 18);
        private static readonly Color ColBarBg     = Color.FromRgb(28, 28, 28);
        private static readonly Color ColMarker    = Color.FromRgb(255, 214, 64); // amber

        // --- Shift pop state ----------------------------------------------------

        private int _lastGear = int.MinValue;
        private DateTime _shiftAtUtc = DateTime.MinValue;
        private int _shiftDir = 0; // +1 upshift, -1 downshift

        // --- Wheel image --------------------------------------------------------

        private static BitmapImage? LoadWheelImage()
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri("pack://application:,,,/Resources/wheel_silhouette.png");
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        // =========================================================================
        // Public: pedal + wheel panel
        // =========================================================================

        public void DrawPedals(Canvas canvas, TelemetryFrame frame)
        {
            if (canvas == null || frame == null) return;
            canvas.Children.Clear();

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            canvas.Background = new SolidColorBrush(Color.FromRgb(8, 8, 8));

            // Gear-change detection — only fire for real single-gear shifts.
            if (_lastGear != int.MinValue && frame.Gear != _lastGear)
            {
                int delta = frame.Gear - _lastGear;
                if (Math.Abs(delta) <= MaxShiftGapForPop)
                {
                    _shiftDir = delta > 0 ? 1 : -1;
                    _shiftAtUtc = DateTime.UtcNow;
                }
            }
            _lastGear = frame.Gear;

            // Layout: left column = steering wheel, right column = horizontal bars
            // Wheel column: square, max dimension = min(w*0.46, h)
            double wheelColW = Math.Min(w * 0.46, h);
            double barsColX  = wheelColW + 8;
            double barsColW  = w - barsColX - 10;

            DrawSteeringWheel(canvas, wheelColW / 2, h / 2,
                              Math.Min(wheelColW, h) * 0.42, frame);

            DrawHorizontalBars(canvas, barsColX, barsColW, h, frame);
        }

        // =========================================================================
        // Steering wheel
        // =========================================================================

        private void DrawSteeringWheel(Canvas canvas, double cx, double cy,
                                       double radius, TelemetryFrame frame)
        {
            double range = frame.SteeringWheelRangeVisual > 0   ? frame.SteeringWheelRangeVisual
                         : frame.SteeringWheelRangePhysical > 0 ? frame.SteeringWheelRangePhysical
                         : FallbackLockToLockDeg;
            double angleDeg = frame.Steering * (range / 2.0);
            var rotate = new RotateTransform(angleDeg);

            if (WheelImage != null)
            {
                double aspect = (double)WheelImage.PixelHeight / WheelImage.PixelWidth;
                double iw = radius * 2.0;
                double ih = iw * aspect;
                var img = new Image
                {
                    Source = WheelImage,
                    Width = iw, Height = ih,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = rotate
                };
                Canvas.SetLeft(img, cx - iw / 2);
                Canvas.SetTop(img, cy - ih / 2);
                canvas.Children.Add(img);
            }
            else
            {
                var disc = new System.Windows.Shapes.Ellipse
                {
                    Width = radius * 2, Height = radius * 2,
                    Stroke = new SolidColorBrush(Color.FromRgb(70, 108, 150)),
                    StrokeThickness = 3,
                    Fill = new SolidColorBrush(Color.FromRgb(38, 42, 48)),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = rotate
                };
                Canvas.SetLeft(disc, cx - radius);
                Canvas.SetTop(disc, cy - radius);
                canvas.Children.Add(disc);
            }

            // Gear label — centred, does not rotate.
            string gearLabel = frame.Gear switch { -1 => "R", 0 => "N", _ => frame.Gear.ToString() };
            double gearFontSize = Math.Max(22, radius * 0.70);
            var gearText = new TextBlock
            {
                Text = gearLabel,
                Foreground = new SolidColorBrush(Color.FromRgb(240, 200, 60)),
                FontSize = gearFontSize,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                TextAlignment = TextAlignment.Center,
                Width = radius * 2
            };
            Canvas.SetLeft(gearText, cx - radius);
            Canvas.SetTop(gearText, cy - gearFontSize * 0.58);
            canvas.Children.Add(gearText);

            // Angle readout — small, below wheel.
            var angleText = new TextBlock
            {
                Text = $"{angleDeg:F0}°",
                Foreground = new SolidColorBrush(ColAccent),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Width = 56
            };
            Canvas.SetLeft(angleText, cx - 28);
            Canvas.SetTop(angleText, cy + radius * 0.56);
            canvas.Children.Add(angleText);

            DrawShiftPop(canvas, cx, cy, radius);
        }

        private void DrawShiftPop(Canvas canvas, double cx, double cy, double radius)
        {
            if (_shiftDir == 0) return;
            double t = (DateTime.UtcNow - _shiftAtUtc).TotalSeconds / ShiftPopSeconds;
            if (t < 0 || t > 1) return;

            double scale   = t < 0.25 ? 0.5 + 2.8 * t : 1.0;
            double opacity = t < 0.45 ? 1.0 : 1.0 - (t - 0.45) / 0.55;

            var color = _shiftDir > 0 ? ColThrottle : ColBrake;
            var pop = new TextBlock
            {
                Text = _shiftDir > 0 ? "+" : "−",
                Foreground = new SolidColorBrush(color),
                FontSize = radius * 0.9,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Width = radius * 2,
                Opacity = Math.Clamp(opacity, 0, 1),
                RenderTransform = new ScaleTransform(scale, scale, radius, radius * 0.5)
            };
            Canvas.SetLeft(pop, cx - radius);
            Canvas.SetTop(pop, cy - radius * 0.5);
            canvas.Children.Add(pop);
        }

        // =========================================================================
        // Horizontal input bars (throttle / brake / steering)
        // =========================================================================

        private static void DrawHorizontalBars(Canvas canvas, double startX, double barColW,
                                               double totalH, TelemetryFrame frame)
        {
            const double barH   = 10;
            const double gap    = 10;
            const double labelW = 14;
            const double pctW   = 34;

            // Three bars: throttle, brake, steering
            // Center them vertically
            double totalBarsH = barH * 3 + gap * 2;
            double startY     = (totalH - totalBarsH) / 2;

            DrawHBar(canvas, startX, startY,           barColW, barH, labelW, pctW,
                     Math.Clamp(frame.Throttle, 0, 1), ColThrottle, "T", false);
            DrawHBar(canvas, startX, startY + barH + gap, barColW, barH, labelW, pctW,
                     Math.Clamp(frame.Brake, 0, 1),    ColBrake,    "B", false);
            DrawHBarBipolar(canvas, startX, startY + (barH + gap) * 2, barColW, barH, labelW, pctW,
                            Math.Clamp(frame.Steering, -1, 1), ColSteering, "S");
        }

        private static void DrawHBar(Canvas canvas, double x, double y, double totalW,
                                     double barH, double labelW, double pctW,
                                     float value, Color color, string label, bool bipolar)
        {
            double trackX = x + labelW + 4;
            double trackW = totalW - labelW - pctW - 8;
            if (trackW < 4) return;

            // Label
            var lbl = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromArgb(160, color.R, color.G, color.B)),
                FontSize = 9, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI"),
                Width = labelW, TextAlignment = TextAlignment.Left
            };
            Canvas.SetLeft(lbl, x);
            Canvas.SetTop(lbl, y + 1);
            canvas.Children.Add(lbl);

            // Track background
            var track = new Rectangle
            {
                Width = trackW, Height = barH,
                Fill = new SolidColorBrush(Color.FromRgb(22, 22, 22)),
                RadiusX = 2, RadiusY = 2
            };
            Canvas.SetLeft(track, trackX);
            Canvas.SetTop(track, y);
            canvas.Children.Add(track);

            // Fill
            double fillW = trackW * value;
            if (fillW > 1)
            {
                var fill = new Rectangle
                {
                    Width = fillW, Height = barH,
                    Fill = new SolidColorBrush(color),
                    RadiusX = 2, RadiusY = 2
                };
                Canvas.SetLeft(fill, trackX);
                Canvas.SetTop(fill, y);
                canvas.Children.Add(fill);
            }

            // Percentage
            var pct = new TextBlock
            {
                Text = $"{value * 100:F0}%",
                Foreground = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)),
                FontSize = 9, FontFamily = new FontFamily("Consolas"),
                Width = pctW - 2, TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(pct, trackX + trackW + 4);
            Canvas.SetTop(pct, y + 1);
            canvas.Children.Add(pct);
        }

        private static void DrawHBarBipolar(Canvas canvas, double x, double y, double totalW,
                                            double barH, double labelW, double pctW,
                                            float value, Color color, string label)
        {
            double trackX  = x + labelW + 4;
            double trackW  = totalW - labelW - pctW - 8;
            if (trackW < 4) return;
            double centerX = trackX + trackW / 2;

            // Label
            var lbl = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromArgb(160, color.R, color.G, color.B)),
                FontSize = 9, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI"),
                Width = labelW, TextAlignment = TextAlignment.Left
            };
            Canvas.SetLeft(lbl, x);
            Canvas.SetTop(lbl, y + 1);
            canvas.Children.Add(lbl);

            // Track background
            var track = new Rectangle
            {
                Width = trackW, Height = barH,
                Fill = new SolidColorBrush(Color.FromRgb(22, 22, 22)),
                RadiusX = 2, RadiusY = 2
            };
            Canvas.SetLeft(track, trackX);
            Canvas.SetTop(track, y);
            canvas.Children.Add(track);

            // Fill from center
            double halfFill = Math.Abs(value) * trackW / 2;
            if (halfFill > 1)
            {
                double fillX = value >= 0 ? centerX : centerX - halfFill;
                var fill = new Rectangle
                {
                    Width = halfFill, Height = barH,
                    Fill = new SolidColorBrush(color),
                    RadiusX = 2, RadiusY = 2
                };
                Canvas.SetLeft(fill, fillX);
                Canvas.SetTop(fill, y);
                canvas.Children.Add(fill);
            }

            // Center tick
            AddLine(canvas, centerX, y, centerX, y + barH,
                    new SolidColorBrush(Color.FromRgb(50, 50, 50)), 1);

            // Value
            var pct = new TextBlock
            {
                Text = $"{value:+0.00;-0.00;0.00}",
                Foreground = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)),
                FontSize = 9, FontFamily = new FontFamily("Consolas"),
                Width = pctW - 2, TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(pct, trackX + trackW + 4);
            Canvas.SetTop(pct, y + 1);
            canvas.Children.Add(pct);
        }

        // =========================================================================
        // Channel descriptor — defines each row in the MoTeC-style trace panel
        // =========================================================================

        private readonly record struct ChannelDef(
            string Name, string Unit, Color TraceColor,
            Func<TelemetryFrame, float> Selector,
            float Min, float Max, bool IsBipolar);

        private static readonly ChannelDef[] Channels =
        [
            new("THROTTLE", "%",   Color.FromRgb(0, 200, 83),   f => f.Throttle * 100, 0, 100, false),
            new("BRAKE",    "%",   Color.FromRgb(244, 67, 54),   f => f.Brake    * 100, 0, 100, false),
            new("SPEED",  "km/h",  Color.FromRgb(230, 230, 230), f => f.Speed,          0, 300, false),
            new("STEERING", "",    Color.FromRgb(0, 184, 212),   f => f.Steering,      -1,   1, true),
        ];

        // Label cache — rebuilt only when channel count changes (which is never after init).
        private bool _labelsPanelBuilt = false;
        private TextBlock[]? _labelValues;
        private TextBlock[]? _labelNames;

        // =========================================================================
        // Public: MoTeC-style channel trace display
        // =========================================================================

        public void DrawInputGraphs(Canvas canvas, StackPanel labelsPanel,
                                    IReadOnlyList<TelemetryFrame> frames, int currentIndex)
        {
            if (canvas == null || frames == null || frames.Count == 0) return;
            canvas.Children.Clear();

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            canvas.Background = new SolidColorBrush(Color.FromRgb(8, 8, 8));

            // Build label panel once
            EnsureLabelsPanelBuilt(labelsPanel, h);

            // 10-second window, cursor at 80%
            const int Visible = 600;
            const double CursorRatio = 0.8;
            int before   = (int)(Visible * CursorRatio);
            int after    = Visible - before;
            int startIdx = Math.Max(0, currentIndex - before);
            int endIdx   = Math.Min(frames.Count - 1, currentIndex + after);
            var slice    = frames.Skip(startIdx).Take(endIdx - startIdx + 1).ToList();
            if (slice.Count < 2) return;

            int nCh  = Channels.Length;
            double rowH = h / nCh;

            // Grid lines and channel band backgrounds
            for (int ch = 0; ch < nCh; ch++)
            {
                double top = rowH * ch;
                // Alternating very-subtle background
                if (ch % 2 == 1)
                {
                    var bg = new Rectangle { Width = w, Height = rowH,
                        Fill = new SolidColorBrush(Color.FromRgb(11, 11, 11)) };
                    Canvas.SetLeft(bg, 0); Canvas.SetTop(bg, top);
                    canvas.Children.Add(bg);
                }
                // Row separator
                if (ch > 0)
                    AddLine(canvas, 0, top, w, top, new SolidColorBrush(Color.FromRgb(24, 24, 24)));

                // Mid-line guide
                var def = Channels[ch];
                double midY = def.IsBipolar ? top + rowH * 0.5 : top + rowH;
                AddDashedLine(canvas, 0, midY, w, midY,
                              new SolidColorBrush(Color.FromRgb(30, 30, 30)));
            }

            // Vertical grid lines (8 divisions)
            for (int v = 1; v < 8; v++)
            {
                double x = w * v / 8.0;
                AddLine(canvas, x, 0, x, h, new SolidColorBrush(Color.FromRgb(20, 20, 20)));
            }

            // Draw each channel trace
            for (int ch = 0; ch < nCh; ch++)
            {
                var def = Channels[ch];
                double top = rowH * ch;
                float curVal = def.Selector(frames[currentIndex]);

                DrawChannelTrace(canvas, slice, top, rowH, w, def);

                // Update live label
                if (_labelValues != null && ch < _labelValues.Length)
                {
                    string valStr = def.Unit == "%" ? $"{curVal:F0}%" :
                                    def.Unit == "km/h" ? $"{curVal:F0}" :
                                    $"{curVal:+0.00;-0.00;0.00}";
                    _labelValues[ch].Text = valStr;
                }
            }

            // Cursor hairline — amber, cuts all channels
            double cursorX = w * CursorRatio;
            AddLine(canvas, cursorX, 0, cursorX, h,
                    new SolidColorBrush(Color.FromArgb(220, 255, 214, 64)), 1.5);

            // Cursor tick marks on each row boundary
            for (int ch = 0; ch < nCh; ch++)
            {
                double y = rowH * ch;
                AddLine(canvas, cursorX - 3, y, cursorX + 3, y,
                        new SolidColorBrush(Color.FromArgb(100, 255, 214, 64)));
            }
        }

        private static void DrawChannelTrace(Canvas canvas, List<TelemetryFrame> frames,
                                              double offsetY, double rowH, double w, ChannelDef def)
        {
            if (frames.Count < 2) return;

            float range = def.Max - def.Min;
            if (range < 0.001f) range = 1;

            double baseY  = def.IsBipolar ? offsetY + rowH * 0.5 : offsetY + rowH;
            var fill = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(28, def.TraceColor.R, def.TraceColor.G, def.TraceColor.B)) };
            var line = new Polyline { Stroke = new SolidColorBrush(def.TraceColor), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };

            fill.Points.Add(new Point(0, baseY));
            for (int i = 0; i < frames.Count; i++)
            {
                double x = w * i / (frames.Count - 1);
                float  v = Math.Clamp((def.Selector(frames[i]) - def.Min) / range, 0, 1);
                double y = offsetY + rowH * (1 - v);
                line.Points.Add(new Point(x, y));
                fill.Points.Add(new Point(x, y));
            }
            fill.Points.Add(new Point(w, baseY));

            canvas.Children.Add(fill);
            canvas.Children.Add(line);
        }

        private void EnsureLabelsPanelBuilt(StackPanel panel, double totalH)
        {
            if (_labelsPanelBuilt || panel == null) return;
            _labelsPanelBuilt = true;
            panel.Children.Clear();

            int n = Channels.Length;
            _labelValues = new TextBlock[n];
            _labelNames  = new TextBlock[n];

            double rowH = totalH / n;

            for (int i = 0; i < n; i++)
            {
                var def   = Channels[i];
                var color = def.TraceColor;

                // Left color swatch — full row height
                var swatch = new Rectangle
                {
                    Width = 3,
                    Fill = new SolidColorBrush(color),
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                // Channel name — small, dimmed, above value
                _labelNames[i] = new TextBlock
                {
                    Text = def.Name,
                    Foreground = new SolidColorBrush(Color.FromArgb(110, color.R, color.G, color.B)),
                    FontSize = 8, FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Segoe UI"),
                    Margin = new Thickness(0, 0, 0, 1)
                };

                // Live value — large, bright, the thing you actually read
                _labelValues[i] = new TextBlock
                {
                    Text = "—",
                    Foreground = new SolidColorBrush(color),
                    FontSize = 15, FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Consolas"),
                };

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Height = rowH,
                    ClipToBounds = true
                };

                var inner = new Grid();
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Grid.SetColumn(swatch, 0);
                inner.Children.Add(swatch);

                var textStack = new StackPanel
                {
                    Margin = new Thickness(8, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                textStack.Children.Add(_labelNames[i]);
                textStack.Children.Add(_labelValues[i]);
                Grid.SetColumn(textStack, 1);
                inner.Children.Add(textStack);

                border.Child = inner;
                panel.Children.Add(border);
            }
        }

        // =========================================================================
        // Helpers
        // =========================================================================

        private static void AddLine(Canvas c, double x1, double y1, double x2, double y2,
                                    Brush stroke, double thick = 1)
            => c.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thick });

        private static void AddDashedLine(Canvas c, double x1, double y1, double x2, double y2, Brush stroke)
            => c.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = stroke, StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            });
    }
}
