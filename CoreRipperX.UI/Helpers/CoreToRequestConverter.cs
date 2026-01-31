using System.Globalization;
using System.Windows.Data;
using CoreRipperX.Core.Models;

namespace CoreRipperX.UI.Helpers;

public class CoreToRequestConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CoreData core && parameter is string algorithm)
        {
            return new StressTestCoreRequest(core, algorithm);
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
