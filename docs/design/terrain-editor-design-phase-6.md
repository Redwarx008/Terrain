# 阶段六：路径系统

## 概述

**目标：** 实现基于贝塞尔曲线的路径系统，支持道路和河流

**范围：**
- 贝塞尔曲线网络
- 曲线编辑器
- 道路网格生成
- 地形变形适配

**参考：**
- Godot MTerrain: MCurve曲线网络、MCurveMesh网格变形

## 架构设计

### 数据层次

```
PathManager (管理器)
    |
    +-- BezierCurveNetwork (曲线网络)
    |    +-- ControlPoints[] (控制点)
    |    +-- Segments[] (曲线段)
    |    +-- Adjacency (连接关系)
    |
    +-- PathConfig[] (路径配置)
    |    +-- RoadConfig
    |    +-- RiverConfig
    |
    +-- PathMeshBuilder (网格生成)
         +-- 沿曲线采样
         +-- 横截面变形
         +-- UV生成
```

### 编辑流程

```
BezierEditor (编辑器)
    |
    +-- 控制点拖拽
    +-- 切线调整
    +-- 连接类型选择
    |
    +-- PathDeformer (地形变形)
         +-- 预计算高度修改
         +-- 撤销/重做支持
```

## 核心数据结构

### BezierControlPoint

```csharp
public struct BezierControlPoint
{
    public Vector3 Position;
    public Vector3 InTangent;      // 入切线
    public Vector3 OutTangent;     // 出切线
    public ConnectionType InConnection;
    public ConnectionType OutConnection;
}

public enum ConnectionType
{
    None,       // 断开
    Linear,     // 线性连接
    Smooth,     // 平滑连接
    Symmetric   // 对称连接 (InOut镜像)
}
```

### BezierSegment

```csharp
public class BezierSegment
{
    public int Id { get; set; }
    public BezierControlPoint StartPoint;
    public BezierControlPoint EndPoint;
    public float Length { get; private set; }
    public int Resolution { get; set; } = 20;

    // 预计算的采样Transform
    private Matrix[]? sampleTransforms;
    public IReadOnlyList<Matrix> SampleTransforms => sampleTransforms;

    public void RecalculateSamples()
    {
        sampleTransforms = new Matrix[Resolution];
        for (int i = 0; i < Resolution; i++)
        {
            float t = (float)i / (Resolution - 1);
            sampleTransforms[i] = BezierSampler.SampleTransform(
                StartPoint.Position,
                StartPoint.Position + StartPoint.OutTangent,
                EndPoint.Position + EndPoint.InTangent,
                EndPoint.Position,
                t,
                Vector3.UnitY);
        }
        Length = CalculateLength();
    }
}
```

### BezierCurveNetwork

```csharp
public sealed class BezierCurveNetwork
{
    private readonly Dictionary<int, BezierControlPoint> controlPoints = new();
    private readonly Dictionary<int, BezierSegment> segments = new();
    private readonly Dictionary<int, HashSet<int>> adjacency = new();

    public IReadOnlyDictionary<int, BezierControlPoint> ControlPoints => controlPoints;
    public IReadOnlyDictionary<int, BezierSegment> Segments => segments;

    public int AddControlPoint(Vector3 position);
    public void RemoveControlPoint(int id);
    public void UpdateControlPoint(int id, Vector3 position);

    public int AddSegment(int startId, int endId);
    public void RemoveSegment(int id);
    public void ConnectSegments(int segmentIdA, int segmentIdB, ConnectionType type);

    public Matrix SampleTransform(float distance, int segmentId);
    public IEnumerable<Matrix> GetAllTransforms(float interval = 1.0f);
}
```

## 曲线采样算法

### BezierSampler

```csharp
public static class BezierSampler
{
    /// <summary>
    /// 三次贝塞尔曲线位置采样
    /// </summary>
    public static Vector3 SampleCubic(
        Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        float mt = 1 - t;
        float mt2 = mt * mt;
        float mt3 = mt2 * mt;

        return mt3 * p0 + 3 * mt2 * t * p1 + 3 * mt * t2 * p2 + t3 * p3;
    }

    /// <summary>
    /// 切线计算
    /// </summary>
    public static Vector3 SampleTangent(
        Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float mt = 1 - t;
        float mt2 = mt * mt;

        return 3 * mt2 * (p1 - p0) + 6 * mt * t * (p2 - p1) + 3 * t2 * (p3 - p2);
    }

    /// <summary>
    /// Frenet标架 (位置、切线、法线、副法线)
    /// </summary>
    public static (Vector3 Position, Vector3 Tangent, Vector3 Normal, Vector3 Binormal)
        SampleFrame(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t, Vector3 up)
    {
        var position = SampleCubic(p0, p1, p2, p3, t);
        var tangent = Vector3.Normalize(SampleTangent(p0, p1, p2, p3, t));
        var binormal = Vector3.Normalize(Vector3.Cross(up, tangent));
        var normal = Vector3.Normalize(Vector3.Cross(tangent, binormal));

        return (position, tangent, normal, binormal);
    }

    /// <summary>
    /// 构建Transform矩阵
    /// </summary>
    public static Matrix SampleTransform(
        Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t, Vector3 up)
    {
        var (position, tangent, normal, binormal) = SampleFrame(p0, p1, p2, p3, t, up);

        return new Matrix(
            new Vector4(tangent, 0),
            new Vector4(normal, 0),
            new Vector4(binormal, 0),
            new Vector4(position, 1));
    }
}
```

## 路径类型配置

### PathType

```csharp
public enum PathType
{
    Road,       // 道路 - 地形上方
    River,      // 河流 - 地形下方
    Bridge,     // 桥梁 - 离地悬空
    Railway     // 铁轨
}
```

### PathConfig

```csharp
public abstract class PathConfig
{
    public PathType Type { get; protected set; }
    public float Width { get; set; } = 10f;
    public float Thickness { get; set; } = 0.5f;
    public Material? SurfaceMaterial { get; set; }
    public Material? EdgeMaterial { get; set; }
}

public class RoadConfig : PathConfig
{
    public RoadConfig() => Type = PathType.Road;

    public float Elevation { get; set; } = 0.5f;    // 抬升高度
    public float SlopeAngle { get; set; } = 30f;    // 边坡角度
    public float BlendDistance { get; set; } = 2f;  // 混合距离
}

public class RiverConfig : PathConfig
{
    public RiverConfig() => Type = PathType.River;

    public float Depth { get; set; } = 5f;          // 河床深度
    public float BankWidth { get; set; } = 3f;      // 河岸宽度
    public float BankSlope { get; set; } = 45f;     // 河岸坡度
}
```

## 网格生成

### PathMeshBuilder

```csharp
public class PathMeshBuilder
{
    public Mesh BuildPathMesh(
        BezierCurveNetwork network,
        PathConfig config,
        int segmentResolution = 20)
    {
        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<int>();

        foreach (var segment in network.Segments.Values)
        {
            BuildSegmentMesh(segment, config, vertices, indices, segmentResolution);
        }

        return CreateMesh(vertices, indices);
    }

    private void BuildSegmentMesh(
        BezierSegment segment,
        PathConfig config,
        List<VertexPositionNormalTexture> vertices,
        List<int> indices,
        int resolution)
    {
        int baseIndex = vertices.Count;

        for (int i = 0; i < resolution; i++)
        {
            float t = (float)i / (resolution - 1);
            var transform = segment.SampleTransforms[i];

            // 生成横截面顶点
            BuildCrossSection(transform, config, vertices);
        }

        // 生成索引
        for (int i = 0; i < resolution - 1; i++)
        {
            int row = i * 2; // 假设简化为4个顶点的横截面
            int nextRow = (i + 1) * 2;

            // 两个三角形组成一个四边形
            indices.Add(baseIndex + row);
            indices.Add(baseIndex + row + 1);
            indices.Add(baseIndex + nextRow);

            indices.Add(baseIndex + row + 1);
            indices.Add(baseIndex + nextRow + 1);
            indices.Add(baseIndex + nextRow);
        }
    }

    private void BuildCrossSection(
        Matrix transform,
        PathConfig config,
        List<VertexPositionNormalTexture> vertices)
    {
        float halfWidth = config.Width / 2;

        // 左边缘
        var leftPos = Vector3.Transform(new Vector3(-halfWidth, 0, 0), transform);
        // 右边缘
        var rightPos = Vector3.Transform(new Vector3(halfWidth, 0, 0), transform);

        // ... 添加顶点
    }
}
```

## 地形变形 (CPU预计算)

### PathDeformer

```csharp
public class PathDeformer
{
    private readonly TerrainManager terrainManager;

    public PathDeformer(TerrainManager terrainManager)
    {
        this.terrainManager = terrainManager;
    }

    /// <summary>
    /// 应用路径变形到高度数据
    /// </summary>
    public void ApplyDeformation(
        BezierCurveNetwork network,
        PathConfig config)
    {
        var heightData = terrainManager.HeightDataCache;
        if (heightData == null) return;

        int width = terrainManager.HeightCacheWidth;
        int height = terrainManager.HeightCacheHeight;

        // 获取曲线采样点
        var transforms = network.GetAllTransforms(interval: 1.0f);

        // 对每个高度图像素
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var worldPos = PixelToWorldPosition(x, y);
                float distToPath = GetDistanceToPath(worldPos, transforms, config.Width);

                if (distToPath < config.Width + config.BlendDistance)
                {
                    int index = y * width + x;
                    heightData[index] = ComputeNewHeight(
                        heightData[index], distToPath, config);
                }
            }
        }

        // 同步到GPU
        terrainManager.UpdateHeightmapTexture();
    }

    private ushort ComputeNewHeight(ushort currentHeight, float distToPath, PathConfig config)
    {
        return config switch
        {
            RoadConfig road => ComputeRoadHeight(currentHeight, distToPath, road),
            RiverConfig river => ComputeRiverHeight(currentHeight, distToPath, river),
            _ => currentHeight
        };
    }

    private ushort ComputeRoadHeight(ushort currentHeight, float dist, RoadConfig config)
    {
        float halfWidth = config.Width / 2;

        if (dist < halfWidth)
        {
            // 在道路上 - 平坦化
            return (ushort)(currentHeight + config.Elevation * ushort.MaxValue / 100f);
        }
        else if (dist < halfWidth + config.BlendDistance)
        {
            // 混合区域 - 平滑过渡
            float blend = (dist - halfWidth) / config.BlendDistance;
            float slope = config.Elevation * (1 - blend);
            return (ushort)(currentHeight + slope * ushort.MaxValue / 100f);
        }

        return currentHeight;
    }

    private ushort ComputeRiverHeight(ushort currentHeight, float dist, RiverConfig config)
    {
        float halfWidth = config.Width / 2;

        if (dist < halfWidth)
        {
            // 在河道中 - 下陷
            return (ushort)Math.Max(0, currentHeight - config.Depth * ushort.MaxValue / 100f);
        }
        else if (dist < halfWidth + config.BankWidth)
        {
            // 河岸区域
            float bankFactor = (dist - halfWidth) / config.BankWidth;
            float depth = config.Depth * (1 - bankFactor);
            return (ushort)Math.Max(0, currentHeight - depth * ushort.MaxValue / 100f);
        }

        return currentHeight;
    }
}
```

## 编辑器集成

### BezierEditor

```csharp
public sealed class BezierEditor
{
    private readonly BezierCurveNetwork network = new();
    private int? selectedPointId;
    private int? selectedSegmentId;

    public BezierCurveNetwork Network => network;

    public void AddControlPoint(Vector3 position)
    {
        network.AddControlPoint(position);
    }

    public void AddSegment(int startId, int endId)
    {
        network.AddSegment(startId, endId);
    }

    public void UpdateSelectedPoint(Vector3 newPosition)
    {
        if (selectedPointId.HasValue)
        {
            network.UpdateControlPoint(selectedPointId.Value, newPosition);
        }
    }

    public void RenderGizmos(RenderDrawContext context)
    {
        // 绘制曲线
        foreach (var segment in network.Segments.Values)
        {
            DrawCurveSegment(context, segment);
        }

        // 绘制控制点
        foreach (var point in network.ControlPoints.Values)
        {
            DrawControlPoint(context, point);
        }

        // 绘制切线手柄
        if (selectedPointId.HasValue)
        {
            DrawTangentHandles(context, network.ControlPoints[selectedPointId.Value]);
        }
    }
}
```

## 文件清单

### 新建文件

| 文件路径 | 说明 |
|---------|------|
| `Terrain/Core/Path/BezierCurve.cs` | 曲线基础结构 |
| `Terrain/Core/Path/BezierSampler.cs` | 采样算法 |
| `Terrain/Core/Path/BezierCurveNetwork.cs` | 曲线网络 |
| `Terrain/Core/Path/PathConfig.cs` | 路径配置 |
| `Terrain/Core/Path/PathMeshBuilder.cs` | 网格生成 |
| `Terrain.Editor/Services/Path/BezierEditor.cs` | 曲线编辑器 |
| `Terrain.Editor/Services/Path/PathManager.cs` | 路径管理器 |
| `Terrain.Editor/Services/Path/PathDeformer.cs` | 地形变形 |
| `Terrain/Rendering/PathRenderFeature.cs` | 渲染特性 |
| `Terrain/Rendering/PathRenderObject.cs` | 渲染对象 |
| `Terrain.Editor/UI/Panels/PathPanel.cs` | 路径UI面板 |

## 实现步骤

### Phase 6.1: 曲线核心
1. 实现 `BezierSampler` 采样算法
2. 实现 `BezierControlPoint` 和 `BezierSegment`
3. 实现 `BezierCurveNetwork` 网络管理

### Phase 6.2: 曲线编辑器
1. 实现 `BezierEditor` 编辑逻辑
2. 实现控制点选择和拖拽
3. 实现切线手柄调整
4. 实现Gizmo渲染

### Phase 6.3: 网格生成
1. 实现 `PathMeshBuilder`
2. 实现横截面生成
3. 实现UV坐标计算
4. 支持不同路径类型

### Phase 6.4: 地形变形
1. 实现 `PathDeformer` CPU变形
2. 实现道路抬升
3. 实现河流凹陷
4. 实现撤销/重做

## 验证方案

1. **曲线编辑**：控制点拖拽流畅，切线调整正确
2. **网格生成**：道路网格沿曲线正确生成
3. **地形变形**：道路抬升、河流凹陷效果正确
4. **交叉支持**：T形和十字交叉正确处理
