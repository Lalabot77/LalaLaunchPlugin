using System.Windows;
using System.Windows.Controls;

namespace LaunchPlugin
{
    /// <summary>
    /// Interaction logic for FuelCalculatorView.xaml
    /// </summary>
    public partial class FuelCalculatorView : UserControl
    {
        // Store a reference to the FuelCalcs engine
        private readonly FuelCalcs _fuelCalcs;
        public FuelCalculatorView(FuelCalcs fuelCalcs)
        {
            InitializeComponent();
            _fuelCalcs = fuelCalcs;
            this.DataContext = _fuelCalcs;
        }

        private void PersonalBestButton_Click(object sender, RoutedEventArgs e)
        {
            _fuelCalcs.LoadPersonalBestAsRacePace();
        }

    }
}
