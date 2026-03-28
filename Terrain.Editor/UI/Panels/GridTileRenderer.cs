#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

internal readonly struct GridTileLayout
{
    public GridTileLayout(float itemWidth, float itemHeight, float labelHeight, float padding, float inset, float cornerRounding)
    {
        ItemWidth = itemWidth;
        ItemHeight = itemHeight;
        LabelHeight = labelHeight;
        Padding = padding;
        Inset = inset;
        CornerRounding = cornerRounding;
    }

    public float ItemWidth { get; }
    public float ItemHeight { get; }
    public float LabelHeight { get; }
    public float Padding { get; }
    public float Inset { get; }
    public float CornerRounding { get; }
}

internal readonly struct GridTileContext
{
    public GridTileContext(ImDrawListPtr drawList, Vector2 cursor, bool isHovered)
    {
        DrawList = drawList;
        Cursor = cursor;
        IsHovered = isHovered;
    }

    public ImDrawListPtr DrawList { get; }
    public Vector2 Cursor { get; }
    public bool IsHovered { get; }
}

internal static class GridTileRenderer
{
    public static GridTileLayout CreateLayout(float baseItemWidth, float basePadding)
    {
        // Keep the visual tile square and reserve a dedicated footer band for text,
        // so DPI/font changes cannot make labels spill into the next row.
        float itemWidth = EditorStyle.ScaleValue(baseItemWidth);
        float padding = EditorStyle.ScaleValue(basePadding);
        float labelHeight = ImGui.GetTextLineHeight() + EditorStyle.ScaleValue(8.0f);
        float itemHeight = itemWidth + labelHeight;
        float inset = EditorStyle.ScaleValue(4.0f);
        float cornerRounding = EditorStyle.ScaleValue(4.0f);
        return new GridTileLayout(itemWidth, itemHeight, labelHeight, padding, inset, cornerRounding);
    }

    public static int GetItemsPerRow(float availableWidth, GridTileLayout layout)
    {
        return Math.Max(1, (int)((availableWidth + layout.Padding) / (layout.ItemWidth + layout.Padding)));
    }

    public static void AdvanceRowLayout(int visibleIndex, int itemsPerRow, GridTileLayout layout)
    {
        if (visibleIndex % itemsPerRow > 0)
            ImGui.SameLine(0.0f, layout.Padding);
    }

    public static GridTileContext BeginTile(string id, GridTileLayout layout, bool isSelected)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 cursor = ImGui.GetCursorScreenPos();

        // The interactive area includes both preview and label, so layout and hit-test
        // stay aligned even when the footer grows with the current font metrics.
        ImGui.InvisibleButton(id, new Vector2(layout.ItemWidth, layout.ItemHeight));
        bool isHovered = ImGui.IsItemHovered();

        uint bgColor = isSelected ? ColorPalette.Selection.ToUint() :
                       isHovered ? ColorPalette.Hover.ToUint() :
                       ColorPalette.DarkBackground.ToUint();

        drawList.AddRectFilled(
            cursor,
            new Vector2(cursor.X + layout.ItemWidth, cursor.Y + layout.ItemHeight),
            bgColor,
            layout.CornerRounding);

        if (isSelected)
        {
            drawList.AddRect(
                cursor,
                new Vector2(cursor.X + layout.ItemWidth, cursor.Y + layout.ItemHeight),
                ColorPalette.Accent.ToUint(),
                layout.CornerRounding,
                ImDrawFlags.None,
                2.0f);
        }

        return new GridTileContext(drawList, cursor, isHovered);
    }

    public static Vector2 GetSquareContentMin(Vector2 cursor, GridTileLayout layout)
    {
        return new Vector2(cursor.X + layout.Inset, cursor.Y + layout.Inset);
    }

    public static Vector2 GetSquareContentSize(GridTileLayout layout)
    {
        float size = layout.ItemWidth - layout.Inset * 2.0f;
        return new Vector2(size, size);
    }

    public static Vector2 GetSquareContentCenter(Vector2 cursor, GridTileLayout layout)
    {
        Vector2 min = GetSquareContentMin(cursor, layout);
        Vector2 size = GetSquareContentSize(layout);
        return new Vector2(min.X + size.X * 0.5f, min.Y + size.Y * 0.5f);
    }

    public static void DrawLabel(ImDrawListPtr drawList, Vector2 cursor, GridTileLayout layout, string text, bool centered = false)
    {
        string displayText = TruncateToWidth(text, layout.ItemWidth - layout.Inset * 2.0f);
        Vector2 textSize = ImGui.CalcTextSize(displayText);
        float x = centered
            ? cursor.X + (layout.ItemWidth - textSize.X) * 0.5f
            : cursor.X + layout.Inset;
        float y = cursor.Y + layout.ItemWidth + Math.Max(0.0f, (layout.LabelHeight - textSize.Y) * 0.5f);

        drawList.AddText(new Vector2(x, y), ColorPalette.TextSecondary.ToUint(), displayText);
    }

    public static string TruncateToWidth(string text, float maxWidth)
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
