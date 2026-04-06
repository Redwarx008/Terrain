#nullable enable

using Hexa.NET.ImGui;
using Stride.Graphics;
using System;
using System.IO;
using System.Numerics;
using Terrain.Editor.Services;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// 纹理属性检查器面板 - 显示选中纹理槽位的详细属性。
/// </summary>
internal class TextureInspectorPanel
{
    /// <summary>
    /// 当前选中的纹理槽位索引。
    /// </summary>
    public int SelectedSlotIndex { get; set; } = -1;

    /// <summary>
    /// 请求导入法线贴图事件。
    /// </summary>
    public event EventHandler<TextureImportEventArgs>? ImportNormalRequested;

    /// <summary>
    /// 请求清除法线贴图事件。
    /// </summary>
    public event EventHandler<TextureSlotEventArgs>? ClearNormalRequested;

    /// <summary>
    /// 渲染检查器内容。
    /// </summary>
    public void Render()
    {
        if (SelectedSlotIndex < 0)
        {
            ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "No texture selected");
            return;
        }

        var slot = MaterialSlotManager.Instance[SelectedSlotIndex];
        if (slot.IsEmpty)
        {
            ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "Empty slot");
            return;
        }

        ImGui.Spacing();

        // 基本信息
        RenderSlotInfo(slot);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Diffuse 区域
        RenderTextureSection("Diffuse", slot.AlbedoTexture, slot.AlbedoTexturePath, slot.ImportSize, null);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Normal 区域
        RenderTextureSection("Normal", slot.NormalTexture, slot.NormalTexturePath, slot.ImportSize,
            () => ImportNormalRequested?.Invoke(this, new TextureImportEventArgs
            {
                SlotIndex = SelectedSlotIndex,
                TextureType = TextureType.Normal
            }),
            () => ClearNormalRequested?.Invoke(this, new TextureSlotEventArgs
            {
                SlotIndex = SelectedSlotIndex
            }));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Tiling Scale
        RenderTilingScale(slot);
    }

    private void RenderSlotInfo(MaterialSlot slot)
    {
        ImGui.Text("Name");
        ImGui.SameLine(80);
        ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), slot.Name);

        ImGui.Text("Index");
        ImGui.SameLine(80);
        ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), $"#{slot.Index}");
    }

    private void RenderTextureSection(
        string label,
        Texture? texture,
        string? path,
        TextureSize importSize,
        Action? onImport,
        Action? onClear = null)
    {
        ImGui.Text(label);

        ImGui.Spacing();

        float previewSize = EditorStyle.ScaleValue(64.0f);
        float lineHeight = ImGui.GetTextLineHeightWithSpacing();

        // 预览图
        Vector2 cursor = ImGui.GetCursorScreenPos();
        if (texture != null)
        {
            var texRef = ImGuiExtension.GetTextureKey(texture);
            ImGui.GetWindowDrawList().AddImage(texRef, cursor, new Vector2(cursor.X + previewSize, cursor.Y + previewSize));
        }
        else
        {
            ImGui.GetWindowDrawList().AddRectFilled(cursor,
                new Vector2(cursor.X + previewSize, cursor.Y + previewSize),
                ColorPalette.Background.ToUint());
            ImGui.GetWindowDrawList().AddRect(cursor,
                new Vector2(cursor.X + previewSize, cursor.Y + previewSize),
                ColorPalette.Border.ToUint());

            // 占位文字
            Vector2 textSize = ImGui.CalcTextSize("N/A");
            Vector2 textPos = new Vector2(
                cursor.X + (previewSize - textSize.X) * 0.5f,
                cursor.Y + (previewSize - textSize.Y) * 0.5f);
            ImGui.GetWindowDrawList().AddText(textPos, ColorPalette.TextSecondary.ToUint(), "N/A");
        }

        ImGui.Dummy(new Vector2(previewSize, previewSize));

        // 右侧信息
        ImGui.SameLine();

        if (path != null)
        {
            float infoWidth = ImGui.GetContentRegionAvail().X;
            float textWrapPos = ImGui.GetCursorPosX() + infoWidth - EditorStyle.ScaleValue(8.0f);
            ImGui.PushTextWrapPos(textWrapPos);

            // 文件名
            string fileName = Path.GetFileName(path);
            ImGui.TextColored(ColorPalette.TextPrimary.ToVector4(), fileName);

            // 文件路径（折叠显示）
            ImGui.PushStyleColor(ImGuiCol.Text, ColorPalette.TextSecondary.ToVector4());
            string directory = Path.GetDirectoryName(path) ?? "";
            if (directory.Length > 30)
                directory = "..." + directory[^27..];
            ImGui.TextWrapped(directory);
            ImGui.PopStyleColor();

            // 尺寸
            if (texture != null)
            {
                ImGui.Text($"Size: {texture.Width}x{texture.Height}");
            }

            // 文件大小
            try
            {
                var fileInfo = new FileInfo(path);
                ImGui.Text($"File: {FormatFileSize(fileInfo.Length)}");
            }
            catch
            {
                // 忽略文件访问错误
            }

            ImGui.PopTextWrapPos();

            // 操作按钮
            ImGui.Spacing();
            if (onClear != null)
            {
                string clearText = "Clear";
                float clearWidth = ImGui.CalcTextSize(clearText).X + EditorStyle.ScaleValue(16.0f);
                if (ImGui.Button($"{clearText}##{label}", new Vector2(clearWidth, 0)))
                {
                    onClear();
                }
            }
        }
        else
        {
            // 无纹理，显示导入按钮
            if (onImport != null)
            {
                string importText = $"Import {label}";
                float importWidth = ImGui.CalcTextSize(importText).X + EditorStyle.ScaleValue(16.0f);
                if (ImGui.Button($"{importText}##{label}", new Vector2(importWidth, 0)))
                {
                    onImport();
                }
            }
        }
    }

    private void RenderTilingScale(MaterialSlot slot)
    {
        ImGui.Text("Tiling Scale");

        float scale = slot.TilingScale;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat("##tiling_scale", ref scale, 0.1f, 10.0f, "%.2f"))
        {
            slot.TilingScale = scale;
        }

        ImGui.TextColored(ColorPalette.TextSecondary.ToVector4(), "Higher = more texture repeats");
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int i = 0;
        double size = bytes;

        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }

        return $"{size:0.#} {suffixes[i]}";
    }
}
