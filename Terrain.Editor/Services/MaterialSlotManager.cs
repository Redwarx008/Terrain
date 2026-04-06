#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stride.Graphics;

namespace Terrain.Editor.Services;

/// <summary>
/// 材质槽位管理器，管理最多 256 个材质槽位。
/// 单例模式，支持纹理数组的按需扩容。
/// </summary>
public sealed class MaterialSlotManager
{
    private static readonly Lazy<MaterialSlotManager> _instance = new(() => new());
    public static MaterialSlotManager Instance => _instance.Value;

    private readonly MaterialSlot[] slots = new MaterialSlot[256];

    /// <summary>
    /// 当前选中的材质槽位索引。
    /// </summary>
    public int SelectedSlotIndex { get; set; } = 0;

    /// <summary>
    /// 获取当前选中的材质槽位。
    /// </summary>
    public MaterialSlot SelectedSlot => slots[SelectedSlotIndex];

    /// <summary>
    /// 通过索引访问材质槽位。
    /// </summary>
    public MaterialSlot this[int index] => slots[index];

    /// <summary>
    /// 获取下一个可用槽位索引。如果没有可用槽位则返回 -1。
    /// </summary>
    public int NextAvailableSlotIndex
    {
        get
        {
            var emptySlot = slots.FirstOrDefault(s => s.IsEmpty);
            return emptySlot?.Index ?? -1;
        }
    }

    /// <summary>
    /// 缓存的材质纹理数组。
    /// </summary>
    private Texture? cachedMaterialAlbedoArray;

    /// <summary>
    /// 标记材质数组需要重建。
    /// </summary>
    private bool materialArrayDirty = true;

    /// <summary>
    /// 标记材质数组需要重建。
    /// </summary>
    public void MarkMaterialArrayDirty()
    {
        materialArrayDirty = true;
    }

    private MaterialSlotManager()
    {
        // 初始化所有槽位
        for (int i = 0; i < 256; i++)
        {
            slots[i] = new MaterialSlot { Index = i };
        }
    }

    /// <summary>
    /// 获取所有已配置（非空）的材质槽位。
    /// </summary>
    public IEnumerable<MaterialSlot> GetActiveSlots()
    {
        return slots.Where(s => !s.IsEmpty);
    }

    /// <summary>
    /// 获取已配置材质的数量。
    /// </summary>
    public int ActiveSlotCount => slots.Count(s => !s.IsEmpty);

    /// <summary>
    /// 设置 Albedo 纹理到指定槽位。
    /// </summary>
    public void SetAlbedoTexture(int slotIndex, Texture texture, string path, TextureSize size)
    {
        var slot = slots[slotIndex];
        slot.AlbedoTexture?.Dispose();
        slot.AlbedoTexture = texture;
        slot.AlbedoTexturePath = path;
        slot.ImportSize = size;

        // 如果名称为空或是默认名称，则使用文件名
        if (string.IsNullOrEmpty(slot.Name) || slot.Name.StartsWith("Texture "))
        {
            slot.Name = Path.GetFileNameWithoutExtension(path);
        }

        materialArrayDirty = true;
    }

    /// <summary>
    /// 设置 Normal 纹理到指定槽位。
    /// </summary>
    public void SetNormalTexture(int slotIndex, Texture texture, string path)
    {
        var slot = slots[slotIndex];
        slot.NormalTexture?.Dispose();
        slot.NormalTexture = texture;
        slot.NormalTexturePath = path;
    }

    /// <summary>
    /// 清除指定槽位的 Normal 纹理。
    /// </summary>
    public void ClearNormalTexture(int slotIndex)
    {
        var slot = slots[slotIndex];
        slot.NormalTexture?.Dispose();
        slot.NormalTexture = null;
        slot.NormalTexturePath = null;
    }

    /// <summary>
    /// 清除指定槽位的配置。
    /// </summary>
    public void ClearSlot(int slotIndex)
    {
        slots[slotIndex].Clear();
        materialArrayDirty = true;
    }

    /// <summary>
    /// 清空所有槽位配置。
    /// </summary>
    public void ClearAll()
    {
        foreach (var slot in slots)
        {
            slot.Clear();
        }
        materialArrayDirty = true;
    }

    /// <summary>
    /// 获取或构建材质 Albedo 纹理数组。
    /// 如果材质槽位发生变化，会重新构建数组。
    /// 纹理数组索引与材质槽位索引保持一致。
    /// </summary>
    /// <param name="graphicsDevice">图形设备。</param>
    /// <param name="commandList">命令列表，用于复制纹理数据。</param>
    /// <returns>材质纹理数组，如果没有活动槽位则返回 null。</returns>
    public Texture? GetOrBuildMaterialAlbedoArray(GraphicsDevice graphicsDevice, CommandList commandList)
    {
        var activeSlots = GetActiveSlots().ToList();

        if (activeSlots.Count == 0)
        {
            cachedMaterialAlbedoArray?.Dispose();
            cachedMaterialAlbedoArray = null;
            return null;
        }

        // 如果不需要重建且有缓存，直接返回
        if (!materialArrayDirty && cachedMaterialAlbedoArray != null)
        {
            return cachedMaterialAlbedoArray;
        }

        // 获取所有活动槽位及其纹理
        var activeTextures = activeSlots
            .Where(s => s.AlbedoTexture != null)
            .Select(s => (Slot: s, Texture: s.AlbedoTexture!))
            .ToList();

        if (activeTextures.Count == 0)
        {
            return null;
        }

        // 使用第一个纹理的尺寸和格式作为基准
        var first = activeTextures[0];
        int width = first.Texture.Width;
        int height = first.Texture.Height;
        PixelFormat format = first.Texture.Format;

        // 验证所有纹理尺寸和格式一致
        var validTextures = new List<(MaterialSlot Slot, Texture Texture)>();
        foreach (var (slot, tex) in activeTextures)
        {
            if (tex.Width != width || tex.Height != height)
            {
                // 跳过尺寸不匹配的纹理
                continue;
            }
            if (tex.Format != format)
            {
                // 跳过格式不匹配的纹理
                continue;
            }
            validTextures.Add((slot, tex));
        }

        if (validTextures.Count == 0)
        {
            return null;
        }

        // 释放旧的缓存
        cachedMaterialAlbedoArray?.Dispose();

        // 计算需要的数组大小（最大槽位索引 + 1）
        // 这样纹理数组索引与材质槽位索引保持一致
        int maxSlotIndex = validTextures.Max(t => t.Slot.Index) + 1;

        // 创建 Texture2DArray，数组大小等于最大槽位索引 + 1
        var desc = TextureDescription.New2D(width, height, format,
            TextureFlags.ShaderResource | TextureFlags.RenderTarget,
            arraySize: maxSlotIndex);
        cachedMaterialAlbedoArray = Texture.New(graphicsDevice, desc);

        // 复制每个纹理到对应的数组切片（槽位索引 = 数组索引）
        foreach (var (slot, tex) in validTextures)
        {
            commandList.CopyRegion(
                tex,
                sourceSubResourceIndex: 0,
                sourceRegion: null,
                cachedMaterialAlbedoArray,
                destinationSubResourceIndex: slot.Index,
                dstX: 0, dstY: 0, dstZ: 0);
        }

        materialArrayDirty = false;
        return cachedMaterialAlbedoArray;
    }
}
