#nullable enable

using System;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Lights;
using Terrain.Editor.Input;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering;
using Terrain.Editor.Services;

namespace Terrain.Editor.Rendering.NativeViewport;

public sealed class EmbeddedStrideViewportGame : Game
{
    private const bool PresenterOnlyDiagnostic = false;

    private readonly EditorTerrainModeController _modeController = new();
    private readonly HybridCameraController _cameraController = new();
    private readonly EditorState _editorState = EditorState.Instance;

    private GraphicsCompositor? _graphicsCompositor;
    private Scene? _scene;
    private CameraComponent? _camera;
    private SceneViewMode _sceneViewMode = SceneViewMode.Perspective;
    private bool _hasRenderedFirstFrame;
    private bool _isBrushStrokeActive;
    private bool _wasLeftMouseDown;

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

        UpdateCamera((float)gameTime.Elapsed.TotalSeconds);
        UpdateBrush((float)gameTime.Elapsed.TotalSeconds);
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

    private void UpdateCamera(float deltaTime)
    {
        _cameraController.Update(deltaTime, Input);

        if (_cameraController.HasPendingCameraRefresh && _camera != null)
        {
            _camera.Entity.Transform.UpdateWorldMatrix();
            float aspectRatio = (float)GraphicsDevice.Presenter.BackBuffer.Width
                              / GraphicsDevice.Presenter.BackBuffer.Height;
            _camera.Update(aspectRatio);
        }
    }

    private void UpdateBrush(float deltaTime)
    {
        if (TerrainManager == null || _camera == null || !_editorState.HasSelectedTool)
        {
            EndBrushStrokeIfNeeded();
            return;
        }

        bool leftMouseDown = Input.IsMouseButtonDown(MouseButton.Left);
        bool rightMouseDown = Input.IsMouseButtonDown(MouseButton.Right);

        // Camera rotation takes priority — right-button orbit cancels brush strokes.
        if (rightMouseDown)
        {
            EndBrushStrokeIfNeeded();
            _wasLeftMouseDown = false;
            return;
        }

        // No terrain data loaded — skip brush input.
        if (!TerrainManager.HasHeightCache)
        {
            EndBrushStrokeIfNeeded();
            _wasLeftMouseDown = leftMouseDown;
            return;
        }

        if (leftMouseDown)
        {
            Vector3? worldPosition = RaycastTerrain();
            if (worldPosition == null)
            {
                // No intersection — cancel the stroke.
                EndBrushStrokeIfNeeded();
                _wasLeftMouseDown = true;
                return;
            }

            if (!_isBrushStrokeActive)
            {
                BeginBrushStroke(worldPosition.Value);
            }

            ApplyBrushStroke(worldPosition.Value, deltaTime);
        }
        else
        {
            EndBrushStrokeIfNeeded();
        }

        _wasLeftMouseDown = leftMouseDown;
    }

    private Vector3? RaycastTerrain()
    {
        if (_camera == null || TerrainManager == null || Window == null)
        {
            return null;
        }

        var mousePosition = Input.MousePosition;
        var clientBounds = Window.ClientBounds;
        float viewportWidth = clientBounds.Width;
        float viewportHeight = clientBounds.Height;

        var (rayOrigin, rayDirection) = TerrainRaycast.ScreenToWorldRay(
            mousePosition.X * viewportWidth,
            mousePosition.Y * viewportHeight,
            0, 0,
            viewportWidth, viewportHeight,
            _camera);

        return TerrainRaycast.RayTerrainIntersection(rayOrigin, rayDirection, TerrainManager);
    }

    private void BeginBrushStroke(Vector3 worldPosition)
    {
        _isBrushStrokeActive = true;

        switch (_editorState.CurrentEditorMode)
        {
            case EditorMode.Sculpt:
                string heightToolName = _editorState.CurrentHeightTool.ToString();
                HeightEditor.Instance.BeginStroke(heightToolName, worldPosition, TerrainManager!);
                break;

            case EditorMode.Paint:
                string paintToolName = _editorState.CurrentPaintTool.ToString();
                PaintEditor.Instance.BeginStroke(paintToolName, TerrainManager!);
                break;

            case EditorMode.Roads:
                // Roads mode currently exposes shell-only layout controls.
                break;
        }
    }

    private void ApplyBrushStroke(Vector3 worldPosition, float deltaTime)
    {
        switch (_editorState.CurrentEditorMode)
        {
            case EditorMode.Sculpt:
                HeightEditor.Instance.ApplyStroke(worldPosition, TerrainManager!, deltaTime);
                break;

            case EditorMode.Paint:
                if (TerrainManager!.MaterialIndices != null)
                {
                    PaintEditor.Instance.ApplyStroke(
                        worldPosition,
                        TerrainManager.MaterialIndices,
                        TerrainManager.HeightCacheWidth,
                        TerrainManager.HeightCacheHeight,
                        TerrainManager);
                }

                break;

            case EditorMode.Roads:
                break;
        }
    }

    private void EndBrushStrokeIfNeeded()
    {
        if (!_isBrushStrokeActive)
        {
            return;
        }

        _isBrushStrokeActive = false;

        switch (_editorState.CurrentEditorMode)
        {
            case EditorMode.Sculpt:
                HeightEditor.Instance.EndStroke();
                break;

            case EditorMode.Paint:
                PaintEditor.Instance.EndStroke();
                break;

            case EditorMode.Roads:
                // Roads mode currently has no stroke lifecycle.
                break;
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

        _cameraController.Camera = _camera;

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
