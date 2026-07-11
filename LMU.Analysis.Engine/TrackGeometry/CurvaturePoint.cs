namespace LMU.Analysis.Engine.TrackGeometry;

/// <summary>
/// A point on a track centerline with geometric properties.
/// </summary>
public sealed class CurvaturePoint
{
    public GeometryPoint Position { get; init; }      // X, Y coordinates in meters
    public double Heading { get; init; }               // Heading angle in radians
    public double Curvature { get; init; }              // Curvature (1/radius of turn)
}
