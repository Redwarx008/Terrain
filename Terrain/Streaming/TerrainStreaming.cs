#nullable enable

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32.SafeHandles;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace Terrain;

/// <summary>
/// Unified terrain chunk node structure for both rendering and LOD lookup.
/// Render nodes (state=Stop) are placed at the beginning of the buffer,
/// internal nodes (state=Subdivided) are placed after them.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TerrainChunkNode
{
    public Int4 NodeInfo;    // chunkX, chunkY, lodLevel, state (Stop=0, Subdivided=1)
    public Int4 StreamInfo;  // heightSliceIndex, pageOffsetX, pageOffsetY, pageTexelStride
    public Stride.Core.Mathematics.Vector4 SplatInfo;   // splatSliceIndex, splatPageOffsetX, splatPageOffsetY, splatPageTexelStride
}

internal enum TerrainLodLookupNodeState : uint
{
    Stop = 0,
    Subdivided = 1,
}

[StructLayout(LayoutKind.Sequential)]
internal struct TerrainLodLookupLayout
{
    public Int4 LayoutInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TerrainLodLookupEntry
{
    public uint Subdivided;
}

internal readonly struct TerrainChunkKey : IEquatable<TerrainChunkKey>
{
    public TerrainChunkKey(int lodLevel, int chunkX, int chunkY)
    {
        LodLevel = lodLevel;
        ChunkX = chunkX;
        ChunkY = chunkY;
    }

    public int LodLevel { get; }
    public int ChunkX { get; }
    public int ChunkY { get; }

    public bool Equals(TerrainChunkKey other)
        => LodLevel == other.LodLevel && ChunkX == other.ChunkX && ChunkY == other.ChunkY;

    public override bool Equals(object? obj)
        => obj is TerrainChunkKey other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(LodLevel, ChunkX, ChunkY);

    public override string ToString()
        => $"Lod={LodLevel}, X={ChunkX}, Y={ChunkY}";
}

internal readonly struct TerrainPageKey : IEquatable<TerrainPageKey>
{
    public TerrainPageKey(int mipLevel, int pageX, int pageY)
    {
        MipLevel = mipLevel;
        PageX = pageX;
        PageY = pageY;
    }

    public int MipLevel { get; }
    public int PageX { get; }
    public int PageY { get; }

    public bool Equals(TerrainPageKey other)
        => MipLevel == other.MipLevel && PageX == other.PageX && PageY == other.PageY;

    public override bool Equals(object? obj)
        => obj is TerrainPageKey other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(MipLevel, PageX, PageY);

    public override string ToString()
        => $"Mip={MipLevel}, X={PageX}, Y={PageY}";
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct TerrainFileHeader
{
    public const int MagicValue = 0x52524554;
    public const int MinSupportedVersion = 6;
    public const int MaxSupportedVersion = 7;

    public int Magic;
    public int Version;
    public int Width;
    public int Height;
    public int LeafNodeSize;
    public int TileSize;
    public int Padding;
    public int HeightMapMipLevels;
    public int SplatMapFormat;
    public int SplatMapMipLevels;
    public int SplatMapResolutionRatio;
    public int RiverMapFormat;
    public int RiverMapMipLevels;
    public int RiverMapResolutionRatio;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct TerrainVirtualTextureHeader
{
    public int Width;
    public int Height;
    public int TileSize;
    public int Padding;
    public int BytesPerPixel;
    public int Mipmaps;
}

internal interface ITerrainFileReader : IDisposable
{
    TerrainFileHeader Header { get; }
    TerrainVirtualTextureHeader HeightmapHeader { get; }
    TerrainVirtualTextureHeader SplatMapHeader { get; }
    int SplatMapResolutionRatio { get; }
    int SplatMapMipCount { get; }
    TerrainMinMaxErrorMap[] ReadAllMinMaxErrorMaps();
    ushort[] ReadAllHeightData();
    void ReadHeightPage(TerrainPageKey key, Span<byte> destination);
}

internal sealed class TerrainFileReader : ITerrainFileReader
{
    private const int MaxTerrainDimension = 1 << 16;
    private readonly SafeFileHandle fileHandle;
    private readonly TerrainMinMaxErrorMap[] minMaxErrorMaps;
    private readonly TerrainVirtualTextureHeader heightmapHeader;
    private readonly TerrainMipLayout[] heightmapMipLayouts;
    private readonly int tileByteSize;
    private readonly TerrainVirtualTextureHeader splatMapHeader;
    private readonly TerrainMipLayout[] splatMapMipLayouts;
    private readonly int splatMapTileByteSize;
    private readonly TerrainVirtualTextureHeader? riverMapHeader;
    private readonly TerrainMipLayout[] riverMapMipLayouts;
    private readonly int riverMapTileByteSize;

    public TerrainFileReader(string path)
    {
        fileHandle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        long offset = 0;

        Header = ReadStruct<TerrainFileHeader>(fileHandle, ref offset);
        if (Header.Magic != TerrainFileHeader.MagicValue)
        {
            throw new InvalidDataException($"'{path}' is not a valid .terrain file.");
        }

        int mapCount = ReadInt32(fileHandle, ref offset);
        ValidateHeader(Header, mapCount);
        minMaxErrorMaps = new TerrainMinMaxErrorMap[mapCount];
        for (int i = 0; i < mapCount; i++)
        {
            minMaxErrorMaps[i] = ReadMinMaxErrorMap(fileHandle, ref offset);
        }

        heightmapHeader = ReadStruct<TerrainVirtualTextureHeader>(fileHandle, ref offset);
        ValidateHeightmapHeader(Header, heightmapHeader);
        int paddedTileSize = checked(heightmapHeader.TileSize + heightmapHeader.Padding * 2);
        tileByteSize = checked(paddedTileSize * paddedTileSize * heightmapHeader.BytesPerPixel);

        heightmapMipLayouts = new TerrainMipLayout[heightmapHeader.Mipmaps];
        long currentOffset = offset;
        for (int mip = 0; mip < heightmapHeader.Mipmaps; mip++)
        {
            VirtualTextureMipLayoutInfo layoutInfo = VirtualTextureLayout.GetMipLayout(
                heightmapHeader.Width,
                heightmapHeader.Height,
                heightmapHeader.TileSize,
                mip);
            heightmapMipLayouts[mip] = new TerrainMipLayout(layoutInfo.Width, layoutInfo.Height, layoutInfo.TilesX, layoutInfo.TilesY, currentOffset);
            currentOffset = checked(currentOffset + checked((long)layoutInfo.TilesX * layoutInfo.TilesY * tileByteSize));
        }

        // v6+: this VT block stores the authored biome mask.
        splatMapHeader = ReadStruct<TerrainVirtualTextureHeader>(fileHandle, ref currentOffset);
        int splatMapPaddedTileSize = checked(splatMapHeader.TileSize + splatMapHeader.Padding * 2);
        splatMapTileByteSize = checked(splatMapPaddedTileSize * splatMapPaddedTileSize * splatMapHeader.BytesPerPixel);

        splatMapMipLayouts = new TerrainMipLayout[splatMapHeader.Mipmaps];
        for (int mip = 0; mip < splatMapHeader.Mipmaps; mip++)
        {
            VirtualTextureMipLayoutInfo layoutInfo = VirtualTextureLayout.GetMipLayout(
                splatMapHeader.Width,
                splatMapHeader.Height,
                splatMapHeader.TileSize,
                mip);
            splatMapMipLayouts[mip] = new TerrainMipLayout(layoutInfo.Width, layoutInfo.Height, layoutInfo.TilesX, layoutInfo.TilesY, currentOffset);
            currentOffset = checked(currentOffset + checked((long)layoutInfo.TilesX * layoutInfo.TilesY * splatMapTileByteSize));
        }

        if (Header.Version >= 7 && Header.RiverMapFormat != 0 && Header.RiverMapMipLevels > 0)
        {
            riverMapHeader = ReadStruct<TerrainVirtualTextureHeader>(fileHandle, ref currentOffset);
            int riverMapPaddedTileSize = checked(riverMapHeader.Value.TileSize + riverMapHeader.Value.Padding * 2);
            riverMapTileByteSize = checked(riverMapPaddedTileSize * riverMapPaddedTileSize * riverMapHeader.Value.BytesPerPixel);

            riverMapMipLayouts = new TerrainMipLayout[riverMapHeader.Value.Mipmaps];
            for (int mip = 0; mip < riverMapHeader.Value.Mipmaps; mip++)
            {
                VirtualTextureMipLayoutInfo layoutInfo = VirtualTextureLayout.GetMipLayout(
                    riverMapHeader.Value.Width,
                    riverMapHeader.Value.Height,
                    riverMapHeader.Value.TileSize,
                    mip);
                riverMapMipLayouts[mip] = new TerrainMipLayout(layoutInfo.Width, layoutInfo.Height, layoutInfo.TilesX, layoutInfo.TilesY, currentOffset);
                currentOffset = checked(currentOffset + checked((long)layoutInfo.TilesX * layoutInfo.TilesY * riverMapTileByteSize));
            }
        }
        else
        {
            riverMapHeader = null;
            riverMapMipLayouts = Array.Empty<TerrainMipLayout>();
            riverMapTileByteSize = 0;
        }
    }

    public TerrainFileHeader Header { get; }

    public TerrainVirtualTextureHeader HeightmapHeader => heightmapHeader;

    /// <summary>
    /// v6 起这里持久化的是 BiomeMask，而不是预烘焙的 detail index map。
    /// </summary>
    public TerrainVirtualTextureHeader SplatMapHeader => splatMapHeader;
    public TerrainVirtualTextureHeader? RiverMapHeader => riverMapHeader;

    /// <summary>
    /// Splatmap 与 heightmap 的分辨率比。1 = 同分辨率（legacy v2），2 = 半分辨率（v3）。
    /// </summary>
    public int SplatMapResolutionRatio =>
        Header.Version >= 3 ? Header.SplatMapResolutionRatio : 1;

    public int RiverMapResolutionRatio =>
        Header.Version >= 7 ? Header.RiverMapResolutionRatio : 1;

    public int SplatMapMipCount => splatMapMipLayouts.Length;
    public int RiverMapMipCount => riverMapMipLayouts.Length;

    public TerrainMinMaxErrorMap[] ReadAllMinMaxErrorMaps()
        => minMaxErrorMaps;

    public ushort[] ReadAllHeightData()
        => ReadAllVirtualTextureData<ushort>(heightmapHeader, heightmapMipLayouts, tileByteSize);

    public byte[] ReadAllBiomeMaskData()
        => ReadAllVirtualTextureData<byte>(splatMapHeader, splatMapMipLayouts, splatMapTileByteSize);

    public byte[] ReadAllRiverMaskData()
    {
        if (riverMapHeader == null || riverMapMipLayouts.Length == 0)
            return Array.Empty<byte>();

        return ReadAllVirtualTextureData<byte>(riverMapHeader.Value, riverMapMipLayouts, riverMapTileByteSize);
    }

    public void ReadHeightPage(TerrainPageKey key, Span<byte> destination)
    {
        if ((uint)key.MipLevel >= (uint)heightmapMipLayouts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(key), $"Invalid mip level {key.MipLevel}.");
        }

        if (destination.Length < tileByteSize)
        {
            throw new ArgumentException($"Destination buffer must be at least {tileByteSize} bytes.", nameof(destination));
        }

        ref readonly var layout = ref heightmapMipLayouts[key.MipLevel];
        if ((uint)key.PageX >= (uint)layout.TilesX || (uint)key.PageY >= (uint)layout.TilesY)
        {
            throw new ArgumentOutOfRangeException(nameof(key), $"Invalid page coordinates ({key.PageX}, {key.PageY}) for mip {key.MipLevel}.");
        }

        long offset = layout.Offset + (long)(key.PageY * layout.TilesX + key.PageX) * tileByteSize;
        ReadExactly(fileHandle, destination[..tileByteSize], offset);
    }

    public void ReadSplatMapPage(TerrainPageKey key, Span<byte> destination)
    {
        if ((uint)key.MipLevel >= (uint)splatMapMipLayouts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(key), $"Invalid mip level {key.MipLevel}.");
        }

        if (destination.Length < splatMapTileByteSize)
        {
            throw new ArgumentException($"Destination buffer must be at least {splatMapTileByteSize} bytes.", nameof(destination));
        }

        ref readonly var layout = ref splatMapMipLayouts[key.MipLevel];
        if ((uint)key.PageX >= (uint)layout.TilesX || (uint)key.PageY >= (uint)layout.TilesY)
        {
            throw new ArgumentOutOfRangeException(nameof(key), $"Invalid page coordinates ({key.PageX}, {key.PageY}) for mip {key.MipLevel}.");
        }

        long offset = layout.Offset + (long)(key.PageY * layout.TilesX + key.PageX) * splatMapTileByteSize;
        ReadExactly(fileHandle, destination[..splatMapTileByteSize], offset);
    }

    public void Dispose()
    {
        fileHandle.Dispose();
    }

    private static T ReadStruct<T>(SafeFileHandle fileHandle, ref long fileOffset) where T : unmanaged
    {
        Span<byte> bytes = stackalloc byte[Unsafe.SizeOf<T>()];
        ReadExactly(fileHandle, bytes, fileOffset);
        fileOffset += bytes.Length;
        return MemoryMarshal.Read<T>(bytes);
    }

    private static int ReadInt32(SafeFileHandle fileHandle, ref long fileOffset)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        ReadExactly(fileHandle, bytes, fileOffset);
        fileOffset += bytes.Length;
        return MemoryMarshal.Read<int>(bytes);
    }

    private static TerrainMinMaxErrorMap ReadMinMaxErrorMap(SafeFileHandle fileHandle, ref long fileOffset)
    {
        int width = ReadInt32(fileHandle, ref fileOffset);
        int height = ReadInt32(fileHandle, ref fileOffset);
        var map = new TerrainMinMaxErrorMap(width, height);
        Span<byte> byteView = map.GetByteView();
        ReadExactly(fileHandle, byteView, fileOffset);
        fileOffset += byteView.Length;
        return map;
    }

    private T[] ReadAllVirtualTextureData<T>(TerrainVirtualTextureHeader header, TerrainMipLayout[] layouts, int tileByteSize)
        where T : unmanaged
    {
        if (layouts.Length == 0)
        {
            return Array.Empty<T>();
        }

        int bytesPerPixel = Unsafe.SizeOf<T>();
        if (header.BytesPerPixel != bytesPerPixel)
        {
            throw new InvalidDataException($"Unexpected VT bytes-per-pixel {header.BytesPerPixel}. Expected {bytesPerPixel}.");
        }

        T[] result = new T[checked(header.Width * header.Height)];
        int paddedTileSize = header.TileSize + header.Padding * 2;
        byte[] pageBytes = new byte[tileByteSize];
        Span<T> pagePixels = MemoryMarshal.Cast<byte, T>(pageBytes.AsSpan());
        ref readonly TerrainMipLayout layout = ref layouts[0];
        int pageStride = header.TileSize - 1;

        for (int pageY = 0; pageY < layout.TilesY; pageY++)
        {
            for (int pageX = 0; pageX < layout.TilesX; pageX++)
            {
                long offset = layout.Offset + (long)(pageY * layout.TilesX + pageX) * tileByteSize;
                ReadExactly(fileHandle, pageBytes, offset);

                int destOriginX = pageX * pageStride;
                int destOriginY = pageY * pageStride;
                for (int localY = 0; localY < header.TileSize; localY++)
                {
                    int destY = destOriginY + localY;
                    if ((uint)destY >= (uint)header.Height)
                    {
                        continue;
                    }

                    int srcRow = (localY + header.Padding) * paddedTileSize + header.Padding;
                    int destRow = destY * header.Width;
                    for (int localX = 0; localX < header.TileSize; localX++)
                    {
                        int destX = destOriginX + localX;
                        if ((uint)destX >= (uint)header.Width)
                        {
                            continue;
                        }

                        result[destRow + destX] = pagePixels[srcRow + localX];
                    }
                }
            }
        }

        return result;
    }

    private static void ReadExactly(SafeFileHandle fileHandle, Span<byte> destination, long fileOffset)
    {
        int totalRead = 0;
        while (totalRead < destination.Length)
        {
            int bytesRead = RandomAccess.Read(fileHandle, destination[totalRead..], fileOffset + totalRead);
            if (bytesRead <= 0)
            {
                throw new EndOfStreamException("Unexpected end of terrain file while reading a page.");
            }

            totalRead += bytesRead;
        }
    }

    private static int DivideRoundUp(int value, int divisor)
        => (value + divisor - 1) / divisor;

    private static void ValidateHeader(TerrainFileHeader header, int mapCount)
    {
        if (header.Version < TerrainFileHeader.MinSupportedVersion || header.Version > TerrainFileHeader.MaxSupportedVersion)
        {
            throw new InvalidDataException($"Unsupported terrain file version {header.Version}. Expected {TerrainFileHeader.MinSupportedVersion}-{TerrainFileHeader.MaxSupportedVersion}.");
        }

        if (header.Width <= 1 || header.Height <= 1 || header.Width > MaxTerrainDimension || header.Height > MaxTerrainDimension)
        {
            throw new InvalidDataException($"Invalid terrain dimensions {header.Width}x{header.Height}.");
        }

        if (header.LeafNodeSize is not (16 or 32 or 64))
        {
            throw new InvalidDataException($"Unsupported leaf node size {header.LeafNodeSize}. Expected 16, 32, or 64.");
        }

        if (header.HeightMapMipLevels <= 0)
        {
            throw new InvalidDataException($"Invalid heightmap mip count {header.HeightMapMipLevels}.");
        }

        int chunkCountX = DivideRoundUp(header.Width - 1, header.LeafNodeSize);
        int chunkCountY = DivideRoundUp(header.Height - 1, header.LeafNodeSize);
        int expectedMapCount = 0;
        while (chunkCountX > 0 && chunkCountY > 0)
        {
            expectedMapCount++;
            if (chunkCountX == 1 && chunkCountY == 1)
            {
                break;
            }

            chunkCountX = Math.Max(1, (chunkCountX + 1) / 2);
            chunkCountY = Math.Max(1, (chunkCountY + 1) / 2);
        }

        if (mapCount != expectedMapCount)
        {
            throw new InvalidDataException($"Invalid MinMaxErrorMap count {mapCount}. Expected {expectedMapCount}.");
        }
    }

    private static void ValidateHeightmapHeader(TerrainFileHeader header, TerrainVirtualTextureHeader heightmapHeader)
    {
        if (heightmapHeader.BytesPerPixel != sizeof(ushort))
        {
            throw new InvalidDataException($"Unsupported heightmap block format. Expected 16-bit heights, got {heightmapHeader.BytesPerPixel} bytes per pixel.");
        }

        if (heightmapHeader.Width != header.Width || heightmapHeader.Height != header.Height)
        {
            throw new InvalidDataException(
                $"Heightmap dimensions {heightmapHeader.Width}x{heightmapHeader.Height} do not match terrain dimensions {header.Width}x{header.Height}.");
        }

        if (heightmapHeader.TileSize != header.TileSize || heightmapHeader.Padding != header.Padding)
        {
            throw new InvalidDataException("Heightmap tile metadata does not match the terrain header.");
        }

        if (heightmapHeader.TileSize is not (129 or 257 or 513))
        {
            throw new InvalidDataException($"Unsupported heightmap tile size {heightmapHeader.TileSize}. Expected 129, 257, or 513.");
        }

        if (heightmapHeader.Padding < 0 || heightmapHeader.Padding > 8)
        {
            throw new InvalidDataException($"Unsupported heightmap padding {heightmapHeader.Padding}.");
        }

        if (heightmapHeader.Mipmaps != header.HeightMapMipLevels)
        {
            throw new InvalidDataException(
                $"Heightmap mip count {heightmapHeader.Mipmaps} does not match terrain header mip count {header.HeightMapMipLevels}.");
        }

        int expectedMipCount = VirtualTextureLayout.GetMipCount(heightmapHeader.Width, heightmapHeader.Height, heightmapHeader.TileSize);
        if (heightmapHeader.Mipmaps != expectedMipCount)
        {
            throw new InvalidDataException(
                $"Heightmap mip count {heightmapHeader.Mipmaps} does not match the shared VT layout rule; expected {expectedMipCount}.");
        }
    }

    private readonly record struct TerrainMipLayout(int Width, int Height, int TilesX, int TilesY, long Offset);
}

internal sealed class GpuVirtualTextureArray : IDisposable
{
    private readonly Dictionary<TerrainPageKey, int> pageToSlice = new();
    private readonly Queue<int> freeSlices = new();
    private readonly LinkedList<int> lruSlices = new();
    private readonly LinkedListNode<int>?[] lruNodes;
    private readonly SlotState[] slots;

    public GpuVirtualTextureArray(Texture textureArray, int tileSize, int padding, int maxResidentChunks)
    {
        TileSize = tileSize;
        Padding = padding;
        TextureArray = textureArray;

        slots = new SlotState[maxResidentChunks];
        lruNodes = new LinkedListNode<int>?[maxResidentChunks];
        for (int i = 0; i < maxResidentChunks; i++)
        {
            freeSlices.Enqueue(i);
        }
    }

    public Texture TextureArray { get; }

    public int TileSize { get; }

    public int Padding { get; }

    public int Capacity => slots.Length;

    public bool IsPageResident(TerrainPageKey key)
        => pageToSlice.ContainsKey(key);

    public bool TryGetResidentSlice(TerrainPageKey key, out int sliceIndex)
    {
        if (pageToSlice.TryGetValue(key, out sliceIndex))
        {
            TouchSlice(sliceIndex);
            return true;
        }

        sliceIndex = -1;
        return false;
    }

    public bool UploadPage(CommandList commandList, TerrainPageKey key, Span<byte> data, bool pinned)
    {
        if (!TryAllocateSlice(key, out int sliceIndex))
        {
            return false;
        }

        bool isPinned = pinned || (slots[sliceIndex].IsOccupied && slots[sliceIndex].Key.Equals(key) && slots[sliceIndex].IsPinned);
        TextureArray.SetData<byte>(commandList, data, sliceIndex, 0, null);
        pageToSlice[key] = sliceIndex;
        slots[sliceIndex] = new SlotState
        {
            IsOccupied = true,
            IsPinned = isPinned,
            Key = key,
        };
        TouchSlice(sliceIndex);
        return true;
    }

    public bool TrySetPinned(TerrainPageKey key, bool pinned)
    {
        if (!pageToSlice.TryGetValue(key, out int sliceIndex))
        {
            return false;
        }

        slots[sliceIndex].IsPinned = pinned;
        TouchSlice(sliceIndex);
        return true;
    }

    public void Dispose()
    {
    }

    private bool TryAllocateSlice(TerrainPageKey key, out int sliceIndex)
    {
        if (pageToSlice.TryGetValue(key, out sliceIndex))
        {
            TouchSlice(sliceIndex);
            return true;
        }

        if (freeSlices.Count > 0)
        {
            sliceIndex = freeSlices.Dequeue();
            return true;
        }

        if (TryEvictLeastRecentlyUsed(out sliceIndex))
        {
            return true;
        }

        sliceIndex = -1;
        return false;
    }

    private bool TryEvictLeastRecentlyUsed(out int sliceIndex)
    {
        while (lruSlices.First is { } node)
        {
            sliceIndex = node.Value;
            if (!slots[sliceIndex].IsOccupied || slots[sliceIndex].IsPinned)
            {
                RemoveFromLru(sliceIndex);
                continue;
            }

            // We are immediately reusing this slice for the incoming page, so do not
            // put it back into the free queue here. Otherwise the same slice can be
            // handed out again later while it is already occupied, corrupting page-to-slice
            // mappings after enough streaming churn.
            EvictSlice(sliceIndex, enqueueFreeSlice: false);
            return true;
        }

        sliceIndex = -1;
        return false;
    }

    private void EvictSlice(int sliceIndex, bool enqueueFreeSlice = true)
    {
        ref var slot = ref slots[sliceIndex];
        if (!slot.IsOccupied)
        {
            return;
        }

        pageToSlice.Remove(slot.Key);
        RemoveFromLru(sliceIndex);
        slot = default;
        if (enqueueFreeSlice)
        {
            freeSlices.Enqueue(sliceIndex);
        }
    }

    private void TouchSlice(int sliceIndex)
    {
        if (!slots[sliceIndex].IsOccupied || slots[sliceIndex].IsPinned)
        {
            RemoveFromLru(sliceIndex);
            return;
        }

        LinkedListNode<int>? node = lruNodes[sliceIndex];
        if (node == null)
        {
            lruNodes[sliceIndex] = lruSlices.AddLast(sliceIndex);
            return;
        }

        if (!ReferenceEquals(node, lruSlices.Last))
        {
            lruSlices.Remove(node);
            lruSlices.AddLast(node);
        }
    }

    private void RemoveFromLru(int sliceIndex)
    {
        LinkedListNode<int>? node = lruNodes[sliceIndex];
        if (node == null)
        {
            return;
        }

        lruSlices.Remove(node);
        lruNodes[sliceIndex] = null;
    }

    private struct SlotState
    {
        public bool IsOccupied;
        public bool IsPinned;
        public TerrainPageKey Key;
    }
}

internal sealed class TerrainStreamingManager : IDisposable
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Quantum");
    private const int DetailControlBytesPerPixel = 4;
    private readonly ITerrainFileReader fileReader;
    private readonly GpuVirtualTextureArray gpuHeightArray;
    private readonly GpuVirtualTextureArray? gpuDetailIndexArray;
    private readonly Texture? detailWeightArray;
    private readonly RuntimeDetailMapData? generatedDetailMaps;
    private readonly BlockingCollection<StreamingRequest> pendingRequests = new();
    private readonly ConcurrentQueue<StreamingRequest> completedRequests = new();
    private readonly ConcurrentDictionary<(TerrainPageKey Key, bool IsDetailMap), byte> queuedKeys = new();
    private readonly Thread ioThread;
    private readonly CancellationTokenSource cancellation = new();
    private readonly int baseChunkSize;
    private readonly int effectivePageSpanInSamples;
    private readonly int heightmapLodOffset;
    private readonly int splatMapLodOffset;
    private readonly PageBufferAllocator heightmapBufferPool;
    private readonly PageBufferAllocator? splatMapBufferPool;
    private readonly TerrainHeightSampler heightSampler;
    private bool hasLoggedBufferPoolExhaustion;

    public TerrainStreamingManager(
        ITerrainFileReader fileReader,
        GpuVirtualTextureArray gpuHeightArray,
        GpuVirtualTextureArray? gpuDetailIndexArray,
        Texture? detailWeightArray,
        RuntimeDetailMapData? generatedDetailMaps,
        TerrainHeightSampler heightSampler,
        int baseChunkSize)
    {
        this.fileReader = fileReader;
        this.gpuHeightArray = gpuHeightArray;
        this.gpuDetailIndexArray = gpuDetailIndexArray;
        this.detailWeightArray = detailWeightArray;
        this.generatedDetailMaps = generatedDetailMaps;
        this.heightSampler = heightSampler;
        this.baseChunkSize = baseChunkSize;
        effectivePageSpanInSamples = Math.Max(1, fileReader.HeightmapHeader.TileSize - 1);

        int heightmapPaddedTileSize = gpuHeightArray.TileSize + gpuHeightArray.Padding * 2;
        int heightmapPageByteSize = heightmapPaddedTileSize * heightmapPaddedTileSize * fileReader.HeightmapHeader.BytesPerPixel;
        heightmapBufferPool = new PageBufferAllocator(heightmapPageByteSize, Math.Max(64, gpuHeightArray.Capacity * 2));

        if (gpuDetailIndexArray != null)
        {
            int splatMapPaddedTileSize = gpuDetailIndexArray.TileSize + gpuDetailIndexArray.Padding * 2;
            int splatMapPageByteSize = splatMapPaddedTileSize * splatMapPaddedTileSize * DetailControlBytesPerPixel;
            splatMapBufferPool = new PageBufferAllocator(splatMapPageByteSize, Math.Max(64, gpuDetailIndexArray.Capacity * 2));
        }

        // Matches the Godot terrain path's HeightmapLodOffset:
        // lod0..lodN may still sample VT mip0 until a chunk grows beyond one page span,
        // then each coarser terrain lod advances the source VT mip by one.
        int pageChunkSpanAtLod0 = Math.Max(1, effectivePageSpanInSamples / Math.Max(1, baseChunkSize));
        heightmapLodOffset = pageChunkSpanAtLod0 > 0 ? BitOperations.Log2((uint)pageChunkSpanAtLod0) : 0;

        // Splatmap LOD offset: each splatmap page covers ratio times more world area
        // because splatmap texel (x,y) maps to heightmap texel (ratio*x, ratio*y)
        int ratio = fileReader.SplatMapResolutionRatio;
        int splatMapPageSpanInChunks = Math.Max(1, (fileReader.SplatMapHeader.TileSize - 1) * ratio / Math.Max(1, baseChunkSize));
        splatMapLodOffset = splatMapPageSpanInChunks > 0 ? BitOperations.Log2((uint)splatMapPageSpanInChunks) : 0;
        ioThread = new Thread(IoThreadMain)
        {
            IsBackground = true,
            Name = "Terrain Streaming",
        };
        ioThread.Start();
    }

    public Texture HeightmapArray => gpuHeightArray.TextureArray;

    public Texture? DetailIndexMapArray => gpuDetailIndexArray?.TextureArray;
    public Texture? DetailWeightMapArray => detailWeightArray;
    public Texture? SplatMapArray => gpuDetailIndexArray?.TextureArray;

    public int TileSize => gpuHeightArray.TileSize;

    public int Padding => gpuHeightArray.Padding;

    public float GetHeight(int sampleX, int sampleZ, float heightScale)
    {
        return heightSampler.GetHeight(sampleX, sampleZ, heightScale);
    }

    /// <summary>
    /// 直接从 chunk key 计算 splatmap page key。
    /// Splatmap 有独立的 LOD offset，因为每个 splatmap page 覆盖更大的 world 区域。
    /// </summary>
    private TerrainPageKey GetSplatMapPageKey(TerrainChunkKey chunkKey, out float pageOffsetX, out float pageOffsetY, out float pageTexelStride)
    {
        int ratio = fileReader.SplatMapResolutionRatio;
        if (ratio <= 1)
        {
            // Legacy: same as heightmap, but keep the shader path consistently float-based.
            TerrainPageKey key = GetPageKey(chunkKey, out int heightOffsetX, out int heightOffsetY, out int heightTexelStride);
            pageOffsetX = heightOffsetX;
            pageOffsetY = heightOffsetY;
            pageTexelStride = heightTexelStride;
            return key;
        }

        // Splatmap uses its own LOD offset calculation, but offsets/strides must stay in
        // splat texel space. LOD0 therefore needs a 0.5 stride instead of being rounded up to 1.
        int sourceMip = Math.Min(Math.Max(0, chunkKey.LodLevel - splatMapLodOffset), fileReader.SplatMapMipCount - 1);
        int sourceHeightTexelStride = 1 << (chunkKey.LodLevel - sourceMip);
        pageTexelStride = (float)sourceHeightTexelStride / ratio;

        // Page coverage is determined in heightmap texel space, then converted back to
        // splat texel space for the shader-facing offsets.
        int splatMapPageSpanInHeightTexels = (fileReader.SplatMapHeader.TileSize - 1) * ratio;
        int chunkSpanInHeightTexels = baseChunkSize * sourceHeightTexelStride;
        int pageChunkSpanAtLod = Math.Max(1, splatMapPageSpanInHeightTexels / Math.Max(1, chunkSpanInHeightTexels));
        int pageX = Math.DivRem(chunkKey.ChunkX, pageChunkSpanAtLod, out int pageXRemainder);
        int pageY = Math.DivRem(chunkKey.ChunkY, pageChunkSpanAtLod, out int pageYRemainder);
        pageOffsetX = pageXRemainder * baseChunkSize * pageTexelStride;
        pageOffsetY = pageYRemainder * baseChunkSize * pageTexelStride;
        return new TerrainPageKey(sourceMip, pageX, pageY);
    }

    public bool TryGetResidentPageForChunk(TerrainChunkKey chunkKey,
        out int heightSliceIndex, out int splatSliceIndex,
        out int pageOffsetX, out int pageOffsetY, out int pageTexelStride)
    {
        TerrainPageKey heightPageKey = GetPageKey(chunkKey, out pageOffsetX, out pageOffsetY, out pageTexelStride);
        if (!gpuHeightArray.TryGetResidentSlice(heightPageKey, out heightSliceIndex))
        {
            splatSliceIndex = -1;
            return false;
        }

        if (gpuDetailIndexArray != null)
        {
            // Use direct calculation for correct LOD offset handling
            TerrainPageKey splatPageKey = GetSplatMapPageKey(chunkKey, out _, out _, out _);
            if (!gpuDetailIndexArray.IsPageResident(splatPageKey))
            {
                splatSliceIndex = -1;
                return false;
            }
            gpuDetailIndexArray.TryGetResidentSlice(splatPageKey, out splatSliceIndex);
        }
        else
        {
            splatSliceIndex = -1;
        }

        return true;
    }

    /// <summary>
    /// 计算 chunk 在 splatmap page 内的偏移和步幅。
    /// </summary>
    public (float splatPageOffsetX, float splatPageOffsetY, float splatPageTexelStride) GetSplatMapPageInfo(TerrainChunkKey chunkKey)
    {
        int ratio = fileReader.SplatMapResolutionRatio;
        if (ratio <= 1)
        {
            GetPageKey(chunkKey, out int _, out int _, out int heightStride);
            return (0, 0, heightStride);
        }

        // Direct calculation with splatmap's own LOD offset
        GetSplatMapPageKey(chunkKey, out float splatPageOffsetX, out float splatPageOffsetY, out float splatPageTexelStride);
        return (splatPageOffsetX, splatPageOffsetY, splatPageTexelStride);
    }

    public void RequestChunk(TerrainChunkKey chunkKey, bool pinned = false)
    {
        RequestPage(chunkKey, pinned);
    }

    public void PreloadTopLevelChunks(CommandList commandList, TerrainMinMaxErrorMap topMap)
    {
        using IMemoryOwner<byte> heightmapPageData = heightmapBufferPool.Rent();
        IMemoryOwner<byte>? splatMapPageData = splatMapBufferPool?.Rent();
        try
        {
            var seenPages = new HashSet<TerrainPageKey>();
            int topLod = fileReader.ReadAllMinMaxErrorMaps().Length - 1;
            for (int y = 0; y < topMap.Height; y++)
            {
                for (int x = 0; x < topMap.Width; x++)
                {
                    var chunkKey = new TerrainChunkKey(topLod, x, y);
                    TerrainPageKey pageKey = GetPageKey(chunkKey, out _, out _, out _);
                    if (!seenPages.Add(pageKey))
                    {
                        continue;
                    }

                    fileReader.ReadHeightPage(pageKey, heightmapPageData.Memory.Span);
                    gpuHeightArray.UploadPage(commandList, pageKey, heightmapPageData.Memory.Span, pinned: false);

                    if (splatMapPageData != null && gpuDetailIndexArray != null && detailWeightArray != null && generatedDetailMaps != null)
                    {
                        TerrainPageKey splatPageKey = GetSplatMapPageKey(chunkKey, out _, out _, out _);
                        if (!gpuDetailIndexArray.IsPageResident(splatPageKey))
                        {
                            FillGeneratedDetailPage(splatPageKey, splatMapPageData.Memory.Span, generatedDetailMaps.Value.IndexData);
                            gpuDetailIndexArray.UploadPage(commandList, splatPageKey, splatMapPageData.Memory.Span, pinned: false);
                            if (gpuDetailIndexArray.TryGetResidentSlice(splatPageKey, out int sliceIndex))
                            {
                                using IMemoryOwner<byte> weightPageData = splatMapBufferPool!.Rent();
                                FillGeneratedDetailPage(splatPageKey, weightPageData.Memory.Span, generatedDetailMaps.Value.WeightData);
                                detailWeightArray.SetData(commandList, weightPageData.Memory.Span, sliceIndex, 0, null);
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            splatMapPageData?.Dispose();
        }
    }

    public void ProcessPendingUploads(CommandList commandList, int maxUploads)
    {
        int processed = 0;
        while (processed < Math.Max(1, maxUploads) && completedRequests.TryDequeue(out var request))
        {
            bool disposeRequest = true;
            try
            {
                var targetArray = request.IsDetailMap ? gpuDetailIndexArray : gpuHeightArray;
                if (targetArray == null || !targetArray.UploadPage(commandList, request.Key, request.Data.Memory.Span, request.IsPinned))
                {
                    completedRequests.Enqueue(request);
                    disposeRequest = false;
                    break;
                }

                if (request.IsDetailMap && request.WeightData != null && detailWeightArray != null && targetArray.TryGetResidentSlice(request.Key, out int sliceIndex))
                {
                    detailWeightArray.SetData(commandList, request.WeightData.Memory.Span, sliceIndex, 0, null);
                    request.WeightData.Dispose();
                    request.WeightData = null;
                }

                queuedKeys.TryRemove((request.Key, request.IsDetailMap), out _);
                processed++;
            }
            finally
            {
                if (disposeRequest)
                {
                    request.Data.Dispose();
                    request.WeightData?.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        pendingRequests.CompleteAdding();
        ioThread.Join();
        DrainRequests(pendingRequests);
        DrainRequests(completedRequests);
        pendingRequests.Dispose();
        cancellation.Dispose();
        heightmapBufferPool.Dispose();
        splatMapBufferPool?.Dispose();
        gpuHeightArray.Dispose();
        gpuDetailIndexArray?.Dispose();
        fileReader.Dispose();
    }

    private void IoThreadMain()
    {
        try
        {
            foreach (var request in pendingRequests.GetConsumingEnumerable(cancellation.Token))
            {
                try
                {
                    if (request.IsDetailMap)
                    {
                        if (generatedDetailMaps == null)
                        {
                            throw new InvalidOperationException("Detail map streaming requires generated runtime detail data.");
                        }

                        FillGeneratedDetailPage(request.Key, request.Data.Memory.Span, generatedDetailMaps.Value.IndexData);
                        if (request.WeightData != null)
                        {
                            FillGeneratedDetailPage(request.Key, request.WeightData.Memory.Span, generatedDetailMaps.Value.WeightData);
                        }
                    }
                    else
                    {
                        fileReader.ReadHeightPage(request.Key, request.Data.Memory.Span);
                    }

                    completedRequests.Enqueue(request);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to read terrain page {request.Key}: {ex.Message}");
                    request.Data.Dispose();
                    request.WeightData?.Dispose();
                    queuedKeys.TryRemove((request.Key, request.IsDetailMap), out _);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log.Info("Terrain streaming thread exited.");
        }
    }

    public bool IsChunkResident(TerrainChunkKey chunkKey)
    {
        TerrainPageKey pageKey = GetPageKey(chunkKey, out _, out _, out _);
        if (!gpuHeightArray.IsPageResident(pageKey))
            return false;

        if (gpuDetailIndexArray != null)
        {
            TerrainPageKey splatPageKey = GetSplatMapPageKey(chunkKey, out _, out _, out _);
            if (!gpuDetailIndexArray.IsPageResident(splatPageKey))
                return false;
        }

        return true;
    }

    private void RequestPage(TerrainChunkKey chunkKey, bool pinned = false)
    {
        TerrainPageKey pageKey = GetPageKey(chunkKey, out _, out _, out _);
        bool heightResident = gpuHeightArray.IsPageResident(pageKey);

        TerrainPageKey splatPageKey = default;
        bool detailResident = true;
        if (gpuDetailIndexArray != null)
        {
            splatPageKey = GetSplatMapPageKey(chunkKey, out _, out _, out _);
            detailResident = gpuDetailIndexArray.IsPageResident(splatPageKey);
        }

        if (heightResident && detailResident)
        {
            if (pinned)
            {
                gpuHeightArray.TrySetPinned(pageKey, pinned: true);
                if (gpuDetailIndexArray != null)
                    gpuDetailIndexArray.TrySetPinned(splatPageKey, pinned: true);
            }

            return;
        }

        if (!heightResident)
        {
            if (!queuedKeys.TryAdd((pageKey, false), 0))
                return;

            if (!heightmapBufferPool.TryRent(out IMemoryOwner<byte>? heightmapBuffer) || heightmapBuffer == null)
            {
                queuedKeys.TryRemove((pageKey, false), out _);
                if (!hasLoggedBufferPoolExhaustion)
                {
                    Log.Warning("Terrain streaming buffer pool is exhausted; deferring page request until a buffer is returned.");
                    hasLoggedBufferPoolExhaustion = true;
                }

                return;
            }

            hasLoggedBufferPoolExhaustion = false;

            try
            {
                pendingRequests.Add(new StreamingRequest(pageKey, heightmapBuffer, null, pinned, isDetailMap: false));
            }
            catch
            {
                queuedKeys.TryRemove((pageKey, false), out _);
                heightmapBuffer.Dispose();
                throw;
            }
        }

        if (gpuDetailIndexArray == null || detailResident)
        {
            if (pinned && gpuDetailIndexArray != null)
                gpuDetailIndexArray.TrySetPinned(splatPageKey, pinned: true);
            return;
        }

        if (!queuedKeys.TryAdd((splatPageKey, true), 0))
            return;

        IMemoryOwner<byte>? detailIndexBuffer = null;
        IMemoryOwner<byte>? detailWeightBuffer = null;
        if (splatMapBufferPool == null
            || !splatMapBufferPool.TryRent(out detailIndexBuffer)
            || detailIndexBuffer == null
            || !splatMapBufferPool.TryRent(out detailWeightBuffer)
            || detailWeightBuffer == null)
        {
            queuedKeys.TryRemove((splatPageKey, true), out _);
            detailIndexBuffer?.Dispose();
            detailWeightBuffer?.Dispose();
            return;
        }

        try
        {
            pendingRequests.Add(new StreamingRequest(splatPageKey, detailIndexBuffer, detailWeightBuffer, pinned, isDetailMap: true));
        }
        catch
        {
            queuedKeys.TryRemove((splatPageKey, true), out _);
            detailIndexBuffer.Dispose();
            detailWeightBuffer.Dispose();
            throw;
        }
    }

    private void DrainRequests(IEnumerable<StreamingRequest> requests)
    {
        foreach (var request in requests)
        {
            request.Data.Dispose();
            request.WeightData?.Dispose();
        }
    }

    private void FillGeneratedDetailPage(TerrainPageKey key, Span<byte> destination, byte[] sourceData)
    {
        if (gpuDetailIndexArray == null || generatedDetailMaps == null)
        {
            throw new InvalidOperationException("Generated detail data is unavailable.");
        }

        int paddedTileSize = gpuDetailIndexArray.TileSize + gpuDetailIndexArray.Padding * 2;
        int expectedByteSize = paddedTileSize * paddedTileSize * DetailControlBytesPerPixel;
        if (destination.Length < expectedByteSize)
        {
            throw new ArgumentException($"Destination buffer must be at least {expectedByteSize} bytes.", nameof(destination));
        }

        int stride = 1 << key.MipLevel;
        int originX = key.PageX * (gpuDetailIndexArray.TileSize - 1) - gpuDetailIndexArray.Padding;
        int originY = key.PageY * (gpuDetailIndexArray.TileSize - 1) - gpuDetailIndexArray.Padding;
        int sourceWidth = generatedDetailMaps.Value.Width;
        int sourceHeight = generatedDetailMaps.Value.Height;

        for (int y = 0; y < paddedTileSize; y++)
        {
            int sourceY = Math.Clamp((originY + y) * stride, 0, sourceHeight - 1);
            int destRow = y * paddedTileSize * DetailControlBytesPerPixel;
            int srcRow = sourceY * sourceWidth * DetailControlBytesPerPixel;
            for (int x = 0; x < paddedTileSize; x++)
            {
                int sourceX = Math.Clamp((originX + x) * stride, 0, sourceWidth - 1);
                int srcOffset = srcRow + sourceX * DetailControlBytesPerPixel;
                int destOffset = destRow + x * DetailControlBytesPerPixel;
                sourceData.AsSpan(srcOffset, DetailControlBytesPerPixel).CopyTo(destination[destOffset..]);
            }
        }
    }

    private TerrainPageKey GetPageKey(TerrainChunkKey chunkKey, out int pageOffsetX, out int pageOffsetY, out int pageTexelStride)
    {
        // A VT page covers TileSize - 1 terrain cells. With LeafNodeSize=16 and TileSize=129,
        // This is the same HeightmapLodOffset rule used by the Godot terrain shader:
        // lod0..lodOffset reuse VT mip0 pages, then each higher terrain lod maps to the next VT mip.
        int sourceMip = Math.Min(Math.Max(0, chunkKey.LodLevel - heightmapLodOffset), fileReader.HeightmapHeader.Mipmaps - 1);
        pageTexelStride = 1 << (chunkKey.LodLevel - sourceMip);

        int chunkSpanInPageTexels = baseChunkSize * pageTexelStride;
        int pageChunkSpanAtLod = Math.Max(1, effectivePageSpanInSamples / Math.Max(1, chunkSpanInPageTexels));
        int pageX = Math.DivRem(chunkKey.ChunkX, pageChunkSpanAtLod, out int pageXRemainder);
        int pageY = Math.DivRem(chunkKey.ChunkY, pageChunkSpanAtLod, out int pageYRemainder);
        pageOffsetX = pageXRemainder * chunkSpanInPageTexels;
        pageOffsetY = pageYRemainder * chunkSpanInPageTexels;
        return new TerrainPageKey(sourceMip, pageX, pageY);
    }

    private sealed class StreamingRequest
    {
        public StreamingRequest(TerrainPageKey key, IMemoryOwner<byte> data, IMemoryOwner<byte>? weightData, bool isPinned, bool isDetailMap)
        {
            Key = key;
            Data = data;
            WeightData = weightData;
            IsPinned = isPinned;
            IsDetailMap = isDetailMap;
        }

        public TerrainPageKey Key { get; }
        public IMemoryOwner<byte> Data { get; }
        public IMemoryOwner<byte>? WeightData { get; set; }
        public bool IsPinned { get; }
        public bool IsDetailMap { get; }
    }
}
