#nullable enable

namespace Terrain.Rivers;

public interface IRiverTerrainHeightSource
{
    bool HasHeightData { get; }
    int HeightmapWidth { get; }
    int HeightmapHeight { get; }
    float HeightScale { get; }
    float SampleHeight(float worldX, float worldZ);
}
