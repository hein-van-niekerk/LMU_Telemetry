namespace LMU.Analysis.Engine.TrackGeometry;

public static class CornerDetector
{
    /// <summary>
    /// Calculate heading angle and curvature for each point along a centerline.
    /// </summary>
    public static List<CurvaturePoint> CalculateHeadingAndCurvature(IReadOnlyList<GeometryPoint> points)
    {
        var trackPoints = new List<CurvaturePoint>();

        if (points.Count < 3)
        {
            // Not enough points for derivatives
            foreach (var point in points)
            {
                trackPoints.Add(new CurvaturePoint
                {
                    Position = point,
                    Heading = 0,
                    Curvature = 0
                });
            }
            return trackPoints;
        }

        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];

            // Calculate heading (tangent direction)
            double heading;
            if (i == 0)
            {
                // Forward difference
                heading = Math.Atan2(
                    points[i + 1].Y - points[i].Y,
                    points[i + 1].X - points[i].X
                );
            }
            else if (i == points.Count - 1)
            {
                // Backward difference
                heading = Math.Atan2(
                    points[i].Y - points[i - 1].Y,
                    points[i].X - points[i - 1].X
                );
            }
            else
            {
                // Central difference
                heading = Math.Atan2(
                    points[i + 1].Y - points[i - 1].Y,
                    points[i + 1].X - points[i - 1].X
                );
            }

            // Calculate curvature (rate of change of heading)
            double curvature = 0;
            if (i > 0 && i < points.Count - 1)
            {
                // First derivatives (velocity)
                double dx = (points[i + 1].X - points[i - 1].X) / 2.0;
                double dy = (points[i + 1].Y - points[i - 1].Y) / 2.0;

                // Second derivatives (acceleration)
                double ddx = points[i + 1].X - 2 * points[i].X + points[i - 1].X;
                double ddy = points[i + 1].Y - 2 * points[i].Y + points[i - 1].Y;

                // Curvature formula: κ = |x'y'' - y'x''| / (x'² + y'²)^(3/2)
                double numerator = Math.Abs(dx * ddy - dy * ddx);
                double denominator = Math.Pow(dx * dx + dy * dy, 1.5);

                if (denominator > 1e-6)
                {
                    curvature = numerator / denominator;
                }
            }

            trackPoints.Add(new CurvaturePoint
            {
                Position = point,
                Heading = heading,
                Curvature = curvature
            });
        }

        return trackPoints;
    }

    /// <summary>
    /// Detect corners from track points based on curvature peaks.
    /// </summary>
    public static List<DetectedCorner> DetectCorners(IReadOnlyList<CurvaturePoint> trackPoints,
        double curvatureThreshold = 0.005, int minDistance = 20)
    {
        var corners = new List<DetectedCorner>();
        if (trackPoints.Count < 10) return corners;

        // Find local maxima of curvature (high curvature = tight corner)
        var cornerIndices = new List<(int index, double curvature)>();

        for (int i = 3; i < trackPoints.Count - 3; i++)
        {
            double curvature = trackPoints[i].Curvature;

            // Check if this is a local maximum and above threshold
            if (curvature > curvatureThreshold &&
                curvature > trackPoints[i - 1].Curvature &&
                curvature > trackPoints[i - 2].Curvature &&
                curvature > trackPoints[i + 1].Curvature &&
                curvature > trackPoints[i + 2].Curvature)
            {
                cornerIndices.Add((i, curvature));
            }
        }

        // Filter to remove adjacent peaks (same corner detected multiple times)
        var filteredCorners = new List<(int index, double curvature)>();

        foreach (var (index, curvature) in cornerIndices)
        {
            if (filteredCorners.Count == 0 ||
                index - filteredCorners.Last().index >= minDistance)
            {
                filteredCorners.Add((index, curvature));
            }
        }

        // Create Corner objects with sequential numbering
        double lapDistance = 0;
        for (int i = 0; i < filteredCorners.Count; i++)
        {
            int idx = filteredCorners[i].index;
            var point = trackPoints[idx];

            // Calculate distance along track up to this point
            if (idx > 0)
            {
                for (int j = 1; j <= idx; j++)
                {
                    double dx = trackPoints[j].Position.X - trackPoints[j - 1].Position.X;
                    double dy = trackPoints[j].Position.Y - trackPoints[j - 1].Position.Y;
                    lapDistance += Math.Sqrt(dx * dx + dy * dy);
                }
            }

            corners.Add(new DetectedCorner
            {
                Number = i + 1,
                Position = point.Position,
                Curvature = point.Curvature,
                LapDistance = lapDistance
            });

            lapDistance = 0; // Reset for next calculation
        }

        return corners;
    }
}
