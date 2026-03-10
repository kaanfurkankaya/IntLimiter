using System.Globalization;
using System.Windows.Data;

namespace IntLimiter.Converters;

public class BytesToHumanReadableConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes) return "—";

        string suffix = parameter as string == "total" ? "" : "/s";

        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB{suffix}",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB{suffix}",
            >= 1024 => $"{bytes / 1024.0:F1} KB{suffix}",
            > 0 => $"{bytes} B{suffix}",
            _ => "—"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BitsToDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bits || bits == 0) return "∞";

        return bits switch
        {
            >= 1_000_000 => $"{bits / 1_000_000.0:F1} Mbit/s",
            >= 1_000 => $"{bits / 1_000.0:F0} Kbit/s",
            _ => $"{bits} bit/s"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return System.Windows.Visibility.Visible;
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
