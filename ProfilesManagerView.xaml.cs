// In file: ProfilesManagerView.xaml.cs
using System.Windows.Controls;
using System.Windows;

namespace LaunchPlugin
{
    public partial class ProfilesManagerView : UserControl
    {
        public ProfilesManagerView(ProfilesManagerViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;
        }

        private void SHButtonPrimary_Click(object sender, System.Windows.RoutedEventArgs e)
        {

        }

        private void ShiftRpmTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            var row = box?.DataContext as ShiftGearRow;
            if (row == null)
            {
                return;
            }

            row.SaveAction?.Invoke(box.Text);
        }
    }
}
