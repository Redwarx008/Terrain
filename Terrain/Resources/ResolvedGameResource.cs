namespace Terrain.Resources;

public readonly record struct ResolvedGameResource(
    string VirtualPath,
    string ResolvedPath,
    string SourceLayerId,
    bool IsWritable,
    bool HasLowerPriorityFallback);
