using System.Windows;

namespace SubGuard.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ApplyHighContrast();
        base.OnStartup(e);
        SystemParameters.StaticPropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SystemParameters.HighContrast))
            {
                ApplyHighContrast();
            }
        };
    }

    // In High Contrast the design tokens give way to the user's system colours.
    private void ApplyHighContrast()
    {
        if (!SystemParameters.HighContrast)
        {
            return;
        }

        var map = new (string Key, SystemColor Color)[]
        {
            ("CanvasBrush", SystemColor.Window), ("SurfaceBrush", SystemColor.Window),
            ("InkBrush", SystemColor.WindowText), ("MutedBrush", SystemColor.WindowText),
            ("AccentBrush", SystemColor.Highlight), ("OnAccentBrush", SystemColor.HighlightText),
            ("FailBrush", SystemColor.WindowText), ("PassBrush", SystemColor.WindowText), ("WarnBrush", SystemColor.WindowText),
            ("DividerBrush", SystemColor.WindowText), ("ControlBorderBrush", SystemColor.WindowText),
            ("HoverBrush", SystemColor.Window), ("SelectedBrush", SystemColor.Highlight),
            ("HeaderBrush", SystemColor.Window), ("DisabledForegroundBrush", SystemColor.GrayText),
            ("PassBgBrush", SystemColor.Window), ("FailBgBrush", SystemColor.Window),
            ("WarnBgBrush", SystemColor.Window), ("NeutralBgBrush", SystemColor.Window)
        };
        foreach (var (key, color) in map)
        {
            Resources[key] = color switch
            {
                SystemColor.Window => SystemColors.WindowBrush,
                SystemColor.WindowText => SystemColors.WindowTextBrush,
                SystemColor.Highlight => SystemColors.HighlightBrush,
                SystemColor.HighlightText => SystemColors.HighlightTextBrush,
                _ => SystemColors.GrayTextBrush
            };
        }
    }

    private enum SystemColor
    {
        Window,
        WindowText,
        Highlight,
        HighlightText,
        GrayText
    }
}
