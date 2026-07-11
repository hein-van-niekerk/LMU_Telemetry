using System;
using System.Collections.Generic;
using System.Linq;
using LMU.Telemetry.Core.Models;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace LMU_Telemetry.Rendering
{
    public class TrackRenderer
    {
        // Draw the complete track outline for live mode (all accumulated frames)
        public void DrawTrack(Canvas canvas, IReadOnlyList<TelemetryFrame> frames)
        {
            if (frames.Count < 2) return;

            // Bounding-box based jump threshold
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var f in frames)
            {
                if (f.PosX < minX) minX = f.PosX; if (f.PosX > maxX) maxX = f.PosX;
                if (f.PosY < minY) minY = f.PosY; if (f.PosY > maxY) maxY = f.PosY;
            }
            double diag = Math.Sqrt((maxX - minX) * (double)(maxX - minX) +
                                    (maxY - minY) * (double)(maxY - minY));
            double maxJump = Math.Max(diag * 0.08, 10.0);

            var pts = new PointCollection();
            Color col = InputColor(frames[0].Throttle, frames[0].Brake);
            float prevX = frames[0].PosX, prevY = frames[0].PosY;
            pts.Add(new System.Windows.Point(prevX, prevY));

            void Flush()
            {
                if (pts.Count < 2) { pts.Clear(); return; }
                canvas.Children.Add(new Polyline { Points = pts, Stroke = new SolidColorBrush(col), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round });
                pts = new PointCollection();
            }

            for (int i = 1; i < frames.Count; i++)
            {
                var f = frames[i];
                double dx = f.PosX - prevX, dy = f.PosY - prevY;
                if (Math.Sqrt(dx * dx + dy * dy) > maxJump) { Flush(); pts.Add(new System.Windows.Point(f.PosX, f.PosY)); prevX = f.PosX; prevY = f.PosY; col = InputColor(f.Throttle, f.Brake); continue; }
                Color c = InputColor(f.Throttle, f.Brake);
                if (c != col) { Flush(); pts.Add(new System.Windows.Point(prevX, prevY)); col = c; }
                pts.Add(new System.Windows.Point(f.PosX, f.PosY));
                prevX = f.PosX; prevY = f.PosY;
            }
            Flush();
        }

        // Draw the driven path up to current position with throttle/brake coloring.
        // NOTE: DrawCompleteLap now renders the full lap ghost; this method is kept
        // for compatibility but not called in the replay path (lap ghost covers it).
        public void DrawDrivenPath(Canvas canvas, IReadOnlyList<TelemetryFrame> frames, int currentIndex, int currentLap)
        {
            if (frames.Count < 2 || currentIndex < 1) return;

            int lapStart = 0;
            for (int i = currentIndex; i >= 0; i--)
            {
                if (frames[i].CurrentLap != currentLap) { lapStart = i + 1; break; }
            }

            var pts = new PointCollection();
            Color col = InputColor(frames[lapStart].Throttle, frames[lapStart].Brake);
            float prevX = frames[lapStart].PosX, prevY = frames[lapStart].PosY;
            pts.Add(new System.Windows.Point(prevX, prevY));

            void Flush()
            {
                if (pts.Count < 2) { pts.Clear(); return; }
                canvas.Children.Add(new Polyline { Points = pts, Stroke = new SolidColorBrush(col), StrokeThickness = 3.0, StrokeLineJoin = PenLineJoin.Round });
                pts = new PointCollection();
            }

            for (int i = lapStart + 1; i <= currentIndex; i++)
            {
                var f = frames[i];
                Color c = InputColor(f.Throttle, f.Brake);
                if (c != col) { Flush(); pts.Add(new System.Windows.Point(prevX, prevY)); col = c; }
                pts.Add(new System.Windows.Point(f.PosX, f.PosY));
                prevX = f.PosX; prevY = f.PosY;
            }
            Flush();
        }
        
        // Draw the COMPLETE lap path for a given lap number
        public void DrawCompleteLap(Canvas canvas, IReadOnlyList<TelemetryFrame> frames, int lapNumber)
        {
            if (frames.Count < 2) return;

            // Collect lap frame indices
            int lapStart = -1, lapEnd = -1;
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].CurrentLap != lapNumber) continue;
                if (lapStart == -1) lapStart = i;
                lapEnd = i;
            }
            if (lapStart == -1) return;

            // Compute adaptive jump threshold from the lap's bounding box.
            // Any segment longer than 8% of the track diagonal is a teleport — skip it.
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = lapStart; i <= lapEnd; i++)
            {
                var f = frames[i];
                if (f.PosX < minX) minX = f.PosX;
                if (f.PosX > maxX) maxX = f.PosX;
                if (f.PosY < minY) minY = f.PosY;
                if (f.PosY > maxY) maxY = f.PosY;
            }
            double diag = Math.Sqrt((maxX - minX) * (double)(maxX - minX) +
                                    (maxY - minY) * (double)(maxY - minY));
            double maxJump = Math.Max(diag * 0.08, 10.0);

            // Draw as Polyline segments, breaking on teleport gaps.
            // Group consecutive frames by input state for throttle/brake coloring.
            // Strategy: one Polyline per run of same color, broken by gaps.
            var currentPoints = new PointCollection();
            Color currentColor = Color.FromRgb(255, 255, 255);
            float prevX = frames[lapStart].PosX, prevY = frames[lapStart].PosY;

            void FlushPolyline()
            {
                if (currentPoints.Count < 2) { currentPoints.Clear(); return; }
                canvas.Children.Add(new Polyline
                {
                    Points          = currentPoints,
                    Stroke          = new SolidColorBrush(currentColor),
                    StrokeThickness = 1.5,
                    StrokeLineJoin  = PenLineJoin.Round,
                });
                currentPoints = new PointCollection();
            }

            currentPoints.Add(new System.Windows.Point(prevX, prevY));

            for (int i = lapStart + 1; i <= lapEnd; i++)
            {
                var f = frames[i];
                double dx = f.PosX - prevX, dy = f.PosY - prevY;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                // Teleport gap — break the polyline here
                if (dist > maxJump)
                {
                    FlushPolyline();
                    currentPoints.Add(new System.Windows.Point(f.PosX, f.PosY));
                    prevX = f.PosX; prevY = f.PosY;
                    currentColor = InputColor(f.Throttle, f.Brake);
                    continue;
                }

                Color segColor = InputColor(f.Throttle, f.Brake);
                if (segColor != currentColor)
                {
                    // Carry last point into next polyline so lines join cleanly
                    FlushPolyline();
                    currentPoints.Add(new System.Windows.Point(prevX, prevY));
                    currentColor = segColor;
                }

                currentPoints.Add(new System.Windows.Point(f.PosX, f.PosY));
                prevX = f.PosX; prevY = f.PosY;
            }
            FlushPolyline();
        }

        // Draw sector and lap markers on the trace
        public void DrawSectorMarkers(Canvas canvas, IReadOnlyList<TelemetryFrame> frames, int lapNumber)
        {
            if (frames.Count < 2) return;

            // Find all frames for this lap
            var lapFrames = frames.Where(f => f.CurrentLap == lapNumber).ToList();
            if (lapFrames.Count == 0) return;

            // Track sector transitions - use a set to ensure we only mark each sector once
            var markedSectors = new HashSet<int>();
            bool lapStartMarked = false;

            foreach (var frame in lapFrames)
            {
                // Mark start/finish line (beginning of lap) with checkered flag
                if (!lapStartMarked)
                {
                    DrawCheckeredFlag(canvas, frame.PosX, frame.PosY);
                    lapStartMarked = true;
                }

                // Mark sector transitions - only mark each sector once per lap
                if (frame.Sector > 0 && !markedSectors.Contains(frame.Sector))
                {
                    Color sectorColor = frame.Sector switch
                    {
                        1 => Color.FromRgb(255, 255, 0),    // Yellow
                        2 => Color.FromRgb(0, 255, 255),    // Cyan
                        3 => Color.FromRgb(255, 0, 255),    // Magenta
                        _ => Color.FromRgb(128, 128, 128)   // Gray
                    };
                    DrawSectorMarker(canvas, frame.PosX, frame.PosY, sectorColor, frame.Sector);
                    markedSectors.Add(frame.Sector);
                }
            }
        }

        // Draw checkered flag for start/finish
        private void DrawCheckeredFlag(Canvas canvas, float posX, float posY)
        {
            double flagSize = 12;
            double squareSize = 3;

            // Draw checkered pattern (2x2 squares)
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    // Alternate black/white
                    System.Windows.Media.Brush squareColor = ((row + col) % 2 == 0) ? Brushes.White : Brushes.Black;
                    
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = squareSize,
                        Height = squareSize,
                        Fill = squareColor,
                        Stroke = Brushes.Black,
                        StrokeThickness = 0.5
                    };
                    Canvas.SetLeft(rect, posX - flagSize / 2 + col * squareSize);
                    Canvas.SetTop(rect, posY - flagSize / 2 + row * squareSize);
                    canvas.Children.Add(rect);
                }
            }
        }

        // Draw sector marker with dot and label line
        private void DrawSectorMarker(Canvas canvas, float posX, float posY, Color sectorColor, int sectorNumber)
        {
            // Draw small colored dot on track
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(sectorColor),
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            Canvas.SetLeft(dot, posX - 3);
            Canvas.SetTop(dot, posY - 3);
            canvas.Children.Add(dot);

            // Draw dotted line to label (offset to avoid clutter)
            double offsetX = 30;
            double offsetY = -20;
            var line = new Line
            {
                X1 = posX,
                Y1 = posY,
                X2 = posX + offsetX,
                Y2 = posY + offsetY,
                Stroke = new SolidColorBrush(sectorColor),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new[] { 2.0, 2.0 }) // Dotted pattern
            };
            canvas.Children.Add(line);

            // Draw label at offset position
            var text = new TextBlock
            {
                Text = $"S{sectorNumber}",
                Foreground = new SolidColorBrush(sectorColor),
                FontSize = 11,
                FontWeight = System.Windows.FontWeights.Bold,
                TextAlignment = System.Windows.TextAlignment.Center
            };
            Canvas.SetLeft(text, posX + offsetX - 15);
            Canvas.SetTop(text, posY + offsetY - 10);
            canvas.Children.Add(text);
        }

        public System.Windows.Shapes.Polygon DrawCar(Canvas canvas, TelemetryFrame frame, TelemetryFrame? previousFrame = null)
        {
            // Heading from movement direction
            double heading = 0;
            if (previousFrame != null)
            {
                float dx = frame.PosX - previousFrame.PosX;
                float dy = frame.PosY - previousFrame.PosY;
                if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                    heading = Math.Atan2(dy, dx) * 180 / Math.PI;
            }

            // Sleek chevron — pointed nose, notched tail
            var points = new PointCollection
            {
                new System.Windows.Point( 11,   0),   // nose
                new System.Windows.Point(-5.5,  4),   // rear-left
                new System.Windows.Point(-2.5,  0),   // tail notch
                new System.Windows.Point(-5.5, -4),   // rear-right
            };

            var arrow = new System.Windows.Shapes.Polygon
            {
                Points          = points,
                Fill            = new SolidColorBrush(Color.FromRgb(255, 60, 60)),
                Stroke          = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                StrokeThickness = 0.8,
                Cursor          = System.Windows.Input.Cursors.SizeAll,
                ToolTip         = "Drag to scrub replay",
            };

            var tg = new TransformGroup();
            tg.Children.Add(new RotateTransform(heading, 0, 0));
            tg.Children.Add(new TranslateTransform(frame.PosX, frame.PosY));
            arrow.RenderTransform = tg;

            canvas.Children.Add(arrow);
            return arrow;
        }

        // Returns a Color (not Brush) so callers can compare cheaply without allocating
        private static Color InputColor(float throttle, float brake)
        {
            if (brake > 0.1f)
                return brake >= 0.5f
                    ? Color.FromRgb(150,   0,  0)   // heavy braking
                    : Color.FromRgb(210,  60, 40);   // light braking
            if (throttle > 0.1f)
                return throttle >= 0.99f
                    ? Color.FromRgb(  0, 220, 140)   // full throttle
                    : Color.FromRgb(  0, 150,  70);  // partial throttle
            return Color.FromRgb(80, 80, 80);        // coasting
        }
    }
}
