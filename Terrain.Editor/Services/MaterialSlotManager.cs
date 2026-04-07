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
    /// 缓存的材质 Albedo 纹理数组。
    /// </summary>
    private Texture? cachedMaterialAlbedoArray;

    /// <summary>
    /// 缓存的材质 Normal 纹理数组。
    /// </summary>
    private Texture? cachedMaterialNormalArray;

    /// <summary>
    /// 已使用的最大槽位索引（用于统一数组大小）。
    /// </summary>
    private int maxSlotIndex = -1;

    /// <summary>
    /// 纹理数组容量档位，用于预分配优化。
    /// </summary>
    private static readonly int[] ArrayCapacityTiers = { 16, 32, 64, 128, 256 };

    /// <summary>
    /// 获取下一个合适的容量档位。
    /// </summary>
    private static int GetNextCapacity(int requiredSize)
    {
        foreach (var tier in ArrayCapacityTiers)
        {
            if (tier >= requiredSize) return tier;
        }
        return 256;
    }

    /// <summary>
    /// 默认平法线纹理（用于填充清除的槽位）。
    /// </summary>
    private Texture? defaultNormalTexture;

    /// <summary>
    /// 标记材质数组需要重建。
    /// </summary>
    public void MarkMaterialArrayDirty()
    {
        cachedMaterialAlbedoArray?.Dispose();
        cachedMaterialAlbedoArray = null;
        cachedMaterialNormalArray?.Dispose();
        cachedMaterialNormalArray = null;
        defaultNormalTexture?.Dispose();
        defaultNormalTexture = null;
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
    public void SetAlbedoTexture(int slotIndex, Texture texture, string path, TextureSize size,
        GraphicsDevice graphicsDevice, CommandList commandList)
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

        // 直接更新纹理数组
        UpdateSlotInArray(slotIndex, graphicsDevice, commandList);
    }

    /// <summary>
    /// 更新指定槽位的纹理到纹理数组。
    /// </summary>
    private void UpdateSlotInArray(int slotIndex, GraphicsDevice graphicsDevice, CommandList commandList)
    {
        var slot = slots[slotIndex];
        if (slot.AlbedoTexture == null)
            return;

        // 更新最大槽位索引
        maxSlotIndex = Math.Max(maxSlotIndex, slotIndex);

        int width = slot.AlbedoTexture.Width;
        int height = slot.AlbedoTexture.Height;
        PixelFormat format = slot.AlbedoTexture.Format;

        // 检查是否需要重建（使用容量档位优化）
        int requiredCapacity = GetNextCapacity(maxSlotIndex + 1);
        bool needsRebuild = cachedMaterialAlbedoArray == null
            || cachedMaterialAlbedoArray.ArraySize < requiredCapacity
            || cachedMaterialAlbedoArray.Format != format
            || cachedMaterialAlbedoArray.Width != width
            || cachedMaterialAlbedoArray.Height != height;

        if (needsRebuild)
        {
            var oldArray = cachedMaterialAlbedoArray;

            var desc = TextureDescription.New2D(width, height, format,
                TextureFlags.ShaderResource | TextureFlags.RenderTarget,
                arraySize: requiredCapacity);
            cachedMaterialAlbedoArray = Texture.New(graphicsDevice, desc);

            // 复制旧数组中的纹理到新数组
            if (oldArray != null)
            {
                int copyCount = Math.Min(oldArray.ArraySize, requiredCapacity);
                for (int i = 0; i < copyCount; i++)
                {
                    if (slots[i].AlbedoTexture != null)
                    {
                        commandList.CopyRegion(slots[i].AlbedoTexture, 0, null, cachedMaterialAlbedoArray, i, 0, 0, 0);
                    }
                }
                oldArray.Dispose();
            }
        }

        commandList.CopyRegion(slot.AlbedoTexture, 0, null, cachedMaterialAlbedoArray, slotIndex, 0, 0, 0);
    }

    /// <summary>
    /// 设置 Normal 纹理到指定槽位。
    /// </summary>
    public void SetNormalTexture(int slotIndex, Texture texture, string path,
        GraphicsDevice graphicsDevice, CommandList commandList)
    {
        var slot = slots[slotIndex];
        slot.NormalTexture?.Dispose();
        slot.NormalTexture = texture;
        slot.NormalTexturePath = path;

        // 直接更新 Normal 纹理数组
        UpdateNormalSlotInArray(slotIndex, graphicsDevice, commandList);
    }

    /// <summary>
    /// 更新指定槽位的 Normal 纹理到纹理数组。
    /// </summary>
    private void UpdateNormalSlotInArray(int slotIndex, GraphicsDevice graphicsDevice, CommandList commandList)
    {
        var slot = slots[slotIndex];
        if (slot.NormalTexture == null)
            return;

        // 更新最大槽位索引
        maxSlotIndex = Math.Max(maxSlotIndex, slotIndex);

        int width = slot.NormalTexture.Width;
        int height = slot.NormalTexture.Height;
        PixelFormat format = slot.NormalTexture.Format;

        // 检查是否需要重建（使用容量档位优化）
        int requiredCapacity = GetNextCapacity(maxSlotIndex + 1);
        bool needsRebuild = cachedMaterialNormalArray == null
            || cachedMaterialNormalArray.ArraySize < requiredCapacity
            || cachedMaterialNormalArray.Format != format
            || cachedMaterialNormalArray.Width != width
            || cachedMaterialNormalArray.Height != height;

        if (needsRebuild)
        {
            var oldArray = cachedMaterialNormalArray;

            var desc = TextureDescription.New2D(width, height, format,
                TextureFlags.ShaderResource | TextureFlags.RenderTarget,
                arraySize: requiredCapacity);
            cachedMaterialNormalArray = Texture.New(graphicsDevice, desc);

            // 复制旧数组中的纹理到新数组
            if (oldArray != null)
            {
                int copyCount = Math.Min(oldArray.ArraySize, requiredCapacity);
                for (int i = 0; i < copyCount; i++)
                {
                    if (slots[i].NormalTexture != null)
                    {
                        commandList.CopyRegion(slots[i].NormalTexture, 0, null, cachedMaterialNormalArray, i, 0, 0, 0);
                    }
                }
                oldArray.Dispose();
            }
        }

        commandList.CopyRegion(slot.NormalTexture, 0, null, cachedMaterialNormalArray, slotIndex, 0, 0, 0);
    }

    /// <summary>
    /// 清除指定槽位的 Normal 纹理。
    /// </summary>
    public void ClearNormalTexture(int slotIndex, GraphicsDevice graphicsDevice, CommandList commandList)
    {
        var slot = slots[slotIndex];
        slot.NormalTexture?.Dispose();
        slot.NormalTexture = null;
        slot.NormalTexturePath = null;

        // 填充默认平法线到数组对应位置
        if (cachedMaterialNormalArray != null && slotIndex < cachedMaterialNormalArray.ArraySize)
        {
            FillArraySliceWithDefaultNormal(slotIndex, graphicsDevice, commandList);
        }
    }

    /// <summary>
    /// 填充默认平法线到数组指定槽位。
    /// </summary>
    private void FillArraySliceWithDefaultNormal(int slotIndex, GraphicsDevice graphicsDevice, CommandList commandList)
    {
        // 懒加载默认平法线纹理
        if (defaultNormalTexture == null)
        {
            defaultNormalTexture = CreateDefaultNormalTexture(graphicsDevice, commandList);
        }

        if (defaultNormalTexture != null)
        {
            commandList.CopyRegion(defaultNormalTexture, 0, null, cachedMaterialNormalArray!, slotIndex, 0, 0, 0);
        }
    }

    /// <summary>
    /// 创建默认平法线纹理 (0.5, 0.5, 1.0)。
    /// </summary>
    private static Texture? CreateDefaultNormalTexture(GraphicsDevice graphicsDevice, CommandList commandList)
    {
        // 创建 4x4 的平法线纹理
        int size = 4;
        var data = new byte[size * size * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i + 0] = 128; // R = 0.5
            data[i + 1] = 128; // G = 0.5
            data[i + 2] = 255; // B = 1.0
            data[i + 3] = 255; // A = 1.0
        }

        var desc = TextureDescription.New2D(size, size, PixelFormat.R8G8B8A8_UNorm,
            TextureFlags.ShaderResource);
        var texture = Texture.New(graphicsDevice, desc);
        texture.SetData(commandList, data);
        return texture;
    }

    /// <summary>
    /// 清除指定槽位的配置。
    /// </summary>
    public void ClearSlot(int slotIndex)
    {
        slots[slotIndex].Clear();
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
    }

    /// <summary>
    /// 获取材质 Albedo 纹理数组。
    /// </summary>
    public Texture? GetMaterialAlbedoArray()
    {
        return cachedMaterialAlbedoArray;
    }

    /// <summary>
    /// 获取材质 Normal 纹理数组。
    /// </summary>
    public Texture? GetMaterialNormalArray()
    {
        return cachedMaterialNormalArray;
    }

    /// <summary>
    /// 从已配置的路径加载纹理到 GPU。
    /// 用于项目加载后恢复材质纹理。
    /// </summary>
    public void LoadTexturesFromConfiguredPaths(GraphicsDevice graphicsDevice, CommandList commandList)
    {
        foreach (var slot in GetActiveSlots())
        {
            // 加载 Albedo 纹理
            if (!string.IsNullOrEmpty(slot.AlbedoTexturePath) && File.Exists(slot.AlbedoTexturePath) && slot.AlbedoTexture == null)
            {
                var texture = TextureImporter.ImportFromFile(
                    slot.AlbedoTexturePath,
                    graphicsDevice,
                    slot.ImportSize,
                    isNormalMap: false);
                if (texture != null)
                {
                    slot.AlbedoTexture = texture;
                    UpdateSlotInArray(slot.Index, graphicsDevice, commandList);
                }
            }

            // 加载 Normal 纹理
            if (!string.IsNullOrEmpty(slot.NormalTexturePath) && File.Exists(slot.NormalTexturePath) && slot.NormalTexture == null)
            {
                var texture = TextureImporter.ImportFromFile(
                    slot.NormalTexturePath,
                    graphicsDevice,
                    slot.ImportSize,
                    isNormalMap: true);
                if (texture != null)
                {
                    slot.NormalTexture = texture;
                    UpdateNormalSlotInArray(slot.Index, graphicsDevice, commandList);
                }
            }
        }
    }
}
