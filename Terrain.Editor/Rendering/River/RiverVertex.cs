#nullable enable

using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.River;

[StructLayout(LayoutKind.Sequential)]
public struct RiverVertex
{
    public Vector4 Position;
    public float Transparency;
    public Vector2 UV;
    public Vector3 Tangent;
    public Vector3 Normal;
    public float Width;
    public float DistanceToMain;

    public RiverVertex(Vector3 position, float transparency, Vector2 uv, Vector3 tangent, Vector3 normal, float width, float distanceToMain)
    {
        Position = new Vector4(position, 1.0f);
        Transparency = transparency;
        UV = uv;
        Tangent = tangent;
        Normal = normal;
        Width = width;
        DistanceToMain = distanceToMain;
    }

    public static readonly VertexDeclaration Layout = new(
        VertexElement.Position<Vector4>(),
        VertexElement.TextureCoordinate<float>(0),
        VertexElement.TextureCoordinate<Vector2>(1),
        VertexElement.TextureCoordinate<Vector3>(2),
        VertexElement.TextureCoordinate<Vector3>(3),
        VertexElement.TextureCoordinate<float>(4),
        VertexElement.TextureCoordinate<float>(5));
}
