using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using HardwareInspector.Core.Models;

namespace HardwareInspector.App.ViewModels;

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value as string;
        if (string.IsNullOrWhiteSpace(hex)) return Brushes.Gray;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.Gray; }
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Severity s
            ? new SolidColorBrush(s switch
            {
                Severity.Critical => Color.FromRgb(0xE5, 0x54, 0x4B),
                Severity.Warning => Color.FromRgb(0xF4, 0xA2, 0x59),
                Severity.Notice => Color.FromRgb(0x8A, 0xC9, 0x26),
                _ => Color.FromRgb(0x6C, 0x72, 0x7D)
            })
            : Brushes.Gray;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (parameter as string == "invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

public sealed class ComponentKindToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ComponentKind k ? Core.Utils.RatingScale.ToText(k) : "—";

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

public sealed class RatingToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is HealthRating r ? Core.Utils.RatingScale.ToText(r) : "—";

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

public sealed class RatingToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HealthRating r) return Brushes.Gray;
        var hex = Core.Utils.RatingScale.ToHexColor(r);
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>
/// Tra hình cờ theo khoá tài nguyên. Cần một converter riêng vì LanguageOption nằm ở
/// tầng Core, không được phép biết gì về tài nguyên giao diện — nó chỉ mang khoá dạng chuỗi.
/// </summary>
public sealed class FlagKeyToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Application.Current?.TryFindResource(key) : null;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}
