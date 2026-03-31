#nullable enable

using Hexa.NET.ImGui;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using System;
using System.Collections.Generic;
using System.Numerics;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector4 = System.Numerics.Vector4;
using StrideVector3 = Stride.Core.Mathematics.Vector3;
using StrideVector4 = Stride.Core.Mathematics.Vector4;
using Terrain.Editor.Input;
using Terrain.Editor.Rendering;
using Terrain.Editor.Services;
using Terrain.Editor.UI.Styling;
using Terrain;
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

    // Brush preview support
    private readonly BrushParameters _brushParams = BrushParameters.Instance;

    // Render target display (placeholder approach until native pointer integration)
    public Texture? SceneRenderTarget { get; set; }
    public Func<Texture, ImTextureID>? TextureIdProvider { get; set; }
    public bool IsViewportHovered { get; private set; }
    public bool IsViewportInteracting { get; private set; }

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
    public bool HasPendingCameraRefresh => cameraController.HasPendingCameraRefresh;

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
    public void InitializeTerrainSupport(GraphicsDevice device, Scene scene, InputManager input, Texture? defaultTerrainTexture = null)
    {
        renderTargetManager = new SceneRenderTargetManager();
        terrainManager = new TerrainManager(device, scene, defaultTerrainTexture);

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
        var absoluteMousePosition = new NumericsVector2(input.AbsoluteMousePosition.X, input.AbsoluteMousePosition.Y);
        bool isMouseOverViewport = IsMouseInsideViewportRect(absoluteMousePosition);
        bool isRightMouseDown = input.IsMouseButtonDown(MouseButton.Right);
        bool isMiddleMouseDown = input.IsMouseButtonDown(MouseButton.Middle);

        // Track interaction state from viewport-local hit testing here instead of depending on the
        // later ImGui InvisibleButton pass. Camera input runs before that render step, so using the
        // previous frame's active state made mouse look feel dead or one frame late.
        IsViewportHovered = isMouseOverViewport;
        IsViewportInteracting = isMouseOverViewport && (isRightMouseDown || isMiddleMouseDown);

        if (!isMouseOverViewport && !IsViewportInteracting)
        {
            return;
        }

        // Use Stride's raw mouse state here. The ImGui IO snapshot was not reliable at this update point,
        // which left yaw/pitch frozen even though the viewport had focus and the camera position changed.
        cameraController.UpdateFromViewportInput(
            deltaTime,
            new NumericsVector2(input.AbsoluteMouseDelta.X, input.AbsoluteMouseDelta.Y),
            input.MouseWheelDelta,
            isRightMouseDown,
            isMiddleMouseDown,
            input.IsKeyDown(Keys.W),
            input.IsKeyDown(Keys.S),
            input.IsKeyDown(Keys.A),
            input.IsKeyDown(Keys.D),
            input.IsKeyDown(Keys.Q),
            input.IsKeyDown(Keys.E),
            input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift));
    }

    public void RefreshCameraForRendering()
    {
        cameraController.RefreshCameraMatrices(GetViewportAspectRatio());
    }

    /// <summary>
    /// Updates render target size. Call this when panel size changes.
    /// </summary>
    public void UpdateRenderTarget(GraphicsDevice device, Stride.Core.Mathematics.Vector2 size)
    {
        SceneRenderTarget = renderTargetManager?.GetOrCreate(device, size);
    }

    protected override void RenderContent()
    {
        RenderToolbar();
        Render3DView();
        RenderViewInfo();
    }

    protected override void RenderBackground()
    {
        if (SceneRenderTarget == null)
        {
            base.RenderBackground();
            return;
        }

        // As soon as a live scene render target exists, never paint the default panel background over it.
        // The viewport must stay transparent whether or not a terrain is currently loaded, otherwise the
        // authored skybox falls back to a flat gray panel and it looks like scene rendering regressed.
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(
            Position,
            new NumericsVector2(Position.X + Size.X, Position.Y + Size.Y),
            ColorPalette.Border.ToUint(),
            0,
            ImDrawFlags.None,
            1.0f);
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

        if (SceneRenderTarget != null && TextureIdProvider != null)
        {
            // Always show the live scene render target. Even before a terrain is loaded, the authored
            // scene skybox and lights are useful camera references; replacing the viewport with a flat
            // placeholder makes it look like camera movement is broken when the scene is actually rendering.
            ImGui.SetCursorScreenPos(viewPos);
            ImGui.Image(TextureIdProvider(SceneRenderTarget), viewSize);
        }
        else
        {
            drawList.AddRectFilled(viewPos, new NumericsVector2(viewPos.X + viewSize.X, viewPos.Y + viewSize.Y), ColorPalette.DarkBackground.ToUint());
        }

        if (terrainManager?.CurrentTerrain == null)
        {
            if (SceneRenderTarget == null && ShowGrid)
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
        else
        {
            // Keep the overlay intentionally short so it never grows wider than the viewport and
            // starts covering the scene.
            string status = $"Terrain Loaded ({terrainManager.GetTerrainBounds().Maximum.X:F0} x {terrainManager.GetTerrainBounds().Maximum.Z:F0})";
            var statusPadding = EditorStyle.ScaleValue(10.0f);
            var textPos = new NumericsVector2(
                viewPos.X + statusPadding,
                viewPos.Y + statusPadding);
            var textSize = ImGui.CalcTextSize(status);
            float maxTextWidth = textSize.X;
            var bgMin = new NumericsVector2(textPos.X - statusPadding * 0.6f, textPos.Y - statusPadding * 0.4f);
            float maxOverlayWidth = MathF.Max(EditorStyle.ScaleValue(160.0f), viewSize.X - statusPadding * 2.0f);
            float overlayWidth = MathF.Min(maxTextWidth + statusPadding * 1.2f, maxOverlayWidth);
            var bgMax = new NumericsVector2(
                bgMin.X + overlayWidth,
                textPos.Y + textSize.Y + statusPadding * 0.4f);
            drawList.AddRectFilled(bgMin, bgMax, new Color4(0.04f, 0.04f, 0.04f, 0.7f).ToUint(), EditorStyle.ScaleValue(4.0f));
            drawList.AddText(textPos, ColorPalette.TextPrimary.ToUint(), status);
        }

        // Register an explicit interactive item over the rendered image so the viewport can own mouse
        // hover/active state like a real control instead of relying on the surrounding window to infer it.
        ImGui.SetCursorScreenPos(viewPos);
        ImGui.InvisibleButton($"##viewport_input_{Id}", viewSize);
        IsViewportHovered = ImGui.IsItemHovered();
        var io = ImGui.GetIO();
        IsViewportInteracting = ImGui.IsItemActive() || (IsViewportHovered && io.MouseDown[(int)ImGuiMouseButton.Right]);

        if (IsViewportInteracting)
        {
            // Tell ImGui to stop claiming the mouse on the next frame while the user is looking around
            // in the viewport, otherwise right-drag can get stuck in UI capture and never reach the camera.
            ImGui.SetNextFrameWantCaptureMouse(false);
        }

        HandleCameraInput(viewPos, viewSize);

        // Render brush preview overlay (Phase 2)
        RenderBrushPreview(viewPos, viewSize);
    }

    private void RenderBrushPreview(NumericsVector2 viewPos, NumericsVector2 viewSize)
    {
        // Per D-09: Only show preview when viewport is hovered and not interacting
        if (!IsViewportHovered || IsViewportInteracting)
            return;

        // Check if we have terrain loaded
        if (terrainManager?.CurrentTerrain == null)
            return;

        var io = ImGui.GetIO();
        NumericsVector2 mousePos = io.MousePos;

        // Check if mouse is within the viewport bounds
        if (mousePos.X < viewPos.X || mousePos.X > viewPos.X + viewSize.X ||
            mousePos.Y < viewPos.Y || mousePos.Y > viewPos.Y + viewSize.Y)
            return;

        // Get camera - need to access the camera component
        var camera = GetActiveCamera();
        if (camera == null)
            return;

        // Convert screen coordinates to world ray
        var (rayOrigin, rayDirection) = TerrainRaycast.ScreenToWorldRay(
            mousePos.X,
            mousePos.Y,
            viewPos.X,
            viewPos.Y,
            viewSize.X,
            viewSize.Y,
            camera);

        // Find intersection with terrain
        var hitPoint = TerrainRaycast.RayTerrainIntersection(
            rayOrigin,
            rayDirection,
            terrainManager);

        if (hitPoint == null)
            return;

        // Generate world-space circle points that follow terrain
        float worldRadius = _brushParams.Size * 0.5f;
        var circlePoints = GenerateWorldSpaceCircle(hitPoint.Value, worldRadius, 32);

        // Project world points to screen and draw
        var drawList = ImGui.GetWindowDrawList();
        uint outerColor = ColorPalette.Accent.WithAlpha(0.5f).ToUint();
        uint innerColor = ColorPalette.Accent.WithAlpha(0.3f).ToUint();

        // Draw outer circle (falloff boundary)
        DrawProjectedCircle(drawList, circlePoints, camera, viewPos, viewSize, outerColor, 2.0f);

        // Draw inner circle (100% strength area) using EffectiveFalloff
        float innerRadius = worldRadius * _brushParams.EffectiveFalloff;
        if (innerRadius > 0.5f)
        {
            var innerCirclePoints = GenerateWorldSpaceCircle(hitPoint.Value, innerRadius, 24);
            DrawProjectedCircleFilled(drawList, innerCirclePoints, camera, viewPos, viewSize, innerColor);
        }

        // Draw center point
        var centerScreen = WorldToScreen(hitPoint.Value, camera, viewPos, viewSize);
        if (centerScreen != null)
        {
            drawList.AddCircleFilled(centerScreen.Value, 3.0f, ColorPalette.Accent.WithAlpha(0.8f).ToUint());
        }
    }

    private CameraComponent? GetActiveCamera()
    {
        // Return the camera used for rendering the scene
        return cameraController?.Camera;
    }

    private List<StrideVector3> GenerateWorldSpaceCircle(StrideVector3 center, float radius, int segments)
    {
        var points = new List<StrideVector3>();

        for (int i = 0; i < segments; i++)
        {
            float angle = (float)(2.0 * Math.PI * i / segments);
            float x = center.X + radius * MathF.Cos(angle);
            float z = center.Z + radius * MathF.Sin(angle);

            // Get terrain height at this position
            float? height = terrainManager?.GetHeightAtPosition(x, z);
            if (height != null)
            {
                points.Add(new StrideVector3(x, height.Value + 0.1f, z)); // Slight offset to avoid z-fighting
            }
            else
            {
                // Outside terrain, use center height
                points.Add(new StrideVector3(x, center.Y + 0.1f, z));
            }
        }

        return points;
    }

    private NumericsVector2? WorldToScreen(StrideVector3 worldPos, CameraComponent camera, NumericsVector2 viewPos, NumericsVector2 viewSize)
    {
        var viewProj = Matrix.Multiply(camera.ViewMatrix, camera.ProjectionMatrix);
        var clipPos = StrideVector4.Transform(new StrideVector4(worldPos, 1.0f), viewProj);

        // Behind camera
        if (clipPos.W <= 0)
            return null;

        // Perspective divide
        float ndcX = clipPos.X / clipPos.W;
        float ndcY = clipPos.Y / clipPos.W;

        // NDC to screen (Y is inverted in screen space)
        float screenX = (ndcX + 1.0f) * 0.5f * viewSize.X + viewPos.X;
        float screenY = (1.0f - ndcY) * 0.5f * viewSize.Y + viewPos.Y;

        return new NumericsVector2(screenX, screenY);
    }

    private void DrawProjectedCircle(
        ImDrawListPtr drawList,
        List<StrideVector3> worldPoints,
        CameraComponent camera,
        NumericsVector2 viewPos,
        NumericsVector2 viewSize,
        uint color,
        float thickness)
    {
        var screenPoints = new List<NumericsVector2>();

        foreach (var worldPoint in worldPoints)
        {
            var screenPoint = WorldToScreen(worldPoint, camera, viewPos, viewSize);
            if (screenPoint != null)
            {
                screenPoints.Add(screenPoint.Value);
            }
        }

        if (screenPoints.Count < 3)
            return;

        // Push clipping rect to constrain drawing to viewport
        drawList.PushClipRect(viewPos, new NumericsVector2(viewPos.X + viewSize.X, viewPos.Y + viewSize.Y));

        // Draw as polygon outline
        for (int i = 0; i < screenPoints.Count; i++)
        {
            int next = (i + 1) % screenPoints.Count;
            drawList.AddLine(screenPoints[i], screenPoints[next], color, thickness);
        }

        drawList.PopClipRect();
    }

    private void DrawProjectedCircleFilled(
        ImDrawListPtr drawList,
        List<StrideVector3> worldPoints,
        CameraComponent camera,
        NumericsVector2 viewPos,
        NumericsVector2 viewSize,
        uint color)
    {
        var screenPoints = new List<NumericsVector2>();

        foreach (var worldPoint in worldPoints)
        {
            var screenPoint = WorldToScreen(worldPoint, camera, viewPos, viewSize);
            if (screenPoint != null)
            {
                screenPoints.Add(screenPoint.Value);
            }
        }

        if (screenPoints.Count < 3)
            return;

        // Push clipping rect to constrain drawing to viewport
        drawList.PushClipRect(viewPos, new NumericsVector2(viewPos.X + viewSize.X, viewPos.Y + viewSize.Y));

        // Draw as filled polygon - convert to array for ref access
        var screenPointsArray = screenPoints.ToArray();
        drawList.AddConvexPolyFilled(ref screenPointsArray[0], screenPoints.Count, color);

        drawList.PopClipRect();
    }

    private float? GetViewportAspectRatio()
    {
        if (SceneRenderTarget != null && SceneRenderTarget.ViewWidth > 0 && SceneRenderTarget.ViewHeight > 0)
        {
            return SceneRenderTarget.ViewWidth / (float)SceneRenderTarget.ViewHeight;
        }

        float toolbarHeight = GetToolbarHeight();
        float infoHeight = GetInfoBarHeight();
        float fallbackHeight = Math.Max(1.0f, ContentRect.Height - toolbarHeight - infoHeight);
        float fallbackWidth = Math.Max(1.0f, ContentRect.Width);
        return fallbackWidth / fallbackHeight;
    }

    private bool IsMouseInsideViewportRect(NumericsVector2 mousePos)
    {
        float toolbarHeight = GetToolbarHeight();
        float infoHeight = GetInfoBarHeight();
        float viewY = ContentRect.Y + toolbarHeight;
        float viewHeight = Math.Max(0.0f, ContentRect.Height - toolbarHeight - infoHeight);

        return
            mousePos.X >= ContentRect.X &&
            mousePos.X <= ContentRect.X + ContentRect.Width &&
            mousePos.Y >= viewY &&
            mousePos.Y <= viewY + viewHeight;
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

        string mode = cameraController.IsFlyModeActive ? "Fly" : "Orbit";
        string info = $"Mode: {mode} | Y/P: {cameraController.YawDegrees:F0}/{cameraController.PitchDegrees:F0} | Cam: {cameraController.CameraPosition.X:F0}, {cameraController.CameraPosition.Y:F0}, {cameraController.CameraPosition.Z:F0}";
        var textPos = new NumericsVector2(
            infoPos.X + EditorStyle.ScaleValue(8.0f),
            infoPos.Y + (infoHeight - ImGui.CalcTextSize(info).Y) * 0.5f);
        drawList.PushClipRect(infoPos, infoEnd, true);
        drawList.AddText(textPos, ColorPalette.TextSecondary.ToUint(), info);
        drawList.PopClipRect();
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
