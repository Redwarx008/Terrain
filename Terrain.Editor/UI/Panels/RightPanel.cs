#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.Services;
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
    private readonly BrushParameters _brushParams = BrushParameters.Instance;

    public float BrushSize { get => _brushParams.Size; set => _brushParams.Size = value; }
    public float BrushStrength { get => _brushParams.Strength; set => _brushParams.Strength = value; }
    public float BrushFalloff { get => _brushParams.Falloff; set => _brushParams.Falloff = value; }

    public event EventHandler<BrushParamsChangedEventArgs>? ParamsChanged;

    public void Render()
    {
        ImGui.Spacing();

        // Size slider
        ImGui.Text("Size");
        ImGui.SetNextItemWidth(-1);
        float size = BrushSize;
        if (ImGui.SliderFloat("##brush_size", ref size, 1.0f, 200.0f, "%.0f"))
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

        // Per D-11: Show Hard/Soft labels for inverted falloff semantics
        ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "Soft <---> Hard");

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
        float radius = previewSize * 0.4f * (BrushSize / 200.0f + 0.1f);

        // Outer circle (full strength area)
        drawList.AddCircleFilled(center, radius, ColorPalette.Accent.WithAlpha(0.3f).ToUint());

        // Inner circle (falloff area) - use EffectiveFalloff for correct inverted semantics
        float innerRadius = radius * _brushParams.EffectiveFalloff;
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
    private readonly BrushParameters _brushParams = BrushParameters.Instance;

    public int SelectedBrush
    {
        get => _brushParams.SelectedBrushIndex;
        set => _brushParams.SelectedBrushIndex = value;
    }

    public event EventHandler<BrushSelectedEventArgs>? BrushSelected;

    private static readonly string[] brushNames = { "Circle", "Square", "Smooth", "Noise" };
    private static readonly string[] brushIcons = { Icons.Circle, Icons.Square, Icons.Water, Icons.Noise };

    public void Render()
    {
        ImGui.Spacing();

        GridTileLayout tileLayout = GridTileRenderer.CreateLayout(60.0f, 8.0f);
        float availableWidth = ImGui.GetContentRegionAvail().X;
        int itemsPerRow = GridTileRenderer.GetItemsPerRow(availableWidth, tileLayout);

        for (int i = 0; i < brushNames.Length; i++)
        {
            GridTileRenderer.AdvanceRowLayout(i, itemsPerRow, tileLayout);

            // Only Circle (index 0) is enabled in Phase 2
            bool isEnabled = (i == 0);
            RenderBrushItem(i, tileLayout, isEnabled);
        }
    }

    private void RenderBrushItem(int index, GridTileLayout tileLayout, bool isEnabled = true)
    {
        bool isSelected = SelectedBrush == index;

        if (!isEnabled)
        {
            EditorStyle.PushDisabled();
        }

        GridTileContext tile = GridTileRenderer.BeginTile($"##brush_{index}", tileLayout, isSelected && isEnabled);
        Vector2 iconCenter = GridTileRenderer.GetSquareContentCenter(tile.Cursor, tileLayout);

        // Icon
        Vector2 iconPos = new Vector2(
            iconCenter.X - FontManager.ScaledIconSize * 0.5f,
            iconCenter.Y - FontManager.ScaledIconSize * 0.5f
        );

        uint iconColor = isSelected && isEnabled ? ColorPalette.Accent.ToUint() : ColorPalette.TextPrimary.ToUint();
        ImGui.AddText(tile.DrawList, FontManager.Icons, FontManager.ScaledIconSize, iconPos, iconColor, brushIcons[index]);

        GridTileRenderer.DrawLabel(tile.DrawList, tile.Cursor, tileLayout, brushNames[index], centered: true);

        // Handle click only for enabled brushes
        if (isEnabled && ImGui.IsItemClicked())
        {
            SelectedBrush = index;
            BrushSelected?.Invoke(this, new BrushSelectedEventArgs { BrushIndex = index, BrushName = brushNames[index] });
        }

        // Add tooltip for disabled brushes
        if (!isEnabled && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Coming in Phase 5");
        }

        if (!isEnabled)
        {
            EditorStyle.PopDisabled();
        }
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
