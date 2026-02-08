using SimHub.Plugins;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace LaunchPlugin
{
    public partial class LaunchPluginSettingsUI : UserControl
    {
        public LalaLaunch Plugin { get; }
        public TelemetryTraceLogger TelemetryService { get; }
        public string DefaultTraceLogPath { get; }
        public string DefaultSummaryLogPath { get; }

        public LaunchPluginSettingsUI(LalaLaunch plugin, TelemetryTraceLogger telemetry)
        {
            Plugin = plugin;
            TelemetryService = telemetry;
            DefaultTraceLogPath = telemetry.GetCurrentTracePath();
            DefaultSummaryLogPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Logs", "LaunchData");
            InitializeComponent();
            // The DataContext is now the entire plugin instance to access ActiveProfile and Settings
            DataContext = plugin;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // Use the full namespace for the FolderBrowserDialog
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select a folder for log files";

                // Use the full namespace for DialogResult
                System.Windows.Forms.DialogResult result = dialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    // Find the TextBox this button is related to
                    if ((sender as Button)?.Tag is TextBox targetTextBox)
                    {
                        targetTextBox.Text = dialog.SelectedPath;
                        // Force the binding to update the source
                        targetTextBox.GetBindingExpression(TextBox.TextProperty).UpdateSource();
                    }
                }
            }
        }

        private void AddFriend_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin?.Settings == null)
            {
                return;
            }

            if (Plugin.Settings.Friends == null)
            {
                Plugin.Settings.Friends = new ObservableCollection<LaunchPluginFriendEntry>();
            }

            Plugin.Settings.Friends.Add(new LaunchPluginFriendEntry { Label = "Friend", UserId = 0 });
        }

        private void RemoveFriend_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin?.Settings?.Friends == null)
            {
                return;
            }

            var entry = (sender as FrameworkElement)?.DataContext as LaunchPluginFriendEntry;
            if (entry == null)
            {
                return;
            }

            Plugin.Settings.Friends.Remove(entry);
        }
    }

    
}
