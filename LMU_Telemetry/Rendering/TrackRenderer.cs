using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LMU_Telemetry.Models;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace LMU_Telemetry.Rendering
{
    public class TrackRenderer
    {
        // Draw the complete track outline (full racing line)
        public void DrawTrack(Canvas canvas, IReadOnlyList<TelemetryFrame> frames)
        {
            if (frames.Count < 2) return;

            // Limit how many segments we draw to prevent performance issues
            var step = Math.Max(1, frames.Count / 1000); // Max 1000 line segments

            // Draw ALL segments to show complete track outline
            for (int i = step; i < frames.Count; i += step)
            {
                var prevFrame = frames[i - step];
                var currentFrame = frames[i];

                var line = new Line
                {
                    X1 = prevFrame.PosX,
                    Y1 = prevFrame.PosY,
                    X2 = currentFrame.PosX,
                    Y2 = currentFrame.PosY,
                    Stroke = GetSpeedColor(currentFrame.Speed),
                    StrokeThickness = 2
                };
                canvas.Children.Add(line);
            }
        }

        // Draw the driven path up to current position with throttle/brake coloring
        public void DrawDrivenPath(Canvas canvas, IReadOnlyList<TelemetryFrame> frames, int currentIndex, int currentLap)
        {
            if (frames.Count < 2 || currentIndex < 1) return;

            // Find the start of the current lap
            int lapStartIndex = 0;
            for (int i = currentIndex; i >= 0; i--)
            {
                if (frames[i].CurrentLap != currentLap)
                {
                    lapStartIndex = i + 1;
                    break;
                }
            }

            // Aggressively optimize - max 100 segments for entire lap
            var lapLength = currentIndex - lapStartIndex;
            var step = Math.Max(1, lapLength / 100);

            for (int i = lapStartIndex + step; i <= currentIndex; i += step)
            {
                var prevFrame = frames[i - step];
                var currentFrame = frames[i];

                var line = new Line
                {
                    X1 = prevFrame.PosX,
                    Y1 = prevFrame.PosY,
                    X2 = currentFrame.PosX,
                    Y2 = currentFrame.PosY,
                    Stroke = GetInputColor(currentFrame.Throttle, currentFrame.Brake),
                    StrokeThickness = 3.8 // 5% thinner than previous 4px
                };
                canvas.Children.Add(line);
            }
        }
        
        // Draw the COMPLETE lap path for a given lap number
        public void DrawCompleteLap(Canvas canvas, IReadOnlyList<TelemetryFrame> frames, int lapNumber)
        {
            if (frames.Count < 2) return;

            // Find start and end of this lap
            int lapStartIndex = -1;
            int lapEndIndex = -1;
            
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].CurrentLap == lapNumber)
                {
                    if (lapStartIndex == -1)
                        lapStartIndex = i;
                    lapEndIndex = i;
                }
            }
            
            if (lapStartIndex == -1 || lapEndIndex == -1)
            {
                return; // Lap not found
            }

            // Draw complete lap - max 300 segments for smooth line
            var lapLength = lapEndIndex - lapStartIndex;
            var step = Math.Max(1, lapLength / 300);

            int prevIndex = lapStartIndex;
            for (int i = lapStartIndex + step; i <= lapEndIndex; i += step)
            {
                var prevFrame = frames[prevIndex];
                var currentFrame = frames[i];

                var line = new Line
                {
                    X1 = prevFrame.PosX,
                    Y1 = prevFrame.PosY,
                    X2 = currentFrame.PosX,
                    Y2 = currentFrame.PosY,
                    Stroke = GetInputColor(currentFrame.Throttle, currentFrame.Brake),
                    StrokeThickness = 1.425 // 5% thinner than previous 1.5px to match driven path ratio
                };
                canvas.Children.Add(line);

                prevIndex = i;
            }

            // Ensure the final segment reaches the lap end
            if (prevIndex != lapEndIndex)
            {
                var prevFrame = frames[prevIndex];
                var endFrame = frames[lapEndIndex];
                var finalLine = new Line
                {
                    X1 = prevFrame.PosX,
                    Y1 = prevFrame.PosY,
                    X2 = endFrame.PosX,
                    Y2 = endFrame.PosY,
                    Stroke = GetInputColor(endFrame.Throttle, endFrame.Brake),
                    StrokeThickness = 1.425
                };
                canvas.Children.Add(finalLine);
            }

            // Close the loop near the finish line if the endpoints are close
            var startFrame = frames[lapStartIndex];
            var lastFrame = frames[lapEndIndex];
            var dxClose = startFrame.PosX - lastFrame.PosX;
            var dyClose = startFrame.PosY - lastFrame.PosY;
            var closeDistance = Math.Sqrt(dxClose * dxClose + dyClose * dyClose);
            if (closeDistance < 80)
            {
                var closeLine = new Line
                {
                    X1 = lastFrame.PosX,
                    Y1 = lastFrame.PosY,
                    X2 = startFrame.PosX,
                    Y2 = startFrame.PosY,
                    Stroke = GetInputColor(lastFrame.Throttle, lastFrame.Brake),
                    StrokeThickness = 1.425
                };
                canvas.Children.Add(closeLine);
            }
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
                    Brush squareColor = ((row + col) % 2 == 0) ? Brushes.White : Brushes.Black;
                    
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

        // Helper to draw a marker at a position (DEPRECATED - kept for compatibility)
        private void DrawMarker(Canvas canvas, float posX, float posY, Brush color, double size, string label)
        {
            // Draw circle marker
            var circle = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = color,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            Canvas.SetLeft(circle, posX - size / 2);
            Canvas.SetTop(circle, posY - size / 2);
            canvas.Children.Add(circle);

            // Add text label
            var text = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = System.Windows.FontWeights.Bold,
                TextAlignment = System.Windows.TextAlignment.Center
            };
            Canvas.SetLeft(text, posX - 15);
            Canvas.SetTop(text, posY + size / 2 + 5);
            canvas.Children.Add(text);
        }

        public System.Windows.Shapes.Polygon DrawCar(Canvas canvas, TelemetryFrame frame, TelemetryFrame? previousFrame = null)
        {
            // Calculate heading angle from movement direction
            double heading = 0;
            
            if (previousFrame != null)
            {
                // Calculate heading from position change
                float dx = frame.PosX - previousFrame.PosX;
                float dy = frame.PosY - previousFrame.PosY;
                
                if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                {
                    heading = Math.Atan2(dy, dx) * 180 / Math.PI;
                }
            }

            // Create arrow shape - triangle pointing in direction of travel
            double arrowSize = 12.0;
            double arrowWidth = 8.0;
            
            // Arrow points: tip at front, two base points
            var points = new PointCollection
            {
                new System.Windows.Point(arrowSize, 0),        // Tip (front)
                new System.Windows.Point(-arrowSize/2, arrowWidth/2),   // Base left
                new System.Windows.Point(-arrowSize/2, -arrowWidth/2)   // Base right
            };
            
            var arrow = new System.Windows.Shapes.Polygon
            {
                Points = points,
                Fill = Brushes.Red,
                Stroke = Brushes.White,
                StrokeThickness = 1.5
            };
            
            // Apply rotation and translation
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new RotateTransform(heading, 0, 0));
            transformGroup.Children.Add(new TranslateTransform(frame.PosX, frame.PosY));
            arrow.RenderTransform = transformGroup;
            
            canvas.Children.Add(arrow);
            
            return arrow; // Return the arrow so caller can track and remove it later
        }

        private Brush GetSpeedColor(float speed)
        {
            // Color code based on speed (FR-11: driven line color based on speed)
            var normalizedSpeed = Math.Clamp(speed / 200f, 0f, 1f); // Assume max 200 km/h

            if (normalizedSpeed < 0.3f)
                return new SolidColorBrush(Color.FromRgb(255, 0, 0)); // Red - slow
            else if (normalizedSpeed < 0.6f)
                return new SolidColorBrush(Color.FromRgb(255, 255, 0)); // Yellow - medium
            else if (normalizedSpeed < 0.8f)
                return new SolidColorBrush(Color.FromRgb(0, 255, 0)); // Green - fast
            else
                return new SolidColorBrush(Color.FromRgb(0, 255, 255)); // Cyan - very fast
        }
        
        private Brush GetInputColor(float throttle, float brake)
        {
            // Priority: braking overrides throttle coloring
            if (brake > 0.1f)
            {
                if (brake >= 0.5f)
                {
                    // Dark red for heavy braking (50-100%)
                    return new SolidColorBrush(Color.FromRgb(150, 0, 0));
                }
                // Lighter red for mild braking (<50%)
                return new SolidColorBrush(Color.FromRgb(210, 60, 40));
            }

            if (throttle > 0.1f)
            {
                if (throttle >= 0.99f)
                {
                    // Lighter green at full throttle
                    return new SolidColorBrush(Color.FromRgb(0, 220, 140));
                }
                // Darker green when on throttle but not 100%
                return new SolidColorBrush(Color.FromRgb(0, 150, 70));
            }

            // Coasting
            return new SolidColorBrush(Color.FromRgb(80, 80, 80));
        }
    }
}
