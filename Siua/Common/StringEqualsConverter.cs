using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Siua.Common;

/// <summary>将字符串选项映射到单选状态；取消选择时不覆盖当前选项。</summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string selected && parameter is string current && selected == current;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string current ? current : BindingOperations.DoNothing;
}
