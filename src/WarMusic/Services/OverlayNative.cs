using System.Runtime.InteropServices;

namespace WarMusic.Services;

internal static class OverlayNative
{
    const int GwlExStyle = -20;
    const nint WsExTransparent = 0x20;
    const nint WsExLayered = 0x80000;
    const nint WsExToolWindow = 0x80;
    const nint WsExNoActivate = 0x08000000;
    static readonly IntPtr HwndTopmost = new(-1);
    const uint SwpNoSize = 0x0001;
    const uint SwpNoMove = 0x0002;
    const uint SwpNoActivate = 0x0010;
    const uint SwpFrameChanged = 0x0020;

    internal static void ScreenPoint(IntPtr lParam, out int x, out int y)
    {
        int packed = unchecked((int)lParam.ToInt64());
        x = unchecked((short)packed);
        y = unchecked((short)(packed >> 16));
    }

    internal static bool Contains(double localX, double localY, double width, double height) =>
        localX >= 0 && localY >= 0 && localX <= width && localY <= height;

    internal static void Apply(IntPtr window, bool clickThrough)
    {
        if (window == IntPtr.Zero)
        {
            return;
        }

        nint style = GetWindowLongPtr(window, GwlExStyle);
        style |= WsExLayered | WsExToolWindow;
        style &= ~WsExTransparent;
        if (clickThrough)
        {
            style |= WsExNoActivate;
        }
        else
        {
            style &= ~WsExNoActivate;
        }

        SetWindowLongPtr(window, GwlExStyle, style);
        SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern nint GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern nint SetWindowLongPtr(IntPtr window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
