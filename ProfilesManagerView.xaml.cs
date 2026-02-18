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
            DataContext = viewModel;
            Loaded += ProfilesManagerView_Loaded;
            Unloaded += ProfilesManagerView_Unloaded;
        }

        private void ProfilesManagerView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProfilesManagerViewModel vm)
                vm.StartShiftAssistRuntimeTimer();
        }

        private void ProfilesManagerView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProfilesManagerViewModel vm)
                vm.StopShiftAssistRuntimeTimer();
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

        private void ShiftLockCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            var box = sender as CheckBox;
            var row = box?.DataContext as ShiftGearRow;
            if (row == null)
            {
                return;
            }

            row.SetLockAction?.Invoke(true);
        }

        private void ShiftLockCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            var box = sender as CheckBox;
            var row = box?.DataContext as ShiftGearRow;
            if (row == null)
            {
                return;
            }

            row.SetLockAction?.Invoke(false);
        }
    }
}
