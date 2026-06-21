#nullable enable

using System;

namespace Terrain.Rivers;

internal sealed class TerrainComponentRiverHeightSource : IRiverTerrainHeightSource
{
    private const float HeightSampleNormalization = 1.0f / ushort.MaxValue;
    private readonly TerrainComponent component;

    public TerrainComponentRiverHeightSource(TerrainComponent component)
    {
        this.component = component ?? throw new ArgumentNullException(nameof(component));
    }

    public bool HasHeightData => component.RuntimeHeightData != null
        && component.RuntimeHeightDataWidth > 0
        && component.RuntimeHeightDataHeight > 0;

    public int HeightmapWidth => component.RuntimeHeightDataWidth;

    public int HeightmapHeight => component.RuntimeHeightDataHeight;

    public float HeightScale => component.HeightScale;

    public float SampleHeight(float worldX, float worldZ)
    {
        ushort[]? data = component.RuntimeHeightData;
        int width = component.RuntimeHeightDataWidth;
        int height = component.RuntimeHeightDataHeight;
        if (data == null || width <= 0 || height <= 0 || data.LongLength < (long)width * height)
            return 0.0f;

        float x = Math.Clamp(worldX, 0.0f, width - 1);
        float z = Math.Clamp(worldZ, 0.0f, height - 1);
        int x0 = (int)MathF.Floor(x);
        int z0 = (int)MathF.Floor(z);
        int x1 = Math.Min(x0 + 1, width - 1);
        int z1 = Math.Min(z0 + 1, height - 1);
        float tx = x - x0;
        float tz = z - z0;

        float h00 = data[z0 * width + x0] * HeightSampleNormalization * component.HeightScale;
        float h10 = data[z0 * width + x1] * HeightSampleNormalization * component.HeightScale;
        float h01 = data[z1 * width + x0] * HeightSampleNormalization * component.HeightScale;
        float h11 = data[z1 * width + x1] * HeightSampleNormalization * component.HeightScale;
        float hx0 = h00 + (h10 - h00) * tx;
        float hx1 = h01 + (h11 - h01) * tx;
        return hx0 + (hx1 - hx0) * tz;
    }
}

internal sealed class NullRiverTerrainHeightSource : IRiverTerrainHeightSource
{
    public static readonly NullRiverTerrainHeightSource Instance = new();

    private NullRiverTerrainHeightSource()
    {
    }

    public bool HasHeightData => false;

    public int HeightmapWidth => 0;

    public int HeightmapHeight => 0;

    public float HeightScale => 0.0f;

    public float SampleHeight(float worldX, float worldZ) => 0.0f;
}
