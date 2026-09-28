using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

/// <summary>Shows a failed test (negative delay) as a word instead of "-1".</summary>
public class DelayTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        return text.IsNotEmpty() && text.ToInt() < 0 ? ResUI.MacDashTimeout : text;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
