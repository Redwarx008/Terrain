#nullable enable

using Hexa.NET.ImGui;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using System;
using System.Numerics;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector4 = System.Numerics.Vector4;
using Terrain.Editor.Input;
using Terrain.Editor.Rendering;
using Terrain.Editor.Services;
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

    // Terrain rendering support
    private readonly HybridCameraController cameraController;
    private SceneRenderTargetManager? renderTargetManager;
    private TerrainManager? terrainManager;

    // Render target display (placeholder approach until native pointer integration)
    public Texture? SceneRenderTarget { get; set; }

    public CameraComponent? Camera
    {
        get => cameraController.Camera;
        set => cameraController.Camera = value;
    }

    public bool ShowGrid { get; set; } = true;
    public bool ShowWireframe { get; set; } = false;
    public bool ShowGizmos { get; set; } = true;
    public SceneViewMode ViewMode { get; set; } = SceneViewMode.Shaded;

    // Expose camera controller for external access
    public HybridCameraController CameraController => cameraController;
    public TerrainManager? TerrainManager => terrainManager;

    // Events for heightmap loading
    public event EventHandler<string>? HeightmapLoaded;
    public event EventHandler<string>? HeightmapLoadFailed;

    public SceneViewPanel()
    {
        Title = "Scene";
        Icon = Icons.Cube;
        ShowTitleBar = true;
        IsCollapsible = true;

        cameraController = new HybridCameraController();
    }

    /// <summary>
    /// Initializes terrain rendering support with required services.
    /// </summary>
    public void InitializeTerrainSupport(GraphicsDevice device, Scene scene, InputManager input)
    {
        renderTargetManager = new SceneRenderTargetManager();
        terrainManager = new TerrainManager(device, scene);

        // Wire up terrain loaded event
        terrainManager.TerrainLoaded += (s, e) =>
        {
            HeightmapLoaded?.Invoke(this, e.TerrainPath);

            // Reset camera to terrain bounds
            var bounds = terrainManager.GetTerrainBounds();
            if (bounds.Maximum.X > 0 && bounds.Maximum.Z > 0)
            {
                cameraController.ResetToTerrainBounds(
                    bounds.Maximum.X,
                    bounds.Maximum.Z,
                    bounds.Maximum.Y);
            }
        };
    }

    /// <summary>
    /// Loads a heightmap file and creates terrain.
    /// </summary>
    public async void LoadHeightmap(string path)
    {
        if (terrainManager == null)
        {
            HeightmapLoadFailed?.Invoke(this, "TerrainManager not initialized");
            return;
        }

        var entity = await terrainManager.LoadTerrainAsync(path);
        if (entity == null)
        {
            HeightmapLoadFailed?.Invoke(this, $"Failed to load heightmap: {path}");
        }
    }

    /// <summary>
    /// Updates camera controller. Call this every frame.
    /// </summary>
    public void UpdateCamera(float deltaTime, InputManager input)
    {
        cameraController.Update(deltaTime, input);
    }

    /// <summary>
    /// Updates render target size. Call this when panel size changes.
    /// </summary>
    public void UpdateRenderTarget(GraphicsDevice device, Stride.Core.Mathematics.Vector2 size)
    {
        renderTargetManager?.GetOrCreate(device, size);
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

        NumericsVector2 toolbarPos = new NumericsVector2(ContentRect.X, ContentRect.Y);
        NumericsVector2 toolbarEnd = new NumericsVector2(ContentRect.X + ContentRect.Width, ContentRect.Y + toolbarHeight);
        drawList.AddRectFilled(toolbarPos, toolbarEnd, ColorPalette.DarkBackground.ToUint());

        ImGui.SetCursorScreenPos(new NumericsVector2(toolbarPos.X + paddingX, toolbarPos.Y + paddingY));

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
        ImGui.SetCursorScreenPos(new NumericsVector2(ImGui.GetCursorScreenPos().X, toolbarPos.Y + (toolbarHeight - ImGui.CalcTextSize("|").Y) * 0.5f));
        ImGui.Text("|");

        ImGui.SameLine(0.0f, EditorStyle.ScaleValue(8.0f));
        ImGui.SetCursorScreenPos(new NumericsVector2(ImGui.GetCursorScreenPos().X, toolbarPos.Y + paddingY));

        bool showGrid = ShowGrid;
        if (ImGui.Checkbox("Grid", ref showGrid))
            ShowGrid = showGrid;

        ImGui.SameLine();
        bool showGizmos = ShowGizmos;
        if (ImGui.Checkbox("Gizmos", ref showGizmos))
            ShowGizmos = showGizmos;

        drawList.AddLine(
            new NumericsVector2(ContentRect.X, ContentRect.Y + toolbarHeight),
            new NumericsVector2(ContentRect.X + ContentRect.Width, ContentRect.Y + toolbarHeight),
            ColorPalette.Border.ToUint(),
            1.0f);
    }

    private void Render3DView()
    {
        float toolbarHeight = GetToolbarHeight();
        float infoHeight = GetInfoBarHeight();
        float viewY = ContentRect.Y + toolbarHeight;
        float viewHeight = Math.Max(0.0f, ContentRect.Height - toolbarHeight - infoHeight);

        NumericsVector2 viewPos = new NumericsVector2(ContentRect.X, viewY);
        NumericsVector2 viewSize = new NumericsVector2(ContentRect.Width, viewHeight);

        var drawList = ImGui.GetWindowDrawList();

        // Draw background
        drawList.AddRectFilled(viewPos, new NumericsVector2(viewPos.X + viewSize.X, viewPos.Y + viewSize.Y), ColorPalette.DarkBackground.ToUint());

        if (terrainManager?.CurrentTerrain == null)
        {
            // No terrain loaded - show placeholder with hint
            if (ShowGrid)
            {
                RenderGridPreview(drawList, viewPos, viewSize);
            }

            string hint = "Use File > Open to load a heightmap";
            var textSize = ImGui.CalcTextSize(hint);
            var textPos = new NumericsVector2(
                viewPos.X + (viewSize.X - textSize.X) * 0.5f,
                viewPos.Y + (viewSize.Y - textSize.Y) * 0.5f);
            drawList.AddText(textPos, ColorPalette.TextSecondary.ToUint(), hint);
        }
        else if (renderTargetManager?.RenderTarget != null)
        {
            // Terrain is loaded - show render target preview
            // Note: Full ImGui.Image integration requires native pointer access
            // For now, show a placeholder indicating terrain is loaded
            if (ShowGrid)
            {
                RenderGridPreview(drawList, viewPos, viewSize);
            }

            string status = $"Terrain Loaded ({terrainManager.GetTerrainBounds().Maximum.X:F0} x {terrainManager.GetTerrainBounds().Maximum.Z:F0})";
            var textSize = ImGui.CalcTextSize(status);
            var textPos = new NumericsVector2(
                viewPos.X + (viewSize.X - textSize.X) * 0.5f,
                viewPos.Y + EditorStyle.ScaleValue(20.0f));
            drawList.AddText(textPos, ColorPalette.TextPrimary.ToUint(), status);
        }

        HandleCameraInput(viewPos, viewSize);
    }

    private void RenderGridPreview(ImDrawListPtr drawList, NumericsVector2 viewPos, NumericsVector2 viewSize)
    {
        uint gridColor = new Color4(0.3f, 0.3f, 0.3f, 0.5f).ToUint();
        float gridSpacing = EditorStyle.ScaleValue(50.0f);

        for (float y = viewPos.Y; y < viewPos.Y + viewSize.Y; y += gridSpacing)
        {
            drawList.AddLine(
                new NumericsVector2(viewPos.X, y),
                new NumericsVector2(viewPos.X + viewSize.X, y),
                gridColor,
                1.0f);
        }

        for (float x = viewPos.X; x < viewPos.X + viewSize.X; x += gridSpacing)
        {
            drawList.AddLine(
                new NumericsVector2(x, viewPos.Y),
                new NumericsVector2(x, viewPos.Y + viewSize.Y),
                gridColor,
                1.0f);
        }
    }

    private void RenderViewInfo()
    {
        float toolbarHeight = GetToolbarHeight();
        float infoHeight = GetInfoBarHeight();
        float viewHeight = Math.Max(0.0f, ContentRect.Height - toolbarHeight - infoHeight);

        NumericsVector2 infoPos = new NumericsVector2(ContentRect.X, ContentRect.Y + toolbarHeight + viewHeight);
        NumericsVector2 infoEnd = new NumericsVector2(ContentRect.X + ContentRect.Width, ContentRect.Y + ContentRect.Height);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(infoPos, infoEnd, ColorPalette.DarkBackground.ToUint());
        drawList.AddLine(
            infoPos,
            new NumericsVector2(infoEnd.X, infoPos.Y),
            ColorPalette.Border.ToUint(),
            1.0f);

        // Show camera mode (Orbit/Fly)
        string mode = cameraController.IsFlyModeActive ? "Fly" : "Orbit";
        string info = $"Mode: {mode} | Center: {cameraController.OrbitCenter.X:F0}, {cameraController.OrbitCenter.Y:F0}, {cameraController.OrbitCenter.Z:F0}";
        var textPos = new NumericsVector2(
            infoPos.X + EditorStyle.ScaleValue(8.0f),
            infoPos.Y + (infoHeight - ImGui.CalcTextSize(info).Y) * 0.5f);
        drawList.AddText(textPos, ColorPalette.TextSecondary.ToUint(), info);
    }

    private void HandleCameraInput(NumericsVector2 viewPos, NumericsVector2 viewSize)
    {
        var io = ImGui.GetIO();
        NumericsVector2 mousePos = io.MousePos;

        bool isOverView =
            mousePos.X >= viewPos.X && mousePos.X <= viewPos.X + viewSize.X &&
            mousePos.Y >= viewPos.Y && mousePos.Y <= viewPos.Y + viewSize.Y;

        if (!isOverView)
            return;

        // Check for double-click to reset camera
        if (io.MouseDoubleClicked[0])
        {
            if (terrainManager != null)
            {
                var bounds = terrainManager.GetTerrainBounds();
                if (bounds.Maximum.X > 0 && bounds.Maximum.Z > 0)
                {
                    cameraController.ResetToTerrainBounds(
                        bounds.Maximum.X,
                        bounds.Maximum.Z,
                        bounds.Maximum.Y);
                }
            }
            return;
        }

        // Let ImGui capture check happen first
        if (io.WantCaptureMouse)
            return;

        // Camera input is handled by HybridCameraController via UpdateCamera()
        // This method is now primarily for detecting double-click and hover state
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
            ImGui.PushStyleColor(ImGuiCol.Button, new NumericsVector4(0, 0, 0, 0));
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

    private static NumericsVector2 GetToolbarButtonSize(string label, float buttonHeight)
    {
        float width = MathF.Max(EditorStyle.ScaleValue(60.0f), ImGui.CalcTextSize(label).X + EditorStyle.ScaleValue(16.0f));
        return new NumericsVector2(width, buttonHeight);
    }

    public override void Dispose()
    {
        renderTargetManager?.Dispose();
        terrainManager?.Dispose();
        base.Dispose();
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
