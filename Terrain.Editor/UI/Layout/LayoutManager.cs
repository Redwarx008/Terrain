#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Controls;
using Terrain.Editor.UI.Panels;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Layout;

public class LayoutManager : ControlBase
{
    public Vector2 WindowSize { get; set; }
    public float ToolbarHeight { get; set; } = 36.0f;
    public float TopInset { get; set; } = 0.0f;
    public float LeftPanelRatio { get; set; } = 0.15f;
    public float RightPanelRatio { get; set; } = 0.18f;
    public float BottomPanelRatio { get; set; } = 0.22f;
    public float MinPanelWidth { get; set; } = 180.0f;
    public float MaxPanelWidth { get; set; } = 400.0f;
    public float MinPanelHeight { get; set; } = 100.0f;
    public float MaxPanelHeight { get; set; } = 400.0f;
    public float SplitterThickness { get; set; } = 4.0f;
    public float SplitterHitPadding { get; set; } = 3.0f;

    public PanelBase? LeftPanel { get; set; }
    public PanelBase? RightPanel { get; set; }
    public PanelBase? CenterPanel { get; set; }
    public PanelBase? BottomPanel { get; set; }
    public PanelBase? TopPanel { get; set; }

    private bool isDraggingLeftSplitter;
    private bool isDraggingRightSplitter;
    private bool isDraggingBottomSplitter;
    private bool isHoveringLeftSplitter;
    private bool isHoveringRightSplitter;
    private bool isHoveringBottomSplitter;
    private bool wasPrimaryMouseDown;

    private static bool IsPanelVisible(PanelBase? panel)
    {
        return panel != null && panel.IsVisible && !panel.IsClosed;
    }

    public void CalculateLayout()
    {
        float availableWidth = WindowSize.X;
        float topHeight = TopInset + ToolbarHeight;
        float availableHeight = Math.Max(0.0f, WindowSize.Y - topHeight);

        LeftPanelRatio = Math.Clamp(LeftPanelRatio, 0.1f, 0.4f);
        RightPanelRatio = Math.Clamp(RightPanelRatio, 0.1f, 0.4f);
        BottomPanelRatio = Math.Clamp(BottomPanelRatio, 0.1f, 0.5f);

        float totalSideRatio = LeftPanelRatio + RightPanelRatio;
        if (totalSideRatio > 0.7f)
        {
            float scale = 0.7f / totalSideRatio;
            LeftPanelRatio *= scale;
            RightPanelRatio *= scale;
        }

        bool showLeftPanel = IsPanelVisible(LeftPanel);
        bool showRightPanel = IsPanelVisible(RightPanel);
        bool showBottomPanel = IsPanelVisible(BottomPanel);

        float leftWidth = showLeftPanel ? Math.Clamp(availableWidth * LeftPanelRatio, MinPanelWidth, MaxPanelWidth) : 0.0f;
        float rightWidth = showRightPanel ? Math.Clamp(availableWidth * RightPanelRatio, MinPanelWidth, MaxPanelWidth) : 0.0f;
        float bottomHeight = showBottomPanel ? Math.Clamp(availableHeight * BottomPanelRatio, MinPanelHeight, MaxPanelHeight) : 0.0f;

        float horizontalSplitterWidth =
            (showLeftPanel ? SplitterThickness : 0.0f) +
            (showRightPanel ? SplitterThickness : 0.0f);
        float centerWidth = Math.Max(0.0f, availableWidth - leftWidth - rightWidth - horizontalSplitterWidth);
        float centerHeight = Math.Max(0.0f, availableHeight - bottomHeight - (showBottomPanel ? SplitterThickness : 0.0f));

        float contentTop = topHeight;

        TopPanel?.Arrange(new Vector2(0, TopInset), new Vector2(availableWidth, ToolbarHeight));

        if (showLeftPanel)
        {
            LeftPanel!.Arrange(new Vector2(0, contentTop), new Vector2(leftWidth, centerHeight));
        }

        if (showRightPanel)
        {
            RightPanel!.Arrange(new Vector2(availableWidth - rightWidth, contentTop), new Vector2(rightWidth, centerHeight));
        }

        float centerX = showLeftPanel ? leftWidth + SplitterThickness : 0.0f;
        CenterPanel?.Arrange(new Vector2(centerX, contentTop), new Vector2(centerWidth, centerHeight));

        if (showBottomPanel)
        {
            BottomPanel!.Arrange(
                new Vector2(0, contentTop + centerHeight + SplitterThickness),
                new Vector2(availableWidth, bottomHeight)
            );
        }
    }

    protected override void OnRender()
    {
        CalculateLayout();

        var io = ImGui.GetIO();
        UpdateSplitterHoverState(io.MousePos);
        UpdateSplitterDragState(io);

        TopPanel?.Render();
        LeftPanel?.Render();
        RightPanel?.Render();
        CenterPanel?.Render();
        BottomPanel?.Render();

        RenderSplitters();
        ApplySplitterCursor();
        HandleSplitterDrag();
    }

    private void RenderSplitters()
    {
        var drawList = ImGui.GetWindowDrawList();

        if (IsPanelVisible(LeftPanel) && IsPanelVisible(CenterPanel))
        {
            float x = LeftPanel!.Position.X + LeftPanel.Size.X;
            float y = CenterPanel!.Position.Y;
            float height = CenterPanel.Size.Y;
            RenderSplitter(drawList, new Vector2(x, y), new Vector2(SplitterThickness, height), true, isDraggingLeftSplitter || isHoveringLeftSplitter);
        }

        if (IsPanelVisible(RightPanel) && IsPanelVisible(CenterPanel))
        {
            float x = RightPanel!.Position.X - SplitterThickness;
            float y = CenterPanel!.Position.Y;
            float height = CenterPanel.Size.Y;
            RenderSplitter(drawList, new Vector2(x, y), new Vector2(SplitterThickness, height), true, isDraggingRightSplitter || isHoveringRightSplitter);
        }

        if (IsPanelVisible(BottomPanel))
        {
            float x = BottomPanel!.Position.X;
            float y = BottomPanel.Position.Y - SplitterThickness;
            float width = BottomPanel.Size.X;
            RenderSplitter(drawList, new Vector2(x, y), new Vector2(width, SplitterThickness), false, isDraggingBottomSplitter || isHoveringBottomSplitter);
        }
    }

    private void RenderSplitter(ImDrawListPtr drawList, Vector2 position, Vector2 size, bool isVertical, bool isActive)
    {
        uint bgColor = isActive
            ? ColorPalette.Accent.ToUint()
            : ColorPalette.Border.ToUint();

        drawList.AddRectFilled(position, new Vector2(position.X + size.X, position.Y + size.Y), bgColor);

        Vector2 center = new Vector2(position.X + size.X * 0.5f, position.Y + size.Y * 0.5f);
        uint handleColor = ColorPalette.TextSecondary.ToUint();

        if (isVertical)
        {
            for (int i = -2; i <= 2; i++)
            {
                drawList.AddCircleFilled(new Vector2(center.X, center.Y + i * 4), 1.5f, handleColor);
            }
        }
        else
        {
            for (int i = -2; i <= 2; i++)
            {
                drawList.AddCircleFilled(new Vector2(center.X + i * 4, center.Y), 1.5f, handleColor);
            }
        }
    }

    protected override bool OnHandleInput(InputEvent evt)
    {
        return false;
    }

    private void HandleSplitterDrag()
    {
        if (!isDraggingLeftSplitter && !isDraggingRightSplitter && !isDraggingBottomSplitter)
        {
            return;
        }

        var io = ImGui.GetIO();
        if (!io.MouseDown[0])
        {
            isDraggingLeftSplitter = false;
            isDraggingRightSplitter = false;
            isDraggingBottomSplitter = false;
            wasPrimaryMouseDown = false;
            return;
        }

        float availableWidth = WindowSize.X;
        float availableHeight = Math.Max(0.0f, WindowSize.Y - (TopInset + ToolbarHeight));

        if (isDraggingLeftSplitter && LeftPanel != null)
        {
            float newWidth = Math.Clamp(LeftPanel.Size.X + io.MouseDelta.X, MinPanelWidth, MaxPanelWidth);
            LeftPanelRatio = newWidth / availableWidth;
        }

        if (isDraggingRightSplitter && RightPanel != null)
        {
            float newWidth = Math.Clamp(RightPanel.Size.X - io.MouseDelta.X, MinPanelWidth, MaxPanelWidth);
            RightPanelRatio = newWidth / availableWidth;
        }

        if (isDraggingBottomSplitter && BottomPanel != null)
        {
            float newHeight = Math.Clamp(BottomPanel.Size.Y - io.MouseDelta.Y, MinPanelHeight, MaxPanelHeight);
            BottomPanelRatio = newHeight / availableHeight;
        }
    }

    private bool IsOverSplitter(Vector2 mousePos, float x, float y, bool isVertical)
    {
        if (isVertical)
        {
            float height = CenterPanel?.Size.Y ?? 0.0f;
            return mousePos.X >= x - SplitterHitPadding && mousePos.X <= x + SplitterThickness + SplitterHitPadding &&
                   mousePos.Y >= y && mousePos.Y <= y + height;
        }

        float rightBound = BottomPanel != null ? x + BottomPanel.Size.X : WindowSize.X;
        return mousePos.X >= x && mousePos.X <= rightBound &&
               mousePos.Y >= y - SplitterHitPadding && mousePos.Y <= y + SplitterThickness + SplitterHitPadding;
    }

    private void UpdateSplitterHoverState(Vector2 mousePos)
    {
        isHoveringLeftSplitter = IsPanelVisible(LeftPanel) && IsPanelVisible(CenterPanel) &&
                                 IsOverSplitter(mousePos, LeftPanel!.Position.X + LeftPanel.Size.X, CenterPanel!.Position.Y, true);
        isHoveringRightSplitter = IsPanelVisible(RightPanel) && IsPanelVisible(CenterPanel) &&
                                  IsOverSplitter(mousePos, RightPanel!.Position.X - SplitterThickness, CenterPanel!.Position.Y, true);
        isHoveringBottomSplitter = IsPanelVisible(BottomPanel) &&
                                   IsOverSplitter(mousePos, BottomPanel!.Position.X, BottomPanel.Position.Y - SplitterThickness, false);
    }

    private void ApplySplitterCursor()
    {
        if (isDraggingLeftSplitter || isDraggingRightSplitter || isHoveringLeftSplitter || isHoveringRightSplitter)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        }
        else if (isDraggingBottomSplitter || isHoveringBottomSplitter)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
        }
    }

    private void UpdateSplitterDragState(ImGuiIOPtr io)
    {
        bool isPrimaryMouseDown = io.MouseDown[0];
        bool mousePressedThisFrame = isPrimaryMouseDown && !wasPrimaryMouseDown;

        if (mousePressedThisFrame)
        {
            isDraggingLeftSplitter = isHoveringLeftSplitter;
            isDraggingRightSplitter = !isDraggingLeftSplitter && isHoveringRightSplitter;
            isDraggingBottomSplitter = !isDraggingLeftSplitter && !isDraggingRightSplitter && isHoveringBottomSplitter;
        }
        else if (!isPrimaryMouseDown)
        {
            isDraggingLeftSplitter = false;
            isDraggingRightSplitter = false;
            isDraggingBottomSplitter = false;
        }

        wasPrimaryMouseDown = isPrimaryMouseDown;
    }

    public void ResetLayout()
    {
        LeftPanelRatio = 0.15f;
        RightPanelRatio = 0.18f;
        BottomPanelRatio = 0.22f;
        CalculateLayout();
    }

    public void ToggleLeftPanel()
    {
        if (LeftPanel != null)
        {
            LeftPanel.IsVisible = !LeftPanel.IsVisible;
            CalculateLayout();
        }
    }

    public void ToggleRightPanel()
    {
        if (RightPanel != null)
        {
            RightPanel.IsVisible = !RightPanel.IsVisible;
            CalculateLayout();
        }
    }

    public void ToggleBottomPanel()
    {
        if (BottomPanel != null)
        {
            BottomPanel.IsVisible = !BottomPanel.IsVisible;
            CalculateLayout();
        }
    }
}
