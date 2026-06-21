#nullable enable

using Terrain.Rivers;

namespace Terrain.Editor.Services;

internal interface IRiverMeshGenerator
{
    RiverGenerationResult? Generate(RiverCell[,] cells, float widthScale, float riverMinWidth, float riverMaxWidth);

    void Clear();
}
