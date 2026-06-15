#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Graphics.SDL;
using Terrain.Editor.Models;
using Terrain.Editor.Services;

namespace Terrain.Editor.Rendering.NativeViewport;

public sealed class NativeStrideViewportHost : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(16);

    private IntPtr _childHwnd;
    private EmbeddedStrideViewportGame? _game;
    private GameContextSDL? _context;
    private Window? _window;
    private DispatcherTimer? _tickTimer;
    private WindowProc? _shortcutWndProc;
    private IntPtr _originalShortcutWndProc;
    private string _status = "Viewport host not attached.";
    private SceneViewMode _sceneViewMode = SceneViewMode.Perspective;
    private int _width;
    private int _height;
    private bool _isDisposed;

    public event EventHandler? RuntimeStateChanged;

    public event EventHandler<ViewportShortcutRequestedEventArgs>? ShortcutRequested;

    public string Status => _status;

    public string Diagnostics => _game?.Diagnostics ?? "Diagnostics unavailable.";

    public bool IsAttached => _childHwnd != IntPtr.Zero;

    public bool HasSceneRuntime => _game?.Scene != null && _game?.TerrainManager != null;

    public Scene? Scene => _game?.Scene;

    public TerrainManager? TerrainManager => _game?.TerrainManager;

    public RiverRenderingService? RiverRenderingService => _game?.RiverRenderingService;

    public RiverMeshService? RiverMeshService => _game?.RiverMeshService;

    public SceneViewMode SceneViewMode => _sceneViewMode;


    public IntPtr ChildHwnd => _childHwnd;

    /// <summary>
    /// Temporarily removes WS_CHILD from the SDL window so that
    /// <see cref="Stride.Input.InputManager.LockMousePosition"/> and
    /// keyboard input work correctly during camera navigation.
    /// Call <see cref="RestoreChildStyle"/> when navigation ends.
    /// </summary>
    public void RemoveChildStyle()
    {
        if (_window == null) return;
        IntPtr style = GetWindowLongPtrW(_window.Handle, GwlStyle);
        nint newStyle = (nint)style.ToInt64() & ~WsChild;
        SetWindowLongPtrW(_window.Handle, GwlStyle, new IntPtr(newStyle));
        SetFocus(_window.Handle);
    }

    /// <summary>
    /// Restores WS_CHILD on the SDL window after camera navigation ends.
    /// </summary>
    public void RestoreChildStyle()
    {
        if (_window == null) return;
        IntPtr style = GetWindowLongPtrW(_window.Handle, GwlStyle);
        nint newStyle = (nint)style.ToInt64() | WsChild;
        SetWindowLongPtrW(_window.Handle, GwlStyle, new IntPtr(newStyle));
    }

    public void FocusRuntimeWindow()
    {
        if (_window == null)
        {
            return;
        }

        SetFocus(_window.Handle);
    }

    public bool TryGetRuntimeServices(out TerrainManager? terrainManager, out GraphicsDevice? graphicsDevice, out CommandList? commandList)
    {
        terrainManager = _game?.TerrainManager;
        graphicsDevice = _game?.GraphicsDevice;
        commandList = _game?.GraphicsContext?.CommandList;
        return terrainManager != null && graphicsDevice != null && commandList != null;
    }

    public void Attach(IntPtr childHwnd, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (childHwnd == IntPtr.Zero)
        {
            throw new ArgumentException("A valid child HWND is required.", nameof(childHwnd));
        }

        if (_childHwnd != IntPtr.Zero && _childHwnd != childHwnd)
        {
            StopRuntime();
        }

        _childHwnd = childHwnd;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);

        if (_game == null)
        {
            StartRuntime();
            return;
        }

        ApplySize();
        _game.SetViewportSize(_width, _height);
        UpdateStatus(BuildAttachedStatus());
        TraceDiagnostics("Attach");
    }

    public void Detach()
    {
        IntPtr previousHwnd = _childHwnd;
        _childHwnd = IntPtr.Zero;
        _width = 0;
        _height = 0;
        StopRuntime();
        _status = "Viewport host detached.";
        RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        _width = Math.Max(1, width);
        _height = Math.Max(1, height);

        if (_childHwnd != IntPtr.Zero)
        {
            ApplySize();
            _game?.SetViewportSize(_width, _height);
            UpdateStatus(BuildAttachedStatus());
            TraceDiagnostics("Resize");
        }
    }

    public void SetSceneViewMode(SceneViewMode sceneViewMode)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_sceneViewMode == sceneViewMode)
        {
            return;
        }

        _sceneViewMode = sceneViewMode;
        _game?.SetSceneViewMode(sceneViewMode);

        if (_childHwnd != IntPtr.Zero)
        {
            UpdateStatus(BuildAttachedStatus());
            TraceDiagnostics("ModeChanged");
            return;
        }

        RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetInputBlocked(bool blocked)
    {
        if (_game == null)
            return;

        _game.IsInputBlocked = blocked;
        if (blocked)
        {
            _game.FlushBlockedInputState();
        }
    }


    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        StopRuntime();
        _isDisposed = true;
        _childHwnd = IntPtr.Zero;
    }

    private void StartRuntime()
    {
        try
        {
            _window = new GameFormSDL("Terrain Editor Viewport")
            {
                Visible = false,
                FormBorderStyle = FormBorderStyle.None,
            };
            AttachSdlWindowToHost();
            _window.ClientSize = new Size2(_width, _height);

            _game = new EmbeddedStrideViewportGame();
            _game.FocusRuntimeWindow = FocusRuntimeWindow;
            _game.SetChildWindowStyle = isChild =>
            {
                if (isChild) RestoreChildStyle(); else RemoveChildStyle();
            };
            _game.RuntimeReady += OnGameRuntimeReady;
            _game.FirstFrameRendered += OnGameFirstFrameRendered;
            _game.SetSceneViewMode(_sceneViewMode);
            _game.SetViewportSize(_width, _height);

            _context = new GameContextSDL(_window, _width, _height, isUserManagingRun: true);
            _game.Run(_context);

            // Stride/SDL initialization can mutate Win32 styles, so restamp hosted-window
            // flags after the game starts to keep the embedded viewport borderless.
            AttachSdlWindowToHost();
            InstallShortcutWndProc();
            _window.Visible = true;

            _tickTimer = new DispatcherTimer(TickInterval, DispatcherPriority.Render, OnTick);
            _tickTimer.Start();

            UpdateStatus($"SDL viewport bootstrapped {_width}x{_height}; initializing.");
            TraceDiagnostics("StartRuntime");
        }
        catch (Exception exception)
        {
            StopRuntime();
            UpdateStatus($"SDL viewport startup failed: {exception.Message}");
        }
    }

    private void StopRuntime()
    {
        if (_tickTimer != null)
        {
            _tickTimer.Stop();
            _tickTimer.Tick -= OnTick;
            _tickTimer = null;
        }

        UninstallShortcutWndProc();

        if (_game != null)
        {
            _game.RuntimeReady -= OnGameRuntimeReady;
            _game.FirstFrameRendered -= OnGameFirstFrameRendered;
            _game.Dispose();
            _game = null;
        }

        if (_window is { IsDisposed: false })
        {
            _window.Dispose();
        }

        _window = null;
        _context = null;
    }

    private void ApplySize()
    {
        if (_window == null)
        {
            return;
        }

        _window.ClientSize = new Size2(_width, _height);
        ApplyHostedWindowStyles();
        SetWindowPos(_window.Handle, IntPtr.Zero, 0, 0, _width, _height, SwpNoActivate | SwpNoZOrder | SwpShowWindow | SwpFrameChanged);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            Stride.Graphics.SDL.Application.ProcessEvents();
            _context?.RunCallback?.Invoke();

            if (_game is { IsExiting: true })
            {
                _tickTimer?.Stop();
                UpdateStatus("SDL viewport exited.");
            }
        }
        catch (Exception exception)
        {
            _tickTimer?.Stop();
            UpdateStatus($"SDL viewport tick failed: {exception.Message}");
        }
    }

    private void OnGameRuntimeReady(object? sender, EventArgs e)
    {
        UpdateStatus($"SDL viewport runtime ready {_width}x{_height}; mode {_sceneViewMode}.");
        TraceDiagnostics("RuntimeReady");
    }

    private void OnGameFirstFrameRendered(object? sender, EventArgs e)
    {
        UpdateStatus($"SDL viewport ready {_width}x{_height}; mode {_sceneViewMode}.");
        TraceDiagnostics("FirstFrameRendered");
    }

    private void UpdateStatus(string status)
    {
        _status = status;
        RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private string BuildAttachedStatus()
    {
        return $"SDL viewport attached {_width}x{_height}; mode {_sceneViewMode}.";
    }

    private void TraceDiagnostics(string stage)
    {
        Debug.WriteLine($"NativeStrideViewportHost: {stage} ({Diagnostics})");
    }

    private void AttachSdlWindowToHost()
    {
        if (_window == null || _childHwnd == IntPtr.Zero)
        {
            return;
        }

        SetParent(_window.Handle, _childHwnd);
        ApplyHostedWindowStyles();
        SetWindowPos(_window.Handle, IntPtr.Zero, 0, 0, _width, _height, SwpNoActivate | SwpNoZOrder | SwpShowWindow | SwpFrameChanged);
    }

    private void InstallShortcutWndProc()
    {
        if (_window == null || _originalShortcutWndProc != IntPtr.Zero)
        {
            return;
        }

        _shortcutWndProc = ShortcutWndProc;
        _originalShortcutWndProc = SetWindowLongPtrW(
            _window.Handle,
            GwlWndProc,
            Marshal.GetFunctionPointerForDelegate(_shortcutWndProc));
    }

    private void UninstallShortcutWndProc()
    {
        if (_window == null || _originalShortcutWndProc == IntPtr.Zero)
        {
            return;
        }

        SetWindowLongPtrW(_window.Handle, GwlWndProc, _originalShortcutWndProc);
        _originalShortcutWndProc = IntPtr.Zero;
        _shortcutWndProc = null;
    }

    private IntPtr ShortcutWndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmKeyDown && TryGetViewportShortcut((int)wParam, out var shortcut))
        {
            Dispatcher.UIThread.Post(() => ShortcutRequested?.Invoke(this, new ViewportShortcutRequestedEventArgs(shortcut)));
            return IntPtr.Zero;
        }

        return CallWindowProcW(_originalShortcutWndProc, hwnd, message, wParam, lParam);
    }

    private static bool TryGetViewportShortcut(int virtualKey, out ViewportShortcut shortcut)
    {
        shortcut = default;

        if (!IsKeyDown(VkControl))
        {
            return false;
        }

        if (virtualKey == VkZ)
        {
            shortcut = IsKeyDown(VkShift) ? ViewportShortcut.Redo : ViewportShortcut.Undo;
            return true;
        }

        if (virtualKey == VkY)
        {
            shortcut = ViewportShortcut.Redo;
            return true;
        }

        return false;
    }

    private void ApplyHostedWindowStyles()
    {
        if (_window == null)
        {
            return;
        }

        IntPtr style = GetWindowLongPtrW(_window.Handle, GwlStyle);
        nint childStyle = (nint)style.ToInt64();
        childStyle &= ~(WsCaption | WsThickFrame | WsPopup | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
        childStyle |= WsChild | WsVisible | WsClipChildren | WsClipSiblings;
        SetWindowLongPtrW(_window.Handle, GwlStyle, new IntPtr(childStyle));

        IntPtr exStyle = GetWindowLongPtrW(_window.Handle, GwlExStyle);
        nint childExStyle = (nint)exStyle.ToInt64();
        childExStyle &= ~(WsExAppWindow | WsExWindowEdge | WsExClientEdge | WsExStaticEdge | WsExDlgModalFrame);
        SetWindowLongPtrW(_window.Handle, GwlExStyle, new IntPtr(childExStyle));
    }

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int GwlWndProc = -4;
    private const nint WsChild = 0x40000000;
    private const nint WsVisible = 0x10000000;
    private const nint WsPopup = unchecked((int)0x80000000);
    private const nint WsCaption = 0x00C00000;
    private const nint WsSysMenu = 0x00080000;
    private const nint WsThickFrame = 0x00040000;
    private const nint WsMinimizeBox = 0x00020000;
    private const nint WsMaximizeBox = 0x00010000;
    private const nint WsClipChildren = 0x02000000;
    private const nint WsClipSiblings = 0x04000000;
    private const nint WsExDlgModalFrame = 0x00000001;
    private const nint WsExClientEdge = 0x00000200;
    private const nint WsExStaticEdge = 0x00020000;
    private const nint WsExAppWindow = 0x00040000;
    private const nint WsExWindowEdge = 0x00000100;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpFrameChanged = 0x0020;
    private const uint WmKeyDown = 0x0100;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkY = 0x59;
    private const int VkZ = 0x5A;

    private delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr childHandle, IntPtr newParentHandle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr newLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProcW(IntPtr previousWndProc, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
    }
}

public enum ViewportShortcut
{
    Undo,
    Redo,
}

public sealed class ViewportShortcutRequestedEventArgs(ViewportShortcut shortcut) : EventArgs
{
    public ViewportShortcut Shortcut { get; } = shortcut;
}
