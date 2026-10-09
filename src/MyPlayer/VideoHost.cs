using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MyPlayer;

/// <summary>
/// Native child window that hosts mpv's video output (passed to mpv as "wid").
/// WPF resizes this window automatically with layout; mpv renders inside it.
/// </summary>
public sealed class VideoHost : HwndHost
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var hwnd = CreateWindowEx(
            0,
            "static",
            string.Empty,
            WS_CHILD | WS_VISIBLE,
            0, 0, 0, 0,
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        return new HandleRef(this, hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpszClassName, string lpszWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hwndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
