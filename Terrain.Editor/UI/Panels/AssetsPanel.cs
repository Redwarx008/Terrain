#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// 资源面板 - 根据编辑模式显示不同的内容
/// - Sculpt: Height layers
/// - Paint: Texture slots (256 slots)
/// - Foliage: Foliage prefabs
/// </summary>
public class AssetsPanel : PanelBase
{
    #region 属性

    public EditorMode CurrentMode { get; set; } = EditorMode.Sculpt;

    /// <summary>
    /// 选中的纹理槽索引 (Paint mode)
    /// </summary>
    public int SelectedTextureSlot { get; set; } = 0;

    /// <summary>
    /// 选中的植被索引 (Foliage mode)
    /// </summary>
    public int SelectedFoliageIndex { get; set; } = 0;

    /// <summary>
    /// 选中的图层索引 (Sculpt mode)
    /// </summary>
    public int SelectedLayerIndex { get; set; } = 0;

    #endregion

    #region 事件

    public event EventHandler<TextureSlotSelectedEventArgs>? TextureSlotSelected;
    public event EventHandler<FoliageSelectedEventArgs>? FoliageSelected;
    public event EventHandler<LayerSelectedEventArgs>? LayerSelected;

    #endregion

    #region 私有字段

    private List<TextureSlot> textureSlots = new();
    private List<FoliageItem> foliageItems = new();
    private List<HeightLayer> heightLayers = new();

    #endregion

    public AssetsPanel()
    {
        Title = "Assets";
        Icon = Icons.Folder;
        ShowTitleBar = true;

        InitializeTextureSlots();
        InitializeFoliageItems();
        InitializeHeightLayers();
    }

    private void InitializeTextureSlots()
    {
        for (int i = 0; i < 256; i++)
        {
            textureSlots.Add(new TextureSlot
            {
                Index = i,
                Name = i == 0 ? "Grass" : i == 1 ? "Dirt" : i == 2 ? "Rock" : i == 3 ? "Snow" : $"Texture {i}",
                IsEmpty = i >= 4
            });
        }
    }

    private void InitializeFoliageItems()
    {
        foliageItems.Add(new FoliageItem { Name = "Pine Tree", Icon = Icons.Tree });
        foliageItems.Add(new FoliageItem { Name = "Bush", Icon = Icons.Tree });
        foliageItems.Add(new FoliageItem { Name = "Rock", Icon = Icons.Cube });
        foliageItems.Add(new FoliageItem { Name = "Grass", Icon = Icons.Plus });
    }

    private void InitializeHeightLayers()
    {
        heightLayers.Add(new HeightLayer { Name = "Base Layer", IsVisible = true, IsLocked = false });
        heightLayers.Add(new HeightLayer { Name = "Mountain Layer", IsVisible = true, IsLocked = false });
    }

    protected override void RenderContent()
    {
        switch (CurrentMode)
        {
            case EditorMode.Sculpt:
                RenderHeightLayers();
                break;
            case EditorMode.Paint:
                RenderTextureSlots();
                break;
            case EditorMode.Foliage:
                RenderFoliageItems();
                break;
        }
    }

    #region Sculpt Mode - Height Layers

    private void RenderHeightLayers()
    {
        // Toolbar
        RenderLayersToolbar();

        ImGui.Spacing();

        // Layer list
        for (int i = 0; i < heightLayers.Count; i++)
        {
            RenderLayerItem(heightLayers[i], i);
        }
    }

    private void RenderLayersToolbar()
    {
        ImGui.SetCursorScreenPos(new Vector2(ContentRect.X + 4, ContentRect.Y + 4));

        if (ImGui.Button($"{Icons.Plus}##add_layer", new Vector2(24, 24)))
        {
            heightLayers.Add(new HeightLayer { Name = $"Layer {heightLayers.Count}" });
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add Layer");

        ImGui.SameLine();

        if (ImGui.Button($"{Icons.Trash}##delete_layer", new Vector2(24, 24)))
        {
            if (heightLayers.Count > 1 && SelectedLayerIndex < heightLayers.Count)
            {
                heightLayers.RemoveAt(SelectedLayerIndex);
                SelectedLayerIndex = Math.Max(0, SelectedLayerIndex - 1);
            }
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Delete Layer");
    }

    private void RenderLayerItem(HeightLayer layer, int index)
    {
        bool isSelected = SelectedLayerIndex == index;

        ImGui.PushID($"layer_{index}");

        // Selectable background
        ImGui.PushStyleColor(ImGuiCol.Header, ColorPalette.Selection.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ColorPalette.Hover.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, ColorPalette.Pressed.ToVector4());

        ImGui.Selectable($"##layer_select_{index}", isSelected);

        if (ImGui.IsItemClicked())
        {
            SelectedLayerIndex = index;
            LayerSelected?.Invoke(this, new LayerSelectedEventArgs { LayerIndex = index, Layer = layer });
        }

        ImGui.PopStyleColor(3);

        // Layer controls on same line
        ImGui.SameLine();

        // Visibility toggle
        bool visible = layer.IsVisible;
        ImGui.PushStyleColor(ImGuiCol.Text, visible ? ColorPalette.TextPrimary.ToVector4() : ColorPalette.TextSecondary.ToVector4());
        ImGui.Text(visible ? Icons.Eye : Icons.EyeOff);
        ImGui.PopStyleColor();
        if (ImGui.IsItemClicked())
        {
            layer.IsVisible = !layer.IsVisible;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Toggle Visibility");

        ImGui.SameLine();

        // Lock toggle
        bool locked = layer.IsLocked;
        ImGui.PushStyleColor(ImGuiCol.Text, locked ? ColorPalette.Warning.ToVector4() : ColorPalette.TextSecondary.ToVector4());
        ImGui.Text(locked ? Icons.Lock : Icons.Unlock);
        ImGui.PopStyleColor();
        if (ImGui.IsItemClicked())
        {
            layer.IsLocked = !layer.IsLocked;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Toggle Lock");

        ImGui.SameLine();

        // Layer name
        ImGui.Text(layer.Name);

        ImGui.PopID();
    }

    #endregion

    #region Paint Mode - Texture Slots

    private void RenderTextureSlots()
    {
        // Search bar
        RenderTextureSearch();

        ImGui.Spacing();

        // Texture grid
        float itemWidth = EditorStyle.ScaleValue(64.0f);
        float padding = EditorStyle.ScaleValue(4.0f);
        float labelHeight = ImGui.GetTextLineHeight() + EditorStyle.ScaleValue(8.0f);
        float itemHeight = itemWidth + labelHeight;
        float availableWidth = ContentRect.Width - EditorStyle.ScaleValue(8.0f);
        int itemsPerRow = Math.Max(1, (int)((availableWidth + padding) / (itemWidth + padding)));

        int visibleCount = 0;
        for (int i = 0; i < textureSlots.Count; i++)
        {
            var slot = textureSlots[i];

            // Only show first 16 slots by default, or if searching
            if (i >= 16 && string.IsNullOrEmpty(searchBuffer))
                continue;

            if (!string.IsNullOrEmpty(searchBuffer) &&
                !slot.Name.Contains(searchBuffer, StringComparison.OrdinalIgnoreCase))
                continue;

            int col = visibleCount % itemsPerRow;
            if (col > 0)
                ImGui.SameLine(0.0f, padding);

            RenderTextureSlot(slot, i, itemWidth, itemHeight, labelHeight);
            visibleCount++;
        }

        // Show more button
        ImGui.Spacing();
        ImGui.SetCursorScreenPos(new Vector2(ContentRect.X + 8, ImGui.GetCursorScreenPos().Y));
        ImGui.TextDisabled($"Showing {Math.Min(16, textureSlots.Count)} of {textureSlots.Count} slots");
    }

    private string searchBuffer = "";

    private void RenderTextureSearch()
    {
        ImGui.SetCursorScreenPos(new Vector2(ContentRect.X + 4, ContentRect.Y + 4));
        ImGui.SetNextItemWidth(ContentRect.Width - 8);
        ImGui.InputTextWithHint("##texture_search", "Search textures...", ref searchBuffer, 256);
    }

    private void RenderTextureSlot(TextureSlot slot, int index, float width, float height, float labelHeight)
    {
        bool isSelected = SelectedTextureSlot == index;

        var drawList = ImGui.GetWindowDrawList();
        Vector2 cursor = ImGui.GetCursorScreenPos();
        float inset = EditorStyle.ScaleValue(4.0f);
        float previewHeight = width - inset * 2.0f;

        // Click area
        ImGui.InvisibleButton($"##tex_slot_{index}", new Vector2(width, height));
        bool isHovered = ImGui.IsItemHovered();

        // Background
        uint bgColor = isSelected ? ColorPalette.Selection.ToUint() :
                       isHovered ? ColorPalette.Hover.ToUint() :
                       ColorPalette.DarkBackground.ToUint();
        drawList.AddRectFilled(cursor, new Vector2(cursor.X + width, cursor.Y + height), bgColor, 4.0f);

        // Border
        if (isSelected)
        {
            drawList.AddRect(cursor, new Vector2(cursor.X + width, cursor.Y + height), ColorPalette.Accent.ToUint(), 4.0f, ImDrawFlags.None, 2.0f);
        }

        // Texture preview (placeholder)
        if (!slot.IsEmpty)
        {
            Vector2 previewPos = new Vector2(cursor.X + inset, cursor.Y + inset);
            Vector2 previewSize = new Vector2(width - inset * 2.0f, previewHeight);
            drawList.AddRectFilled(previewPos, new Vector2(previewPos.X + previewSize.X, previewPos.Y + previewSize.Y),
                ColorPalette.Background.ToUint());

            // Placeholder pattern
            drawList.AddText(new Vector2(previewPos.X + 4, previewPos.Y + previewSize.Y * 0.4f),
                ColorPalette.TextSecondary.ToUint(), $"#{index}");
        }
        else
        {
            // Empty slot indicator
            Vector2 center = new Vector2(cursor.X + width * 0.5f, cursor.Y + inset + previewHeight * 0.5f);
            Vector2 iconPos = new Vector2(center.X - FontManager.ScaledIconSize * 0.5f, center.Y - FontManager.ScaledIconSize * 0.5f);
            ImGui.AddText(drawList, FontManager.Icons, FontManager.ScaledIconSize, iconPos, ColorPalette.TextSecondary.ToUint(), Icons.Plus);
        }

        // Name
        string displayName = TruncateToWidth(slot.Name, width - inset * 2.0f);
        Vector2 textSize = ImGui.CalcTextSize(displayName);
        Vector2 namePos = new Vector2(
            cursor.X + inset,
            cursor.Y + width + MathF.Max(0.0f, (labelHeight - textSize.Y) * 0.5f));
        drawList.AddText(namePos, ColorPalette.TextSecondary.ToUint(), displayName);

        // Handle click
        if (ImGui.IsItemClicked())
        {
            SelectedTextureSlot = index;
            TextureSlotSelected?.Invoke(this, new TextureSlotSelectedEventArgs { SlotIndex = index, Slot = slot });
        }

        // Tooltip
        if (isHovered)
        {
            ImGui.SetTooltip($"{slot.Name}\nIndex: {index}");
        }

        // Right click menu
        if (ImGui.BeginPopupContextItem($"##tex_context_{index}"))
        {
            if (ImGui.MenuItem("Import Texture"))
            {
                // TODO: Import texture
            }
            if (ImGui.MenuItem("Clear"))
            {
                slot.IsEmpty = true;
                slot.Name = $"Texture {index}";
            }
            ImGui.EndPopup();
        }
    }

    #endregion

    #region Foliage Mode

    private void RenderFoliageItems()
    {
        // Toolbar
        RenderFoliageToolbar();

        ImGui.Spacing();

        // Foliage list
        float itemWidth = EditorStyle.ScaleValue(80.0f);
        float padding = EditorStyle.ScaleValue(4.0f);
        float labelHeight = ImGui.GetTextLineHeight() + EditorStyle.ScaleValue(8.0f);
        float itemHeight = itemWidth + labelHeight;
        int itemsPerRow = Math.Max(1, (int)((ContentRect.Width + padding) / (itemWidth + padding)));
        int visibleCount = 0;

        for (int i = 0; i < foliageItems.Count; i++)
        {
            int col = visibleCount % itemsPerRow;
            if (col > 0)
                ImGui.SameLine(0.0f, padding);

            RenderFoliageItem(foliageItems[i], i, itemWidth, itemHeight, labelHeight);
            visibleCount++;
        }
    }

    private void RenderFoliageToolbar()
    {
        ImGui.SetCursorScreenPos(new Vector2(ContentRect.X + 4, ContentRect.Y + 4));

        if (ImGui.Button($"{Icons.Plus}##add_foliage", new Vector2(24, 24)))
        {
            foliageItems.Add(new FoliageItem { Name = $"Foliage {foliageItems.Count}", Icon = Icons.Tree });
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add Foliage");

        ImGui.SameLine();

        if (ImGui.Button($"{Icons.Folder}##import_foliage", new Vector2(24, 24)))
        {
            // TODO: Import foliage prefab
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Import Prefab");
    }

    private void RenderFoliageItem(FoliageItem item, int index, float width, float height, float labelHeight)
    {
        bool isSelected = SelectedFoliageIndex == index;

        var drawList = ImGui.GetWindowDrawList();
        Vector2 cursor = ImGui.GetCursorScreenPos();
        float inset = EditorStyle.ScaleValue(4.0f);

        ImGui.InvisibleButton($"##foliage_{index}", new Vector2(width, height));
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
        Vector2 iconPos = new Vector2(cursor.X + (width - FontManager.ScaledIconSize) * 0.5f, cursor.Y + EditorStyle.ScaleValue(8.0f));
        uint iconColor = isSelected ? ColorPalette.Accent.ToUint() : ColorPalette.TextPrimary.ToUint();
        ImGui.AddText(drawList, FontManager.Icons, FontManager.ScaledIconSize, iconPos, iconColor, item.Icon);

        // Name
        string displayName = TruncateToWidth(item.Name, width - inset * 2.0f);
        Vector2 textSize = ImGui.CalcTextSize(displayName);
        Vector2 namePos = new Vector2(
            cursor.X + inset,
            cursor.Y + width + MathF.Max(0.0f, (labelHeight - textSize.Y) * 0.5f));
        drawList.AddText(namePos, ColorPalette.TextSecondary.ToUint(), displayName);

        // Handle click
        if (ImGui.IsItemClicked())
        {
            SelectedFoliageIndex = index;
            FoliageSelected?.Invoke(this, new FoliageSelectedEventArgs { Index = index, Item = item });
        }
    }

    #endregion

    public void SetMode(EditorMode mode)
    {
        CurrentMode = mode;
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

#region 数据类

public class TextureSlot
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public bool IsEmpty { get; set; } = true;
    public string? TexturePath { get; set; }
}

public class FoliageItem
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = Icons.Tree;
    public string? PrefabPath { get; set; }
}

public class HeightLayer
{
    public string Name { get; set; } = "";
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; } = false;
}

#endregion

#region 事件参数

public class TextureSlotSelectedEventArgs : EventArgs
{
    public int SlotIndex { get; set; }
    public TextureSlot Slot { get; set; } = null!;
}

public class FoliageSelectedEventArgs : EventArgs
{
    public int Index { get; set; }
    public FoliageItem Item { get; set; } = null!;
}

public class LayerSelectedEventArgs : EventArgs
{
    public int LayerIndex { get; set; }
    public HeightLayer Layer { get; set; } = null!;
}

#endregion
