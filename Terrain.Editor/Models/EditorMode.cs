#nullable enable

namespace Terrain.Editor.Models;

public enum EditorMode
{
    Sculpt,
    Paint,
    Foliage,
    Water,
    Landscape,
}

public enum SceneViewMode
{
    Perspective,
    Wireframe,
    Textured,
}

public enum SceneLightingMode
{
    Lit,
    Unlit,
    Wireframe,
}
