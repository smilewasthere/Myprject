using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace MyPlayer;

/// <summary>
/// Low-level mouse + keyboard hooks. mpv renders into a native child window,
/// so input landing on the video never reaches WPF — these hooks observe it.
/// Mouse is never swallowed (mpv keeps its own click/wheel input.conf
/// bindings). Keys are swallowed only when they match an app shortcut AND
/// keyboard focus is inside the mpv window (otherwise WPF's normal key
/// handling runs untouched).
/// </summary>
internal sealed class NativeHooks : IDisposable
{
    /// <summary>Right mouse button released over our window. Point is in screen pixels.</summary>
    public event Action<Point>? RightButtonUp;

    /// <summary>Left button double-clicked over our window. Point is in screen pixels.</summary>
    public event Action<Point>? LeftDoubleClick;

    /// <summary>
    /// Invoked for key presses while our window is foreground and mpv has
    /// keyboard focus. Return true to swallow the key.
    /// </summary>
    public Func<Key, bool, bool>? KeyPressed;

    /// <summary>True when keyboard focus is inside the mpv native window.</summary>
    public Func<bool>? IsMpvFocused;

    private IntPtr _mainHwnd;
    private IntPtr _mouseHook;
    private IntPtr _kbdHook;
    private HookProc? _mouseProc;
    private HookProc? _kbdProc;
    private bool _disposed;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_MOUSE_LL = 14;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_CONTROL = 0x11;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    public void Start(IntPtr mainHwnd)
    {
        _mainHwnd = mainHwnd;
        var module = GetModuleHandle(null);
        _mouseProc = MouseProc;
        _kbdProc = KbdProc;
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        _kbdHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbdProc, module, 0);
        if (_mouseHook == IntPtr.Zero || _kbdHook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install input hooks.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        if (_kbdHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_kbdHook);
            _kbdHook = IntPtr.Zero;
        }
        // Keep delegates reachable until unhooked.
        GC.KeepAlive(_mouseProc);
        GC.KeepAlive(_kbdProc);
    }

    private bool IsOverOurWindow(POINT pt)
    {
        if (GetForegroundWindow() != _mainHwnd)
            return false;
        if (!GetWindowRect(_mainHwnd, out var rect))
            return false;
        return pt.X >= rect.Left && pt.X < rect.Right && pt.Y >= rect.Top && pt.Y < rect.Bottom;
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_RBUTTONUP || wParam == (IntPtr)WM_LBUTTONDBLCLK))
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (IsOverOurWindow(info.Pt))
            {
                var point = new Point(info.Pt.X, info.Pt.Y);
                if (wParam == (IntPtr)WM_RBUTTONUP)
                    RightButtonUp?.Invoke(point);
                else
                    LeftDoubleClick?.Invoke(point);
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KbdProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            if (KeyPressed is not null && GetForegroundWindow() == _mainHwnd && IsMpvFocused?.Invoke() == true)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                var key = KeyInterop.KeyFromVirtualKey((int)info.VkCode);
                if (KeyPressed(key, ctrl))
                    return (IntPtr)1; // swallowed — we handled it
            }
        }
        return CallNextHookEx(_kbdHook, nCode, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

