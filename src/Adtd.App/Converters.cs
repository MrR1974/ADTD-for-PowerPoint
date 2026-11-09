using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Adtd.App.ViewModels;
using Adtd.Collect;
using Wpf.Ui.Controls;

namespace Adtd.App;

public sealed class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

public sealed class BoolToVisibilityInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible when the value is set (not null, not an empty string or list).</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => IsSet(value) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    internal static bool IsSet(object? value) => value switch
    {
        null => false,
        string s => s.Length > 0,
        int i => i != 0,
        System.Collections.ICollection c => c.Count > 0,
        _ => true,
    };
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => NotNullToVisibilityConverter.IsSet(value) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ReportLevelToSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ReportLevel.Success => InfoBarSeverity.Success,
        ReportLevel.Warning => InfoBarSeverity.Warning,
        ReportLevel.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CollectLevel.Warning => Application.Current.TryFindResource("SystemFillColorCautionBrush") as Brush ?? Brushes.DarkOrange,
        CollectLevel.Step => Application.Current.TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.Black,
        _ => Application.Current.TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray,
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>True when the value is set (see <see cref="NotNullToVisibilityConverter"/>).</summary>
public sealed class IsSetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => NotNullToVisibilityConverter.IsSet(value);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>An InfoBar severity from an "is it an error" flag.</summary>
public sealed class ErrorToSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
