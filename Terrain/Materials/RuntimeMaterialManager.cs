#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Stride.Core.Diagnostics;
using Stride.Graphics;
using Terrain.Utilities;
using Tommy;

namespace Terrain;

/// <summary>
/// Manages material texture arrays for runtime terrain rendering.
/// Loads albedo and normal textures from file paths and builds Texture2DArray resources.
/// Can read material slot configuration from a TOML project file.
/// </summary>
public sealed class RuntimeMaterialManager : IDisposable
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Materials");
    private static readonly int[] ArrayCapacityTiers = { 16, 32, 64, 128, 256 };

    private Texture? albedoArray;
    private Texture? normalArray;
    private int materialCount;

    public Texture? AlbedoArray => albedoArray;
    public Texture? NormalArray => normalArray;
    public int MaterialCount => materialCount;

    /// <summary>
    /// Initializes material arrays by reading slot configuration from a TOML project file.
    /// </summary>
    public void InitializeFromToml(GraphicsDevice graphicsDevice, CommandList commandList, string tomlFilePath)
    {
        var slots = ReadMaterialSlots(tomlFilePath);
        Initialize(graphicsDevice, commandList, slots);
    }

    /// <summary>
    /// Reads material slot paths from a TOML project file.
    /// Paths in the TOML are resolved relative to the TOML file's directory.
    /// </summary>
    public static List<(int index, string albedoPath, string? normalPath)> ReadMaterialSlots(string tomlFilePath)
    {
        var result = new List<(int index, string albedoPath, string? normalPath)>();
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(tomlFilePath)) ?? "";

        using var reader = File.OpenText(tomlFilePath);
        var root = TOML.Parse(reader);

        if (!root.HasKey("material_slots") || !root["material_slots"].IsArray)
            return result;

        foreach (TomlNode slotNode in root["material_slots"].AsArray)
        {
            if (!slotNode.IsTable)
                continue;

            int index = slotNode.HasKey("index") && slotNode["index"].IsInteger
                ? (int)slotNode["index"].AsInteger
                : -1;

            if (index < 0)
                continue;

            string? albedoPath = slotNode.HasKey("albedo") && slotNode["albedo"].IsString
                ? ResolvePath(slotNode["albedo"].AsString.Value, baseDir)
                : null;

            string? normalPath = slotNode.HasKey("normal") && slotNode["normal"].IsString
                ? ResolvePath(slotNode["normal"].AsString.Value, baseDir)
                : null;

            result.Add((index, albedoPath ?? "", normalPath));
        }

        return result;
    }

    private static string? ResolvePath(string? relativeOrAbsolute, string baseDir)
    {
        if (string.IsNullOrEmpty(relativeOrAbsolute))
            return null;
        if (Path.IsPathRooted(relativeOrAbsolute))
            return relativeOrAbsolute;
        return Path.GetFullPath(Path.Combine(baseDir, relativeOrAbsolute.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// Initializes material arrays from the given file paths.
    /// Each entry is a (albedoPath, normalPath) tuple indexed by slot index.
    /// </summary>
    public void Initialize(
        GraphicsDevice graphicsDevice,
        CommandList commandList,
        IReadOnlyList<(int index, string albedoPath, string? normalPath)> slots)
    {
        Dispose();

        if (slots.Count == 0)
        {
            Log.Warning("No material slots configured for runtime terrain.");
            return;
        }

        int maxSlotIndex = 0;
        foreach (var (index, _, _) in slots)
        {
            maxSlotIndex = Math.Max(maxSlotIndex, index);
        }

        int capacity = GetNextCapacity(maxSlotIndex + 1);
        materialCount = capacity;

        // Load individual textures
        var albedoTextures = new Texture?[capacity];
        var normalTextures = new Texture?[capacity];

        foreach (var (index, albedoPath, normalPath) in slots)
        {
            if (index >= capacity)
                continue;

            if (!string.IsNullOrEmpty(albedoPath) && File.Exists(albedoPath))
            {
                try
                {
                    albedoTextures[index] = LoadTexture(graphicsDevice, albedoPath);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to load albedo texture '{albedoPath}': {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(normalPath) && File.Exists(normalPath))
            {
                try
                {
                    normalTextures[index] = LoadTexture(graphicsDevice, normalPath);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to load normal texture '{normalPath}': {ex.Message}");
                }
            }
        }

        // Find template texture for array creation
        Texture? albedoTemplate = null;
        Texture? normalTemplate = null;
        foreach (var tex in albedoTextures)
        {
            if (tex != null) { albedoTemplate = tex; break; }
        }
        foreach (var tex in normalTextures)
        {
            if (tex != null) { normalTemplate = tex; break; }
        }

        // Build albedo array
        if (albedoTemplate != null)
        {
            albedoArray = Texture.New2D(
                graphicsDevice,
                albedoTemplate.Width,
                albedoTemplate.Height,
                albedoTemplate.MipLevelCount,
                albedoTemplate.Format,
                TextureFlags.ShaderResource,
                arraySize: capacity);

            for (int i = 0; i < capacity; i++)
            {
                if (albedoTextures[i] != null)
                {
                    CopyTextureToArraySlice(albedoTextures[i], albedoArray, i, commandList);
                    albedoTextures[i]?.Dispose();
                }
            }
        }

        // Build normal array
        var normalArrayTemplate = normalTemplate ?? albedoTemplate;
        if (normalArrayTemplate != null)
        {
            PixelFormat normalArrayFormat = normalTemplate?.Format ?? PixelFormat.R8G8B8A8_UNorm;
            normalArray = Texture.New2D(
                graphicsDevice,
                normalArrayTemplate.Width,
                normalArrayTemplate.Height,
                normalArrayTemplate.MipLevelCount,
                normalArrayFormat,
                TextureFlags.ShaderResource,
                arraySize: capacity);

            // Create default flat normal (128,128,255,255)
            var defaultNormal = CreateDefaultNormalTexture(
                graphicsDevice,
                commandList,
                normalArrayTemplate.Width,
                normalArrayTemplate.Height,
                normalArrayTemplate.MipLevelCount,
                normalArrayFormat);

            for (int i = 0; i < capacity; i++)
            {
                var source = normalTextures[i] ?? defaultNormal;
                CopyTextureToArraySlice(source, normalArray, i, commandList);
                normalTextures[i]?.Dispose();
            }

            defaultNormal.Dispose();
        }
    }

    private static Texture LoadTexture(GraphicsDevice graphicsDevice, string path)
    {
        using var stream = File.OpenRead(path);
        return Texture.Load(graphicsDevice, stream);
    }

    private static void CopyTextureToArraySlice(Texture source, Texture destinationArray, int sliceIndex, CommandList commandList)
    {
        for (int mipLevel = 0; mipLevel < source.MipLevelCount; mipLevel++)
        {
            commandList.CopyRegion(
                source,
                source.GetSubResourceIndex(0, mipLevel),
                null,
                destinationArray,
                destinationArray.GetSubResourceIndex(sliceIndex, mipLevel),
                0, 0, 0);
        }
    }

    private static Texture CreateDefaultNormalTexture(GraphicsDevice graphicsDevice, CommandList commandList, int width, int height, int mipCount, PixelFormat format)
    {
        var texture = Texture.New2D(graphicsDevice, width, height, mipCount, format, TextureFlags.ShaderResource);
        var mipData = TextureBlockEncoder.CreateFlatNormalMipData(format, width, height, mipCount);
        for (int mip = 0; mip < mipCount; mip++)
            texture.SetData(commandList, mipData[mip], 0, mip);
        return texture;
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

    public void Dispose()
    {
        albedoArray?.Dispose();
        albedoArray = null;
        normalArray?.Dispose();
        normalArray = null;
        materialCount = 0;
    }
}
