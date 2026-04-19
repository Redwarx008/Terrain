#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.Services;
using Terrain.Editor.UI;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Controls;

/// <summary>
/// Row of proportional texture buttons — one per rule for the active climate.
/// Each button's width is proportional to the rule's height range.
/// Buttons are filled with the rule's albedo texture thumbnail.
/// Clicking a button selects that rule.
/// </summary>
internal sealed class ClimateGradientBar
{
    public event EventHandler? RuleSelected;

    private static readonly Vector4 FallbackFillTint = new(0.32f, 0.40f, 0.34f, 0.85f);
    private static readonly Vector4 SegmentHoverTint = new(1.0f, 1.0f, 1.0f, 0.10f);

    public void Render()
    {
        var state = EditorState.Instance;
        var climateState = ClimateRuleService.Instance;

        var climate = climateState.FindClimate(state.CurrentClimateId);
        if (climate == null)
        {
            ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "No climate selected");
            return;
        }

        var rules = climateState.GetRulesForClimate(climate.Id);
        if (rules.Count == 0)
        {
            ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "No rules");
            return;
        }

        float buttonWidth = EditorStyle.ScaleValue(56.0f);
        float buttonHeight = EditorStyle.ScaleValue(56.0f);
        float availableWidth = ImGui.GetContentRegionAvail().X;
        float borderThickness = EditorStyle.ScaleValue(2.0f);
        float segmentSpacing = EditorStyle.ScaleValue(8.0f);
        float rounding = EditorStyle.ScaleValue(6.0f);
        var drawList = ImGui.GetWindowDrawList();
        var rowStart = ImGui.GetCursorScreenPos();

        float totalWidth = rules.Count * buttonWidth + MathF.Max(0, rules.Count - 1) * segmentSpacing;

        float xOffset = 0f;
        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var segMin = new Vector2(rowStart.X + xOffset, rowStart.Y);
            var segMax = new Vector2(segMin.X + buttonWidth, rowStart.Y + buttonHeight);
            bool isSelected = state.SelectedRuleIndex >= 0 && state.SelectedRuleIndex < climateState.Rules.Count
                && ReferenceEquals(climateState.Rules[state.SelectedRuleIndex], rule);
            bool isHovered = ImGui.IsMouseHoveringRect(segMin, segMax);

            var slot = MaterialSlotManager.Instance[rule.MaterialSlotIndex];
            if (!slot.IsEmpty && slot.AlbedoTexture != null)
            {
                var texRef = ImGuiExtension.GetTextureKey(slot.AlbedoTexture);
                drawList.AddImage(texRef, segMin, segMax);
            }
            else
            {
                Vector4 tint = new(
                    Math.Clamp(climate.DebugColor.X * 0.65f + FallbackFillTint.X * 0.35f, 0.0f, 1.0f),
                    Math.Clamp(climate.DebugColor.Y * 0.65f + FallbackFillTint.Y * 0.35f, 0.0f, 1.0f),
                    Math.Clamp(climate.DebugColor.Z * 0.65f + FallbackFillTint.Z * 0.35f, 0.0f, 1.0f),
                    0.95f);
                drawList.AddRectFilled(segMin, segMax, ImGui.ColorConvertFloat4ToU32(tint), rounding);
            }

            drawList.AddRect(segMin, segMax, ColorPalette.BorderLight.ToUint(), rounding);

            if (isSelected)
            {
                drawList.AddRect(
                    new Vector2(segMin.X + borderThickness * 0.5f, segMin.Y + borderThickness * 0.5f),
                    new Vector2(segMax.X - borderThickness * 0.5f, segMax.Y - borderThickness * 0.5f),
                    ColorPalette.Accent.ToUint(), rounding, ImDrawFlags.RoundCornersAll, borderThickness);
            }

            if (isHovered && !isSelected)
            {
                drawList.AddRectFilled(segMin, segMax, ImGui.ColorConvertFloat4ToU32(SegmentHoverTint), rounding);
            }

            xOffset += buttonWidth + segmentSpacing;
        }

        xOffset = 0f;
        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var segMin = new Vector2(rowStart.X + xOffset, rowStart.Y);
            ImGui.SetCursorScreenPos(segMin);
            ImGui.PushID($"##gradient_seg_{i}");
            if (ImGui.InvisibleButton("", new Vector2(buttonWidth, buttonHeight)))
            {
                int globalIndex = climateState.GetRuleGlobalIndex(rule);
                if (globalIndex >= 0)
                {
                    state.SelectedRuleIndex = globalIndex;
                    RuleSelected?.Invoke(this, EventArgs.Empty);
                }
            }

            if (ImGui.IsItemHovered())
            {
                int localIndex = climateState.GetRuleLocalIndex(rule) + 1;
                ImGui.SetTooltip($"Rule {localIndex}\nHeight: {rule.MinAltitude:0.##} - {rule.MaxAltitude:0.##}\nSlope: {rule.MinSlopeDegrees:0.#} - {rule.MaxSlopeDegrees:0.#}");
            }
            ImGui.PopID();

            xOffset += buttonWidth + segmentSpacing;
        }

        if (totalWidth > availableWidth)
        {
            ImGui.SetCursorScreenPos(new Vector2(rowStart.X, rowStart.Y + buttonHeight + EditorStyle.ScaleValue(4.0f)));
            return;
        }

        ImGui.SetCursorScreenPos(new Vector2(rowStart.X, rowStart.Y + buttonHeight + EditorStyle.ScaleValue(4.0f)));
    }
}
