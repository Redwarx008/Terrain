#nullable enable

using System;
using System.Collections.Generic;
using Stride.Core;
using Stride.Engine;
using Stride.Engine.Design;

namespace Terrain.Rendering.River;

[DataContract("RiverComponent")]
[DefaultEntityComponentRenderer(typeof(RiverProcessor))]
public sealed class RiverComponent : ActivableEntityComponent
{
    private IReadOnlyList<RiverMeshData> meshes = Array.Empty<RiverMeshData>();

    public IReadOnlyList<RiverMeshData> Meshes
    {
        get
        {
            var snapshot = new RiverMeshData[meshes.Count];
            for (int i = 0; i < meshes.Count; i++)
            {
                snapshot[i] = meshes[i].CloneSnapshot();
            }

            return snapshot;
        }
    }

    public RiverRenderSettings Settings { get; } = new();
    public int MeshCount => meshes.Count;
    public int Version { get; private set; }
    public RiverRuntimeLoadState RuntimeLoadState { get; private set; } = RiverRuntimeLoadState.NotAttempted;
    public RiverRuntimeLoadConfig FailedRuntimeLoadConfig { get; private set; }

    public void SetMeshes(IReadOnlyList<RiverMeshData> newMeshes)
    {
        if (newMeshes == null) throw new ArgumentNullException(nameof(newMeshes));

        var snapshot = new RiverMeshData[newMeshes.Count];
        for (int i = 0; i < newMeshes.Count; i++)
        {
            snapshot[i] = newMeshes[i].CloneSnapshot();
        }

        meshes = snapshot;
        Version++;
    }

    public void Clear()
    {
        meshes = Array.Empty<RiverMeshData>();
        Version++;
    }

    public bool ShouldAttemptRuntimeLoad(RiverRuntimeLoadConfig config)
    {
        return RuntimeLoadState != RiverRuntimeLoadState.Failed || FailedRuntimeLoadConfig != config;
    }

    public void MarkRuntimeLoadSuccess()
    {
        RuntimeLoadState = RiverRuntimeLoadState.Loaded;
        FailedRuntimeLoadConfig = default;
    }

    public void MarkRuntimeNoRiverResource()
    {
        Clear();
        RuntimeLoadState = RiverRuntimeLoadState.NoRiverResource;
        FailedRuntimeLoadConfig = default;
    }

    public void MarkRuntimeLoadFailure(RiverRuntimeLoadConfig config)
    {
        Clear();
        RuntimeLoadState = RiverRuntimeLoadState.Failed;
        FailedRuntimeLoadConfig = config;
    }
}
