#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// 右侧面板 - 包含参数和笔刷两个标签页
/// </summary>
public class RightPanel : PanelBase
{
    private readonly BrushParamsPanel brushParamsPanel;
    private readonly BrushesPanel brushesPanel;

    public event EventHandler<BrushSelectedEventArgs>? BrushSelected;
    public event EventHandler<BrushParamsChangedEventArgs>? BrushParamsChanged;

    public RightPanel()
    {
        Title = "";
        ShowTitleBar = false;
        Padding = Controls.Margin.Zero;

        brushParamsPanel = new BrushParamsPanel();
        brushesPanel = new BrushesPanel();

        brushesPanel.BrushSelected += (s, e) => BrushSelected?.Invoke(this, e);
        brushParamsPanel.ParamsChanged += (s, e) => BrushParamsChanged?.Invoke(this, e);
    }

    protected override void RenderContent()
    {
        ImGui.SetCursorScreenPos(new Vector2(ContentRect.X, ContentRect.Y));

        if (ImGui.BeginChild($"##right_panel_{Id}", new Vector2(ContentRect.Width, ContentRect.Height), ImGuiChildFlags.None))
        {
            // Tab bar
            if (ImGui.BeginTabBar($"##right_tabs_{Id}", ImGuiTabBarFlags.None))
            {
                // Params tab
                if (ImGui.BeginTabItem($"{Icons.Settings} Params"))
                {
                    brushParamsPanel.Render();
                    ImGui.EndTabItem();
                }

                // Brushes tab
                if (ImGui.BeginTabItem($"{Icons.Brush} Brushes"))
                {
                    brushesPanel.Render();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
        ImGui.EndChild();
    }

    public void SetBrushSize(float size) => brushParamsPanel.BrushSize = size;
    public void SetBrushStrength(float strength) => brushParamsPanel.BrushStrength = strength;
    public void SetBrushFalloff(float falloff) => brushParamsPanel.BrushFalloff = falloff;
    public float GetBrushSize() => brushParamsPanel.BrushSize;
    public float GetBrushStrength() => brushParamsPanel.BrushStrength;
    public float GetBrushFalloff() => brushParamsPanel.BrushFalloff;
}

/// <summary>
/// 笔刷参数面板
/// </summary>
internal class BrushParamsPanel
{
    public float BrushSize { get; set; } = 50.0f;
    public float BrushStrength { get; set; } = 0.5f;
    public float BrushFalloff { get; set; } = 0.3f;

    public event EventHandler<BrushParamsChangedEventArgs>? ParamsChanged;

    public void Render()
    {
        ImGui.Spacing();

        // Size slider
        ImGui.Text("Size");
        ImGui.SetNextItemWidth(-1);
        float size = BrushSize;
        if (ImGui.SliderFloat("##brush_size", ref size, 1.0f, 500.0f, "%.0f"))
        {
            BrushSize = size;
            ParamsChanged?.Invoke(this, new BrushParamsChangedEventArgs { Param = "Size", Value = BrushSize });
        }

        ImGui.Spacing();

        // Strength slider
        ImGui.Text("Strength");
        ImGui.SetNextItemWidth(-1);
        float strength = BrushStrength;
        if (ImGui.SliderFloat("##brush_strength", ref strength, 0.0f, 1.0f, "%.2f"))
        {
            BrushStrength = strength;
            ParamsChanged?.Invoke(this, new BrushParamsChangedEventArgs { Param = "Strength", Value = BrushStrength });
        }

        ImGui.Spacing();

        // Falloff slider
        ImGui.Text("Falloff");
        ImGui.SetNextItemWidth(-1);
        float falloff = BrushFalloff;
        if (ImGui.SliderFloat("##brush_falloff", ref falloff, 0.0f, 1.0f, "%.2f"))
        {
            BrushFalloff = falloff;
            ParamsChanged?.Invoke(this, new BrushParamsChangedEventArgs { Param = "Falloff", Value = BrushFalloff });
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Brush preview
        ImGui.Text("Preview");
        RenderBrushPreview();
    }

    private void RenderBrushPreview()
    {
        float previewSize = Math.Min(ImGui.GetContentRegionAvail().X, 100);
        Vector2 cursor = ImGui.GetCursorScreenPos();

        var drawList = ImGui.GetWindowDrawList();

        // Background
        drawList.AddRectFilled(cursor, new Vector2(cursor.X + previewSize, cursor.Y + previewSize), ColorPalette.DarkBackground.ToUint());

        // Brush circle with falloff visualization
        Vector2 center = new Vector2(cursor.X + previewSize * 0.5f, cursor.Y + previewSize * 0.5f);
        float radius = previewSize * 0.4f * (BrushSize / 500.0f + 0.1f);

        // Outer circle (full strength area)
        drawList.AddCircleFilled(center, radius, ColorPalette.Accent.WithAlpha(0.3f).ToUint());

        // Inner circle (falloff area)
        float innerRadius = radius * (1.0f - BrushFalloff);
        drawList.AddCircleFilled(center, innerRadius, ColorPalette.Accent.WithAlpha(0.6f).ToUint());

        // Border
        drawList.AddCircle(center, radius, ColorPalette.Border.ToUint());

        ImGui.Dummy(new Vector2(previewSize, previewSize));
    }
}

/// <summary>
/// 笔刷形状面板
/// </summary>
internal class BrushesPanel
{
    public int SelectedBrush { get; set; } = 0;

    public event EventHandler<BrushSelectedEventArgs>? BrushSelected;

    private static readonly string[] brushNames = { "Circle", "Square", "Smooth", "Noise" };
    private static readonly string[] brushIcons = { Icons.Circle, Icons.Square, Icons.Water, Icons.Noise };

    public void Render()
    {
        ImGui.Spacing();

        float itemWidth = EditorStyle.ScaleValue(60.0f);
        float padding = EditorStyle.ScaleValue(8.0f);
        float labelHeight = ImGui.GetTextLineHeight() + EditorStyle.ScaleValue(8.0f);
        float itemHeight = itemWidth + labelHeight;
        float availableWidth = ImGui.GetContentRegionAvail().X;
        int itemsPerRow = Math.Max(1, (int)((availableWidth + padding) / (itemWidth + padding)));

        for (int i = 0; i < brushNames.Length; i++)
        {
            int col = i % itemsPerRow;
            if (col > 0)
                ImGui.SameLine(0.0f, padding);

            RenderBrushItem(i, itemWidth, itemHeight, labelHeight);
        }
    }

    private void RenderBrushItem(int index, float width, float height, float labelHeight)
    {
        bool isSelected = SelectedBrush == index;

        var drawList = ImGui.GetWindowDrawList();
        Vector2 cursor = ImGui.GetCursorScreenPos();
        float inset = EditorStyle.ScaleValue(4.0f);

        // Button area
        ImGui.InvisibleButton($"##brush_{index}", new Vector2(width, height));

        bool isHovered = ImGui.IsItemHovered();

        // Background
        uint bgColor = isSelected ? ColorPalette.Selection.ToUint() :
                       isHovered ? ColorPalette.Hover.ToUint() :
                       ColorPalette.DarkBackground.ToUint();
        drawList.AddRectFilled(cursor, new Vector2(cursor.X + width, cursor.Y + height), bgColor, 4.0f);

        // Border
        if (isSelected)
        {
            drawList.AddRect(cursor, new Vector2(cursor.X + width, cursor.Y + height), ColorPalette.Accent.ToUint(), 4.0f);
        }

        // Icon
        Vector2 iconPos = new Vector2(
            cursor.X + (width - FontManager.ScaledIconSize) * 0.5f,
            cursor.Y + (width - FontManager.ScaledIconSize) * 0.5f
        );

        uint iconColor = isSelected ? ColorPalette.Accent.ToUint() : ColorPalette.TextPrimary.ToUint();
        ImGui.AddText(drawList, FontManager.Icons, FontManager.ScaledIconSize, iconPos, iconColor, brushIcons[index]);

        // Name
        string displayName = TruncateToWidth(brushNames[index], width - inset * 2.0f);
        Vector2 textSize = ImGui.CalcTextSize(displayName);
        Vector2 namePos = new Vector2(
            cursor.X + (width - textSize.X) * 0.5f,
            cursor.Y + width + MathF.Max(0.0f, (labelHeight - textSize.Y) * 0.5f)
        );
        drawList.AddText(namePos, ColorPalette.TextSecondary.ToUint(), displayName);

        // Handle click
        if (ImGui.IsItemClicked())
        {
            SelectedBrush = index;
            BrushSelected?.Invoke(this, new BrushSelectedEventArgs { BrushIndex = index, BrushName = brushNames[index] });
        }
    }

    private static string TruncateToWidth(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        const string ellipsis = "...";
        float ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;
        if (ellipsisWidth >= maxWidth)
            return ellipsis;

        for (int length = text.Length - 1; length > 0; length--)
        {
            string candidate = text[..length] + ellipsis;
            if (ImGui.CalcTextSize(candidate).X <= maxWidth)
                return candidate;
        }

        return ellipsis;
    }
}

public class BrushSelectedEventArgs : EventArgs
{
    public int BrushIndex { get; set; }
    public string BrushName { get; set; } = "";
}

public class BrushParamsChangedEventArgs : EventArgs
{
    public string Param { get; set; } = "";
    public float Value { get; set; }
}
