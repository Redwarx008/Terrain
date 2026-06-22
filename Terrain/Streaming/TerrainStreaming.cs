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
    public const int MinSupportedVersion = 8;
    public const int MaxSupportedVersion = 8;

    public int Magic;
    public int Version;
    public int Width;
    public int Height;
    public int LeafNodeSize;
    public int TileSize;
    public int Padding;
    public int HeightMapMipLevels;
    public int DetailMapFormat;
    public int DetailMapMipLevels;
    public int DetailMapResolutionRatio;
}

internal enum TerrainTextureFormat : int
{
    Rgba32 = 0,
    L16 = 1,
    Rg32 = 2,
    R8 = 3,
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
    TerrainVirtualTextureHeader DetailIndexMapHeader { get; }
    TerrainVirtualTextureHeader DetailWeightMapHeader { get; }
    int DetailMapResolutionRatio { get; }
    int DetailMapMipCount { get; }
    TerrainMinMaxErrorMap[] ReadAllMinMaxErrorMaps();
    ushort[] ReadAllHeightData();
    void ReadHeightPage(TerrainPageKey key, Span<byte> destination);
    void ReadDetailIndexPage(TerrainPageKey key, Span<byte> destination);
    void ReadDetailWeightPage(TerrainPageKey key, Span<byte> destination);
}

internal sealed class TerrainFileReader : ITerrainFileReader
{
    private const int MaxTerrainDimension = 1 << 16;
    private readonly SafeFileHandle fileHandle;
    private readonly TerrainMinMaxErrorMap[] minMaxErrorMaps;
    private readonly TerrainVirtualTextureHeader heightmapHeader;
    private readonly TerrainMipLayout[] heightmapMipLayouts;
    private readonly int tileByteSize;
    private readonly TerrainVirtualTextureHeader detailIndexMapHeader;
    private readonly TerrainMipLayout[] detailIndexMipLayouts;
    private readonly int detailIndexTileByteSize;
    private readonly TerrainVirtualTextureHeader detailWeightMapHeader;
    private readonly TerrainMipLayout[] detailWeightMipLayouts;
    private readonly int detailWeightTileByteSize;

    public TerrainFileReader(string path)
    {
        fileHandle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        try
        {
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
            tileByteSize = ComputeTileByteSize(heightmapHeader);
            long currentOffset = offset;
            heightmapMipLayouts = BuildMipLayouts(heightmapHeader, tileByteSize, ref currentOffset);

            detailIndexMapHeader = ReadStruct<TerrainVirtualTextureHeader>(fileHandle, ref currentOffset);
            ValidateDetailHeader(Header, detailIndexMapHeader, "DetailIndex");
            detailIndexTileByteSize = ComputeTileByteSize(detailIndexMapHeader);
            detailIndexMipLayouts = BuildMipLayouts(detailIndexMapHeader, detailIndexTileByteSize, ref currentOffset);

            detailWeightMapHeader = ReadStruct<TerrainVirtualTextureHeader>(fileHandle, ref currentOffset);
            ValidateDetailHeader(Header, detailWeightMapHeader, "DetailWeight");
            ValidateMatchingDetailHeaders(detailIndexMapHeader, detailWeightMapHeader);
            detailWeightTileByteSize = ComputeTileByteSize(detailWeightMapHeader);
            detailWeightMipLayouts = BuildMipLayouts(detailWeightMapHeader, detailWeightTileByteSize, ref currentOffset);

            long fileLength = RandomAccess.GetLength(fileHandle);
            if (currentOffset > fileLength)
            {
                throw new InvalidDataException($"Terrain detail VT payload is truncated. Expected at least {currentOffset} bytes, got {fileLength}.");
            }
        }
        catch (EndOfStreamException ex)
        {
            fileHandle.Dispose();
            throw new InvalidDataException("Terrain file is missing required v8 baked detail data or is truncated.", ex);
        }
        catch
        {
            fileHandle.Dispose();
            throw;
        }
    }

    public TerrainFileHeader Header { get; }

    public TerrainVirtualTextureHeader HeightmapHeader => heightmapHeader;

    public TerrainVirtualTextureHeader DetailIndexMapHeader => detailIndexMapHeader;
    public TerrainVirtualTextureHeader DetailWeightMapHeader => detailWeightMapHeader;
    public int DetailMapResolutionRatio => Header.DetailMapResolutionRatio;
    public int DetailMapMipCount => detailIndexMipLayouts.Length;

    public TerrainMinMaxErrorMap[] ReadAllMinMaxErrorMaps()
        => minMaxErrorMaps;

    public ushort[] ReadAllHeightData()
        => ReadAllVirtualTextureData<ushort>(heightmapHeader, heightmapMipLayouts, tileByteSize);

    public void ReadHeightPage(TerrainPageKey key, Span<byte> destination)
    {
        ReadVirtualTexturePage(key, destination, heightmapMipLayouts, tileByteSize, "heightmap");
    }

    public void ReadDetailIndexPage(TerrainPageKey key, Span<byte> destination)
    {
        ReadVirtualTexturePage(key, destination, detailIndexMipLayouts, detailIndexTileByteSize, "detail index");
    }

    public void ReadDetailWeightPage(TerrainPageKey key, Span<byte> destination)
    {
        ReadVirtualTexturePage(key, destination, detailWeightMipLayouts, detailWeightTileByteSize, "detail weight");
    }

    public void Dispose()
    {
        fileHandle.Dispose();
    }

    private void ReadVirtualTexturePage(
        TerrainPageKey key,
        Span<byte> destination,
        TerrainMipLayout[] layouts,
        int pageByteSize,
        string payloadName)
    {
        if ((uint)key.MipLevel >= (uint)layouts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(key), $"Invalid {payloadName} mip level {key.MipLevel}.");
        }

        if (destination.Length < pageByteSize)
        {
            throw new ArgumentException($"Destination buffer must be at least {pageByteSize} bytes.", nameof(destination));
        }

        ref readonly var layout = ref layouts[key.MipLevel];
        if ((uint)key.PageX >= (uint)layout.TilesX || (uint)key.PageY >= (uint)layout.TilesY)
        {
            throw new ArgumentOutOfRangeException(nameof(key), $"Invalid {payloadName} page coordinates ({key.PageX}, {key.PageY}) for mip {key.MipLevel}.");
        }

        long offset = layout.Offset + (long)(key.PageY * layout.TilesX + key.PageX) * pageByteSize;
        ReadExactly(fileHandle, destination[..pageByteSize], offset);
    }

    private static int ComputeTileByteSize(TerrainVirtualTextureHeader header)
    {
        int paddedTileSize = checked(header.TileSize + header.Padding * 2);
        return checked(paddedTileSize * paddedTileSize * header.BytesPerPixel);
    }

    private static TerrainMipLayout[] BuildMipLayouts(TerrainVirtualTextureHeader header, int tileByteSize, ref long currentOffset)
    {
        var layouts = new TerrainMipLayout[header.Mipmaps];
        for (int mip = 0; mip < header.Mipmaps; mip++)
        {
            VirtualTextureMipLayoutInfo layoutInfo = VirtualTextureLayout.GetMipLayout(
                header.Width,
                header.Height,
                header.TileSize,
                mip);
            layouts[mip] = new TerrainMipLayout(layoutInfo.Width, layoutInfo.Height, layoutInfo.TilesX, layoutInfo.TilesY, currentOffset);
            currentOffset = checked(currentOffset + checked((long)layoutInfo.TilesX * layoutInfo.TilesY * tileByteSize));
        }

        return layouts;
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
            throw new InvalidDataException($"Unsupported terrain file version {header.Version}. Re-export the terrain to .terrain v8 with baked detail textures.");
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

        if (header.DetailMapFormat != (int)TerrainTextureFormat.Rgba32)
        {
            throw new InvalidDataException($"Unsupported detail map format {header.DetailMapFormat}. Expected {nameof(TerrainTextureFormat.Rgba32)}.");
        }

        if (header.DetailMapMipLevels <= 0)
        {
            throw new InvalidDataException($"Invalid detail map mip count {header.DetailMapMipLevels}.");
        }

        if (header.DetailMapResolutionRatio != 2)
        {
            throw new InvalidDataException($"Unsupported detail map resolution ratio {header.DetailMapResolutionRatio}. Expected 2.");
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

    private static void ValidateDetailHeader(TerrainFileHeader header, TerrainVirtualTextureHeader detailHeader, string payloadName)
    {
        if (detailHeader.BytesPerPixel != 4)
        {
            throw new InvalidDataException($"{payloadName} block must be RGBA8, got {detailHeader.BytesPerPixel} bytes per pixel.");
        }

        int expectedWidth = (header.Width + 1) / 2;
        int expectedHeight = (header.Height + 1) / 2;
        if (detailHeader.Width != expectedWidth || detailHeader.Height != expectedHeight)
        {
            throw new InvalidDataException($"{payloadName} dimensions {detailHeader.Width}x{detailHeader.Height} do not match expected half-resolution {expectedWidth}x{expectedHeight}.");
        }

        if (detailHeader.TileSize != header.TileSize)
        {
            throw new InvalidDataException($"{payloadName} tile size does not match the terrain header.");
        }

        if (detailHeader.Padding != 1)
        {
            throw new InvalidDataException($"{payloadName} padding must be 1.");
        }

        if (detailHeader.Mipmaps != header.DetailMapMipLevels)
        {
            throw new InvalidDataException($"{payloadName} mip count {detailHeader.Mipmaps} does not match terrain header detail mip count {header.DetailMapMipLevels}.");
        }

        int expectedMipCount = VirtualTextureLayout.GetMipCount(detailHeader.Width, detailHeader.Height, detailHeader.TileSize);
        if (detailHeader.Mipmaps != expectedMipCount)
        {
            throw new InvalidDataException($"{payloadName} mip count {detailHeader.Mipmaps} does not match the shared VT layout rule; expected {expectedMipCount}.");
        }
    }

    private static void ValidateMatchingDetailHeaders(TerrainVirtualTextureHeader indexHeader, TerrainVirtualTextureHeader weightHeader)
    {
        if (indexHeader.Width != weightHeader.Width
            || indexHeader.Height != weightHeader.Height
            || indexHeader.TileSize != weightHeader.TileSize
            || indexHeader.Padding != weightHeader.Padding
            || indexHeader.BytesPerPixel != weightHeader.BytesPerPixel
            || indexHeader.Mipmaps != weightHeader.Mipmaps)
        {
            throw new InvalidDataException("DetailIndex and DetailWeight VT headers must match.");
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
    private Action<TerrainPageKey>? pageEvicted;

    public GpuVirtualTextureArray(Texture textureArray, int tileSize, int padding, int maxResidentChunks, Action<TerrainPageKey>? pageEvicted = null)
    {
        TileSize = tileSize;
        Padding = padding;
        TextureArray = textureArray;
        this.pageEvicted = pageEvicted;

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

    public event Action<TerrainPageKey> PageEvicted
    {
        add => pageEvicted += value;
        remove => pageEvicted -= value;
    }

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

        TerrainPageKey evictedKey = slot.Key;
        pageToSlice.Remove(slot.Key);
        RemoveFromLru(sliceIndex);
        slot = default;
        pageEvicted?.Invoke(evictedKey);
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
    private RuntimeDetailMapData? generatedDetailMaps;
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
    private readonly ConcurrentDictionary<TerrainPageKey, CachedHeightPage> cachedHeightPages = new();
    private readonly ConcurrentQueue<TerrainPageKey> cachedHeightPageOrder = new();
    private readonly int maxCachedHeightPages;
    private readonly int heightmapPaddedTileSize;
    private readonly int heightmapPageByteSize;
    private readonly int heightPageSpan;
    private readonly VirtualTextureMipLayoutInfo heightMip0Layout;
    private bool hasLoggedBufferPoolExhaustion;

    public TerrainStreamingManager(
        ITerrainFileReader fileReader,
        GpuVirtualTextureArray gpuHeightArray,
        GpuVirtualTextureArray? gpuDetailIndexArray,
        Texture? detailWeightArray,
        int baseChunkSize)
    {
        this.fileReader = fileReader;
        this.gpuHeightArray = gpuHeightArray;
        this.gpuDetailIndexArray = gpuDetailIndexArray;
        this.detailWeightArray = detailWeightArray;
        this.baseChunkSize = baseChunkSize;
        gpuHeightArray.PageEvicted += RemoveCachedHeightPage;
        effectivePageSpanInSamples = Math.Max(1, fileReader.HeightmapHeader.TileSize - 1);
        maxCachedHeightPages = Math.Max(1, gpuHeightArray.Capacity);
        heightmapPaddedTileSize = gpuHeightArray.TileSize + gpuHeightArray.Padding * 2;
        heightmapPageByteSize = heightmapPaddedTileSize * heightmapPaddedTileSize * fileReader.HeightmapHeader.BytesPerPixel;
        heightPageSpan = Math.Max(1, fileReader.HeightmapHeader.TileSize - 1);
        heightMip0Layout = VirtualTextureLayout.GetMipLayout(
            fileReader.HeightmapHeader.Width,
            fileReader.HeightmapHeader.Height,
            fileReader.HeightmapHeader.TileSize,
            mipLevel: 0);

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

        // Detail map LOD offset: each detail page covers ratio times more world area
        // because detail texel (x,y) maps to heightmap texel (ratio*x, ratio*y).
        int ratio = fileReader.DetailMapResolutionRatio;
        int splatMapPageSpanInChunks = Math.Max(1, (fileReader.DetailIndexMapHeader.TileSize - 1) * ratio / Math.Max(1, baseChunkSize));
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

    public void SetGeneratedDetailMaps(RuntimeDetailMapData detailMaps)
    {
        generatedDetailMaps = detailMaps;
    }

    public float GetHeight(int sampleX, int sampleZ, float heightScale)
    {
        sampleX = Math.Clamp(sampleX, 0, fileReader.HeightmapHeader.Width - 1);
        sampleZ = Math.Clamp(sampleZ, 0, fileReader.HeightmapHeader.Height - 1);

        int pageX = Math.Min(sampleX / heightPageSpan, heightMip0Layout.TilesX - 1);
        int pageY = Math.Min(sampleZ / heightPageSpan, heightMip0Layout.TilesY - 1);
        int localX = sampleX - pageX * heightPageSpan;
        int localY = sampleZ - pageY * heightPageSpan;
        var key = new TerrainPageKey(0, pageX, pageY);

        CachedHeightPage page = GetOrLoadHeightPage(key);
        int paddedX = localX + fileReader.HeightmapHeader.Padding;
        int paddedY = localY + fileReader.HeightmapHeader.Padding;
        ushort encodedHeight = page.Data[paddedY * heightmapPaddedTileSize + paddedX];
        return encodedHeight * TerrainComponent.HeightSampleNormalization * heightScale;
    }

    /// <summary>
    /// 直接从 chunk key 计算 splatmap page key。
    /// Splatmap 有独立的 LOD offset，因为每个 splatmap page 覆盖更大的 world 区域。
    /// </summary>
    private TerrainPageKey GetSplatMapPageKey(TerrainChunkKey chunkKey, out float pageOffsetX, out float pageOffsetY, out float pageTexelStride)
    {
        int ratio = fileReader.DetailMapResolutionRatio;
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
        int sourceMip = Math.Min(Math.Max(0, chunkKey.LodLevel - splatMapLodOffset), fileReader.DetailMapMipCount - 1);
        int sourceHeightTexelStride = 1 << (chunkKey.LodLevel - sourceMip);
        pageTexelStride = (float)sourceHeightTexelStride / ratio;

        // Page coverage is determined in heightmap texel space, then converted back to
        // splat texel space for the shader-facing offsets.
        int splatMapPageSpanInHeightTexels = (fileReader.DetailIndexMapHeader.TileSize - 1) * ratio;
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
        int ratio = fileReader.DetailMapResolutionRatio;
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
                    CacheHeightPage(pageKey, heightmapPageData.Memory.Span, isGpuResident: true);
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

                if (!request.IsDetailMap)
                {
                    CacheHeightPage(request.Key, request.Data.Memory.Span, isGpuResident: true);
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
        gpuHeightArray.PageEvicted -= RemoveCachedHeightPage;
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

    private CachedHeightPage GetOrLoadHeightPage(TerrainPageKey key)
    {
        if (cachedHeightPages.TryGetValue(key, out CachedHeightPage? cachedPage))
            return cachedPage;

        var bytes = new byte[heightmapPageByteSize];
        fileReader.ReadHeightPage(key, bytes);
        return CacheHeightPage(key, bytes, isGpuResident: false);
    }

    private CachedHeightPage CacheHeightPage(TerrainPageKey key, Span<byte> bytes, bool isGpuResident)
    {
        if (cachedHeightPages.TryGetValue(key, out CachedHeightPage? existingPage))
        {
            if (isGpuResident)
                existingPage.IsGpuResident = true;
            return existingPage;
        }

        var data = new ushort[heightmapPaddedTileSize * heightmapPaddedTileSize];
        MemoryMarshal.Cast<byte, ushort>(bytes[..heightmapPageByteSize]).CopyTo(data);
        var cachedPage = new CachedHeightPage(key, data)
        {
            IsGpuResident = isGpuResident,
        };

        if (!cachedHeightPages.TryAdd(key, cachedPage))
        {
            cachedHeightPages.TryGetValue(key, out CachedHeightPage? racedPage);
            if (racedPage != null && isGpuResident)
                racedPage.IsGpuResident = true;
            return racedPage ?? cachedPage;
        }

        cachedHeightPageOrder.Enqueue(key);
        TrimNonResidentHeightPagesIfNeeded();
        return cachedPage;
    }

    private void RemoveCachedHeightPage(TerrainPageKey key)
    {
        cachedHeightPages.TryRemove(key, out _);
    }

    private void TrimNonResidentHeightPagesIfNeeded()
    {
        int attempts = cachedHeightPageOrder.Count;
        while (cachedHeightPages.Count > maxCachedHeightPages
            && attempts-- > 0
            && cachedHeightPageOrder.TryDequeue(out TerrainPageKey oldestKey))
        {
            if (!cachedHeightPages.TryGetValue(oldestKey, out CachedHeightPage? oldestPage))
                continue;

            if (oldestPage.IsGpuResident)
            {
                cachedHeightPageOrder.Enqueue(oldestKey);
                continue;
            }

            cachedHeightPages.TryRemove(oldestKey, out _);
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

    private sealed class CachedHeightPage
    {
        public CachedHeightPage(TerrainPageKey key, ushort[] data)
        {
            Key = key;
            Data = data;
        }

        public TerrainPageKey Key { get; }
        public ushort[] Data { get; }
        public bool IsGpuResident { get; set; }
    }
}
