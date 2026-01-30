using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LMU_Telemetry.Models;

namespace LMU_Telemetry.ViewModels
{
    public class InputViewModel : ObservableObject
    {
        private float _throttle;
        private float _brake;
        private float _steering;
        private int _gear;
        private float _rpm;
        private ObservableCollection<TelemetryFrame> _inputHistory = new();

        public float Throttle
        {
            get => _throttle;
            set => SetProperty(ref _throttle, value);
        }

        public float Brake
        {
            get => _brake;
            set => SetProperty(ref _brake, value);
        }

        public float Steering
        {
            get => _steering;
            set => SetProperty(ref _steering, value);
        }

        public int Gear
        {
            get => _gear;
            set => SetProperty(ref _gear, value);
        }

        public float Rpm
        {
            get => _rpm;
            set => SetProperty(ref _rpm, value);
        }

        public ObservableCollection<TelemetryFrame> InputHistory
        {
            get => _inputHistory;
            set => SetProperty(ref _inputHistory, value);
        }

        public void Update(TelemetryFrame frame)
        {
            Throttle = frame.Throttle;
            Brake = frame.Brake;
            Steering = frame.Steering;
            Gear = frame.Gear;
            Rpm = frame.Rpm;

            // Keep limited history for graphs
            InputHistory.Add(frame);
            if (InputHistory.Count > 600) // ~10 seconds @ 60Hz
            {
                InputHistory.RemoveAt(0);
            }
        }
    }
}
