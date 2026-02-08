using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace LaunchPlugin
{
    public partial class GlobalSettingsView : UserControl
    {
        public LalaLaunch Plugin { get; }

        public GlobalSettingsView(LalaLaunch plugin)
        {
            InitializeComponent();
            Plugin = plugin;
            DataContext = plugin;
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

            Plugin.Settings.Friends.Add(new LaunchPluginFriendEntry { Name = "Friend", UserId = 0, IsTeammate = false });
            Plugin.NotifyFriendsChanged();
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
            Plugin.NotifyFriendsChanged();
        }

        private void FriendsGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            Plugin?.NotifyFriendsChanged();
        }
    }
}
