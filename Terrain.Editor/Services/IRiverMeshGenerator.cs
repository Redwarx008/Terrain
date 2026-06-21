#nullable enable

using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

internal interface IRiverMeshGenerator
{
    RiverGenerationResult? Generate(RiverCell[,] cells, float widthScale, float riverMinWidth, float riverMaxWidth);

    void Clear();
}
