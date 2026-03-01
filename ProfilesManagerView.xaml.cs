// In file: ProfilesManagerView.xaml.cs
using System.Windows.Controls;
using System.Windows;
using System;

namespace LaunchPlugin
{
    public partial class ProfilesManagerView : UserControl
    {
        private bool _suppressShiftStackSelectionChanged;

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

        private void ShiftStackSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressShiftStackSelectionChanged)
            {
                return;
            }

            var combo = sender as ComboBox;
            var vm = DataContext as ProfilesManagerViewModel;
            if (combo == null || vm == null)
            {
                return;
            }

            string targetId = combo.SelectedItem as string;
            string beforeId = vm.SelectedShiftStackId;
            if (vm.ConfirmSwitchShiftStackCommand != null && vm.ConfirmSwitchShiftStackCommand.CanExecute(targetId))
            {
                vm.ConfirmSwitchShiftStackCommand.Execute(targetId);
            }
            else
            {
                vm.ConfirmSwitchShiftStack(targetId);
            }
            bool changed = string.Equals(vm.SelectedShiftStackId, targetId, StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(beforeId, vm.SelectedShiftStackId, StringComparison.OrdinalIgnoreCase);

            _suppressShiftStackSelectionChanged = true;
            try
            {
                if (!string.Equals(combo.SelectedItem as string, vm.SelectedShiftStackId, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = vm.SelectedShiftStackId;
                }
            }
            finally
            {
                _suppressShiftStackSelectionChanged = false;
            }

            if (!changed)
            {
                return;
            }
        }
    }
}
