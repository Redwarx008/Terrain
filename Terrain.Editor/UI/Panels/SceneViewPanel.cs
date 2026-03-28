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
    private const float ToolbarBaseHeight = 28.0f;
    private const float InfoBarBaseHeight = 20.0f;

    public Texture? SceneRenderTarget { get; set; }

    public CameraComponent? Camera { get; set; }

    public bool ShowGrid { get; set; } = true;

    public bool ShowWireframe { get; set; } = false;

    public bool ShowGizmos { get; set; } = true;

    public SceneViewMode ViewMode { get; set; } = SceneViewMode.Shaded;

    private bool isDragging;
    private Vector2 lastMousePos;
    private float cameraDistance = 100.0f;
    private float cameraYaw = 45.0f;
    private float cameraPitch = 30.0f;

    public SceneViewPanel()
    {
        Title = "Scene";
        Icon = Icons.Cube;
        ShowTitleBar = true;
        IsCollapsible = true;
    }

    protected override void RenderContent()
    {
        RenderToolbar();
        Render3DView();
        RenderViewInfo();
    }

    private void RenderToolbar()
    {
        var drawList = ImGui.GetWindowDrawList();
        float toolbarHeight = GetToolbarHeight();
        float buttonHeight = EditorStyle.ButtonHeightScaled;
        float paddingX = EditorStyle.ScaleValue(8.0f);
        float paddingY = (toolbarHeight - buttonHeight) * 0.5f;

        Vector2 toolbarPos = new Vector2(ContentRect.X, ContentRect.Y);
        Vector2 toolbarEnd = new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + toolbarHeight);
        drawList.AddRectFilled(toolbarPos, toolbarEnd, ColorPalette.DarkBackground.ToUint());

        ImGui.SetCursorScreenPos(new Vector2(toolbarPos.X + paddingX, toolbarPos.Y + paddingY));

        PushToolbarButtonStyle(ViewMode == SceneViewMode.Shaded);
        if (ImGui.Button("Shaded", GetToolbarButtonSize("Shaded", buttonHeight)))
            ViewMode = SceneViewMode.Shaded;
        PopToolbarButtonStyle();

        ImGui.SameLine();
        PushToolbarButtonStyle(ViewMode == SceneViewMode.Wireframe);
        if (ImGui.Button("Wireframe", GetToolbarButtonSize("Wireframe", buttonHeight)))
            ViewMode = SceneViewMode.Wireframe;
        PopToolbarButtonStyle();

        ImGui.SameLine();
        PushToolbarButtonStyle(ViewMode == SceneViewMode.Textured);
        if (ImGui.Button("Textured", GetToolbarButtonSize("Textured", buttonHeight)))
            ViewMode = SceneViewMode.Textured;
        PopToolbarButtonStyle();

        ImGui.SameLine(0.0f, EditorStyle.ScaleValue(8.0f));
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, toolbarPos.Y + (toolbarHeight - ImGui.CalcTextSize("|").Y) * 0.5f));
        ImGui.Text("|");

        ImGui.SameLine(0.0f, EditorStyle.ScaleValue(8.0f));
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, toolbarPos.Y + paddingY));

        bool showGrid = ShowGrid;
        if (ImGui.Checkbox("Grid", ref showGrid))
            ShowGrid = showGrid;

        ImGui.SameLine();
        bool showGizmos = ShowGizmos;
        if (ImGui.Checkbox("Gizmos", ref showGizmos))
            ShowGizmos = showGizmos;

        drawList.AddLine(
            new Vector2(ContentRect.X, ContentRect.Y + toolbarHeight),
            new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + toolbarHeight),
            ColorPalette.Border.ToUint(),
            1.0f);
    }

    private void Render3DView()
    {
        float toolbarHeight = GetToolbarHeight();
        float infoHeight = GetInfoBarHeight();
        float viewY = ContentRect.Y + toolbarHeight;
        float viewHeight = Math.Max(0.0f, ContentRect.Height - toolbarHeight - infoHeight);

        Vector2 viewPos = new Vector2(ContentRect.X, viewY);
        Vector2 viewSize = new Vector2(ContentRect.Width, viewHeight);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(viewPos, new Vector2(viewPos.X + viewSize.X, viewPos.Y + viewSize.Y), ColorPalette.DarkBackground.ToUint());

        if (SceneRenderTarget != null)
        {
            if (ShowGrid)
            {
                RenderGridPreview(drawList, viewPos, viewSize);
            }

            string hint = "3D Scene View\n(Camera controls will be here)";
            var textSize = ImGui.CalcTextSize(hint);
            var textPos = new Vector2(
                viewPos.X + (viewSize.X - textSize.X) * 0.5f,
                viewPos.Y + (viewSize.Y - textSize.Y) * 0.5f);
            drawList.AddText(textPos, ColorPalette.TextSecondary.ToUint(), hint);
        }
        else
        {
            string hint = "No Scene Loaded";
            var textSize = ImGui.CalcTextSize(hint);
            var textPos = new Vector2(
                viewPos.X + (viewSize.X - textSize.X) * 0.5f,
                viewPos.Y + (viewSize.Y - textSize.Y) * 0.5f);
            drawList.AddText(textPos, ColorPalette.TextSecondary.ToUint(), hint);
        }

        HandleCameraInput(viewPos, viewSize);
    }

    private void RenderGridPreview(ImDrawListPtr drawList, Vector2 viewPos, Vector2 viewSize)
    {
        uint gridColor = new Color4(0.3f, 0.3f, 0.3f, 0.5f).ToUint();
        float gridSpacing = EditorStyle.ScaleValue(50.0f);

        for (float y = viewPos.Y; y < viewPos.Y + viewSize.Y; y += gridSpacing)
        {
            drawList.AddLine(
                new Vector2(viewPos.X, y),
                new Vector2(viewPos.X + viewSize.X, y),
                gridColor,
                1.0f);
        }

        for (float x = viewPos.X; x < viewPos.X + viewSize.X; x += gridSpacing)
        {
            drawList.AddLine(
                new Vector2(x, viewPos.Y),
                new Vector2(x, viewPos.Y + viewSize.Y),
                gridColor,
                1.0f);
        }
    }

    private void RenderViewInfo()
    {
        float toolbarHeight = GetToolbarHeight();
        float infoHeight = GetInfoBarHeight();
        float viewHeight = Math.Max(0.0f, ContentRect.Height - toolbarHeight - infoHeight);

        Vector2 infoPos = new Vector2(ContentRect.X, ContentRect.Y + toolbarHeight + viewHeight);
        Vector2 infoEnd = new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + ContentRect.Height);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(infoPos, infoEnd, ColorPalette.DarkBackground.ToUint());
        drawList.AddLine(
            infoPos,
            new Vector2(infoEnd.X, infoPos.Y),
            ColorPalette.Border.ToUint(),
            1.0f);

        string info = $"View: {ViewMode} | Camera: {cameraYaw:F0}°, {cameraPitch:F0}° | Zoom: {cameraDistance:F0}";
        var textPos = new Vector2(
            infoPos.X + EditorStyle.ScaleValue(8.0f),
            infoPos.Y + (infoHeight - ImGui.CalcTextSize(info).Y) * 0.5f);
        drawList.AddText(textPos, ColorPalette.TextSecondary.ToUint(), info);
    }

    private void HandleCameraInput(Vector2 viewPos, Vector2 viewSize)
    {
        var io = ImGui.GetIO();
        Vector2 mousePos = io.MousePos;

        bool isOverView =
            mousePos.X >= viewPos.X && mousePos.X <= viewPos.X + viewSize.X &&
            mousePos.Y >= viewPos.Y && mousePos.Y <= viewPos.Y + viewSize.Y;

        if (!isOverView)
            return;

        if (io.MouseDown[2])
        {
            Vector2 delta = io.MouseDelta;
            lastMousePos = delta;
            isDragging = true;
        }
        else
        {
            isDragging = false;
        }

        if (io.MouseDown[1])
        {
            Vector2 delta = io.MouseDelta;
            cameraYaw -= delta.X * 0.5f;
            cameraPitch = Math.Clamp(cameraPitch - delta.Y * 0.5f, -89, 89);
        }

        if (io.MouseWheel != 0)
        {
            cameraDistance = Math.Max(10, cameraDistance - io.MouseWheel * 5);
        }
    }

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

    private float GetToolbarHeight()
    {
        return MathF.Max(EditorStyle.ScaleValue(ToolbarBaseHeight), EditorStyle.ButtonHeightScaled + EditorStyle.ScaleValue(8.0f));
    }

    private float GetInfoBarHeight()
    {
        return MathF.Max(EditorStyle.ScaleValue(InfoBarBaseHeight), ImGui.GetTextLineHeight() + EditorStyle.ScaleValue(6.0f));
    }

    private static Vector2 GetToolbarButtonSize(string label, float buttonHeight)
    {
        float width = MathF.Max(EditorStyle.ScaleValue(60.0f), ImGui.CalcTextSize(label).X + EditorStyle.ScaleValue(16.0f));
        return new Vector2(width, buttonHeight);
    }
}

/// <summary>
/// 场景视图模式
/// </summary>
public enum SceneViewMode
{
    Shaded,
    Wireframe,
    Textured
}
