using System;
using System.Threading;
using LMU_Telemetry.Models;
using LMU_Telemetry.Telemetry;
using LMU_Telemetry.Telemetry.LMU_Telemetry.Telemetry;

namespace LMU_Telemetry.Services
{
    // Real telemetry service with LMU integration and lap detection
    public class TelemetryService : IDisposable
    {
        private readonly SharedMemoryReader _sharedMemory;
        private readonly TelemetryBuffer _buffer;
        private readonly System.Threading.Timer _timer;
        private bool _isRunning = false;
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
            // 60Hz telemetry polling (NFR-1)
            _timer = new System.Threading.Timer(PollTelemetry, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            if (_isRunning) return;

            // Try to connect to LMU
            if (_sharedMemory.Connect())
            {
                _isRunning = true;
                _timer.Change(0, 16); // ~60Hz (16ms interval)
                ConnectionStatusChanged?.Invoke(this, "Connected to LMU");
            }
            else
            {
                ConnectionStatusChanged?.Invoke(this, "Failed to connect - Is LMU running?");
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _sharedMemory.Disconnect();
            ConnectionStatusChanged?.Invoke(this, "Disconnected");
        }

        public void TogglePause()
        {
            if (_isRunning)
                Stop();
            else
                Start();
        }

        private void PollTelemetry(object? state)
        {
            if (!_isRunning) return;

            try
            {
                // FR-1: Read from shared memory
                var frame = _sharedMemory.ReadTelemetry();

                if (frame == null)
                {
                    // Connection lost (NFR-3: handle game closing)
                    if (_isRunning)
                    {
                        Stop();
                        ConnectionStatusChanged?.Invoke(this, "Connection lost - Game closed?");
                    }
                    return;
                }

                // FR-4: Add to buffer
                _buffer.Add(frame);

                // FR-16: Detect lap boundaries
                DetectLapChange(frame);

                // Notify listeners
                NewFrameReceived?.Invoke(this, frame);
            }
            catch (Exception ex)
            {
                // NFR-3: Handle errors gracefully
                if (_isRunning)
                {
                    Stop();
                    ConnectionStatusChanged?.Invoke(this, $"Error: {ex.Message}");
                }
            }
        }

        // FR-16: Lap boundary detection
        private void DetectLapChange(TelemetryFrame frame)
        {
            // Simple lap detection: check if time wraps or explicit lap number change
            // In real implementation, would use lap number from telemetry
            var currentLap = (int)(frame.Time / 120.0); // Assume ~2min laps for mock

            if (currentLap != _lastLapNumber && _lastLapNumber >= 0)
            {
                // Lap completed
                var lapInfo = new LapInfo
                {
                    LapNumber = _lastLapNumber,
                    StartTime = _lastLapStartTime,
                    EndTime = frame.Time,
                    LapTime = frame.Time - _lastLapStartTime,
                    IsValid = true,
                    IsBestLap = false // Would calculate based on best time
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
            Stop();
            _timer?.Dispose();
            _sharedMemory?.Dispose();
        }
    }
}
