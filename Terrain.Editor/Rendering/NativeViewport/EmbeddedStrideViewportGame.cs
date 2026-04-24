#nullable enable

using System;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Lights;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering;
using Terrain.Editor.Services;

namespace Terrain.Editor.Rendering.NativeViewport;

public sealed class EmbeddedStrideViewportGame : Game
{
    private const bool PresenterOnlyDiagnostic = false;

    private readonly EditorTerrainModeController _modeController = new();
    private GraphicsCompositor? _graphicsCompositor;
    private Scene? _scene;
    private CameraComponent? _camera;
    private SceneViewMode _sceneViewMode = SceneViewMode.Shaded;
    private bool _hasRenderedFirstFrame;

    public EmbeddedStrideViewportGame()
    {
        GraphicsDeviceManager.PreferredGraphicsProfile = [GraphicsProfile.Level_11_1];
        GraphicsDeviceManager.PreferredDepthStencilFormat = PixelFormat.D24_UNorm_S8_UInt;
        GraphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
        TreatNotFocusedLikeMinimized = false;
        DrawWhileMinimized = true;
        AutoLoadDefaultSettings = false;
    }

    public event EventHandler? RuntimeReady;

    public event EventHandler? FirstFrameRendered;

    public Scene? Scene => _scene;

    public TerrainManager? TerrainManager { get; private set; }

    public SceneViewMode SceneViewMode => _sceneViewMode;

    public string Diagnostics
    {
        get
        {
            var backBuffer = GraphicsDevice?.Presenter?.BackBuffer;
            var backBufferText = backBuffer == null
                ? "BackBuffer=null"
                : $"BackBuffer={backBuffer.Width}x{backBuffer.Height}";
            var depthText = GraphicsDevice?.Presenter?.DepthStencilBuffer == null
                ? "Depth=null"
                : $"Depth={GraphicsDevice.Presenter.DepthStencilBuffer.Width}x{GraphicsDevice.Presenter.DepthStencilBuffer.Height}";
            var clientBounds = Window?.ClientBounds;
            var clientText = clientBounds == null
                ? "ClientBounds=n/a"
                : $"ClientBounds={clientBounds.Value.Width}x{clientBounds.Value.Height}";
            var focusText = Window == null ? "Focused=n/a" : $"Focused={Window.Focused}";
            var visibleText = Window == null ? "Visible=n/a" : $"Visible={Window.Visible}";
            return $"{backBufferText}; {depthText}; {clientText}; {focusText}; {visibleText}";
        }
    }

    protected override void BeginRun()
    {
        base.BeginRun();

        Window.IsBorderLess = true;
        Window.AllowUserResizing = true;
        Window.IsMouseVisible = true;

        InitializeScene();
        RuntimeReady?.Invoke(this, EventArgs.Empty);
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_graphicsCompositor != null)
        {
            _modeController.Apply(_sceneViewMode, _graphicsCompositor);
        }
    }

    protected override void EndRun()
    {
        TerrainManager?.Dispose();
        TerrainManager = null;
        base.EndRun();
    }

    protected override void Draw(GameTime gameTime)
    {
        if (GraphicsDevice?.Presenter?.BackBuffer != null)
        {
            GraphicsContext.CommandList.Clear(GraphicsDevice.Presenter.BackBuffer, new Color4(0.40491876f, 0.41189542f, 0.43775f, 1.0f));

            if (GraphicsDevice.Presenter.DepthStencilBuffer != null)
            {
                GraphicsContext.CommandList.Clear(GraphicsDevice.Presenter.DepthStencilBuffer, DepthStencilClearOptions.DepthBuffer);
            }
        }

        if (!PresenterOnlyDiagnostic)
        {
            base.Draw(gameTime);
        }

        if (!_hasRenderedFirstFrame)
        {
            _hasRenderedFirstFrame = true;
            FirstFrameRendered?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetSceneViewMode(SceneViewMode sceneViewMode)
    {
        _sceneViewMode = sceneViewMode;
        if (_graphicsCompositor != null)
        {
            _modeController.Apply(sceneViewMode, _graphicsCompositor);
        }
    }

    public void SetViewportSize(int width, int height)
    {
        GraphicsDeviceManager.PreferredBackBufferWidth = Math.Max(1, width);
        GraphicsDeviceManager.PreferredBackBufferHeight = Math.Max(1, height);

        if (IsRunning)
        {
            GraphicsDeviceManager.ApplyChanges();
        }
    }

    private void InitializeScene()
    {
        _scene = new Scene();

        _camera = new CameraComponent
        {
            NearClipPlane = 0.1f,
            FarClipPlane = 100000.0f,
        };

        var cameraEntity = new Entity("Editor Camera")
        {
            _camera,
        };
        cameraEntity.Transform.Position = new Vector3(0.0f, 160.0f, -96.0f);
        cameraEntity.Transform.Rotation = Quaternion.LookRotation(
            Vector3.Normalize(new Vector3(0.0f, -0.8660254f, 0.5f)),
            Vector3.UnitY);
        _scene.Entities.Add(cameraEntity);

        _scene.Entities.Add(new Entity("Ambient Light")
        {
            new LightComponent
            {
                Type = new LightAmbient(),
                Intensity = 0.15f,
            },
        });

        var keyLight = new Entity("Directional Light")
        {
            new LightComponent
            {
                Type = new LightDirectional(),
                Intensity = 2.0f,
            },
        };
        keyLight.Transform.Rotation = Quaternion.RotationX(MathUtil.DegreesToRadians(-55f))
            * Quaternion.RotationY(MathUtil.DegreesToRadians(35f));
        _scene.Entities.Add(keyLight);

        _graphicsCompositor = GraphicsCompositorHelper.CreateDefault(
            enablePostEffects: true,
            modelEffectName: EditorTerrainRenderFeature.EffectName,
            camera: _camera,
            clearColor: new Color4(0.40491876f, 0.41189542f, 0.43775f, 1.0f),
            graphicsProfile: GraphicsDevice.Features.CurrentProfile);
        _graphicsCompositor.Game = new PresenterViewportSceneRenderer
        {
            Child = _graphicsCompositor.Game,
        };
        _camera.Slot = _graphicsCompositor.Cameras[0].ToSlotId();
        _graphicsCompositor.RenderFeatures.Add(new EditorTerrainRenderFeature());
        _modeController.Apply(_sceneViewMode, _graphicsCompositor);

        SceneSystem.GraphicsCompositor = _graphicsCompositor;
        SceneSystem.SceneInstance = new SceneInstance(Services, _scene);
        TerrainManager = new TerrainManager(GraphicsDevice, _scene);
    }
}
