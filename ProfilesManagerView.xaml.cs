// In file: ProfilesManagerView.xaml.cs
using System.Windows.Controls;

namespace LaunchPlugin
{
    public partial class ProfilesManagerView : UserControl
    {
        public ProfilesManagerView(ProfilesManagerViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;
        }
    }
}