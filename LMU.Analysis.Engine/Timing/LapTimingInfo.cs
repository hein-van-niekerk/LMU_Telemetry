namespace LMU.Analysis.Engine.Timing;

public sealed class LapTimingInfo
{
    public int LapNumber { get; init; }
    public TimeSpan? S1 { get; init; }
    public TimeSpan? S2 { get; init; }
    public TimeSpan? S3 { get; init; }
    public TimeSpan LapTime { get; init; }
}
