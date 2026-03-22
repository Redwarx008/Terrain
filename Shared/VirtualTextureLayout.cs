using System;

namespace Terrain.Shared;

internal readonly record struct VirtualTextureMipLayoutInfo(
    int MipLevel,
    int Width,
    int Height,
    int TilesX,
    int TilesY);

internal static class VirtualTextureLayout
{
    public static int GetMipCount(int width, int height, int tileSize)
    {
        int maxDimension = Math.Max(width, height);
        int levels = 0;
        while (maxDimension > tileSize)
        {
            maxDimension = (maxDimension + 1) / 2;
            levels++;
        }

        return levels + 1;
    }

    public static VirtualTextureMipLayoutInfo GetMipLayout(int width, int height, int tileSize, int mipLevel)
    {
        int mipWidth = width;
        int mipHeight = height;
        for (int mip = 0; mip < mipLevel; mip++)
        {
            mipWidth = Math.Max(1, (mipWidth + 1) / 2);
            mipHeight = Math.Max(1, (mipHeight + 1) / 2);
        }

        return new VirtualTextureMipLayoutInfo(
            mipLevel,
            mipWidth,
            mipHeight,
            ComputeTileCount(mipWidth, tileSize),
            ComputeTileCount(mipHeight, tileSize));
    }

    public static int ComputeTileCount(int mipDimension, int tileSize)
    {
        // Terrain VT pages overlap by one shared edge sample, so each page contributes
        // TileSize - 1 new samples along an axis rather than the full TileSize.
        int effectiveTileSpan = tileSize - 1;
        return DivideRoundUp(Math.Max(1, mipDimension - 1), effectiveTileSpan);
    }

    private static int DivideRoundUp(int value, int divisor)
        => (value + divisor - 1) / divisor;
}
