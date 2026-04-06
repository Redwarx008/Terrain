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
}
