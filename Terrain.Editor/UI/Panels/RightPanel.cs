#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.Services;
using Terrain.Editor.UI.Controls;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// 右侧面板 - 包含参数、笔刷和纹理属性标签页
/// </summary>
public class RightPanel : PanelBase
{
    private const string BrushTabId = "brush";
    private const string TextureTabId = "texture";

    private readonly BrushParamsPanel brushParamsPanel;
    private readonly BrushesPanel brushesPanel;
    private readonly TextureInspectorPanel textureInspectorPanel;

    // Centralized tab state (visibility/activation/close) for this panel.
    private readonly TabController tabs = new();

    public event EventHandler<BrushSelectedEventArgs>? BrushSelected;
    public event EventHandler<BrushParamsChangedEventArgs>? BrushParamsChanged;
    public event EventHandler<TextureImportEventArgs>? ImportNormalRequested;
    public event EventHandler<TextureSlotEventArgs>? ClearNormalRequested;

    /// <summary>
    /// 当前编辑模式。
    /// </summary>
    public EditorMode CurrentMode { get; set; } = EditorMode.Sculpt;

    /// <summary>
    /// 选中的纹理槽位索引（Paint 模式）。
    /// </summary>
    public int SelectedTextureSlot
    {
        get => textureInspectorPanel.SelectedSlotIndex;
        set => textureInspectorPanel.SelectedSlotIndex = value;
    }

    /// <summary>工具被选中时调用 - 显示 Brush 标签</summary>
    public void OnToolSelected()
    {
        // Key behavior: any tool click should show Brush and activate it immediately.
        tabs.SetVisible(BrushTabId, true);
        tabs.RequestActivate(BrushTabId);
    }

    /// <summary>工具取消选择时调用 - 隐藏 Brush 标签</summary>
    public void OnToolDeselected()
    {
        tabs.SetVisible(BrushTabId, false);
        tabs.ClearActive(BrushTabId);
    }

    /// <summary>纹理被选中时调用 - 显示 Texture 标签</summary>
    public void OnTextureSelected()
    {
        tabs.SetVisible(TextureTabId, true);
    }

    /// <summary>纹理取消选择时调用 - 关闭 Texture 标签</summary>
    public void OnTextureDeselected()
    {
        tabs.SetVisible(TextureTabId, false);
        tabs.ClearActive(TextureTabId);
    }

    public RightPanel()
    {
        Title = "";
        ShowTitleBar = false;

        brushParamsPanel = new BrushParamsPanel();
        brushesPanel = new BrushesPanel();
        textureInspectorPanel = new TextureInspectorPanel();
        tabs.Register(new TabItemState(BrushTabId, "Brush", Icons.Brush) { IsVisible = false });
        tabs.Register(new TabItemState(TextureTabId, "Texture", Icons.Image) { IsVisible = false });

        brushesPanel.BrushSelected += (s, e) => BrushSelected?.Invoke(this, e);
        brushParamsPanel.ParamsChanged += (s, e) => BrushParamsChanged?.Invoke(this, e);
        textureInspectorPanel.ImportNormalRequested += (s, e) => ImportNormalRequested?.Invoke(this, e);
        textureInspectorPanel.ClearNormalRequested += (s, e) => ClearNormalRequested?.Invoke(this, e);
    }

    protected override void RenderContent()
    {
        // 同步当前模式到子面板
        brushParamsPanel.CurrentMode = CurrentMode;

        ImGui.SetCursorScreenPos(new Vector2(ContentRect.X, ContentRect.Y));

        if (ImGui.BeginChild($"##right_panel_{Id}", new Vector2(ContentRect.Width, ContentRect.Height), ImGuiChildFlags.None))
        {
            // Tab bar
            if (ImGui.BeginTabBar($"##right_tabs_{Id}", ImGuiTabBarFlags.None))
            {
                // Brush tab - 合并了 Brush 选择和 Params 参数
                var brushTab = tabs.GetRequired(BrushTabId);
                if (brushTab.IsVisible)
                {
                    bool brushOpen = !brushTab.IsClosed;
                    ImGuiTabItemFlags brushFlags = ConsumeSelectionRequest(brushTab);

                    if (ImGui.BeginTabItem($"{brushTab.Icon} {brushTab.Title}###{brushTab.Id}", ref brushOpen, brushFlags))
                    {
                        tabs.SetActive(brushTab.Id);
                        brushesPanel.Render();
                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();
                        brushParamsPanel.Render();
                        ImGui.EndTabItem();
                    }

                    // Keep tab state in one place after ImGui close interactions.
                    ApplyOpenState(brushTab, brushOpen);
                }

                // Texture tab - 仅在 Paint 模式且可见时显示
                var textureTab = tabs.GetRequired(TextureTabId);
                if (CurrentMode == EditorMode.Paint && textureTab.IsVisible)
                {
                    bool textureOpen = !textureTab.IsClosed;
                    ImGuiTabItemFlags textureFlags = ConsumeSelectionRequest(textureTab);

                    if (ImGui.BeginTabItem($"{textureTab.Icon} {textureTab.Title}###{textureTab.Id}", ref textureOpen, textureFlags))
                    {
                        tabs.SetActive(textureTab.Id);
                        textureInspectorPanel.Render();
                        ImGui.EndTabItem();
                    }

                    ApplyOpenState(textureTab, textureOpen);
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

    private static ImGuiTabItemFlags ConsumeSelectionRequest(TabItemState tab)
    {
        // Key behavior: consume once so SetSelected affects only next frame.
        if (!tab.RequestActivate)
            return ImGuiTabItemFlags.None;

        tab.RequestActivate = false;
        return ImGuiTabItemFlags.SetSelected;
    }

    private void ApplyOpenState(TabItemState tab, bool isOpen)
    {
        if (isOpen)
        {
            tab.IsClosed = false;
            return;
        }

        tab.IsClosed = true;
        tab.IsVisible = false;
        tabs.ClearActive(tab.Id);
    }
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

    /// <summary>
    /// 当前编辑模式，由外部设置。
    /// </summary>
    public EditorMode CurrentMode { get; set; } = EditorMode.Sculpt;

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

        // === 材质绘制参数 (Paint 模式) ===
        RenderMaterialPaintParams();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Brush preview
        ImGui.Text("Preview");
        RenderBrushPreview();
    }

    /// <summary>
    /// 渲染材质绘制参数 (仅在 Paint 模式显示)。
    /// </summary>
    private void RenderMaterialPaintParams()
    {
        // 检查当前是否为 Paint 模式
        if (CurrentMode != EditorMode.Paint)
            return;

        ImGui.Text("Material Settings");
        ImGui.Spacing();

        // Weight slider
        ImGui.Text("Weight");
        ImGui.SetNextItemWidth(-1);
        float weight = _brushParams.Weight;
        if (ImGui.SliderFloat("##material_weight", ref weight, 0.0f, 1.0f, "%.2f"))
        {
            _brushParams.Weight = weight;
            ParamsChanged?.Invoke(this, new BrushParamsChangedEventArgs { Param = "Weight", Value = weight });
        }
        ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "Edge <---> Center");

        ImGui.Spacing();

        // Random Rotation toggle
        bool randomRotation = _brushParams.RandomRotation;
        if (ImGui.Checkbox("Random Rotation", ref randomRotation))
        {
            _brushParams.RandomRotation = randomRotation;
            ParamsChanged?.Invoke(this, new BrushParamsChangedEventArgs { Param = "RandomRotation", Value = randomRotation ? 1.0f : 0.0f });
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Break texture tiling pattern with random rotation");
        }

        // Fixed Rotation (only when random rotation is disabled)
        if (!randomRotation)
        {
            ImGui.Indent();
            ImGui.Text("Fixed Angle");
            ImGui.SetNextItemWidth(-1);
            float fixedAngle = _brushParams.FixedRotationDegrees;
            if (ImGui.SliderFloat("##fixed_rotation", ref fixedAngle, 0.0f, 360.0f, "%.0f°"))
            {
                _brushParams.FixedRotationDegrees = fixedAngle;
                ParamsChanged?.Invoke(this, new BrushParamsChangedEventArgs { Param = "FixedRotation", Value = fixedAngle });
            }
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // 3D Projection toggle
        bool use3DProjection = _brushParams.Use3DProjection;
        if (ImGui.Checkbox("3D Projection", ref use3DProjection))
        {
            _brushParams.Use3DProjection = use3DProjection;
            ParamsChanged?.Invoke(this, new BrushParamsChangedEventArgs { Param = "Use3DProjection", Value = use3DProjection ? 1.0f : 0.0f });
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Fix texture stretching on steep cliff faces");
        }
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
