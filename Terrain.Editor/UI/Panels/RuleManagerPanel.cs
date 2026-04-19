#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.Services;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

internal sealed class RuleManagerPanel
{
    public event EventHandler? RulesChanged;

    public void Render()
    {
        var rules = ClimateRuleService.Instance;

        if (ImGui.Button("[+] Add Rule Layer##rule_add", new Vector2(-1, EditorStyle.ScaleValue(30.0f))))
        {
            rules.AddRule();
            EditorState.Instance.SelectedRuleIndex = rules.Rules.Count - 1;
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }

        ImGui.Spacing();
        ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), "Rule Stack");
        ImGui.Spacing();

        for (int i = 0; i < rules.Rules.Count; i++)
        {
            RenderRuleRow(rules, i, rules.Rules[i]);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        RenderRuleProperties(rules);
    }

    private void RenderRuleRow(ClimateRuleService service, int index, ClimateRuleLayer rule)
    {
        bool selected = EditorState.Instance.SelectedRuleIndex == index;
        Vector2 rowStart = ImGui.GetCursorScreenPos();
        float rowHeight = EditorStyle.ScaleValue(34.0f);
        float rowWidth = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(
            rowStart,
            new Vector2(rowStart.X + rowWidth, rowStart.Y + rowHeight),
            selected ? ColorPalette.Selection.ToUint() : ColorPalette.DarkBackground.ToUint(),
            EditorStyle.ScaleValue(4.0f));
        drawList.AddRect(
            rowStart,
            new Vector2(rowStart.X + rowWidth, rowStart.Y + rowHeight),
            ColorPalette.Border.ToUint(),
            EditorStyle.ScaleValue(4.0f));

        ImGui.SetCursorScreenPos(rowStart);
        if (ImGui.InvisibleButton($"##rule_row_{index}", new Vector2(rowWidth, rowHeight)))
            EditorState.Instance.SelectedRuleIndex = index;

        drawList.AddText(
            new Vector2(rowStart.X + EditorStyle.ScaleValue(8.0f), rowStart.Y + EditorStyle.ScaleValue(9.0f)),
            ColorPalette.TextSecondary.ToUint(),
            "[R]");
        drawList.AddText(
            new Vector2(rowStart.X + EditorStyle.ScaleValue(28.0f), rowStart.Y + EditorStyle.ScaleValue(9.0f)),
            ColorPalette.TextPrimary.ToUint(),
            rule.Name);

        float buttonY = rowStart.Y + EditorStyle.ScaleValue(5.0f);
        float buttonWidth = EditorStyle.ScaleValue(24.0f);
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + rowWidth - buttonWidth * 2.0f - EditorStyle.ScaleValue(12.0f), buttonY));
        if (ImGui.Button($"{Icons.ChevronUp}##rule_up_{index}", new Vector2(buttonWidth, EditorStyle.ScaleValue(24.0f))))
        {
            service.MoveRule(index, Math.Max(0, index - 1));
            EditorState.Instance.SelectedRuleIndex = Math.Max(0, index - 1);
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }

        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + rowWidth - buttonWidth - EditorStyle.ScaleValue(8.0f), buttonY));
        if (ImGui.Button($"{Icons.ChevronDown}##rule_down_{index}", new Vector2(buttonWidth, EditorStyle.ScaleValue(24.0f))))
        {
            service.MoveRule(index, Math.Min(service.Rules.Count - 1, index + 1));
            EditorState.Instance.SelectedRuleIndex = Math.Min(service.Rules.Count - 1, index + 1);
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }

        ImGui.SetCursorScreenPos(new Vector2(rowStart.X, rowStart.Y + rowHeight + EditorStyle.ScaleValue(4.0f)));
    }

    private void RenderRuleProperties(ClimateRuleService service)
    {
        ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), "Rule Properties");
        int selectedIndex = EditorState.Instance.SelectedRuleIndex;
        if (selectedIndex < 0 || selectedIndex >= service.Rules.Count)
        {
            ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "Select a rule layer to edit.");
            return;
        }

        ClimateRuleLayer rule = service.Rules[selectedIndex];

        ImGui.Text("Selected Layer");
        ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), rule.Name);
        ImGui.Spacing();

        ImGui.Text("Material");
        if (ImGui.BeginCombo("##rule_material", FormatMaterialLabel(rule.MaterialSlotIndex)))
        {
            foreach (var slot in MaterialSlotManager.Instance.GetActiveSlots())
            {
                bool isSelected = slot.Index == rule.MaterialSlotIndex;
                if (ImGui.Selectable($"{slot.Name} (#{slot.Index})", isSelected))
                {
                    rule.MaterialSlotIndex = slot.Index;
                    service.NotifyMutated();
                    RulesChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();
        ImGui.Text("Climate Index");
        int climateId = rule.ClimateId;
        if (ImGui.InputInt("##rule_climate_id", ref climateId))
        {
            rule.ClimateId = Math.Max(0, climateId);
            service.NotifyMutated();
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }

        ImGui.Spacing();
        ImGui.Text("Altitude (m)");
        Vector2 altitudeRange = new(rule.MinAltitude, rule.MaxAltitude);
        if (ImGui.SliderFloat2("##rule_altitude", ref altitudeRange, 0.0f, 3000.0f, "%.0f"))
        {
            rule.MinAltitude = MathF.Min(altitudeRange.X, altitudeRange.Y);
            rule.MaxAltitude = MathF.Max(altitudeRange.X, altitudeRange.Y);
            service.NotifyMutated();
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }

        ImGui.Spacing();
        ImGui.Text("Max Slope (deg)");
        float maxSlope = rule.MaxSlopeDegrees;
        if (ImGui.SliderFloat("##rule_max_slope", ref maxSlope, 0.0f, 90.0f, "%.0f"))
        {
            rule.MaxSlopeDegrees = maxSlope;
            service.NotifyMutated();
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string FormatMaterialLabel(int slotIndex)
    {
        var slot = MaterialSlotManager.Instance[slotIndex];
        return slot.IsEmpty ? $"Slot #{slotIndex}" : $"{slot.Name} (#{slotIndex})";
    }
}
