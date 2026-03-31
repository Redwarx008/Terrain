#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;
using Terrain.Editor.Services;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// 工具面板 - 显示当前模式的工具列表
/// </summary>
public class ToolsPanel : PanelBase
{
    #region 属性

    public EditorMode CurrentMode { get; set; } = EditorMode.Sculpt;

    public string? SelectedTool { get; set; }

    public List<ToolItem> Tools { get; } = new();

    #endregion

    #region 事件

    public event EventHandler<ToolSelectedEventArgs>? ToolSelected;

    #endregion

    public ToolsPanel()
    {
        Title = "Tools";
        Icon = Icons.Tools;
        ShowTitleBar = true;
        TitleBarHeight = 26.0f;

        InitializeTools();

        // D-18, D-19: Wire to EditorState for tool selection
        EditorState.Instance.ToolChanged += OnEditorToolChanged;
        SelectedTool = EditorState.Instance.CurrentTool.ToString();
    }

    private void InitializeTools()
    {
        // Sculpt tools
        Tools.Add(new ToolItem { Name = "Raise", Icon = Icons.ArrowUp, Mode = EditorMode.Sculpt, Description = "Raise terrain height" });
        Tools.Add(new ToolItem { Name = "Lower", Icon = Icons.ArrowDown, Mode = EditorMode.Sculpt, Description = "Lower terrain height" });
        Tools.Add(new ToolItem { Name = "Smooth", Icon = Icons.Water, Mode = EditorMode.Sculpt, Description = "Smooth terrain" });
        Tools.Add(new ToolItem { Name = "Flatten", Icon = Icons.Layer, Mode = EditorMode.Sculpt, Description = "Flatten terrain to target height" });

        // Paint tools
        Tools.Add(new ToolItem { Name = "Paint", Icon = Icons.Brush, Mode = EditorMode.Paint, Description = "Paint texture on terrain" });
        Tools.Add(new ToolItem { Name = "Erase", Icon = Icons.Eraser, Mode = EditorMode.Paint, Description = "Erase texture from terrain" });

        // Foliage tools
        Tools.Add(new ToolItem { Name = "Place", Icon = Icons.Plus, Mode = EditorMode.Foliage, Description = "Place foliage" });
        Tools.Add(new ToolItem { Name = "Remove", Icon = Icons.Trash, Mode = EditorMode.Foliage, Description = "Remove foliage" });
    }

    protected override void RenderContent()
    {
        float itemHeight = EditorStyle.ScaleValue(32.0f);
        float padding = EditorStyle.ScaleValue(4.0f);
        float y = ContentRect.Y + padding;

        // Filter tools by current mode
        foreach (var tool in Tools)
        {
            if (tool.Mode != CurrentMode)
                continue;

            RenderToolItem(tool, ContentRect.X + padding, y, ContentRect.Width - padding * 2, itemHeight);
            y += itemHeight + padding;
        }
    }

    private void RenderToolItem(ToolItem tool, float x, float y, float width, float height)
    {
        var drawList = ImGui.GetWindowDrawList();
        bool isSelected = SelectedTool == tool.Name;
        bool isHovered = false;

        // Check hover
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        ImGui.InvisibleButton($"##tool_{tool.Name}", new Vector2(width, height));
        isHovered = ImGui.IsItemHovered();

        // Background
        uint bgColor;
        if (isSelected)
        {
            bgColor = ColorPalette.Selection.ToUint();
        }
        else if (isHovered)
        {
            bgColor = ColorPalette.Hover.ToUint();
        }
        else
        {
            bgColor = ColorPalette.PanelBackground.ToUint();
        }

        drawList.AddRectFilled(new Vector2(x, y), new Vector2(x + width, y + height), bgColor);

        // Icon
        float iconSize = EditorStyle.ScaleValue(20.0f);
        float iconX = x + EditorStyle.ScaleValue(8.0f);
        float iconY = y + (height - iconSize) * 0.5f;

        FontManager.PushIcons();
        Vector2 textSize = ImGui.CalcTextSize(tool.Icon);
        FontManager.PopFont();

        ImGui.SetCursorScreenPos(new Vector2(iconX, iconY));
        FontManager.PushIcons();
        ImGui.TextColored(isSelected ? ColorPalette.Accent.ToVector4() : ColorPalette.TextPrimary.ToVector4(), tool.Icon);
        FontManager.PopFont();

        // Name
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(iconX + iconSize + EditorStyle.ScaleValue(8.0f), y + (height - ImGui.CalcTextSize(tool.Name).Y) * 0.5f));
        ImGui.TextColored(isSelected ? ColorPalette.TextPrimary.ToVector4() : ColorPalette.TextSecondary.ToVector4(), tool.Name);

        // Tooltip
        if (isHovered && !string.IsNullOrEmpty(tool.Description))
        {
            ImGui.SetTooltip(tool.Description);
        }

        // Handle click
        if (ImGui.IsItemClicked())
        {
            SelectedTool = tool.Name;
            // D-18: Update EditorState when tool is selected
            if (Enum.TryParse<HeightTool>(tool.Name, out var heightTool))
            {
                EditorState.Instance.CurrentTool = heightTool;
            }
            ToolSelected?.Invoke(this, new ToolSelectedEventArgs { Tool = tool });
        }
    }

    public void SetMode(EditorMode mode)
    {
        CurrentMode = mode;
        // Select first tool of the mode
        var firstTool = Tools.Find(t => t.Mode == mode);
        if (firstTool != null)
        {
            SelectedTool = firstTool.Name;
        }
    }

    /// <summary>
    /// Handles tool change events from EditorState.
    /// Per D-19: Current tool state stored in EditorState service.
    /// </summary>
    private void OnEditorToolChanged(object? sender, EventArgs e)
    {
        SelectedTool = EditorState.Instance.CurrentTool.ToString();
    }

    public override void Dispose()
    {
        EditorState.Instance.ToolChanged -= OnEditorToolChanged;
        base.Dispose();
    }
}

public class ToolItem
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = Icons.Cube;
    public EditorMode Mode { get; set; }
    public string? Description { get; set; }
}

public class ToolSelectedEventArgs : EventArgs
{
    public ToolItem Tool { get; set; } = null!;
}
