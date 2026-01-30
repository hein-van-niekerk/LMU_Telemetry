using System.Configuration;
using System.Data;
using System.Windows;
using System.Diagnostics;

namespace LMU_Telemetry;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Add console output for Debug.WriteLine statements
        Trace.Listeners.Add(new TextWriterTraceListener(System.Console.Out));
        Trace.AutoFlush = true;
        
        System.Diagnostics.Debug.WriteLine("=== LMU Telemetry Application Started ===");
        System.Console.WriteLine("=== LMU Telemetry Application Started ===");
    }
}

