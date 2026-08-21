namespace LMU.Telemetry.Core.Telemetry;

/// <summary>
/// Owns replay playback state (timer, speed multiplier, paused/playing) that used to
/// live directly in the WPF code-behind. Ticks a plain threadpool timer at ~60Hz and
/// asks the caller (via <see cref="FrameAdvanceRequested"/>) to scrub the buffer to a
/// target index - it does not mutate the buffer itself, since the UI layer is
/// responsible for keeping CurrentFrame/derived view state in sync when scrubbing.
/// </summary>
public sealed class PlaybackController : IDisposable
{
    private readonly TelemetryBuffer _buffer;
    private readonly Timer _timer;
    private bool _isPaused = true;
    private int _speedMultiplier = 1; // 1x, 2x, or 4x

    public event EventHandler<int>? FrameAdvanceRequested;

    public PlaybackController(TelemetryBuffer buffer)
    {
        _buffer = buffer;
        _timer = new Timer(Tick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsPaused => _isPaused;
    public int SpeedMultiplier => _speedMultiplier;

    public void SetSpeedMultiplier(int multiplier) => _speedMultiplier = multiplier;

    public void Play()
    {
        _isPaused = false;
        _timer.Change(0, 16); // ~60Hz
    }

    public void Pause()
    {
        _isPaused = true;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void Tick(object? state)
    {
        if (_isPaused || !_buffer.HasData) return;

        var nextIndex = _buffer.CurrentIndex + _speedMultiplier;
        if (nextIndex >= _buffer.Frames.Count)
        {
            nextIndex = 0; // loop back to start
        }

        FrameAdvanceRequested?.Invoke(this, nextIndex);
    }

    public void Dispose() => _timer.Dispose();
}
