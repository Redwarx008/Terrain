#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.Services;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

internal sealed class ClimateManagerPanel
{
    public event EventHandler? ClimateMaskChanged;

    public void Render()
    {
        var climateState = ClimateRuleService.Instance;

        ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), "Climate Palette");
        ImGui.Spacing();

        if (ImGui.Button("[+] Add New Climate##climate_add", new Vector2(-1, EditorStyle.ScaleValue(30.0f))))
        {
            ClimateDefinition climate = climateState.AddClimate();
            EditorState.Instance.CurrentClimateId = climate.Id;
        }

        ImGui.Spacing();

        foreach (var climate in climateState.Climates)
        {
            RenderClimateRow(climateState, climate);
            ImGui.Spacing();
        }

        bool showOverlay = EditorState.Instance.ShowMaskOverlay;
        if (ImGui.Checkbox("Show Mask Overlay", ref showOverlay))
            EditorState.Instance.ShowMaskOverlay = showOverlay;
    }

    private void RenderClimateRow(ClimateRuleService climateState, ClimateDefinition climate)
    {
        bool selected = EditorState.Instance.CurrentClimateId == climate.Id;
        Vector2 rowStart = ImGui.GetCursorScreenPos();
        float rowHeight = EditorStyle.ScaleValue(36.0f);
        float rowWidth = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();

        uint rowColor = selected ? ColorPalette.Selection.ToUint() : ColorPalette.DarkBackground.ToUint();
        drawList.AddRectFilled(rowStart, new Vector2(rowStart.X + rowWidth, rowStart.Y + rowHeight), rowColor, EditorStyle.ScaleValue(4.0f));
        drawList.AddRect(rowStart, new Vector2(rowStart.X + rowWidth, rowStart.Y + rowHeight), ColorPalette.Border.ToUint(), EditorStyle.ScaleValue(4.0f));

        ImGui.SetCursorScreenPos(rowStart);
        if (ImGui.InvisibleButton($"##climate_row_{climate.Id}", new Vector2(rowWidth, rowHeight)))
        {
            EditorState.Instance.CurrentClimateId = climate.Id;
            EditorState.Instance.HasSelectedTool = true;
        }

        float colorBoxSize = EditorStyle.ScaleValue(22.0f);
        Vector2 colorMin = new(rowStart.X + EditorStyle.ScaleValue(26.0f), rowStart.Y + EditorStyle.ScaleValue(7.0f));
        Vector2 colorMax = new(colorMin.X + colorBoxSize, colorMin.Y + colorBoxSize);
        drawList.AddRectFilled(colorMin, colorMax, ImGui.ColorConvertFloat4ToU32(climate.DebugColor), EditorStyle.ScaleValue(3.0f));
        drawList.AddRect(colorMin, colorMax, ColorPalette.BorderLight.ToUint(), EditorStyle.ScaleValue(3.0f));

        drawList.AddText(new Vector2(rowStart.X + EditorStyle.ScaleValue(8.0f), rowStart.Y + EditorStyle.ScaleValue(10.0f)), ColorPalette.TextSecondary.ToUint(), climate.Id.ToString());
        drawList.AddText(new Vector2(colorMax.X + EditorStyle.ScaleValue(8.0f), rowStart.Y + EditorStyle.ScaleValue(10.0f)), ColorPalette.TextPrimary.ToUint(), climate.Name);

        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + rowWidth - EditorStyle.ScaleValue(62.0f), rowStart.Y + EditorStyle.ScaleValue(6.0f)));
        Vector4 debugColor = climate.DebugColor;
        if (ImGui.ColorEdit4($"##climate_color_{climate.Id}", ref debugColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaBar))
        {
            climate.DebugColor = debugColor;
            climateState.NotifyMutated();
            ClimateMaskChanged?.Invoke(this, EventArgs.Empty);
        }

        ImGui.SetCursorScreenPos(new Vector2(rowStart.X, rowStart.Y + rowHeight));
    }
}
