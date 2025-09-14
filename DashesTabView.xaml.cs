using System.Windows.Controls;

namespace LaunchPlugin
{
    public partial class DashesTabView : UserControl
    {
        public LalaLaunch Plugin { get; }

        public DashesTabView(LalaLaunch plugin)
        {
            InitializeComponent();
            this.Plugin = plugin;
            // The DataContext is now the entire plugin instance
            this.DataContext = plugin;
        }
    }
}