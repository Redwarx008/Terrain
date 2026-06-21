#nullable enable

using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

internal interface IRiverMeshGenerator
{
    RiverGenerationResult? Generate(RiverCell[,] cells, float widthScale, float riverMinWidth = 1.0f, float riverMaxWidth = 4.0f);

    void Clear();
}
