#nullable enable

using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace Terrain.Rendering.Ocean;

[StructLayout(LayoutKind.Sequential)]
public struct OceanVertex
{
    public Vector4 Position;
    public Vector2 UV;

    public OceanVertex(Vector3 position, Vector2 uv)
    {
        Position = new Vector4(position, 1.0f);
        UV = uv;
    }

    public static readonly VertexDeclaration Layout = new(
        VertexElement.Position<Vector4>(),
        VertexElement.TextureCoordinate<Vector2>());
}
