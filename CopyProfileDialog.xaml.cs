using System.Collections.Generic;
using System.Windows;

namespace LaunchPlugin
{
    public partial class CopyProfileDialog : Window
    {
        // Public properties to hold the result
        public bool IsCreatingNew { get; private set; }
        public string NewProfileName { get; private set; }
        public CarProfile DestinationProfile { get; private set; }

        public CopyProfileDialog(CarProfile sourceProfile, IEnumerable<CarProfile> existingProfiles)
        {
            InitializeComponent();

            // Display the source profile name
            SourceProfileNameTextBlock.Text = sourceProfile.ProfileName;

            // Set default name for the new profile text box
            NewProfileNameTextBox.Text = $"{sourceProfile.ProfileName} (Copy)";

            // Populate the dropdown with all OTHER profiles
            ExistingProfilesComboBox.ItemsSource = existingProfiles;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (CreateNewRadioButton.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(NewProfileNameTextBox.Text))
                {
                    MessageBox.Show("Please enter a name for the new profile.", "Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                IsCreatingNew = true;
                NewProfileName = NewProfileNameTextBox.Text.Trim();
            }
            else // Copying to existing
            {
                if (ExistingProfilesComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Please select a destination profile from the list.", "Destination Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                IsCreatingNew = false;
                DestinationProfile = ExistingProfilesComboBox.SelectedItem as CarProfile;
            }

            // This closes the dialog and returns control to the main window
            this.DialogResult = true;
        }
    }
}