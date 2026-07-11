namespace LMU.Analysis.Engine.TrackGeometry;

/// <summary>
/// A corner detected on a track centerline from curvature peaks.
/// </summary>
public sealed class DetectedCorner
{
    public int Number { get; init; }
    public GeometryPoint Position { get; init; }
    public double Curvature { get; init; }
    public double LapDistance { get; init; }
}
