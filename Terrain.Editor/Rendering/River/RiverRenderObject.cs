#nullable enable

using System;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverRenderObject : RenderObject, IDisposable
{
    public int SegmentId { get; private set; }
    public int SourceVersion { get; private set; } = -1;
    public Buffer? VertexBuffer { get; private set; }
    public Buffer? IndexBuffer { get; private set; }
    public MeshDraw? MeshDraw { get; private set; }
    public int IndexCount { get; private set; }
    public BoundingSphere BoundingSphere { get; private set; } = BoundingSphere.Empty;
    public float MapExtent { get; private set; } = 4096.0f;
    public Vector2 MapWorldSize { get; private set; } = new(4096.0f, 4096.0f);
    public float RefractionMaxCameraHeight { get; private set; } = 50.0f;
    public float TextureUvScale { get; private set; } = 1.0f;
    public float FlowNormalUvScale { get; private set; } = 0.4f;
    public float FlowNormalSpeed { get; private set; } = 0.075f;
    public float RiverFoamFactor { get; private set; } = 0.5f;
    public float NoiseScale { get; private set; } = 0.25f;
    public float NoiseSpeed { get; private set; } = 2.0f;
    public float FlattenMultiplier { get; private set; } = 1.0f;
    public float OceanFadeRate { get; private set; } = 0.8f;
    public float BankAmount { get; private set; } = 0.0f;
    public float BankFade { get; private set; } = 0.025f;
    public float Depth { get; private set; } = 0.15f;
    public float DepthWidthPower { get; private set; } = 2.0f;
    public float DepthFakeFactor { get; private set; } = 2.0f;
    public int ParallaxIterations { get; private set; } = 10;
    public float BottomNormalStrength { get; private set; } = 1.0f;
    public float BottomEnvironmentIntensity { get; private set; } = 1.0f;
    public float FlatMapLerp { get; private set; } = 0.0f;
    public float WaterRefractionScale { get; private set; } = 500.0f;
    public float WaterRefractionShoreMaskDepth { get; private set; } = 3.0f;
    public float WaterRefractionShoreMaskSharpness { get; private set; } = 1.0f;
    public float WaterRefractionFade { get; private set; } = 1.0f;
    public Vector4 WaterColorShallow { get; private set; } = new(0.0055146287f, 0.0078107193f, 0.0120865023f, 1.0f);
    public Vector4 WaterColorDeep { get; private set; } = new(0.0001385075f, 0.0001974951f, 0.0002262951f, 1.0f);
    public Matrix World { get; set; } = Matrix.Identity;

    public RiverRenderObject()
    {
        BoundingBox = (BoundingBoxExt)new BoundingBox(Vector3.Zero, Vector3.One);
    }

    public void ApplySettings(RiverRenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        TextureUvScale = settings.TextureUvScale;
        FlowNormalUvScale = settings.FlowNormalUvScale;
        FlowNormalSpeed = settings.FlowNormalSpeed;
        RiverFoamFactor = settings.RiverFoamFactor;
        NoiseScale = settings.NoiseScale;
        NoiseSpeed = settings.NoiseSpeed;
        FlattenMultiplier = settings.FlattenMultiplier;
        OceanFadeRate = settings.OceanFadeRate;
        BankAmount = settings.BankAmount;
        BankFade = settings.BankFade;
        Depth = settings.Depth;
        DepthWidthPower = settings.DepthWidthPower;
        DepthFakeFactor = settings.DepthFakeFactor;
        ParallaxIterations = settings.ParallaxIterations;
        BottomNormalStrength = settings.BottomNormalStrength;
        BottomEnvironmentIntensity = settings.BottomEnvironmentIntensity;
        FlatMapLerp = settings.FlatMapLerp;
        WaterRefractionScale = settings.WaterRefractionScale;
        WaterRefractionShoreMaskDepth = settings.WaterRefractionShoreMaskDepth;
        WaterRefractionShoreMaskSharpness = settings.WaterRefractionShoreMaskSharpness;
        WaterRefractionFade = settings.WaterRefractionFade;
        WaterColorShallow = settings.WaterColorShallow;
        WaterColorDeep = settings.WaterColorDeep;
    }

    public void Rebuild(GraphicsDevice graphicsDevice, RiverMeshData mesh, int sourceVersion)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(mesh);

        ReleaseGpuResources();

        SegmentId = mesh.SegmentId;
        SourceVersion = sourceVersion;
        IndexCount = mesh.Indices.Length;
        BoundingBox = (BoundingBoxExt)mesh.BoundingBox;
        BoundingSphere = mesh.BoundingSphere;
        MapExtent = mesh.MapExtent;
        MapWorldSize = mesh.MapWorldSize;
        RefractionMaxCameraHeight = mesh.RefractionMaxCameraHeight;

        if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0)
        {
            Enabled = false;
            return;
        }

        VertexBuffer = Buffer.Vertex.New(graphicsDevice, mesh.Vertices, GraphicsResourceUsage.Dynamic);
        IndexBuffer = Buffer.Index.New(graphicsDevice, mesh.Indices);
        MeshDraw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            DrawCount = mesh.Indices.Length,
            StartLocation = 0,
            VertexBuffers =
            [
                new VertexBufferBinding(VertexBuffer, RiverVertex.Layout, mesh.Vertices.Length),
            ],
            IndexBuffer = new IndexBufferBinding(IndexBuffer, true, mesh.Indices.Length),
        };
    }

    public void ReleaseGpuResources()
    {
        VertexBuffer?.Dispose();
        VertexBuffer = null;

        IndexBuffer?.Dispose();
        IndexBuffer = null;

        MeshDraw = null;
        IndexCount = 0;
    }

    public void Dispose()
    {
        ReleaseGpuResources();
    }
}
