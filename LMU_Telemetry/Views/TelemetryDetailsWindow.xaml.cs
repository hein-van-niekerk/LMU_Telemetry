using System.Collections.Generic;
using System.Windows;
using LMU.Telemetry.Core.Models;

namespace LMU_Telemetry.Views;

public partial class TelemetryDetailsWindow : Window
{
    private bool _suppressClose = false;

    public TelemetryDetailsWindow()
    {
        InitializeComponent();
    }

    // Called every frame from MainWindow when the detail window is visible.
    public void PushFrame(TelemetryFrame frame, IReadOnlyList<TelemetryFrame> allFrames)
    {
        if (frame == null) return;

        DataView.PushFrame(frame, allFrames);
        AnalysisView.PushFrame(frame, allFrames);

        // Header subtitle: lap + time
        if (allFrames != null && allFrames.Count > 0)
        {
            double sessionTime = frame.Time - allFrames[0].Time;
            HeaderSubtitle.Text = $"  ·  LAP {frame.CurrentLap + 1}  ·  {sessionTime:F1}s  ·  {allFrames.Count:N0} frames";
        }
    }

    // Hide instead of close so it can be re-shown without rebuilding.
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_suppressClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    // Called by MainWindow when the app is actually exiting.
    public void ForceClose()
    {
        _suppressClose = true;
        Close();
    }
}
