using Microsoft.Win32; // For OpenFileDialog
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // Needed for ObservableCollection
using System.ComponentModel; // Needed for INotifyPropertyChanged
using System.IO;        // For file operations
using System.Linq;
using System.Runtime.CompilerServices; // Needed for CallerMemberName
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LaunchPlugin
{
    // --- MODIFICATION: Implement INotifyPropertyChanged to allow UI to update automatically ---
    public partial class LaunchAnalysisControl : UserControl, INotifyPropertyChanged
    {
        // --- PROPERTIES FOR UI BINDING ---
        // These properties will be bound to the controls in the XAML file.

        private void ChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            // Use the full namespace for the FolderBrowserDialog
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select a folder containing launch trace files";
                dialog.SelectedPath = _telemetryTraceLogger.GetCurrentTracePath();

                // Use the full namespace for DialogResult
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    // Call our modified RefreshFileList method with the path the user selected
                    RefreshFileList(dialog.SelectedPath);
                }
            }
        }

        private void DeleteFile_Click(object sender, RoutedEventArgs e)
        {
            // First, check if a file is actually selected in the dropdown
            if (string.IsNullOrEmpty(SelectedFilePath))
            {
                MessageBox.Show("Please select a file from the list to delete.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Ask for confirmation - this is a critical safety step!
            MessageBoxResult result = MessageBox.Show(
                $"Are you sure you want to permanently delete this file?\n\n{SelectedFilePath}",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Construct the full path and delete the file
                    string fullPath = System.IO.Path.Combine(_telemetryTraceLogger.GetCurrentTracePath(), SelectedFilePath);
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                        SimHub.Logging.Current.Info($"[LaunchTrace] Deleted trace file: {fullPath}");
                    }
                    else
                    {
                        MessageBox.Show("The selected file could not be found on disk.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Error($"LaunchPlugin: Error deleting file: {ex.Message}");
                    MessageBox.Show($"An error occurred while trying to delete the file:\n\n{ex.Message}", "Deletion Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    // IMPORTANT: Refresh the file list to update the UI
                    RefreshFileList();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string prop = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        private ObservableCollection<string> _availableTraceFiles = new ObservableCollection<string>();
        public ObservableCollection<string> AvailableTraceFiles
        {
            get => _availableTraceFiles;
            set { _availableTraceFiles = value; OnPropertyChanged(); }
        }

        private string _selectedFilePath;
        public string SelectedFilePath
        {
            get => _selectedFilePath;
            set
            {
                if (_selectedFilePath != value)
                {
                    _selectedFilePath = value;
                    OnPropertyChanged();
                    // When the selection changes, load the corresponding file data.
                    LoadSelectedTraceFile();
                }
            }
        }

        private ObservableCollection<TelemetryDataRow> _rawTelemetryData = new ObservableCollection<TelemetryDataRow>();
        public ObservableCollection<TelemetryDataRow> RawTelemetryData
        {
            get => _rawTelemetryData;
            set { _rawTelemetryData = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> PlottableProperties { get; set; } = new ObservableCollection<string>
        {
            "None", "Speed (Kmh)", "RPMs", "Throttle (%)", "Paddle Clutch (%)", "Game Clutch (%)", // Renamed and Added
            "AccelerationSurge (G)", "TractionLoss (ShakeIT)"
        };

        private string _selectedPlotProperty = "Paddle Clutch (%)";
        public string SelectedPlotProperty
        {
            get => _selectedPlotProperty;
            set { _selectedPlotProperty = value; OnPropertyChanged(); Dispatcher.BeginInvoke(new Action(DrawGraph)); }
        }

        private string _selectedPlotProperty2 = "Speed (Kmh)";
        public string SelectedPlotProperty2
        {
            get => _selectedPlotProperty2;
            set { _selectedPlotProperty2 = value; OnPropertyChanged(); Dispatcher.BeginInvoke(new Action(DrawGraph)); }
        }

        private bool _normalizeData = false;
        public bool NormalizeData
        {
            get => _normalizeData;
            set { _normalizeData = value; OnPropertyChanged(); Dispatcher.BeginInvoke(new Action(DrawGraph)); }
        }

        // --- These private fields remain the same ---
        private List<TelemetryDataRow> _currentFullTelemetryData = new List<TelemetryDataRow>();
        private List<ChartDataPoint> _series1Points = new List<ChartDataPoint>();
        private List<ChartDataPoint> _series2Points = new List<ChartDataPoint>();

        private readonly Brush _series1Color = Brushes.LightBlue;
        private readonly Brush _series2Color = Brushes.OrangeRed;

        private Line _crosshairX;
        private Line _crosshairY;
        private TextBlock _dataReadout;

        private readonly TelemetryTraceLogger _telemetryTraceLogger;
        private string _currentAnalysisPath;

        // --- START: NEW PROPERTIES FOR RANGE SLIDER ---
        private double _sliderMaximum = 10;
        public double SliderMaximum
        {
            get => _sliderMaximum;
            set { _sliderMaximum = value; OnPropertyChanged(); }
        }

        private double _sliderMinTime = 0;
        public double SliderMinTime
        {
            get => _sliderMinTime;
            set { _sliderMinTime = value; OnPropertyChanged(); Dispatcher.BeginInvoke(new Action(DrawGraph)); }
        }

        private double _sliderMaxTime = 5;
        public double SliderMaxTime
        {
            get => _sliderMaxTime;
            set { _sliderMaxTime = value; OnPropertyChanged(); Dispatcher.BeginInvoke(new Action(DrawGraph)); }
        }
        // --- END: NEW PROPERTIES FOR RANGE SLIDER ---

        public LaunchAnalysisControl()
        {
            InitializeComponent();

            // DESIGNER GUARD — must be right here
            if (DesignerProperties.GetIsInDesignMode(this))
                return;
            // --- Set the DataContext to this class instance so bindings work ---
            this.DataContext = this;

            Loaded += (s, e) =>
            {
                GraphCanvas.MouseMove += GraphCanvas_MouseMove;
                GraphCanvas.SizeChanged += (s_size, e_size) => DrawGraph();
                GraphCanvas.MouseEnter += (s3, e3) =>
                {
                    _crosshairX.Visibility = Visibility.Visible;
                    _crosshairY.Visibility = Visibility.Visible;
                    _dataReadout.Visibility = Visibility.Visible;
                };
                GraphCanvas.MouseLeave += (s4, e4) =>
                {
                    _crosshairX.Visibility = Visibility.Collapsed;
                    _crosshairY.Visibility = Visibility.Collapsed;
                    _dataReadout.Visibility = Visibility.Collapsed;
                };

                // --- NEW: Attach event handler to the refresh button and load files on startup ---
                RefreshButton.Click += (s5, e5) => RefreshFileList();
                RefreshFileList();
            };
        }

        public LaunchAnalysisControl(TelemetryTraceLogger telemetryTraceLogger) : this()
        {
            _telemetryTraceLogger = telemetryTraceLogger;
        }

        // --- NEW METHOD: To refresh the list of available trace files ---
        private void RefreshFileList(string directoryPath = null)
        {
            if (DesignerProperties.GetIsInDesignMode(this)) return;
            if (_telemetryTraceLogger == null) return;

            // If no custom path is provided, get the path from settings.
            // Otherwise, use the path the user selected.
            string pathToScan = directoryPath ?? _telemetryTraceLogger.GetCurrentTracePath();

            // --- ADD THIS LINE to remember the path we are using ---
            _currentAnalysisPath = pathToScan;

            AvailableTraceFiles.Clear();

            // Check if the target directory exists before trying to get files.
            if (!Directory.Exists(pathToScan))
            {
                SimHub.Logging.Current.Warn($"LaunchAnalysis: Directory not found: {pathToScan}");
                return; // Exit if the directory doesn't exist.
            }

            var files = _telemetryTraceLogger.GetLaunchTraceFiles(pathToScan); // Pass the path to the logger
            foreach (var file in files)
            {
                AvailableTraceFiles.Add(System.IO.Path.GetFileName(file));
            }

            // Automatically select the first file in the list if any exist.
            if (AvailableTraceFiles.Any())
            {
                SelectedFilePath = AvailableTraceFiles.First();
            }
            else
            {
                // If no files are found, clear the display.
                ClearAllData();
            }
        }

        // --- NEW METHOD: To load the data from the selected file ---
        private void LoadSelectedTraceFile()
        {
            if (DesignerProperties.GetIsInDesignMode(this)) return;

            if (_telemetryTraceLogger == null || string.IsNullOrEmpty(SelectedFilePath))
            {
                ClearAllData();
                return;
            }

            string fullPath = System.IO.Path.Combine(_currentAnalysisPath, SelectedFilePath);

            if (!System.IO.File.Exists(fullPath))
            {
                SimHub.Logging.Current.Warn($"LaunchAnalysis: Selected file not found at {fullPath}");
                ClearAllData();
                return;
            }

            var (data, summary) = _telemetryTraceLogger.ReadLaunchTraceFile(fullPath);

            // --- MODIFICATION START ---
            // Set the slider's max range based on the data, then reset the selected values.
            if (data != null && data.Any())
            {
                var maxTime = data.Max(p => p.TimeElapsed);
                this.SliderMaximum = maxTime;
                this.SliderMinTime = 0;
                this.SliderMaxTime = maxTime; // Default to showing the full range
            }
            // --- MODIFICATION END ---

            RawTelemetryData = new ObservableCollection<TelemetryDataRow>(data);
            SetTelemetryData(data);
            DisplaySummary(summary);
        }

        // --- NEW METHOD: To clear all displayed data ---
        private void ClearAllData()
        {
            RawTelemetryData.Clear();
            _currentFullTelemetryData.Clear();
            SummaryGrid.Children.Clear();
            Redraw();
        }

        // --- To display summary data ---
        private void DisplaySummary(ParsedSummary summary)
        {
            SummaryGrid.Children.Clear(); // CLEAR previous summary
            if (summary == null) return;

            // --- Define all tooltips in a dictionary for easy management ---
            var tooltips = new Dictionary<string, string>
    {
        { "Car", "The model of the car used for the launch." },
        { "Track", "The track where the launch occurred." },
        { "Session", "The type of session (e.g., Race, Practice)." },
        { "Target Bite Point", "Your configured target bite point setting (%)." },
        { "Track Temp", "The temperature of the track surface." },
        { "Humidity", "The relative humidity of the air." },
        { "Fuel", "The percentage of fuel in the car at the time of the launch." },
        { "Surface Grip", "The state of rubber on the track surface (e.g., High usage)." },
        { "Timestamp", "The date and time the launch was recorded." },
        { "Reaction Time", "The time from the 'Go' signal to the first vehicle movement." },
        { "Launch RPM", "The engine speed (RPM) at the first moment of vehicle movement. The baseline for the bog-down check." },
        { "Throttle Start", "Your throttle position (%) when the clutch started to be released." },
        { "Clutch Release", "The time taken (in milliseconds) to release the clutch from 95% to 5%." },
        { "Min RPM", "The lowest RPM the engine dropped to during the 0-100 km/h acceleration phase." },
        { "Throttle Mod", "The difference between the maximum and minimum throttle position during the 0-100 km/h acceleration phase." },
        { "0-100 Time", "The time taken (in milliseconds) to accelerate from (near) zero to 100 km/h." },
        { "RPM Opt D", "The difference between the RPM at clutch release and your configured 'Target Launch RPM'." },
        { "Traction Loss", "The maximum raw value reported by the traction loss ShakeIt effect during the launch." },
        { "Anti-Stall?", "Indicates if the game's anti-stall assist was detected overriding your clutch input." },
        { "Bogged?", "Indicates if the engine RPM dropped below your configured 'Bog Down Factor' threshold." },
        { "Wheel Spin?", "Indicates if significant wheel spin was detected (based on traction loss)." }
    };

            var grid = new Grid();
            // Define our 6-column layout with spacers
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });           // Col 0: Label 1
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 1: Value 1
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });          // Col 2: Spacer
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });           // Col 3: Label 2
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 4: Value 2
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });          // Col 5: Spacer
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });           // Col 6: Label 3
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 7: Value 3

            // Helper function to add a full 6-column row of data to the grid
            void addRow(int rowIndex, string label1, string value1, string label2, string value2, string label3, string value3)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                void addPair(int col, int row, string labelText, string valueText)
                {
                    if (string.IsNullOrEmpty(labelText)) return;
                    var label = new TextBlock { Text = labelText, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
                    if (tooltips.ContainsKey(labelText)) { label.ToolTip = tooltips[labelText]; }
                    var value = new TextBlock { Text = valueText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 2, 0, 2) };
                    Grid.SetRow(label, row); Grid.SetColumn(label, col);
                    Grid.SetRow(value, row); Grid.SetColumn(value, col + 1);
                    grid.Children.Add(label);
                    grid.Children.Add(value);
                }

                addPair(0, rowIndex, label1, value1);
                addPair(3, rowIndex, label2, value2);
                addPair(6, rowIndex, label3, value3);
            }

            // Helper function to add a divider line
            void addDivider(int rowIndex)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var border = new Border
                {
                    Height = 1,
                    Background = Brushes.DarkGray,
                    Margin = new Thickness(0, 8, 0, 8),
                    Opacity = 0.5
                };
                Grid.SetRow(border, rowIndex);
                Grid.SetColumnSpan(border, 8);
                grid.Children.Add(border);
            }

            // --- Build the UI row by row ---
            int r = 0; // Current row index

            // --- NEW REORGANIZED LAYOUT ---

            // Top Section
            grid.RowDefinitions.Add(new RowDefinition());
            addRow(r++, "Car", summary.Car, "Track", summary.Track, "Session", summary.Session);

            grid.RowDefinitions.Add(new RowDefinition());
            // Target Bite Point replaces Air Temp in the left column
            addRow(r++, "Target Bite Point", summary.TargetBitePoint + " %", "Track Temp", summary.TrackTemp, "Humidity", summary.Humidity + " %");

            grid.RowDefinitions.Add(new RowDefinition());
            addRow(r++, "Fuel", summary.Fuel + " %", "Surface Grip", summary.SurfaceGrip, "Timestamp", summary.TimestampUtc);

            addDivider(r++);

            // Middle Section
            grid.RowDefinitions.Add(new RowDefinition());
            // Reaction Time now leads the driver performance metrics
            addRow(r++, "Reaction Time", summary.ReactionTimeMs + " ms", "Launch RPM", summary.LaunchRpm, "Throttle Start", summary.ThrottleAtLaunchZoneStart + " %");

            grid.RowDefinitions.Add(new RowDefinition());
            addRow(r++, "Clutch Release", summary.ClutchReleaseTime + " ms", "Min RPM", summary.MinRpm, "Throttle Mod", summary.ThrottleModulationDelta);

            grid.RowDefinitions.Add(new RowDefinition());
            addRow(r++, "0-100 Time", summary.AccelTime100Ms + " ms", "RPM Opt D", summary.RpmDeltaToOptimal, "Traction Loss", summary.TractionLossRaw);

            addDivider(r++);

            // Bottom Section
            grid.RowDefinitions.Add(new RowDefinition());
            // Anti-Stall? now takes the spot previously held by Reaction Time
            addRow(r++, "Anti-Stall?", summary.AntiStallDetected, "Bogged?", summary.Bogged, "Wheel Spin?", summary.WheelSpin);

            SummaryGrid.Children.Add(grid);
        }

        private void Redraw()
        {
            // This method is now much simpler as the properties handle the updates.
            DrawGraph();
        }

        private void DrawGraph()
        {
            // CLEAR previous elements
            if (GraphCanvas == null) return;
            GraphCanvas.Children.Clear();
            if (YAxisLabelsPanel == null) return;
            YAxisLabelsPanel.Children.Clear();
            if (XAxisLabelsPanel == null) return;
            XAxisLabelsPanel.Children.Clear();

            // Initialize Crosshair elements
            _crosshairX = new Line { Stroke = Brushes.IndianRed, StrokeThickness = 0.75, Visibility = Visibility.Collapsed };
            _crosshairY = new Line { Stroke = Brushes.IndianRed, StrokeThickness = 0.75, Visibility = Visibility.Collapsed };
            _dataReadout = new TextBlock { Background = new SolidColorBrush(Color.FromArgb(200, 10, 10, 10)), Foreground = Brushes.White, Padding = new Thickness(4), Visibility = Visibility.Collapsed };

            if (_currentFullTelemetryData == null || !_currentFullTelemetryData.Any() || GraphCanvas.ActualWidth <= 0 || string.IsNullOrEmpty(this.SelectedPlotProperty)) return;

            // Use the new public properties for the time range
            double minTime = this.SliderMinTime;
            double maxTime = this.SliderMaxTime;

            var visiblePoints = _currentFullTelemetryData.Where(p => p.TimeElapsed >= minTime && p.TimeElapsed <= maxTime).ToList();
            if (!visiblePoints.Any()) return;

            List<ChartDataPoint> getPointsForProperty(string propertyName, List<TelemetryDataRow> sourceData)
            {
                var points = new List<ChartDataPoint>();
                if (string.IsNullOrEmpty(propertyName) || propertyName == "None") return points;
                foreach (var row in sourceData)
                {
                    double value;
                    switch (propertyName)
                    {
                        case "Speed (Kmh)": value = row.SpeedKmh; break;
                        case "RPMs": value = row.RPMs; break;
                        case "Throttle (%)": value = row.Throttle; break;
                        case "Paddle Clutch (%)": value = row.PaddleClutch; break;
                        case "Game Clutch (%)": value = row.GameClutch; break;
                        case "AccelerationSurge (G)": value = row.AccelerationSurge; break;
                        case "TractionLoss (ShakeIT)": value = row.TractionLoss; break;
                        default: continue;
                    }
                    points.Add(new ChartDataPoint { TimeElapsed = row.TimeElapsed, Value = value });
                }
                return points;
            }

            _series1Points = getPointsForProperty(this.SelectedPlotProperty, visiblePoints);
            _series2Points = getPointsForProperty(this.SelectedPlotProperty2, visiblePoints);

            // NOTE: The block that modified the data for normalization has been removed.
            // _series1Points and _series2Points will now ALWAYS contain the true, raw values.

            var allPointsForRange = _series1Points.Concat(_series2Points).ToList();
            double timeRange = maxTime - minTime;
            if (timeRange <= 0) timeRange = 1;

            double minValue, maxValue;
            if (this.NormalizeData) { minValue = 0; maxValue = 100; }
            else
            {
                minValue = allPointsForRange.Any() ? allPointsForRange.Min(p => p.Value) : 0;
                maxValue = allPointsForRange.Any() ? allPointsForRange.Max(p => p.Value) : 1;
            }
            double valueRange = maxValue - minValue;
            if (valueRange == 0)
            {
                valueRange = Math.Abs(maxValue) > 0 ? Math.Abs(maxValue) : 1;
                minValue = maxValue - (valueRange / 2); maxValue = minValue + valueRange;
                valueRange = maxValue - minValue;
            }

            double buffer = valueRange * 0.10;
            minValue -= buffer / 2;
            maxValue += buffer / 2;
            valueRange = maxValue - minValue;
            if (valueRange == 0) valueRange = 1;

            double canvasWidth = GraphCanvas.ActualWidth;
            double canvasHeight = GraphCanvas.ActualHeight;
            int gridLineCount = 5;

            // Drawing Y-axis and X-axis grid (code is unchanged)
            for (int i = 0; i <= gridLineCount; i++)
            {
                double value = minValue + (valueRange / gridLineCount * i); double y = canvasHeight - ((value - minValue) / valueRange) * canvasHeight;
                var yGridLine = new Line { X1 = 0, Y1 = y, X2 = canvasWidth, Y2 = y, Stroke = Brushes.DarkGray, StrokeThickness = 0.5, StrokeDashArray = new DoubleCollection { 2, 4 } };
                GraphCanvas.Children.Add(yGridLine); var yLabelText = this.NormalizeData ? value.ToString("F0") + "%" : value.ToString("F1");
                var yLabel = new TextBlock { Text = yLabelText, Foreground = Brushes.LightGray, FontSize = 10, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, y - 8, 4, 0) };
                YAxisLabelsPanel.Children.Add(yLabel);
            }
            for (int i = 0; i <= gridLineCount; i++)
            {
                double time = minTime + (timeRange / gridLineCount * i);
                double x = ((time - minTime) / timeRange) * canvasWidth;
                var xGridLine = new Line { X1 = x, Y1 = 0, X2 = x, Y2 = canvasHeight, Stroke = Brushes.DarkGray, StrokeThickness = 0.5, StrokeDashArray = new DoubleCollection { 2, 4 } };
                GraphCanvas.Children.Add(xGridLine);
                var xLabel = new TextBlock { Text = time.ToString("F1") + "s", Foreground = Brushes.LightGray, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(x - 5, 2, 0, 0) };
                XAxisLabelsPanel.Children.Add(xLabel);
            }

            // The drawPolyline function now handles normalization just for drawing
            void drawPolyline(List<ChartDataPoint> points, Brush color, string propertyName)
            {
                if (points.Count < 2) return;
                var polyline = new Polyline { Stroke = color, StrokeThickness = 2 };

                double seriesMin = 0, seriesRange = 1;
                if (this.NormalizeData && points.Any())
                {
                    // --- NEW LOGIC: Special, more realistic handling for RPM normalization ---
                    if (propertyName == "RPMs")
                    {
                        seriesMin = 0; // Anchor the bottom of the scale at 0 RPM
                        seriesRange = points.Max(p => p.Value); // The top is the max RPM in the launch
                    }
                    else // Original logic for all other properties
                    {
                        seriesMin = points.Min(p => p.Value);
                        seriesRange = points.Max(p => p.Value) - seriesMin;
                    }

                    if (seriesRange == 0) seriesRange = 1;
                }

                foreach (var point in points)
                {
                    double displayValue = point.Value;
                    if (this.NormalizeData)
                    {
                        displayValue = ((point.Value - seriesMin) / seriesRange) * 100;
                    }

                    double x = ((point.TimeElapsed - minTime) / timeRange) * canvasWidth;
                    double y = canvasHeight - ((displayValue - minValue) / valueRange) * canvasHeight;
                    polyline.Points.Add(new Point(x, y));
                }
                GraphCanvas.Children.Add(polyline);
            }

            drawPolyline(_series1Points, _series1Color, this.SelectedPlotProperty);
            drawPolyline(_series2Points, _series2Color, this.SelectedPlotProperty2);

            GraphCanvas.Children.Add(_crosshairX);
            GraphCanvas.Children.Add(_crosshairY);
            GraphCanvas.Children.Add(_dataReadout);
        }

        private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (GraphCanvas.ActualWidth == 0 || !_currentFullTelemetryData.Any()) return;

            var pos = e.GetPosition(GraphCanvas);
            _crosshairX.X1 = 0;
            _crosshairX.X2 = GraphCanvas.ActualWidth;
            _crosshairX.Y1 = _crosshairX.Y2 = pos.Y;

            _crosshairY.Y1 = 0;
            _crosshairY.Y2 = GraphCanvas.ActualHeight;
            _crosshairY.X1 = _crosshairY.X2 = pos.X;

            double timeRange = this.SliderMaxTime - this.SliderMinTime;
            if (timeRange <= 0) timeRange = 1;

            double timeAtCursor = this.SliderMinTime + (pos.X / GraphCanvas.ActualWidth) * timeRange;

            // Find the closest data point in each series to the cursor's time
            var point1 = _series1Points.Any() ? _series1Points.OrderBy(p => Math.Abs(p.TimeElapsed - timeAtCursor)).First() : null;
            var point2 = _series2Points.Any() ? _series2Points.OrderBy(p => Math.Abs(p.TimeElapsed - timeAtCursor)).First() : null;

            // Build the text for the data readout box
            var readoutLines = new List<string>
            {
                $"Time: {timeAtCursor:F2}s"
            };

            if (point1 != null)
            {
                readoutLines.Add($"{this.SelectedPlotProperty}: {point1.Value:F1}");
            }
            if (point2 != null)
            {
                readoutLines.Add($"{this.SelectedPlotProperty2}: {point2.Value:F1}");
            }

            _dataReadout.Text = string.Join(Environment.NewLine, readoutLines);

            // --- NEW: Smart positioning logic for the data readout box ---
            double padding = 15; // The space between the cursor and the box
            double finalX = pos.X + padding;
            double finalY = pos.Y + padding;

            // If the box would go off the RIGHT edge, flip it to the LEFT of the cursor
            if (finalX + _dataReadout.ActualWidth > GraphCanvas.ActualWidth)
            {
                finalX = pos.X - _dataReadout.ActualWidth - padding;
            }

            // If the box would go off the BOTTOM edge, flip it ABOVE the cursor
            if (finalY + _dataReadout.ActualHeight > GraphCanvas.ActualHeight)
            {
                finalY = pos.Y - _dataReadout.ActualHeight - padding;
            }

            Canvas.SetLeft(_dataReadout, finalX);
            Canvas.SetTop(_dataReadout, finalY);
        }

        public void SetTelemetryData(List<TelemetryDataRow> data)
        {
            _currentFullTelemetryData = data ?? new List<TelemetryDataRow>();
            Redraw();
        }

        // --- These properties were moved to the top to be public and bindable ---
        // public string SelectedPlotProperty { get; set; }
        // public string SelectedPlotProperty2 { get; set; }
    }

    // This class remains the same
    public class ChartDataPoint
    {
        public double TimeElapsed { get; set; }
        public double Value { get; set; }
    }
}