#nullable enable

using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Colors;
using Stride.Rendering.Lights;
using Stride.Core.Mathematics;
using Terrain.Editor.UI;
using Terrain.Editor.UI.Styling;
using System.Threading.Tasks;
using System;

namespace Terrain.Editor;

/// <summary>
/// 地形编辑器主游戏类
/// </summary>
public class EditorGame : Game
{
    private EditorUIRenderer? uiRenderer;
    private MainWindow? mainWindow;

    protected override void BeginRun()
    {
        base.BeginRun();

        // 配置图形设置
        var graphicsDeviceManager = Services.GetService<IGraphicsDeviceManager>() as GraphicsDeviceManager;
        if (graphicsDeviceManager != null)
        {
            graphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
            graphicsDeviceManager.PreferredBackBufferWidth = 1920;
            graphicsDeviceManager.PreferredBackBufferHeight = 1080;
            graphicsDeviceManager.IsFullScreen = false;
            graphicsDeviceManager.ApplyChanges();
        }

        // 初始化场景
        InitializeScene();
    }

    protected override async Task LoadContent()
    {
        await base.LoadContent();

        // 初始化UI系统（这会自动注册到GameSystems）
        uiRenderer = new EditorUIRenderer(this);

        // 创建主窗口
        mainWindow = new MainWindow();
        mainWindow.Initialize(GraphicsDevice);

        // 设置渲染回调
        uiRenderer.OnRender = () =>
        {
            mainWindow.Render();
        };

        // 应用样式
        EditorStyle.Apply();
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        // EditorUIRenderer.Update() 会自动被GameSystems调用
        // 之后更新我们的UI状态
        mainWindow?.Update((float)gameTime.TimePerFrame.TotalSeconds);
    }

    protected override void Draw(GameTime gameTime)
    {
        // 清除背景
        GraphicsContext.CommandList.Clear(GraphicsDevice.Presenter.BackBuffer, Color4.Black);
        GraphicsContext.CommandList.Clear(GraphicsDevice.Presenter.DepthStencilBuffer, DepthStencilClearOptions.DepthBuffer);

        // 渲染3D场景
        base.Draw(gameTime);

        // 注意：UI渲染在EditorUIRenderer.EndDraw()中自动执行
        // 它会在base.Draw()结束后被GameSystems调用
    }

    protected override void UnloadContent()
    {
        uiRenderer?.Dispose();
        base.UnloadContent();
    }

    private void InitializeScene()
    {
        // 创建基础场景
        var scene = new Scene();
        SceneSystem.SceneInstance = new SceneInstance(Services, scene);

        // 添加相机实体
        var cameraEntity = new Entity("MainCamera")
        {
            new CameraComponent
            {
                Slot = SceneSystem.GraphicsCompositor.Cameras[0].ToSlotId()
            }
        };
        cameraEntity.Transform.Position = new Vector3(0, 50, -100);
        cameraEntity.Transform.Rotation = Quaternion.RotationX((float)Math.PI / 6);
        scene.Entities.Add(cameraEntity);

        // 添加方向光
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
    }
}
