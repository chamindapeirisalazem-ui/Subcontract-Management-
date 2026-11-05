using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SubGuard.Desktop.Converters;

public sealed class ChipToneConverter : IValueConverter
{
    private static readonly string[] Pass =
        ["active", "approved", "paid", "current", "valid", "pass", "passed", "signed", "received", "reconciled", "compliant"];
    private static readonly string[] Fail =
        ["expired", "fail", "failed", "blocked", "rejected", "missing", "expiry missing", "mismatch", "non-compliant", "no records"];
    private static readonly string[] Warn =
        ["expires today", "expiring", "pending", "pendingcompliance", "pendingwaiver", "pendingapproval", "review", "warning", "attention"];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;
        if (Array.IndexOf(Pass, text) >= 0)
        {
            return "Pass";
        }

        if (Array.IndexOf(Fail, text) >= 0)
        {
            return "Fail";
        }

        return Array.IndexOf(Warn, text) >= 0 ? "Warn" : "Neutral";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
