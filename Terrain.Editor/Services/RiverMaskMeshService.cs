#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Terrain.Editor;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering.Materials;
using Terrain.Editor.Services.PathFeatures;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Editor.Services;

public sealed class RiverMaskMeshService : IDisposable
{
    private const float MaskCellSize = 2.0f;
    private const float MaskCellHalfSize = MaskCellSize * 0.5f;
    private const float SurfaceOffset = 0.02f;
    private const float NormalSampleStep = 1.0f;
    private const float RiverTextureRepeatUScale = 1.0f / 35.0f;
    private const float RiverEdgeFadeStart = 0.68f;
    private const float RiverFlowTiling = 0.22f;
    private const float RiverFlowStrength = 0.18f;
    private const float RiverHighlightStrength = 0.22f;
    private const float ConnectionTaperDistance = 6.0f;
    private const float MinCurvePointSpacing = 0.05f;
    private const float MinHalfWidth = 1.0f;
    private const float MinVisibleHalfWidth = 0.05f;
    private const float CurveSampleSpacing = 4.0f;
    private const float CurveSubdivisionTolerance = 0.35f;
    private const float CenterlineControlSpacing = 6.0f;
    private const float CenterlineControlTurnThresholdDegrees = 18.0f;
    private const float RiverCornerSpanFactor = 0.75f;
    private const float RiverJoinTurnThresholdDegrees = 12.0f;
    private const float NearUturnDotThreshold = -0.95f;
    private const float MaxEdgeDeviation = 0.12f;
    private const float CurveTangentSampleStep = 0.02f;
    private const float MeshFollowTerrainSpacing = 1.0f;
    private const float MeshFollowTerrainHeightTolerance = 0.08f;
    private const int SkeletonNeighborDirections = 8;
    private const int CenterlineSmoothingIterations = 2;
    private const int RibbonSmoothingIterations = 3;
    private const int MaxCurveSubdivisionDepth = 8;
    private const int MaxMeshFollowTerrainSubdivisionDepth = 6;
    private const int MaxWidthSampleSteps = 64;

    private static readonly (int X, int Y)[] NeighborOffsets =
    [
        (0, -1),
        (1, -1),
        (1, 0),
        (1, 1),
        (0, 1),
        (-1, 1),
        (-1, 0),
        (-1, -1),
    ];

    private readonly GraphicsDevice graphicsDevice;
    private readonly Scene scene;
    private readonly TerrainManager terrainManager;

    private Entity? riverMeshEntity;
    private Buffer? vertexBuffer;
    private Buffer? indexBuffer;
    private Material? riverMeshMaterial;
    private RiverMeshTopology? cachedTopology;

    public RiverMaskMeshService(GraphicsDevice graphicsDevice, Scene scene, TerrainManager terrainManager)
    {
        this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
        this.terrainManager = terrainManager ?? throw new ArgumentNullException(nameof(terrainManager));

        this.terrainManager.RiverMaskChanged += OnRiverMaskChanged;
        this.terrainManager.TerrainLoaded += OnTerrainLoaded;
        this.terrainManager.TerrainSurfaceChanged += OnTerrainSurfaceChanged;
        Rebuild();
    }

    public void Rebuild()
    {
        cachedTopology = BuildTopology();
        RebuildSurfaceMeshFromTopology();
    }

    public void Dispose()
    {
        terrainManager.RiverMaskChanged -= OnRiverMaskChanged;
        terrainManager.TerrainLoaded -= OnTerrainLoaded;
        terrainManager.TerrainSurfaceChanged -= OnTerrainSurfaceChanged;
        RemoveRiverMesh();
    }

    private void OnRiverMaskChanged(object? sender, EventArgs e)
    {
        Rebuild();
    }

    private void OnTerrainLoaded(object? sender, TerrainLoadedEventArgs e)
    {
        Rebuild();
    }

    private void OnTerrainSurfaceChanged(object? sender, EventArgs e)
    {
        RebuildSurfaceMeshFromTopology();
    }

    private RiverMeshTopology? BuildTopology()
    {
        RiverMap? riverMask = terrainManager.RiverMap;
        if (riverMask == null || !terrainManager.HasHeightCache)
            return null;

        byte[] rawData = riverMask.GetRawData();
        int mapWidth = riverMask.Width;
        int mapHeight = riverMask.Height;

        // 优先：从 RiverMap 像素直接追踪蓝色路径生成段
        List<RiverCenterSegment>? segments = TryBuildFromPixelTrace(riverMask, rawData);

        // fallback: 旧格式/退化 → 骨架提取
        segments ??= ExtractSegments(BuildSkeleton(rawData, mapWidth, mapHeight), mapWidth, mapHeight);
        if (segments == null || segments.Count == 0)
            return null;

        MeasureSegments(segments, rawData, mapWidth, mapHeight);
        if (segments.Count == 0)
            return null;

        return new RiverMeshTopology(mapWidth, mapHeight, rawData, segments);
    }

    private void RebuildSurfaceMeshFromTopology()
    {
        RiverMeshTopology? topology = cachedTopology;
        if (!terrainManager.HasHeightCache)
        {
            cachedTopology = null;
            RemoveRiverMesh();
            return;
        }

        if (topology == null)
        {
            RemoveRiverMesh();
            return;
        }

        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<int>();
        foreach (RiverCenterSegment segment in topology.Segments)
        {
            AppendSegmentMesh(vertices, indices, topology.RawData, topology.Width, topology.Height, segment);
        }

        if (vertices.Count == 0 || indices.Count == 0)
        {
            RemoveRiverMesh();
            return;
        }

        ReplaceRiverMesh(vertices.ToArray(), indices.ToArray());
    }

    private void MeasureSegments(List<RiverCenterSegment> segments, byte[] rawData, int width, int height)
    {
        foreach (RiverCenterSegment segment in segments)
        {
            List<Vector3> centerline = BuildCenterline(rawData, width, height, segment.Cells, segment.IsLoop, segment.StartAnchor, segment.EndAnchor);
            if (centerline.Count < 2)
                continue;

            segment.Centerline = centerline;
            segment.WorldLength = ComputePolylineLength(centerline, segment.IsLoop);
            segment.AverageHalfWidth = EstimateAverageHalfWidth(rawData, width, height, centerline, segment.IsLoop);
        }

        segments.RemoveAll(static segment => segment.Centerline == null || segment.Centerline.Count < 2 || segment.WorldLength <= MaskCellSize * 0.5f);
    }

    private void ClassifyJunctionConnections(List<RiverCenterSegment> segments, Dictionary<int, RiverPixelType> junctionPixelTypes)
    {
        var attachmentsByJunction = new Dictionary<int, List<RiverJunctionAttachment>>();
        for (int i = 0; i < segments.Count; i++)
        {
            RiverCenterSegment segment = segments[i];
            if (segment.IsLoop)
                continue;

            if (segment.StartNodeKey >= 0)
                AddJunctionAttachment(attachmentsByJunction, segment.StartNodeKey, new RiverJunctionAttachment(i, true, segment.AverageHalfWidth, segment.WorldLength));
            if (segment.EndNodeKey >= 0)
                AddJunctionAttachment(attachmentsByJunction, segment.EndNodeKey, new RiverJunctionAttachment(i, false, segment.AverageHalfWidth, segment.WorldLength));
        }

        int paddedWidth = (terrainManager.RiverMap?.Width ?? 0) + 2;

        foreach ((int junctionKey, List<RiverJunctionAttachment> attachments) in attachmentsByJunction)
        {
            if (attachments.Count <= 2)
                continue;

            // 读取骨架节点对应的 RiverMap 像素类型
            RiverPixelType pixelType = RiverPixelType.Land;
            if (junctionPixelTypes.TryGetValue(junctionKey, out RiverPixelType mappedType))
                pixelType = mappedType;

            switch (pixelType)
            {
                case RiverPixelType.Confluence:
                    // 红色 = 汇合点: 支流 taper
                    // 按宽度排序，最窄的 taper（支流通常更窄）
                    attachments.Sort(static (a, b) =>
                    {
                        int widthCompare = b.AverageHalfWidth.CompareTo(a.AverageHalfWidth);
                        return widthCompare != 0 ? widthCompare : b.WorldLength.CompareTo(a.WorldLength);
                    });
                    // 最窄的一条 taper
                    TaperAttachments(segments, attachments, keepCount: 2);
                    break;

                case RiverPixelType.Bifurcation:
                    // 黄色 = 分叉点: 三条全保持（主河入+两侧分支出）
                    // 不 taper 任何段
                    break;

                case RiverPixelType.Source:
                    // 绿色 = 源头: 唯一连接的段 taperStart=false（从源渐宽）
                    if (attachments.Count == 1)
                    {
                        RiverCenterSegment seg = segments[attachments[0].SegmentIndex];
                        if (attachments[0].AtStart)
                            seg.TaperStart = false;
                        else
                            seg.TaperEnd = false;
                    }
                    break;

                default:
                    // 蓝色 River 或骨架节点: 退回到宽度/长度排序启发式
                    attachments.Sort(static (a, b) =>
                    {
                        int widthCompare = b.AverageHalfWidth.CompareTo(a.AverageHalfWidth);
                        return widthCompare != 0 ? widthCompare : b.WorldLength.CompareTo(a.WorldLength);
                    });

                    for (int i = 2; i < attachments.Count; i++)
                    {
                        RiverJunctionAttachment attachment = attachments[i];
                        RiverCenterSegment segment = segments[attachment.SegmentIndex];
                        if (attachment.AtStart)
                            segment.TaperStart = true;
                        else
                            segment.TaperEnd = true;
                    }
                    break;
            }
        }
    }

    private static void TaperAttachments(List<RiverCenterSegment> segments, List<RiverJunctionAttachment> attachments, int keepCount)
    {
        for (int i = keepCount; i < attachments.Count; i++)
        {
            RiverJunctionAttachment attachment = attachments[i];
            RiverCenterSegment segment = segments[attachment.SegmentIndex];
            if (attachment.AtStart)
                segment.TaperStart = true;
            else
                segment.TaperEnd = true;
        }
    }

    private static void AddJunctionAttachment(
        Dictionary<int, List<RiverJunctionAttachment>> attachmentsByJunction,
        int junctionKey,
        RiverJunctionAttachment attachment)
    {
        if (!attachmentsByJunction.TryGetValue(junctionKey, out List<RiverJunctionAttachment>? attachments))
        {
            attachments = [];
            attachmentsByJunction.Add(junctionKey, attachments);
        }

        attachments.Add(attachment);
    }

    private void AppendSegmentMesh(
        List<VertexPositionNormalTexture> vertices,
        List<int> indices,
        byte[] rawData,
        int width,
        int height,
        RiverCenterSegment segment)
    {
        if (segment.Centerline == null || segment.Centerline.Count < 2)
            return;

        List<RiverRibbonRow> rows = BuildRibbonRows(rawData, width, height, segment);
        if (rows.Count < 2)
            return;

        rows = RefineRibbonRowsForTerrainFit(rows, segment.IsLoop);
        if (rows.Count < 2)
            return;

        rows = SmoothRibbonRows(rows, segment.IsLoop);
        if (rows.Count < 2)
            return;

        rows = RecomputeRibbonDistances(rows, segment.IsLoop);
        if (rows.Count < 2)
            return;

        rows = StabilizeRibbonSideOrder(rows, segment.IsLoop);
        if (rows.Count < 2)
            return;

        float ribbonLength = rows[^1].Distance;
        int vertexStart = vertices.Count;
        for (int i = 0; i < rows.Count; i++)
        {
            RiverRibbonRow row = rows[i];
            float u = ComputeRibbonTextureU(row.Distance, ribbonLength, segment.IsLoop);
            Vector3 leftPosition = SampleSurfacePosition(row.Position.X - row.Side.X * row.HalfWidth, row.Position.Z - row.Side.Z * row.HalfWidth);
            Vector3 rightPosition = SampleSurfacePosition(row.Position.X + row.Side.X * row.HalfWidth, row.Position.Z + row.Side.Z * row.HalfWidth);
            Vector3 leftNormal = SampleSurfaceNormal(leftPosition.X, leftPosition.Z);
            Vector3 rightNormal = SampleSurfaceNormal(rightPosition.X, rightPosition.Z);
            vertices.Add(new VertexPositionNormalTexture(leftPosition, leftNormal, new Vector2(u, 0.0f)));
            vertices.Add(new VertexPositionNormalTexture(rightPosition, rightNormal, new Vector2(u, 1.0f)));
        }

        int stripSegmentCount = rows.Count - 1;
        for (int i = 0; i < stripSegmentCount; i++)
        {
            int nextRow = i + 1;
            int a = vertexStart + i * 2;
            int b = a + 1;
            int c = vertexStart + nextRow * 2;
            int d = c + 1;
            indices.Add(a);
            indices.Add(c);
            indices.Add(b);
            indices.Add(b);
            indices.Add(c);
            indices.Add(d);
        }
    }

    private List<RiverRibbonRow> BuildRibbonRows(byte[] rawData, int width, int height, RiverCenterSegment segment)
    {
        List<Vector3> centerline = segment.Centerline!;
        int sourceCount = centerline.Count;
        bool hasLoopClosurePoint = segment.IsLoop && sourceCount >= 2 && DistanceXZ(centerline[0], centerline[^1]) <= MinCurvePointSpacing;
        int rowCount = hasLoopClosurePoint ? sourceCount - 1 : sourceCount;
        if (rowCount <= 1)
            return [];

        float totalDistance = segment.WorldLength;
        float baseHalfWidth = Math.Max(MinHalfWidth, segment.AverageHalfWidth);
        var positions = new Vector3[rowCount];
        var sides = new Vector3[rowCount];
        var halfWidths = new float[rowCount];
        var distances = new float[rowCount];
        float distance = 0.0f;
        for (int i = 0; i < rowCount; i++)
        {
            Vector3 point = centerline[i];
            if (i > 0)
                distance += DistanceXZ(centerline[i - 1], point);

            Vector3 tangent = ComputePolylineTangent(centerline, i, segment.IsLoop, rowCount);
            Vector3 side = PerpendicularXZ(tangent);
            float taperScale = ComputeTaperScale(distance, totalDistance, segment.TaperStart, segment.TaperEnd);
            positions[i] = SampleSurfacePosition(point.X, point.Z);
            sides[i] = side;
            halfWidths[i] = Math.Max(MinVisibleHalfWidth, baseHalfWidth * taperScale);
            distances[i] = distance;
        }

        var result = new List<RiverRibbonRow>(rowCount * 2 + (segment.IsLoop ? 1 : 0));
        for (int i = 0; i < rowCount; i++)
        {
            bool canJoin = segment.IsLoop || (i > 0 && i < rowCount - 1);
            if (canJoin)
            {
                int previousIndex = i > 0 ? i - 1 : rowCount - 1;
                int nextIndex = i < rowCount - 1 ? i + 1 : 0;
                Vector3 previousPosition = positions[previousIndex];
                Vector3 position = positions[i];
                Vector3 nextPosition = positions[nextIndex];
                Vector3 previousTangent = NormalizeXZ(position - previousPosition);
                Vector3 nextTangent = NormalizeXZ(nextPosition - position);
                if (previousTangent.LengthSquared() >= 0.0001f && nextTangent.LengthSquared() >= 0.0001f)
                {
                    float turnDegrees = MathF.Abs(SignedAngleXZ(previousTangent, nextTangent)) * (180.0f / MathF.PI);
                    if (turnDegrees >= RiverJoinTurnThresholdDegrees)
                    {
                        AppendJoinedRibbonRows(result, previousPosition, position, nextPosition, previousTangent, nextTangent, distances[i], RiverCornerSpanFactor, halfWidths[i]);
                        continue;
                    }
                }
            }

            AppendRibbonRow(result, new RiverRibbonRow(positions[i], sides[i], distances[i], halfWidths[i]));
        }

        if (segment.IsLoop && result.Count > 1)
        {
            RiverRibbonRow first = result[0];
            if (DistanceXZ(first.Position, result[^1].Position) <= MinCurvePointSpacing)
                result[^1] = first;
            else
                result.Add(first);
        }

        return RecomputeRibbonDistances(result, segment.IsLoop);
    }

    private static float ComputeTaperScale(float distance, float totalDistance, bool taperStart, bool taperEnd)
    {
        float scale = 1.0f;
        float taperDistance = Math.Max(MaskCellSize, Math.Min(ConnectionTaperDistance, totalDistance * 0.5f));
        if (taperStart)
            scale = Math.Min(scale, SmoothStep01(Math.Clamp(distance / taperDistance, 0.0f, 1.0f)));
        if (taperEnd)
            scale = Math.Min(scale, SmoothStep01(Math.Clamp((totalDistance - distance) / taperDistance, 0.0f, 1.0f)));
        return scale;
    }

    private static float SmoothStep01(float t)
    {
        return t * t * (3.0f - 2.0f * t);
    }

    private static void AppendRibbonRow(List<RiverRibbonRow> result, RiverRibbonRow row)
    {
        if (result.Count == 0)
        {
            result.Add(row);
            return;
        }

        RiverRibbonRow previous = result[^1];
        if (DistanceXZ(previous.Position, row.Position) <= MinCurvePointSpacing && Vector3.Dot(previous.Side, row.Side) >= 0.9995f)
        {
            result[^1] = row;
            return;
        }

        result.Add(row);
    }

    private List<RiverRibbonRow> RefineRibbonRowsForTerrainFit(List<RiverRibbonRow> ribbonRows, bool isLoop)
    {
        if (ribbonRows.Count <= 1)
            return ribbonRows;

        var result = new List<RiverRibbonRow>(ribbonRows.Count * 2)
        {
            ribbonRows[0]
        };

        for (int i = 0; i < ribbonRows.Count - 1; i++)
            SubdivideRibbonRowForTerrainFit(result, ribbonRows[i], ribbonRows[i + 1], 0);

        if (isLoop && result.Count > 1)
            result[^1] = result[0] with { Distance = result[^1].Distance };

        return result;
    }

    private void SubdivideRibbonRowForTerrainFit(List<RiverRibbonRow> result, RiverRibbonRow startRow, RiverRibbonRow endRow, int depth)
    {
        if (depth >= MaxMeshFollowTerrainSubdivisionDepth)
        {
            AppendRibbonRow(result, endRow);
            return;
        }

        float segmentLength = DistanceXZ(startRow.Position, endRow.Position);
        RiverRibbonRow midRow = LerpRibbonRow(startRow, endRow, 0.5f);
        float terrainDeviation = ComputeTerrainMidpointDeviation(startRow, midRow, endRow);
        float widthDelta = MathF.Abs(endRow.HalfWidth - startRow.HalfWidth);

        if (segmentLength <= MeshFollowTerrainSpacing
            && terrainDeviation <= MeshFollowTerrainHeightTolerance
            && widthDelta <= 0.5f)
        {
            AppendRibbonRow(result, endRow);
            return;
        }

        SubdivideRibbonRowForTerrainFit(result, startRow, midRow, depth + 1);
        SubdivideRibbonRowForTerrainFit(result, midRow, endRow, depth + 1);
    }

    private List<RiverRibbonRow> SmoothRibbonRows(List<RiverRibbonRow> rows, bool isLoop)
    {
        int activeCount = isLoop && rows.Count > 1 ? rows.Count - 1 : rows.Count;
        if (activeCount <= 2)
            return rows;

        var working = new List<RiverRibbonRow>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
            working.Add(rows[i]);

        for (int iteration = 0; iteration < RibbonSmoothingIterations; iteration++)
        {
            var smoothed = new RiverRibbonRow[working.Count];
            for (int i = 0; i < activeCount; i++)
            {
                RiverRibbonRow current = working[i];
                if (!isLoop && (i == 0 || i == activeCount - 1))
                {
                    smoothed[i] = current;
                    continue;
                }

                int previousIndex = i > 0 ? i - 1 : activeCount - 1;
                int nextIndex = i < activeCount - 1 ? i + 1 : 0;
                RiverRibbonRow previous = working[previousIndex];
                RiverRibbonRow next = working[nextIndex];
                float halfWidth = (previous.HalfWidth + current.HalfWidth * 2.0f + next.HalfWidth) * 0.25f;
                smoothed[i] = current with
                {
                    HalfWidth = Math.Max(MinVisibleHalfWidth, halfWidth),
                };
            }

            if (isLoop)
                smoothed[activeCount] = smoothed[0] with { Distance = working[activeCount].Distance };
            else if (working.Count > activeCount)
                smoothed[activeCount] = working[activeCount];

            RebuildRibbonSides(smoothed, activeCount, isLoop);
            for (int i = 0; i < smoothed.Length; i++)
                working[i] = smoothed[i];
        }

        return working;
    }

    private static void RebuildRibbonSides(RiverRibbonRow[] rows, int activeCount, bool isLoop)
    {
        for (int i = 0; i < activeCount; i++)
        {
            Vector3 tangent = ComputeRibbonTangent(rows, i, activeCount, isLoop);
            Vector3 side = PerpendicularXZ(tangent);
            rows[i] = rows[i] with { Side = side };
        }

        if (isLoop)
            rows[activeCount] = rows[0] with { Distance = rows[activeCount].Distance };
    }

    private static List<RiverRibbonRow> RecomputeRibbonDistances(List<RiverRibbonRow> rows, bool isLoop)
    {
        if (rows.Count <= 1)
            return rows;

        int activeCount = isLoop && rows.Count > 1 ? rows.Count - 1 : rows.Count;
        if (activeCount <= 0)
            return rows;

        var result = new List<RiverRibbonRow>(rows.Count)
        {
            rows[0] with { Distance = 0.0f }
        };

        float distance = 0.0f;
        for (int i = 1; i < activeCount; i++)
        {
            distance += DistanceXZ(rows[i - 1].Position, rows[i].Position);
            result.Add(rows[i] with { Distance = distance });
        }

        if (isLoop)
        {
            RiverRibbonRow first = result[0];
            float loopDistance = distance + DistanceXZ(rows[activeCount - 1].Position, first.Position);
            result.Add(first with { Distance = loopDistance });
        }
        else if (rows.Count > activeCount)
        {
            result.Add(rows[activeCount] with { Distance = distance });
        }

        return result;
    }

    private static List<RiverRibbonRow> StabilizeRibbonSideOrder(List<RiverRibbonRow> rows, bool isLoop)
    {
        if (rows.Count <= 1)
            return rows;

        int activeCount = isLoop && rows.Count > 1 ? rows.Count - 1 : rows.Count;
        if (activeCount <= 1)
            return rows;

        var result = new List<RiverRibbonRow>(rows.Count)
        {
            rows[0]
        };

        for (int i = 1; i < activeCount; i++)
        {
            RiverRibbonRow previous = result[^1];
            RiverRibbonRow current = rows[i];
            Vector3 previousForward = NormalizeXZ(current.Position - previous.Position);
            if (previousForward.LengthSquared() < 0.0001f && i + 1 < activeCount)
                previousForward = NormalizeXZ(rows[i + 1].Position - current.Position);

            if (previousForward.LengthSquared() >= 0.0001f)
            {
                Vector3 expectedSide = PerpendicularXZ(previousForward);
                float same = Vector3.Dot(current.Side, expectedSide);
                float flipped = Vector3.Dot(-current.Side, expectedSide);
                if (flipped > same)
                    current = current with { Side = -current.Side };
            }

            result.Add(current);
        }

        if (isLoop)
            result.Add(result[0] with { Distance = rows[^1].Distance });
        else if (rows.Count > activeCount)
            result.Add(rows[activeCount]);

        return result;
    }

    private static void AppendJoinedRibbonRows(
        List<RiverRibbonRow> result,
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
            AppendRibbonRow(result, new RiverRibbonRow(position, PerpendicularXZ(fallbackTangent), distance, halfWidth));
            return;
        }

        Vector3 startSide = PerpendicularXZ(previousTangent);
        Vector3 endSide = PerpendicularXZ(nextTangent);

        float tangentTurn = SignedAngleXZ(previousTangent, nextTangent);
        float absTurn = MathF.Abs(tangentTurn);
        if (absTurn < 0.0001f)
        {
            AppendRibbonRow(result, new RiverRibbonRow(position, startSide, distance, halfWidth));
            return;
        }

        if (Vector3.Dot(previousTangent, nextTangent) <= NearUturnDotThreshold)
        {
            AppendRibbonRow(result, new RiverRibbonRow(position, startSide, distance, halfWidth));
            return;
        }

        float previousLength = DistanceXZ(previousPosition, position);
        float nextLength = DistanceXZ(position, nextPosition);
        float minSegmentLength = Math.Min(previousLength, nextLength);
        if (minSegmentLength <= MinCurvePointSpacing * 2.0f)
        {
            AppendRibbonRow(result, new RiverRibbonRow(position, startSide, distance, halfWidth));
            return;
        }

        float trimFromSpan = minSegmentLength * Math.Clamp(cornerSpanFactor, 0.05f, 1.0f) * 0.5f;
        float halfTurn = absTurn * 0.5f;
        float tanHalfTurn = MathF.Tan(halfTurn);
        Vector3 bevelSide = NormalizeSide(startSide + endSide);
        float safeHalfWidth = Math.Max(halfWidth, 0.5f);
        float fallbackTrim = Math.Max(MinCurvePointSpacing * 2.0f, Math.Min(minSegmentLength * 0.2f, safeHalfWidth * 0.5f));
        if (tanHalfTurn <= 0.0001f)
        {
            AppendFallbackBevelRows(result, position, previousTangent, nextTangent, startSide, endSide, bevelSide, distance, fallbackTrim, halfWidth, Math.Max(MinCurvePointSpacing, minSegmentLength * 0.5f - MinCurvePointSpacing));
            return;
        }

        float minRadius = safeHalfWidth + MinCurvePointSpacing;
        float minTrim = minRadius * tanHalfTurn;
        float maxTrim = minSegmentLength * 0.5f - MinCurvePointSpacing;
        if (absTurn >= MathF.PI * 0.9f || maxTrim <= MinCurvePointSpacing || minTrim > maxTrim)
        {
            AppendFallbackBevelRows(result, position, previousTangent, nextTangent, startSide, endSide, bevelSide, distance, fallbackTrim, halfWidth, Math.Max(MinCurvePointSpacing, minSegmentLength * 0.5f - MinCurvePointSpacing));
            return;
        }

        float trimDistance = Math.Clamp(Math.Max(trimFromSpan, minTrim), MinCurvePointSpacing, maxTrim);
        float arcRadius = trimDistance / tanHalfTurn;
        Vector3 startCenter = position - previousTangent * trimDistance;
        Vector3 endCenter = position + nextTangent * trimDistance;
        Vector3 rawStartSide = startSide;
        Vector3 rawEndSide = endSide;
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
            AppendFallbackBevelRows(result, position, previousTangent, nextTangent, startSide, endSide, bevelSide, distance, fallbackTrim, halfWidth, Math.Max(MinCurvePointSpacing, minSegmentLength * 0.5f - MinCurvePointSpacing));
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

            float rowDistance = baseDistance + arcLength * t;
            AppendRibbonRow(result, new RiverRibbonRow(arcPosition, side, rowDistance, halfWidth));
        }
    }

    private static void AppendFallbackBevelRows(
        List<RiverRibbonRow> result,
        Vector3 position,
        Vector3 previousTangent,
        Vector3 nextTangent,
        Vector3 startSide,
        Vector3 endSide,
        Vector3 bevelSide,
        float distance,
        float trimDistance,
        float halfWidth,
        float maxAvailableTrim)
    {
        float clampedMaxTrim = Math.Max(MinCurvePointSpacing, maxAvailableTrim);
        trimDistance = Math.Clamp(Math.Max(trimDistance, MinCurvePointSpacing * 2.0f), MinCurvePointSpacing, clampedMaxTrim);
        Vector3 startPosition = position - previousTangent * trimDistance;
        Vector3 endPosition = position + nextTangent * trimDistance;
        Vector3 centerPosition = Vector3.Lerp(startPosition, endPosition, 0.5f);
        centerPosition.Y = position.Y;
        float startDistance = Math.Max(0.0f, distance - trimDistance);
        float endDistance = distance + trimDistance;
        float centerDistance = (startDistance + endDistance) * 0.5f;

        AppendRibbonRow(result, new RiverRibbonRow(startPosition, startSide, startDistance, halfWidth));
        AppendRibbonRow(result, new RiverRibbonRow(centerPosition, bevelSide, centerDistance, halfWidth));
        AppendRibbonRow(result, new RiverRibbonRow(endPosition, endSide, endDistance, halfWidth));
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

    private RiverRibbonRow LerpRibbonRow(RiverRibbonRow start, RiverRibbonRow end, float t)
    {
        Vector3 position = Vector3.Lerp(start.Position, end.Position, t);
        Vector3 side = NormalizeSide(Vector3.Lerp(start.Side, end.Side, t));
        if (side.LengthSquared() < 0.0001f)
            side = PerpendicularXZ(NormalizeXZ(end.Position - start.Position));
        float distance = MathUtil.Lerp(start.Distance, end.Distance, t);
        float halfWidth = MathUtil.Lerp(start.HalfWidth, end.HalfWidth, t);
        return new RiverRibbonRow(position, side, distance, halfWidth);
    }

    private float ComputeTerrainMidpointDeviation(RiverRibbonRow startRow, RiverRibbonRow midRow, RiverRibbonRow endRow)
    {
        float startCenter = SampleHeight(startRow.Position.X, startRow.Position.Z);
        float endCenter = SampleHeight(endRow.Position.X, endRow.Position.Z);
        float actualMidCenter = SampleHeight(midRow.Position.X, midRow.Position.Z);
        float expectedMidCenter = (startCenter + endCenter) * 0.5f;

        Vector3 startLeftPosition = startRow.Position - startRow.Side * startRow.HalfWidth;
        Vector3 endLeftPosition = endRow.Position - endRow.Side * endRow.HalfWidth;
        Vector3 midLeftPosition = midRow.Position - midRow.Side * midRow.HalfWidth;
        float startLeft = SampleHeight(startLeftPosition.X, startLeftPosition.Z);
        float endLeft = SampleHeight(endLeftPosition.X, endLeftPosition.Z);
        float actualMidLeft = SampleHeight(midLeftPosition.X, midLeftPosition.Z);
        float expectedMidLeft = (startLeft + endLeft) * 0.5f;

        Vector3 startRightPosition = startRow.Position + startRow.Side * startRow.HalfWidth;
        Vector3 endRightPosition = endRow.Position + endRow.Side * endRow.HalfWidth;
        Vector3 midRightPosition = midRow.Position + midRow.Side * midRow.HalfWidth;
        float startRight = SampleHeight(startRightPosition.X, startRightPosition.Z);
        float endRight = SampleHeight(endRightPosition.X, endRightPosition.Z);
        float actualMidRight = SampleHeight(midRightPosition.X, midRightPosition.Z);
        float expectedMidRight = (startRight + endRight) * 0.5f;

        return MathF.Max(
            MathF.Abs(actualMidCenter - expectedMidCenter),
            MathF.Max(
                MathF.Abs(actualMidLeft - expectedMidLeft),
                MathF.Abs(actualMidRight - expectedMidRight)));
    }

    private float EstimateAverageHalfWidth(byte[] rawData, int width, int height, List<Vector3> centerline, bool isLoop)
    {
        float total = 0.0f;
        int sampleCount = 0;
        for (int i = 0; i < centerline.Count; i += 4) // 每 4 个采样一次
        {
            int mx = (int)MathF.Round(centerline[i].X / MaskCellSize);
            int my = (int)MathF.Round(centerline[i].Z / MaskCellSize);
            if ((uint)mx >= (uint)width || (uint)my >= (uint)height)
                continue;

            byte blueValue = rawData[(my * width + mx) * 2 + 1];
            float halfWidth = RiverColorConverter.BlueValueToHalfWidth(blueValue);
            total += halfWidth;
            sampleCount++;
        }

        return sampleCount == 0 ? MinHalfWidth : total / sampleCount;
    }

    private float EstimateHalfWidth(byte[] rawData, int width, int height, Vector3 point, Vector3 side)
    {
        float positive = SampleFilledDistance(rawData, width, height, point, side);
        float negative = SampleFilledDistance(rawData, width, height, point, -side);
        float estimated = Math.Min(positive, negative);
        return Math.Max(MinHalfWidth, estimated);
    }

    private static float SampleFilledDistance(byte[] rawData, int width, int height, Vector3 point, Vector3 direction)
    {
        float lastFilledDistance = MaskCellHalfSize;
        for (int step = 1; step <= MaxWidthSampleSteps; step++)
        {
            float sampleDistance = step * MaskCellSize;
            float sampleX = point.X + direction.X * sampleDistance;
            float sampleZ = point.Z + direction.Z * sampleDistance;
            if (!IsFilled(rawData, width, height, sampleX, sampleZ))
                break;

            lastFilledDistance = sampleDistance + MaskCellHalfSize;
        }

        return lastFilledDistance;
    }

    private static bool[] BuildSkeleton(byte[] rawData, int width, int height)
    {
        int paddedWidth = width + 2;
        int paddedHeight = height + 2;
        var paddedSkeleton = new bool[paddedWidth * paddedHeight];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * width;
            int destinationRow = (y + 1) * paddedWidth + 1;
            for (int x = 0; x < width; x++)
                paddedSkeleton[destinationRow + x] = rawData[(sourceRow + x) * 2] != 0;
        }

        if (paddedWidth < 3 || paddedHeight < 3)
            return paddedSkeleton;

        var marked = new List<int>();
        bool changed;
        do
        {
            changed = false;
            MarkThinningPass(paddedSkeleton, paddedWidth, paddedHeight, firstPass: true, marked);
            if (marked.Count > 0)
            {
                changed = true;
                RemoveMarkedCells(paddedSkeleton, marked);
            }

            MarkThinningPass(paddedSkeleton, paddedWidth, paddedHeight, firstPass: false, marked);
            if (marked.Count > 0)
            {
                changed = true;
                RemoveMarkedCells(paddedSkeleton, marked);
            }
        }
        while (changed);

        var skeleton = new bool[rawData.Length];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = (y + 1) * paddedWidth + 1;
            int destinationRow = y * width;
            for (int x = 0; x < width; x++)
                skeleton[destinationRow + x] = paddedSkeleton[sourceRow + x];
        }

        return skeleton;
    }

    private static void MarkThinningPass(bool[] skeleton, int width, int height, bool firstPass, List<int> marked)
    {
        marked.Clear();
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                int index = y * width + x;
                if (!skeleton[index])
                    continue;

                GetNeighborFlags(skeleton, width, x, y,
                    out bool p2, out bool p3, out bool p4, out bool p5,
                    out bool p6, out bool p7, out bool p8, out bool p9);

                int neighborCount = CountTrueNeighbors(p2, p3, p4, p5, p6, p7, p8, p9);
                if (neighborCount < 2 || neighborCount > 6)
                    continue;

                int transitionCount = CountNeighborTransitions(p2, p3, p4, p5, p6, p7, p8, p9);
                if (transitionCount != 1)
                    continue;

                if (firstPass)
                {
                    if (p2 && p4 && p6)
                        continue;
                    if (p4 && p6 && p8)
                        continue;
                }
                else
                {
                    if (p2 && p4 && p8)
                        continue;
                    if (p2 && p6 && p8)
                        continue;
                }

                marked.Add(index);
            }
        }
    }

    private static void RemoveMarkedCells(bool[] skeleton, List<int> marked)
    {
        foreach (int index in marked)
            skeleton[index] = false;
        marked.Clear();
    }

    private static void GetNeighborFlags(
        bool[] skeleton,
        int width,
        int x,
        int y,
        out bool p2,
        out bool p3,
        out bool p4,
        out bool p5,
        out bool p6,
        out bool p7,
        out bool p8,
        out bool p9)
    {
        p2 = skeleton[(y - 1) * width + x];
        p3 = skeleton[(y - 1) * width + x + 1];
        p4 = skeleton[y * width + x + 1];
        p5 = skeleton[(y + 1) * width + x + 1];
        p6 = skeleton[(y + 1) * width + x];
        p7 = skeleton[(y + 1) * width + x - 1];
        p8 = skeleton[y * width + x - 1];
        p9 = skeleton[(y - 1) * width + x - 1];
    }

    private static int CountTrueNeighbors(bool p2, bool p3, bool p4, bool p5, bool p6, bool p7, bool p8, bool p9)
    {
        int count = 0;
        if (p2) count++;
        if (p3) count++;
        if (p4) count++;
        if (p5) count++;
        if (p6) count++;
        if (p7) count++;
        if (p8) count++;
        if (p9) count++;
        return count;
    }

    private static int CountNeighborTransitions(bool p2, bool p3, bool p4, bool p5, bool p6, bool p7, bool p8, bool p9)
    {
        ReadOnlySpan<bool> values = [p2, p3, p4, p5, p6, p7, p8, p9, p2];
        int transitions = 0;
        for (int i = 0; i < values.Length - 1; i++)
        {
            if (!values[i] && values[i + 1])
                transitions++;
        }

        return transitions;
    }

    private static List<RiverCenterSegment> ExtractSegments(bool[] skeleton, int width, int height)
    {
        var nodeKeys = new HashSet<int>();
        var nodeAnchors = new Dictionary<int, Vector3>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (!skeleton[index])
                    continue;

                int degree = CountSkeletonNeighbors(skeleton, width, height, x, y);
                if (degree != 2)
                    nodeKeys.Add(index);
            }
        }

        Dictionary<int, int> nodeComponentByKey = BuildNodeComponents(skeleton, width, height, nodeKeys, out List<RiverNodeComponent> nodeComponents);
        foreach (RiverNodeComponent component in nodeComponents)
            nodeAnchors[component.Id] = ComputeNodeAnchor(component.Keys, width);

        var visitedEdges = new HashSet<long>();
        var segments = new List<RiverCenterSegment>();
        int[] neighbors = new int[SkeletonNeighborDirections];

        foreach (RiverNodeComponent component in nodeComponents)
        {
            foreach (int nodeKey in component.Keys)
            {
                int nodeX = nodeKey % width;
                int nodeY = nodeKey / width;
                int neighborCount = CollectSkeletonNeighbors(skeleton, width, height, nodeX, nodeY, neighbors);
                for (int i = 0; i < neighborCount; i++)
                {
                    int neighborKey = neighbors[i];
                    if (nodeComponentByKey.TryGetValue(neighborKey, out int neighborComponentId) && neighborComponentId == component.Id)
                        continue;

                    long edgeKey = ComposeEdgeKey(nodeKey, neighborKey);
                    if (visitedEdges.Contains(edgeKey))
                        continue;

                    RiverCenterSegment? segment = TraceSegment(skeleton, width, height, nodeComponentByKey, nodeComponents, nodeAnchors, visitedEdges, component, nodeKey, neighborKey);
                    if (segment != null)
                        segments.Add(segment);
                }
            }
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (!skeleton[index] || nodeComponentByKey.ContainsKey(index))
                    continue;

                int neighborCount = CollectSkeletonNeighbors(skeleton, width, height, x, y, neighbors);
                for (int i = 0; i < neighborCount; i++)
                {
                    int neighborKey = neighbors[i];
                    if (nodeComponentByKey.ContainsKey(neighborKey))
                        continue;

                    long edgeKey = ComposeEdgeKey(index, neighborKey);
                    if (visitedEdges.Contains(edgeKey))
                        continue;

                    RiverCenterSegment? loopSegment = TraceLoopSegment(skeleton, width, height, visitedEdges, index, neighborKey);
                    if (loopSegment != null)
                        segments.Add(loopSegment);
                }
            }
        }

        return segments;
    }

    private static Dictionary<int, int> BuildNodeComponents(bool[] skeleton, int width, int height, HashSet<int> nodeKeys, out List<RiverNodeComponent> nodeComponents)
    {
        var componentByKey = new Dictionary<int, int>(nodeKeys.Count);
        nodeComponents = new List<RiverNodeComponent>();
        var queue = new Queue<int>();
        var visited = new HashSet<int>();

        foreach (int startKey in nodeKeys)
        {
            if (visited.Contains(startKey))
                continue;

            int startX = startKey % width;
            int startY = startKey / width;
            int startDegree = CountSkeletonNeighbors(skeleton, width, height, startX, startY);
            if (startDegree <= 1)
            {
                visited.Add(startKey);
                componentByKey[startKey] = nodeComponents.Count;
                nodeComponents.Add(new RiverNodeComponent
                {
                    Id = nodeComponents.Count,
                    Keys = [startKey],
                    IsJunction = false,
                    IsTerminal = true,
                });
                continue;
            }

            visited.Add(startKey);
            queue.Enqueue(startKey);
            var keys = new List<int>();
            while (queue.Count > 0)
            {
                int currentKey = queue.Dequeue();
                keys.Add(currentKey);
                componentByKey[currentKey] = nodeComponents.Count;

                int currentX = currentKey % width;
                int currentY = currentKey / width;
                for (int i = 0; i < NeighborOffsets.Length; i++)
                {
                    int neighborX = currentX + NeighborOffsets[i].X;
                    int neighborY = currentY + NeighborOffsets[i].Y;
                    if (!IsSkeletonNeighborConnected(skeleton, width, height, currentX, currentY, neighborX, neighborY))
                        continue;

                    int neighborKey = neighborY * width + neighborX;
                    if (!nodeKeys.Contains(neighborKey) || visited.Contains(neighborKey))
                        continue;

                    int neighborDegree = CountSkeletonNeighbors(skeleton, width, height, neighborX, neighborY);
                    if (neighborDegree < 3)
                        continue;

                    visited.Add(neighborKey);
                    queue.Enqueue(neighborKey);
                }
            }

            nodeComponents.Add(new RiverNodeComponent
            {
                Id = nodeComponents.Count,
                Keys = keys,
                IsJunction = true,
                IsTerminal = false,
            });
        }

        return componentByKey;
    }

    private static RiverCenterSegment? TraceSegment(
        bool[] skeleton,
        int width,
        int height,
        Dictionary<int, int> nodeComponentByKey,
        List<RiverNodeComponent> nodeComponents,
        Dictionary<int, Vector3> nodeAnchors,
        HashSet<long> visitedEdges,
        RiverNodeComponent startComponent,
        int startKey,
        int firstNeighborKey)
    {
        var cells = new List<MaskCell>();
        int previousKey = startKey;
        int currentKey = firstNeighborKey;
        cells.Add(ToMaskCell(startKey, width));

        int endNodeKey = -1;
        bool endTerminal = false;
        Vector3? startAnchor = startComponent.IsJunction && nodeAnchors.TryGetValue(startComponent.Id, out Vector3 resolvedStartAnchor)
            ? resolvedStartAnchor
            : null;
        Vector3? endAnchor = null;
        int[] neighbors = new int[SkeletonNeighborDirections];
        while (true)
        {
            cells.Add(ToMaskCell(currentKey, width));
            visitedEdges.Add(ComposeEdgeKey(previousKey, currentKey));
            if (nodeComponentByKey.TryGetValue(currentKey, out int currentComponentId) && currentComponentId != startComponent.Id)
            {
                RiverNodeComponent endComponent = nodeComponents[currentComponentId];
                endNodeKey = endComponent.IsJunction ? endComponent.Id : -1;
                endTerminal = endComponent.IsTerminal;
                if (endComponent.IsJunction && nodeAnchors.TryGetValue(endComponent.Id, out Vector3 resolvedEndAnchor))
                    endAnchor = resolvedEndAnchor;
                break;
            }

            int neighborCount = CollectSkeletonNeighbors(skeleton, width, height, currentKey % width, currentKey / width, neighbors);
            int nextKey = SelectNextNeighbor(previousKey, currentKey, width, neighbors.AsSpan(0, neighborCount));

            if (nextKey < 0)
                break;

            previousKey = currentKey;
            currentKey = nextKey;
        }

        if (cells.Count < 2)
            return null;

        return new RiverCenterSegment
        {
            Cells = cells,
            StartNodeKey = startComponent.IsJunction ? startComponent.Id : -1,
            EndNodeKey = endNodeKey,
            StartTerminal = startComponent.IsTerminal,
            EndTerminal = endTerminal,
            StartAnchor = startAnchor,
            EndAnchor = endAnchor,
            TaperStart = startComponent.IsTerminal,
            TaperEnd = endTerminal,
            IsLoop = false,
        };
    }

    private static RiverCenterSegment? TraceLoopSegment(bool[] skeleton, int width, int height, HashSet<long> visitedEdges, int startKey, int firstNeighborKey)
    {
        var cells = new List<MaskCell> { ToMaskCell(startKey, width) };
        var traversedEdges = new List<long>();
        int previousKey = startKey;
        int currentKey = firstNeighborKey;
        bool closedLoop = false;
        int[] neighbors = new int[SkeletonNeighborDirections];

        while (true)
        {
            cells.Add(ToMaskCell(currentKey, width));
            traversedEdges.Add(ComposeEdgeKey(previousKey, currentKey));

            int neighborCount = CollectSkeletonNeighbors(skeleton, width, height, currentKey % width, currentKey / width, neighbors);
            int nextKey = SelectNextNeighbor(previousKey, currentKey, width, neighbors.AsSpan(0, neighborCount));

            if (nextKey == startKey)
            {
                traversedEdges.Add(ComposeEdgeKey(currentKey, startKey));
                closedLoop = true;
                break;
            }

            if (nextKey < 0)
                break;

            previousKey = currentKey;
            currentKey = nextKey;
        }

        if (!closedLoop || cells.Count < 3)
            return null;

        foreach (long edge in traversedEdges)
            visitedEdges.Add(edge);

        return new RiverCenterSegment
        {
            Cells = cells,
            StartNodeKey = -1,
            EndNodeKey = -1,
            StartTerminal = false,
            EndTerminal = false,
            IsLoop = true,
        };
    }

    private static MaskCell ToMaskCell(int key, int width)
    {
        return new MaskCell(key % width, key / width);
    }

    private static Vector3 ComputeNodeAnchor(List<int> keys, int width)
    {
        if (keys.Count == 0)
            return Vector3.Zero;

        float sumX = 0.0f;
        float sumZ = 0.0f;
        foreach (int key in keys)
        {
            sumX += (key % width) * MaskCellSize;
            sumZ += (key / width) * MaskCellSize;
        }

        float inverseCount = 1.0f / keys.Count;
        return new Vector3(sumX * inverseCount, 0.0f, sumZ * inverseCount);
    }

    private static int CountSkeletonNeighbors(bool[] skeleton, int width, int height, int x, int y)
    {
        int count = 0;
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            int neighborX = x + NeighborOffsets[i].X;
            int neighborY = y + NeighborOffsets[i].Y;
            if (IsSkeletonNeighborConnected(skeleton, width, height, x, y, neighborX, neighborY))
                count++;
        }

        return count;
    }

    private static int CollectSkeletonNeighbors(bool[] skeleton, int width, int height, int x, int y, Span<int> destination)
    {
        int count = 0;
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            int neighborX = x + NeighborOffsets[i].X;
            int neighborY = y + NeighborOffsets[i].Y;
            if (!IsSkeletonNeighborConnected(skeleton, width, height, x, y, neighborX, neighborY))
                continue;

            destination[count++] = neighborY * width + neighborX;
        }

        return count;
    }

    private static bool IsSkeletonNeighborConnected(bool[] skeleton, int width, int height, int x, int y, int neighborX, int neighborY)
    {
        if ((uint)neighborX >= (uint)width || (uint)neighborY >= (uint)height)
            return false;

        if (!skeleton[neighborY * width + neighborX])
            return false;

        int dx = neighborX - x;
        int dy = neighborY - y;
        if (Math.Abs(dx) != 1 || Math.Abs(dy) != 1)
            return true;

        bool horizontalBridge = skeleton[y * width + neighborX];
        bool verticalBridge = skeleton[neighborY * width + x];
        return !horizontalBridge && !verticalBridge;
    }

    private static long ComposeEdgeKey(int a, int b)
    {
        uint min = (uint)Math.Min(a, b);
        uint max = (uint)Math.Max(a, b);
        return ((long)min << 32) | max;
    }

    private static int SelectNextNeighbor(int previousKey, int currentKey, int width, ReadOnlySpan<int> neighbors)
    {
        int bestKey = -1;
        float bestScore = float.NegativeInfinity;
        int previousX = previousKey % width;
        int previousY = previousKey / width;
        int currentX = currentKey % width;
        int currentY = currentKey / width;
        Vector2 forward = new(currentX - previousX, currentY - previousY);
        if (forward.LengthSquared() > 0.0f)
            forward.Normalize();

        for (int i = 0; i < neighbors.Length; i++)
        {
            int candidate = neighbors[i];
            if (candidate == previousKey)
                continue;

            int candidateX = candidate % width;
            int candidateY = candidate / width;
            Vector2 direction = new(candidateX - currentX, candidateY - currentY);
            if (direction.LengthSquared() > 0.0f)
                direction.Normalize();

            float score = Vector2.Dot(forward, direction);
            if (score > bestScore)
            {
                bestScore = score;
                bestKey = candidate;
            }
        }

        return bestKey;
    }

    private List<Vector3> BuildCenterline(byte[] rawData, int width, int height, List<MaskCell> cells, bool isLoop, Vector3? startAnchor, Vector3? endAnchor)
    {
        List<Vector3> sourcePoints = BuildMaskCenterlineSourcePoints(rawData, width, height, cells, isLoop, startAnchor, endAnchor);
        if (sourcePoints.Count < 2)
            return sourcePoints;

        List<Vector3> controlPoints = BuildCenterlineControlPoints(rawData, width, height, sourcePoints, isLoop);
        List<Vector3> result = ResampleCenterline(rawData, width, height, controlPoints, isLoop);
        if (result.Count == 0)
            result = controlPoints;

        if (isLoop && result.Count >= 2 && DistanceXZ(result[0], result[^1]) <= MinCurvePointSpacing)
            result.RemoveAt(result.Count - 1);

        if (isLoop && result.Count >= 3)
            result.Add(result[0]);
        return result;
    }

    private List<Vector3> BuildMaskCenterlineSourcePoints(byte[] rawData, int width, int height, List<MaskCell> cells, bool isLoop, Vector3? startAnchor, Vector3? endAnchor)
    {
        var rawPoints = new List<Vector3>(cells.Count + 2);
        if (!isLoop && startAnchor.HasValue)
            rawPoints.Add(startAnchor.Value);

        foreach (MaskCell cell in cells)
            rawPoints.Add(new Vector3(cell.X * MaskCellSize, 0.0f, cell.Y * MaskCellSize));

        if (!isLoop && endAnchor.HasValue)
            rawPoints.Add(endAnchor.Value);

        if (rawPoints.Count <= 2)
            return rawPoints;

        List<Vector3> orderedPoints = isLoop ? RotateLoopToStableSeam(rawPoints) : rawPoints;
        List<Vector3> centeredPoints = RecenterPointsToMaskCorridor(rawData, width, height, orderedPoints, isLoop);
        List<Vector3> smoothedPoints = SmoothCenterlineSourcePoints(centeredPoints, isLoop);
        List<Vector3> simplifiedPoints = RemoveBacktrackingCenterlinePoints(smoothedPoints, isLoop);
        int minimumPointCount = isLoop ? 3 : 2;
        return simplifiedPoints.Count >= minimumPointCount ? simplifiedPoints : orderedPoints;
    }

    private List<Vector3> BuildCenterlineControlPoints(byte[] rawData, int width, int height, List<Vector3> sourcePoints, bool isLoop)
    {
        if (sourcePoints.Count <= 2)
            return sourcePoints;

        List<Vector3> controlPoints = SelectCenterlineControlPoints(rawData, width, height, sourcePoints, isLoop);
        return controlPoints.Count >= 2 ? controlPoints : sourcePoints;
    }

    private static List<Vector3> RecenterPointsToMaskCorridor(byte[] rawData, int width, int height, List<Vector3> points, bool isLoop)
    {
        if (points.Count <= 2)
            return points;

        var result = new List<Vector3>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 point = points[i];
            if (ShouldPreserveTopologyNeighborhood(i, points.Count, isLoop))
            {
                result.Add(point);
                continue;
            }

            Vector3 tangent = ComputePolylineTangent(points, i, isLoop);
            if (tangent.LengthSquared() < 0.0001f)
            {
                result.Add(point);
                continue;
            }

            Vector3 side = PerpendicularXZ(tangent);
            float positive = SampleFilledDistance(rawData, width, height, point, side);
            float negative = SampleFilledDistance(rawData, width, height, point, -side);
            float centerOffset = (positive - negative) * 0.5f;
            Vector3 centeredPoint = point + side * centerOffset;
            result.Add(IsCenterlinePointInsideMask(rawData, width, height, centeredPoint) ? centeredPoint : point);
        }

        return result;
    }

    private static List<Vector3> SmoothCenterlineSourcePoints(List<Vector3> points, bool isLoop)
    {
        int count = points.Count;
        if (count <= 2)
            return points;

        var working = new List<Vector3>(points.Count);
        foreach (Vector3 point in points)
            working.Add(point);

        for (int iteration = 0; iteration < CenterlineSmoothingIterations; iteration++)
        {
            var smoothed = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                if (ShouldPreserveTopologyNeighborhood(i, count, isLoop))
                {
                    smoothed[i] = working[i];
                    continue;
                }

                int previousIndex = i > 0 ? i - 1 : count - 1;
                int nextIndex = i < count - 1 ? i + 1 : 0;
                Vector3 previous = working[previousIndex];
                Vector3 current = working[i];
                Vector3 next = working[nextIndex];
                smoothed[i] = new Vector3(
                    (previous.X + current.X * 2.0f + next.X) * 0.25f,
                    current.Y,
                    (previous.Z + current.Z * 2.0f + next.Z) * 0.25f);
            }

            working.Clear();
            for (int i = 0; i < count; i++)
                working.Add(smoothed[i]);
        }

        return working;
    }

    private static List<Vector3> RemoveBacktrackingCenterlinePoints(List<Vector3> points, bool isLoop)
    {
        if (points.Count <= 2)
            return points;

        float maxBacktrackDot = MathUtil.Lerp(-0.1f, -0.55f, Math.Clamp(RiverCornerSpanFactor, 0.05f, 1.0f));
        if (!isLoop)
        {
            var result = new List<Vector3>(points.Count)
            {
                points[0]
            };

            for (int i = 1; i < points.Count - 1; i++)
            {
                if (ShouldPreserveTopologyNeighborhood(i, points.Count, isLoop))
                {
                    AppendCenterlinePoint(result, points[i]);
                    continue;
                }

                Vector3 previous = result[^1];
                Vector3 current = points[i];
                Vector3 next = points[i + 1];
                if (ShouldSkipBacktrackingCenterlinePoint(previous, current, next, maxBacktrackDot))
                    continue;

                AppendCenterlinePoint(result, current);
            }

            AppendCenterlinePoint(result, points[^1]);
            return result;
        }

        int count = points.Count;
        var loopResult = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            if (ShouldPreserveTopologyNeighborhood(i, count, isLoop))
            {
                AppendCenterlinePoint(loopResult, points[i]);
                continue;
            }

            Vector3 previous = points[(i - 1 + count) % count];
            Vector3 current = points[i];
            Vector3 next = points[(i + 1) % count];
            if (ShouldSkipBacktrackingCenterlinePoint(previous, current, next, maxBacktrackDot))
                continue;

            AppendCenterlinePoint(loopResult, current);
        }

        return loopResult.Count >= 3 ? loopResult : points;
    }

    private static bool ShouldPreserveTopologyNeighborhood(int index, int count, bool isLoop)
    {
        if (count <= 2)
            return true;

        if (!isLoop)
            return index <= 1 || index >= count - 2;

        return index == 0 || index == 1 || index == count - 1 || index == count - 2;
    }

    private static bool ShouldSkipBacktrackingCenterlinePoint(Vector3 previous, Vector3 current, Vector3 next, float maxBacktrackDot)
    {
        Vector3 incoming = NormalizeXZ(current - previous);
        Vector3 outgoing = NormalizeXZ(next - current);
        if (incoming.LengthSquared() < 0.0001f || outgoing.LengthSquared() < 0.0001f)
            return false;

        float dot = Vector3.Dot(incoming, outgoing);
        return dot <= maxBacktrackDot
            && DistanceXZ(previous, next) <= CurveSampleSpacing;
    }

    private static List<Vector3> ResampleCenterline(byte[] rawData, int width, int height, List<Vector3> controlPoints, bool isLoop)
    {
        if (controlPoints.Count <= 2)
            return controlPoints;

        return isLoop
            ? ResampleLoopCenterline(rawData, width, height, controlPoints)
            : ResampleOpenCenterline(rawData, width, height, controlPoints);
    }

    private static List<Vector3> ResampleOpenCenterline(byte[] rawData, int width, int height, List<Vector3> controlPoints)
    {
        var result = new List<Vector3>();
        for (int i = 0; i < controlPoints.Count - 1; i++)
        {
            Vector3 p0 = controlPoints[Math.Max(0, i - 1)];
            Vector3 p1 = controlPoints[i];
            Vector3 p2 = controlPoints[i + 1];
            Vector3 p3 = controlPoints[Math.Min(controlPoints.Count - 1, i + 2)];
            if (result.Count == 0)
                AppendCenterlinePoint(result, p1);

            float segmentHalfWidth = EstimateSegmentHalfWidth(rawData, width, height, p1, p2);
            SubdivideCenterlineSegment(result, rawData, width, height, p0, p1, p2, p3, 0.0f, 1.0f, p1, p2, 0, segmentHalfWidth);
        }

        return result;
    }

    private static List<Vector3> ResampleLoopCenterline(byte[] rawData, int width, int height, List<Vector3> controlPoints)
    {
        var result = new List<Vector3>();
        int count = controlPoints.Count;
        for (int i = 0; i < count; i++)
        {
            Vector3 p0 = controlPoints[(i - 1 + count) % count];
            Vector3 p1 = controlPoints[i];
            Vector3 p2 = controlPoints[(i + 1) % count];
            Vector3 p3 = controlPoints[(i + 2) % count];
            if (result.Count == 0)
                AppendCenterlinePoint(result, p1);

            float segmentHalfWidth = EstimateSegmentHalfWidth(rawData, width, height, p1, p2);
            SubdivideCenterlineSegment(result, rawData, width, height, p0, p1, p2, p3, 0.0f, 1.0f, p1, p2, 0, segmentHalfWidth);
        }

        return result;
    }

    private static float EstimateSegmentHalfWidth(byte[] rawData, int width, int height, Vector3 startPoint, Vector3 endPoint)
    {
        Vector3 tangent = NormalizeXZ(endPoint - startPoint);
        if (tangent.LengthSquared() < 0.0001f)
            return MinHalfWidth;

        Vector3 side = PerpendicularXZ(tangent);
        float startHalfWidth = EstimateHalfWidthAt(rawData, width, height, startPoint, side);
        float endHalfWidth = EstimateHalfWidthAt(rawData, width, height, endPoint, side);
        return Math.Max(MinHalfWidth, (startHalfWidth + endHalfWidth) * 0.5f);
    }

    private static float EstimateHalfWidthAt(byte[] rawData, int width, int height, Vector3 point, Vector3 side)
    {
        float positive = SampleFilledDistance(rawData, width, height, point, side);
        float negative = SampleFilledDistance(rawData, width, height, point, -side);
        return Math.Max(MinHalfWidth, Math.Min(positive, negative));
    }

    private static void SubdivideCenterlineSegment(
        List<Vector3> result,
        byte[] rawData,
        int width,
        int height,
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector3 p3,
        float startT,
        float endT,
        Vector3 startPoint,
        Vector3 endPoint,
        int depth,
        float halfWidth)
    {
        if (depth >= MaxCurveSubdivisionDepth)
        {
            AppendCenterlinePoint(result, endPoint);
            return;
        }

        float chordLength = DistanceXZ(startPoint, endPoint);
        float midT = (startT + endT) * 0.5f;
        Vector3 midPoint = EvaluateCentripetalCatmullRom(p0, p1, p2, p3, midT);
        float deviation = DistancePointToSegmentXZ(midPoint, startPoint, endPoint);
        Vector3 startTangent = EvaluateCurveTangent(p0, p1, p2, p3, startT);
        Vector3 endTangent = EvaluateCurveTangent(p0, p1, p2, p3, endT);
        float turnDegrees = MathF.Abs(SignedAngleXZ(startTangent, endTangent)) * (180.0f / MathF.PI);
        float midHalfWidth = EstimateHalfWidthAt(rawData, width, height, midPoint, PerpendicularXZ(NormalizeSide(startTangent + endTangent)));
        float maxHalfWidth = Math.Max(halfWidth, midHalfWidth);

        if (chordLength <= CurveSampleSpacing
            && deviation <= CurveSubdivisionTolerance
            && turnDegrees <= ComputeAdaptiveTurnThreshold(maxHalfWidth)
            && IsCenterlineSegmentInsideMask(rawData, width, height, startPoint, midPoint, endPoint))
        {
            AppendCenterlinePoint(result, endPoint);
            return;
        }

        float leftHalfWidth = EstimateSegmentHalfWidth(rawData, width, height, startPoint, midPoint);
        float rightHalfWidth = EstimateSegmentHalfWidth(rawData, width, height, midPoint, endPoint);
        SubdivideCenterlineSegment(result, rawData, width, height, p0, p1, p2, p3, startT, midT, startPoint, midPoint, depth + 1, leftHalfWidth);
        SubdivideCenterlineSegment(result, rawData, width, height, p0, p1, p2, p3, midT, endT, midPoint, endPoint, depth + 1, rightHalfWidth);
    }

    private static void AppendCenterlinePoint(List<Vector3> result, Vector3 point)
    {
        if (result.Count == 0)
        {
            result.Add(point);
            return;
        }

        if (DistanceXZ(result[^1], point) <= MinCurvePointSpacing)
        {
            result[^1] = point;
            return;
        }

        result.Add(point);
    }

    private static float ComputePolylineLength(List<Vector3> points, bool isLoop)
    {
        float length = 0.0f;
        int count = points.Count;
        if (count < 2)
            return length;

        for (int i = 1; i < count; i++)
            length += DistanceXZ(points[i - 1], points[i]);

        if (isLoop && count > 2)
            length += DistanceXZ(points[count - 1], points[0]);

        return length;
    }

    private static Vector3 ComputePolylineTangent(List<Vector3> points, int index, bool isLoop, int activeCount = -1)
    {
        int count = activeCount > 0 ? activeCount : points.Count;
        if (count <= 1)
            return Vector3.UnitZ;

        Vector3 tangent = Vector3.Zero;
        if (isLoop)
        {
            int previousIndex = (index - 1 + count) % count;
            int nextIndex = (index + 1) % count;
            tangent += NormalizeXZ(points[index] - points[previousIndex]);
            tangent += NormalizeXZ(points[nextIndex] - points[index]);
        }
        else
        {
            if (index > 0)
                tangent += NormalizeXZ(points[index] - points[index - 1]);
            if (index < count - 1)
                tangent += NormalizeXZ(points[index + 1] - points[index]);
        }

        tangent = NormalizeXZ(tangent);
        if (tangent.LengthSquared() < 0.0001f)
        {
            if (isLoop)
            {
                int nextIndex = (index + 1) % count;
                tangent = NormalizeXZ(points[nextIndex] - points[index]);
            }
            else if (index < count - 1)
            {
                tangent = NormalizeXZ(points[index + 1] - points[index]);
            }
            else
            {
                tangent = NormalizeXZ(points[index] - points[index - 1]);
            }
        }

        return tangent.LengthSquared() < 0.0001f ? Vector3.UnitZ : tangent;
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
        float thresholdRadians = MathF.Atan2(MaxEdgeDeviation, safeWidth);
        return thresholdRadians * (180.0f / MathF.PI);
    }

    private static List<Vector3> RotateLoopToStableSeam(List<Vector3> rawPoints)
    {
        if (rawPoints.Count <= 2)
            return rawPoints;

        int bestIndex = 0;
        float bestScore = float.NegativeInfinity;
        float bestX = float.PositiveInfinity;
        float bestZ = float.PositiveInfinity;
        for (int i = 0; i < rawPoints.Count; i++)
        {
            Vector3 previous = rawPoints[(i - 1 + rawPoints.Count) % rawPoints.Count];
            Vector3 current = rawPoints[i];
            Vector3 next = rawPoints[(i + 1) % rawPoints.Count];
            Vector3 incoming = NormalizeXZ(current - previous);
            Vector3 outgoing = NormalizeXZ(next - current);
            float straightness = Vector3.Dot(incoming, outgoing);
            float axisBias = MathF.Abs(outgoing.X) + MathF.Abs(outgoing.Z);
            float score = straightness * 10.0f + axisBias;
            if (score > bestScore + 0.0001f
                || (MathF.Abs(score - bestScore) <= 0.0001f && (current.X < bestX - 0.0001f
                    || (MathF.Abs(current.X - bestX) <= 0.0001f && current.Z < bestZ - 0.0001f))))
            {
                bestScore = score;
                bestIndex = i;
                bestX = current.X;
                bestZ = current.Z;
            }
        }

        if (bestIndex == 0)
            return rawPoints;

        var result = new List<Vector3>(rawPoints.Count);
        for (int i = 0; i < rawPoints.Count; i++)
            result.Add(rawPoints[(bestIndex + i) % rawPoints.Count]);
        return result;
    }

    private List<Vector3> SelectCenterlineControlPoints(byte[] rawData, int width, int height, List<Vector3> points, bool isLoop)
    {
        int count = points.Count;
        var result = new List<Vector3>(count)
        {
            points[0]
        };

        int lastIncludedIndex = 0;
        int finalExclusive = isLoop ? count : count - 1;
        for (int i = 1; i < finalExclusive; i++)
        {
            Vector3 current = points[i];
            if (ShouldPreserveTopologyNeighborhood(i, count, isLoop))
            {
                AppendCenterlinePoint(result, current);
                lastIncludedIndex = i;
                continue;
            }

            int previousIndex = isLoop ? (i - 2 + count) % count : Math.Max(0, i - 2);
            int nextIndex = isLoop ? (i + 2) % count : Math.Min(count - 1, i + 2);
            Vector3 previous = points[previousIndex];
            Vector3 next = points[nextIndex];
            Vector3 incoming = NormalizeXZ(current - previous);
            Vector3 outgoing = NormalizeXZ(next - current);
            float turnDegrees = incoming.LengthSquared() < 0.0001f || outgoing.LengthSquared() < 0.0001f
                ? 0.0f
                : MathF.Abs(SignedAngleXZ(incoming, outgoing)) * (180.0f / MathF.PI);
            float distanceFromLast = DistanceXZ(points[lastIncludedIndex], current);
            float localHalfWidth = EstimateLocalControlHalfWidth(rawData, width, height, previous, current, next, incoming, outgoing);
            float spacingThreshold = Math.Max(CenterlineControlSpacing, localHalfWidth * 1.5f);
            bool shouldKeep = turnDegrees >= CenterlineControlTurnThresholdDegrees
                || distanceFromLast >= spacingThreshold
                || !CanSkipControlPoint(rawData, width, height, points[lastIncludedIndex], current, next, localHalfWidth);
            if (shouldKeep)
            {
                AppendCenterlinePoint(result, current);
                lastIncludedIndex = i;
            }
        }

        if (!isLoop)
        {
            AppendCenterlinePoint(result, points[^1]);
            return result;
        }

        if (DistanceXZ(result[0], result[^1]) <= MinCurvePointSpacing)
            result.RemoveAt(result.Count - 1);

        return result.Count >= 3 ? result : new List<Vector3>(points);
    }

    private float EstimateLocalControlHalfWidth(byte[] rawData, int width, int height, Vector3 previous, Vector3 current, Vector3 next, Vector3 incoming, Vector3 outgoing)
    {
        Vector3 tangent = NormalizeSide(incoming + outgoing);
        if (tangent.LengthSquared() < 0.0001f)
            tangent = incoming.LengthSquared() >= 0.0001f ? incoming : outgoing;
        Vector3 side = PerpendicularXZ(tangent);
        return EstimateHalfWidthAt(rawData, width, height, current, side);
    }

    private bool CanSkipControlPoint(byte[] rawData, int width, int height, Vector3 start, Vector3 current, Vector3 end, float localHalfWidth)
    {
        if (DistancePointToSegmentXZ(current, start, end) > Math.Max(0.6f, localHalfWidth * 0.2f))
            return false;

        Vector3 tangent = NormalizeXZ(end - start);
        if (tangent.LengthSquared() < 0.0001f)
            return false;

        Vector3 side = PerpendicularXZ(tangent);
        float sampleHalfWidth = Math.Max(MinHalfWidth, localHalfWidth * 0.75f);
        return IsMaskCorridorFilled(rawData, width, height, start, end, side, sampleHalfWidth);
    }

    private static bool IsMaskCorridorFilled(byte[] rawData, int width, int height, Vector3 start, Vector3 end, Vector3 side, float halfWidth)
    {
        float length = DistanceXZ(start, end);
        int steps = Math.Max(2, (int)MathF.Ceiling(length / MaskCellSize));
        for (int i = 1; i < steps; i++)
        {
            float t = i / (float)steps;
            Vector3 center = Vector3.Lerp(start, end, t);
            if (!IsFilled(rawData, width, height, center.X, center.Z))
                return false;

            Vector3 left = center - side * halfWidth;
            Vector3 right = center + side * halfWidth;
            if (!IsFilled(rawData, width, height, left.X, left.Z) || !IsFilled(rawData, width, height, right.X, right.Z))
                return false;
        }

        return true;
    }

    private static float DistancePointToSegmentXZ(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
    {
        Vector2 p = new(point.X, point.Z);
        Vector2 a = new(segmentStart.X, segmentStart.Z);
        Vector2 b = new(segmentEnd.X, segmentEnd.Z);
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared <= 0.0001f)
            return Vector2.Distance(p, a);

        float t = Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0.0f, 1.0f);
        Vector2 projection = a + ab * t;
        return Vector2.Distance(p, projection);
    }

    private static bool IsCenterlinePointInsideMask(byte[] rawData, int width, int height, Vector3 point)
    {
        return IsFilled(rawData, width, height, point.X, point.Z);
    }

    private static bool IsCenterlineSegmentInsideMask(byte[] rawData, int width, int height, Vector3 startPoint, Vector3 midPoint, Vector3 endPoint)
    {
        if (!IsCenterlinePointInsideMask(rawData, width, height, startPoint)
            || !IsCenterlinePointInsideMask(rawData, width, height, midPoint)
            || !IsCenterlinePointInsideMask(rawData, width, height, endPoint))
        {
            return false;
        }

        Vector3 quarterPoint = Vector3.Lerp(startPoint, midPoint, 0.5f);
        Vector3 threeQuarterPoint = Vector3.Lerp(midPoint, endPoint, 0.5f);
        if (!IsCenterlinePointInsideMask(rawData, width, height, quarterPoint)
            || !IsCenterlinePointInsideMask(rawData, width, height, threeQuarterPoint))
        {
            return false;
        }

        Vector3 tangent = NormalizeXZ(endPoint - startPoint);
        if (tangent.LengthSquared() < 0.0001f)
            tangent = NormalizeXZ(endPoint - midPoint);
        if (tangent.LengthSquared() < 0.0001f)
            tangent = NormalizeXZ(midPoint - startPoint);
        if (tangent.LengthSquared() < 0.0001f)
            return true;

        Vector3 side = PerpendicularXZ(tangent);
        float halfWidth = Math.Max(MinHalfWidth, Math.Min(
            EstimateHalfWidthAt(rawData, width, height, startPoint, side),
            Math.Min(
                EstimateHalfWidthAt(rawData, width, height, midPoint, side),
                EstimateHalfWidthAt(rawData, width, height, endPoint, side))));

        return IsRibbonPointInsideMask(rawData, width, height, startPoint, side, halfWidth)
            && IsRibbonPointInsideMask(rawData, width, height, quarterPoint, side, halfWidth)
            && IsRibbonPointInsideMask(rawData, width, height, midPoint, side, halfWidth)
            && IsRibbonPointInsideMask(rawData, width, height, threeQuarterPoint, side, halfWidth)
            && IsRibbonPointInsideMask(rawData, width, height, endPoint, side, halfWidth);
    }

    private static bool IsRibbonPointInsideMask(byte[] rawData, int width, int height, Vector3 point, Vector3 side, float halfWidth)
    {
        return IsCenterlinePointInsideMask(rawData, width, height, point - side * halfWidth)
            && IsCenterlinePointInsideMask(rawData, width, height, point)
            && IsCenterlinePointInsideMask(rawData, width, height, point + side * halfWidth);
    }

    private static Vector3 ComputeRibbonTangent(IReadOnlyList<RiverRibbonRow> rows, int index, int activeCount, bool isLoop)
    {
        if (activeCount <= 1)
            return Vector3.UnitZ;

        Vector3 tangent = Vector3.Zero;
        if (isLoop)
        {
            int previousIndex = index > 0 ? index - 1 : activeCount - 1;
            int nextIndex = index < activeCount - 1 ? index + 1 : 0;
            tangent += NormalizeXZ(rows[index].Position - rows[previousIndex].Position);
            tangent += NormalizeXZ(rows[nextIndex].Position - rows[index].Position);
        }
        else
        {
            if (index > 0)
                tangent += NormalizeXZ(rows[index].Position - rows[index - 1].Position);
            if (index < activeCount - 1)
                tangent += NormalizeXZ(rows[index + 1].Position - rows[index].Position);
        }

        tangent = NormalizeXZ(tangent);
        if (tangent.LengthSquared() < 0.0001f)
        {
            if (isLoop)
            {
                int nextIndex = index < activeCount - 1 ? index + 1 : 0;
                tangent = NormalizeXZ(rows[nextIndex].Position - rows[index].Position);
            }
            else if (index < activeCount - 1)
            {
                tangent = NormalizeXZ(rows[index + 1].Position - rows[index].Position);
            }
            else
            {
                tangent = NormalizeXZ(rows[index].Position - rows[index - 1].Position);
            }
        }

        return tangent.LengthSquared() < 0.0001f ? Vector3.UnitZ : tangent;
    }

    private static float ComputeRibbonTextureU(float distance, float totalDistance, bool isLoop)
    {
        float baseU = distance * RiverTextureRepeatUScale;
        if (!isLoop || totalDistance <= MinCurvePointSpacing || RiverFlowTiling <= 0.0001f)
            return baseU;

        float totalPhase = totalDistance * RiverTextureRepeatUScale * RiverFlowTiling;
        float loopCycles = Math.Max(1.0f, MathF.Round(totalPhase / (MathF.PI * 2.0f)));
        float phaseScale = (MathF.PI * 2.0f * loopCycles) / Math.Max(totalPhase, 0.0001f);
        return baseU * phaseScale;
    }

    private static float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = b.X - a.X;
        float dz = b.Z - a.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
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

    private Vector3 SampleSurfacePosition(float worldX, float worldZ)
    {
        float clampedX = Math.Clamp(worldX, 0.0f, Math.Max(0.0f, terrainManager.HeightCacheWidth - 1));
        float clampedZ = Math.Clamp(worldZ, 0.0f, Math.Max(0.0f, terrainManager.HeightCacheHeight - 1));
        float worldY = terrainManager.GetHeightAtPosition(clampedX, clampedZ) ?? 0.0f;
        return new Vector3(clampedX, worldY + SurfaceOffset, clampedZ);
    }

    private Vector3 SampleSurfaceNormal(float worldX, float worldZ)
    {
        float left = SampleHeight(worldX - NormalSampleStep, worldZ);
        float right = SampleHeight(worldX + NormalSampleStep, worldZ);
        float top = SampleHeight(worldX, worldZ - NormalSampleStep);
        float bottom = SampleHeight(worldX, worldZ + NormalSampleStep);
        Vector3 normal = Vector3.Normalize(new Vector3(left - right, 2.0f * NormalSampleStep, top - bottom));
        return float.IsNaN(normal.X) || float.IsNaN(normal.Y) || float.IsNaN(normal.Z) || normal.LengthSquared() < 0.0001f
            ? Vector3.UnitY
            : normal;
    }

    private float SampleHeight(float worldX, float worldZ)
    {
        float clampedX = Math.Clamp(worldX, 0.0f, Math.Max(0.0f, terrainManager.HeightCacheWidth - 1));
        float clampedZ = Math.Clamp(worldZ, 0.0f, Math.Max(0.0f, terrainManager.HeightCacheHeight - 1));
        return terrainManager.GetHeightAtPosition(clampedX, clampedZ) ?? 0.0f;
    }

    private static bool IsFilled(byte[] rawData, int width, int height, float worldX, float worldZ)
    {
        int maskX = (int)MathF.Round(worldX / MaskCellSize);
        int maskY = (int)MathF.Round(worldZ / MaskCellSize);
        return IsFilled(rawData, width, height, maskX, maskY);
    }

    private static bool IsFilled(byte[] rawData, int width, int height, int x, int y)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            return false;
        return rawData[(y * width + x) * 2] != 0;
    }

    private void ReplaceRiverMesh(VertexPositionNormalTexture[] vertices, int[] indices)
    {
        RemoveRiverMesh();

        vertexBuffer = Buffer.Vertex.New(graphicsDevice, vertices, GraphicsResourceUsage.Dynamic);
        indexBuffer = Buffer.Index.New(graphicsDevice, indices);

        var meshDraw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            DrawCount = indices.Length,
            VertexBuffers =
            [
                new VertexBufferBinding(vertexBuffer, VertexPositionNormalTexture.Layout, vertices.Length),
            ],
            IndexBuffer = new IndexBufferBinding(indexBuffer, true, indices.Length),
        };

        var mesh = new Mesh(meshDraw, new ParameterCollection())
        {
            MaterialIndex = 0,
        };

        var model = new Model();
        model.Meshes.Add(mesh);
        model.Materials.Add(GetOrCreateRiverMeshMaterial());

        riverMeshEntity = new Entity("RiverMaskMesh")
        {
            new ModelComponent(model)
            {
                RenderGroup = PathFeatureService.PathRenderGroup,
                IsShadowCaster = false,
            }
        };
        scene.Entities.Add(riverMeshEntity);
    }

    private void RemoveRiverMesh()
    {
        if (riverMeshEntity != null)
        {
            scene.Entities.Remove(riverMeshEntity);
            riverMeshEntity = null;
        }

        vertexBuffer?.Dispose();
        vertexBuffer = null;

        indexBuffer?.Dispose();
        indexBuffer = null;
    }

    private Material GetOrCreateRiverMeshMaterial()
    {
        if (riverMeshMaterial != null)
            return riverMeshMaterial;

        var descriptor = new MaterialDescriptor();
        descriptor.Attributes.Diffuse = new MaterialPathRiverFeature();
        descriptor.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();
        descriptor.Attributes.MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(0.78f));
        descriptor.Attributes.Specular = new MaterialMetalnessMapFeature(new ComputeFloat(0.0f));
        descriptor.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
        descriptor.Attributes.Transparency = new MaterialTransparencyBlendFeature();

        riverMeshMaterial = Material.New(graphicsDevice, descriptor);
        ParameterCollection parameters = riverMeshMaterial.Passes[0].Parameters;
        parameters.Set(PathRiverSurfaceKeys.ShallowColor, new Color4(0.10f, 0.42f, 0.78f, 0.58f));
        parameters.Set(PathRiverSurfaceKeys.DeepColor, new Color4(0.03f, 0.18f, 0.42f, 0.90f));
        parameters.Set(PathRiverSurfaceKeys.EdgeFadeStart, RiverEdgeFadeStart);
        parameters.Set(PathRiverSurfaceKeys.FlowTiling, RiverFlowTiling);
        parameters.Set(PathRiverSurfaceKeys.FlowStrength, RiverFlowStrength);
        parameters.Set(PathRiverSurfaceKeys.HighlightStrength, RiverHighlightStrength);
        return riverMeshMaterial;
    }

    private readonly record struct MaskCell(int X, int Y);

    private readonly record struct RiverRibbonRow(Vector3 Position, Vector3 Side, float Distance, float HalfWidth);

    private readonly record struct RiverJunctionAttachment(int SegmentIndex, bool AtStart, float AverageHalfWidth, float WorldLength);

    private sealed class RiverMeshTopology
    {
        public RiverMeshTopology(int width, int height, byte[] rawData, List<RiverCenterSegment> segments)
        {
            Width = width;
            Height = height;
            RawData = rawData;
            Segments = segments;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] RawData { get; }
        public List<RiverCenterSegment> Segments { get; }
    }

    private sealed class RiverCenterSegment
    {
        public required List<MaskCell> Cells { get; init; }
        public required int StartNodeKey { get; init; }
        public required int EndNodeKey { get; init; }
        public required bool StartTerminal { get; init; }
        public required bool EndTerminal { get; init; }
        public required bool IsLoop { get; init; }
        public Vector3? StartAnchor { get; init; }
        public Vector3? EndAnchor { get; init; }
        public List<Vector3>? Centerline { get; set; }
        public float AverageHalfWidth { get; set; }
        public float WorldLength { get; set; }
        public bool TaperStart { get; set; }
        public bool TaperEnd { get; set; }
    }

    private sealed class RiverNodeComponent
    {
        public required int Id { get; init; }
        public required List<int> Keys { get; init; }
        public required bool IsJunction { get; init; }
        public required bool IsTerminal { get; init; }
    }

    // ---- Pixel-tracing section extraction ---- //

    private static readonly (int Dx, int Dy)[] OrthoNeighbors = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    private static List<RiverCenterSegment>? TryBuildFromPixelTrace(RiverMap riverMap, byte[] rawData)
    {
        int w = riverMap.Width;
        int h = riverMap.Height;

        var specialPixels = new List<(int X, int Y, RiverPixelType Type)>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var t = (RiverPixelType)rawData[(y * w + x) * 2];
                if (t is RiverPixelType.Source or RiverPixelType.Confluence or RiverPixelType.Bifurcation)
                    specialPixels.Add((x, y, t));
            }

        if (specialPixels.Count == 0)
            return null;

        var visitedBlue = new bool[w, h];
        var blueComponents = new List<List<MaskCell>>();

        foreach (var (sx, sy, _) in specialPixels)
            visitedBlue[sx, sy] = true;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (visitedBlue[x, y]) continue;
                if (rawData[(y * w + x) * 2] != (byte)RiverPixelType.River) continue;

                var component = new List<MaskCell>();
                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue((x, y));
                visitedBlue[x, y] = true;

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    component.Add(new MaskCell(cx, cy));

                    foreach (var (dx, dy) in OrthoNeighbors)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if ((uint)nx >= w || (uint)ny >= h || visitedBlue[nx, ny]) continue;
                        if (rawData[(ny * w + nx) * 2] != (byte)RiverPixelType.River) continue;
                        visitedBlue[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
                }

                if (component.Count >= 2)
                    blueComponents.Add(component);
            }

        var segments = new List<RiverCenterSegment>();

        foreach (var component in blueComponents)
        {
            var coordSet = new HashSet<(int, int)>();
            foreach (var cell in component)
                coordSet.Add((cell.X, cell.Y));

            var endpoints = new List<MaskCell>();
            foreach (var cell in component)
            {
                int blueNeighborCount = 0;
                foreach (var (dx, dy) in OrthoNeighbors)
                    if (coordSet.Contains((cell.X + dx, cell.Y + dy)))
                        blueNeighborCount++;
                if (blueNeighborCount != 2)
                    endpoints.Add(cell);
            }

            if (endpoints.Count < 2) continue;

            var path = new List<MaskCell> { endpoints[0] };
            var visitedPath = new HashSet<(int, int)> { (endpoints[0].X, endpoints[0].Y) };

            while (path.Count > 0)
            {
                MaskCell current = path[^1];
                bool found = false;
                foreach (var (dx, dy) in OrthoNeighbors)
                {
                    int nx = current.X + dx, ny = current.Y + dy;
                    if (!coordSet.Contains((nx, ny)) || visitedPath.Contains((nx, ny)))
                        continue;
                    visitedPath.Add((nx, ny));
                    path.Add(new MaskCell(nx, ny));
                    found = true;
                    break;
                }
                if (!found) break;
            }

            if (path.Count < 2) continue;

            MaskCell ps = path[0], pe = path[^1];
            bool taperStart = true, taperEnd = true;

            foreach (var (sx, sy, st) in specialPixels)
            {
                if (Math.Abs(sx - ps.X) + Math.Abs(sy - ps.Y) == 1)
                    taperStart = st switch
                    {
                        RiverPixelType.Source => false,
                        RiverPixelType.Bifurcation => false,
                        _ => taperStart,
                    };
                if (Math.Abs(sx - pe.X) + Math.Abs(sy - pe.Y) == 1)
                    taperEnd = st switch
                    {
                        RiverPixelType.Source => false,
                        RiverPixelType.Bifurcation => false,
                        _ => taperEnd,
                    };
            }

            segments.Add(new RiverCenterSegment
            {
                Cells = path,
                StartNodeKey = -1,
                EndNodeKey = -1,
                StartTerminal = taperStart,
                EndTerminal = taperEnd,
                IsLoop = false,
                TaperStart = taperStart,
                TaperEnd = taperEnd,
            });
        }

        MergeThroughPairsAtConfluences(segments, specialPixels, rawData, w, h);

        return segments.Count > 0 ? segments : null;
    }

    private static void MergeThroughPairsAtConfluences(
        List<RiverCenterSegment> segments,
        List<(int X, int Y, RiverPixelType Type)> specialPixels,
        byte[] rawData, int w, int h)
    {
        foreach (var (sx, sy, type) in specialPixels)
        {
            if (type != RiverPixelType.Confluence) continue;

            var blueNeighbors = new List<(int X, int Y)>();
            foreach (var (dx, dy) in OrthoNeighbors)
            {
                int nx = sx + dx, ny = sy + dy;
                if ((uint)nx < w && (uint)ny < h
                    && rawData[(ny * w + nx) * 2] == (byte)RiverPixelType.River)
                    blueNeighbors.Add((nx, ny));
            }
            if (blueNeighbors.Count < 3) continue;

            var matchingSegs = new List<(int SegIdx, bool AtStart)>();
            foreach (var (nx, ny) in blueNeighbors)
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    var seg = segments[i];
                    if (seg.Cells.Count == 0) continue;
                    MaskCell first = seg.Cells[0];
                    MaskCell last = seg.Cells[^1];
                    if (first.X == nx && first.Y == ny) matchingSegs.Add((i, true));
                    else if (last.X == nx && last.Y == ny) matchingSegs.Add((i, false));
                }
            }

            if (matchingSegs.Count < 3) continue;

            var starts = matchingSegs.Where(m => m.AtStart).ToList();
            var ends = matchingSegs.Where(m => !m.AtStart).ToList();
            if (starts.Count == 0 || ends.Count == 0) continue;

            var throughPair = new[] { starts[0], ends[0] };
            var tributary = matchingSegs.First(m => m != throughPair[0] && m != throughPair[1]);

            var segA = segments[throughPair[0].SegIdx];
            var segB = segments[throughPair[1].SegIdx];

            var cellsA = new List<MaskCell>(segA.Cells);
            var cellsB = new List<MaskCell>(segB.Cells);

            if (throughPair[0].AtStart) cellsA.Reverse();
            if (!throughPair[1].AtStart) cellsB.Reverse();

            var merged = new List<MaskCell>(cellsA);
            merged.AddRange(cellsB);

            if (merged.Count >= 2)
            {
                var dedup = new List<MaskCell>(merged.Count) { merged[0] };
                for (int i = 1; i < merged.Count; i++)
                    if (merged[i].X != merged[i - 1].X || merged[i].Y != merged[i - 1].Y)
                        dedup.Add(merged[i]);
                merged = dedup;
            }

            segments[throughPair[0].SegIdx] = new RiverCenterSegment
            {
                Cells = merged,
                StartNodeKey = -1,
                EndNodeKey = -1,
                StartTerminal = false,
                EndTerminal = false,
                IsLoop = false,
                TaperStart = segA.TaperStart || segB.TaperStart,
                TaperEnd = segA.TaperEnd || segB.TaperEnd,
            };

            int removeIdx = throughPair[1].SegIdx;
            segments.RemoveAt(removeIdx);

            int tribIdx = tributary.SegIdx;
            if (tribIdx > removeIdx) tribIdx--;

            var tribSeg = segments[tribIdx];
            if (tributary.AtStart)
                tribSeg.TaperStart = true;
            else
                tribSeg.TaperEnd = true;
        }
    }
}
