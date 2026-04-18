#nullable enable

using Hexa.NET.ImGui;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Terrain.Editor.Services;
using Terrain.Editor.UI;
using Terrain.Editor.UI.Styling;
using Stride.Graphics;

namespace Terrain.Editor.UI.Panels;

public sealed class InputsDataPanel : PanelBase
{
    private const int PreviewTextureMaxSize = 64;

    private readonly Dictionary<string, PreviewTextureEntry> previewCache = new(StringComparer.OrdinalIgnoreCase);
    private TerrainManager? terrainManager;
    private GraphicsDevice? graphicsDevice;
    private GraphicsContext? graphicsContext;

    public InputsDataPanel()
    {
        Title = "Inputs & Data";
        ShowTitleBar = true;
    }

    public void SetTerrainManager(TerrainManager? manager)
    {
        terrainManager = manager;
    }

    public void SetGraphicsResources(GraphicsDevice? device, GraphicsContext? context)
    {
        if (!ReferenceEquals(graphicsDevice, device) || !ReferenceEquals(graphicsContext, context))
        {
            ClearPreviewCache();
        }

        graphicsDevice = device;
        graphicsContext = context;
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
            terrainManager?.CurrentTerrainPath);
        ImGui.Spacing();
        RenderInputSlot(
            "Climate Mask",
            terrainManager?.CurrentClimateMaskPath);

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

    private void RenderInputSlot(string label, string? path)
    {
        float rowHeight = EditorStyle.ScaleValue(58.0f);
        float rowSpacing = EditorStyle.ScaleValue(8.0f);
        float previewMaxHeight = EditorStyle.ScaleValue(42.0f);
        float previewMaxWidth = EditorStyle.ScaleValue(64.0f);
        float leftPadding = EditorStyle.ScaleValue(10.0f);
        float labelColumnWidth = GetLabelColumnWidth();
        float labelToPreviewPadding = EditorStyle.ScaleValue(12.0f);
        float cornerRadius = EditorStyle.ScaleValue(4.0f);
        Vector2 cursor = ImGui.GetCursorScreenPos();
        Vector2 rowSize = new(ContentRect.Width - EditorStyle.ScaleValue(8.0f), rowHeight);
        Vector2 rowEnd = new(cursor.X + rowSize.X, cursor.Y + rowSize.Y);
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(cursor, rowEnd, ColorPalette.SelectionInactive.ToUint(), cornerRadius);
        drawList.AddRect(cursor, rowEnd, ColorPalette.Border.ToUint(), cornerRadius);

        Vector2 labelSize = ImGui.CalcTextSize(label);
        Vector2 labelPos = new(
            cursor.X + leftPadding,
            cursor.Y + (rowHeight - labelSize.Y) * 0.5f);
        drawList.AddText(labelPos, ColorPalette.TextPrimary.ToUint(), label);

        float previewX = cursor.X + leftPadding + labelColumnWidth + labelToPreviewPadding;
        Vector2 previewFrameSize = GetPreviewFrameSize(path, previewMaxWidth, previewMaxHeight);

        Vector2 previewMin = new(previewX, cursor.Y + (rowHeight - previewFrameSize.Y) * 0.5f);
        Vector2 previewMax = new(previewMin.X + previewFrameSize.X, previewMin.Y + previewFrameSize.Y);
        drawList.AddRectFilled(previewMin, previewMax, ColorPalette.Selection.ToUint(), EditorStyle.ScaleValue(3.0f));
        drawList.AddRect(previewMin, previewMax, ColorPalette.BorderLight.ToUint(), EditorStyle.ScaleValue(3.0f));

        if (TryGetPreviewTexture(path, out Texture? previewTexture))
        {
            drawList.AddImage(ImGuiExtension.GetTextureKey(previewTexture!), previewMin, previewMax);
        }
        else
        {
            const string previewText = "N/A";
            Vector2 textSize = ImGui.CalcTextSize(previewText);
            drawList.AddText(
                new Vector2(previewMin.X + (previewFrameSize.X - textSize.X) * 0.5f, previewMin.Y + (previewFrameSize.Y - textSize.Y) * 0.5f),
                ColorPalette.TextSecondary.ToUint(),
                previewText);
        }

        if (!string.IsNullOrEmpty(path) && ImGui.IsMouseHoveringRect(previewMin, previewMax))
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(Path.GetFileName(path));
            ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), path);
            ImGui.EndTooltip();
        }

        ImGui.SetCursorScreenPos(new Vector2(cursor.X, cursor.Y + rowHeight + rowSpacing));
    }

    private static float GetLabelColumnWidth()
    {
        float longestLabelWidth = MathF.Max(
            ImGui.CalcTextSize("Heightmap").X,
            ImGui.CalcTextSize("Climate Mask").X);
        return longestLabelWidth;
    }

    private bool TryGetPreviewTexture(string? path, out Texture? texture)
    {
        texture = null;
        if (string.IsNullOrEmpty(path) || graphicsDevice == null || graphicsContext == null || !File.Exists(path))
            return false;

        if (previewCache.TryGetValue(path, out PreviewTextureEntry? existing))
        {
            if (!string.Equals(existing.SourcePath, path, StringComparison.OrdinalIgnoreCase))
            {
                existing.Dispose();
                previewCache.Remove(path);
                return false;
            }

            texture = existing.Texture;
            return texture != null;
        }

        Texture? createdTexture = CreatePreviewTexture(path);
        previewCache[path] = new PreviewTextureEntry(path, createdTexture);
        texture = createdTexture;
        return texture != null;
    }

    private Texture? CreatePreviewTexture(string path)
    {
        try
        {
            using SixLabors.ImageSharp.Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
            int maxDimension = Math.Max(image.Width, image.Height);
            if (maxDimension <= 0)
                return null;

            float scale = Math.Min(1.0f, PreviewTextureMaxSize / (float)maxDimension);
            int previewWidth = Math.Max(1, (int)MathF.Round(image.Width * scale));
            int previewHeight = Math.Max(1, (int)MathF.Round(image.Height * scale));

            using SixLabors.ImageSharp.Image<Rgba32> resized = image.Clone(ctx => ctx.Resize(previewWidth, previewHeight));
            byte[] pixelData = new byte[previewWidth * previewHeight * 4];
            resized.CopyPixelDataTo(pixelData);

            var texture = Texture.New2D(
                graphicsDevice!,
                previewWidth,
                previewHeight,
                PixelFormat.R8G8B8A8_UNorm_SRgb,
                TextureFlags.ShaderResource);
            texture.SetData(graphicsContext!.CommandList, pixelData);
            return texture;
        }
        catch
        {
            return null;
        }
    }

    private Vector2 GetPreviewFrameSize(string? path, float maxWidth, float maxHeight)
    {
        if (!TryGetPreviewTexture(path, out Texture? texture))
            return new Vector2(maxHeight, maxHeight);

        float width = texture!.Width;
        float height = texture.Height;
        float scale = MathF.Min(maxWidth / width, maxHeight / height);
        return new Vector2(width * scale, height * scale);
    }

    private void ClearPreviewCache()
    {
        foreach (PreviewTextureEntry entry in previewCache.Values)
        {
            entry.Dispose();
        }

        previewCache.Clear();
    }

    protected override void OnDispose()
    {
        ClearPreviewCache();
        base.OnDispose();
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

    private sealed class PreviewTextureEntry : IDisposable
    {
        public PreviewTextureEntry(string sourcePath, Texture? texture)
        {
            SourcePath = sourcePath;
            Texture = texture;
        }

        public string SourcePath { get; }

        public Texture? Texture { get; }

        public void Dispose()
        {
            Texture?.Dispose();
        }
    }
}
