#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Terrain.Editor.Services;
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
    /// 选中的纹理槽索引 (Paint mode)，-1 表示无选择
    /// </summary>
    public int SelectedTextureSlot { get; set; } = -1;

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
    public event EventHandler? TextureSlotDeselected;
    public event EventHandler<TextureImportEventArgs>? TextureImportRequested;
    public event EventHandler<TextureSlotEventArgs>? TextureClearRequested;
    public event EventHandler<FoliageSelectedEventArgs>? FoliageSelected;
    public event EventHandler<LayerSelectedEventArgs>? LayerSelected;

    #endregion

    #region 私有字段

    private List<FoliageItem> foliageItems = new();
    private List<HeightLayer> heightLayers = new();
    private int textureContextMenuSlotIndex = -1;
    private Vector2 textureContextMenuPosition;
    private int textureContextMenuOpenedFrame = -1;

    #endregion

    public AssetsPanel()
    {
        Title = "Assets";
        Icon = null;
        ShowTitleBar = true;

        InitializeFoliageItems();
        InitializeHeightLayers();
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

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
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
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
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
        GridTileLayout tileLayout = GridTileRenderer.CreateLayout(64.0f, 4.0f);
        float availableWidth = ContentRect.Width - EditorStyle.ScaleValue(8.0f);
        int itemsPerRow = GridTileRenderer.GetItemsPerRow(availableWidth, tileLayout);

        int visibleCount = 0;
        var activeSlots = MaterialSlotManager.Instance.GetActiveSlots().ToList();

        // 渲染已导入纹理的槽位
        foreach (var slot in activeSlots)
        {
            GridTileRenderer.AdvanceRowLayout(visibleCount, itemsPerRow, tileLayout);
            RenderMaterialSlot(slot, tileLayout);
            visibleCount++;
        }

        // 渲染 "+" 导入入口（始终显示）
        GridTileRenderer.AdvanceRowLayout(visibleCount, itemsPerRow, tileLayout);
        RenderAddTextureTile(tileLayout);
        RenderTextureContextMenu();
    }

    private void RenderMaterialSlot(MaterialSlot slot, GridTileLayout tileLayout)
    {
        bool isSelected = SelectedTextureSlot == slot.Index;
        var drawList = ImGui.GetWindowDrawList();
        Vector2 cursor = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##tex_slot_{slot.Index}", new Vector2(tileLayout.ItemWidth, tileLayout.ItemHeight));
        bool isHovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            OpenTextureContextMenu(slot.Index);
        }

        bool isContextMenuOpenForSlot = textureContextMenuSlotIndex == slot.Index;
        uint bgColor = isSelected ? ColorPalette.Selection.ToUint() :
                       isHovered ? ColorPalette.Hover.ToUint() :
                       ColorPalette.DarkBackground.ToUint();
        drawList.AddRectFilled(
            cursor,
            new Vector2(cursor.X + tileLayout.ItemWidth, cursor.Y + tileLayout.ItemHeight),
            bgColor,
            tileLayout.CornerRounding);
        if (isSelected)
        {
            drawList.AddRect(
                cursor,
                new Vector2(cursor.X + tileLayout.ItemWidth, cursor.Y + tileLayout.ItemHeight),
                ColorPalette.Accent.ToUint(),
                tileLayout.CornerRounding,
                ImDrawFlags.None,
                2.0f);
        }
        Vector2 previewPos = GridTileRenderer.GetSquareContentMin(cursor, tileLayout);
        Vector2 previewSize = GridTileRenderer.GetSquareContentSize(tileLayout);

        // 纹理预览 - 使用 DrawList 绘制，避免干扰 ImGui 布局
        if (slot.AlbedoTexture != null)
        {
            // 使用 DrawList 的 AddImage 方法绘制纹理
            var texRef = ImGuiExtension.GetTextureKey(slot.AlbedoTexture);
            drawList.AddImage(texRef, previewPos, new Vector2(previewPos.X + previewSize.X, previewPos.Y + previewSize.Y));
        }
        else
        {
            // 有路径但无 GPU 纹理 - 显示占位
            drawList.AddRectFilled(previewPos,
                new Vector2(previewPos.X + previewSize.X, previewPos.Y + previewSize.Y),
                ColorPalette.Background.ToUint());
            drawList.AddText(
                new Vector2(previewPos.X + tileLayout.Inset, previewPos.Y + previewSize.Y * 0.4f),
                ColorPalette.TextSecondary.ToUint(),
                $"#{slot.Index}");
        }

        GridTileRenderer.DrawLabel(drawList, cursor, tileLayout, slot.Name);

        // 点击选中 - toggle behavior
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            CloseTextureContextMenu();

            if (isSelected)
            {
                // 取消选择
                SelectedTextureSlot = -1;
                TextureSlotDeselected?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                // 选中新槽位
                SelectedTextureSlot = slot.Index;
                TextureSlotSelected?.Invoke(this, new TextureSlotSelectedEventArgs { SlotIndex = slot.Index });
            }
        }


        // 右键菜单

        // Tooltip
        if (isHovered &&
            !isContextMenuOpenForSlot &&
            !ImGui.IsMouseDown(ImGuiMouseButton.Left) &&
            !ImGui.IsMouseDown(ImGuiMouseButton.Right) &&
            !ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            ImGui.SetTooltip($"{slot.Name}\nIndex: {slot.Index}");
        }
    }

    private void RenderTextureContextMenu()
    {
        if (textureContextMenuSlotIndex < 0)
            return;

        var slot = MaterialSlotManager.Instance[textureContextMenuSlotIndex];
        if (slot.IsEmpty)
        {
            CloseTextureContextMenu();
            return;
        }

        var style = ImGui.GetStyle();
        Vector2 labelSize = ImGui.CalcTextSize("Delete");
        float menuWidth = labelSize.X + style.FramePadding.X * 2.0f + EditorStyle.ScaleValue(2.0f);
        float menuHeight = labelSize.Y + style.FramePadding.Y * 2.0f + EditorStyle.ScaleValue(2.0f);
        float maxX = MathF.Max(ContentRect.X, ContentRect.X + ContentRect.Width - menuWidth);
        float maxY = MathF.Max(ContentRect.Y, ContentRect.Y + ContentRect.Height - menuHeight);
        Vector2 menuPos = new Vector2(
            Math.Clamp(textureContextMenuPosition.X, ContentRect.X, maxX),
            Math.Clamp(textureContextMenuPosition.Y, ContentRect.Y, maxY));
        Vector2 menuSize = new Vector2(menuWidth, menuHeight);

        ImGui.SetCursorScreenPos(menuPos);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        bool began = ImGui.BeginChild(
            $"##texture_context_menu_{Id}",
            menuSize,
            ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        bool isWindowHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

        if (began)
        {
            Vector2 itemSize = new Vector2(menuSize.X, menuSize.Y);

            if (ImGui.Button("Delete", itemSize))
            {
                TextureClearRequested?.Invoke(this, new TextureSlotEventArgs { SlotIndex = textureContextMenuSlotIndex });
                CloseTextureContextMenu();
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();

        if (ImGui.GetFrameCount() > textureContextMenuOpenedFrame &&
            !isWindowHovered &&
            (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
        {
            CloseTextureContextMenu();
        }
    }

    private void OpenTextureContextMenu(int slotIndex)
    {
        textureContextMenuSlotIndex = slotIndex;
        textureContextMenuPosition = ImGui.GetIO().MousePos;
        textureContextMenuOpenedFrame = ImGui.GetFrameCount();
    }

    private void CloseTextureContextMenu()
    {
        textureContextMenuSlotIndex = -1;
        textureContextMenuOpenedFrame = -1;
    }

    private void RenderAddTextureTile(GridTileLayout tileLayout)
    {
        GridTileContext tile = GridTileRenderer.BeginTile("##add_texture", tileLayout, false);
        Vector2 previewPos = GridTileRenderer.GetSquareContentMin(tile.Cursor, tileLayout);
        Vector2 previewSize = GridTileRenderer.GetSquareContentSize(tileLayout);

        // 背景
        tile.DrawList.AddRectFilled(previewPos,
            new Vector2(previewPos.X + previewSize.X, previewPos.Y + previewSize.Y),
            ColorPalette.Background.ToUint());

        // 边框
        tile.DrawList.AddRect(previewPos,
            new Vector2(previewPos.X + previewSize.X, previewPos.Y + previewSize.Y),
            ColorPalette.Border.ToUint());

        // + 图标居中
        Vector2 center = GridTileRenderer.GetSquareContentCenter(tile.Cursor, tileLayout);
        Vector2 iconPos = new Vector2(
            center.X - FontManager.ScaledIconSize * 0.5f,
            center.Y - FontManager.ScaledIconSize * 0.5f);
        ImGui.AddText(tile.DrawList, FontManager.Icons, FontManager.ScaledIconSize, iconPos,
            ColorPalette.TextSecondary.ToUint(), Icons.Plus);

        GridTileRenderer.DrawLabel(tile.DrawList, tile.Cursor, tileLayout, "Add Texture");

        // 点击触发导入
        if (ImGui.IsItemClicked())
        {
            int nextSlot = MaterialSlotManager.Instance.NextAvailableSlotIndex;
            if (nextSlot >= 0)
            {
                TextureImportRequested?.Invoke(this, new TextureImportEventArgs { SlotIndex = nextSlot, TextureType = TextureType.Albedo });
            }
            else
            {
                ImGui.OpenPopup("##no_slots_available");
            }
        }

        if (tile.IsHovered)
            ImGui.SetTooltip("Import new texture");

        // 槽位已满提示
        if (ImGui.BeginPopup("##no_slots_available"))
        {
            ImGui.Text("All 256 texture slots are in use.");
            ImGui.Text("Please delete unused textures.");
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
        GridTileLayout tileLayout = GridTileRenderer.CreateLayout(80.0f, 4.0f);
        int itemsPerRow = GridTileRenderer.GetItemsPerRow(ContentRect.Width, tileLayout);
        int visibleCount = 0;

        for (int i = 0; i < foliageItems.Count; i++)
        {
            GridTileRenderer.AdvanceRowLayout(visibleCount, itemsPerRow, tileLayout);

            RenderFoliageItem(foliageItems[i], i, tileLayout);
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

    private void RenderFoliageItem(FoliageItem item, int index, GridTileLayout tileLayout)
    {
        bool isSelected = SelectedFoliageIndex == index;

        GridTileContext tile = GridTileRenderer.BeginTile($"##foliage_{index}", tileLayout, isSelected);

        // Icon
        Vector2 iconPos = new Vector2(
            tile.Cursor.X + (tileLayout.ItemWidth - FontManager.ScaledIconSize) * 0.5f,
            tile.Cursor.Y + EditorStyle.ScaleValue(8.0f));
        uint iconColor = isSelected ? ColorPalette.Accent.ToUint() : ColorPalette.TextPrimary.ToUint();
        ImGui.AddText(tile.DrawList, FontManager.Icons, FontManager.ScaledIconSize, iconPos, iconColor, item.Icon);

        GridTileRenderer.DrawLabel(tile.DrawList, tile.Cursor, tileLayout, item.Name);

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
}

#region 数据类

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

/// <summary>
/// 纹理类型。
/// </summary>
public enum TextureType
{
    Albedo,
    Normal
}

public class TextureSlotSelectedEventArgs : EventArgs
{
    public int SlotIndex { get; set; }
}

public class TextureImportEventArgs : EventArgs
{
    public int SlotIndex { get; set; }
    public TextureType TextureType { get; set; }
}

public class TextureSlotEventArgs : EventArgs
{
    public int SlotIndex { get; set; }
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
