using System;
using System.Collections.Generic;
using System.Linq;
using Point = System.Windows.Point;

namespace LMU_Telemetry.Models;

/// <summary>
/// One stretch of the candidate map where post-alignment divergence from the
/// existing map is unusually large — a signal worth showing the dev, not
/// averaging away. <see cref="LapDistance"/> is the arc-length along the
/// (aligned) candidate centerline where this was measured.
/// </summary>
public class DivergenceSegment
{
    public double LapDistance { get; set; }
    public double DivergenceMeters { get; set; }
}

/// <summary>Result of aligning a candidate map onto an existing one.</summary>
public class AlignmentResult
{
    /// <summary>The candidate centerline positions after the rigid transform is applied.</summary>
    public List<Point> AlignedCandidate { get; set; } = new();

    public double AverageDivergenceMeters { get; set; }
    public double MaxDivergenceMeters { get; set; }

    /// <summary>Stretches where divergence from the existing map is unusually large.</summary>
    public List<DivergenceSegment> HighDivergenceSegments { get; set; } = new();
}

/// <summary>
/// Aligns a newly-generated candidate track map onto an existing one (OSM-sourced
/// or a prior telemetry generation) using iterative closest point (ICP).
///
/// Candidate and existing map already live in the same world-coordinate frame
/// (both are ultimately derived from the sim's own X/Z telemetry), so unlike a
/// from-scratch point-cloud registration problem there is no need for a manual
/// landmark pick to get a rough starting transform — ICP started from the
/// identity transform converges reliably here, since the two point sets are
/// already roughly co-located. It only needs to correct small drift/noise
/// between the two sources, not gross misalignment.
/// </summary>
public static class TrackMapAligner
{
    /// <summary>
    /// Run ICP: iterate nearest-neighbour correspondence + best-fit rigid
    /// transform (2D Kabsch/Procrustes, rotation + translation, no scale)
    /// until the fit stops improving.
    /// </summary>
    public static AlignmentResult Align(
        List<Point> candidate,
        List<Point> existing,
        int maxIterations = 20,
        double convergenceEpsilonMeters = 0.005)
    {
        var result = new AlignmentResult();

        if (candidate.Count == 0 || existing.Count == 0)
        {
            result.AlignedCandidate = new List<Point>(candidate);
            return result;
        }

        var current = new List<Point>(candidate);
        double prevRmse = double.MaxValue;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            var correspondences = FindNearestNeighbors(current, existing);
            var (cosT, sinT, t, rmse) = SolveRigidTransform(current, correspondences);

            current = current.Select(p => ApplyTransform(p, cosT, sinT, t)).ToList();

            if (Math.Abs(prevRmse - rmse) < convergenceEpsilonMeters)
            {
                prevRmse = rmse;
                break;
            }
            prevRmse = rmse;
        }

        // --- Final per-point divergence report ---
        var finalCorrespondences = FindNearestNeighbors(current, existing);
        var divergences = new double[current.Count];
        for (int i = 0; i < current.Count; i++)
        {
            double dx = current[i].X - finalCorrespondences[i].X;
            double dy = current[i].Y - finalCorrespondences[i].Y;
            divergences[i] = Math.Sqrt(dx * dx + dy * dy);
        }

        double avg = divergences.Length > 0 ? divergences.Average() : 0;
        double max = divergences.Length > 0 ? divergences.Max() : 0;

        // Flag stretches with divergence well above the overall average —
        // could mean a stitching error in the existing map, or the new laps
        // went off the intended line there. Surface it, don't hide it.
        double threshold = Math.Max(avg * 3, 2.0);
        var highDivergence = new List<DivergenceSegment>();
        double cumulativeDist = 0;
        for (int i = 0; i < current.Count; i++)
        {
            if (i > 0)
            {
                double dx = current[i].X - current[i - 1].X;
                double dy = current[i].Y - current[i - 1].Y;
                cumulativeDist += Math.Sqrt(dx * dx + dy * dy);
            }
            if (divergences[i] > threshold)
                highDivergence.Add(new DivergenceSegment { LapDistance = cumulativeDist, DivergenceMeters = divergences[i] });
        }

        result.AlignedCandidate = current;
        result.AverageDivergenceMeters = avg;
        result.MaxDivergenceMeters = max;
        result.HighDivergenceSegments = CollapseAdjacent(highDivergence);
        return result;
    }

    /// <summary>Merge adjacent flagged samples (within ~20 m) into one segment, keeping the worst divergence.</summary>
    private static List<DivergenceSegment> CollapseAdjacent(List<DivergenceSegment> flagged)
    {
        if (flagged.Count == 0) return flagged;

        var collapsed = new List<DivergenceSegment>();
        var current = flagged[0];

        for (int i = 1; i < flagged.Count; i++)
        {
            if (flagged[i].LapDistance - current.LapDistance <= 20.0)
            {
                if (flagged[i].DivergenceMeters > current.DivergenceMeters)
                    current = flagged[i];
            }
            else
            {
                collapsed.Add(current);
                current = flagged[i];
            }
        }
        collapsed.Add(current);
        return collapsed;
    }

    /// <summary>Brute-force nearest neighbour — fine at the ~500-600 points/map this app generates.</summary>
    private static List<Point> FindNearestNeighbors(List<Point> from, List<Point> to)
    {
        var result = new List<Point>(from.Count);
        foreach (var p in from)
        {
            double bestDist = double.MaxValue;
            Point best = to[0];
            foreach (var q in to)
            {
                double dx = p.X - q.X, dy = p.Y - q.Y;
                double d = dx * dx + dy * dy;
                if (d < bestDist) { bestDist = d; best = q; }
            }
            result.Add(best);
        }
        return result;
    }

    /// <summary>
    /// Closed-form 2D Kabsch/Procrustes: best-fit rotation + translation
    /// (no scale) minimizing total squared distance between paired points.
    /// Returns cos/sin of the rotation, the translation, and the resulting RMSE.
    /// </summary>
    private static (double CosT, double SinT, Point T, double Rmse) SolveRigidTransform(
        List<Point> src, List<Point> dst)
    {
        int n = src.Count;
        double srcCx = src.Average(p => p.X), srcCy = src.Average(p => p.Y);
        double dstCx = dst.Average(p => p.X), dstCy = dst.Average(p => p.Y);

        double sxx = 0, sxy = 0, syx = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            double ax = src[i].X - srcCx, ay = src[i].Y - srcCy;
            double bx = dst[i].X - dstCx, by = dst[i].Y - dstCy;
            sxx += ax * bx; sxy += ax * by;
            syx += ay * bx; syy += ay * by;
        }

        // Closed-form optimal 2D rotation from the cross-covariance matrix
        // (the 2D special case of Kabsch — no SVD needed).
        double theta = Math.Atan2(sxy - syx, sxx + syy);
        double cosT = Math.Cos(theta), sinT = Math.Sin(theta);

        double rx = cosT * srcCx - sinT * srcCy;
        double ry = sinT * srcCx + cosT * srcCy;
        var t = new Point(dstCx - rx, dstCy - ry);

        double sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            double px = cosT * src[i].X - sinT * src[i].Y + t.X;
            double py = sinT * src[i].X + cosT * src[i].Y + t.Y;
            double dx = px - dst[i].X, dy = py - dst[i].Y;
            sumSq += dx * dx + dy * dy;
        }
        double rmse = n > 0 ? Math.Sqrt(sumSq / n) : 0;

        return (cosT, sinT, t, rmse);
    }

    private static Point ApplyTransform(Point p, double cosT, double sinT, Point t)
    {
        double x = cosT * p.X - sinT * p.Y + t.X;
        double y = sinT * p.X + cosT * p.Y + t.Y;
        return new Point(x, y);
    }
}
