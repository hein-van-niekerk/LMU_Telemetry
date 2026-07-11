using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LMU.Telemetry.Core.Models;

namespace LMU_Telemetry.ViewModels
{
    public class TrackViewModel : ObservableObject
    {
        private ObservableCollection<TelemetryFrame> _trackPath = new();
        private TelemetryFrame? _currentPosition;
        private bool _isLiveMode = true;

        public ObservableCollection<TelemetryFrame> TrackPath
        {
            get => _trackPath;
            set => SetProperty(ref _trackPath, value);
        }

        public TelemetryFrame? CurrentPosition
        {
            get => _currentPosition;
            set => SetProperty(ref _currentPosition, value);
        }

        public bool IsLiveMode
        {
            get => _isLiveMode;
            set => SetProperty(ref _isLiveMode, value);
        }

        public void UpdateTrack(IReadOnlyList<TelemetryFrame> frames)
        {
            TrackPath.Clear();
            foreach (var frame in frames)
            {
                TrackPath.Add(frame);
            }
        }

        public void UpdateCurrentPosition(TelemetryFrame frame)
        {
            CurrentPosition = frame;
        }
    }
}
