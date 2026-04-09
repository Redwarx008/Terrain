#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stride.Graphics;

namespace Terrain.Editor.Services;

/// <summary>
/// Manages up to 256 material slots and their GPU texture arrays.
/// </summary>
public sealed class MaterialSlotManager
{
    private static readonly Lazy<MaterialSlotManager> InstanceFactory = new(() => new());
    public static MaterialSlotManager Instance => InstanceFactory.Value;

    private readonly MaterialSlot[] slots = new MaterialSlot[256];
    private static readonly int[] ArrayCapacityTiers = { 16, 32, 64, 128, 256 };

    private Texture? cachedMaterialAlbedoArray;
    private Texture? cachedMaterialNormalArray;

    public int SelectedSlotIndex { get; set; }
    public MaterialSlot SelectedSlot => slots[SelectedSlotIndex];
    public MaterialSlot this[int index] => slots[index];

    public int NextAvailableSlotIndex
    {
        get
        {
            var emptySlot = slots.FirstOrDefault(static slot => slot.IsEmpty);
            return emptySlot?.Index ?? -1;
        }
    }

    private MaterialSlotManager()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = new MaterialSlot { Index = i };
        }
    }

    public IEnumerable<MaterialSlot> GetActiveSlots()
    {
        return slots.Where(static slot => !slot.IsEmpty);
    }

    public int ActiveSlotCount => slots.Count(static slot => !slot.IsEmpty);

    public void MarkMaterialArrayDirty()
    {
        cachedMaterialAlbedoArray?.Dispose();
        cachedMaterialAlbedoArray = null;
        cachedMaterialNormalArray?.Dispose();
        cachedMaterialNormalArray = null;
    }

    public void SetAlbedoTexture(
        int slotIndex,
        Texture texture,
        string path,
        TextureSize size,
        GraphicsDevice graphicsDevice,
        CommandList commandList)
    {
        var slot = slots[slotIndex];
        slot.AlbedoTexture?.Dispose();
        slot.AlbedoTexture = texture;
        slot.AlbedoTexturePath = path;
        slot.ImportSize = size;

        if (string.IsNullOrEmpty(slot.Name) || slot.Name.StartsWith("Texture ", StringComparison.Ordinal))
        {
            slot.Name = Path.GetFileNameWithoutExtension(path);
        }

        RebuildMaterialArrays(graphicsDevice, commandList);
    }

    public void SetNormalTexture(
        int slotIndex,
        Texture texture,
        string path,
        GraphicsDevice graphicsDevice,
        CommandList commandList)
    {
        var slot = slots[slotIndex];
        slot.NormalTexture?.Dispose();
        slot.NormalTexture = texture;
        slot.NormalTexturePath = path;
        RebuildMaterialArrays(graphicsDevice, commandList);
    }

    public void ClearNormalTexture(int slotIndex, GraphicsDevice graphicsDevice, CommandList commandList)
    {
        var slot = slots[slotIndex];
        slot.NormalTexture?.Dispose();
        slot.NormalTexture = null;
        slot.NormalTexturePath = null;
        RebuildMaterialArrays(graphicsDevice, commandList);
    }

    public void ClearSlot(int slotIndex, GraphicsDevice graphicsDevice, CommandList commandList)
    {
        slots[slotIndex].Clear();
        RebuildMaterialArrays(graphicsDevice, commandList);
    }

    public void ClearAll()
    {
        foreach (var slot in slots)
        {
            slot.Clear();
        }

        MarkMaterialArrayDirty();
    }

    public Texture? GetMaterialAlbedoArray()
    {
        return cachedMaterialAlbedoArray;
    }

    public Texture? GetMaterialNormalArray()
    {
        return cachedMaterialNormalArray;
    }

    public void LoadTexturesFromConfiguredPaths(GraphicsDevice graphicsDevice, CommandList commandList)
    {
        foreach (var slot in GetActiveSlots())
        {
            if (!string.IsNullOrEmpty(slot.AlbedoTexturePath) && File.Exists(slot.AlbedoTexturePath) && slot.AlbedoTexture == null)
            {
                var texture = TextureImporter.ImportFromFile(
                    slot.AlbedoTexturePath,
                    graphicsDevice,
                    commandList,
                    slot.ImportSize,
                    isNormalMap: false);
                if (texture != null)
                {
                    slot.AlbedoTexture = texture;
                }
            }

            if (!string.IsNullOrEmpty(slot.NormalTexturePath) && File.Exists(slot.NormalTexturePath) && slot.NormalTexture == null)
            {
                var texture = TextureImporter.ImportFromFile(
                    slot.NormalTexturePath,
                    graphicsDevice,
                    commandList,
                    slot.ImportSize,
                    isNormalMap: true);
                if (texture != null)
                {
                    slot.NormalTexture = texture;
                }
            }
        }

        RebuildMaterialArrays(graphicsDevice, commandList);
    }

    private void RebuildMaterialArrays(GraphicsDevice graphicsDevice, CommandList commandList)
    {
        MarkMaterialArrayDirty();

        int maxSlotIndex = -1;

        foreach (var slot in slots)
        {
            if (!EnsureTextureLoaded(slot, isNormalMap: false, graphicsDevice, commandList))
                continue;

            maxSlotIndex = Math.Max(maxSlotIndex, slot.Index);
        }

        foreach (var slot in slots)
        {
            if (!EnsureTextureLoaded(slot, isNormalMap: true, graphicsDevice, commandList))
                continue;

            maxSlotIndex = Math.Max(maxSlotIndex, slot.Index);
        }

        if (maxSlotIndex < 0)
            return;

        int requiredCapacity = GetNextCapacity(maxSlotIndex + 1);

        cachedMaterialAlbedoArray = BuildArrayTexture(
            slots,
            static slot => slot.AlbedoTexture,
            requiredCapacity,
            graphicsDevice,
            commandList);

        cachedMaterialNormalArray = BuildArrayTexture(
            slots,
            static slot => slot.NormalTexture,
            requiredCapacity,
            graphicsDevice,
            commandList);
    }

    private static Texture? BuildArrayTexture(
        IReadOnlyList<MaterialSlot> sourceSlots,
        Func<MaterialSlot, Texture?> textureSelector,
        int requiredCapacity,
        GraphicsDevice graphicsDevice,
        CommandList commandList)
    {
        Texture? arrayTexture = null;
        Texture? templateTexture = null;

        foreach (var slot in sourceSlots)
        {
            var texture = textureSelector(slot);
            if (texture == null)
                continue;

            if (templateTexture == null)
            {
                templateTexture = texture;
                arrayTexture = Texture.New2D(
                    graphicsDevice,
                    templateTexture.Width,
                    templateTexture.Height,
                    templateTexture.MipLevelCount,
                    templateTexture.Format,
                    TextureFlags.ShaderResource,
                    arraySize: requiredCapacity);
            }

            if (!IsCompatibleWithTemplate(texture, templateTexture))
                continue;

            for (int mipLevel = 0; mipLevel < templateTexture.MipLevelCount; mipLevel++)
            {
                commandList.CopyRegion(
                    texture,
                    texture.GetSubResourceIndex(0, mipLevel),
                    null,
                    arrayTexture!,
                    arrayTexture!.GetSubResourceIndex(slot.Index, mipLevel),
                    0,
                    0,
                    0);
            }
        }

        return arrayTexture;
    }

    private static bool IsCompatibleWithTemplate(Texture texture, Texture templateTexture)
    {
        return texture.Width == templateTexture.Width
            && texture.Height == templateTexture.Height
            && texture.Format == templateTexture.Format
            && texture.MipLevelCount == templateTexture.MipLevelCount;
    }

    private static bool EnsureTextureLoaded(MaterialSlot slot, bool isNormalMap, GraphicsDevice graphicsDevice, CommandList commandList)
    {
        string? path = isNormalMap ? slot.NormalTexturePath : slot.AlbedoTexturePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        var currentTexture = isNormalMap ? slot.NormalTexture : slot.AlbedoTexture;
        if (currentTexture != null)
            return true;

        var loadedTexture = TextureImporter.ImportFromFile(path, graphicsDevice, commandList, slot.ImportSize, isNormalMap);
        if (loadedTexture == null)
            return false;

        if (isNormalMap)
        {
            slot.NormalTexture = loadedTexture;
        }
        else
        {
            slot.AlbedoTexture = loadedTexture;
        }

        return true;
    }

    private static int GetNextCapacity(int requiredSize)
    {
        foreach (var tier in ArrayCapacityTiers)
        {
            if (tier >= requiredSize)
                return tier;
        }

        return 256;
    }
}
