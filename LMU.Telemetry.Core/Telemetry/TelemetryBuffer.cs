using System;
using System.Collections.Generic;
using System.Linq;
using LMU.Telemetry.Core.Models;

namespace LMU.Telemetry.Core.Telemetry
{
    public class TelemetryBuffer
    {
        private readonly List<TelemetryFrame> _frames = new();
        private readonly int _maxFrames;
        private int _currentIndex = 0;

        public TelemetryBuffer(int maxFrames = 60 * 300) // ~5 min @ 60Hz
        {
            _maxFrames = maxFrames;
        }

        public void Add(TelemetryFrame frame)
        {
            _frames.Add(frame);
            if (_frames.Count > _maxFrames)
                _frames.RemoveAt(0);

            // Auto-advance current index to latest frame for live mode
            _currentIndex = Math.Max(0, _frames.Count - 1);
        }

        public void AddRange(IEnumerable<TelemetryFrame> frames)
        {
            // Bulk load without size limit - for loading recordings from file
            _frames.AddRange(frames);
            _currentIndex = 0; // Start at beginning for replay
        }

        public IReadOnlyList<TelemetryFrame> Frames => _frames;

        // For time scrubbing (FR-5)
        public int CurrentIndex
        {
            get => _currentIndex;
            set => _currentIndex = Math.Clamp(value, 0, _frames.Count - 1);
        }

        public TelemetryFrame? CurrentFrame =>
            _frames.Count > 0 && _currentIndex < _frames.Count ? _frames[_currentIndex] : null;

        public TelemetryFrame? GetClosestByTime(double time)
        {
            return _frames
                .OrderBy(f => Math.Abs(f.Time - time))
                .FirstOrDefault();
        }

        // Get index of frame closest to given time
        public int GetIndexByTime(double time)
        {
            if (_frames.Count == 0) return 0;

            var closestFrame = GetClosestByTime(time);
            return closestFrame != null ? _frames.IndexOf(closestFrame) : 0;
        }

        // For clicking on track to scrub (FR-6)
        public int GetIndexByPosition(float x, float y, float tolerance = 50f)
        {
            for (int i = 0; i < _frames.Count; i++)
            {
                var frame = _frames[i];
                var distance = Math.Sqrt(Math.Pow(frame.PosX - x, 2) + Math.Pow(frame.PosY - y, 2));
                if (distance <= tolerance)
                    return i;
            }
            return _currentIndex;
        }

        public void Clear()
        {
            _frames.Clear();
            _currentIndex = 0;
        }

        public bool HasData => _frames.Count > 0;
    }
}
