#nullable enable

using System;

namespace Terrain.Editor.Services;

public struct DetailControlPixel
{
    public byte Index0;
    public byte Index1;
    public byte Index2;
    public byte Index3;
    public byte Weight0;
    public byte Weight1;
    public byte Weight2;
    public byte Weight3;

    public static readonly DetailControlPixel Default = new()
    {
        Index0 = 0,
        Index1 = byte.MaxValue,
        Index2 = byte.MaxValue,
        Index3 = byte.MaxValue,
        Weight0 = byte.MaxValue,
    };
}

/// <summary>
/// Compatibility wrapper around the new CK3-style terrain control maps.
/// The historical type name is retained so older editor code can be migrated
/// incrementally while the underlying storage has already switched to 4 indices
/// plus 4 weights per texel.
/// </summary>
public sealed class MaterialIndexMap
{
    public int Width { get; }
    public int Height { get; }

    private readonly byte[] indexData;
    private readonly byte[] weightData;

    public const int IndicesBytesPerPixel = 4;
    public const int WeightsBytesPerPixel = 4;
    public const int BytesPerPixel = IndicesBytesPerPixel;

    public MaterialIndexMap(int width, int height)
    {
        Width = width;
        Height = height;
        indexData = new byte[(long)width * height * IndicesBytesPerPixel];
        weightData = new byte[(long)width * height * WeightsBytesPerPixel];
        Fill(DetailControlPixel.Default);
    }

    public DetailControlPixel GetPixel(int x, int z)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return DetailControlPixel.Default;

        int offset = (int)(((long)z * Width + x) * 4);
        return new DetailControlPixel
        {
            Index0 = indexData[offset],
            Index1 = indexData[offset + 1],
            Index2 = indexData[offset + 2],
            Index3 = indexData[offset + 3],
            Weight0 = weightData[offset],
            Weight1 = weightData[offset + 1],
            Weight2 = weightData[offset + 2],
            Weight3 = weightData[offset + 3],
        };
    }

    public void SetPixel(int x, int z, DetailControlPixel pixel)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return;

        int offset = (int)(((long)z * Width + x) * 4);
        indexData[offset] = pixel.Index0;
        indexData[offset + 1] = pixel.Index1;
        indexData[offset + 2] = pixel.Index2;
        indexData[offset + 3] = pixel.Index3;
        weightData[offset] = pixel.Weight0;
        weightData[offset + 1] = pixel.Weight1;
        weightData[offset + 2] = pixel.Weight2;
        weightData[offset + 3] = pixel.Weight3;
    }

    public void SetTop4IndicesAndWeights(int x, int z, ReadOnlySpan<byte> indices, ReadOnlySpan<byte> weights)
    {
        if (indices.Length < 4 || weights.Length < 4)
            throw new ArgumentException("Expected four indices and four weights.");

        SetPixel(x, z, new DetailControlPixel
        {
            Index0 = indices[0],
            Index1 = indices[1],
            Index2 = indices[2],
            Index3 = indices[3],
            Weight0 = weights[0],
            Weight1 = weights[1],
            Weight2 = weights[2],
            Weight3 = weights[3],
        });
    }

    // Legacy helpers
    public void SetIndex(int x, int z, byte materialIndex)
    {
        SetPixel(x, z, new DetailControlPixel
        {
            Index0 = materialIndex,
            Index1 = byte.MaxValue,
            Index2 = byte.MaxValue,
            Index3 = byte.MaxValue,
            Weight0 = byte.MaxValue,
        });
    }

    public byte GetIndex(int x, int z)
    {
        return GetPixel(x, z).Index0;
    }

    public byte[] GetRawData() => indexData;
    public byte[] GetIndexRawData() => indexData;
    public byte[] GetWeightRawData() => weightData;

    public ReadOnlySpan<byte> GetIndexSliceBytesPerRow(int startX, int startZ, int row, int sliceWidth)
    {
        int pixelOffset = (int)(((long)(startZ + row) * Width + startX) * IndicesBytesPerPixel);
        int byteCount = (int)((long)sliceWidth * IndicesBytesPerPixel);
        return indexData.AsSpan(pixelOffset, byteCount);
    }

    public ReadOnlySpan<byte> GetWeightSliceBytesPerRow(int startX, int startZ, int row, int sliceWidth)
    {
        int pixelOffset = (int)(((long)(startZ + row) * Width + startX) * WeightsBytesPerPixel);
        int byteCount = (int)((long)sliceWidth * WeightsBytesPerPixel);
        return weightData.AsSpan(pixelOffset, byteCount);
    }

    public ReadOnlySpan<byte> GetSliceBytesPerRow(int startX, int startZ, int row, int sliceWidth)
    {
        return GetIndexSliceBytesPerRow(startX, startZ, row, sliceWidth);
    }

    public void SetRegionFromBytes(int startX, int startZ, int regionWidth, int regionHeight, ReadOnlySpan<byte> bytes)
    {
        int rowByteCount = (int)((long)regionWidth * IndicesBytesPerPixel);
        for (int row = 0; row < regionHeight; row++)
        {
            int dstOffset = (int)((((long)startZ + row) * Width + startX) * IndicesBytesPerPixel);
            int srcByteOffset = (int)((long)row * regionWidth * IndicesBytesPerPixel);
            bytes.Slice(srcByteOffset, rowByteCount).CopyTo(indexData.AsSpan(dstOffset, rowByteCount));
        }
    }

    public byte[] CopyRegionToBytes(int startX, int startZ, int regionWidth, int regionHeight)
    {
        var bytes = new byte[(long)regionWidth * regionHeight * IndicesBytesPerPixel];
        int rowByteCount = (int)((long)regionWidth * IndicesBytesPerPixel);
        for (int row = 0; row < regionHeight; row++)
        {
            int srcOffset = (int)((((long)startZ + row) * Width + startX) * IndicesBytesPerPixel);
            int dstOffset = (int)((long)row * regionWidth * IndicesBytesPerPixel);
            indexData.AsSpan(srcOffset, rowByteCount).CopyTo(bytes.AsSpan(dstOffset));
        }
        return bytes;
    }

    public void Clear() => Fill(DetailControlPixel.Default);

    public void Fill(DetailControlPixel pixel)
    {
        long pixelCount = (long)Width * Height;
        for (long i = 0; i < pixelCount; i++)
        {
            int offset = (int)(i * 4);
            indexData[offset] = pixel.Index0;
            indexData[offset + 1] = pixel.Index1;
            indexData[offset + 2] = pixel.Index2;
            indexData[offset + 3] = pixel.Index3;
            weightData[offset] = pixel.Weight0;
            weightData[offset + 1] = pixel.Weight1;
            weightData[offset + 2] = pixel.Weight2;
            weightData[offset + 3] = pixel.Weight3;
        }
    }

    public void Fill(byte materialIndex)
    {
        Fill(new DetailControlPixel
        {
            Index0 = materialIndex,
            Index1 = byte.MaxValue,
            Index2 = byte.MaxValue,
            Index3 = byte.MaxValue,
            Weight0 = byte.MaxValue,
        });
    }

    public void MigrateFromR8(byte[] oldData)
    {
        if (oldData.Length != (long)Width * Height)
            throw new ArgumentException("Old data size mismatch.", nameof(oldData));

        long length = oldData.Length;
        for (long i = 0; i < length; i++)
        {
            int offset = (int)(i * 4);
            indexData[offset] = oldData[(int)i];
            indexData[offset + 1] = byte.MaxValue;
            indexData[offset + 2] = byte.MaxValue;
            indexData[offset + 3] = byte.MaxValue;
            weightData[offset] = byte.MaxValue;
            weightData[offset + 1] = 0;
            weightData[offset + 2] = 0;
            weightData[offset + 3] = 0;
        }
    }
}
