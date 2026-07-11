using LMU.Telemetry.Core.Models;

namespace LMU.Analysis.Engine.Timing;

public static class LapTimingCalculator
{
    public static List<LapTimingInfo> BuildTimingCache(IReadOnlyList<TelemetryFrame> frames)
    {
        var result = new List<LapTimingInfo>();
        var laps = frames.GroupBy(f => f.CurrentLap).OrderBy(g => g.Key);

        foreach (var lap in laps)
        {
            var list = lap.ToList();
            if (list.Count == 0) continue;

            var lapStartTime = list.First().Time;
            var lapEndTime = list.Last().Time;

            double? s2Start = null;
            double? s3Start = null;

            foreach (var frame in list)
            {
                if (!s2Start.HasValue && frame.Sector >= 2)
                {
                    s2Start = frame.Time;
                }
                if (!s3Start.HasValue && frame.Sector >= 3)
                {
                    s3Start = frame.Time;
                }
            }

            // Fallback: derive sector boundaries from lap distance thirds if sector data is missing
            var maxDistance = list.Max(f => f.LapDistance);
            if (maxDistance > 0 && (!s2Start.HasValue || !s3Start.HasValue))
            {
                var s1Distance = maxDistance / 3f;
                var s2Distance = maxDistance * 2f / 3f;

                if (!s2Start.HasValue)
                {
                    s2Start = list.FirstOrDefault(f => f.LapDistance >= s1Distance)?.Time;
                }
                if (!s3Start.HasValue)
                {
                    s3Start = list.FirstOrDefault(f => f.LapDistance >= s2Distance)?.Time;
                }
            }

            TimeSpan? s1 = s2Start.HasValue ? TimeSpan.FromSeconds(s2Start.Value - lapStartTime) : null;
            TimeSpan? s2 = (s2Start.HasValue && s3Start.HasValue) ? TimeSpan.FromSeconds(s3Start.Value - s2Start.Value) : null;
            TimeSpan? s3 = s3Start.HasValue ? TimeSpan.FromSeconds(lapEndTime - s3Start.Value) : null;

            var lapTime = TimeSpan.FromSeconds(lapEndTime - lapStartTime);

            result.Add(new LapTimingInfo
            {
                LapNumber = lap.Key,
                S1 = s1,
                S2 = s2,
                S3 = s3,
                LapTime = lapTime
            });
        }

        return result;
    }
}
