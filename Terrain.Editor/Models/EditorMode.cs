#nullable enable

namespace Terrain.Editor.Models;

public enum EditorMode
{
    Sculpt,
    Paint,
    Path,
    River,
    Foliage,
    Settings,
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
