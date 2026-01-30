using System;
using System.Collections.Generic;
using System.Windows.Input;
using LMU_Telemetry.Models;
using LMU_Telemetry.Telemetry.LMU_Telemetry.Telemetry;

namespace LMU_Telemetry.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly TelemetryBuffer _buffer;
        private TelemetryFrame? _currentFrame;
        private string _statusText = "Stopped";
        private string _connectionStatus = "Disconnected";
        private int _frameCount;
        private int _currentLap;
        private bool _isLiveMode = true;

        public TelemetryBuffer Buffer => _buffer;
        
        public TrackViewModel Track { get; }
        public InputViewModel Input { get; }
        public SessionState Session { get; }
        public CarState Car { get; }

        public TelemetryFrame? CurrentFrame
        {
            get => _currentFrame;
            set => SetProperty(ref _currentFrame, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        public int FrameCount
        {
            get => _frameCount;
            set => SetProperty(ref _frameCount, value);
        }

        public int CurrentLap
        {
            get => _currentLap;
            set => SetProperty(ref _currentLap, value);
        }

        public bool IsLiveMode
        {
            get => _isLiveMode;
            set
            {
                if (SetProperty(ref _isLiveMode, value))
                {
                    Track.IsLiveMode = value;
                }
            }
        }

        public MainViewModel()
        {
            _buffer = new TelemetryBuffer();
            Track = new TrackViewModel();
            Input = new InputViewModel();
            Session = new SessionState();
            Car = new CarState();
        }

        public void Update(TelemetryFrame frame)
        {
            _buffer.Add(frame);
            CurrentFrame = frame;
            FrameCount = _buffer.Frames.Count;

            // Update child ViewModels
            if (IsLiveMode)
            {
                Track.UpdateCurrentPosition(frame);
                Input.Update(frame);
            }
        }

        public void ScrubToIndex(int index)
        {
            _buffer.CurrentIndex = index;
            CurrentFrame = _buffer.CurrentFrame;
            IsLiveMode = false;

            if (CurrentFrame != null)
            {
                Track.UpdateCurrentPosition(CurrentFrame);
                Input.Update(CurrentFrame);
            }
        }

        public void ScrubToPosition(float x, float y, float tolerance = 30f)
        {
            var index = _buffer.GetIndexByPosition(x, y, tolerance);
            ScrubToIndex(index);
        }

        public void ReturnToLiveMode()
        {
            IsLiveMode = true;
            _buffer.CurrentIndex = Math.Max(0, _buffer.Frames.Count - 1);
            CurrentFrame = _buffer.CurrentFrame;
        }
    }
}
