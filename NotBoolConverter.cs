// NotBoolConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace LaunchPlugin
{
    public class NotBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : (object)false;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : (object)Binding.DoNothing;
    }
}