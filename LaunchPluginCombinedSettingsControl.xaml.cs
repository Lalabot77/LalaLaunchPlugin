// In file: LaunchPluginCombinedSettingsControl.xaml.cs
using SimHub.Plugins.Styles;
using System.Windows.Controls;

namespace LaunchPlugin
{
    public partial class LaunchPluginCombinedSettingsControl : UserControl
    {
        public LaunchPluginCombinedSettingsControl(LalaLaunch mainPluginInstance, TelemetryTraceLogger telemetryTraceLoggerService)
        {
            InitializeComponent();

            var dashesTab = new SHTabItem
            {
                Header = "DASH CONTROL",
                Content = new DashesTabView(mainPluginInstance)
            };
            MainTabControl.Items.Add(dashesTab);

            var settingsTab = new SHTabItem
            {
                Header = "LAUNCH SETTINGS",
                Content = new LaunchPluginSettingsUI(mainPluginInstance, telemetryTraceLoggerService)
            };
            MainTabControl.Items.Add(settingsTab);

            var launchAnalysisTab = new SHTabItem
            {
                Header = "POST LAUNCH ANALYSIS",
                Content = new LaunchAnalysisControl(telemetryTraceLoggerService)
            };
            MainTabControl.Items.Add(launchAnalysisTab);

            var fuelTab = new SHTabItem
            {
                Header = "FUEL",
                Content = new FuelCalculatorView(mainPluginInstance.FuelCalculator)
            };

            MainTabControl.Items.Add(fuelTab);
            // The Profiles tab now gets its content from a view that is given the new ViewModel
            var profilesTab = new SHTabItem
            {
                Header = "PROFILES",
                Content = new ProfilesManagerView(mainPluginInstance.ProfilesViewModel)
            };
            MainTabControl.Items.Add(profilesTab);
            
        }
    }
}