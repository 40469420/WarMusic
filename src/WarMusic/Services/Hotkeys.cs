using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
namespace WarMusic.Services;

public sealed class Hotkeys : IDisposable
{
    readonly HwndSource source; readonly Dictionary<int, Action> actions = []; int next = 100;
    public Hotkeys(IntPtr window) { source = HwndSource.FromHwnd(window); source.AddHook(Hook); }
    public void Clear() { foreach (int id in actions.Keys) UnregisterHotKey(source.Handle, id); actions.Clear(); }
    public void Register(string gesture, Action action)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return;
        var parsed = Parse(gesture); int id = next++;
        if (!RegisterHotKey(source.Handle, id, (uint)parsed.Modifiers | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(parsed.Key))) throw new InvalidOperationException($"Hotkey {gesture} is unavailable or already used. Choose another combination."); actions.Add(id, action);
    }
    public static KeyGesture Parse(string gesture) => (KeyGesture)(new KeyGestureConverter().ConvertFromInvariantString(gesture) ?? throw new FormatException("Invalid hotkey."));
    public static bool IsDown(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        try { var value = (Key)(new KeyConverter().ConvertFromInvariantString(key) ?? Key.None); return value != Key.None && (GetAsyncKeyState(KeyInterop.VirtualKeyFromKey(value)) & 0x8000) != 0; } catch { return false; }
    }
    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) { if (msg == 0x312 && actions.TryGetValue(wp.ToInt32(), out var action)) { action(); handled = true; } return IntPtr.Zero; }
    public void Dispose() { Clear(); source.RemoveHook(Hook); }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
}
