using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

public class DelayColorConverter : IValueConverter
{
    // Tuned to stay readable on both light and dark backgrounds.
    private static readonly IBrush Good = new SolidColorBrush(Color.Parse("#2FA84F"));
    private static readonly IBrush Fair = new SolidColorBrush(Color.Parse("#E68A00"));
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#E5484D"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var delay = value.ToString().ToInt();

        return delay switch
        {
            <= 0 => Bad,
            <= 300 => Good,
            <= 800 => Fair,
            _ => Bad
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
