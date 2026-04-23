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

        int offset = ((z * Width) + x) * 4;
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

        int offset = ((z * Width) + x) * 4;
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
        int pixelOffset = (((startZ + row) * Width) + startX) * IndicesBytesPerPixel;
        return indexData.AsSpan(pixelOffset, sliceWidth * IndicesBytesPerPixel);
    }

    public ReadOnlySpan<byte> GetWeightSliceBytesPerRow(int startX, int startZ, int row, int sliceWidth)
    {
        int pixelOffset = (((startZ + row) * Width) + startX) * WeightsBytesPerPixel;
        return weightData.AsSpan(pixelOffset, sliceWidth * WeightsBytesPerPixel);
    }

    public ReadOnlySpan<byte> GetSliceBytesPerRow(int startX, int startZ, int row, int sliceWidth)
    {
        return GetIndexSliceBytesPerRow(startX, startZ, row, sliceWidth);
    }

    public void SetRegionFromBytes(int startX, int startZ, int regionWidth, int regionHeight, ReadOnlySpan<byte> bytes)
    {
        for (int row = 0; row < regionHeight; row++)
        {
            int dstOffset = (((startZ + row) * Width) + startX) * IndicesBytesPerPixel;
            int srcByteOffset = row * regionWidth * IndicesBytesPerPixel;
            bytes.Slice(srcByteOffset, regionWidth * IndicesBytesPerPixel).CopyTo(indexData.AsSpan(dstOffset, regionWidth * IndicesBytesPerPixel));
        }
    }

    public byte[] CopyRegionToBytes(int startX, int startZ, int regionWidth, int regionHeight)
    {
        var bytes = new byte[regionWidth * regionHeight * IndicesBytesPerPixel];
        for (int row = 0; row < regionHeight; row++)
        {
            int srcOffset = (((startZ + row) * Width) + startX) * IndicesBytesPerPixel;
            indexData.AsSpan(srcOffset, regionWidth * IndicesBytesPerPixel).CopyTo(bytes.AsSpan(row * regionWidth * IndicesBytesPerPixel));
        }
        return bytes;
    }

    public void Clear() => Fill(DetailControlPixel.Default);

    public void Fill(DetailControlPixel pixel)
    {
        for (int i = 0; i < Width * Height; i++)
        {
            int offset = i * 4;
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

        for (int i = 0; i < oldData.Length; i++)
        {
            int offset = i * 4;
            indexData[offset] = oldData[i];
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
