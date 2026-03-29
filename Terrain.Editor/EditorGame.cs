#nullable enable

using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Colors;
using Stride.Rendering.Lights;
using System;
using System.Linq;
using System.Threading.Tasks;
using Terrain.Editor.Input;
using Terrain.Editor.Platform;
using Terrain.Editor.Services;
using Terrain.Editor.UI;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor;

/// <summary>
/// 地形编辑器主游戏类。
/// </summary>
public class EditorGame : Game
{
    private EditorUIRenderer? uiRenderer;
    private MainWindow? mainWindow;
    private GraphicsDeviceManager? editorGraphicsDeviceManager;
    private Scene? editorScene;
    private Int2 lastValidClientSize = new(1920, 1080);
    private Int2 pendingClientSize = new(1920, 1080);
    private bool wasMinimized;
    private int restoreCooldownFrames;
    private bool isSyncingBackBuffer;
    private bool hasPendingBackBufferSync;

    protected override void BeginRun()
    {
        base.BeginRun();

        // 恢复自绘无边框窗口，标题栏按钮和拖拽由编辑器自己接管。
        Window.IsBorderLess = true;
        Window.AllowUserResizing = true;
        Window.Title = "Terrain Editor";
        // 最小化时直接停掉 Draw，避免 Stride 在系统回退出来的 1x1 尺寸上继续跑渲染链。
        DrawWhileMinimized = false;

        editorGraphicsDeviceManager = Services.GetService<IGraphicsDeviceManager>() as GraphicsDeviceManager;
        if (editorGraphicsDeviceManager != null)
        {
            editorGraphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
            editorGraphicsDeviceManager.PreferredBackBufferWidth = 1920;
            editorGraphicsDeviceManager.PreferredBackBufferHeight = 1080;
            editorGraphicsDeviceManager.IsFullScreen = false;
            editorGraphicsDeviceManager.ApplyChanges();
        }

        Window.ClientSizeChanged += OnWindowClientSizeChanged;
        Window.Activated += OnWindowActivated;

        InitializeScene();
    }

    protected override async Task LoadContent()
    {
        await base.LoadContent();

        uiRenderer = new EditorUIRenderer(this);

        mainWindow = new MainWindow();
        mainWindow.Initialize(GraphicsDevice, Window, Services);

        // Initialize terrain support in viewport after scene is created
        if (editorScene != null)
        {
            mainWindow.Viewport.InitializeTerrainSupport(GraphicsDevice, editorScene, Input);
            mainWindow.Viewport.Camera = SceneSystem.SceneInstance.RootScene.Entities.FirstOrDefault(e => e.Name == "MainCamera")?.Get<CameraComponent>();
        }

        uiRenderer.OnRender = () =>
        {
            mainWindow.Render();
        };

        EditorStyle.Apply();
    }

    protected override void Update(GameTime gameTime)
    {
        nint hwnd = GetNativeWindowHandle();
        bool systemMinimized = Window.IsMinimized || WindowInterop.IsMinimized(hwnd);

        if (systemMinimized)
        {
            wasMinimized = true;
            restoreCooldownFrames = 0;
            uiRenderer?.SuspendFrame();
            return;
        }

        CapturePendingClientSize();

        if (wasMinimized)
        {
            wasMinimized = false;
            // 恢复后的前几帧交给系统把交换链和 client rect 稳定下来，再继续正常渲染。
            restoreCooldownFrames = 3;
            hasPendingBackBufferSync = true;
        }

        if (hasPendingBackBufferSync)
        {
            hasPendingBackBufferSync = !EnsureValidBackBuffer(pendingClientSize);
        }

        if (restoreCooldownFrames > 0)
        {
            restoreCooldownFrames--;
            uiRenderer?.SuspendFrame();
            return;
        }

        base.Update(gameTime);

        // Update camera controller
        mainWindow?.Viewport.UpdateCamera((float)gameTime.TimePerFrame.TotalSeconds, Input);

        mainWindow?.Update((float)gameTime.TimePerFrame.TotalSeconds);
    }

    protected override void Draw(GameTime gameTime)
    {
        nint hwnd = GetNativeWindowHandle();
        if (Window.IsMinimized || WindowInterop.IsMinimized(hwnd))
        {
            uiRenderer?.SuspendFrame();
            return;
        }

        if (restoreCooldownFrames > 0 || hasPendingBackBufferSync)
        {
            if (restoreCooldownFrames > 0)
            {
                uiRenderer?.SuspendFrame();
                return;
            }

            // 普通窗口缩放时允许同帧继续尝试同步 backbuffer，而不是整帧跳过导致界面看起来卡在旧尺寸。
            hasPendingBackBufferSync = !EnsureValidBackBuffer(pendingClientSize);
        }

        var clientBounds = Window.ClientBounds;
        if (clientBounds.Width <= 1 || clientBounds.Height <= 1)
        {
            uiRenderer?.SuspendFrame();
            return;
        }

        // Update viewport render target size
        if (mainWindow != null)
        {
            var viewportSize = mainWindow.Viewport.ContentRect.Size;
            if (viewportSize.X > 0 && viewportSize.Y > 0)
            {
                mainWindow.Viewport.UpdateRenderTarget(GraphicsDevice, new Vector2(viewportSize.X, viewportSize.Y));
            }
        }

        GraphicsContext.CommandList.Clear(GraphicsDevice.Presenter.BackBuffer, Color4.Black);
        GraphicsContext.CommandList.Clear(GraphicsDevice.Presenter.DepthStencilBuffer, DepthStencilClearOptions.DepthBuffer);

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        Window.ClientSizeChanged -= OnWindowClientSizeChanged;
        Window.Activated -= OnWindowActivated;
        uiRenderer?.Dispose();
        base.UnloadContent();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (Window.IsMinimized)
            return;

        // 从任务栏恢复后，激活事件通常早于第一帧正常渲染，先安排几帧缓冲期。
        // 任务栏恢复通常会先收到激活事件，再过几帧窗口尺寸才真正稳定。
        restoreCooldownFrames = Math.Max(restoreCooldownFrames, 3);
        hasPendingBackBufferSync = true;
    }

    private void OnWindowClientSizeChanged(object? sender, EventArgs e)
    {
        var clientBounds = Window.ClientBounds;
        if (clientBounds.Width <= 1 || clientBounds.Height <= 1)
        {
            wasMinimized = true;
            return;
        }

        lastValidClientSize = new Int2(clientBounds.Width, clientBounds.Height);
        pendingClientSize = lastValidClientSize;
        hasPendingBackBufferSync = true;
    }

    private void CapturePendingClientSize()
    {
        var clientBounds = Window.ClientBounds;
        if (clientBounds.Width <= 1 || clientBounds.Height <= 1)
            return;

        lastValidClientSize = new Int2(clientBounds.Width, clientBounds.Height);
        pendingClientSize = lastValidClientSize;
    }

    private bool EnsureValidBackBuffer(Int2 targetSize)
    {
        if (editorGraphicsDeviceManager == null || isSyncingBackBuffer)
            return false;

        int targetWidth = Math.Max(1, targetSize.X);
        int targetHeight = Math.Max(1, targetSize.Y);

        bool preferredMatches =
            editorGraphicsDeviceManager.PreferredBackBufferWidth == targetWidth &&
            editorGraphicsDeviceManager.PreferredBackBufferHeight == targetHeight;
        bool actualMatches =
            GraphicsDevice.Presenter.BackBuffer.Width == targetWidth &&
            GraphicsDevice.Presenter.BackBuffer.Height == targetHeight;

        if (preferredMatches && actualMatches)
            return true;

        // 用系统窗口当前的有效 client 尺寸回填 backbuffer，避免恢复后继续沿用 1x1 目标。
        isSyncingBackBuffer = true;
        try
        {
            editorGraphicsDeviceManager.PreferredBackBufferWidth = targetWidth;
            editorGraphicsDeviceManager.PreferredBackBufferHeight = targetHeight;
            editorGraphicsDeviceManager.ApplyChanges();
        }
        finally
        {
            isSyncingBackBuffer = false;
        }

        return
            GraphicsDevice.Presenter.BackBuffer.Width == targetWidth &&
            GraphicsDevice.Presenter.BackBuffer.Height == targetHeight;
    }

    private nint GetNativeWindowHandle()
    {
        return Window.NativeWindow?.Handle ?? nint.Zero;
    }

    private void InitializeScene()
    {
        editorScene = new Scene();
        SceneSystem.SceneInstance = new SceneInstance(Services, editorScene);

        var cameraEntity = new Entity("MainCamera")
        {
            new CameraComponent
            {
                Slot = SceneSystem.GraphicsCompositor.Cameras[0].ToSlotId()
            }
        };
        cameraEntity.Transform.Position = new Vector3(0, 50, -100);
        cameraEntity.Transform.Rotation = Quaternion.RotationX((float)Math.PI / 6);
        editorScene.Entities.Add(cameraEntity);

        var lightEntity = new Entity("DirectionalLight")
        {
            new LightComponent
            {
                Type = new LightDirectional
                {
                    Color = new ColorRgbProvider(Color.White)
                },
                Intensity = 1.0f
            }
        };
        lightEntity.Transform.Rotation = Quaternion.RotationX((float)Math.PI / 3);
        editorScene.Entities.Add(lightEntity);
    }
}
