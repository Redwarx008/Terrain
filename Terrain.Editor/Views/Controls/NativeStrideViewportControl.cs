#nullable enable

using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Terrain.Editor.Rendering.NativeViewport;

namespace Terrain.Editor.Views.Controls;

public sealed class NativeStrideViewportControl : NativeControlHost
{
    public static readonly StyledProperty<NativeStrideViewportHost?> ViewportHostProperty =
        AvaloniaProperty.Register<NativeStrideViewportControl, NativeStrideViewportHost?>(nameof(ViewportHost));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<NativeStrideViewportControl>();

    private NativeChildWindow? _childWindow;
    private bool _viewportSyncQueued;

    public NativeStrideViewportHost? ViewportHost
    {
        get => GetValue(ViewportHostProperty);
        set => SetValue(ViewportHostProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        ViewportHost?.FocusRuntimeWindow();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty && _childWindow != null)
        {
            ScheduleViewportSync();
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        NativeChildWindow childWindow = new(
            parent.Handle,
            1,
            1);

        _childWindow = childWindow;
        ViewportHost?.Attach(childWindow.Handle, 1, 1);
        ScheduleViewportSync();
        return new PlatformHandle(childWindow.Handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        ViewportHost?.Detach();

        _childWindow?.Dispose();
        _childWindow = null;

        base.DestroyNativeControlCore(control);
    }

    private void ScheduleViewportSync()
    {
        if (_viewportSyncQueued)
        {
            return;
        }

        _viewportSyncQueued = true;
        Dispatcher.UIThread.Post(SyncViewportToNativeHost, DispatcherPriority.Render);
    }

    private void SyncViewportToNativeHost()
    {
        _viewportSyncQueued = false;

        if (_childWindow == null)
        {
            return;
        }

        TryUpdateNativeControlPosition();
        PixelSize pixelSize = _childWindow.GetClientSize();
        ViewportHost?.Resize(pixelSize.Width, pixelSize.Height);
    }

    private sealed class NativeChildWindow : IDisposable
    {
        private static readonly WindowProc WndProcDelegate = WndProc;
        private static readonly ushort ClassAtom = RegisterWindowClass();
        private bool _disposed;

        public NativeChildWindow(IntPtr parentHandle, int width, int height)
        {
            int initialWidth = Math.Max(1, width);
            int initialHeight = Math.Max(1, height);

            Handle = CreateWindowExW(
                0,
                ClassAtom,
                null,
                WsChild | WsVisible | WsClipChildren | WsClipSiblings,
                0,
                0,
                initialWidth,
                initialHeight,
                parentHandle,
                IntPtr.Zero,
                GetModuleHandleW(null),
                IntPtr.Zero);

            if (Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to create native HWND viewport child window.");
            }
        }

        public IntPtr Handle { get; }

        public PixelSize GetClientSize()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!GetClientRect(Handle, out RECT rect))
            {
                return new PixelSize(1, 1);
            }

            int width = Math.Max(1, rect.Right - rect.Left);
            int height = Math.Max(1, rect.Bottom - rect.Top);
            return new PixelSize(width, height);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (Handle != IntPtr.Zero)
            {
                DestroyWindow(Handle);
            }
        }

        private static ushort RegisterWindowClass()
        {
            WNDCLASSEXW windowClass = new()
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcDelegate),
                hInstance = GetModuleHandleW(null),
                lpszClassName = "TerrainEditorNativeViewportHost"
            };

            ushort classAtom = RegisterClassExW(ref windowClass);
            int error = Marshal.GetLastWin32Error();
            if (classAtom == 0 && error != ErrorClassAlreadyExists)
            {
                throw new InvalidOperationException($"Unable to register native viewport window class. Win32 error: {error}.");
            }

            return classAtom;
        }

        private static IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            return DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    private const int ErrorClassAlreadyExists = 1410;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipChildren = 0x02000000;
    private const uint WsClipSiblings = 0x04000000;

    private delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle,
        ushort className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parentHandle,
        IntPtr menuHandle,
        IntPtr instanceHandle,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);
}
