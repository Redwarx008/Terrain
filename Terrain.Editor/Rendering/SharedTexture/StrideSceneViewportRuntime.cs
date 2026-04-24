#nullable enable

using System;
using Stride.Core;
using Stride.Core.IO;
using Stride.Core.Mathematics;
using Stride.Core.Serialization.Contents;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Lights;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Rendering.SharedTexture;

/// <summary>
/// Hosts the minimal Stride scene/compositor runtime needed to render an editor scene
/// into the shared-texture viewport without creating a native window or Game instance.
/// </summary>
public sealed class StrideSceneViewportRuntime : IStrideOffscreenViewportRenderer, IDisposable
{
    private static readonly TimeSpan FallbackElapsed = TimeSpan.FromMilliseconds(16);

    private readonly ServiceRegistry _services = new();
    private readonly EffectSystem _effectSystem;
    private readonly GraphicsContext _graphicsContext;
    private readonly GraphicsCompositor _graphicsCompositor;
    private readonly ContentManager _contentManager;
    private readonly ViewportRenderTextureSceneRenderer _viewportRenderer;
    private readonly EditorTerrainModeController _modeController = new();
    private readonly Scene _scene;
    private readonly SceneInstance _sceneInstance;
    private readonly RenderContext _renderContext;
    private readonly RenderDrawContext _renderDrawContext;
    private readonly CameraComponent _cameraComponent;
    private readonly Entity _cameraEntity;
    private TimeSpan _lastElapsed;
    private int _previousWidth;
    private int _previousHeight;
    private SceneViewMode _sceneViewMode = SceneViewMode.Shaded;
    private bool _isDisposed;

    public StrideSceneViewportRuntime(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _graphicsContext = new GraphicsContext(graphicsDevice);
        _services.AddService<IDatabaseFileProviderService>(new DatabaseFileProviderService(null));
        _contentManager = new ContentManager(_services);
        _effectSystem = new EffectSystem(_services);
        _services.AddService<IGraphicsDeviceService>(new GraphicsDeviceServiceLocal(graphicsDevice));
        _services.AddService<IContentManager>(_contentManager);
        _services.AddService(_contentManager);
        _services.AddService(_graphicsContext);
        _services.AddService(_effectSystem);

        _renderContext = RenderContext.GetShared(_services);
        _renderDrawContext = new RenderDrawContext(_services, _renderContext, _graphicsContext);

        _scene = new Scene();
        _cameraComponent = new CameraComponent();
        _cameraEntity = new Entity("Editor Camera")
        {
            _cameraComponent,
        };
        _cameraEntity.Transform.Position = new Vector3(0f, 128f, 256f);
        _cameraEntity.Transform.Rotation = Quaternion.RotationX(MathUtil.DegreesToRadians(-25f));
        _scene.Entities.Add(_cameraEntity);
        AddDefaultLights(_scene);

        _graphicsCompositor = GraphicsCompositorHelper.CreateDefault(
            enablePostEffects: true,
            modelEffectName: EditorTerrainRenderFeature.EffectName,
            camera: _cameraComponent,
            clearColor: new Color4(0.40491876f, 0.41189542f, 0.43775f, 1.0f),
            graphicsProfile: graphicsDevice.Features.CurrentProfile);

        _graphicsCompositor.RenderFeatures.Add(new EditorTerrainRenderFeature());

        _viewportRenderer = new ViewportRenderTextureSceneRenderer
        {
            Child = _graphicsCompositor.Game,
        };
        _graphicsCompositor.Game = _viewportRenderer;

        _sceneInstance = new SceneInstance(_services, _scene);
        ApplySceneViewMode(SceneViewMode.Shaded);
    }

    public string Description => $"Stride scene runtime ({_sceneViewMode})";

    public Scene Scene => _scene;

    public GraphicsCompositor GraphicsCompositor => _graphicsCompositor;

    public SceneViewMode SceneViewMode => _sceneViewMode;

    public void ApplySceneViewMode(SceneViewMode sceneViewMode)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _sceneViewMode = sceneViewMode;
        _modeController.Apply(sceneViewMode, _graphicsCompositor);
    }

    public bool Render(in StrideOffscreenViewportRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        Texture renderTarget = context.RenderTarget;
        Texture depthBuffer = context.DepthBuffer;
        if (renderTarget.ViewWidth <= 0 || renderTarget.ViewHeight <= 0)
        {
            return false;
        }

        var elapsed = context.Elapsed - _lastElapsed;
        if (elapsed <= TimeSpan.Zero)
        {
            elapsed = FallbackElapsed;
        }

        _lastElapsed = context.Elapsed;
        var gameTime = new GameTime(context.Elapsed, elapsed);

        _viewportRenderer.RenderTexture = renderTarget;

        var commandList = _graphicsContext.CommandList;
        commandList.SetRenderTargetAndViewport(depthBuffer, renderTarget);

        _renderContext.Reset();
        RecycleTemporaryTexturesIfNeeded(renderTarget);
        _renderContext.Time = gameTime;

        _sceneInstance.Update(gameTime);

        using (_renderContext.PushTagAndRestore(GraphicsCompositor.Current, _graphicsCompositor))
        {
            _sceneInstance.Draw(_renderContext);
        }

        _renderDrawContext.ResourceGroupAllocator.Flush();
        _renderDrawContext.QueryManager.Flush();

        using (_renderDrawContext.RenderContext.PushTagAndRestore(SceneInstance.Current, _sceneInstance))
        {
            _graphicsCompositor.Draw(_renderDrawContext);
        }

        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _viewportRenderer.RenderTexture = null;
        _graphicsCompositor.Dispose();
        ((IReferencable)_sceneInstance).Release();
        _effectSystem.Dispose();
    }

    private void RecycleTemporaryTexturesIfNeeded(Texture renderTarget)
    {
        if (_previousWidth == renderTarget.ViewWidth && _previousHeight == renderTarget.ViewHeight)
        {
            return;
        }

        _renderContext.Allocator.Recycle(static link => link.Resource is Texture);
        _previousWidth = renderTarget.ViewWidth;
        _previousHeight = renderTarget.ViewHeight;
    }

    private static void AddDefaultLights(Scene scene)
    {
        scene.Entities.Add(new Entity("Ambient Light")
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
        scene.Entities.Add(keyLight);
    }
}
