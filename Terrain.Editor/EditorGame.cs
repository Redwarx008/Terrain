#nullable enable

using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Colors;
using Stride.Rendering.Lights;
using System;
using System.Linq;
using System.Threading.Tasks;
using Terrain.Editor.Input;
using Terrain.Editor.Platform;
using Terrain.Editor.Rendering;
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
    private ViewportRenderTextureSceneRenderer? viewportSceneRenderer;
    private string sceneSourceStatus = "Scene: pending";
    private string compositorSourceStatus = "Compositor: pending";
    private Entity? debugMarkerEntity;
    private Int2 lastViewportRenderTargetSize;

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
            Texture? defaultTerrainTexture = null;
            try
            {
                defaultTerrainTexture = Content.Load<Texture>("Grid Gray 128x128");
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load default terrain texture asset: {exception.Message}");
            }

            mainWindow.Viewport.InitializeTerrainSupport(GraphicsDevice, editorScene, Input, defaultTerrainTexture);
            mainWindow.Viewport.Camera = FindEditorCamera();
            mainWindow.Viewport.TextureIdProvider = uiRenderer.GetOrCreateTextureId;
            mainWindow.Viewport.HeightmapLoaded += OnViewportHeightmapLoaded;
            mainWindow.Viewport.RefreshCameraForRendering();
            UpdateViewportDiagnostics();
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

        mainWindow?.Update((float)gameTime.TimePerFrame.TotalSeconds);

        // Read viewport input after Stride and the ImGui bridge have updated their per-frame state.
        // Doing this before base.Update() left the editor camera one frame behind, and mouse look
        // could miss completely because the current-frame delta had not been populated yet.
        mainWindow?.Viewport.UpdateCamera((float)gameTime.TimePerFrame.TotalSeconds, Input);
        if (mainWindow?.Viewport.HasPendingCameraRefresh == true)
        {
            // Camera.Update() lives in one place now: the game loop. The controller only mutates
            // Transform, and the viewport only gathers input, which avoids the previous three-way
            // coupling where every layer tried to "fix" camera matrices independently.
            mainWindow.Viewport.RefreshCameraForRendering();
        }
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
                viewportSceneRenderer!.RenderTexture = mainWindow.Viewport.SceneRenderTarget;

                var currentViewportSize = new Int2(
                    mainWindow.Viewport.SceneRenderTarget?.ViewWidth ?? 0,
                    mainWindow.Viewport.SceneRenderTarget?.ViewHeight ?? 0);
                if (currentViewportSize != lastViewportRenderTargetSize)
                {
                    // Refresh the camera only when the offscreen viewport size actually changes.
                    // Continuously forcing Camera.Update() every frame was turning an initially-correct
                    // scene view into a dark/broken one shortly after startup.
                    lastViewportRenderTargetSize = currentViewportSize;
                    mainWindow.Viewport.CameraController.MarkCameraRefreshPending();
                    mainWindow.Viewport.RefreshCameraForRendering();
                }

                UpdateViewportDiagnostics();
            }
            else if (viewportSceneRenderer != null)
            {
                viewportSceneRenderer.RenderTexture = null;
                lastViewportRenderTargetSize = default;
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
        if (mainWindow != null)
        {
            mainWindow.Viewport.HeightmapLoaded -= OnViewportHeightmapLoaded;
        }
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
        TryLoadProjectGraphicsCompositor();
        // Prefer the project's authored scene so the editor viewport inherits the same skybox,
        // light rig, and baseline camera setup as the runtime Terrain scene.
        editorScene = TryLoadProjectScene() ?? CreateViewportScene();
        PrepareSceneForEditor(editorScene);
        SceneSystem.SceneInstance = new SceneInstance(Services, editorScene);
        EnsureTerrainRenderFeature();
        EnsureViewportSceneRenderer();
    }

    private void TryLoadProjectGraphicsCompositor()
    {
        try
        {
            // The runtime game already uses the compiled GraphicsCompositor asset, which contains the
            // terrain render feature and the exact render-stage wiring it expects. Reuse it in the editor
            // so the viewport doesn't drift away from the proven runtime pipeline.
            var graphicsCompositor = Content.Load<GraphicsCompositor>("GraphicsCompositor");
            SceneSystem.GraphicsCompositor = graphicsCompositor;
            compositorSourceStatus = "Compositor: project GraphicsCompositor";
        }
        catch (Exception exception)
        {
            compositorSourceStatus = $"Compositor: fallback ({exception.GetType().Name})";
            System.Diagnostics.Debug.WriteLine($"Failed to load project GraphicsCompositor asset: {exception.Message}");
        }
    }

    private Scene? TryLoadProjectScene()
    {
        try
        {
            var loadedScene = Content.Load<Scene>("MainScene");
            var detachedScene = new Scene();

            // Clone root entities into a fresh scene so the editor can own the resulting SceneInstance
            // without colliding with the entity manager attached to the content-loaded scene asset.
            foreach (var entity in loadedScene.Entities)
            {
                detachedScene.Entities.Add(entity.Clone());
            }

            sceneSourceStatus = "Scene: project MainScene";
            return detachedScene;
        }
        catch (Exception exception)
        {
            sceneSourceStatus = $"Scene: fallback ({exception.GetType().Name})";
            System.Diagnostics.Debug.WriteLine($"Failed to load project MainScene asset: {exception.Message}");
            return null;
        }
    }

    private Scene CreateViewportScene()
    {
        sceneSourceStatus = "Scene: dedicated viewport scene";
        var scene = new Scene();

        var cameraEntity = new Entity("MainCamera")
        {
            new CameraComponent
            {
                Slot = SceneSystem.GraphicsCompositor.Cameras[0].ToSlotId(),
                NearClipPlane = 0.1f,
                FarClipPlane = 100000.0f,
            }
        };
        cameraEntity.Transform.Position = new Vector3(0, 50, -100);
        cameraEntity.Transform.Rotation = Quaternion.RotationX((float)Math.PI / 6);
        scene.Entities.Add(cameraEntity);

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
        scene.Entities.Add(lightEntity);

        var ambientLightEntity = new Entity("AmbientLight")
        {
            new LightComponent
            {
                Type = new LightAmbient
                {
                    Color = new ColorRgbProvider(new Color3(0.35f, 0.35f, 0.35f))
                },
                Intensity = 1.0f
            }
        };
        scene.Entities.Add(ambientLightEntity);

        return scene;
    }

    private void PrepareSceneForEditor(Scene scene)
    {
        bool hasEditorCamera = false;

        foreach (var entity in scene.Entities.ToList())
        {
            if (entity.Get<TerrainComponent>() != null)
            {
                // The runtime scene contains a baked demo terrain path. Remove it so editor-loaded
                // terrains are the only terrain source and we don't accidentally render stale data.
                scene.Entities.Remove(entity);
                continue;
            }

            if (entity.Get<CameraComponent>() != null)
            {
                hasEditorCamera = true;

                // Keep the authored MainScene camera entity intact so the editor inherits the exact
                // skybox-facing startup shot and any scene-specific camera setup that already works.
                // Only strip runtime scripts from it so editor input is the sole camera driver.
                foreach (var script in entity.GetAll<ScriptComponent>().ToList())
                {
                    entity.Remove(script);
                }

                continue;
            }
        }

        if (!hasEditorCamera)
        {
            scene.Entities.Add(CreateEditorCameraEntity());
        }

        TryAddDebugMarker(scene);
    }

    private Entity CreateEditorCameraEntity()
    {
        var cameraEntity = new Entity("EditorCamera")
        {
            new CameraComponent
            {
                Slot = SceneSystem.GraphicsCompositor.Cameras[0].ToSlotId(),
                NearClipPlane = 0.1f,
                FarClipPlane = 100000.0f,
            }
        };

        // Fallback to the expected editor startup shot when the project scene has no authored camera.
        cameraEntity.Transform.Position = new Vector3(0.0f, 160.0f, -96.0f);
        cameraEntity.Transform.Rotation = Quaternion.LookRotation(
            Vector3.Normalize(new Vector3(0.0f, -0.8660254f, 0.5f)),
            Vector3.UnitY);

        cameraEntity.Transform.UpdateWorldMatrix();
        return cameraEntity;
    }

    private void TryAddDebugMarker(Scene scene)
    {
        try
        {
            var sphereModel = Content.Load<Model>("Sphere");
            debugMarkerEntity = new Entity("ViewportDebugSphere")
            {
                new ModelComponent
                {
                    Model = sphereModel,
                }
            };

            // Keep a large reference marker in the viewport scene. If this marker is also invisible,
            // the problem is broader than terrain rendering and we can stop blaming TerrainComponent.
            debugMarkerEntity.Transform.Position = new Vector3(256.0f, 96.0f, 256.0f);
            debugMarkerEntity.Transform.Scale = new Vector3(24.0f);
            scene.Entities.Add(debugMarkerEntity);
        }
        catch (Exception exception)
        {
            debugMarkerEntity = null;
            System.Diagnostics.Debug.WriteLine($"Failed to add viewport debug marker: {exception.Message}");
        }
    }

    private void OnViewportHeightmapLoaded(object? sender, string terrainPath)
    {
        if (debugMarkerEntity == null || mainWindow?.Viewport.TerrainManager == null)
        {
            return;
        }

        var bounds = mainWindow.Viewport.TerrainManager.GetTerrainBounds();
        var center = new Vector3(
            (bounds.Minimum.X + bounds.Maximum.X) * 0.5f,
            bounds.Maximum.Y + 64.0f,
            (bounds.Minimum.Z + bounds.Maximum.Z) * 0.5f);
        debugMarkerEntity.Transform.Position = center;
        debugMarkerEntity.Transform.UpdateWorldMatrix();
    }

    private CameraComponent? FindEditorCamera()
    {
        return SceneSystem.SceneInstance.RootScene.Entities
            .Select(entity => entity.Get<CameraComponent>())
            .FirstOrDefault(camera => camera != null);
    }

    private void EnsureTerrainRenderFeature()
    {
        var graphicsCompositor = SceneSystem.GraphicsCompositor;
        if (graphicsCompositor == null)
        {
            return;
        }

        // The editor scene is assembled in code instead of loading the project's asset compositor,
        // so we need to explicitly attach the terrain render feature or TerrainComponent will never draw.
        if (!graphicsCompositor.RenderFeatures.OfType<TerrainRenderFeature>().Any())
        {
            graphicsCompositor.RenderFeatures.Add(new TerrainRenderFeature());
        }
    }

    private void EnsureViewportSceneRenderer()
    {
        var graphicsCompositor = SceneSystem.GraphicsCompositor;
        if (graphicsCompositor?.Game == null)
        {
            return;
        }

        if (graphicsCompositor.Game is ViewportRenderTextureSceneRenderer existingRenderer)
        {
            viewportSceneRenderer = existingRenderer;
            return;
        }

        // The project Game renderer chain is the path that already proved it can draw the scene skybox
        // and debug geometry into the viewport. Wrapping that exact chain keeps the offscreen RT on the
        // same proven render path instead of swapping to Editor/SingleView and ending up with clear color.
        viewportSceneRenderer = new ViewportRenderTextureSceneRenderer
        {
            Child = graphicsCompositor.Game,
        };
        graphicsCompositor.Game = viewportSceneRenderer;
    }

    private void UpdateViewportDiagnostics()
    {
        // Viewport diagnostics were reduced to the terrain-local overlay inside SceneViewPanel.
        // Keep this method as a no-op so the existing update calls stay harmless while the
        // renderer/input work continues to evolve.
    }
}
