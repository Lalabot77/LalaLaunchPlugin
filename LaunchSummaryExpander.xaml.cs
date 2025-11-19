using System.Windows;
using System.Windows.Controls;

namespace LaunchPlugin
{
    public partial class LaunchSummaryExpander : UserControl
    {
        private bool _isSyncingToggle;

        public LaunchSummaryExpander()
        {
            InitializeComponent();
            Loaded += (s, e) => UpdateBodyVisibility();
            HeaderButton.Checked += HeaderButton_Toggled;
            HeaderButton.Unchecked += HeaderButton_Toggled;
        }

        private void HeaderButton_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncingToggle) return;
            IsExpanded = HeaderButton.IsChecked == true;
        }

        private void UpdateBodyVisibility()
        {
            if (BodyPresenter == null || HeaderButton == null) return;
            _isSyncingToggle = true;
            HeaderButton.IsChecked = IsExpanded;
            BodyPresenter.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            _isSyncingToggle = false;
        }

        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
            nameof(Header), typeof(string), typeof(LaunchSummaryExpander), new PropertyMetadata(string.Empty));

        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public static readonly DependencyProperty BodyContentProperty = DependencyProperty.Register(
            nameof(BodyContent), typeof(object), typeof(LaunchSummaryExpander), new PropertyMetadata(null));

        public object BodyContent
        {
            get => GetValue(BodyContentProperty);
            set => SetValue(BodyContentProperty, value);
        }

        public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
            nameof(IsExpanded), typeof(bool), typeof(LaunchSummaryExpander),
            new PropertyMetadata(true, OnIsExpandedChanged));

        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LaunchSummaryExpander expander)
            {
                expander.UpdateBodyVisibility();
            }
        }

        public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(
            nameof(IsHighlighted), typeof(bool), typeof(LaunchSummaryExpander), new PropertyMetadata(false));

        public bool IsHighlighted
        {
            get => (bool)GetValue(IsHighlightedProperty);
            set => SetValue(IsHighlightedProperty, value);
        }
    }
}
