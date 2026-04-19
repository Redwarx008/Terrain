#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Linq;
using System.Numerics;
using Terrain.Editor.Services;
using Terrain.Editor.UI.Controls;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// Hosts the mode-specific inspector content. Only the active workflow's tools are
/// visible so SculptMode and ClimateEdit never leak controls into each other.
/// </summary>
public sealed class RightPanel : PanelBase
{
    private readonly SculptModePanel sculptModePanel = new();
    private readonly ClimateGradientBar climateGradientBar = new();
    private readonly RuleInspectorPanel ruleInspectorPanel = new();

    private TerrainManager? terrainManager;

    public EditorMode CurrentMode { get; set; } = EditorMode.Sculpt;

    public event EventHandler? RulesChanged;
    public event EventHandler? ClimateMaskChanged;

    public RightPanel()
    {
        Title = "Inspector";
        ShowTitleBar = false;

        climateGradientBar.RuleSelected += (s, e) => RulesChanged?.Invoke(this, EventArgs.Empty);
        ruleInspectorPanel.RulesChanged += (s, e) => RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTerrainManager(TerrainManager? manager)
    {
        terrainManager = manager;
    }

    protected override void RenderContent()
    {
        ImGui.SetCursorScreenPos(new Vector2(ContentRect.X, ContentRect.Y));
        if (!ImGui.BeginChild($"##mode_panel_{Id}", new Vector2(ContentRect.Width, ContentRect.Height), ImGuiChildFlags.None))
        {
            ImGui.EndChild();
            return;
        }

        switch (CurrentMode)
        {
            case EditorMode.Sculpt:
                RenderSingleModeTab("SculptMode", sculptModePanel.Render);
                break;

            case EditorMode.Paint:
                RenderClimatePanel();
                break;

            default:
                ImGui.TextDisabled("No inspector available for this mode.");
                break;
        }

        ImGui.EndChild();
    }

    private static void RenderSingleModeTab(string title, Action renderContent)
    {
        if (ImGui.BeginTabBar("##mode_tabs_single", ImGuiTabBarFlags.None))
        {
            if (ImGui.BeginTabItem(title))
            {
                renderContent();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void RenderClimatePanel()
    {
        EnsureClimateSelection();
        RenderClimateSelector();
        ImGui.Spacing();
        climateGradientBar.Render();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ruleInspectorPanel.Render();
    }

    private static void RenderClimateSelector()
    {
        var climateState = ClimateRuleService.Instance;
        var state = EditorState.Instance;

        var currentClimate = climateState.FindClimate(state.CurrentClimateId);
        string climateLabel = currentClimate?.Name ?? "Select Climate";

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "CLIMATE:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(MathF.Max(EditorStyle.ScaleValue(180.0f), ImGui.GetContentRegionAvail().X));

        if (ImGui.BeginCombo("##climate_select", climateLabel))
        {
            foreach (var climate in climateState.Climates)
            {
                bool isSelected = climate.Id == state.CurrentClimateId;
                if (ImGui.Selectable($"{climate.Name}##climate_{climate.Id}", isSelected))
                {
                    state.CurrentClimateId = climate.Id;
                    var firstRule = climateState.GetRulesForClimate(climate.Id).FirstOrDefault();
                    state.SelectedRuleIndex = climateState.GetRuleGlobalIndex(firstRule);
                    state.HasSelectedTool = true;
                }
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }

    private static void EnsureClimateSelection()
    {
        var climateState = ClimateRuleService.Instance;
        var state = EditorState.Instance;

        if (climateState.Climates.Count == 0)
            return;

        if (climateState.FindClimate(state.CurrentClimateId) == null)
            state.CurrentClimateId = climateState.Climates[0].Id;

        ClimateRuleLayer? selectedRule = climateState.GetRuleByGlobalIndex(state.SelectedRuleIndex);
        if (selectedRule == null || selectedRule.ClimateId != state.CurrentClimateId)
        {
            var firstRule = climateState.GetRulesForClimate(state.CurrentClimateId).FirstOrDefault();
            state.SelectedRuleIndex = climateState.GetRuleGlobalIndex(firstRule);
        }
    }
}
