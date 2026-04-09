#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;

namespace Terrain.Editor.UI.Styling;

/// <summary>
/// Shared styling helpers for text input controls.
/// Keeps focused/active text inputs visually consistent across the editor.
/// </summary>
internal static class TextInputStyle
{
    private static readonly Vector4 FocusOutline = ColorPalette.Accent.ToVector4();

    public static void Push()
    {
    }

    public static void Pop()
    {
    }

    public static void DrawFocusOutline()
    {
        bool isFocused = ImGui.IsItemFocused() || ImGui.IsItemActive();
        if (!isFocused)
            return;

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        float thickness = MathF.Max(1.0f, EditorStyle.ScaleValue(1.0f));
        uint outlineColor = ImGui.GetColorU32(FocusOutline);
        var drawList = ImGui.GetWindowDrawList();

        float left = MathF.Round(min.X);
        float top = MathF.Round(min.Y);
        float right = MathF.Round(max.X);
        float bottom = MathF.Round(max.Y);
        float t = MathF.Round(thickness);
        if (t < 1.0f)
            t = 1.0f;

        drawList.AddRectFilled(new Vector2(left, top), new Vector2(right, top + t), outlineColor);
        drawList.AddRectFilled(new Vector2(left, bottom - t), new Vector2(right, bottom), outlineColor);
        drawList.AddRectFilled(new Vector2(left, top), new Vector2(left + t, bottom), outlineColor);
        drawList.AddRectFilled(new Vector2(right - t, top), new Vector2(right, bottom), outlineColor);
    }

    public static bool Render(Action renderInput)
    {
        Push();
        try
        {
            renderInput();
            DrawFocusOutline();
            return ImGui.IsItemFocused() || ImGui.IsItemActive();
        }
        finally
        {
            Pop();
        }
    }
}
