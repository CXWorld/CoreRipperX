using System.Globalization;
using System.Runtime.Intrinsics.X86;
using System.Windows;
using System.Windows.Data;

namespace CoreRipperX.UI.Helpers;

public sealed class LoadGroupHeaderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int order)
        {
            return order switch
            {
                1 => "Light Load",
                2 => "Medium Load",
                3 => "Heavy Load",
                _ => "Load"
            };
        }

        return "Load";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class AvxGroupHeaderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int order)
        {
            if (order == 2)
                return Avx512F.IsSupported ? "AVX512" : "AVX512 (N/A)";
            return "AVX2";
        }

        return "AVX";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
