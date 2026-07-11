using System;
using System.Threading;
using LMU.Telemetry.Core.Models;
using LMU.Telemetry.Core.Telemetry;

namespace LMU.Telemetry.Core.Services
{
    // Real telemetry service with LMU integration and lap detection
    public class TelemetryService : IDisposable
    {
        private readonly SharedMemoryReader _sharedMemory;
        private readonly TelemetryBuffer _buffer;
        private readonly System.Threading.Timer _pollTimer;
        private readonly System.Threading.Timer _reconnectTimer;
        private bool _isRunning = false;
        private bool _isDisposed = false;
        private int _lastLapNumber = -1;
        private double _lastLapStartTime = 0;

        public event EventHandler<TelemetryFrame>? NewFrameReceived;
        public event EventHandler<LapInfo>? LapCompleted;
        public event EventHandler<string>? ConnectionStatusChanged;

        public TelemetryBuffer Buffer => _buffer;
        public bool IsRunning => _isRunning;
        public SessionState Session { get; } = new();
        public CarState Car { get; } = new();

        public TelemetryService()
        {
            _sharedMemory = new SharedMemoryReader();
            _buffer = new TelemetryBuffer();
            _pollTimer      = new System.Threading.Timer(PollTelemetry,  null, Timeout.Infinite, Timeout.Infinite);
            _reconnectTimer = new System.Threading.Timer(TryReconnect,   null, Timeout.Infinite, Timeout.Infinite);
        }

        // Called once at startup — begins waiting for LMU silently; no error noise until first attempt.
        public void Start()
        {
            if (_isRunning || _isDisposed) return;
            ConnectionStatusChanged?.Invoke(this, "Waiting for LMU...");
            // Attempt immediately, then retry every 3 s until connected.
            _reconnectTimer.Change(0, 3000);
        }

        private void TryReconnect(object? state)
        {
            if (_isRunning || _isDisposed) return;

            if (_sharedMemory.Connect())
            {
                _reconnectTimer.Change(Timeout.Infinite, Timeout.Infinite); // stop retrying
                _isRunning = true;
                _pollTimer.Change(0, 16); // ~60 Hz
                ConnectionStatusChanged?.Invoke(this, "Connected to LMU");
            }
            // else: silently wait for next retry — no status spam
        }

        private void LoseConnection(string reason)
        {
            _isRunning = false;
            _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _sharedMemory.Disconnect();
            ConnectionStatusChanged?.Invoke(this, reason);
            // Start retrying so we reconnect when LMU is restarted
            if (!_isDisposed)
                _reconnectTimer.Change(3000, 3000);
        }

        public void Stop()
        {
            _reconnectTimer.Change(Timeout.Infinite, Timeout.Infinite);
            LoseConnection("Disconnected");
        }

        public void TogglePause()
        {
            if (_isRunning) Stop();
            else Start();
        }

        private void PollTelemetry(object? state)
        {
            if (!_isRunning) return;

            try
            {
                var frame = _sharedMemory.ReadTelemetry();

                if (frame == null)
                {
                    LoseConnection("Connection lost — is LMU running?");
                    return;
                }

                _buffer.Add(frame);
                DetectLapChange(frame);
                NewFrameReceived?.Invoke(this, frame);
            }
            catch (Exception ex)
            {
                LoseConnection($"Error: {ex.Message}");
            }
        }

        // FR-16: Lap boundary detection using the real lap number from the sim.
        private void DetectLapChange(TelemetryFrame frame)
        {
            var currentLap = frame.CurrentLap;

            if (currentLap != _lastLapNumber && _lastLapNumber >= 0 && currentLap > _lastLapNumber)
            {
                // Prefer the sim's measured lap time; fall back to elapsed-time delta.
                double lapTime = frame.LastLapTime > 0 ? frame.LastLapTime : frame.Time - _lastLapStartTime;

                var lapInfo = new LapInfo
                {
                    LapNumber = _lastLapNumber,
                    StartTime = _lastLapStartTime,
                    EndTime = frame.Time,
                    LapTime = lapTime,
                    IsValid = true,
                    IsBestLap = frame.BestLapTime > 0 && Math.Abs(lapTime - frame.BestLapTime) < 0.001
                };

                Session.Laps.Add(lapInfo);
                Session.CurrentLapNumber = currentLap;
                _lastLapStartTime = frame.Time;

                LapCompleted?.Invoke(this, lapInfo);
            }

            _lastLapNumber = currentLap;
        }

        public void Dispose()
        {
            _isDisposed = true;
            _reconnectTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _isRunning = false;
            _sharedMemory?.Dispose();
            _pollTimer?.Dispose();
            _reconnectTimer?.Dispose();
        }
    }
}
