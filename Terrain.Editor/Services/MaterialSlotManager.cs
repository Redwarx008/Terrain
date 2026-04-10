#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Stride.Core.Diagnostics;
using Stride.Graphics;
using Terrain.Utilities;

namespace Terrain.Editor.Services;

/// <summary>
/// Manages up to 256 material slots and their GPU texture arrays.
/// </summary>
public sealed class MaterialSlotManager
{
    private readonly record struct TextureSignature(int Width, int Height, PixelFormat Format, int MipLevelCount)
    {
        public static TextureSignature FromTexture(Texture texture)
            => new(texture.Width, texture.Height, texture.Format, texture.MipLevelCount);
    }

    private static readonly Lazy<MaterialSlotManager> InstanceFactory = new(() => new());
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor.MaterialSlots");
    private static readonly int[] ArrayCapacityTiers = { 16, 32, 64, 128, 256 };

    private readonly MaterialSlot[] slots = new MaterialSlot[256];
    private Texture? cachedMaterialAlbedoArray;
    private Texture? cachedMaterialNormalArray;
    private Texture? cachedDefaultNormalTexture;
    private TextureSignature? cachedDefaultNormalSignature;

    public static MaterialSlotManager Instance => InstanceFactory.Value;

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

    public bool TrySetAlbedoTexture(
        int slotIndex,
        Texture texture,
        string path,
        TextureSize size,
        GraphicsDevice graphicsDevice,
        CommandList commandList,
        out string? error)
    {
        if (!TryValidateTextureCompatibility(slotIndex, texture, isNormalMap: false, out error))
        {
            texture.Dispose();
            return false;
        }

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
        return true;
    }

    public bool TrySetNormalTexture(
        int slotIndex,
        Texture texture,
        string path,
        GraphicsDevice graphicsDevice,
        CommandList commandList,
        out string? error)
    {
        if (!TryValidateTextureCompatibility(slotIndex, texture, isNormalMap: true, out error))
        {
            texture.Dispose();
            return false;
        }

        var slot = slots[slotIndex];
        slot.NormalTexture?.Dispose();
        slot.NormalTexture = texture;
        slot.NormalTexturePath = path;
        RebuildMaterialArrays(graphicsDevice, commandList);
        return true;
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
        cachedDefaultNormalTexture?.Dispose();
        cachedDefaultNormalTexture = null;
        cachedDefaultNormalSignature = null;
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
                    if (!TryAssignLoadedTexture(slot.Index, texture, slot.AlbedoTexturePath, slot.ImportSize, isNormalMap: false, out string? error))
                    {
                        Log.Warning($"Skipped albedo texture for slot {slot.Index}: {error}");
                    }
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
                    if (!TryAssignLoadedTexture(slot.Index, texture, slot.NormalTexturePath, slot.ImportSize, isNormalMap: true, out string? error))
                    {
                        Log.Warning($"Skipped normal texture for slot {slot.Index}: {error}");
                    }
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
            EnsureTextureLoaded(slot, isNormalMap: true, graphicsDevice, commandList);
        }

        if (maxSlotIndex < 0)
            return;

        int requiredCapacity = GetNextCapacity(maxSlotIndex + 1);

        cachedMaterialAlbedoArray = BuildAlbedoArrayTexture(requiredCapacity, graphicsDevice, commandList);
        cachedMaterialNormalArray = BuildNormalArrayTexture(requiredCapacity, graphicsDevice, commandList);
    }

    private Texture? BuildAlbedoArrayTexture(int requiredCapacity, GraphicsDevice graphicsDevice, CommandList commandList)
    {
        Texture? arrayTexture = null;
        Texture? templateTexture = null;

        foreach (var slot in slots)
        {
            var texture = slot.AlbedoTexture;
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

            if (!IsCompatible(texture, templateTexture))
            {
                Log.Warning($"Encountered incompatible albedo texture in slot {slot.Index} during array rebuild.");
                Debug.Assert(false, "Albedo texture compatibility should have been validated before rebuild.");
                continue;
            }

            CopyTextureToArraySlice(texture, arrayTexture!, slot.Index, commandList);
        }

        return arrayTexture;
    }

    private Texture? BuildNormalArrayTexture(int requiredCapacity, GraphicsDevice graphicsDevice, CommandList commandList)
    {
        Texture? templateTexture = slots
            .Select(static slot => slot.NormalTexture)
            .FirstOrDefault(static texture => texture != null);

        if (templateTexture == null)
            return null;

        var arrayTexture = Texture.New2D(
            graphicsDevice,
            templateTexture.Width,
            templateTexture.Height,
            templateTexture.MipLevelCount,
            templateTexture.Format,
            TextureFlags.ShaderResource,
            arraySize: requiredCapacity);

        var defaultNormalTexture = GetOrCreateDefaultNormalTexture(TextureSignature.FromTexture(templateTexture), graphicsDevice, commandList);

        foreach (var slot in slots)
        {
            if (slot.IsEmpty)
                continue;

            var sourceTexture = slot.NormalTexture ?? defaultNormalTexture;
            if (!IsCompatible(sourceTexture, templateTexture))
            {
                Log.Warning($"Encountered incompatible normal texture in slot {slot.Index} during array rebuild.");
                Debug.Assert(false, "Normal texture compatibility should have been validated before rebuild.");
                CopyTextureToArraySlice(defaultNormalTexture, arrayTexture, slot.Index, commandList);
                continue;
            }

            CopyTextureToArraySlice(sourceTexture, arrayTexture, slot.Index, commandList);
        }

        return arrayTexture;
    }

    private static void CopyTextureToArraySlice(Texture sourceTexture, Texture destinationArray, int slotIndex, CommandList commandList)
    {
        for (int mipLevel = 0; mipLevel < sourceTexture.MipLevelCount; mipLevel++)
        {
            commandList.CopyRegion(
                sourceTexture,
                sourceTexture.GetSubResourceIndex(0, mipLevel),
                null,
                destinationArray,
                destinationArray.GetSubResourceIndex(slotIndex, mipLevel),
                0,
                0,
                0);
        }
    }

    private Texture GetOrCreateDefaultNormalTexture(TextureSignature signature, GraphicsDevice graphicsDevice, CommandList commandList)
    {
        if (cachedDefaultNormalTexture != null && cachedDefaultNormalSignature == signature)
            return cachedDefaultNormalTexture;

        cachedDefaultNormalTexture?.Dispose();
        cachedDefaultNormalTexture = Texture.New2D(
            graphicsDevice,
            signature.Width,
            signature.Height,
            signature.MipLevelCount,
            signature.Format,
            TextureFlags.ShaderResource);

        var mipData = TextureBlockEncoder.CreateFlatNormalMipData(
            signature.Format, signature.Width, signature.Height, signature.MipLevelCount);
        for (int mipLevel = 0; mipLevel < signature.MipLevelCount; mipLevel++)
        {
            cachedDefaultNormalTexture.SetData(commandList, mipData[mipLevel], 0, mipLevel);
        }

        cachedDefaultNormalSignature = signature;
        return cachedDefaultNormalTexture;
    }

    private bool TryValidateTextureCompatibility(int slotIndex, Texture texture, bool isNormalMap, out string? error)
    {
        var templateTexture = GetCompatibilityTemplateTexture(slotIndex, isNormalMap);
        if (templateTexture == null)
        {
            error = null;
            return true;
        }

        if (IsCompatible(texture, templateTexture))
        {
            error = null;
            return true;
        }

        error = BuildCompatibilityError(slotIndex, texture, templateTexture, isNormalMap);
        return false;
    }

    private Texture? GetCompatibilityTemplateTexture(int excludedSlotIndex, bool isNormalMap)
    {
        foreach (var slot in slots)
        {
            if (slot.Index == excludedSlotIndex)
                continue;

            var texture = isNormalMap ? slot.NormalTexture : slot.AlbedoTexture;
            if (texture != null)
                return texture;
        }

        return null;
    }

    private static bool IsCompatible(Texture texture, Texture templateTexture)
    {
        return texture.Width == templateTexture.Width
            && texture.Height == templateTexture.Height
            && texture.Format == templateTexture.Format
            && texture.MipLevelCount == templateTexture.MipLevelCount;
    }

    private static string BuildCompatibilityError(int slotIndex, Texture texture, Texture templateTexture, bool isNormalMap)
    {
        string textureType = isNormalMap ? "normal" : "albedo";
        return $"Rejected {textureType} import for slot {slotIndex}: incompatible array template; width/height/format/mip count must match. Incoming={DescribeTexture(texture)}, Template={DescribeTexture(templateTexture)}";
    }

    private static string DescribeTexture(Texture texture)
    {
        return $"{texture.Width}x{texture.Height}, {texture.Format}, mip={texture.MipLevelCount}";
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

        if (!Instance.TryAssignLoadedTexture(slot.Index, loadedTexture, path, slot.ImportSize, isNormalMap, out string? error))
        {
            Log.Warning($"Skipped {(isNormalMap ? "normal" : "albedo")} texture for slot {slot.Index}: {error}");
            return false;
        }

        return true;
    }

    private bool TryAssignLoadedTexture(int slotIndex, Texture texture, string path, TextureSize size, bool isNormalMap, out string? error)
    {
        if (!TryValidateTextureCompatibility(slotIndex, texture, isNormalMap, out error))
        {
            texture.Dispose();
            return false;
        }

        var slot = slots[slotIndex];
        if (isNormalMap)
        {
            slot.NormalTexture?.Dispose();
            slot.NormalTexture = texture;
            slot.NormalTexturePath = path;
        }
        else
        {
            slot.AlbedoTexture?.Dispose();
            slot.AlbedoTexture = texture;
            slot.AlbedoTexturePath = path;
            slot.ImportSize = size;

            if (string.IsNullOrEmpty(slot.Name) || slot.Name.StartsWith("Texture ", StringComparison.Ordinal))
            {
                slot.Name = Path.GetFileNameWithoutExtension(path);
            }
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
