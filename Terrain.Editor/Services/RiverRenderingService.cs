#nullable enable

using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Terrain.Editor.Models;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Editor.Services;

public sealed class RiverRenderingService : IDisposable
{
    private readonly GraphicsDevice graphicsDevice;
    private readonly Scene scene;
    private readonly List<Entity> riverEntities = new();
    private Entity? riverContainer;

    public RiverRenderingService(GraphicsDevice graphicsDevice, Scene scene)
    {
        this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    public void UpdateMeshes(List<RiverSegment> segments, RiverMeshService meshService, float widthScale)
    {
        ClearMeshes();

        riverContainer = new Entity("RiverSystem");
        scene.Entities.Add(riverContainer);

        foreach (var seg in segments)
        {
            var (vertices, indices) = meshService.BuildRibbonMesh(seg, widthScale);
            if (vertices.Length == 0) continue;

            var vertexBuffer = Buffer.Vertex.New(graphicsDevice, vertices, GraphicsResourceUsage.Dynamic);
            var indexBuffer = Buffer.Index.New(graphicsDevice, indices);

            var meshDraw = new MeshDraw
            {
                DrawCount = indices.Length,
                PrimitiveType = PrimitiveType.TriangleList,
                VertexBuffers = new[] { new VertexBufferBinding(vertexBuffer, VertexPositionNormalTexture.Layout, vertexBuffer.ElementCount) },
                IndexBuffer = new IndexBufferBinding(indexBuffer, true, indexBuffer.ElementCount),
            };

            var mesh = new Mesh(meshDraw, new ParameterCollection())
            {
                MaterialIndex = 0,
                BoundingSphere = BoundingSphere.Empty,
            };

            var model = new Model();
            model.Meshes.Add(mesh);

            // Create simple material with transparency
            var descriptor = new MaterialDescriptor();
            descriptor.Attributes.Diffuse = new MaterialDiffuseMapFeature(new ComputeColor());
            descriptor.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();
            descriptor.Attributes.Transparency = new MaterialTransparencyBlendFeature();
            var mat = Material.New(graphicsDevice, descriptor);

            model.Materials.Add(mat);
            var modelComponent = new ModelComponent(model);

            var entity = new Entity($"RiverSegment_{seg.SystemId}_{riverEntities.Count}")
            {
                modelComponent
            };

            riverEntities.Add(entity);
            riverContainer.AddChild(entity);
        }
    }

    public void SetVisible(bool visible)
    {
        foreach (var entity in riverEntities)
        {
            foreach (var component in entity.Components)
            {
                if (component is ModelComponent mc)
                    mc.Enabled = visible;
            }
        }
    }

    public void ClearMeshes()
    {
        foreach (var entity in riverEntities)
        {
            entity.Scene?.Entities.Remove(entity);
            entity.Dispose();
        }
        riverEntities.Clear();

        if (riverContainer != null)
        {
            scene.Entities.Remove(riverContainer);
            riverContainer.Dispose();
            riverContainer = null;
        }
    }

    public void Dispose()
    {
        ClearMeshes();
    }
}
