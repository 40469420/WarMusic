using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WarMusic.Services;

namespace WarMusic;

public sealed partial class OverlayWindow : Window
{
    readonly MainViewModel model;
    HwndSource? hwndSource;
    Point resizeOrigin;
    Size resizeStart;
    bool resizing;
    internal bool ClickThroughEnabled { get; private set; } = true;

    public OverlayWindow(MainViewModel model)
    {
        this.model = model;
        DataContext = model;
        InitializeComponent();
        ApplyPlacement();
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => { ApplyLayout(); ApplyClickThrough(); };
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
        model.OverlayInteractionChanged += ApplyClickThrough;
        model.OverlayLayoutChanged += ApplyLayout;
        model.OverlayPlacementReset += ResetPlacement;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettings;
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        hwndSource?.AddHook(HitTestHook);
        ApplyClickThrough();
    }

    internal void ApplyClickThrough()
    {
        ClickThroughEnabled = !model.OverlayInteractive;
        var handle = new WindowInteropHelper(this).Handle;
        OverlayNative.Apply(handle, ClickThroughEnabled);
        Focusable = model.OverlayInteractive;
    }

    IntPtr HitTestHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0x0084 || model.OverlayInteractive)
        {
            return IntPtr.Zero;
        }

        OverlayNative.ScreenPoint(lParam, out int x, out int y);
        if (HitsLockChip(new Point(x, y)))
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(-1);
    }

    bool HitsLockChip(Point screen)
    {
        try
        {
            var local = LockButton.PointFromScreen(screen);
            return OverlayNative.Contains(local.X, local.Y, LockButton.ActualWidth, LockButton.ActualHeight);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal void ApplyLayout()
    {
        Width = OverlayPlacement.ClampWidth(model.OverlayWidth);
        SizeToContent = SizeToContent.Height;
        if (model.OverlayExpanded)
        {
            MinHeight = OverlayPlacement.MinExpandedHeight;
            MaxHeight = OverlayPlacement.MaxHeight;
        }
        else
        {
            MinHeight = OverlayPlacement.MinCompactHeight;
            MaxHeight = OverlayPlacement.MaxCompactHeight;
        }

        RecoverIfNeeded();
    }

    internal void ResetPlacement()
    {
        Width = OverlayPlacement.DefaultWidth;
        model.OverlayWidth = Width;
        model.OverlayHeight = OverlayPlacement.DefaultExpandedHeight;
        var fitted = OverlayPlacement.Fit(
            new Rect(OverlayPlacement.DefaultLeft, OverlayPlacement.DefaultTop, Width, OverlayPlacement.DefaultExpandedHeight),
            OverlayPlacement.CurrentVirtualScreen());
        Left = fitted.X;
        Top = fitted.Y;
        ApplyLayout();
    }

    void ApplyPlacement()
    {
        Width = OverlayPlacement.ClampWidth(model.OverlayWidth);
        var height = model.OverlayExpanded ? model.OverlayHeight : OverlayPlacement.DefaultCompactHeight;
        var fitted = OverlayPlacement.Fit(new Rect(model.OverlayLeft, model.OverlayTop, Width, height), OverlayPlacement.CurrentVirtualScreen());
        Left = fitted.X;
        Top = fitted.Y;
        Width = fitted.Width;
    }

    void RecoverIfNeeded()
    {
        var fitted = OverlayPlacement.Fit(new Rect(Left, Top, Width, ActualHeight > 0 ? ActualHeight : Height), OverlayPlacement.CurrentVirtualScreen());
        Left = fitted.X;
        Top = fitted.Y;
    }

    void SavePlacement()
    {
        model.OverlayLeft = Left;
        model.OverlayTop = Top;
        model.OverlayWidth = Width;
        if (model.OverlayExpanded && ActualHeight > 0)
        {
            model.OverlayHeight = ActualHeight;
        }

        model.Save();
    }

    void OnClosed(object? sender, EventArgs e)
    {
        model.OverlayInteractionChanged -= ApplyClickThrough;
        model.OverlayLayoutChanged -= ApplyLayout;
        model.OverlayPlacementReset -= ResetPlacement;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettings;
        hwndSource?.RemoveHook(HitTestHook);
        hwndSource = null;
        SavePlacement();
        model.LockOverlay();
    }

    void OnDisplaySettings(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RecoverIfNeeded);

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !model.OverlayInteractive)
        {
            return;
        }

        model.LockOverlay();
        e.Handled = true;
    }

    void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (!model.OverlayInteractive || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        DragMove();
        RecoverIfNeeded();
    }

    void ResizePressed(object sender, MouseButtonEventArgs e)
    {
        if (!model.OverlayInteractive || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        resizing = true;
        resizeOrigin = PointToScreen(e.GetPosition(this));
        resizeStart = new Size(Width, Height);
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    void ResizeMoved(object sender, MouseEventArgs e)
    {
        if (!resizing || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var now = PointToScreen(e.GetPosition(this));
        Width = OverlayPlacement.ClampWidth(resizeStart.Width + now.X - resizeOrigin.X);
        if (model.OverlayExpanded)
        {
            SizeToContent = SizeToContent.Manual;
            Height = OverlayPlacement.ClampHeight(resizeStart.Height + now.Y - resizeOrigin.Y);
        }
    }

    void ResizeReleased(object sender, MouseButtonEventArgs e)
    {
        if (!resizing)
        {
            return;
        }

        resizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
        model.OverlayWidth = Width;
        model.OverlayHeight = Height;
    }
}
