using LMU.Telemetry.Core.Models;

namespace LMU.Telemetry.Core.Services;

/// <summary>
/// Common shape for anything that produces a live stream of telemetry frames -
/// real shared-memory capture or the mock/simulated generator. Lets the UI layer
/// hold one polymorphic reference instead of branching on concrete type.
///
/// Deliberately does NOT cover file-replay: loading a .duckdb recording is a bulk
/// parse (DuckDBTelemetryReader.LoadTelemetryData) into a fixed, seekable frame
/// list, not a stream - scrubbing/looping over that list is an interaction concern
/// (see PlaybackController), not a capture concern.
/// </summary>
public interface ITelemetrySource
{
    event EventHandler<TelemetryFrame>? FrameReceived;

    bool IsRunning { get; }

    void Start();
    void Stop();
}
