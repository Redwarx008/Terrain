#nullable enable

using Hexa.NET.ImGui;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;
using Color4 = Stride.Core.Mathematics.Color4;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// 场景视图面板 - 3D地形渲染视图
/// </summary>
public class SceneViewPanel : PanelBase
{
    #region 属性

    /// <summary>
    /// 场景渲染目标
    /// </summary>
    public Texture? SceneRenderTarget { get; set; }

    /// <summary>
    /// 当前相机
    /// </summary>
    public CameraComponent? Camera { get; set; }

    /// <summary>
    /// 是否显示网格
    /// </summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>
    /// 是否显示线框
    /// </summary>
    public bool ShowWireframe { get; set; } = false;

    /// <summary>
    /// 是否显示Gizmos
    /// </summary>
    public bool ShowGizmos { get; set; } = true;

    /// <summary>
    /// 视图模式
    /// </summary>
    public SceneViewMode ViewMode { get; set; } = SceneViewMode.Shaded;

    #endregion

    #region 私有字段

    private bool isDragging = false;
    private Vector2 lastMousePos;
    private float cameraDistance = 100.0f;
    private float cameraYaw = 45.0f;
    private float cameraPitch = 30.0f;

    #endregion

    #region 构造函数

    public SceneViewPanel()
    {
        Title = "Scene";
        Icon = Icons.Cube;
        ShowTitleBar = true;
        IsCollapsible = true;
    }

    #endregion

    #region 渲染

    protected override void RenderContent()
    {
        // 渲染工具栏
        RenderToolbar();

        // 渲染3D视图
        Render3DView();

        // 渲染视图信息
        RenderViewInfo();
    }

    private void RenderToolbar()
    {
        var drawList = ImGui.GetWindowDrawList();
        float toolbarHeight = 28;

        // 工具栏背景
        Vector2 toolbarPos = new Vector2(ContentRect.X, ContentRect.Y);
        Vector2 toolbarEnd = new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + toolbarHeight);
        drawList.AddRectFilled(toolbarPos, toolbarEnd, ColorPalette.DarkBackground.ToUint());

        // 视图模式按钮组
        ImGui.SetCursorScreenPos(new Vector2(toolbarPos.X + 8, toolbarPos.Y + 4));

        PushToolbarButtonStyle(ViewMode == SceneViewMode.Shaded);
        if (ImGui.Button("Shaded", new Vector2(60, 20)))
            ViewMode = SceneViewMode.Shaded;
        PopToolbarButtonStyle();

        ImGui.SameLine();
        PushToolbarButtonStyle(ViewMode == SceneViewMode.Wireframe);
        if (ImGui.Button("Wireframe", new Vector2(70, 20)))
            ViewMode = SceneViewMode.Wireframe;
        PopToolbarButtonStyle();

        ImGui.SameLine();
        PushToolbarButtonStyle(ViewMode == SceneViewMode.Textured);
        if (ImGui.Button("Textured", new Vector2(70, 20)))
            ViewMode = SceneViewMode.Textured;
        PopToolbarButtonStyle();

        // 分隔线
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X + 8, toolbarPos.Y + 6));
        ImGui.Text("|");

        // 显示选项
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X + 8, toolbarPos.Y + 4));

        bool showGrid = ShowGrid;
        if (ImGui.Checkbox("Grid", ref showGrid))
            ShowGrid = showGrid;

        ImGui.SameLine();
        bool showGizmos = ShowGizmos;
        if (ImGui.Checkbox("Gizmos", ref showGizmos))
            ShowGizmos = showGizmos;

        // 分隔线
        drawList.AddLine(
            new Vector2(ContentRect.X, ContentRect.Y + toolbarHeight),
            new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + toolbarHeight),
            ColorPalette.Border.ToUint(),
            1.0f
        );
    }

    private void Render3DView()
    {
        float toolbarHeight = 28;
        float infoHeight = 20;
        float viewY = ContentRect.Y + toolbarHeight;
        float viewHeight = ContentRect.Height - toolbarHeight - infoHeight;

        Vector2 viewPos = new Vector2(ContentRect.X, viewY);
        Vector2 viewSize = new Vector2(ContentRect.Width, viewHeight);

        // 渲染场景到纹理（如果可用）
        if (SceneRenderTarget != null)
        {
            // 获取纹理ID（需要通过ImGui渲染器注册）
            // IntPtr textureId = GetImGuiTextureId(SceneRenderTarget);
            // ImGui.SetCursorScreenPos(viewPos);
            // ImGui.Image(textureId, viewSize);

            // 临时：绘制占位背景
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(viewPos, new Vector2(viewPos.X + viewSize.X, viewPos.Y + viewSize.Y), ColorPalette.DarkBackground.ToUint());

            // 绘制网格预览
            if (ShowGrid)
            {
                RenderGridPreview(drawList, viewPos, viewSize);
            }

            // 绘制提示文本
            string hint = "3D Scene View\n(Camera controls will be here)";
            var textSize = ImGui.CalcTextSize(hint);
            var textPos = new Vector2(
                viewPos.X + (viewSize.X - textSize.X) * 0.5f,
                viewPos.Y + (viewSize.Y - textSize.Y) * 0.5f
            );
            drawList.AddText(textPos, ColorPalette.TextSecondary.ToUint(), hint);
        }
        else
        {
            // 无渲染目标时显示占位
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(viewPos, new Vector2(viewPos.X + viewSize.X, viewPos.Y + viewSize.Y), ColorPalette.DarkBackground.ToUint());

            string hint = "No Scene Loaded";
            var textSize = ImGui.CalcTextSize(hint);
            var textPos = new Vector2(
                viewPos.X + (viewSize.X - textSize.X) * 0.5f,
                viewPos.Y + (viewSize.Y - textSize.Y) * 0.5f
            );
            drawList.AddText(textPos, ColorPalette.TextSecondary.ToUint(), hint);
        }

        // 处理相机控制输入
        HandleCameraInput(viewPos, viewSize);
    }

    private void RenderGridPreview(ImDrawListPtr drawList, Vector2 viewPos, Vector2 viewSize)
    {
        uint gridColor = new Color4(0.3f, 0.3f, 0.3f, 0.5f).ToUint();
        float gridSpacing = 50;

        // 绘制水平线
        for (float y = viewPos.Y; y < viewPos.Y + viewSize.Y; y += gridSpacing)
        {
            drawList.AddLine(
                new Vector2(viewPos.X, y),
                new Vector2(viewPos.X + viewSize.X, y),
                gridColor,
                1.0f
            );
        }

        // 绘制垂直线
        for (float x = viewPos.X; x < viewPos.X + viewSize.X; x += gridSpacing)
        {
            drawList.AddLine(
                new Vector2(x, viewPos.Y),
                new Vector2(x, viewPos.Y + viewSize.Y),
                gridColor,
                1.0f
            );
        }
    }

    private void RenderViewInfo()
    {
        float toolbarHeight = 28;
        float infoHeight = 20;
        float viewHeight = ContentRect.Height - toolbarHeight - infoHeight;

        Vector2 infoPos = new Vector2(ContentRect.X, ContentRect.Y + toolbarHeight + viewHeight);
        Vector2 infoEnd = new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + ContentRect.Height);

        var drawList = ImGui.GetWindowDrawList();

        // 信息栏背景
        drawList.AddRectFilled(infoPos, infoEnd, ColorPalette.DarkBackground.ToUint());

        // 顶部边框
        drawList.AddLine(
            infoPos,
            new Vector2(infoEnd.X, infoPos.Y),
            ColorPalette.Border.ToUint(),
            1.0f
        );

        // 信息文本
        string info = $"View: {ViewMode} | Camera: {cameraYaw:F0}°, {cameraPitch:F0}° | Zoom: {cameraDistance:F0}";
        var textPos = new Vector2(infoPos.X + 8, infoPos.Y + 2);
        drawList.AddText(textPos, ColorPalette.TextSecondary.ToUint(), info);
    }

    #endregion

    #region 相机控制

    private void HandleCameraInput(Vector2 viewPos, Vector2 viewSize)
    {
        // 检测鼠标是否在视图内
        var io = ImGui.GetIO();
        Vector2 mousePos = io.MousePos;

        bool isOverView = mousePos.X >= viewPos.X && mousePos.X <= viewPos.X + viewSize.X &&
                         mousePos.Y >= viewPos.Y && mousePos.Y <= viewPos.Y + viewSize.Y;

        if (!isOverView)
            return;

        // 中键拖拽 - 平移
        if (io.MouseDown[2])
        {
            Vector2 delta = io.MouseDelta;
            // TODO: 实现相机平移
        }

        // 右键拖拽 - 旋转
        if (io.MouseDown[1])
        {
            Vector2 delta = io.MouseDelta;
            cameraYaw -= delta.X * 0.5f;
            cameraPitch = Math.Clamp(cameraPitch - delta.Y * 0.5f, -89, 89);
        }

        // 滚轮 - 缩放
        if (io.MouseWheel != 0)
        {
            cameraDistance = Math.Max(10, cameraDistance - io.MouseWheel * 5);
        }
    }

    #endregion

    #region 样式辅助

    private void PushToolbarButtonStyle(bool isActive)
    {
        if (isActive)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ColorPalette.Accent.ToVector4());
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.AccentHover.ToVector4());
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.AccentPressed.ToVector4());
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.Hover.ToVector4());
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.Pressed.ToVector4());
        }
    }

    private void PopToolbarButtonStyle()
    {
        ImGui.PopStyleColor(3);
    }

    #endregion
}

/// <summary>
/// 场景视图模式
/// </summary>
public enum SceneViewMode
{
    Shaded,     // 着色模式
    Wireframe,  // 线框模式
    Textured    // 纹理模式
}
