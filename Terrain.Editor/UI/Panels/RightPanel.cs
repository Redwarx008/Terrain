#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.Services;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// Hosts the mode-specific inspector content. Only the active workflow's tools are
/// visible so SculptMode and ClimateEdit never leak controls into each other.
/// </summary>
public sealed class RightPanel : PanelBase
{
    private readonly SculptModePanel sculptModePanel = new();
    private readonly ClimateManagerPanel climateManagerPanel = new();
    private readonly RuleManagerPanel ruleManagerPanel = new();

    private TerrainManager? terrainManager;

    public EditorMode CurrentMode { get; set; } = EditorMode.Sculpt;

    public event EventHandler? RulesChanged;
    public event EventHandler? ClimateMaskChanged;

    public RightPanel()
    {
        Title = "Inspector";
        ShowTitleBar = false;

        climateManagerPanel.ClimateMaskChanged += (s, e) => ClimateMaskChanged?.Invoke(this, EventArgs.Empty);
        ruleManagerPanel.RulesChanged += (s, e) => RulesChanged?.Invoke(this, EventArgs.Empty);
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
                RenderClimateTabs();
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

    private void RenderClimateTabs()
    {
        if (ImGui.BeginTabBar("##mode_tabs_climate", ImGuiTabBarFlags.None))
        {
            if (ImGui.BeginTabItem("Climate Manager"))
            {
                climateManagerPanel.Render();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Rule Manager"))
            {
                ruleManagerPanel.Render();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }
}
