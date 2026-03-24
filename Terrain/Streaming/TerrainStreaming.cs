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
using Terrain.Shared;
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
    public Int4 StreamInfo;  // sliceIndex, pageOffsetX, pageOffsetY, pageTexelStride
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
    public const int SupportedVersion = 1;

    public int Magic;
    public int Version;
    public int Width;
    public int Height;
    public int LeafNodeSize;
    public int TileSize;
    public int Padding;
    public int HeightMapMipLevels;
    public int HasSplatMap;
    public int SplatMapFormat;
    public int SplatMapMipLevels;
    public int Reserved1;
    public int Reserved2;
    public int Reserved3;
    public int Reserved4;
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

internal sealed class TerrainFileReader : IDisposable
{
    private const int MaxTerrainDimension = 1 << 16;
    private readonly SafeFileHandle fileHandle;
    private readonly TerrainMinMaxErrorMap[] minMaxErrorMaps;
    private readonly TerrainVirtualTextureHeader heightmapHeader;
    private readonly TerrainMipLayout[] mipLayouts;
    private readonly int tileByteSize;

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

        mipLayouts = new TerrainMipLayout[heightmapHeader.Mipmaps];
        long currentOffset = offset;
        for (int mip = 0; mip < heightmapHeader.Mipmaps; mip++)
        {
            // Reader offsets must follow the same page layout rule as the preprocessor,
            // otherwise valid page keys drift into the wrong mip span or look out of bounds.
            VirtualTextureMipLayoutInfo layoutInfo = VirtualTextureLayout.GetMipLayout(
                heightmapHeader.Width,
                heightmapHeader.Height,
                heightmapHeader.TileSize,
                mip);
            mipLayouts[mip] = new TerrainMipLayout(layoutInfo.Width, layoutInfo.Height, layoutInfo.TilesX, layoutInfo.TilesY, currentOffset);
            currentOffset = checked(currentOffset + checked((long)layoutInfo.TilesX * layoutInfo.TilesY * tileByteSize));
        }
    }

    public TerrainFileHeader Header { get; }

    public TerrainVirtualTextureHeader HeightmapHeader => heightmapHeader;

    public TerrainMinMaxErrorMap[] ReadAllMinMaxErrorMaps()
        => minMaxErrorMaps;

    public void ReadHeightPage(TerrainPageKey key, Span<byte> destination)
    {
        if ((uint)key.MipLevel >= (uint)mipLayouts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(key), $"Invalid mip level {key.MipLevel}.");
        }

        if (destination.Length < tileByteSize)
        {
            throw new ArgumentException($"Destination buffer must be at least {tileByteSize} bytes.", nameof(destination));
        }

        ref readonly var layout = ref mipLayouts[key.MipLevel];
        if ((uint)key.PageX >= (uint)layout.TilesX || (uint)key.PageY >= (uint)layout.TilesY)
        {
            throw new ArgumentOutOfRangeException(nameof(key), $"Invalid page coordinates ({key.PageX}, {key.PageY}) for mip {key.MipLevel}.");
        }

        long offset = layout.Offset + (long)(key.PageY * layout.TilesX + key.PageX) * tileByteSize;
        ReadExactly(fileHandle, destination[..tileByteSize], offset);
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
        if (header.Version != TerrainFileHeader.SupportedVersion)
        {
            throw new InvalidDataException($"Unsupported terrain file version {header.Version}. Expected {TerrainFileHeader.SupportedVersion}.");
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

internal sealed class GpuHeightArray : IDisposable
{
    private readonly Dictionary<TerrainPageKey, int> pageToSlice = new();
    private readonly Queue<int> freeSlices = new();
    private readonly LinkedList<int> lruSlices = new();
    private readonly LinkedListNode<int>?[] lruNodes;
    private readonly SlotState[] slots;

    public GpuHeightArray(Texture heightmapArray, int tileSize, int padding, int maxResidentChunks)
    {
        TileSize = tileSize;
        Padding = padding;
        HeightmapArray = heightmapArray;

        slots = new SlotState[maxResidentChunks];
        lruNodes = new LinkedListNode<int>?[maxResidentChunks];
        for (int i = 0; i < maxResidentChunks; i++)
        {
            freeSlices.Enqueue(i);
        }
    }

    public Texture HeightmapArray { get; }

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
        HeightmapArray.SetData(commandList, MemoryMarshal.Cast<byte, ushort>(data), sliceIndex, 0, null);
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
    private readonly TerrainFileReader fileReader;
    private readonly GpuHeightArray gpuHeightArray;
    private readonly BlockingCollection<StreamingRequest> pendingRequests = new();
    private readonly ConcurrentQueue<StreamingRequest> completedRequests = new();
    private readonly ConcurrentDictionary<TerrainPageKey, byte> queuedKeys = new();
    private readonly Thread ioThread;
    private readonly CancellationTokenSource cancellation = new();
    private readonly int baseChunkSize;
    private readonly int effectivePageSpanInSamples;
    private readonly int pageByteSize;
    private readonly int heightmapLodOffset;
    private readonly PageBufferAllocator pageBufferAllocator;
    private bool hasLoggedBufferPoolExhaustion;

    public TerrainStreamingManager(TerrainFileReader fileReader, GpuHeightArray gpuHeightArray, int baseChunkSize)
    {
        this.fileReader = fileReader;
        this.gpuHeightArray = gpuHeightArray;
        this.baseChunkSize = baseChunkSize;
        effectivePageSpanInSamples = Math.Max(1, fileReader.HeightmapHeader.TileSize - 1);
        pageByteSize = (gpuHeightArray.TileSize + gpuHeightArray.Padding * 2) * (gpuHeightArray.TileSize + gpuHeightArray.Padding * 2) * sizeof(ushort);
        pageBufferAllocator = new PageBufferAllocator(pageByteSize, Math.Max(64, gpuHeightArray.Capacity * 2));
        // Matches the Godot terrain path's HeightmapLodOffset:
        // lod0..lodN may still sample VT mip0 until a chunk grows beyond one page span,
        // then each coarser terrain lod advances the source VT mip by one.
        int pageChunkSpanAtLod0 = Math.Max(1, effectivePageSpanInSamples / Math.Max(1, baseChunkSize));
        heightmapLodOffset = pageChunkSpanAtLod0 > 0 ? BitOperations.Log2((uint)pageChunkSpanAtLod0) : 0;
        ioThread = new Thread(IoThreadMain)
        {
            IsBackground = true,
            Name = "Terrain Streaming",
        };
        ioThread.Start();
    }

    public Texture HeightmapArray => gpuHeightArray.HeightmapArray;

    public int TileSize => gpuHeightArray.TileSize;

    public int Padding => gpuHeightArray.Padding;

    public bool TryGetResidentPageForChunk(TerrainChunkKey chunkKey, out int sliceIndex, out int pageOffsetX, out int pageOffsetY, out int pageTexelStride)
    {
        TerrainPageKey pageKey = GetPageKey(chunkKey, out pageOffsetX, out pageOffsetY, out pageTexelStride);
        return gpuHeightArray.TryGetResidentSlice(pageKey, out sliceIndex);
    }

    public void RequestChunk(TerrainChunkKey chunkKey, bool pinned = false)
    {
        RequestPage(GetPageKey(chunkKey, out _, out _, out _), pinned);
    }

    public void PreloadTopLevelChunks(CommandList commandList, TerrainMinMaxErrorMap topMap)
    {
        using IMemoryOwner<byte> pageData = RentPageBuffer();
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

                fileReader.ReadHeightPage(pageKey, pageData.Memory.Span);
                gpuHeightArray.UploadPage(commandList, pageKey, pageData.Memory.Span, pinned: false);
            }
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
                if (!gpuHeightArray.UploadPage(commandList, request.Key, request.Data.Memory.Span, request.IsPinned))
                {
                    completedRequests.Enqueue(request);
                    disposeRequest = false;
                    break;
                }

                queuedKeys.TryRemove(request.Key, out _);
                processed++;
            }
            finally
            {
                if (disposeRequest)
                {
                    request.Data.Dispose();
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
        pageBufferAllocator.Dispose();
        gpuHeightArray.Dispose();
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
                    fileReader.ReadHeightPage(request.Key, request.Data.Memory.Span);
                    completedRequests.Enqueue(request);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to read terrain page {request.Key}: {ex.Message}");
                    request.Data.Dispose();
                    queuedKeys.TryRemove(request.Key, out _);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log.Info("Terrain streaming thread exited.");
        }
    }

    public bool IsChunkResident(TerrainChunkKey chunkKey)
        => gpuHeightArray.IsPageResident(GetPageKey(chunkKey, out _, out _, out _));

    private void RequestPage(TerrainPageKey pageKey, bool pinned = false)
    {
        if (gpuHeightArray.IsPageResident(pageKey))
        {
            if (pinned)
            {
                gpuHeightArray.TrySetPinned(pageKey, pinned: true);
            }
            return;
        }

        if (!queuedKeys.TryAdd(pageKey, 0))
        {
            return;
        }

        if (!pageBufferAllocator.TryRent(out IMemoryOwner<byte>? buffer) || buffer == null)
        {
            queuedKeys.TryRemove(pageKey, out _);
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
            pendingRequests.Add(new StreamingRequest(pageKey, buffer, pinned));
        }
        catch
        {
            queuedKeys.TryRemove(pageKey, out _);
            buffer.Dispose();
            throw;
        }
    }
    private IMemoryOwner<byte> RentPageBuffer()
        => pageBufferAllocator.Rent();

    private void DrainRequests(IEnumerable<StreamingRequest> requests)
    {
        foreach (var request in requests)
        {
            request.Data.Dispose();
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
        public StreamingRequest(TerrainPageKey key, IMemoryOwner<byte> data, bool isPinned)
        {
            Key = key;
            Data = data;
            IsPinned = isPinned;
        }

        public TerrainPageKey Key { get; }
        public IMemoryOwner<byte> Data { get; }
        public bool IsPinned { get; }
    }
}
