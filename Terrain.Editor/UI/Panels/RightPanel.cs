#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Linq;
using System.Numerics;
using Terrain.Editor.Services;
using Terrain.Editor.UI;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// Hosts the active workflow inspector. Paint mode now follows a biome/layer/modifier
/// terrain painter layout instead of the legacy gradient + rule editor flow.
/// </summary>
public sealed class RightPanel : PanelBase
{
    private readonly SculptModePanel sculptModePanel = new();

    private TerrainManager? terrainManager;
    private string layerNameBuffer = string.Empty;
    private string modifierNameBuffer = string.Empty;

    public EditorMode CurrentMode { get; set; } = EditorMode.Sculpt;

    public event EventHandler? RulesChanged;
    public event EventHandler? ClimateMaskChanged;

    public RightPanel()
    {
        Title = "Inspector";
        ShowTitleBar = false;
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
                RenderTerrainPainter();
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

    private void RenderTerrainPainter()
    {
        EnsurePainterSelection();
        var state = EditorState.Instance;

        if (ImGui.BeginTabBar("##terrain_painter_tabs", ImGuiTabBarFlags.None))
        {
            bool openLayers = ImGui.BeginTabItem("Layers");
            if (openLayers)
            {
                state.CurrentTerrainPainterTab = TerrainPainterTab.Layers;
                RenderLayersTab();
                ImGui.EndTabItem();
            }

            bool openSettings = ImGui.BeginTabItem("Settings");
            if (openSettings)
            {
                state.CurrentTerrainPainterTab = TerrainPainterTab.Settings;
                RenderSettingsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void RenderLayersTab()
    {
        ClimateRuleService service = ClimateRuleService.Instance;
        EditorState state = EditorState.Instance;
        ClimateRuleLayer? layer = service.GetRuleByGlobalIndex(state.SelectedRuleIndex);

        RenderBiomeSelector();
        ImGui.Spacing();

        RenderLayerSection(service, state);
        layer = service.GetRuleByGlobalIndex(state.SelectedRuleIndex);
        if (layer == null)
        {
            ImGui.TextDisabled("No layer selected.");
            return;
        }

        ImGui.Spacing();
        RenderModifierSection(layer, service, state);
        ImGui.Spacing();
        RenderModifierInspectorSection(layer, service, state);
    }

    private void RenderSettingsTab()
    {
        EditorState state = EditorState.Instance;

        ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), "Painter Settings");
        ImGui.Separator();

        bool heatmap = state.HeatmapEnabled;
        if (ImGui.Checkbox("Heatmap Preview", ref heatmap))
        {
            state.HeatmapEnabled = heatmap;
            state.CurrentDebugViewMode = heatmap ? SceneDebugViewMode.LayerHeatmap : SceneDebugViewMode.FinalOutput;
        }

        bool editLayerMode = state.EditLayerMode;
        if (ImGui.Checkbox("Edit Layer Mode", ref editLayerMode))
        {
            state.EditLayerMode = editLayerMode;
        }

        ImGui.Spacing();
        ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), "Debug View");

        RenderDebugRadio("Final Output", SceneDebugViewMode.FinalOutput);
        RenderDebugRadio("Layer Heatmap", SceneDebugViewMode.LayerHeatmap);
        RenderDebugRadio("Detail Index", SceneDebugViewMode.DetailIndexMap);
        RenderDebugRadio("Detail Weight", SceneDebugViewMode.DetailWeightMap);

        ImGui.Spacing();
        ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), terrainManager?.HasTerrainLoaded == true
            ? "Control map generation is GPU-driven and updates when layer data changes."
            : "Load a terrain to start editing biomes, layers, and modifiers.");
    }

    private static void RenderDebugRadio(string label, SceneDebugViewMode mode)
    {
        EditorState state = EditorState.Instance;
        bool selected = state.CurrentDebugViewMode == mode;
        if (ImGui.RadioButton(label, selected))
        {
            state.CurrentDebugViewMode = mode;
            state.HeatmapEnabled = mode == SceneDebugViewMode.LayerHeatmap;
        }
    }

    private void RenderBiomeSelector()
    {
        ClimateRuleService service = ClimateRuleService.Instance;
        EditorState state = EditorState.Instance;
        ClimateDefinition? biome = service.FindBiome(state.CurrentClimateId);
        string currentLabel = biome?.Name ?? "Select Biome";

        ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "Biome");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##biome_select", currentLabel))
        {
            foreach (ClimateDefinition entry in service.Biomes)
            {
                bool selected = entry.Id == state.CurrentClimateId;
                if (ImGui.Selectable($"{entry.Name}##biome_{entry.Id}", selected))
                {
                    state.CurrentClimateId = entry.Id;
                    ClimateRuleLayer? firstLayer = service.GetLayersForBiome(entry.Id).FirstOrDefault();
                    state.SelectedRuleIndex = service.GetRuleGlobalIndex(firstLayer);
                    state.SelectedModifierIndex = 0;
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }
    }

    private void RenderLayerSection(ClimateRuleService service, EditorState state)
    {
        var layers = service.GetLayersForBiome(state.CurrentClimateId).ToList();
        RenderSectionHeader("1.", "Layers");
        bool addLayerRequested = false;
        bool removeLayerRequested = false;
        bool duplicateLayerRequested = false;
        int layerMoveDelta = 0;
        int? dragLayerFrom = null;
        int? dragLayerTo = null;

        if (ImGui.BeginChild("##biome_layers_section", new Vector2(0.0f, EditorStyle.ScaleValue(230.0f)), ImGuiChildFlags.Borders))
        {
            if (ImGui.BeginTable("##biome_layers_table", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Drag", ImGuiTableColumnFlags.WidthFixed, EditorStyle.ScaleValue(26.0f));
                ImGui.TableSetupColumn("Show", ImGuiTableColumnFlags.WidthFixed, EditorStyle.ScaleValue(28.0f));
                ImGui.TableSetupColumn("Mat", ImGuiTableColumnFlags.WidthFixed, EditorStyle.ScaleValue(42.0f));
                ImGui.TableSetupColumn("Layer");
                ImGui.TableSetupColumn("Pick", ImGuiTableColumnFlags.WidthFixed, EditorStyle.ScaleValue(32.0f));

                foreach (ClimateRuleLayer layer in layers)
                {
                    bool selected = service.GetRuleGlobalIndex(layer) == state.SelectedRuleIndex;
                    ImGui.PushID(layer.Id);
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextDisabled("==");
                    BeginLayerDragDrop(service, state, layer);
                    dragLayerFrom ??= AcceptLayerDragDrop(service, layer, out dragLayerTo);

                    ImGui.TableSetColumnIndex(1);
                    bool visible = layer.Visible;
                    if (ImGui.Checkbox("##visible", ref visible))
                    {
                        layer.Visible = visible;
                        NotifyRulesMutated(service);
                    }

                    ImGui.TableSetColumnIndex(2);
                    RenderMaterialSwatch(layer.MaterialSlotIndex);

                    ImGui.TableSetColumnIndex(3);
                    if (ImGui.Selectable($"{layer.Name}##layer_select", selected, ImGuiSelectableFlags.SpanAllColumns))
                    {
                        state.SelectedRuleIndex = service.GetRuleGlobalIndex(layer);
                        state.SelectedModifierIndex = 0;
                        layerNameBuffer = layer.Name;
                    }

                    ImGui.TableSetColumnIndex(4);
                    if (ImGui.RadioButton("##selected", selected))
                    {
                        state.SelectedRuleIndex = service.GetRuleGlobalIndex(layer);
                        state.SelectedModifierIndex = 0;
                        layerNameBuffer = layer.Name;
                    }

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
            ImGui.Separator();

            EditorStyle.PushCompact();
            RenderHeatmapToggle();
            ImGui.SameLine();
            RenderEditLayerToggle();
            ImGui.SameLine();
            if (ImGui.Button("Copy"))
            {
                duplicateLayerRequested = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("+"))
            {
                addLayerRequested = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("-"))
            {
                removeLayerRequested = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("Up"))
                layerMoveDelta = -1;
            ImGui.SameLine();
            if (ImGui.Button("Down"))
                layerMoveDelta = 1;
            EditorStyle.PopCompact();

            ClimateRuleLayer? selectedLayer = service.GetRuleByGlobalIndex(state.SelectedRuleIndex);
            if (selectedLayer != null)
            {
                ImGui.Spacing();
                if (string.IsNullOrEmpty(layerNameBuffer) || layerNameBuffer != selectedLayer.Name)
                    layerNameBuffer = selectedLayer.Name;

                if (ImGui.InputText("Layer Name", ref layerNameBuffer, 128))
                {
                    selectedLayer.Name = string.IsNullOrWhiteSpace(layerNameBuffer) ? $"Layer {selectedLayer.Id}" : layerNameBuffer;
                    NotifyRulesMutated(service);
                }

                RenderLayerMaterialCombo(selectedLayer, service);
            }
        }

        ImGui.EndChild();

        if (dragLayerFrom.HasValue && dragLayerTo.HasValue && dragLayerFrom.Value != dragLayerTo.Value)
        {
            service.MoveRule(dragLayerFrom.Value, dragLayerTo.Value);
            state.SelectedRuleIndex = dragLayerTo.Value;
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (addLayerRequested)
        {
            ClimateRuleLayer layer = service.AddLayer(state.CurrentClimateId);
            state.SelectedRuleIndex = service.GetRuleGlobalIndex(layer);
            state.SelectedModifierIndex = 0;
            layerNameBuffer = layer.Name;
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (duplicateLayerRequested)
        {
            DuplicateSelectedLayer(service, state);
        }
        else if (removeLayerRequested)
        {
            int selectedIndex = state.SelectedRuleIndex;
            service.RemoveRuleAt(selectedIndex);
            ClimateRuleLayer? replacement = service.GetLayersForBiome(state.CurrentClimateId).FirstOrDefault();
            state.SelectedRuleIndex = service.GetRuleGlobalIndex(replacement);
            state.SelectedModifierIndex = 0;
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (layerMoveDelta != 0)
        {
            MoveSelectedLayer(service, state, layerMoveDelta);
        }
    }

    private void RenderLayerMaterialCombo(ClimateRuleLayer layer, ClimateRuleService service)
    {
        MaterialSlot currentSlot = MaterialSlotManager.Instance[layer.MaterialSlotIndex];
        string currentLabel = currentSlot.IsEmpty ? $"Slot #{layer.MaterialSlotIndex}" : $"{currentSlot.Name} (#{layer.MaterialSlotIndex})";
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("Material", currentLabel))
        {
            foreach (MaterialSlot slot in MaterialSlotManager.Instance.GetActiveSlots())
            {
                bool selected = slot.Index == layer.MaterialSlotIndex;
                string label = $"{slot.Name} (#{slot.Index})";
                if (ImGui.Selectable(label, selected))
                {
                    layer.MaterialSlotIndex = slot.Index;
                    NotifyRulesMutated(service);
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }
    }

    private void RenderModifierSection(ClimateRuleLayer layer, ClimateRuleService service, EditorState state)
    {
        EnsureModifierSelection(layer, state);
        RenderSectionHeader("2.", "Modifier Stack");
        bool addModifierRequested = false;
        bool removeModifierRequested = false;
        int modifierMoveDelta = 0;
        int? dragModifierFrom = null;
        int? dragModifierTo = null;
        BiomeModifierType? pendingNewModifierType = null;

        if (ImGui.BeginChild("##modifier_stack_section", new Vector2(0.0f, EditorStyle.ScaleValue(230.0f)), ImGuiChildFlags.Borders))
        {
            if (ImGui.BeginTable("##modifier_stack_table", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Drag", ImGuiTableColumnFlags.WidthFixed, EditorStyle.ScaleValue(26.0f));
                ImGui.TableSetupColumn("Show", ImGuiTableColumnFlags.WidthFixed, EditorStyle.ScaleValue(28.0f));
                ImGui.TableSetupColumn("Modifier", ImGuiTableColumnFlags.WidthStretch, 1.6f);
                ImGui.TableSetupColumn("Blend", ImGuiTableColumnFlags.WidthStretch, 1.0f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.7f);

                for (int i = 0; i < layer.Modifiers.Count; i++)
                {
                    BiomeModifier modifier = layer.Modifiers[i];
                    ImGui.PushID($"modifier_{modifier.Id}");
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextDisabled("==");
                    BeginModifierDragDrop(layer, service, state, i);
                    dragModifierFrom ??= AcceptModifierDragDrop(layer, i, out dragModifierTo);

                    ImGui.TableSetColumnIndex(1);
                    bool visible = modifier.Visible;
                    if (ImGui.Checkbox("##modifier_visible", ref visible))
                    {
                        modifier.Visible = visible;
                        NotifyRulesMutated(service);
                    }

                    ImGui.TableSetColumnIndex(2);
                    bool selected = state.SelectedModifierIndex == i;
                    if (ImGui.Selectable($"{GetModifierDisplayName(modifier)}##modifier_select", selected, ImGuiSelectableFlags.SpanAllColumns))
                    {
                        state.SelectedModifierIndex = i;
                        modifierNameBuffer = modifier.Name;
                    }

                    ImGui.TableSetColumnIndex(3);
                    RenderBlendModeCombo("##blend", modifier, service);

                    ImGui.TableSetColumnIndex(4);
                    int opacityPercent = (int)MathF.Round(modifier.Opacity * 100.0f);
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputInt("##opacity", ref opacityPercent))
                    {
                        modifier.Opacity = Math.Clamp(opacityPercent / 100.0f, 0.0f, 1.0f);
                        NotifyRulesMutated(service);
                    }

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
            ImGui.Separator();
            EditorStyle.PushCompact();
            if (ImGui.Button("+"))
                addModifierRequested = true;
            ImGui.SameLine();
            if (ImGui.Button("-"))
            {
                removeModifierRequested = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("Up##modifier"))
                modifierMoveDelta = -1;
            ImGui.SameLine();
            if (ImGui.Button("Down##modifier"))
                modifierMoveDelta = 1;
            EditorStyle.PopCompact();

            if (addModifierRequested)
                ImGui.OpenPopup("##add_modifier_popup");

            if (ImGui.BeginPopup("##add_modifier_popup"))
            {
                foreach (BiomeModifierType type in Enum.GetValues<BiomeModifierType>().Where(static type => type != BiomeModifierType.TextureMask))
                {
                    if (ImGui.Selectable(GetModifierTypeLabel(type)))
                    {
                        pendingNewModifierType = type;
                    }
                }

                ImGui.EndPopup();
            }
        }

        ImGui.EndChild();

        if (dragModifierFrom.HasValue && dragModifierTo.HasValue && dragModifierFrom.Value != dragModifierTo.Value)
        {
            service.MoveModifier(layer, dragModifierFrom.Value, dragModifierTo.Value);
            state.SelectedModifierIndex = dragModifierTo.Value;
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (pendingNewModifierType.HasValue)
        {
            BiomeModifier modifier = service.AddModifier(layer, pendingNewModifierType.Value);
            state.SelectedModifierIndex = layer.Modifiers.IndexOf(modifier);
            modifierNameBuffer = modifier.Name;
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (removeModifierRequested)
        {
            service.RemoveModifier(layer, state.SelectedModifierIndex);
            state.SelectedModifierIndex = Math.Clamp(state.SelectedModifierIndex, 0, Math.Max(layer.Modifiers.Count - 1, 0));
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (modifierMoveDelta != 0)
        {
            MoveSelectedModifier(layer, service, state, modifierMoveDelta);
        }
    }

    private void RenderModifierInspectorSection(ClimateRuleLayer layer, ClimateRuleService service, EditorState state)
    {
        EnsureModifierSelection(layer, state);
        RenderSectionHeader("3.", "Property Inspector");

        if (!ImGui.BeginChild("##modifier_inspector_section", new Vector2(0.0f, 0.0f), ImGuiChildFlags.Borders))
        {
            ImGui.EndChild();
            return;
        }

        if (state.SelectedModifierIndex < 0 || state.SelectedModifierIndex >= layer.Modifiers.Count)
        {
            ImGui.TextDisabled("No modifier selected.");
            ImGui.EndChild();
            return;
        }

        BiomeModifier modifier = layer.Modifiers[state.SelectedModifierIndex];
        if (string.IsNullOrEmpty(modifierNameBuffer) || modifierNameBuffer != modifier.Name)
            modifierNameBuffer = modifier.Name;

        ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), $"# {GetModifierTypeLabel(modifier.Type)}");
        ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), modifier.Name);
        ImGui.Separator();

        switch (modifier.Type)
        {
            case BiomeModifierType.HeightRange:
            case BiomeModifierType.SlopeRange:
            case BiomeModifierType.CurvatureRange:
                RenderRangeModifier(modifier, service);
                break;
            case BiomeModifierType.DirectionRange:
                RenderDirectionModifier(modifier, service);
                break;
            case BiomeModifierType.Noise:
                RenderNoiseModifier(modifier, service);
                break;
            case BiomeModifierType.TextureMask:
                RenderTextureMaskModifier(modifier, service);
                break;
        }

        ImGui.EndChild();
    }

    private static void RenderBlendModeCombo(string label, BiomeModifier modifier, ClimateRuleService service)
    {
        string currentBlend = modifier.BlendMode.ToString();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo(label, currentBlend))
        {
            foreach (BiomeModifierBlendMode blendMode in Enum.GetValues<BiomeModifierBlendMode>())
            {
                bool selected = blendMode == modifier.BlendMode;
                if (ImGui.Selectable(blendMode.ToString(), selected))
                {
                    modifier.BlendMode = blendMode;
                    service.NotifyMutated();
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

                ImGui.EndCombo();
        }
    }

    private static void RenderSectionHeader(string index, string title)
    {
        ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), $"{index} {title}");
    }

    private static void RenderHeatmapToggle()
    {
        EditorState state = EditorState.Instance;
        bool active = state.HeatmapEnabled;
        if (ImGui.Button(active ? "[*] Heatmap" : "[ ] Heatmap"))
        {
            state.HeatmapEnabled = !state.HeatmapEnabled;
            state.CurrentDebugViewMode = state.HeatmapEnabled ? SceneDebugViewMode.LayerHeatmap : SceneDebugViewMode.FinalOutput;
        }
    }

    private static void RenderEditLayerToggle()
    {
        EditorState state = EditorState.Instance;
        bool active = state.EditLayerMode;
        if (ImGui.Button(active ? "[*] Edit Layer" : "[ ] Edit Layer"))
            state.EditLayerMode = !state.EditLayerMode;
    }

    private static void RenderMaterialSwatch(int slotIndex)
    {
        var size = new Vector2(EditorStyle.ScaleValue(20.0f), EditorStyle.ScaleValue(20.0f));
        MaterialSlot slot = MaterialSlotManager.Instance[slotIndex];

        if (slot.AlbedoTexture != null)
        {
            ImGuiExtension.Image(slot.AlbedoTexture, size);
        }
        else
        {
            var color = GetMaterialSwatchColor(slotIndex);
            ImGui.ColorButton($"##swatch_{slotIndex}", color, ImGuiColorEditFlags.NoTooltip, size);
        }

        if (ImGui.IsItemHovered())
        {
            string label = slot.IsEmpty ? $"Slot #{slotIndex}" : $"{slot.Name} (#{slotIndex})";
            ImGui.SetTooltip(label);
        }
    }

    private static Vector4 GetMaterialSwatchColor(int slotIndex)
    {
        float hue = (slotIndex * 0.173f) % 1.0f;
        float r = 0.45f + 0.35f * MathF.Sin((hue + 0.00f) * MathF.Tau);
        float g = 0.45f + 0.35f * MathF.Sin((hue + 0.33f) * MathF.Tau);
        float b = 0.45f + 0.35f * MathF.Sin((hue + 0.66f) * MathF.Tau);
        return new Vector4(Math.Clamp(r, 0.0f, 1.0f), Math.Clamp(g, 0.0f, 1.0f), Math.Clamp(b, 0.0f, 1.0f), 1.0f);
    }

    private static string GetModifierDisplayName(BiomeModifier modifier)
    {
        return string.IsNullOrWhiteSpace(modifier.Name) ? GetModifierTypeLabel(modifier.Type) : modifier.Name;
    }

    private static string GetModifierTypeLabel(BiomeModifierType type)
    {
        return type switch
        {
            BiomeModifierType.HeightRange => "Height range",
            BiomeModifierType.SlopeRange => "Slope range",
            BiomeModifierType.CurvatureRange => "Curvature range",
            BiomeModifierType.DirectionRange => "Direction range",
            BiomeModifierType.Noise => "Noise",
            BiomeModifierType.TextureMask => "Texture mask",
            _ => type.ToString()
        };
    }

    private static void RenderRangeModifier(BiomeModifier modifier, ClimateRuleService service)
    {
        float min = modifier.Min;
        float max = modifier.Max;
        float minFalloff = modifier.MinFalloff;
        float maxFalloff = modifier.MaxFalloff;
        float radius = modifier.Radius;

        if (ImGui.InputFloat("Min", ref min))
        {
            modifier.Min = min;
            service.NotifyMutated();
        }

        if (ImGui.InputFloat("Max", ref max))
        {
            modifier.Max = max;
            service.NotifyMutated();
        }

        if (ImGui.InputFloat("Min Falloff", ref minFalloff))
        {
            modifier.MinFalloff = Math.Max(0.0f, minFalloff);
            service.NotifyMutated();
        }

        if (ImGui.InputFloat("Max Falloff", ref maxFalloff))
        {
            modifier.MaxFalloff = Math.Max(0.0f, maxFalloff);
            service.NotifyMutated();
        }

        if (modifier.Type == BiomeModifierType.CurvatureRange && ImGui.InputFloat("Radius", ref radius))
        {
            modifier.Radius = Math.Max(0.001f, radius);
            service.NotifyMutated();
        }
    }

    private static void RenderDirectionModifier(BiomeModifier modifier, ClimateRuleService service)
    {
        float angle = modifier.AngleDegrees;
        float range = modifier.AngleRangeDegrees;
        float minFalloff = modifier.MinFalloff;
        float maxFalloff = modifier.MaxFalloff;

        if (ImGui.SliderFloat("Angle", ref angle, -180.0f, 180.0f, "%.1f"))
        {
            modifier.AngleDegrees = angle;
            service.NotifyMutated();
        }

        if (ImGui.SliderFloat("Range", ref range, 0.0f, 180.0f, "%.1f"))
        {
            modifier.AngleRangeDegrees = range;
            service.NotifyMutated();
        }

        if (ImGui.InputFloat("Min Falloff", ref minFalloff))
        {
            modifier.MinFalloff = Math.Max(0.0f, minFalloff);
            service.NotifyMutated();
        }

        if (ImGui.InputFloat("Max Falloff", ref maxFalloff))
        {
            modifier.MaxFalloff = Math.Max(0.0f, maxFalloff);
            service.NotifyMutated();
        }
    }

    private static void RenderNoiseModifier(BiomeModifier modifier, ClimateRuleService service)
    {
        float scale = modifier.Scale;
        float offsetX = modifier.OffsetX;
        float offsetY = modifier.OffsetY;
        float seed = modifier.Seed;
        float octaves = modifier.Octaves;

        if (ImGui.InputFloat("Scale", ref scale))
        {
            modifier.Scale = Math.Max(0.0001f, scale);
            service.NotifyMutated();
        }

        if (ImGui.InputFloat("Offset X", ref offsetX))
        {
            modifier.OffsetX = offsetX;
            service.NotifyMutated();
        }

        if (ImGui.InputFloat("Offset Y", ref offsetY))
        {
            modifier.OffsetY = offsetY;
            service.NotifyMutated();
        }

        if (ImGui.InputFloat("Seed", ref seed))
        {
            modifier.Seed = seed;
            service.NotifyMutated();
        }

        if (ImGui.InputFloat("Octaves", ref octaves))
        {
            modifier.Octaves = Math.Max(1.0f, octaves);
            service.NotifyMutated();
        }
    }

    private static void RenderTextureMaskModifier(BiomeModifier modifier, ClimateRuleService service)
    {
        string texturePath = modifier.TextureMaskPath ?? string.Empty;
        if (ImGui.InputText("Texture", ref texturePath, 260))
        {
            modifier.TextureMaskPath = string.IsNullOrWhiteSpace(texturePath) ? null : texturePath;
            service.NotifyMutated();
        }

        int channel = modifier.TextureMaskChannel;
        if (ImGui.SliderInt("Channel", ref channel, 0, 3))
        {
            modifier.TextureMaskChannel = channel;
            service.NotifyMutated();
        }

        bool invert = modifier.Invert > 0.5f;
        if (ImGui.Checkbox("Invert", ref invert))
        {
            modifier.Invert = invert ? 1.0f : 0.0f;
            service.NotifyMutated();
        }
    }

    private static void EnsurePainterSelection()
    {
        ClimateRuleService service = ClimateRuleService.Instance;
        EditorState state = EditorState.Instance;

        if (service.Biomes.Count == 0)
            return;

        if (service.FindBiome(state.CurrentClimateId) == null)
            state.CurrentClimateId = service.Biomes[0].Id;

        ClimateRuleLayer? selected = service.GetRuleByGlobalIndex(state.SelectedRuleIndex);
        if (selected == null || selected.ClimateId != state.CurrentClimateId)
        {
            ClimateRuleLayer? firstLayer = service.GetLayersForBiome(state.CurrentClimateId).FirstOrDefault();
            state.SelectedRuleIndex = service.GetRuleGlobalIndex(firstLayer);
            state.SelectedModifierIndex = 0;
        }
    }

    private static void EnsureModifierSelection(ClimateRuleLayer layer, EditorState state)
    {
        if (layer.Modifiers.Count == 0)
        {
            state.SelectedModifierIndex = -1;
            return;
        }

        if (state.SelectedModifierIndex < 0 || state.SelectedModifierIndex >= layer.Modifiers.Count)
            state.SelectedModifierIndex = 0;
    }

    private void MoveSelectedLayer(ClimateRuleService service, EditorState state, int delta)
    {
        ClimateRuleLayer? selected = service.GetRuleByGlobalIndex(state.SelectedRuleIndex);
        if (selected == null)
            return;

        var biomeLayers = service.GetLayersForBiome(state.CurrentClimateId).ToList();
        int localIndex = biomeLayers.FindIndex(layer => ReferenceEquals(layer, selected));
        int targetLocalIndex = localIndex + delta;
        if (localIndex < 0 || targetLocalIndex < 0 || targetLocalIndex >= biomeLayers.Count)
            return;

        int globalFrom = service.GetRuleGlobalIndex(biomeLayers[localIndex]);
        int globalTo = service.GetRuleGlobalIndex(biomeLayers[targetLocalIndex]);
        service.MoveRule(globalFrom, globalTo);
        state.SelectedRuleIndex = globalTo;
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MoveSelectedModifier(ClimateRuleLayer layer, ClimateRuleService service, EditorState state, int delta)
    {
        int from = state.SelectedModifierIndex;
        int to = from + delta;
        if (from < 0 || to < 0 || to >= layer.Modifiers.Count)
            return;

        service.MoveModifier(layer, from, to);
        state.SelectedModifierIndex = to;
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BeginLayerDragDrop(ClimateRuleService service, EditorState state, ClimateRuleLayer layer)
    {
        int fromIndex = service.GetRuleGlobalIndex(layer);
        unsafe
        {
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullId))
            {
                ImGui.SetDragDropPayload("BiomeLayerRow", &fromIndex, (uint)sizeof(int));
                ImGui.TextUnformatted(layer.Name);
                ImGui.EndDragDropSource();
            }
        }
    }

    private static int? AcceptLayerDragDrop(ClimateRuleService service, ClimateRuleLayer targetLayer, out int? targetIndex)
    {
        targetIndex = null;
        unsafe
        {
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("BiomeLayerRow");
                if (!payload.IsNull && payload.Data != null && payload.DataSize == sizeof(int))
                {
                    int fromIndex = *(int*)payload.Data;
                    targetIndex = service.GetRuleGlobalIndex(targetLayer);
                    ImGui.EndDragDropTarget();
                    return fromIndex;
                }

                ImGui.EndDragDropTarget();
            }
        }

        return null;
    }

    private void BeginModifierDragDrop(ClimateRuleLayer layer, ClimateRuleService service, EditorState state, int index)
    {
        unsafe
        {
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullId))
            {
                ImGui.SetDragDropPayload("BiomeModifierRow", &index, (uint)sizeof(int));
                ImGui.TextUnformatted(GetModifierDisplayName(layer.Modifiers[index]));
                ImGui.EndDragDropSource();
            }
        }
    }

    private static int? AcceptModifierDragDrop(ClimateRuleLayer layer, int targetIndex, out int? acceptedTargetIndex)
    {
        acceptedTargetIndex = null;
        unsafe
        {
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("BiomeModifierRow");
                if (!payload.IsNull && payload.Data != null && payload.DataSize == sizeof(int))
                {
                    int fromIndex = *(int*)payload.Data;
                    acceptedTargetIndex = targetIndex;
                    ImGui.EndDragDropTarget();
                    return fromIndex;
                }

                ImGui.EndDragDropTarget();
            }
        }

        return null;
    }

    private void DuplicateSelectedLayer(ClimateRuleService service, EditorState state)
    {
        ClimateRuleLayer? selected = service.GetRuleByGlobalIndex(state.SelectedRuleIndex);
        if (selected == null)
            return;

        ClimateRuleLayer duplicate = service.AddLayer(selected.ClimateId);
        duplicate.Name = $"{selected.Name} Copy";
        duplicate.Enabled = selected.Enabled;
        duplicate.Visible = selected.Visible;
        duplicate.MaterialSlotIndex = selected.MaterialSlotIndex;

        duplicate.Modifiers.Clear();
        foreach (BiomeModifier modifier in selected.Modifiers)
        {
            BiomeModifier clone = modifier.Clone();
            clone.Id = 0;
            duplicate.Modifiers.Add(clone);
        }
        duplicate.EnsureLegacyModifiers();

        state.SelectedRuleIndex = service.GetRuleGlobalIndex(duplicate);
        state.SelectedModifierIndex = duplicate.Modifiers.Count > 0 ? 0 : -1;
        layerNameBuffer = duplicate.Name;
        NotifyRulesMutated(service);
    }

    private void NotifyRulesMutated(ClimateRuleService service)
    {
        service.NotifyMutated();
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }
}
