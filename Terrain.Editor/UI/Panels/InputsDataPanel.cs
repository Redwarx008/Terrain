#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.Services;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

public sealed class InputsDataPanel : PanelBase
{
    private TerrainManager? terrainManager;

    public event EventHandler? LoadHeightmapRequested;
    public event EventHandler? LoadClimateMaskRequested;

    public InputsDataPanel()
    {
        Title = "Inputs & Data";
        ShowTitleBar = true;
    }

    public void SetTerrainManager(TerrainManager? manager)
    {
        terrainManager = manager;
    }

    protected override void RenderContent()
    {
        ImGui.SetCursorScreenPos(new Vector2(ContentRect.X, ContentRect.Y));
        if (!ImGui.BeginChild($"##inputs_data_{Id}", new Vector2(ContentRect.Width, ContentRect.Height), ImGuiChildFlags.None))
        {
            ImGui.EndChild();
            return;
        }

        RenderSectionHeader("Input Data");
        RenderInputSlot(
            "Heightmap",
            terrainManager?.CurrentTerrainPath,
            LoadHeightmapRequested);
        ImGui.Spacing();
        RenderInputSlot(
            "Climate Mask",
            terrainManager?.CurrentClimateMaskPath,
            LoadClimateMaskRequested);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        RenderSectionHeader("General Parameters");
        RenderHeightScale();

        ImGui.EndChild();
    }

    private static void RenderSectionHeader(string title)
    {
        ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), title);
        ImGui.Spacing();
    }

    private void RenderInputSlot(string label, string? path, EventHandler? onClick)
    {
        float rowHeight = EditorStyle.ScaleValue(68.0f);
        float previewSize = EditorStyle.ScaleValue(52.0f);
        Vector2 cursor = ImGui.GetCursorScreenPos();
        Vector2 rowSize = new(ContentRect.Width - EditorStyle.ScaleValue(8.0f), rowHeight);
        Vector2 rowEnd = new(cursor.X + rowSize.X, cursor.Y + rowSize.Y);
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(cursor, rowEnd, ColorPalette.DarkBackground.ToUint(), EditorStyle.ScaleValue(4.0f));
        drawList.AddRect(cursor, rowEnd, ColorPalette.Border.ToUint(), EditorStyle.ScaleValue(4.0f));

        ImGui.SetCursorScreenPos(new Vector2(cursor.X + EditorStyle.ScaleValue(8.0f), cursor.Y + EditorStyle.ScaleValue(8.0f)));
        ImGui.Text(label);

        Vector2 previewMin = new(cursor.X + EditorStyle.ScaleValue(8.0f), cursor.Y + EditorStyle.ScaleValue(24.0f));
        Vector2 previewMax = new(previewMin.X + previewSize, previewMin.Y + previewSize);
        drawList.AddRectFilled(previewMin, previewMax, ColorPalette.Selection.ToUint(), EditorStyle.ScaleValue(3.0f));
        drawList.AddRect(previewMin, previewMax, ColorPalette.BorderLight.ToUint(), EditorStyle.ScaleValue(3.0f));

        string previewText = string.IsNullOrEmpty(path) ? "N/A" : System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        Vector2 textSize = ImGui.CalcTextSize(previewText);
        drawList.AddText(
            new Vector2(previewMin.X + (previewSize - textSize.X) * 0.5f, previewMin.Y + (previewSize - textSize.Y) * 0.5f),
            ColorPalette.TextSecondary.ToUint(),
            previewText);

        float buttonWidth = EditorStyle.ScaleValue(56.0f);
        float buttonX = rowEnd.X - buttonWidth - EditorStyle.ScaleValue(8.0f);
        float buttonY = cursor.Y + (rowHeight - EditorStyle.ScaleValue(28.0f)) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(buttonX, buttonY));
        if (ImGui.Button($"Load##{label}_{Id}", new Vector2(buttonWidth, EditorStyle.ScaleValue(28.0f))))
        {
            onClick?.Invoke(this, EventArgs.Empty);
        }

        string fileLabel = string.IsNullOrEmpty(path) ? "Not loaded" : System.IO.Path.GetFileName(path);
        ImGui.SetCursorScreenPos(new Vector2(previewMax.X + EditorStyle.ScaleValue(10.0f), previewMin.Y + EditorStyle.ScaleValue(4.0f)));
        ImGui.PushTextWrapPos(buttonX - EditorStyle.ScaleValue(10.0f));
        ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), fileLabel);
        ImGui.PopTextWrapPos();

        ImGui.SetCursorScreenPos(new Vector2(cursor.X, cursor.Y + rowHeight + EditorStyle.ScaleValue(6.0f)));
    }

    private void RenderHeightScale()
    {
        if (terrainManager == null)
        {
            ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "No terrain loaded.");
            return;
        }

        ImGui.Text("Height Scale");
        ImGui.SetNextItemWidth(-1);
        float heightScale = terrainManager.HeightScale;
        if (ImGui.SliderFloat($"##height_scale_inputs_{Id}", ref heightScale, 1.0f, 200.0f, "%.1f"))
        {
            terrainManager.SetHeightScale(heightScale);
            // Height scale affects all derived terrain diagnostics, so rebuild the
            // generated material map immediately to keep rule results coherent.
            terrainManager.RegenerateMaterialIndices();
        }
    }
}
