using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Siua.Common;

public class LogLevelToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Info => new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                LogLevel.Error => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                LogLevel.Browser => new SolidColorBrush(Color.FromRgb(156, 39, 176)),
                _ => new SolidColorBrush(Color.FromRgb(33, 150, 243))
            };
        }
        return new SolidColorBrush(Color.FromRgb(33, 150, 243));
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
