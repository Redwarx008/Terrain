#nullable enable

using Stride.Core.Mathematics;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Terrain.Editor.Platform;

internal static class WindowInterop
{
    private const int GwlWndProc = -4;
    private const int SmCxSizeFrame = 32;
    private const int SmCxPaddedBorder = 92;
    private const int SwRestore = 9;
    private const int SwMaximize = 3;
    private const int SwMinimize = 6;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmSysCommand = 0x0112;
    private const uint WmNclButtonDown = 0x00A1;
    private const int ScMove = 0xF010;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private static readonly Dictionary<nint, nint> OriginalWndProcs = new();
    private static readonly Dictionary<nint, ChromeMetrics> ChromeMetricMap = new();
    private static readonly WndProcDelegate CustomWndProc = WindowProc;

    public static void Minimize(nint hwnd)
    {
        if (hwnd != nint.Zero)
        {
            ShowWindow(hwnd, SwMinimize);
        }
    }

    public static void ToggleMaximize(nint hwnd)
    {
        if (hwnd == nint.Zero)
            return;

        ShowWindow(hwnd, IsZoomed(hwnd) ? SwRestore : SwMaximize);
    }

    public static bool IsMaximized(nint hwnd)
    {
        return hwnd != nint.Zero && IsZoomed(hwnd);
    }

    public static bool IsMinimized(nint hwnd)
    {
        return hwnd != nint.Zero && IsIconic(hwnd);
    }

    public static bool TryGetClientSize(nint hwnd, out Int2 size)
    {
        size = default;
        if (hwnd == nint.Zero || !GetClientRect(hwnd, out RECT rect))
            return false;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return false;

        size = new Int2(width, height);
        return true;
    }

    public static bool TryGetCursorScreenPosition(out Int2 position)
    {
        position = default;
        if (!GetCursorPos(out POINT point))
            return false;

        position = new Int2(point.X, point.Y);
        return true;
    }

    public static void EnableCustomChrome(nint hwnd, int titleBarHeight, int buttonAreaWidth, int resizeBorderThickness)
    {
        if (hwnd == nint.Zero)
            return;

        ChromeMetricMap[hwnd] = new ChromeMetrics(titleBarHeight, buttonAreaWidth, resizeBorderThickness);
        if (OriginalWndProcs.ContainsKey(hwnd))
            return;

        nint newWndProc = Marshal.GetFunctionPointerForDelegate(CustomWndProc);
        nint previousWndProc = SetWindowLongPtr(hwnd, GwlWndProc, newWndProc);
        if (previousWndProc != nint.Zero)
        {
            OriginalWndProcs[hwnd] = previousWndProc;
        }
    }

    public static float GetBorderlessMaximizedInset(nint hwnd, float fallback)
    {
        if (!IsMaximized(hwnd))
            return 0.0f;

        if (hwnd == nint.Zero)
            return fallback;

        try
        {
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi == 0)
                return fallback;

            int frame = GetSystemMetricsForDpi(SmCxSizeFrame, dpi);
            int paddedBorder = GetSystemMetricsForDpi(SmCxPaddedBorder, dpi);
            int inset = frame + paddedBorder;

            return inset > 0 ? inset : fallback;
        }
        catch (EntryPointNotFoundException)
        {
            return fallback;
        }
    }

    public static float GetWindowScaleFactor(nint hwnd, float fallback = 1.0f)
    {
        if (hwnd == nint.Zero)
            return fallback;

        try
        {
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi == 0)
                return fallback;

            float scale = dpi / 96.0f;
            return Math.Clamp(scale, 1.0f, 2.0f);
        }
        catch (EntryPointNotFoundException)
        {
            return fallback;
        }
    }

    public static void BeginWindowMove(nint hwnd)
    {
        if (hwnd == nint.Zero)
            return;

        // 标题栏拖拽改成异步系统命令，避免同步 SendMessage 把当前 UI 帧卡在系统移动循环里。
        ReleaseCapture();
        PostMessage(hwnd, WmSysCommand, (nint)(ScMove + HtCaption), nint.Zero);
    }

    public static void BeginWindowResize(nint hwnd, int edge)
    {
        if (hwnd == nint.Zero)
            return;

        int hitTest = edge switch
        {
            1 => HtLeft,
            2 => HtRight,
            4 => HtTop,
            5 => HtTopLeft,
            6 => HtTopRight,
            8 => HtBottom,
            9 => HtBottomLeft,
            10 => HtBottomRight,
            _ => 0
        };

        if (hitTest == 0)
            return;

        ReleaseCapture();
        SendMessage(hwnd, WmNclButtonDown, (nint)hitTest, nint.Zero);
    }

    private static nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmNcHitTest && ChromeMetricMap.TryGetValue(hwnd, out ChromeMetrics metrics))
        {
            nint hitTest = HitTestCustomChrome(hwnd, lParam, metrics);
            if (hitTest != nint.Zero)
                return hitTest;
        }

        if (OriginalWndProcs.TryGetValue(hwnd, out nint originalWndProc))
            return CallWindowProc(originalWndProc, hwnd, msg, wParam, lParam);

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static nint HitTestCustomChrome(nint hwnd, nint lParam, ChromeMetrics metrics)
    {
        if (!GetWindowRect(hwnd, out RECT rect))
            return nint.Zero;

        int x = GetXLParam(lParam) - rect.Left;
        int y = GetYLParam(lParam) - rect.Top;
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (x < 0 || y < 0 || x >= width || y >= height)
            return nint.Zero;

        bool isMaximized = IsMaximized(hwnd);
        if (!isMaximized)
        {
            bool onLeft = x < metrics.ResizeBorderThickness;
            bool onRight = x >= width - metrics.ResizeBorderThickness;
            bool onTop = y < metrics.ResizeBorderThickness;
            bool onBottom = y >= height - metrics.ResizeBorderThickness;

            if (onTop && onLeft) return (nint)HtTopLeft;
            if (onTop && onRight) return (nint)HtTopRight;
            if (onBottom && onLeft) return (nint)HtBottomLeft;
            if (onBottom && onRight) return (nint)HtBottomRight;
            if (onLeft) return (nint)HtLeft;
            if (onRight) return (nint)HtRight;
            if (onTop) return (nint)HtTop;
            if (onBottom) return (nint)HtBottom;
        }

        if (y < metrics.TitleBarHeight && x < width - metrics.ButtonAreaWidth)
            return (nint)HtCaption;

        return (nint)HtClient;
    }

    private static int GetXLParam(nint lParam)
    {
        return unchecked((short)(long)lParam);
    }

    private static int GetYLParam(nint lParam)
    {
        return unchecked((short)(((long)lParam >> 16) & 0xFFFF));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private readonly record struct ChromeMetrics(int TitleBarHeight, int ButtonAreaWidth, int ResizeBorderThickness);

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);

        return (nint)SetWindowLong32(hWnd, nIndex, unchecked((int)dwNewLong));
    }
}
