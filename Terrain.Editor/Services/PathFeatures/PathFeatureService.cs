#nullable enable

using System;
using System.Collections.Generic;
using Terrain.Editor.Models;
using Terrain.Editor.Services;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Gizmos;
using Stride.Graphics;
using Stride.Graphics.GeometricPrimitives;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Terrain.Editor;
using Terrain.Editor.Rendering;
using Terrain.Editor.Rendering.Materials;
using Terrain.Editor.Services.Commands;
using StrideColor = Stride.Core.Mathematics.Color;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Editor.Services.PathFeatures;

public sealed class PathFeatureService : IDisposable
{
    public const RenderGroup PathRenderGroup = RenderGroup.Group1;

    private const float NodePickRadius = 5.0f;
    private const float SegmentPickRadius = 4.0f;
    private const float SketchPointSpacing = 5.0f;
    private const float SketchSimplificationTolerance = 2.5f;
    private const float CurveSampleSpacing = 4.0f;
    private const float CurveSubdivisionTolerance = 0.35f;
    private const float MinCurvePointSpacing = 0.05f;
    private const float MaxEdgeDeviation = 0.12f;
    private const float CurveTangentSampleStep = 0.02f;
    private const float TerrainBrushSpacing = 0.75f;
    private const float MeshFollowTerrainSpacing = 1.0f;
    private const float MeshFollowTerrainHeightTolerance = 0.08f;
    private const int MaxCurveSubdivisionDepth = 8;
    private const int MaxMeshFollowTerrainSubdivisionDepth = 6;
    private const float MeshVerticalOffset = 0.02f;
    private const float NodeGizmoVerticalOffset = 1.35f;
    private const float RiverTextureRepeatUScale = 1.0f / 35.0f;
    private const float DirtRoadTextureAspect = 4096.0f / 128.0f;
    private const float PavedRoadTextureAspect = 512.0f / 64.0f;
    private const string DirtRoadDiffuseFileName = "road_dirt_diffuse.dds";
    private const string DirtRoadNormalFileName = "road_dirt_normal.dds";
    private const string DirtRoadPropertiesFileName = "road_dirt_properties.dds";
    private const string PavedRoadDiffuseFileName = "roadpaved_diffuse.dds";
    private const string PavedRoadNormalFileName = "roadpaved_normal.dds";
    private const string PavedRoadPropertiesFileName = "roadpaved_properties.dds";
    private const float RoadEdgeFadeStart = 0.86f;
    private const float RoadEndFadeoutFactor = 4.0f;
    private const float RoadAlphaClipThreshold = 0.08f;
    private const float RiverEdgeFadeStart = 0.68f;
    private const float RiverFlowTiling = 0.22f;
    private const float RiverFlowStrength = 0.18f;
    private const float RiverHighlightStrength = 0.22f;

    private readonly GraphicsDevice graphicsDevice;
    private readonly Scene scene;
    private readonly TerrainManager terrainManager;
    private readonly PathFeatureParameters pathParameters = PathFeatureParameters.Instance;
    private readonly Dictionary<Guid, PathNode> nodes = new();
    private readonly List<PathFeature> features = new();
    private readonly Dictionary<Guid, PathFeatureMeshHandle> meshHandles = new();
    private readonly Dictionary<Guid, PathNodeGizmoHandle> nodeGizmos = new();
    private ushort[]? baseHeightData;
    private Texture? dirtRoadDiffuseTexture;
    private Texture? dirtRoadNormalTexture;
    private Texture? dirtRoadPropertiesTexture;
    private SamplerState? roadSurfaceSampler;
    private Texture? pavedRoadDiffuseTexture;
    private Texture? pavedRoadNormalTexture;
    private Texture? pavedRoadPropertiesTexture;
    private Texture? fallbackPathDiffuseTexture;
    private Texture? fallbackPathNormalTexture;
    private Material? dirtRoadMaterial;
    private Material? pavedRoadMaterial;
    private Material? riverMaterial;
    private Material? normalNodeGizmoMaterial;
    private Material? connectedNodeGizmoMaterial;
    private Material? selectedNodeGizmoMaterial;
    private bool gizmosVisible;
    private bool isSyncingParameters;
    private PathEditOperation? activeOperation;
    private Guid? selectedFeatureId;
    private Guid? selectedNodeId;

    public PathFeatureService(GraphicsDevice graphicsDevice, Scene scene, TerrainManager terrainManager)
    {
        this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
        this.terrainManager = terrainManager ?? throw new ArgumentNullException(nameof(terrainManager));
        pathParameters.ParametersChanged += OnPathParametersChanged;
    }

    public IReadOnlyList<PathFeature> Features => features;

    public IReadOnlyDictionary<Guid, PathNode> Nodes => nodes;

    public Guid? SelectedFeatureId => selectedFeatureId;

    public Guid? SelectedNodeId => selectedNodeId;

    public void RefreshSelectedFeatureParameters()
    {
        SyncParametersFromSelection();
    }

    public event EventHandler? NetworkChanged;

    public event EventHandler<PathFeatureSelectionChangedEventArgs>? SelectionChanged;

    public void BeginPointerEdit(Vector3 worldPosition, PathFeatureKind kind, bool sketchMode)
    {
        if (!terrainManager.HasHeightCache)
            return;

        EndPointerEdit(commit: false);
        EnsureBaseHeightData();
        var operation = new PathEditOperation(
            this,
            terrainManager,
            CaptureSnapshot(),
            CaptureAllHeightChunks(),
            sketchMode ? $"Sketch {kind}" : $"Edit {kind}");
        activeOperation = operation;

        if (TryPickNode(worldPosition, NodePickRadius, ignoredNodeId: null, out Guid hitNodeId, out PathFeature? hitFeature))
        {
            Select(hitFeature?.Id, hitNodeId);
            operation.DraggedNodeId = hitNodeId;
            return;
        }

        if (TryPickSegment(worldPosition, SegmentPickRadius, out PathFeature? segmentFeature, out int insertIndex)
            && segmentFeature != null)
        {
            Guid insertedNodeId = CreateNode(worldPosition);
            segmentFeature.NodeIds.Insert(insertIndex, insertedNodeId);
            Select(segmentFeature.Id, insertedNodeId);
            operation.DraggedNodeId = insertedNodeId;
            RebuildPathTerrainAndMeshes();
            NotifyNetworkChanged();
            return;
        }

        PathFeature feature = ResolveAppendTarget(kind);
        Guid nodeId = ResolveSnappedOrNewNode(worldPosition, ignoredNodeId: null);
        if (feature.NodeIds.Count == 0 || feature.NodeIds[^1] != nodeId)
        {
            feature.NodeIds.Add(nodeId);
        }

        Select(feature.Id, nodeId);
        operation.DraggedNodeId = nodeId;
        operation.LastSketchPoint = worldPosition;
        if (operation.SketchMode)
        {
            operation.SketchNodeIds.Add(nodeId);
            operation.SketchPositions.Add(worldPosition);
        }
        RebuildPathTerrainAndMeshes();
        NotifyNetworkChanged();
    }

    public void ContinuePointerEdit(Vector3 worldPosition)
    {
        if (activeOperation == null)
            return;

        if (activeOperation.SketchMode)
        {
            ContinueSketch(worldPosition);
            return;
        }

        if (activeOperation.DraggedNodeId is not { } nodeId || !nodes.TryGetValue(nodeId, out PathNode? node))
            return;

        node.Position = worldPosition;
        RebuildPathTerrainAndMeshes();
        NotifyNetworkChanged();
    }

    public void EndPointerEdit(bool commit)
    {
        if (activeOperation == null)
            return;

        PathEditOperation operation = activeOperation;

        if (commit && operation.SketchMode)
        {
            FinalizeSketch(operation);
        }

        if (commit && activeOperation.DraggedNodeId is { } draggedNodeId)
        {
            TryMergeDraggedNode(draggedNodeId);
            RebuildPathTerrainAndMeshes();
        }

        activeOperation = null;

        if (!commit)
        {
            operation.Command.Undo();
            return;
        }

        operation.Command.CaptureAfter(CaptureSnapshot(), CaptureAllHeightChunks());
        if (operation.Command.HasChanges())
        {
            HistoryManager.Instance.BeginCommand(operation.Command);
            HistoryManager.Instance.CommitCommand();
            ProjectManager.Instance.MarkDirty();
        }
    }

    public bool DeleteSelectedNode()
    {
        if (selectedNodeId is not { } nodeId || selectedFeatureId is not { } featureId)
            return false;

        return ExecuteImmediateEdit("Delete Path Node", () =>
        {
            PathFeature? feature = FindFeature(featureId);
            if (feature == null)
                return false;

            feature.NodeIds.RemoveAll(id => id == nodeId);
            RemoveNodeIfOrphan(nodeId);
            if (feature.NodeIds.Count < 2)
            {
                features.Remove(feature);
                selectedFeatureId = null;
            }
            else
            {
                selectedFeatureId = feature.Id;
            }

            selectedNodeId = null;
            RebuildPathTerrainAndMeshes();
            return true;
        });
    }

    public bool DisconnectSelectedNode()
    {
        if (selectedNodeId is not { } nodeId || selectedFeatureId is not { } featureId)
            return false;

        return ExecuteImmediateEdit("Disconnect Path Node", () =>
        {
            PathFeature? feature = FindFeature(featureId);
            if (feature == null || !nodes.TryGetValue(nodeId, out PathNode? sourceNode))
                return false;

            int useCount = features.Sum(item => item.NodeIds.Count(id => id == nodeId));
            if (useCount <= 1)
                return false;

            Guid replacementId = CreateNode(sourceNode.Position);
            for (int i = 0; i < feature.NodeIds.Count; i++)
            {
                if (feature.NodeIds[i] == nodeId)
                    feature.NodeIds[i] = replacementId;
            }

            selectedNodeId = replacementId;
            selectedFeatureId = feature.Id;
            RebuildPathTerrainAndMeshes();
            return true;
        });
    }

    public PathNetworkSnapshot CaptureSnapshot()
    {
        var snapshot = new PathNetworkSnapshot
        {
            SelectedFeatureId = selectedFeatureId,
            SelectedNodeId = selectedNodeId,
        };
        snapshot.Nodes.AddRange(nodes.Values.Select(static node => node.Clone()));
        snapshot.Features.AddRange(features.Select(static feature => feature.Clone()));
        return snapshot;
    }

    public void RestoreSnapshot(PathNetworkSnapshot snapshot)
    {
        RestoreSnapshotInternal(snapshot);
        RebuildPathTerrainAndMeshes();
        ProjectManager.Instance.MarkDirty();
    }

    public void RestoreSnapshotFromCommand(PathNetworkSnapshot snapshot)
    {
        RestoreSnapshotInternal(snapshot);
    }

    public void RestoreSnapshotFromProject(PathNetworkSnapshot snapshot)
    {
        RestoreSnapshotInternal(snapshot);
        ResetBaseHeightFromCurrentTerrain();
        RebuildPathTerrainAndMeshes();
    }

    public ushort[]? GetHeightDataForSave()
    {
        return baseHeightData ?? terrainManager.HeightDataCache;
    }

    public void ApplyExternalHeightEditDeltas(
        IEnumerable<(TerrainChunkRegion Region, ushort[] Before, ushort[] After)> deltas,
        bool applyAfterState)
    {
        if (features.Count == 0 || baseHeightData == null || terrainManager.HeightDataCache == null)
            return;

        int dataWidth = terrainManager.HeightCacheWidth;
        int dataHeight = terrainManager.HeightCacheHeight;
        if (baseHeightData.Length != dataWidth * dataHeight)
            return;

        bool changed = false;
        foreach ((TerrainChunkRegion region, ushort[] before, ushort[] after) in deltas)
        {
            if (region.Width <= 0 || region.Height <= 0 || before.Length != after.Length)
                continue;

            int expectedLength = region.Width * region.Height;
            if (before.Length != expectedLength)
                continue;

            for (int row = 0; row < region.Height; row++)
            {
                int srcOffset = row * region.Width;
                int dstOffset = (region.Y + row) * dataWidth + region.X;
                if (dstOffset < 0 || dstOffset + region.Width > baseHeightData.Length)
                    continue;

                for (int col = 0; col < region.Width; col++)
                {
                    int sourceIndex = srcOffset + col;
                    int destinationIndex = dstOffset + col;
                    int delta = applyAfterState
                        ? after[sourceIndex] - before[sourceIndex]
                        : before[sourceIndex] - after[sourceIndex];
                    if (delta == 0)
                        continue;

                    baseHeightData[destinationIndex] = (ushort)Math.Clamp(baseHeightData[destinationIndex] + delta, 0, ushort.MaxValue);
                    changed = true;
                }
            }
        }

        if (changed)
            RebuildPathTerrainAndMeshes();
    }

    public void Clear()
    {
        nodes.Clear();
        features.Clear();
        selectedFeatureId = null;
        selectedNodeId = null;
        activeOperation = null;
        baseHeightData = null;
        foreach (PathFeatureMeshHandle handle in meshHandles.Values)
            handle.Dispose(scene);
        meshHandles.Clear();
        foreach (PathNodeGizmoHandle handle in nodeGizmos.Values)
            handle.Dispose(scene);
        nodeGizmos.Clear();
        dirtRoadMaterial = null;
        pavedRoadMaterial = null;
        riverMaterial = null;
        dirtRoadDiffuseTexture?.Dispose();
        dirtRoadDiffuseTexture = null;
        dirtRoadNormalTexture?.Dispose();
        dirtRoadNormalTexture = null;
        dirtRoadPropertiesTexture?.Dispose();
        dirtRoadPropertiesTexture = null;
        roadSurfaceSampler?.Dispose();
        roadSurfaceSampler = null;
        pavedRoadDiffuseTexture?.Dispose();
        pavedRoadDiffuseTexture = null;
        pavedRoadNormalTexture?.Dispose();
        pavedRoadNormalTexture = null;
        pavedRoadPropertiesTexture?.Dispose();
        pavedRoadPropertiesTexture = null;
        fallbackPathDiffuseTexture?.Dispose();
        fallbackPathDiffuseTexture = null;
        fallbackPathNormalTexture?.Dispose();
        fallbackPathNormalTexture = null;
        fallbackPathPropertiesTexture?.Dispose();
        fallbackPathPropertiesTexture = null;
        normalNodeGizmoMaterial = null;
        connectedNodeGizmoMaterial = null;
        selectedNodeGizmoMaterial = null;
        NotifyNetworkChanged();
        NotifySelectionChanged();
    }

    public void SetGizmosVisible(bool visible)
    {
        if (gizmosVisible == visible)
            return;

        gizmosVisible = visible;
        RefreshNodeGizmos();
    }

    public void RebuildAllMeshes()
    {
        var liveIds = features.Select(static feature => feature.Id).ToHashSet();
        foreach (Guid staleId in meshHandles.Keys.Where(id => !liveIds.Contains(id)).ToArray())
            RemoveMesh(staleId);

        foreach (PathFeature feature in features)
            RebuildMesh(feature);

        RefreshNodeGizmos();
    }

    public void Dispose()
    {
        pathParameters.ParametersChanged -= OnPathParametersChanged;
        Clear();
    }

    private void OnPathParametersChanged(object? sender, EventArgs e)
    {
        if (isSyncingParameters)
            return;

        if (EditorState.Instance.CurrentEditorMode != EditorMode.Path)
            return;

        if (selectedFeatureId is not { } featureId)
            return;

        PathFeature? feature = FindFeature(featureId);
        if (feature == null)
            return;

        if (feature.Kind != pathParameters.Kind)
        {
            selectedFeatureId = null;
            selectedNodeId = null;
            RefreshNodeGizmos();
            NotifySelectionChanged();
            return;
        }

        PathFeatureStyle nextStyle = pathParameters.CreateStyle();
        if (AreStylesEqual(feature.Style, nextStyle))
            return;

        ExecuteImmediateEdit("Edit Path Style", () =>
        {
            feature.Style = nextStyle;
            RebuildPathTerrainAndMeshes();
            return true;
        });
    }

    private void ContinueSketch(Vector3 worldPosition)
    {
        if (activeOperation == null || selectedFeatureId is not { } featureId)
            return;

        PathFeature? feature = FindFeature(featureId);
        if (feature == null)
            return;

        Vector3 previous = activeOperation.LastSketchPoint ?? worldPosition;
        float distance = DistanceXZ(previous, worldPosition);
        if (distance < SketchPointSpacing)
            return;

        Guid nodeId = ResolveSnappedOrNewNode(worldPosition, ignoredNodeId: feature.NodeIds.LastOrDefault());
        if (feature.NodeIds.Count == 0 || feature.NodeIds[^1] != nodeId)
            feature.NodeIds.Add(nodeId);

        activeOperation.SketchNodeIds.Add(nodeId);
        activeOperation.SketchPositions.Add(worldPosition);
        Select(feature.Id, nodeId);
        activeOperation.DraggedNodeId = nodeId;
        activeOperation.LastSketchPoint = worldPosition;
        RebuildPathTerrainAndMeshes();
        NotifyNetworkChanged();
    }

    private void FinalizeSketch(PathEditOperation operation)
    {
        if (selectedFeatureId is not { } featureId)
            return;

        PathFeature? feature = FindFeature(featureId);
        if (feature == null)
            return;

        if (operation.SketchNodeIds.Count <= 2 || operation.SketchPositions.Count <= 2)
            return;

        List<int> retainedIndices = SimplifyPolylineIndices(operation.SketchPositions, SketchSimplificationTolerance);
        PreserveSharedSketchNodes(operation, retainedIndices);
        if (retainedIndices.Count >= operation.SketchNodeIds.Count)
            return;

        Guid firstNodeId = operation.SketchNodeIds[0];
        Guid lastNodeId = operation.SketchNodeIds[^1];
        var retainedNodeIds = retainedIndices
            .Select(index => operation.SketchNodeIds[index])
            .ToList();

        if (retainedNodeIds.Count < 2)
            return;

        int firstIndex = feature.NodeIds.FindIndex(id => id == firstNodeId);
        if (firstIndex < 0)
            return;

        int sketchCount = operation.SketchNodeIds.Count;
        if (firstIndex + sketchCount > feature.NodeIds.Count)
            return;

        bool matchesSegment = true;
        for (int i = 0; i < sketchCount; i++)
        {
            if (feature.NodeIds[firstIndex + i] != operation.SketchNodeIds[i])
            {
                matchesSegment = false;
                break;
            }
        }

        if (!matchesSegment)
            return;

        feature.NodeIds.RemoveRange(firstIndex, sketchCount);
        feature.NodeIds.InsertRange(firstIndex, retainedNodeIds);

        foreach (Guid nodeId in operation.SketchNodeIds)
        {
            if (nodeId == firstNodeId || nodeId == lastNodeId || retainedNodeIds.Contains(nodeId))
                continue;

            RemoveNodeIfOrphan(nodeId);
        }

        operation.DraggedNodeId = lastNodeId;
        Select(feature.Id, lastNodeId);
        RebuildPathTerrainAndMeshes();
        NotifyNetworkChanged();
    }

    private static List<int> SimplifyPolylineIndices(IReadOnlyList<Vector3> points, float tolerance)
    {
        var retained = new List<int>();
        if (points.Count == 0)
            return retained;

        retained.Add(0);
        if (points.Count == 1)
            return retained;

        SimplifyPolylineSection(points, 0, points.Count - 1, tolerance, retained);
        retained.Add(points.Count - 1);
        retained.Sort();
        return retained.Distinct().ToList();
    }

    private void PreserveSharedSketchNodes(PathEditOperation operation, List<int> retainedIndices)
    {
        for (int i = 1; i < operation.SketchNodeIds.Count - 1; i++)
        {
            Guid nodeId = operation.SketchNodeIds[i];
            if (FindFeaturesUsingNode(nodeId).Skip(1).Any())
            {
                retainedIndices.Add(i);
            }
        }
    }

    private static void SimplifyPolylineSection(IReadOnlyList<Vector3> points, int startIndex, int endIndex, float tolerance, List<int> retained)
    {
        if (endIndex <= startIndex + 1)
            return;

        float maxDistance = -1.0f;
        int splitIndex = -1;
        Vector3 start = points[startIndex];
        Vector3 end = points[endIndex];

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            float distance = DistancePointToSegmentXZ(points[i], start, end, out _);
            if (distance <= maxDistance)
                continue;

            maxDistance = distance;
            splitIndex = i;
        }

        if (splitIndex < 0 || maxDistance <= tolerance)
            return;

        retained.Add(splitIndex);
        SimplifyPolylineSection(points, startIndex, splitIndex, tolerance, retained);
        SimplifyPolylineSection(points, splitIndex, endIndex, tolerance, retained);
    }

    private bool ExecuteImmediateEdit(string description, Func<bool> edit)
    {
        EnsureBaseHeightData();
        var command = new PathFeatureEditCommand(this, terrainManager, CaptureSnapshot(), CaptureAllHeightChunks(), description);
        if (!edit())
            return false;

        SyncParametersFromSelection();
        command.CaptureAfter(CaptureSnapshot(), CaptureAllHeightChunks());
        HistoryManager.Instance.BeginCommand(command);
        HistoryManager.Instance.CommitCommand();
        ProjectManager.Instance.MarkDirty();
        NotifyNetworkChanged();
        NotifySelectionChanged();
        return true;
    }

    private PathFeature ResolveAppendTarget(PathFeatureKind kind)
    {
        if (selectedFeatureId is { } selectedId)
        {
            PathFeature? selected = FindFeature(selectedId);
            if (selected is { Kind: var selectedKind } && selectedKind == kind)
                return selected;
        }

        var feature = new PathFeature
        {
            Kind = kind,
            Name = $"{kind} {features.Count(item => item.Kind == kind) + 1}",
            Style = PathFeatureParameters.Instance.CreateStyle(),
        };
        feature.Style.Depth = kind == PathFeatureKind.River && feature.Style.Depth <= 0.0f ? 2.0f : feature.Style.Depth;
        features.Add(feature);
        return feature;
    }

    private Guid CreateNode(Vector3 worldPosition)
    {
        var node = new PathNode
        {
            Id = Guid.NewGuid(),
            Position = worldPosition,
        };
        nodes[node.Id] = node;
        return node.Id;
    }

    private Guid ResolveSnappedOrNewNode(Vector3 worldPosition, Guid? ignoredNodeId)
    {
        return TryPickNode(worldPosition, NodePickRadius, ignoredNodeId, out Guid snappedId, out _)
            ? snappedId
            : CreateNode(worldPosition);
    }

    private bool TryMergeDraggedNode(Guid draggedNodeId)
    {
        if (!nodes.TryGetValue(draggedNodeId, out PathNode? draggedNode))
            return false;

        if (!TryPickNode(draggedNode.Position, NodePickRadius, draggedNodeId, out Guid targetNodeId, out _))
            return false;

        foreach (PathFeature feature in features)
        {
            bool changed = false;
            for (int i = 0; i < feature.NodeIds.Count; i++)
            {
                if (feature.NodeIds[i] == draggedNodeId)
                {
                    feature.NodeIds[i] = targetNodeId;
                    changed = true;
                }
            }

            if (changed)
                RebuildPathTerrainAndMeshes();
        }

        RemoveNodeIfOrphan(draggedNodeId);
        selectedNodeId = targetNodeId;
        SyncParametersFromSelection();
        NotifyNetworkChanged();
        NotifySelectionChanged();
        return true;
    }

    private void Select(Guid? featureId, Guid? nodeId)
    {
        if (selectedFeatureId == featureId && selectedNodeId == nodeId)
            return;

        selectedFeatureId = featureId;
        selectedNodeId = nodeId;
        SyncParametersFromSelection();
        RefreshNodeGizmos();
        NotifySelectionChanged();
    }

    private bool TryPickNode(Vector3 worldPosition, float radius, Guid? ignoredNodeId, out Guid nodeId, out PathFeature? feature)
    {
        nodeId = Guid.Empty;
        feature = null;
        float bestDistance = radius;
        foreach (PathNode node in nodes.Values)
        {
            if (node.Id == ignoredNodeId)
                continue;

            float distance = DistanceXZ(node.Position, worldPosition);
            if (distance > bestDistance)
                continue;

            bestDistance = distance;
            nodeId = node.Id;
            feature = FindFeaturesUsingNode(node.Id).FirstOrDefault();
        }

        return nodeId != Guid.Empty;
    }

    private bool TryPickSegment(Vector3 worldPosition, float radius, out PathFeature? feature, out int insertIndex)
    {
        feature = null;
        insertIndex = -1;
        float bestDistance = radius;
        foreach (PathFeature candidate in features)
        {
            foreach (PathCurveSegment segment in EnumerateCurveSegments(candidate))
            {
                float distance = DistancePointToSegmentXZ(worldPosition, segment.A.Position, segment.B.Position, out _);
                if (distance > bestDistance)
                    continue;

                bestDistance = distance;
                feature = candidate;
                insertIndex = segment.InsertIndex;
            }
        }

        return feature != null;
    }

    private PathFeature? FindFeature(Guid featureId)
    {
        return features.FirstOrDefault(feature => feature.Id == featureId);
    }

    private IEnumerable<PathFeature> FindFeaturesUsingNode(Guid nodeId)
    {
        return features.Where(feature => feature.NodeIds.Contains(nodeId));
    }

    private void RemoveNodeIfOrphan(Guid nodeId)
    {
        if (features.Any(feature => feature.NodeIds.Contains(nodeId)))
            return;

        nodes.Remove(nodeId);
    }

    private void ResetBaseHeightFromCurrentTerrain()
    {
        baseHeightData = terrainManager.HeightDataCache == null
            ? null
            : CopyFullHeightData(terrainManager.HeightDataCache);
    }

    private void EnsureBaseHeightData()
    {
        ushort[]? heightData = terrainManager.HeightDataCache;
        if (heightData == null)
            return;

        if (baseHeightData == null || baseHeightData.Length != heightData.Length)
            baseHeightData = CopyFullHeightData(heightData);
    }

    private void RebuildPathTerrainAndMeshes()
    {
        ushort[]? heightData = terrainManager.HeightDataCache;
        if (heightData != null)
        {
            EnsureBaseHeightData();
            if (baseHeightData != null && baseHeightData.Length == heightData.Length)
            {
                Array.Copy(baseHeightData, heightData, baseHeightData.Length);
                bool heightChanged = false;
                foreach (PathFeature feature in features)
                {
                    if (feature.Kind != PathFeatureKind.River)
                        continue;

                    ApplyFeatureTerrain(feature);
                    heightChanged = true;
                }

                if (heightChanged)
                {
                    terrainManager.MarkDataDirty(
                        TerrainDataChannel.Height,
                        terrainManager.HeightCacheWidth / 2,
                        terrainManager.HeightCacheHeight / 2,
                        Math.Max(terrainManager.HeightCacheWidth, terrainManager.HeightCacheHeight) * 0.5f);
                }
            }
        }

        RebuildAllMeshes();
    }

    private void RefreshNodeGizmos()
    {
        HashSet<Guid> liveNodeIds = features.SelectMany(static feature => feature.NodeIds).ToHashSet();
        foreach (Guid staleId in nodeGizmos.Keys.Where(id => !liveNodeIds.Contains(id)).ToArray())
        {
            nodeGizmos[staleId].Dispose(scene);
            nodeGizmos.Remove(staleId);
        }

        foreach (Guid nodeId in liveNodeIds)
        {
            if (!nodes.TryGetValue(nodeId, out PathNode? node))
                continue;

            bool selected = selectedNodeId == nodeId;
            bool connected = features.Sum(feature => feature.NodeIds.Count(id => id == nodeId)) > 1;
            if (!nodeGizmos.TryGetValue(nodeId, out PathNodeGizmoHandle? handle))
            {
                handle = CreateNodeGizmo(nodeId);
                nodeGizmos[nodeId] = handle;
            }

            Vector3 position = node.Position;
            float y = terrainManager.GetHeightAtPosition(position.X, position.Z) ?? position.Y;
            handle.Update(
                new Vector3(position.X, y + NodeGizmoVerticalOffset, position.Z),
                selected,
                connected,
                gizmosVisible,
                GetNormalNodeGizmoMaterial(),
                GetConnectedNodeGizmoMaterial(),
                GetSelectedNodeGizmoMaterial());
        }
    }

    private PathNodeGizmoHandle CreateNodeGizmo(Guid nodeId)
    {
        GeometricPrimitive sphere = GeometricPrimitive.Sphere.New(graphicsDevice);
        Buffer vertexBuffer = sphere.VertexBuffer;
        Buffer indexBuffer = sphere.IndexBuffer;
        var vertexBufferBinding = new VertexBufferBinding(
            vertexBuffer,
            new VertexPositionNormalTexture().GetLayout(),
            vertexBuffer.ElementCount);
        var indexBufferBinding = new IndexBufferBinding(indexBuffer, sphere.IsIndex32Bits, indexBuffer.ElementCount);
        var meshDraw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            DrawCount = indexBuffer.ElementCount,
            VertexBuffers =
            [
                vertexBufferBinding,
            ],
            IndexBuffer = indexBufferBinding,
        };

        var model = new Model();
        model.Meshes.Add(new Mesh(meshDraw, new ParameterCollection()) { MaterialIndex = 0 });
        model.Materials.Add(GetNormalNodeGizmoMaterial());

        var modelComponent = new ModelComponent
        {
            Model = model,
            RenderGroup = IEntityGizmo.PickingRenderGroup,
        };
        var entity = new Entity($"PathNodeGizmo_{nodeId:D}")
        {
            modelComponent,
        };
        scene.Entities.Add(entity);
        return new PathNodeGizmoHandle(entity, modelComponent, sphere);
    }

    private Material GetNormalNodeGizmoMaterial()
    {
        return normalNodeGizmoMaterial ??= CreateGizmoMaterial(new Color4(1.0f, 0.92f, 0.25f, 1.0f));
    }

    private Material GetConnectedNodeGizmoMaterial()
    {
        return connectedNodeGizmoMaterial ??= CreateGizmoMaterial(new Color4(0.1f, 0.85f, 0.95f, 1.0f));
    }

    private Material GetSelectedNodeGizmoMaterial()
    {
        return selectedNodeGizmoMaterial ??= CreateGizmoMaterial(new Color4(1.0f, 0.48f, 0.12f, 1.0f));
    }

    private void ApplyFeatureTerrain(PathFeature feature)
    {
        if (feature.NodeIds.Count < 2 || terrainManager.HeightDataCache == null)
            return;

        IReadOnlyList<PathCurvePoint> curvePoints = BuildCurvePoints(feature);
        if (curvePoints.Count < 2)
            return;

        float halfWidth = Math.Max(0.5f, feature.Style.Width * 0.5f);
        IReadOnlyList<PathRibbonRow> ribbonRows = BuildRibbonRows(curvePoints, feature.Style.CornerSpan, halfWidth);
        if (ribbonRows.Count < 2)
            return;

        IReadOnlyList<PathTerrainSample> terrainSamples = BuildTerrainSamples(ribbonRows);
        if (terrainSamples.Count == 0)
            return;

        ushort[] heightData = terrainManager.HeightDataCache;
        int width = terrainManager.HeightCacheWidth;
        int height = terrainManager.HeightCacheHeight;
        float worldToRaw = ushort.MaxValue / terrainManager.HeightScale;
        float slopeWidth = Math.Max(0.1f, feature.Style.SideSlope);
        float editRadius = halfWidth + slopeWidth;
        int lateralSteps = Math.Max(1, (int)MathF.Ceiling(editRadius / TerrainBrushSpacing));

        foreach (PathTerrainSample sample in terrainSamples)
        {
            float targetRaw = (sample.Position.Y - feature.Style.Depth) * worldToRaw;
            ApplyTerrainBandSample(
                feature.Kind,
                heightData,
                width,
                height,
                sample,
                targetRaw,
                halfWidth,
                slopeWidth,
                editRadius,
                lateralSteps);
        }

        terrainManager.MarkDataDirty(
            TerrainDataChannel.Height,
            width / 2,
            height / 2,
            Math.Max(width, height) * 0.5f);
    }

    private static void ApplyTerrainBandSample(
        PathFeatureKind kind,
        ushort[] heightData,
        int width,
        int height,
        PathTerrainSample sample,
        float targetRaw,
        float halfWidth,
        float slopeWidth,
        float editRadius,
        int lateralSteps)
    {
        ApplyTerrainBandOffset(kind, heightData, width, height, sample, targetRaw, 0.0f, halfWidth, slopeWidth, editRadius);
        for (int step = 1; step <= lateralSteps; step++)
        {
            float sideDistance = Math.Min(step * TerrainBrushSpacing, editRadius);
            ApplyTerrainBandOffset(kind, heightData, width, height, sample, targetRaw, sideDistance, halfWidth, slopeWidth, editRadius);
            ApplyTerrainBandOffset(kind, heightData, width, height, sample, targetRaw, -sideDistance, halfWidth, slopeWidth, editRadius);
        }
    }

    private static void ApplyTerrainBandOffset(
        PathFeatureKind kind,
        ushort[] heightData,
        int width,
        int height,
        PathTerrainSample sample,
        float targetRaw,
        float sideDistance,
        float halfWidth,
        float slopeWidth,
        float editRadius)
    {
        float distance = MathF.Abs(sideDistance);
        if (distance > editRadius)
            return;

        float influence = ComputeTerrainInfluence(distance, halfWidth, slopeWidth);
        if (influence <= 0.0f)
            return;

        Vector3 worldPosition = sample.Position + sample.Side * sideDistance;
        int x = (int)MathF.Round(worldPosition.X);
        int z = (int)MathF.Round(worldPosition.Z);
        if (x < 0 || x >= width || z < 0 || z >= height)
            return;

        int index = z * width + x;
        float current = heightData[index];
        float blended = current + (targetRaw - current) * influence;
        float next = kind == PathFeatureKind.River
            ? Math.Min(current, blended)
            : blended;
        heightData[index] = (ushort)Math.Clamp(next, 0.0f, ushort.MaxValue);
    }

    private static float ComputeTerrainInfluence(float distance, float halfWidth, float slopeWidth)
    {
        if (distance <= halfWidth)
            return 1.0f;

        if (distance >= halfWidth + slopeWidth)
            return 0.0f;

        float t = Math.Clamp((distance - halfWidth) / slopeWidth, 0.0f, 1.0f);
        float smooth = t * t * (3.0f - 2.0f * t);
        return 1.0f - smooth;
    }

    private IReadOnlyList<HeightChunkDelta> CaptureAllHeightChunks()
    {
        var result = new List<HeightChunkDelta>();
        ushort[]? heightData = terrainManager.HeightDataCache;
        if (heightData == null)
            return result;

        int width = terrainManager.HeightCacheWidth;
        int height = terrainManager.HeightCacheHeight;
        int chunkSize = StrokeChunkTracker.DefaultChunkSize;
        for (int y = 0; y < height; y += chunkSize)
        {
            for (int x = 0; x < width; x += chunkSize)
            {
                int chunkWidth = Math.Min(chunkSize, width - x);
                int chunkHeight = Math.Min(chunkSize, height - y);
                var region = new TerrainChunkRegion(ComposeChunkKey(x / chunkSize, y / chunkSize), x, y, chunkWidth, chunkHeight);
                result.Add(new HeightChunkDelta(region, CopyChunk(heightData, region, width)));
            }
        }

        return result;
    }

    private static ushort[] CopyFullHeightData(ushort[] source)
    {
        var result = new ushort[source.Length];
        Array.Copy(source, result, source.Length);
        return result;
    }

    private static ushort[] CopyChunk(ushort[] source, TerrainChunkRegion region, int dataWidth)
    {
        var result = new ushort[region.Width * region.Height];
        for (int row = 0; row < region.Height; row++)
        {
            int srcOffset = (region.Y + row) * dataWidth + region.X;
            int dstOffset = row * region.Width;
            Array.Copy(source, srcOffset, result, dstOffset, region.Width);
        }

        return result;
    }

    private void RestoreSnapshotInternal(PathNetworkSnapshot snapshot)
    {
        nodes.Clear();
        foreach (PathNode node in snapshot.Nodes)
            nodes[node.Id] = node.Clone();

        features.Clear();
        features.AddRange(snapshot.Features.Select(static feature => feature.Clone()));
        selectedFeatureId = snapshot.SelectedFeatureId;
        selectedNodeId = snapshot.SelectedNodeId;
        SyncParametersFromSelection();
        RebuildAllMeshes();
        NotifyNetworkChanged();
        NotifySelectionChanged();
    }

    private void RebuildMesh(PathFeature feature)
    {
        RemoveMesh(feature.Id);

        if (feature.NodeIds.Count < 2)
            return;

        IReadOnlyList<PathCurvePoint> curvePoints = BuildCurvePoints(feature);
        if (curvePoints.Count < 2)
            return;

        float halfWidth = Math.Max(0.5f, feature.Style.Width * 0.5f);
        IReadOnlyList<PathRibbonRow> ribbonRows = BuildRibbonRows(curvePoints, feature.Style.CornerSpan, halfWidth);
        if (ribbonRows.Count < 2)
            return;
        IReadOnlyList<PathRibbonRow> meshRows = RefineRibbonRowsForTerrainFit(ribbonRows, halfWidth);
        if (meshRows.Count < 2)
            return;

        float[] rowHalfWidths = CreateUniformHalfWidths(meshRows.Count, halfWidth);

        var vertices = new List<PathMeshVertex>(meshRows.Count * 2);
        var cumulativeDistances = new float[meshRows.Count];
        float uvDistance = 0.0f;
        for (int i = 1; i < meshRows.Count; i++)
        {
            uvDistance += DistanceXZ(meshRows[i - 1].Position, meshRows[i].Position);
            cumulativeDistances[i] = uvDistance;
        }

        float roadRepeatScale = feature.Kind == PathFeatureKind.Road
            ? GetRoadTextureRepeatScale(feature.Style)
            : RiverTextureRepeatUScale;
        float maxDistance = cumulativeDistances[^1];
        for (int i = 0; i < meshRows.Count; i++)
        {
            PathRibbonRow row = meshRows[i];
            float rowHalfWidth = rowHalfWidths[i];
            Vector3 leftPosition = row.Position - row.Side * rowHalfWidth;
            Vector3 rightPosition = row.Position + row.Side * rowHalfWidth;
            leftPosition.Y = (terrainManager.GetHeightAtPosition(leftPosition.X, leftPosition.Z) ?? row.Position.Y) + MeshVerticalOffset;
            rightPosition.Y = (terrainManager.GetHeightAtPosition(rightPosition.X, rightPosition.Z) ?? row.Position.Y) + MeshVerticalOffset;
            float distanceAlongPath = cumulativeDistances[i];
            float u = distanceAlongPath * roadRepeatScale;
            Vector2 texCoord1 = new(distanceAlongPath, maxDistance);
            Vector3 tangentDirection = ComputeRibbonTangent(meshRows, i);
            Vector4 tangent = new(tangentDirection.X, tangentDirection.Y, tangentDirection.Z, 1.0f);
            Vector3 leftNormal = ComputeTerrainNormal(leftPosition.X, leftPosition.Z);
            Vector3 rightNormal = ComputeTerrainNormal(rightPosition.X, rightPosition.Z);
            vertices.Add(new PathMeshVertex
            {
                Position = leftPosition,
                Normal = leftNormal,
                Tangent = tangent,
                TexCoord = new Vector2(u, 0.0f),
                TexCoord1 = texCoord1,
            });
            vertices.Add(new PathMeshVertex
            {
                Position = rightPosition,
                Normal = rightNormal,
                Tangent = tangent,
                TexCoord = new Vector2(u, 1.0f),
                TexCoord1 = texCoord1,
            });
        }

        if (vertices.Count < 4)
            return;

        var indices = new int[(meshRows.Count - 1) * 6];
        int index = 0;
        for (int i = 0; i < meshRows.Count - 1; i++)
        {
            int a = i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;
            indices[index++] = a;
            indices[index++] = c;
            indices[index++] = b;
            indices[index++] = b;
            indices[index++] = c;
            indices[index++] = d;
        }

        Buffer vertexBuffer = Buffer.Vertex.New(graphicsDevice, vertices.ToArray());
        Buffer indexBuffer = Buffer.Index.New(graphicsDevice, indices);
        var meshDraw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            DrawCount = indices.Length,
            VertexBuffers =
            [
                new VertexBufferBinding(vertexBuffer, PathMeshVertex.Layout, vertices.Count),
            ],
            IndexBuffer = new IndexBufferBinding(indexBuffer, true, indices.Length),
        };

        var mesh = new Mesh(meshDraw, new ParameterCollection())
        {
            MaterialIndex = 0,
        };
        var model = new Model();
        model.Meshes.Add(mesh);
        Material material = CreateMaterial(feature);
        model.Materials.Add(material);

        var entity = new Entity($"PathFeature_{feature.Name}")
        {
            new ModelComponent(model)
            {
                RenderGroup = PathRenderGroup,
                IsShadowCaster = false,
            },
        };
        scene.Entities.Add(entity);
        meshHandles[feature.Id] = new PathFeatureMeshHandle(entity, vertexBuffer, indexBuffer);
    }

    private IReadOnlyList<PathRibbonRow> RefineRibbonRowsForTerrainFit(IReadOnlyList<PathRibbonRow> ribbonRows, float halfWidth)
    {
        if (ribbonRows.Count <= 1)
            return ribbonRows;

        var result = new List<PathRibbonRow>(ribbonRows.Count * 2)
        {
            ribbonRows[0]
        };

        for (int i = 0; i < ribbonRows.Count - 1; i++)
        {
            SubdivideRibbonRowForTerrainFit(result, ribbonRows[i], ribbonRows[i + 1], halfWidth, 0);
        }

        return result;
    }

    private void SubdivideRibbonRowForTerrainFit(
        List<PathRibbonRow> result,
        PathRibbonRow startRow,
        PathRibbonRow endRow,
        float halfWidth,
        int depth)
    {
        if (depth >= MaxMeshFollowTerrainSubdivisionDepth)
        {
            AppendRibbonRow(result, endRow);
            return;
        }

        float segmentLength = DistanceXZ(startRow.Position, endRow.Position);
        PathRibbonRow midRow = LerpRibbonRow(startRow, endRow, 0.5f);
        float terrainDeviation = ComputeTerrainMidpointDeviation(startRow, midRow, endRow, halfWidth);

        if (segmentLength <= MeshFollowTerrainSpacing
            && terrainDeviation <= MeshFollowTerrainHeightTolerance)
        {
            AppendRibbonRow(result, endRow);
            return;
        }

        SubdivideRibbonRowForTerrainFit(result, startRow, midRow, halfWidth, depth + 1);
        SubdivideRibbonRowForTerrainFit(result, midRow, endRow, halfWidth, depth + 1);
    }

    private float ComputeTerrainMidpointDeviation(
        PathRibbonRow startRow,
        PathRibbonRow midRow,
        PathRibbonRow endRow,
        float halfWidth)
    {
        float startCenter = SampleTerrainHeight(startRow.Position);
        float endCenter = SampleTerrainHeight(endRow.Position);
        float actualMidCenter = SampleTerrainHeight(midRow.Position);
        float expectedMidCenter = (startCenter + endCenter) * 0.5f;

        Vector3 startLeftPosition = startRow.Position - startRow.Side * halfWidth;
        Vector3 endLeftPosition = endRow.Position - endRow.Side * halfWidth;
        Vector3 midLeftPosition = midRow.Position - midRow.Side * halfWidth;
        float startLeft = SampleTerrainHeight(startLeftPosition);
        float endLeft = SampleTerrainHeight(endLeftPosition);
        float actualMidLeft = SampleTerrainHeight(midLeftPosition);
        float expectedMidLeft = (startLeft + endLeft) * 0.5f;

        Vector3 startRightPosition = startRow.Position + startRow.Side * halfWidth;
        Vector3 endRightPosition = endRow.Position + endRow.Side * halfWidth;
        Vector3 midRightPosition = midRow.Position + midRow.Side * halfWidth;
        float startRight = SampleTerrainHeight(startRightPosition);
        float endRight = SampleTerrainHeight(endRightPosition);
        float actualMidRight = SampleTerrainHeight(midRightPosition);
        float expectedMidRight = (startRight + endRight) * 0.5f;

        return MathF.Max(
            MathF.Abs(actualMidCenter - expectedMidCenter),
            MathF.Max(
                MathF.Abs(actualMidLeft - expectedMidLeft),
                MathF.Abs(actualMidRight - expectedMidRight)));
    }

    private float SampleTerrainHeight(Vector3 position)
    {
        return terrainManager.GetHeightAtPosition(position.X, position.Z) ?? position.Y;
    }

    private void RemoveMesh(Guid featureId)
    {
        if (!meshHandles.Remove(featureId, out PathFeatureMeshHandle? handle))
            return;

        handle.Dispose(scene);
    }

    private Material CreateMaterial(PathFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        if (feature.Kind == PathFeatureKind.River)
            return riverMaterial ??= CreateFallbackRiverMaterial();

        return feature.Style.RoadStyle == PathRoadStyle.Paved
            ? pavedRoadMaterial ??= CreateRoadMaterial(PathRoadStyle.Paved)
            : dirtRoadMaterial ??= CreateRoadMaterial(PathRoadStyle.Dirt);
    }

    private Material CreateRoadMaterial(PathRoadStyle roadStyle)
    {
        (Texture? diffuseTexture, Texture? normalTexture, Texture? propertiesTexture) = GetOrLoadRoadTextures(roadStyle);
        bool useFallbackColors = diffuseTexture == null || normalTexture == null || propertiesTexture == null;
        diffuseTexture ??= GetFallbackPathDiffuseTexture();
        normalTexture ??= GetFallbackPathNormalTexture();
        propertiesTexture ??= GetFallbackPathPropertiesTexture();

        Color4 baseColor = useFallbackColors
            ? roadStyle == PathRoadStyle.Paved
                ? new Color4(0.32f, 0.30f, 0.28f, 1.0f)
                : new Color4(0.28f, 0.24f, 0.18f, 1.0f)
            : Color4.White;

        var descriptor = new MaterialDescriptor();
        descriptor.Attributes.Diffuse = new MaterialPathRoadFeature();
        descriptor.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();
        descriptor.Attributes.MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(0.28f));
        descriptor.Attributes.Specular = new MaterialMetalnessMapFeature(new ComputeFloat(0.0f));
        descriptor.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
        descriptor.Attributes.Transparency = new MaterialTransparencyBlendFeature();

        Material material = Material.New(graphicsDevice, descriptor);
        ParameterCollection parameters = material.Passes[0].Parameters;
        parameters.Set(MaterialKeys.HasNormalMap, true);
        SamplerState roadSampler = GetRoadSurfaceSampler();
        parameters.Set(PathRoadSurfaceKeys.DiffuseTexture, diffuseTexture);
        parameters.Set(PathRoadSurfaceKeys.DiffuseSampler, roadSampler);
        parameters.Set(PathRoadSurfaceKeys.NormalTexture, normalTexture);
        parameters.Set(PathRoadSurfaceKeys.NormalSampler, roadSampler);
        parameters.Set(PathRoadSurfaceKeys.PropertiesTexture, propertiesTexture);
        parameters.Set(PathRoadSurfaceKeys.PropertiesSampler, roadSampler);
        parameters.Set(PathRoadSurfaceKeys.BaseColor, baseColor);
        parameters.Set(PathRoadSurfaceKeys.EdgeFadeStart, RoadEdgeFadeStart);
        parameters.Set(PathRoadSurfaceKeys.NormalStrength, 1.0f);
        parameters.Set(PathRoadSurfaceKeys.EndFadeoutFactor, RoadEndFadeoutFactor);
        parameters.Set(PathRoadSurfaceKeys.AlphaClipThreshold, RoadAlphaClipThreshold);
        return material;
    }

    private Material CreateFallbackRoadMaterial(PathRoadStyle roadStyle)
    {
        return CreateRoadMaterial(roadStyle);
    }

    private Material CreateFallbackRiverMaterial()
    {
        var descriptor = new MaterialDescriptor();
        descriptor.Attributes.Diffuse = new MaterialPathRiverFeature();
        descriptor.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();
        descriptor.Attributes.MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(0.78f));
        descriptor.Attributes.Specular = new MaterialMetalnessMapFeature(new ComputeFloat(0.0f));
        descriptor.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
        descriptor.Attributes.Transparency = new MaterialTransparencyBlendFeature();

        Material material = Material.New(graphicsDevice, descriptor);
        ParameterCollection parameters = material.Passes[0].Parameters;
        parameters.Set(PathRiverSurfaceKeys.ShallowColor, new Color4(0.10f, 0.42f, 0.78f, 0.58f));
        parameters.Set(PathRiverSurfaceKeys.DeepColor, new Color4(0.03f, 0.18f, 0.42f, 0.90f));
        parameters.Set(PathRiverSurfaceKeys.EdgeFadeStart, RiverEdgeFadeStart);
        parameters.Set(PathRiverSurfaceKeys.FlowTiling, RiverFlowTiling);
        parameters.Set(PathRiverSurfaceKeys.FlowStrength, RiverFlowStrength);
        parameters.Set(PathRiverSurfaceKeys.HighlightStrength, RiverHighlightStrength);
        return material;
    }

    private (Texture? Diffuse, Texture? Normal, Texture? Properties) GetOrLoadRoadTextures(PathRoadStyle roadStyle)
    {
        switch (roadStyle)
        {
            case PathRoadStyle.Paved:
                pavedRoadDiffuseTexture ??= LoadRoadTexture(PavedRoadDiffuseFileName, isNormalMap: false);
                pavedRoadNormalTexture ??= LoadRoadTexture(PavedRoadNormalFileName, isNormalMap: true);
                pavedRoadPropertiesTexture ??= LoadRoadTexture(PavedRoadPropertiesFileName, isNormalMap: false);
                return (pavedRoadDiffuseTexture, pavedRoadNormalTexture, pavedRoadPropertiesTexture);
            default:
                dirtRoadDiffuseTexture ??= LoadRoadTexture(DirtRoadDiffuseFileName, isNormalMap: false);
                dirtRoadNormalTexture ??= LoadRoadTexture(DirtRoadNormalFileName, isNormalMap: true);
                dirtRoadPropertiesTexture ??= LoadRoadTexture(DirtRoadPropertiesFileName, isNormalMap: false);
                return (dirtRoadDiffuseTexture, dirtRoadNormalTexture, dirtRoadPropertiesTexture);
        }
    }

    private Texture? LoadRoadTexture(string fileName, bool isNormalMap)
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, "Resources", "Vic3", "Roads", fileName);
        if (!File.Exists(filePath))
        {
            Trace.WriteLine($"PathFeatureService: missing road texture '{filePath}'.");
            return null;
        }

        bool isDataTexture = isNormalMap || fileName.EndsWith("_properties.dds", StringComparison.OrdinalIgnoreCase);

        try
        {
            using var stream = File.OpenRead(filePath);
            return Texture.Load(
                graphicsDevice,
                stream,
                TextureFlags.ShaderResource,
                GraphicsResourceUsage.Immutable,
                loadAsSrgb: !isDataTexture);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"PathFeatureService: failed to load road texture '{filePath}': {ex}");
            return null;
        }
    }

    private SamplerState GetRoadSurfaceSampler()
    {
        if (roadSurfaceSampler != null)
            return roadSurfaceSampler;

        var description = new SamplerStateDescription(TextureFilter.Linear, TextureAddressMode.Wrap)
        {
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Wrap,
        };
        roadSurfaceSampler = SamplerState.New(graphicsDevice, description, "PathRoadSurfaceSampler");
        return roadSurfaceSampler;
    }

    private Texture GetFallbackPathDiffuseTexture()
    {
        if (fallbackPathDiffuseTexture != null)
            return fallbackPathDiffuseTexture;

        var data = new[]
        {
            new StrideColor(255, 255, 255, 255)
        };
        fallbackPathDiffuseTexture = Texture.New2D(
            graphicsDevice,
            1,
            1,
            PixelFormat.R8G8B8A8_UNorm_SRgb,
            data);
        return fallbackPathDiffuseTexture;
    }

    private Texture GetFallbackPathNormalTexture()
    {
        if (fallbackPathNormalTexture != null)
            return fallbackPathNormalTexture;

        var data = new[]
        {
            new StrideColor(128, 128, 255, 255)
        };
        fallbackPathNormalTexture = Texture.New2D(
            graphicsDevice,
            1,
            1,
            PixelFormat.R8G8B8A8_UNorm,
            data);
        return fallbackPathNormalTexture;
    }

    private Texture? fallbackPathPropertiesTexture;

    private Texture GetFallbackPathPropertiesTexture()
    {
        if (fallbackPathPropertiesTexture != null)
            return fallbackPathPropertiesTexture;

        // Properties: R=unused, G=0 (non-metallic), B=255 (no AO), A=128 (medium roughness)
        var data = new[]
        {
            new StrideColor(128, 0, 255, 128)
        };
        fallbackPathPropertiesTexture = Texture.New2D(
            graphicsDevice,
            1,
            1,
            PixelFormat.R8G8B8A8_UNorm,
            data);
        return fallbackPathPropertiesTexture;
    }

    private Material CreateGizmoMaterial(Color4 color)
    {
        var descriptor = new MaterialDescriptor();
        descriptor.Attributes.Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(color));
        descriptor.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();
        descriptor.Attributes.MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(0.35f));
        descriptor.Attributes.Specular = new MaterialMetalnessMapFeature(new ComputeFloat(0.0f));
        descriptor.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
        return Material.New(graphicsDevice, descriptor);
    }


    private IReadOnlyList<PathCurvePoint> BuildCurvePoints(PathFeature feature)
    {
        var controlPoints = new List<PathCurvePoint>();
        for (int i = 0; i < feature.NodeIds.Count; i++)
        {
            if (nodes.TryGetValue(feature.NodeIds[i], out PathNode? node))
                controlPoints.Add(new PathCurvePoint(node.Position, i));
        }

        return BuildCurvePoints(controlPoints, feature.Style.Width, feature.Style.CornerSpan);
    }

    public static IReadOnlyList<Vector3> SampleLegacyCurve(IReadOnlyList<Vector3> controlPoints, float width, float cornerSpan)
    {
        var indexedControlPoints = new List<PathCurvePoint>(controlPoints.Count);
        for (int i = 0; i < controlPoints.Count; i++)
            indexedControlPoints.Add(new PathCurvePoint(controlPoints[i], i));

        return BuildCurvePoints(indexedControlPoints, width, cornerSpan)
            .Select(static point => point.Position)
            .ToList();
    }

    private static IReadOnlyList<PathCurvePoint> BuildCurvePoints(IReadOnlyList<PathCurvePoint> controlPoints, float width, float cornerSpan)
    {
        if (controlPoints.Count <= 2)
            return controlPoints;

        var result = new List<PathCurvePoint>();
        float halfWidth = Math.Max(0.5f, width * 0.5f);
        for (int i = 0; i < controlPoints.Count - 1; i++)
        {
            Vector3 p0 = controlPoints[Math.Max(0, i - 1)].Position;
            Vector3 p1 = controlPoints[i].Position;
            Vector3 p2 = controlPoints[i + 1].Position;
            Vector3 p3 = controlPoints[Math.Min(controlPoints.Count - 1, i + 2)].Position;
            int insertIndex = i + 1;

            if (result.Count == 0)
                AppendCurvePoint(result, new PathCurvePoint(p1, insertIndex));

            SubdivideCurveSegment(
                result,
                p0,
                p1,
                p2,
                p3,
                0.0f,
                1.0f,
                p1,
                p2,
                insertIndex,
                0,
                halfWidth);
        }

        return RemoveBacktrackingCurvePoints(result, cornerSpan);
    }

    private IEnumerable<PathCurveSegment> EnumerateCurveSegments(PathFeature feature)
    {
        IReadOnlyList<PathCurvePoint> curvePoints = BuildCurvePoints(feature);
        for (int i = 0; i < curvePoints.Count - 1; i++)
        {
            yield return new PathCurveSegment(curvePoints[i], curvePoints[i + 1], curvePoints[i + 1].InsertIndex);
        }
    }

    private static IReadOnlyList<PathCurvePoint> RemoveBacktrackingCurvePoints(IReadOnlyList<PathCurvePoint> points, float cornerSpanFactor)
    {
        if (points.Count <= 2)
            return points;

        var result = new List<PathCurvePoint>(points.Count)
        {
            points[0]
        };
        float maxBacktrackDot = MathUtil.Lerp(-0.1f, -0.55f, Math.Clamp(cornerSpanFactor, 0.05f, 1.0f));
        for (int i = 1; i < points.Count - 1; i++)
        {
            PathCurvePoint previous = result[^1];
            PathCurvePoint current = points[i];
            PathCurvePoint next = points[i + 1];
            Vector3 incoming = NormalizeXZ(current.Position - previous.Position);
            Vector3 outgoing = NormalizeXZ(next.Position - current.Position);
            if (incoming.LengthSquared() < 0.0001f || outgoing.LengthSquared() < 0.0001f)
                continue;

            float dot = Vector3.Dot(incoming, outgoing);
            if (dot <= maxBacktrackDot
                && DistanceXZ(previous.Position, next.Position) <= CurveSampleSpacing)
            {
                continue;
            }

            result.Add(current);
        }

        result.Add(points[^1]);
        return result;
    }

    private static Vector3 ComputeCurveTangent(IReadOnlyList<PathCurvePoint> points, int index)
    {
        Vector3 previous = index > 0 ? points[index - 1].Position : points[index].Position;
        Vector3 next = index < points.Count - 1 ? points[index + 1].Position : points[index].Position;
        return NormalizeXZ(next - previous);
    }

    private static Vector3 EvaluateCentripetalCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t0 = 0.0f;
        float t1 = NextCatmullRomKnot(t0, p0, p1);
        float t2 = NextCatmullRomKnot(t1, p1, p2);
        float t3 = NextCatmullRomKnot(t2, p2, p3);
        float target = MathUtil.Lerp(t1, t2, t);

        Vector3 a1 = InterpolateCatmullRomPoint(p0, p1, t0, t1, target);
        Vector3 a2 = InterpolateCatmullRomPoint(p1, p2, t1, t2, target);
        Vector3 a3 = InterpolateCatmullRomPoint(p2, p3, t2, t3, target);
        Vector3 b1 = InterpolateCatmullRomPoint(a1, a2, t0, t2, target);
        Vector3 b2 = InterpolateCatmullRomPoint(a2, a3, t1, t3, target);
        return InterpolateCatmullRomPoint(b1, b2, t1, t2, target);
    }

    private static float NextCatmullRomKnot(float current, Vector3 a, Vector3 b)
    {
        return current + MathF.Sqrt(MathF.Max(DistanceXZ(a, b), 0.0001f));
    }

    private static Vector3 InterpolateCatmullRomPoint(Vector3 a, Vector3 b, float ta, float tb, float t)
    {
        float span = tb - ta;
        if (span <= 0.0001f)
            return b;

        float blend = (t - ta) / span;
        return a + (b - a) * Math.Clamp(blend, 0.0f, 1.0f);
    }

    private static float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static void SubdivideCurveSegment(
        List<PathCurvePoint> result,
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector3 p3,
        float startT,
        float endT,
        Vector3 startPoint,
        Vector3 endPoint,
        int insertIndex,
        int depth,
        float halfWidth)
    {
        if (depth >= MaxCurveSubdivisionDepth)
        {
            AppendCurvePoint(result, new PathCurvePoint(endPoint, insertIndex));
            return;
        }

        float chordLength = DistanceXZ(startPoint, endPoint);
        float midT = (startT + endT) * 0.5f;
        Vector3 midPoint = EvaluateCentripetalCatmullRom(p0, p1, p2, p3, midT);
        float deviation = DistancePointToSegmentXZ(midPoint, startPoint, endPoint, out _);
        Vector3 startTangent = EvaluateCurveTangent(p0, p1, p2, p3, startT);
        Vector3 endTangent = EvaluateCurveTangent(p0, p1, p2, p3, endT);
        float turnDegrees = MathF.Abs(SignedAngleXZ(startTangent, endTangent)) * (180.0f / MathF.PI);

        if (chordLength <= CurveSampleSpacing
            && deviation <= CurveSubdivisionTolerance
            && turnDegrees <= ComputeAdaptiveTurnThreshold(halfWidth))
        {
            AppendCurvePoint(result, new PathCurvePoint(endPoint, insertIndex));
            return;
        }

        SubdivideCurveSegment(result, p0, p1, p2, p3, startT, midT, startPoint, midPoint, insertIndex, depth + 1, halfWidth);
        SubdivideCurveSegment(result, p0, p1, p2, p3, midT, endT, midPoint, endPoint, insertIndex, depth + 1, halfWidth);
    }

    private static void AppendCurvePoint(List<PathCurvePoint> result, PathCurvePoint point)
    {
        if (result.Count == 0)
        {
            result.Add(point);
            return;
        }

        if (DistanceXZ(result[^1].Position, point.Position) <= MinCurvePointSpacing)
        {
            result[^1] = point;
            return;
        }

        result.Add(point);
    }

    private static IReadOnlyList<PathTerrainSample> BuildTerrainSamples(IReadOnlyList<PathRibbonRow> ribbonRows)
    {
        if (ribbonRows.Count == 0)
            return Array.Empty<PathTerrainSample>();

        var result = new List<PathTerrainSample>(ribbonRows.Count * 2);
        AppendTerrainSample(result, ribbonRows[0].Position, ComputeRibbonTangent(ribbonRows, 0));

        for (int i = 0; i < ribbonRows.Count - 1; i++)
        {
            PathRibbonRow startRow = ribbonRows[i];
            PathRibbonRow endRow = ribbonRows[i + 1];
            Vector3 start = startRow.Position;
            Vector3 end = endRow.Position;
            float segmentLength = DistanceXZ(start, end);
            if (segmentLength <= MinCurvePointSpacing)
                continue;

            Vector3 segmentTangent = NormalizeXZ(end - start);
            Vector3 startTangent = ComputeRibbonTangent(ribbonRows, i);
            Vector3 endTangent = ComputeRibbonTangent(ribbonRows, i + 1);
            if (startTangent.LengthSquared() < 0.0001f)
                startTangent = segmentTangent;
            if (endTangent.LengthSquared() < 0.0001f)
                endTangent = segmentTangent;

            int sampleCount = Math.Max(1, (int)MathF.Ceiling(segmentLength / TerrainBrushSpacing));
            for (int sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
            {
                float t = sampleIndex / (float)sampleCount;
                Vector3 position = Vector3.Lerp(start, end, t);
                Vector3 tangent = NormalizeXZ(Vector3.Lerp(startTangent, endTangent, t));
                if (tangent.LengthSquared() < 0.0001f)
                    tangent = segmentTangent;

                AppendTerrainSample(result, position, tangent);
            }
        }

        return result;
    }

    private static Vector3 SideToTangent(Vector3 side)
    {
        return NormalizeXZ(new Vector3(side.Z, 0.0f, -side.X));
    }

    private static float GetRoadTextureRepeatScale(PathFeatureStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        float textureAspect = style.RoadStyle == PathRoadStyle.Paved
            ? PavedRoadTextureAspect
            : DirtRoadTextureAspect;
        float width = Math.Max(1.0f, style.Width);
        return 1.0f / (textureAspect * width);
    }

    private static float[] CreateUniformHalfWidths(int count, float halfWidth)
    {
        var result = new float[count];
        Array.Fill(result, halfWidth);
        return result;
    }

    private static Vector3 ComputeRibbonTangent(IReadOnlyList<PathRibbonRow> rows, int index)
    {
        if (rows.Count <= 1)
            return Vector3.UnitZ;

        Vector3 tangent = Vector3.Zero;
        if (index > 0)
            tangent += NormalizeXZ(rows[index].Position - rows[index - 1].Position);
        if (index < rows.Count - 1)
            tangent += NormalizeXZ(rows[index + 1].Position - rows[index].Position);

        tangent = NormalizeXZ(tangent);
        if (tangent.LengthSquared() < 0.0001f)
            tangent = index < rows.Count - 1
                ? NormalizeXZ(rows[index + 1].Position - rows[index].Position)
                : NormalizeXZ(rows[index].Position - rows[index - 1].Position);

        return tangent.LengthSquared() < 0.0001f ? Vector3.UnitZ : tangent;
    }

    private static float Cross2D(Vector2 a, Vector2 b)
    {
        return a.X * b.Y - a.Y * b.X;
    }

    private Vector3 ComputeTerrainNormal(float worldX, float worldZ)
    {
        float step = 1.0f;
        float hL = terrainManager.GetHeightAtPosition(worldX - step, worldZ) ?? 0f;
        float hR = terrainManager.GetHeightAtPosition(worldX + step, worldZ) ?? 0f;
        float hD = terrainManager.GetHeightAtPosition(worldX, worldZ - step) ?? 0f;
        float hU = terrainManager.GetHeightAtPosition(worldX, worldZ + step) ?? 0f;
        return Vector3.Normalize(new Vector3(hL - hR, 2.0f * step, hD - hU));
    }

    private static void AppendTerrainSample(List<PathTerrainSample> result, Vector3 position, Vector3 tangent)
    {
        Vector3 normalizedTangent = NormalizeXZ(tangent);
        if (normalizedTangent.LengthSquared() < 0.0001f)
            normalizedTangent = Vector3.UnitZ;

        var sample = new PathTerrainSample(position, PerpendicularXZ(normalizedTangent));
        if (result.Count == 0)
        {
            result.Add(sample);
            return;
        }

        PathTerrainSample previous = result[^1];
        if (DistanceXZ(previous.Position, sample.Position) <= MinCurvePointSpacing
            && Vector3.Dot(previous.Side, sample.Side) >= 0.9995f)
        {
            result[^1] = sample;
            return;
        }

        result.Add(sample);
    }

    private static IReadOnlyList<PathRibbonRow> BuildRibbonRows(IReadOnlyList<PathCurvePoint> curvePoints, float cornerSpanFactor, float halfWidth)
    {
        var result = new List<PathRibbonRow>(curvePoints.Count * 2);
        float distance = 0.0f;
        for (int i = 0; i < curvePoints.Count; i++)
        {
            Vector3 position = curvePoints[i].Position;
            if (i > 0)
                distance += DistanceXZ(curvePoints[i - 1].Position, position);

            Vector3 tangent = ComputeCurveTangent(curvePoints, i);
            if (tangent.LengthSquared() < 0.0001f)
                tangent = i > 0 ? NormalizeXZ(position - curvePoints[i - 1].Position) : Vector3.UnitZ;

            Vector3 side = PerpendicularXZ(tangent);
            if (result.Count > 0 && Vector3.Dot(result[^1].Side, side) < 0.0f)
                side = -side;

            if (i > 0 && i < curvePoints.Count - 1)
            {
                Vector3 previousPosition = curvePoints[i - 1].Position;
                Vector3 nextPosition = curvePoints[i + 1].Position;
                Vector3 previousTangent = NormalizeXZ(position - previousPosition);
                Vector3 nextTangent = NormalizeXZ(nextPosition - position);
                if (previousTangent.LengthSquared() >= 0.0001f && nextTangent.LengthSquared() >= 0.0001f)
                {
                    AppendJoinedRibbonRows(result, previousPosition, position, nextPosition, previousTangent, nextTangent, distance, cornerSpanFactor, halfWidth);
                    continue;
                }
            }

            AppendRibbonRow(result, new PathRibbonRow(position, side, distance));
        }

        return result;
    }

    private static void AppendRibbonRow(List<PathRibbonRow> result, PathRibbonRow row)
    {
        if (result.Count == 0)
        {
            result.Add(row);
            return;
        }

        PathRibbonRow previous = result[^1];
        if (DistanceXZ(previous.Position, row.Position) <= MinCurvePointSpacing
            && Vector3.Dot(previous.Side, row.Side) >= 0.9995f)
        {
            result[^1] = row;
            return;
        }

        result.Add(row);
    }

    private static PathRibbonRow LerpRibbonRow(PathRibbonRow start, PathRibbonRow end, float t)
    {
        Vector3 position = Vector3.Lerp(start.Position, end.Position, t);
        Vector3 endSide = Vector3.Dot(start.Side, end.Side) < 0.0f ? -end.Side : end.Side;
        Vector3 side = NormalizeSide(Vector3.Lerp(start.Side, endSide, t));
        float distance = MathUtil.Lerp(start.Distance, end.Distance, t);
        return new PathRibbonRow(position, side, distance);
    }

    private static Vector3 EvaluateCurveTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float start = Math.Max(0.0f, t - CurveTangentSampleStep);
        float end = Math.Min(1.0f, t + CurveTangentSampleStep);
        if (end - start <= 0.0001f)
            return NormalizeSide(p2 - p1);

        Vector3 startPoint = EvaluateCentripetalCatmullRom(p0, p1, p2, p3, start);
        Vector3 endPoint = EvaluateCentripetalCatmullRom(p0, p1, p2, p3, end);
        Vector3 tangent = NormalizeXZ(endPoint - startPoint);
        return tangent.LengthSquared() < 0.0001f ? NormalizeSide(p2 - p1) : tangent;
    }

    private static float SignedAngleXZ(Vector3 from, Vector3 to)
    {
        from = NormalizeXZ(from);
        to = NormalizeXZ(to);
        if (from.LengthSquared() < 0.0001f || to.LengthSquared() < 0.0001f)
            return 0.0f;

        float dot = Math.Clamp(Vector3.Dot(from, to), -1.0f, 1.0f);
        float cross = from.X * to.Z - from.Z * to.X;
        return MathF.Atan2(cross, dot);
    }

    private static float ComputeAdaptiveTurnThreshold(float halfWidth)
    {
        float safeWidth = Math.Max(halfWidth, 0.25f);
        float maxEdgeDeviation = MaxEdgeDeviation;
        float thresholdRadians = MathF.Atan2(maxEdgeDeviation, safeWidth);
        return thresholdRadians * (180.0f / MathF.PI);
    }

    private static void AppendJoinedRibbonRows(
        List<PathRibbonRow> result,
        Vector3 previousPosition,
        Vector3 position,
        Vector3 nextPosition,
        Vector3 previousTangent,
        Vector3 nextTangent,
        float distance,
        float cornerSpanFactor,
        float halfWidth)
    {
        previousTangent = NormalizeXZ(previousTangent);
        nextTangent = NormalizeXZ(nextTangent);
        if (previousTangent.LengthSquared() < 0.0001f || nextTangent.LengthSquared() < 0.0001f)
        {
            Vector3 fallbackTangent = previousTangent.LengthSquared() >= 0.0001f ? previousTangent : nextTangent;
            AppendRibbonRow(result, new PathRibbonRow(position, PerpendicularXZ(fallbackTangent), distance));
            return;
        }

        Vector3 startSide = PerpendicularXZ(previousTangent);
        bool flipStoredSide = result.Count > 0 && Vector3.Dot(result[^1].Side, startSide) < 0.0f;
        if (flipStoredSide)
            startSide = -startSide;

        Vector3 endSide = PerpendicularXZ(nextTangent);
        if (flipStoredSide)
            endSide = -endSide;

        float tangentTurn = SignedAngleXZ(previousTangent, nextTangent);
        float absTurn = MathF.Abs(tangentTurn);
        if (absTurn < 0.0001f)
        {
            AppendRibbonRow(result, new PathRibbonRow(position, startSide, distance));
            return;
        }

        float previousLength = DistanceXZ(previousPosition, position);
        float nextLength = DistanceXZ(position, nextPosition);
        float minSegmentLength = Math.Min(previousLength, nextLength);
        float trimFromSpan = minSegmentLength * Math.Clamp(cornerSpanFactor, 0.05f, 1.0f) * 0.5f;
        float halfTurn = absTurn * 0.5f;
        float tanHalfTurn = MathF.Tan(halfTurn);
        Vector3 bevelSide = NormalizeSide(startSide + endSide);
        float safeHalfWidth = Math.Max(halfWidth, 0.5f);
        float fallbackTrim = Math.Max(MinCurvePointSpacing * 2.0f, Math.Min(minSegmentLength * 0.2f, safeHalfWidth * 0.5f));
        if (tanHalfTurn <= 0.0001f)
        {
            AppendFallbackBevelRows(result, position, previousTangent, nextTangent, startSide, endSide, bevelSide, distance, fallbackTrim);
            return;
        }

        float minRadius = safeHalfWidth + MinCurvePointSpacing;
        float minTrim = minRadius * tanHalfTurn;
        float maxTrim = minSegmentLength * 0.5f - MinCurvePointSpacing;
        if (absTurn >= MathF.PI * 0.9f || maxTrim <= MinCurvePointSpacing || minTrim > maxTrim)
        {
            AppendFallbackBevelRows(result, position, previousTangent, nextTangent, startSide, endSide, bevelSide, distance, fallbackTrim);
            return;
        }

        float trimDistance = Math.Clamp(Math.Max(trimFromSpan, minTrim), MinCurvePointSpacing, maxTrim);
        float arcRadius = trimDistance / tanHalfTurn;
        Vector3 startCenter = position - previousTangent * trimDistance;
        Vector3 endCenter = position + nextTangent * trimDistance;
        Vector3 rawStartSide = flipStoredSide ? -PerpendicularXZ(previousTangent) : PerpendicularXZ(previousTangent);
        Vector3 rawEndSide = flipStoredSide ? -PerpendicularXZ(nextTangent) : PerpendicularXZ(nextTangent);
        Vector3 insideStartNormal = tangentTurn >= 0.0f ? rawStartSide : -rawStartSide;
        Vector3 insideEndNormal = tangentTurn >= 0.0f ? rawEndSide : -rawEndSide;
        Vector3 arcCenter = startCenter + insideStartNormal * arcRadius;
        Vector3 arcCenterFromEnd = endCenter + insideEndNormal * arcRadius;
        arcCenter = new Vector3(
            (arcCenter.X + arcCenterFromEnd.X) * 0.5f,
            position.Y,
            (arcCenter.Z + arcCenterFromEnd.Z) * 0.5f);

        Vector3 startRadiusDirection = NormalizeXZ(startCenter - arcCenter);
        Vector3 endRadiusDirection = NormalizeXZ(endCenter - arcCenter);
        float arcTurn = SignedAngleXZ(startRadiusDirection, endRadiusDirection);
        if (MathF.Abs(arcTurn) < 0.0001f)
        {
            AppendFallbackBevelRows(result, position, previousTangent, nextTangent, startSide, endSide, bevelSide, distance, fallbackTrim);
            return;
        }

        int joinSegments = Math.Max(1, (int)MathF.Ceiling(absTurn / (MathF.PI / 10.0f)));
        float baseDistance = Math.Max(0.0f, distance - trimDistance);
        float arcLength = arcRadius * MathF.Abs(arcTurn);
        for (int segmentIndex = 0; segmentIndex <= joinSegments; segmentIndex++)
        {
            float t = segmentIndex / (float)joinSegments;
            float angle = arcTurn * t;
            Vector3 radiusDirection = RotateAroundY(startRadiusDirection, angle);
            Vector3 arcPosition = arcCenter + radiusDirection * arcRadius;
            arcPosition.Y = MathUtil.Lerp(startCenter.Y, endCenter.Y, t);

            Vector3 tangent = arcTurn >= 0.0f
                ? PerpendicularXZ(radiusDirection)
                : -PerpendicularXZ(radiusDirection);
            Vector3 side = PerpendicularXZ(tangent);
            if (flipStoredSide)
                side = -side;

            float rowDistance = baseDistance + arcLength * t;
            AppendRibbonRow(result, new PathRibbonRow(arcPosition, side, rowDistance));
        }
    }

    private static void AppendFallbackBevelRows(
        List<PathRibbonRow> result,
        Vector3 position,
        Vector3 previousTangent,
        Vector3 nextTangent,
        Vector3 startSide,
        Vector3 endSide,
        Vector3 bevelSide,
        float distance,
        float trimDistance)
    {
        trimDistance = Math.Max(trimDistance, MinCurvePointSpacing * 2.0f);
        Vector3 startPosition = position - previousTangent * trimDistance;
        Vector3 endPosition = position + nextTangent * trimDistance;
        Vector3 centerPosition = Vector3.Lerp(startPosition, endPosition, 0.5f);
        centerPosition.Y = position.Y;
        float startDistance = Math.Max(0.0f, distance - trimDistance);
        float endDistance = distance + trimDistance;
        float centerDistance = (startDistance + endDistance) * 0.5f;

        AppendRibbonRow(result, new PathRibbonRow(startPosition, startSide, startDistance));
        AppendRibbonRow(result, new PathRibbonRow(centerPosition, bevelSide, centerDistance));
        AppendRibbonRow(result, new PathRibbonRow(endPosition, endSide, endDistance));
    }

    private static Vector3 RotateAroundY(Vector3 vector, float angle)
    {
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        return NormalizeSide(new Vector3(
            vector.X * cos - vector.Z * sin,
            0.0f,
            vector.X * sin + vector.Z * cos));
    }

    private static Vector3 NormalizeXZ(Vector3 vector)
    {
        vector.Y = 0.0f;
        if (vector.LengthSquared() < 0.0001f)
            return Vector3.Zero;

        vector.Normalize();
        return vector;
    }

    private static Vector3 NormalizeSide(Vector3 vector)
    {
        Vector3 normalized = NormalizeXZ(vector);
        return normalized.LengthSquared() < 0.0001f ? Vector3.UnitX : normalized;
    }

    private static Vector3 PerpendicularXZ(Vector3 tangent)
    {
        Vector3 side = new(-tangent.Z, 0.0f, tangent.X);
        if (side.LengthSquared() < 0.0001f)
            return Vector3.UnitX;

        side.Normalize();
        return side;
    }

    private static float DistancePointToSegmentXZ(Vector3 point, Vector3 a, Vector3 b, out float t)
    {
        float abX = b.X - a.X;
        float abZ = b.Z - a.Z;
        float lengthSq = abX * abX + abZ * abZ;
        if (lengthSq < 0.0001f)
        {
            t = 0.0f;
            return DistanceXZ(point, a);
        }

        float apX = point.X - a.X;
        float apZ = point.Z - a.Z;
        t = Math.Clamp((apX * abX + apZ * abZ) / lengthSq, 0.0f, 1.0f);
        float closestX = a.X + abX * t;
        float closestZ = a.Z + abZ * t;
        float dx = point.X - closestX;
        float dz = point.Z - closestZ;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static long ComposeChunkKey(int chunkX, int chunkZ)
    {
        return ((long)chunkZ << 32) | (uint)chunkX;
    }

    private static bool AreStylesEqual(PathFeatureStyle a, PathFeatureStyle b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return Math.Abs(a.Width - b.Width) < 0.001f
            && Math.Abs(a.Depth - b.Depth) < 0.001f
            && Math.Abs(a.SideSlope - b.SideSlope) < 0.001f
            && Math.Abs(a.CornerSpan - b.CornerSpan) < 0.001f
            && a.RoadStyle == b.RoadStyle;
    }

    private void SyncParametersFromSelection()
    {
        if (selectedFeatureId is not { } featureId)
            return;

        PathFeature? feature = FindFeature(featureId);
        if (feature == null)
            return;

        isSyncingParameters = true;
        pathParameters.LoadFromFeature(feature.Kind, feature.Style);
        isSyncingParameters = false;
    }

    private void NotifyNetworkChanged()
    {
        NetworkChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifySelectionChanged()
    {
        SelectionChanged?.Invoke(this, new PathFeatureSelectionChangedEventArgs
        {
            SelectedFeatureId = selectedFeatureId,
            SelectedNodeId = selectedNodeId,
        });
    }

    private sealed class PathEditOperation
    {
        public PathEditOperation(
            PathFeatureService service,
            TerrainManager terrainManager,
            PathNetworkSnapshot beforeNetwork,
            IReadOnlyList<HeightChunkDelta> initialHeightChunks,
            string description)
        {
            Command = new PathFeatureEditCommand(service, terrainManager, beforeNetwork, initialHeightChunks, description);
        }

        public PathFeatureEditCommand Command { get; }

        public bool SketchMode => Command.Description.StartsWith("Sketch ", StringComparison.Ordinal);

        public Guid? DraggedNodeId { get; set; }

        public Vector3? LastSketchPoint { get; set; }

        public List<Guid> SketchNodeIds { get; } = new();

        public List<Vector3> SketchPositions { get; } = new();
    }

    private readonly record struct PathCurvePoint(Vector3 Position, int InsertIndex);

    private readonly record struct PathCurveSegment(PathCurvePoint A, PathCurvePoint B, int InsertIndex);

    private readonly record struct PathTerrainSample(Vector3 Position, Vector3 Side);

    private readonly record struct PathRibbonRow(Vector3 Position, Vector3 Side, float Distance);

    private sealed class PathFeatureMeshHandle
    {
        private readonly Entity entity;
        private readonly Buffer vertexBuffer;
        private readonly Buffer indexBuffer;

        public PathFeatureMeshHandle(Entity entity, Buffer vertexBuffer, Buffer indexBuffer)
        {
            this.entity = entity;
            this.vertexBuffer = vertexBuffer;
            this.indexBuffer = indexBuffer;
        }

        public void Dispose(Scene scene)
        {
            scene.Entities.Remove(entity);
            vertexBuffer.Dispose();
            indexBuffer.Dispose();
        }
    }

    private sealed class PathNodeGizmoHandle
    {
        private readonly Entity entity;
        private readonly ModelComponent modelComponent;
        private readonly GeometricPrimitive primitive;

        public PathNodeGizmoHandle(
            Entity entity,
            ModelComponent modelComponent,
            GeometricPrimitive primitive)
        {
            this.entity = entity;
            this.modelComponent = modelComponent;
            this.primitive = primitive;
        }

        public void Update(Vector3 position, bool selected, bool connected, bool visible, Material normalMaterial, Material connectedMaterial, Material selectedMaterial)
        {
            entity.Transform.Position = position;
            entity.Transform.Scale = new Vector3(selected ? 2.0f : 1.45f);
            entity.Transform.UpdateWorldMatrix();
            modelComponent.Enabled = visible;
            if (modelComponent.Model == null)
                return;

            Material material = selected ? selectedMaterial : connected ? connectedMaterial : normalMaterial;
            if (modelComponent.Model.Materials.Count == 0)
                modelComponent.Model.Materials.Add(material);
            else
                modelComponent.Model.Materials[0] = material;
        }

        public void Dispose(Scene scene)
        {
            scene.Entities.Remove(entity);
            primitive.Dispose();
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct PathMeshVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Tangent;
    public Vector2 TexCoord;
    public Vector2 TexCoord1;

    public static readonly VertexDeclaration Layout = new(
        VertexElement.Position<Vector3>(),
        VertexElement.Normal<Vector3>(),
        VertexElement.Tangent<Vector4>(),
        VertexElement.TextureCoordinate<Vector2>(),
        VertexElement.TextureCoordinate<Vector2>(1));
}
