using GameReaderCommon;
using SimHub.Plugins; // SimHub.Plugins is essential
using System;
using System.Collections.Generic; // For List<T>
using System.Globalization; // For CultureInfo, used in parsing for DataRow
using System.IO; // For file operations
using System.Linq; // For LINQ operations
using System.Windows.Controls; // For UserControl, CheckBox, TextBlock, TextBox, Button, StackPanel
using System.Windows.Data; // For Binding
using System.Windows; // For Thickness, Orientation, VerticalAlignment, HorizontalAlignment, FontWeights, FontStyle
using System.Windows.Media; // For Brushes
using System.ComponentModel; // For INotifyPropertyChanged support (even if not directly used in TelemetryTraceLogger, it's good practice for this file)
using System.Diagnostics; // For Stopwatch (if any debug/timing was in this file, though it's mainly in Plugin.cs)
using System.Runtime.CompilerServices; // For CallerMemberName (if any INotifyPropertyChanged was in this file)
using LaunchPlugin; // To resolve the 'Plugin' type not found error

namespace SimHub.Plugins
{
    // --- IMPORTANT: TelemetryDataRow MUST be defined directly within the SimHub.Plugins namespace ---
    // It should NOT be nested inside TelemetryTraceLogger or any other class.
    // Ensure this class definition starts directly after the 'namespace SimHub.Plugins' block,
    // like TelemetryTraceLogger and TelemetryTraceLoggerSettings.

    /// <summary>
    /// Represents a single row of telemetry data from a launch trace.
    /// </summary>
    public class TelemetryDataRow
    {
        public DateTime Timestamp { get; set; }
        public double TimeElapsed { get; set; } // Derived: Time from the start of the trace, in seconds
        public double SpeedKmh { get; set; }
        public double GameClutch { get; set; } // Anti-stall influenced game
        public double PaddleClutch { get; set; } // Pure paddle value
        public double Throttle { get; set; }
        public double RPMs { get; set; }
        public double AccelerationSurge { get; set; }
        public double TractionLoss { get; set; }
    }
}