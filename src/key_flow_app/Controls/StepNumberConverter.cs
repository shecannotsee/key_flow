using System.Globalization;
using System.Windows.Data;

namespace key_flow_app.Controls;

public sealed class StepNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int index ? (index + 1).ToString(culture) : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
