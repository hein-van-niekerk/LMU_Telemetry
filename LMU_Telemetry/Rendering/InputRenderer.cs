using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LMU_Telemetry.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;
using Point = System.Windows.Point;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace LMU_Telemetry.Rendering
{
    public class InputRenderer
    {
        // FR-13: Draw pedal bars (throttle and brake)
        public void DrawPedals(Canvas canvas, TelemetryFrame frame)
        {
            if (canvas == null || frame == null) return;

            canvas.Children.Clear();

            var width = canvas.ActualWidth;
            var height = canvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Split layout: Top half for steering wheel, bottom half for pedals
            var steeringHeight = height * 0.5;
            var pedalHeight = height * 0.5;

            // Draw steering wheel at top
            DrawSteeringWheel(canvas, width / 2, steeringHeight / 2, Math.Min(width, steeringHeight) * 0.35, frame.Steering);

            // Layout pedals at bottom: Throttle (left), Brake (right)
            var pedalWidth = width / 2 - 10;
            var maxHeight = pedalHeight - 40;

            // Throttle bar (green)
            DrawPedalBar(canvas, 5, height - 20, pedalWidth, maxHeight, frame.Throttle, 
                         Brushes.LimeGreen, "T");

            // Brake bar (red)
            DrawPedalBar(canvas, width / 2 + 5, height - 20, pedalWidth, maxHeight, frame.Brake, 
                         Brushes.Red, "B");
        }

        private void DrawSteeringWheel(Canvas canvas, double centerX, double centerY, double radius, float steeringAngle)
        {
            // Steering angle in LMU is typically -1 to 1, representing rotation
            // Convert to degrees (assuming max 900 degrees rotation = 450 degrees each way)
            var angleDegrees = steeringAngle * 450;

            // Outer ring
            var outerRing = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                StrokeThickness = 3,
                Fill = new SolidColorBrush(Color.FromRgb(40, 40, 40))
            };
            Canvas.SetLeft(outerRing, centerX - radius);
            Canvas.SetTop(outerRing, centerY - radius);
            canvas.Children.Add(outerRing);

            // Rotating group for spokes
            var rotateTransform = new RotateTransform(angleDegrees, centerX, centerY);

            // Center dot
            var centerDot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.White
            };
            Canvas.SetLeft(centerDot, centerX - 4);
            Canvas.SetTop(centerDot, centerY - 4);
            canvas.Children.Add(centerDot);

            // Top spoke (12 o'clock marker - red)
            var topSpoke = new Line
            {
                X1 = centerX,
                Y1 = centerY,
                X2 = centerX,
                Y2 = centerY - radius * 0.8,
                Stroke = Brushes.Red,
                StrokeThickness = 4,
                RenderTransform = rotateTransform
            };
            canvas.Children.Add(topSpoke);

            // Side spokes
            for (int i = 0; i < 2; i++)
            {
                var angle = (i * 120 + 120) * Math.PI / 180;
                var spokeEndX = centerX + Math.Sin(angle) * radius * 0.7;
                var spokeEndY = centerY - Math.Cos(angle) * radius * 0.7;

                var spoke = new Line
                {
                    X1 = centerX,
                    Y1 = centerY,
                    X2 = spokeEndX,
                    Y2 = spokeEndY,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 3,
                    RenderTransform = rotateTransform
                };
                canvas.Children.Add(spoke);
            }

            // Angle display
            var angleText = new TextBlock
            {
                Text = $"{angleDegrees:F0}°",
                Foreground = Brushes.Cyan,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(angleText, centerX - 20);
            Canvas.SetTop(angleText, centerY + radius + 5);
            canvas.Children.Add(angleText);
        }

        private void DrawPedalBar(Canvas canvas, double x, double baseY, double width, double maxHeight, 
                                  float value, Brush color, string label)
        {
            // Background bar
            var background = new Rectangle
            {
                Width = width,
                Height = maxHeight,
                Fill = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Stroke = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                StrokeThickness = 1
            };
            Canvas.SetLeft(background, x);
            Canvas.SetTop(background, baseY - maxHeight);
            canvas.Children.Add(background);

            // Value bar
            var barHeight = maxHeight * Math.Clamp(value, 0f, 1f);
            var valueBar = new Rectangle
            {
                Width = width,
                Height = barHeight,
                Fill = color
            };
            Canvas.SetLeft(valueBar, x);
            Canvas.SetTop(valueBar, baseY - barHeight);
            canvas.Children.Add(valueBar);

            // Label
            var labelText = new TextBlock
            {
                Text = $"{label}\n{(value * 100):F0}%",
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Width = width
            };
            Canvas.SetLeft(labelText, x);
            Canvas.SetTop(labelText, baseY + 2);
            canvas.Children.Add(labelText);
        }

        // FR-14: Draw time-series graphs for throttle, brake, steering
        public void DrawInputGraphs(Canvas canvas, IReadOnlyList<TelemetryFrame> frames, int currentIndex)
        {
            if (canvas == null || frames == null || frames.Count == 0) return;

            canvas.Children.Clear();

            var width = canvas.ActualWidth;
            var height = canvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Determine visible range - show 10 seconds centered at 80% position
            var visibleFrames = 600; // 10 seconds @ 60Hz
            var currentPositionRatio = 0.8; // Show current at 80% of width
            var framesBeforeCurrent = (int)(visibleFrames * currentPositionRatio);
            var framesAfterCurrent = visibleFrames - framesBeforeCurrent;
            
            var startIndex = Math.Max(0, currentIndex - framesBeforeCurrent);
            var endIndex = Math.Min(frames.Count - 1, currentIndex + framesAfterCurrent);
            var frameRange = frames.Skip(startIndex).Take(endIndex - startIndex + 1).ToList();
            var currentFrameOffset = currentIndex - startIndex;

            if (frameRange.Count < 2) return;

            // Draw grid lines
            DrawGraphGrid(canvas, width, height);

            // Draw three graphs stacked
            var graphHeight = height / 3;

            // Throttle (green)
            DrawGraph(canvas, frameRange, 0, graphHeight, width, f => f.Throttle, Brushes.LimeGreen, "Throttle", currentFrameOffset);

            // Brake (red)
            DrawGraph(canvas, frameRange, graphHeight, graphHeight, width, f => f.Brake, Brushes.Red, "Brake", currentFrameOffset);

            // Steering (cyan, normalized -1 to 1)
            DrawSteeringGraph(canvas, frameRange, graphHeight * 2, graphHeight, width, currentFrameOffset);

            // Draw current position indicator at the correct offset position (always at 80%)
            var currentX = width * currentPositionRatio;
            var indicator = new Line
            {
                X1 = currentX,
                Y1 = 0,
                X2 = currentX,
                Y2 = height,
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 3 }
            };
            canvas.Children.Add(indicator);
        }

        private void DrawGraphGrid(Canvas canvas, double width, double height)
        {
            var gridColor = new SolidColorBrush(Color.FromRgb(60, 60, 60));

            // Horizontal lines
            for (int i = 0; i <= 3; i++)
            {
                var y = height / 3 * i;
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = gridColor,
                    StrokeThickness = 1
                };
                canvas.Children.Add(line);
            }
        }

        private void DrawGraph(Canvas canvas, List<TelemetryFrame> frames, double offsetY, double height, 
                               double width, Func<TelemetryFrame, float> valueSelector, Brush color, string label, int currentOffset = -1)
        {
            if (frames.Count < 2) return;

            // Label
            var labelText = new TextBlock
            {
                Text = label,
                Foreground = color,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(labelText, 5);
            Canvas.SetTop(labelText, offsetY + 5);
            canvas.Children.Add(labelText);

            // Draw polyline
            var points = new PointCollection();
            for (int i = 0; i < frames.Count; i++)
            {
                var x = width * i / (frames.Count - 1);
                var value = Math.Clamp(valueSelector(frames[i]), 0f, 1f);
                var y = offsetY + height - (value * height);
                points.Add(new Point(x, y));
            }

            var polyline = new Polyline
            {
                Points = points,
                Stroke = color,
                StrokeThickness = 2
            };
            canvas.Children.Add(polyline);
        }

        private void DrawSteeringGraph(Canvas canvas, List<TelemetryFrame> frames, double offsetY, 
                                       double height, double width, int currentOffset = -1)
        {
            if (frames.Count < 2) return;

            var color = Brushes.Cyan;
            var label = "Steering";

            // Label
            var labelText = new TextBlock
            {
                Text = label,
                Foreground = color,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(labelText, 5);
            Canvas.SetTop(labelText, offsetY + 5);
            canvas.Children.Add(labelText);

            // Center line (0 steering)
            var centerY = offsetY + height / 2;
            var centerLine = new Line
            {
                X1 = 0,
                Y1 = centerY,
                X2 = width,
                Y2 = centerY,
                Stroke = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 2 }
            };
            canvas.Children.Add(centerLine);

            // Draw polyline (steering is -1 to +1)
            var points = new PointCollection();
            for (int i = 0; i < frames.Count; i++)
            {
                var x = width * i / (frames.Count - 1);
                var value = Math.Clamp(frames[i].Steering, -1f, 1f);
                var y = centerY - (value * height / 2);
                points.Add(new Point(x, y));
            }

            var polyline = new Polyline
            {
                Points = points,
                Stroke = color,
                StrokeThickness = 2
            };
            canvas.Children.Add(polyline);
        }
    }
}
