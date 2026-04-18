#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.Services;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

internal sealed class SculptModePanel
{
    private static readonly (HeightTool Tool, string Icon, string Description)[] Tools =
    {
        (HeightTool.Raise, Icons.ArrowUp, "Raise terrain height"),
        (HeightTool.Lower, Icons.ArrowDown, "Lower terrain height"),
        (HeightTool.Smooth, Icons.Water, "Smooth terrain"),
        (HeightTool.Flatten, Icons.Layer, "Flatten terrain"),
    };

    public void Render()
    {
        RenderTools();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        RenderBrushParameters();
    }

    private void RenderTools()
    {
        foreach (var entry in Tools)
        {
            bool selected = EditorState.Instance.CurrentHeightTool == entry.Tool;
            if (selected)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ColorPalette.Accent.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.AccentHover.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.AccentPressed.ToVector4());
            }

            if (ImGui.Button($"{entry.Icon} {entry.Tool}##sculpt_tool_{entry.Tool}", new Vector2(-1, EditorStyle.ScaleValue(30.0f))))
            {
                EditorState.Instance.CurrentHeightTool = entry.Tool;
                EditorState.Instance.HasSelectedTool = true;
            }

            if (selected)
                ImGui.PopStyleColor(3);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(entry.Description);
        }
    }

    private void RenderBrushParameters()
    {
        var brush = BrushParameters.Instance;

        ImGui.Text("Size");
        ImGui.SetNextItemWidth(-1);
        float size = brush.Size;
        if (ImGui.SliderFloat("##sculpt_size", ref size, 1.0f, 200.0f, "%.0f"))
            brush.Size = size;

        ImGui.Spacing();
        ImGui.Text("Strength");
        ImGui.SetNextItemWidth(-1);
        float strength = brush.Strength;
        if (ImGui.SliderFloat("##sculpt_strength", ref strength, 0.0f, 1.0f, "%.2f"))
            brush.Strength = strength;

        ImGui.Spacing();
        ImGui.Text("Falloff");
        ImGui.SetNextItemWidth(-1);
        float falloff = brush.Falloff;
        if (ImGui.SliderFloat("##sculpt_falloff", ref falloff, 0.0f, 1.0f, "%.2f"))
            brush.Falloff = falloff;

        ImGui.Spacing();
        ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "Soft <---> Hard");

        ImGui.Spacing();
        ImGui.Text("Preview");
        RenderBrushPreview(brush);
    }

    private static void RenderBrushPreview(BrushParameters brush)
    {
        float previewSize = Math.Min(ImGui.GetContentRegionAvail().X, EditorStyle.ScaleValue(120.0f));
        Vector2 cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, new Vector2(cursor.X + previewSize, cursor.Y + previewSize), ColorPalette.DarkBackground.ToUint());

        Vector2 center = new(cursor.X + previewSize * 0.5f, cursor.Y + previewSize * 0.5f);
        float radius = previewSize * 0.4f * (brush.Size / 200.0f + 0.1f);
        float innerRadius = radius * brush.EffectiveFalloff;

        drawList.AddCircleFilled(center, radius, ColorPalette.Accent.WithAlpha(0.25f).ToUint());
        drawList.AddCircleFilled(center, innerRadius, ColorPalette.Accent.WithAlpha(0.55f).ToUint());
        drawList.AddCircle(center, radius, ColorPalette.BorderLight.ToUint());

        ImGui.Dummy(new Vector2(previewSize, previewSize));
    }
}
