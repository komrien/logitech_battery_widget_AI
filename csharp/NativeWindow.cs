using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LogitechBatteryWidget;

/// <summary>WPF만으로 안 되는 창 설정(Win32).</summary>
static class NativeWindow
{
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x00000080;
    const long WS_EX_APPWINDOW = 0x00040000;
    const uint MONITOR_DEFAULTTONULL = 0;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    /// <summary>도구 창으로 만들어 Alt+Tab·작업 표시줄에 나타나지 않게 한다.</summary>
    public static void HideFromAltTab(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style = (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    /// <summary>창이 연결된 모니터 중 하나와 겹치는지.</summary>
    public static bool IsOnAnyMonitor(Window w) =>
        MonitorFromWindow(new WindowInteropHelper(w).Handle, MONITOR_DEFAULTTONULL) != IntPtr.Zero;
}
