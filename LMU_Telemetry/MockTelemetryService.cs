using System;
using System.Threading;
using LMU_Telemetry.Models;
using LMU_Telemetry.Telemetry.LMU_Telemetry.Telemetry;

namespace LMU_Telemetry
{
    public class MockTelemetryService : IDisposable
    {
        private readonly FakeTelemetryGenerator _generator = new();
        private readonly TelemetryBuffer _buffer = new();
        private readonly System.Threading.Timer _timer;
        private bool _isRunning = false;

        public event EventHandler<TelemetryFrame>? NewFrameReceived;
        public TelemetryBuffer Buffer => _buffer;
        public bool IsRunning => _isRunning;

        public MockTelemetryService()
        {
            // 60Hz telemetry (NFR-1: ≥60Hz)
            _timer = new System.Threading.Timer(GenerateFrame, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            if (_isRunning) return;
            
            _isRunning = true;
            _timer.Change(0, 16); // ~60Hz (16ms interval)
        }

        public void Stop()
        {
            _isRunning = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void TogglePause()
        {
            if (_isRunning)
                Stop();
            else
                Start();
        }

        private void GenerateFrame(object? state)
        {
            if (!_isRunning) return;

            var frame = _generator.Next();
            _buffer.Add(frame);
            
            // Notify UI thread
            NewFrameReceived?.Invoke(this, frame);
        }

        public void Dispose()
        {
            Stop();
            _timer?.Dispose();
        }
    }
}