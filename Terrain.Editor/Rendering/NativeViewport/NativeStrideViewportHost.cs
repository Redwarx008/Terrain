#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
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
    private string _status = "Viewport host not attached.";
    private SceneViewMode _sceneViewMode = SceneViewMode.Shaded;
    private int _width;
    private int _height;
    private bool _isDisposed;

    public event EventHandler? RuntimeStateChanged;

    public string Status => _status;

    public string Diagnostics => _game?.Diagnostics ?? "Diagnostics unavailable.";

    public bool IsAttached => _childHwnd != IntPtr.Zero;

    public bool HasSceneRuntime => _game?.Scene != null && _game?.TerrainManager != null;

    public Scene? Scene => _game?.Scene;

    public TerrainManager? TerrainManager => _game?.TerrainManager;

    public SceneViewMode SceneViewMode => _sceneViewMode;

    public IntPtr ChildHwnd => _childHwnd;

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
        StopRuntime();
        _childHwnd = IntPtr.Zero;
        _width = 0;
        _height = 0;
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
                Visible = true,
                FormBorderStyle = FormBorderStyle.None,
            };
            AttachSdlWindowToHost();
            _window.ClientSize = new Size2(_width, _height);

            _game = new EmbeddedStrideViewportGame();
            _game.RuntimeReady += OnGameRuntimeReady;
            _game.FirstFrameRendered += OnGameFirstFrameRendered;
            _game.SetSceneViewMode(_sceneViewMode);
            _game.SetViewportSize(_width, _height);

            _context = new GameContextSDL(_window, _width, _height, isUserManagingRun: true);
            _game.Run(_context);

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
        SetWindowPos(_window.Handle, IntPtr.Zero, 0, 0, _width, _height, SwpNoActivate | SwpNoZOrder | SwpShowWindow);
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

        IntPtr style = GetWindowLongPtrW(_window.Handle, GwlStyle);
        nint childStyle = (nint)style.ToInt64();
        childStyle &= ~(WsCaption | WsThickFrame | WsPopup);
        childStyle |= WsChild | WsVisible;
        SetWindowLongPtrW(_window.Handle, GwlStyle, new IntPtr(childStyle));

        SetParent(_window.Handle, _childHwnd);
        SetWindowPos(_window.Handle, IntPtr.Zero, 0, 0, _width, _height, SwpNoActivate | SwpNoZOrder | SwpShowWindow | SwpFrameChanged);
    }

    private const int GwlStyle = -16;
    private const nint WsChild = 0x40000000;
    private const nint WsVisible = 0x10000000;
    private const nint WsPopup = unchecked((int)0x80000000);
    private const nint WsCaption = 0x00C00000;
    private const nint WsThickFrame = 0x00040000;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpFrameChanged = 0x0020;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr childHandle, IntPtr newParentHandle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr newLong);

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
}
