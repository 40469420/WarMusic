using System.Windows;

namespace WarMusic.Services;

internal static class OverlayPlacement
{
    internal const double DefaultLeft = 80;
    internal const double DefaultTop = 80;
    internal const double DefaultWidth = 312;
    internal const double MinWidth = 268;
    internal const double MaxWidth = 460;
    internal const double MinCompactHeight = 118;
    internal const double MaxCompactHeight = 200;
    internal const double DefaultCompactHeight = 138;
    internal const double MinExpandedHeight = 280;
    internal const double MaxHeight = 520;
    internal const double DefaultExpandedHeight = 360;
    internal const double DefaultOpacity = .78;
    internal const double MinOpacity = .38;
    internal const double MaxOpacity = .94;
    internal const string DefaultToggleKey = "Control+Shift+O";
    internal const string DefaultInteractKey = "Control+Shift+I";

    internal static Rect CurrentVirtualScreen() => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    internal static double ClampWidth(double width) =>
        double.IsFinite(width) ? Math.Clamp(width, MinWidth, MaxWidth) : DefaultWidth;

    internal static double ClampHeight(double height) =>
        double.IsFinite(height) ? Math.Clamp(height, MinCompactHeight, MaxHeight) : DefaultExpandedHeight;

    internal static double ClampOpacity(double opacity) =>
        double.IsFinite(opacity) ? Math.Clamp(opacity, MinOpacity, MaxOpacity) : DefaultOpacity;

    internal static string NormalizeHotkey(string? gesture, string fallback)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return fallback;
        }

        try
        {
            _ = Hotkeys.Parse(gesture.Trim());
            return gesture.Trim();
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
        {
            return fallback;
        }
    }

    internal static Rect Fit(Rect window, Rect screen)
    {
        if (screen.Width < 80 || screen.Height < 80)
        {
            screen = new Rect(0, 0, 1920, 1080);
        }

        var width = ClampWidth(window.Width);
        var height = ClampHeight(window.Height);
        var left = double.IsFinite(window.X) ? window.X : DefaultLeft;
        var top = double.IsFinite(window.Y) ? window.Y : DefaultTop;
        var candidate = new Rect(left, top, width, height);
        var visible = Rect.Intersect(candidate, screen);
        if (visible.Width < Math.Min(80, width) || visible.Height < Math.Min(40, height))
        {
            left = screen.Left + 24;
            top = screen.Top + 24;
        }

        var maxLeft = screen.Left + Math.Max(0, screen.Width - width);
        var maxTop = screen.Top + Math.Max(0, screen.Height - height);
        left = Math.Clamp(left, screen.Left, Math.Max(screen.Left, maxLeft));
        top = Math.Clamp(top, screen.Top, Math.Max(screen.Top, maxTop));
        return new Rect(left, top, width, height);
    }
}
