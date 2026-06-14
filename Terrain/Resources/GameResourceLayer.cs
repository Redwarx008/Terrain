namespace Terrain.Resources;

public readonly record struct GameResourceLayer
{
    public GameResourceLayer(string id, string rootPath, bool isBaseLayer)
    {
        Id = id;
        RootPath = rootPath;
        IsBaseLayer = isBaseLayer;
    }

    public string Id { get; }
    public string RootPath { get; }
    public bool IsBaseLayer { get; }
}
