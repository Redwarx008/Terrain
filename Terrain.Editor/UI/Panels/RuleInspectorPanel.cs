#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Terrain.Editor.Services;
using Terrain.Editor.UI;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

internal sealed class RuleInspectorPanel
{
    private string textureSearchFilter = string.Empty;
    private string? activeRangeSliderId;
    private int activeRangeSliderHandle;

    public event EventHandler? RulesChanged;

    public void Render()
    {
        var service = ClimateRuleService.Instance;
        var state = EditorState.Instance;
        ClimateRuleLayer? rule = service.GetRuleByGlobalIndex(state.SelectedRuleIndex);

        if (rule == null || rule.ClimateId != state.CurrentClimateId)
        {
            ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "Select a rule button above to edit its properties.");
            return;
        }

        IReadOnlyList<ClimateRuleLayer> climateRules = service.GetRulesForClimate(state.CurrentClimateId);
        int localRuleIndex = service.GetRuleLocalIndex(rule);
        RenderHeader(service, state, rule, climateRules, localRuleIndex);
        ImGui.Spacing();
        RenderTextureRow(rule, service);
        ImGui.Spacing();
        RenderHeightRangeRow(rule, state.SelectedRuleIndex, service);
        ImGui.Spacing();
        RenderSlopeRangeRow(rule, service);
        ImGui.Spacing();
        RenderBlendRow(rule, service);
        ImGui.Spacing();
        RenderRuleOptionsRow(rule, service);
    }

    private void RenderHeader(
        ClimateRuleService service,
        EditorState state,
        ClimateRuleLayer rule,
        IReadOnlyList<ClimateRuleLayer> climateRules,
        int localRuleIndex)
    {
        string activeMaterialName = GetMaterialName(rule.MaterialSlotIndex);
        ImGui.PushFont(FontManager.Regular, EditorStyle.ScaleValue(18.0f));
        ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), "Rule Properties");
        ImGui.PopFont();
        ImGui.TextColored(
            ColorPalette.TextSecondary.ToVector4(),
            $"Active: Slot {localRuleIndex + 1} - {activeMaterialName}");

        float buttonWidth = EditorStyle.ScaleValue(30.0f);
        float buttonSpacing = EditorStyle.ScaleValue(6.0f);
        float totalButtonWidth = buttonWidth * 2.0f + buttonSpacing;
        float startX = ImGui.GetCursorPosX() + MathF.Max(0.0f, ImGui.GetContentRegionAvail().X - totalButtonWidth);

        ImGui.SetCursorPosX(startX);
        if (ImGui.Button("+##rule_add", new Vector2(buttonWidth, 0.0f)))
        {
            ClimateRuleLayer newRule = service.AddRule(state.CurrentClimateId);
            state.SelectedRuleIndex = service.GetRuleGlobalIndex(newRule);
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }

        ImGui.SameLine();
        if (ImGui.Button("-##rule_remove", new Vector2(buttonWidth, 0.0f)))
        {
            int currentIndex = state.SelectedRuleIndex;
            service.RemoveRuleAt(currentIndex);

            ClimateRuleLayer? replacementRule = climateRules.ElementAtOrDefault(Math.Max(0, localRuleIndex - 1))
                ?? service.GetRulesForClimate(state.CurrentClimateId).FirstOrDefault();
            state.SelectedRuleIndex = service.GetRuleGlobalIndex(replacementRule);
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RenderTextureRow(ClimateRuleLayer rule, ClimateRuleService service)
    {
        RenderFieldLabel("Texture");
        ImGui.Spacing();

        var currentSlot = MaterialSlotManager.Instance[rule.MaterialSlotIndex];
        string currentLabel = currentSlot.IsEmpty ? $"Slot #{rule.MaterialSlotIndex}" : currentSlot.Name;
        float previewSize = EditorStyle.ScaleValue(28.0f);

        DrawSlotThumbnail(currentSlot, ImGui.GetCursorScreenPos(), previewSize, EditorStyle.ScaleValue(4.0f));
        ImGui.Dummy(new Vector2(previewSize, previewSize));
        ImGui.SameLine();

        ImGui.PushItemWidth(MathF.Max(EditorStyle.ScaleValue(120.0f), ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##rule_material", currentLabel))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##texture_search", ref textureSearchFilter, 128);
            ImGui.Separator();

            List<MaterialSlot> slots = MaterialSlotManager.Instance.GetActiveSlots()
                .Where(slot => string.IsNullOrWhiteSpace(textureSearchFilter)
                    || slot.Name.Contains(textureSearchFilter, StringComparison.OrdinalIgnoreCase)
                    || slot.Index.ToString().Contains(textureSearchFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (MaterialSlot slot in slots)
            {
                bool isSelected = slot.Index == rule.MaterialSlotIndex;
                Vector2 rowStart = ImGui.GetCursorScreenPos();
                float rowHeight = EditorStyle.ScaleValue(30.0f);

                DrawSlotThumbnail(slot, rowStart, EditorStyle.ScaleValue(22.0f), EditorStyle.ScaleValue(3.0f));
                ImGui.SetCursorScreenPos(new Vector2(rowStart.X + EditorStyle.ScaleValue(30.0f), rowStart.Y));
                if (ImGui.Selectable($"{slot.Name} (#{slot.Index})##slot_{slot.Index}", isSelected, ImGuiSelectableFlags.None, new Vector2(0.0f, rowHeight)))
                {
                    rule.MaterialSlotIndex = slot.Index;
                    service.NotifyMutated();
                    RulesChanged?.Invoke(this, EventArgs.Empty);
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            if (slots.Count == 0)
                ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "No matching textures.");

            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();
    }

    private void RenderHeightRangeRow(
        ClimateRuleLayer rule,
        int selectedRuleIndex,
        ClimateRuleService service)
    {
        RenderFieldLabel("Height Range");
        ImGui.Spacing();
        float contentWidth = MathF.Max(EditorStyle.ScaleValue(120.0f), ImGui.GetContentRegionAvail().X);

        float sliderMin = ClimateRuleService.MinHeight;
        float sliderMax = MathF.Max(
            ClimateRuleService.DefaultMaxHeight,
            MathF.Max(rule.MaxAltitude, rule.MinAltitude + 100.0f));

        float minValue = rule.MinAltitude;
        float maxValue = rule.MaxAltitude;
        if (RenderDualHandleSlider("height_range", ref minValue, ref maxValue, sliderMin, sliderMax, contentWidth))
        {
            service.SetRuleAltitude(selectedRuleIndex, minValue, maxValue);
            service.NotifyMutated();
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }

        ImGui.TextColored(
            ColorPalette.TextPrimary.ToVector4(),
            $"Range: {rule.MinAltitude:0.##} - {rule.MaxAltitude:0.##}");
    }

    private void RenderSlopeRangeRow(ClimateRuleLayer rule, ClimateRuleService service)
    {
        RenderFieldLabel("Slope Range");
        ImGui.Spacing();
        float contentWidth = MathF.Max(EditorStyle.ScaleValue(120.0f), ImGui.GetContentRegionAvail().X);

        float minValue = rule.MinSlopeDegrees;
        float maxValue = rule.MaxSlopeDegrees;
        if (RenderDualHandleSlider("slope_range", ref minValue, ref maxValue, 0.0f, 90.0f, contentWidth))
        {
            rule.MinSlopeDegrees = minValue;
            rule.MaxSlopeDegrees = maxValue;
            service.NotifyMutated();
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }

        ImGui.TextColored(
            ColorPalette.TextPrimary.ToVector4(),
            $"Range: {rule.MinSlopeDegrees:0.#}\u00b0 - {rule.MaxSlopeDegrees:0.#}\u00b0");
    }

    private void RenderBlendRow(ClimateRuleLayer rule, ClimateRuleService service)
    {
        RenderFieldLabel("Blend Range");
        ImGui.Spacing();
        float contentWidth = MathF.Max(EditorStyle.ScaleValue(120.0f), ImGui.GetContentRegionAvail().X);
        float maxBlend = MathF.Max(rule.MaxAltitude - rule.MinAltitude, 0.15f);

        float blend = rule.BlendRange;
        ImGui.PushItemWidth(contentWidth);
        if (ImGui.SliderFloat("##blend_range", ref blend, 0.0f, maxBlend, "%.2f"))
        {
            rule.BlendRange = Math.Clamp(blend, 0.0f, maxBlend);
            service.NotifyMutated();
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
        ImGui.PopItemWidth();

        ImGui.TextColored(
            ColorPalette.TextPrimary.ToVector4(),
            $"Blend (Softness): {rule.BlendRange:0.00}");
    }

    private void RenderRuleOptionsRow(ClimateRuleLayer rule, ClimateRuleService service)
    {
        RenderFieldLabel("Options");
        ImGui.Spacing();

        bool enabled = rule.Enabled;
        if (ImGui.Checkbox("Enabled##rule_enabled", ref enabled))
        {
            rule.Enabled = enabled;
            service.NotifyMutated();
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string GetMaterialName(int slotIndex)
    {
        MaterialSlot slot = MaterialSlotManager.Instance[slotIndex];
        return slot.IsEmpty ? $"Slot #{slotIndex}" : slot.Name;
    }

    private static void RenderFieldLabel(string label)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.PushFont(FontManager.Regular, EditorStyle.ScaleValue(17.0f));
        ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), label);
        ImGui.PopFont();
    }

    private static void DrawSlotThumbnail(MaterialSlot slot, Vector2 topLeft, float size, float cornerRounding)
    {
        var drawList = ImGui.GetWindowDrawList();
        Vector2 bottomRight = new(topLeft.X + size, topLeft.Y + size);

        if (!slot.IsEmpty && slot.AlbedoTexture != null)
        {
            drawList.AddRectFilled(topLeft, bottomRight, ColorPalette.DarkBackground.ToUint(), cornerRounding);
            drawList.AddImage(ImGuiExtension.GetTextureKey(slot.AlbedoTexture), topLeft, bottomRight);
        }
        else
        {
            drawList.AddRectFilled(topLeft, bottomRight, ColorPalette.DarkBackground.ToUint(), cornerRounding);
        }

        drawList.AddRect(topLeft, bottomRight, ColorPalette.BorderLight.ToUint(), cornerRounding);
    }

    private bool RenderDualHandleSlider(string id, ref float minValue, ref float maxValue, float minLimit, float maxLimit, float width)
    {
        float trackHeight = EditorStyle.ScaleValue(26.0f);
        float visualTrackHeight = EditorStyle.ScaleValue(6.0f);
        float handleRadius = EditorStyle.ScaleValue(7.0f);
        float grabPadding = EditorStyle.ScaleValue(10.0f);
        Vector2 start = ImGui.GetCursorScreenPos();
        Vector2 size = new(width, trackHeight);

        ImGui.InvisibleButton($"##{id}", size);
        bool isHovered = ImGui.IsItemHovered();
        bool isActive = ImGui.IsItemActive();
        var drawList = ImGui.GetWindowDrawList();

        Vector2 trackMin = new(start.X + grabPadding, start.Y + (trackHeight - visualTrackHeight) * 0.5f);
        Vector2 trackMax = new(start.X + size.X - grabPadding, trackMin.Y + visualTrackHeight);
        float trackWidth = MathF.Max(1.0f, trackMax.X - trackMin.X);

        float minT = Math.Clamp((minValue - minLimit) / MathF.Max(0.0001f, maxLimit - minLimit), 0.0f, 1.0f);
        float maxT = Math.Clamp((maxValue - minLimit) / MathF.Max(0.0001f, maxLimit - minLimit), 0.0f, 1.0f);
        float minX = trackMin.X + trackWidth * minT;
        float maxX = trackMin.X + trackWidth * maxT;
        float centerY = start.Y + trackHeight * 0.5f;

        if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            float mouseX = ImGui.GetIO().MousePos.X;
            activeRangeSliderId = id;
            activeRangeSliderHandle = MathF.Abs(mouseX - minX) <= MathF.Abs(mouseX - maxX) ? 1 : 2;
        }

        bool changed = false;
        if (activeRangeSliderId == id)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                activeRangeSliderId = null;
                activeRangeSliderHandle = 0;
            }
            else
            {
                float mouseT = Math.Clamp((ImGui.GetIO().MousePos.X - trackMin.X) / trackWidth, 0.0f, 1.0f);
                float mouseValue = minLimit + (maxLimit - minLimit) * mouseT;

                if (activeRangeSliderHandle == 1)
                {
                    float nextMin = MathF.Min(mouseValue, maxValue);
                    changed = MathF.Abs(nextMin - minValue) > 0.0001f;
                    minValue = nextMin;
                }
                else if (activeRangeSliderHandle == 2)
                {
                    float nextMax = MathF.Max(mouseValue, minValue);
                    changed = MathF.Abs(nextMax - maxValue) > 0.0001f;
                    maxValue = nextMax;
                }

                minT = Math.Clamp((minValue - minLimit) / MathF.Max(0.0001f, maxLimit - minLimit), 0.0f, 1.0f);
                maxT = Math.Clamp((maxValue - minLimit) / MathF.Max(0.0001f, maxLimit - minLimit), 0.0f, 1.0f);
                minX = trackMin.X + trackWidth * minT;
                maxX = trackMin.X + trackWidth * maxT;
            }
        }

        drawList.AddRectFilled(trackMin, trackMax, ColorPalette.DarkBackground.ToUint(), visualTrackHeight * 0.5f);
        drawList.AddRectFilled(
            new Vector2(minX, trackMin.Y),
            new Vector2(maxX, trackMax.Y),
            ColorPalette.Accent.ToUint(),
            visualTrackHeight * 0.5f);

        uint handleColor = isActive || isHovered
            ? ColorPalette.TextPrimary.ToUint()
            : ColorPalette.BorderLight.ToUint();
        drawList.AddCircleFilled(new Vector2(minX, centerY), handleRadius, handleColor);
        drawList.AddCircleFilled(new Vector2(maxX, centerY), handleRadius, handleColor);
        drawList.AddCircle(new Vector2(minX, centerY), handleRadius, ColorPalette.PanelBackground.ToUint(), 0, EditorStyle.ScaleValue(2.0f));
        drawList.AddCircle(new Vector2(maxX, centerY), handleRadius, ColorPalette.PanelBackground.ToUint(), 0, EditorStyle.ScaleValue(2.0f));

        return changed;
    }
}
