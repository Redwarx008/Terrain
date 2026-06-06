# River Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a full river rendering stack with `RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature`, including half-resolution bottom/refraction rendering and full-resolution animated river surface rendering.

**Architecture:** River editing remains driven by the existing editor service and view model, but scene state moves into `RiverComponent`. `RiverProcessor` converts component mesh data into GPU render objects, and `RiverRenderFeature` owns the bottom/surface effects, render targets, pipeline states, and draw loop. Shader/resource names use neutral river terminology and do not contain external product names.

**Tech Stack:** C#/.NET, Stride Engine rendering APIs, SDSL shaders, Stride asset compiler, RenderDoc validation, existing `Terrain.Editor.Tests` console test harness.

---

## File Structure

Create or modify these primary files:

- Create: `Terrain.Editor/Rendering/River/RiverComponent.cs` — scene component storing CPU river mesh data, settings, visibility, and version.
- Create: `Terrain.Editor/Rendering/River/RiverRenderSettings.cs` — river shader/render default parameters.
- Create: `Terrain.Editor/Rendering/River/RiverMeshData.cs` — CPU mesh payload generated per river segment.
- Create: `Terrain.Editor/Rendering/River/RiverVertex.cs` — river vertex struct and `VertexDeclaration`.
- Create: `Terrain.Editor/Rendering/River/RiverRenderObject.cs` — GPU render object for one river segment draw.
- Create: `Terrain.Editor/Rendering/River/RiverProcessor.cs` — synchronizes `RiverComponent` versions into render objects.
- Create: `Terrain.Editor/Rendering/River/RiverRenderResources.cs` — half-resolution render target/depth ownership.
- Create: `Terrain.Editor/Rendering/River/RiverResourceLoader.cs` — loads neutral river texture resources.
- Create: `Terrain.Editor/Rendering/River/RiverRenderFeature.cs` — manual multi-pass river renderer.
- Modify: `Terrain.Editor/Services/RiverRenderingService.cs` — façade updates `RiverComponent` instead of creating primary `ModelComponent` render entities.
- Modify: `Terrain.Editor/Services/RiverMeshService.cs` — output `RiverMeshData`/`RiverVertex`.
- Modify: `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs` — ensure river feature/component/service integration.
- Modify: `Terrain.Editor/Rendering/RiverWireframeModeController.cs` — migrate or bridge wireframe behavior.
- Create: `Terrain.Editor/Effects/RiverVertexStreams.sdsl` — river vertex stream declarations.
- Create: `Terrain.Editor/Effects/RiverCommon.sdsl` — river depth/fade/refraction helpers.
- Create: `Terrain.Editor/Effects/RiverWaterCommon.sdsl` — water/foam/reflection helpers and neutral fallbacks.
- Modify: `Terrain.Editor/Effects/RiverBottom.sdsl` — full bottom pass shader.
- Modify: `Terrain.Editor/Effects/RiverSurface.sdsl` — full surface pass shader.
- Modify: `Terrain.Editor/Effects/RiverEffect.sdfx` — keep/adjust bottom/surface effect organization.
- Modify: `Terrain.Editor/Terrain.Editor.csproj` — include new shader files and generated key files.
- Check/modify: `Terrain.Editor/Terrain.Editor.sdpkg` — ensure `Assets`/`Effects` folders are included.
- Create: `Terrain.Editor/Assets/River/README.md` and resource directories.
- Modify: `Terrain.Editor.Tests/Program.cs` — add component/vertex/mesh tests.

---

## Task 1: River Component and Settings

**Files:**
- Create: `Terrain.Editor/Rendering/River/RiverRenderSettings.cs`
- Create: `Terrain.Editor/Rendering/River/RiverMeshData.cs`
- Create: `Terrain.Editor/Rendering/River/RiverComponent.cs`
- Test: `Terrain.Editor.Tests/Program.cs`

- [ ] **Step 1: Add a failing component version test**

Add a test method in `Terrain.Editor.Tests/Program.cs`:

```csharp
private static void TestRiverComponentVersioning()
{
    var component = new Terrain.Editor.Rendering.River.RiverComponent();
    int initial = component.Version;

    component.SetMeshes(new[]
    {
        new Terrain.Editor.Rendering.River.RiverMeshData
        {
            SegmentId = 1,
            Vertices = Array.Empty<Terrain.Editor.Rendering.River.RiverVertex>(),
            Indices = Array.Empty<int>(),
            BoundingBox = BoundingBox.Empty,
            BoundingSphere = BoundingSphere.Empty,
        }
    });

    Assert(component.Version == initial + 1, "RiverComponent.SetMeshes increments Version");
    Assert(component.Meshes.Count == 1, "RiverComponent stores meshes");

    component.Clear();

    Assert(component.Version == initial + 2, "RiverComponent.Clear increments Version");
    Assert(component.Meshes.Count == 0, "RiverComponent.Clear removes meshes");
}
```

Call it from the test runner's main test list.

- [ ] **Step 2: Run the test and verify it fails**

Run:

```bash
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj
```

Expected: compile failure because `RiverComponent`, `RiverMeshData`, and `RiverVertex` do not exist yet.

- [ ] **Step 3: Create `RiverRenderSettings.cs`**

Create `Terrain.Editor/Rendering/River/RiverRenderSettings.cs`:

```csharp
#nullable enable

using Stride.Core.Mathematics;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverRenderSettings
{
    public bool Visible { get; set; } = true;
    public bool ShowBottom { get; set; } = true;
    public bool ShowSurface { get; set; } = true;

    public float TextureUvScale { get; set; } = 1.0f;
    public float FlowNormalUvScale { get; set; } = 0.4f;
    public float FlowNormalSpeed { get; set; } = 0.075f;
    public float RiverFoamFactor { get; set; } = 0.5f;
    public float NoiseScale { get; set; } = 0.25f;
    public float NoiseSpeed { get; set; } = 2.0f;
    public float FlattenMultiplier { get; set; } = 1.0f;
    public float OceanFadeRate { get; set; } = 0.8f;
    public float BankAmount { get; set; } = 0.0f;
    public float BankFade { get; set; } = 0.02f;
    public float Depth { get; set; } = 0.15f;
    public float DepthWidthPower { get; set; } = 2.0f;
    public float DepthFakeFactor { get; set; } = 2.0f;
    public int ParallaxIterations { get; set; } = 10;

    public float FlatMapLerp { get; set; } = 0.0f;
    public float ZoomBlendOut { get; set; } = 1.0f;
    public float ShadowTermFallback { get; set; } = 1.0f;
    public float CloudMaskFallback { get; set; } = 0.0f;

    public Vector4 WaterColorShallow { get; set; } = new(0.0f, 0.3f, 0.5f, 0.7f);
    public Vector4 WaterColorDeep { get; set; } = new(0.0f, 0.05f, 0.15f, 0.85f);
}
```

- [ ] **Step 4: Create placeholder `RiverMeshData.cs` and `RiverVertex.cs` for component compilation**

Create `Terrain.Editor/Rendering/River/RiverVertex.cs` with the full implementation from Task 2 Step 3 if you are executing sequentially. If Task 2 has not been implemented yet, use this minimal compile-safe version and replace it in Task 2:

```csharp
#nullable enable

using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.River;

[StructLayout(LayoutKind.Sequential)]
public struct RiverVertex
{
    public Vector3 Position;
    public float Transparency;
    public Vector2 UV;
    public Vector3 Tangent;
    public Vector3 Normal;
    public float Width;
    public float DistanceToMain;

    public RiverVertex(Vector3 position, float transparency, Vector2 uv, Vector3 tangent, Vector3 normal, float width, float distanceToMain)
    {
        Position = position;
        Transparency = transparency;
        UV = uv;
        Tangent = tangent;
        Normal = normal;
        Width = width;
        DistanceToMain = distanceToMain;
    }

    public static readonly VertexDeclaration Layout = new(
        VertexElement.Position<Vector3>(),
        VertexElement.TextureCoordinate<float>(0),
        VertexElement.TextureCoordinate<Vector2>(1),
        VertexElement.TextureCoordinate<Vector3>(2),
        VertexElement.TextureCoordinate<Vector3>(3),
        VertexElement.TextureCoordinate<float>(4),
        VertexElement.TextureCoordinate<float>(5));
}
```

Create `Terrain.Editor/Rendering/River/RiverMeshData.cs`:

```csharp
#nullable enable

using Stride.Core.Mathematics;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverMeshData
{
    public int SegmentId { get; init; }
    public RiverVertex[] Vertices { get; init; } = Array.Empty<RiverVertex>();
    public int[] Indices { get; init; } = Array.Empty<int>();
    public BoundingBox BoundingBox { get; init; } = BoundingBox.Empty;
    public BoundingSphere BoundingSphere { get; init; } = BoundingSphere.Empty;
    public float WorldLength { get; init; }
    public float AvgHalfWidth { get; init; }
}
```

- [ ] **Step 5: Create `RiverComponent.cs`**

Create `Terrain.Editor/Rendering/River/RiverComponent.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using Stride.Engine;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverComponent : ActivableEntityComponent
{
    private IReadOnlyList<RiverMeshData> meshes = Array.Empty<RiverMeshData>();

    public IReadOnlyList<RiverMeshData> Meshes => meshes;
    public RiverRenderSettings Settings { get; } = new();
    public int Version { get; private set; }

    public void SetMeshes(IReadOnlyList<RiverMeshData> newMeshes)
    {
        meshes = newMeshes ?? throw new ArgumentNullException(nameof(newMeshes));
        Version++;
    }

    public void Clear()
    {
        meshes = Array.Empty<RiverMeshData>();
        Version++;
    }
}
```

- [ ] **Step 6: Run component tests**

Run:

```bash
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj
```

Expected: new component test passes or reveals namespace/import issues to fix.

- [ ] **Step 7: Commit component data model**

```bash
git add Terrain.Editor/Rendering/River/RiverComponent.cs Terrain.Editor/Rendering/River/RiverRenderSettings.cs Terrain.Editor/Rendering/River/RiverMeshData.cs Terrain.Editor/Rendering/River/RiverVertex.cs Terrain.Editor.Tests/Program.cs
git commit -m "feat: add river component data model"
```

---

## Task 2: River Vertex and Mesh Output

**Files:**
- Modify: `Terrain.Editor/Rendering/River/RiverVertex.cs`
- Modify: `Terrain.Editor/Services/RiverMeshService.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [ ] **Step 1: Add a vertex layout test**

In `Terrain.Editor.Tests/Program.cs`, add:

```csharp
private static void TestRiverVertexLayout()
{
    var elements = Terrain.Editor.Rendering.River.RiverVertex.Layout.VertexElements;
    Assert(elements.Length == 7, "RiverVertex has seven vertex elements");
    Assert(elements[0].SemanticName == "POSITION", "RiverVertex element 0 is POSITION");
    for (int i = 1; i < elements.Length; i++)
    {
        Assert(elements[i].SemanticName == "TEXCOORD", $"RiverVertex element {i} is TEXCOORD");
        Assert(elements[i].SemanticIndex == i - 1, $"RiverVertex element {i} has TEXCOORD{i - 1}");
    }
}
```

Call it from the test runner.

- [ ] **Step 2: Add a river vertex attribute generation test**

Add a test using an existing generated segment fixture or construct a `RiverSegment` with a two-point centerline:

```csharp
private static void TestRiverMeshProducesRiverVertexAttributes()
{
    var segment = new RiverSegment
    {
        SystemId = 7,
        Centerline = new List<Vector3>
        {
            new(0, 1, 0),
            new(10, 1, 0),
        },
        WorldLength = 10,
        AvgHalfWidth = 1,
    };

    var service = new RiverMeshService(null);
    var mesh = service.BuildRiverMesh(segment, widthScale: 1.0f, mapSize: new Vector2(100, 100));

    Assert(mesh.Vertices.Length == 4, "Two centerline points produce four river vertices");
    Assert(mesh.Indices.Length == 6, "Two centerline points produce two triangles");

    foreach (var vertex in mesh.Vertices)
    {
        Assert(MathF.Abs(vertex.Tangent.Length() - 1.0f) < 0.001f, "River tangent is normalized");
        Assert(MathF.Abs(vertex.Normal.Length() - 1.0f) < 0.001f, "River normal is normalized");
        Assert(vertex.Transparency == 1.0f, "River transparency defaults to 1");
        Assert(vertex.DistanceToMain >= 0.0f && vertex.DistanceToMain <= 1.0f, "DistanceToMain is normalized");
    }
}
```

- [ ] **Step 3: Run tests and verify failure**

```bash
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj
```

Expected: failure because `BuildRiverMesh` does not exist.

- [ ] **Step 4: Implement `BuildRiverMesh`**

In `Terrain.Editor/Services/RiverMeshService.cs`, keep existing `BuildRibbonMesh` during migration and add:

```csharp
public RiverMeshData BuildRiverMesh(RiverSegment segment, float widthScale, Vector2 mapSize)
{
    var centerline = segment.Centerline;
    if (centerline == null || centerline.Count < 2)
    {
        return new RiverMeshData { SegmentId = segment.SystemId };
    }

    float totalLength = segment.WorldLength > 0.001f ? segment.WorldLength : ComputePolylineLength(centerline);
    float baseHalfWidth = Math.Max(MinVisibleHalfWidth, segment.AvgHalfWidth * widthScale);
    float mapExtent = Math.Max(Math.Max(mapSize.X, mapSize.Y), 1.0f);
    float[] distances = ComputeDistances(centerline);

    var vertices = new List<RiverVertex>(centerline.Count * 2);
    var indices = new List<int>((centerline.Count - 1) * 6);
    var boundsMin = new Vector3(float.MaxValue);
    var boundsMax = new Vector3(float.MinValue);

    for (int i = 0; i < centerline.Count; i++)
    {
        Vector3 center = centerline[i];
        float u = totalLength > 0.001f ? distances[i] / totalLength : 0;
        float taperScale = ComputeTaperScale(u, totalLength, segment.TaperStart, segment.TaperEnd);
        float halfWidth = baseHalfWidth * taperScale;
        Vector3 offset = ComputeMiterOffset(centerline, i, halfWidth);
        Vector3 tangent = ComputeRiverTangent(centerline, i);
        Vector3 normal = SampleTerrainNormal(center.X, center.Z);
        if (normal.LengthSquared() <= 0.000001f)
            normal = Vector3.UnitY;
        normal.Normalize();

        float widthNormalized = halfWidth / mapExtent;
        float distanceToMain = ComputeDistanceToMain(u, segment.TaperStart, segment.TaperEnd);

        AddVertex(center - offset, new Vector2(u, 0), tangent, normal, widthNormalized, distanceToMain);
        AddVertex(center + offset, new Vector2(u, 1), tangent, normal, widthNormalized, distanceToMain);
    }

    for (int i = 0; i < centerline.Count - 1; i++)
    {
        int a = i * 2;
        int b = a + 1;
        int c = a + 2;
        int d = a + 3;
        indices.Add(a); indices.Add(c); indices.Add(b);
        indices.Add(b); indices.Add(c); indices.Add(d);
    }

    var boundingBox = new BoundingBox(boundsMin, boundsMax);
    return new RiverMeshData
    {
        SegmentId = segment.SystemId,
        Vertices = vertices.ToArray(),
        Indices = indices.ToArray(),
        BoundingBox = boundingBox,
        BoundingSphere = BoundingSphere.FromBox(boundingBox),
        WorldLength = totalLength,
        AvgHalfWidth = segment.AvgHalfWidth,
    };

    void AddVertex(Vector3 position, Vector2 uv, Vector3 tangent, Vector3 normal, float width, float distanceToMain)
    {
        vertices.Add(new RiverVertex(position, 1.0f, uv, tangent, normal, width, distanceToMain));
        boundsMin = Vector3.Min(boundsMin, position);
        boundsMax = Vector3.Max(boundsMax, position);
    }
}

private static Vector3 ComputeRiverTangent(List<Vector3> centerline, int index)
{
    Vector3 tangent;
    if (index == 0)
        tangent = centerline[1] - centerline[0];
    else if (index == centerline.Count - 1)
        tangent = centerline[^1] - centerline[^2];
    else
        tangent = centerline[index + 1] - centerline[index - 1];

    return tangent.LengthSquared() > 0.000001f ? Vector3.Normalize(tangent) : Vector3.UnitX;
}

private static float ComputeDistanceToMain(float u, bool taperStart, bool taperEnd)
{
    float value = 1.0f;
    if (taperStart)
        value = Math.Min(value, Math.Clamp(u * 10.0f, 0.0f, 1.0f));
    if (taperEnd)
        value = Math.Min(value, Math.Clamp((1.0f - u) * 10.0f, 0.0f, 1.0f));
    return value;
}
```

Add the required `using Terrain.Editor.Rendering.River;` and `using Stride.Core.Mathematics;` if missing.

- [ ] **Step 5: Run mesh tests**

```bash
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj
```

Expected: all mesh tests pass.

- [ ] **Step 6: Commit river vertex mesh output**

```bash
git add Terrain.Editor/Rendering/River/RiverVertex.cs Terrain.Editor/Services/RiverMeshService.cs Terrain.Editor.Tests/Program.cs
git commit -m "feat: generate river vertex mesh data"
```

---

## Task 3: River Processor and Render Objects

**Files:**
- Create: `Terrain.Editor/Rendering/River/RiverRenderObject.cs`
- Create: `Terrain.Editor/Rendering/River/RiverProcessor.cs`
- Modify: `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

- [ ] **Step 1: Create `RiverRenderObject.cs`**

```csharp
#nullable enable

using System;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverRenderObject : RenderObject, IDisposable
{
    public RiverComponent? SourceComponent { get; set; }
    public Buffer? VertexBuffer { get; set; }
    public Buffer? IndexBuffer { get; set; }
    public int IndexCount { get; set; }
    public int SegmentId { get; set; }
    public int SourceVersion { get; set; }
    public BoundingBox BoundingBox { get; set; } = BoundingBox.Empty;
    public BoundingSphere BoundingSphere { get; set; } = BoundingSphere.Empty;

    public void Dispose()
    {
        VertexBuffer?.Dispose();
        IndexBuffer?.Dispose();
        VertexBuffer = null;
        IndexBuffer = null;
    }
}
```

- [ ] **Step 2: Create `RiverProcessor.cs` with explicit update method**

Start with an explicit processor object that can be called from the game loop before integrating deeper into Stride processors:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using Stride.Graphics;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverProcessor : IDisposable
{
    private readonly GraphicsDevice graphicsDevice;
    private readonly List<RiverRenderObject> renderObjects = new();
    private RiverComponent? component;
    private int syncedVersion = -1;

    public RiverProcessor(GraphicsDevice graphicsDevice)
    {
        this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
    }

    public IReadOnlyList<RiverRenderObject> RenderObjects => renderObjects;

    public void SetComponent(RiverComponent? riverComponent)
    {
        if (component == riverComponent)
            return;

        ClearRenderObjects();
        component = riverComponent;
        syncedVersion = -1;
    }

    public void Update()
    {
        if (component == null)
        {
            ClearRenderObjects();
            syncedVersion = -1;
            return;
        }

        if (syncedVersion == component.Version)
            return;

        RebuildRenderObjects(component);
        syncedVersion = component.Version;
    }

    private void RebuildRenderObjects(RiverComponent source)
    {
        ClearRenderObjects();
        foreach (var mesh in source.Meshes)
        {
            if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0)
                continue;

            var vertexBuffer = Buffer.Vertex.New(graphicsDevice, mesh.Vertices, GraphicsResourceUsage.Dynamic);
            var indexBuffer = Buffer.Index.New(graphicsDevice, mesh.Indices);
            renderObjects.Add(new RiverRenderObject
            {
                SourceComponent = source,
                VertexBuffer = vertexBuffer,
                IndexBuffer = indexBuffer,
                IndexCount = mesh.Indices.Length,
                SegmentId = mesh.SegmentId,
                SourceVersion = source.Version,
                BoundingBox = mesh.BoundingBox,
                BoundingSphere = mesh.BoundingSphere,
            });
        }
    }

    private void ClearRenderObjects()
    {
        foreach (var renderObject in renderObjects)
            renderObject.Dispose();
        renderObjects.Clear();
    }

    public void Dispose()
    {
        ClearRenderObjects();
    }
}
```

- [ ] **Step 3: Wire processor into `EmbeddedStrideViewportGame` fields**

Add private/public members near existing river services:

```csharp
private RiverProcessor? riverProcessor;
public RiverProcessor? RiverProcessor => riverProcessor;
```

Initialize after `GraphicsDevice` is available:

```csharp
riverProcessor = new RiverProcessor(GraphicsDevice);
```

Dispose in `EndRun`:

```csharp
riverProcessor?.Dispose();
riverProcessor = null;
```

- [ ] **Step 4: Update processor each frame**

In the game `Update` path, after editor services have had a chance to change river data:

```csharp
riverProcessor?.Update();
```

- [ ] **Step 5: Commit processor/render object**

```bash
git add Terrain.Editor/Rendering/River/RiverRenderObject.cs Terrain.Editor/Rendering/River/RiverProcessor.cs Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs
git commit -m "feat: add river render object processor"
```

---

## Task 4: RiverRenderingService Component Bridge

**Files:**
- Modify: `Terrain.Editor/Services/RiverRenderingService.cs`
- Modify: `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

- [ ] **Step 1: Update constructor signature**

Change `RiverRenderingService` to accept and store a `RiverComponent`:

```csharp
private readonly RiverComponent riverComponent;

public RiverRenderingService(GraphicsDevice graphicsDevice, Scene scene, RiverComponent riverComponent)
{
    this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
    this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
    this.riverComponent = riverComponent ?? throw new ArgumentNullException(nameof(riverComponent));
}
```

- [ ] **Step 2: Replace `UpdateMeshes` implementation**

Replace entity/model creation with component mesh submission:

```csharp
public void UpdateMeshes(List<RiverSegment> segments, RiverMeshService meshService, float widthScale)
{
    var meshes = new List<RiverMeshData>();
    var mapSize = GetMapSize();

    foreach (var seg in segments)
    {
        var mesh = meshService.BuildRiverMesh(seg, widthScale, mapSize);
        if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0)
            continue;
        meshes.Add(mesh);
    }

    riverComponent.SetMeshes(meshes);
}

private Vector2 GetMapSize()
{
    // Use a safe default until map size is supplied from TerrainManager or project metadata.
    return new Vector2(4096, 4096);
}
```

Add `using Terrain.Editor.Rendering.River;`.

- [ ] **Step 3: Replace visibility and clear logic**

```csharp
public void SetVisible(bool visible)
{
    isVisible = visible;
    riverComponent.Enabled = visible;
    riverComponent.Settings.Visible = visible;
}

public void ClearMeshes()
{
    riverComponent.Clear();
}

public void Dispose()
{
    ClearMeshes();
}
```

Remove the old scene entity disposal loops only after the new path compiles. If retaining a temporary debug entity, guard it separately and keep it disabled in normal mode.

- [ ] **Step 4: Create river entity/component in `EmbeddedStrideViewportGame`**

Add fields:

```csharp
private Entity? riverEntity;
private RiverComponent? riverComponent;
```

During scene initialization:

```csharp
riverEntity = new Entity("RiverSystem");
riverComponent = new RiverComponent();
riverEntity.Add(riverComponent);
_scene!.Entities.Add(riverEntity);
riverProcessor?.SetComponent(riverComponent);
RiverRenderingService = new RiverRenderingService(GraphicsDevice, _scene!, riverComponent);
```

- [ ] **Step 5: Dispose river entity/component**

In shutdown:

```csharp
RiverRenderingService?.Dispose();
RiverRenderingService = null;
riverProcessor?.SetComponent(null);
if (riverEntity != null)
{
    riverEntity.Scene?.Entities.Remove(riverEntity);
    riverEntity.Dispose();
    riverEntity = null;
    riverComponent = null;
}
```

- [ ] **Step 6: Build**

```bash
dotnet build Terrain.sln -c Debug
```

Expected: build succeeds with existing warnings only.

- [ ] **Step 7: Commit service bridge**

```bash
git add Terrain.Editor/Services/RiverRenderingService.cs Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs
git commit -m "refactor: route river rendering service through component"
```

---

## Task 5: River Render Resources

**Files:**
- Create: `Terrain.Editor/Rendering/River/RiverRenderResources.cs`

- [ ] **Step 1: Create resource owner**

```csharp
#nullable enable

using System;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverRenderResources : IDisposable
{
    public Texture? BottomColor { get; private set; }
    public Texture? BottomDepth { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public void Ensure(GraphicsDevice graphicsDevice, int viewWidth, int viewHeight)
    {
        int width = Math.Max(1, (viewWidth + 1) / 2);
        int height = Math.Max(1, (viewHeight + 1) / 2);
        if (BottomColor != null && BottomDepth != null && Width == width && Height == height)
            return;

        DisposeTextures();
        Width = width;
        Height = height;

        BottomColor = Texture.New2D(
            graphicsDevice,
            width,
            height,
            PixelFormat.R16G16B16A16_Float,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        BottomDepth = Texture.New2D(
            graphicsDevice,
            width,
            height,
            PixelFormat.D24_UNorm_S8_UInt,
            TextureFlags.DepthStencil);
    }

    private void DisposeTextures()
    {
        BottomColor?.Dispose();
        BottomDepth?.Dispose();
        BottomColor = null;
        BottomDepth = null;
        Width = 0;
        Height = 0;
    }

    public void Dispose()
    {
        DisposeTextures();
    }
}
```

- [ ] **Step 2: Add compile check**

```bash
dotnet build Terrain.sln -c Debug
```

Expected: compile succeeds or flags exact Stride texture API adjustments to make.

- [ ] **Step 3: Commit resources**

```bash
git add Terrain.Editor/Rendering/River/RiverRenderResources.cs
git commit -m "feat: add river render target resources"
```

---

## Task 6: Shader Streams and Common Files

**Files:**
- Create: `Terrain.Editor/Effects/RiverVertexStreams.sdsl`
- Create: `Terrain.Editor/Effects/RiverCommon.sdsl`
- Create: `Terrain.Editor/Effects/RiverWaterCommon.sdsl`
- Modify: `Terrain.Editor/Terrain.Editor.csproj`

- [ ] **Step 1: Add `RiverVertexStreams.sdsl`**

```c
namespace Terrain.Editor
{
    shader RiverVertexStreams
    {
        stage stream float RiverTransparency : TEXCOORD0;
        stage stream float2 RiverUV : TEXCOORD1;
        stage stream float3 RiverTangent : TEXCOORD2;
        stage stream float3 RiverNormal : TEXCOORD3;
        stage stream float RiverWidth : TEXCOORD4;
        stage stream float RiverDistanceToMain : TEXCOORD5;
    }
}
```

- [ ] **Step 2: Add `RiverCommon.sdsl`**

```c
namespace Terrain.Editor
{
    shader RiverCommon
    {
        stage float _TextureUvScale = 1.0f;
        stage float _BankAmount = 0.0f;
        stage float _BankFade = 0.02f;
        stage float _Depth = 0.15f;
        stage float _DepthWidthPower = 2.0f;
        stage float _DepthFakeFactor = 2.0f;
        stage int _ParallaxIterations = 10;
        stage float2 MapSize = float2(4096.0f, 4096.0f);
        stage float GlobalTime = 0.0f;

        float CalcRiverDepth(float2 uv)
        {
            return _Depth * (1.0f - pow(cos(uv.y * 2.0f * 3.14159265f) * 0.5f + 0.5f, 2.0f));
        }

        float CompressRiverWorldSpace(float3 worldPosition)
        {
            return saturate(worldPosition.y / max(_DepthFakeFactor * 100.0f, 0.001f));
        }
    }
}
```

- [ ] **Step 3: Add `RiverWaterCommon.sdsl`**

```c
namespace Terrain.Editor
{
    shader RiverWaterCommon
    {
        stage float _FlowNormalUvScale = 0.4f;
        stage float _FlowNormalSpeed = 0.075f;
        stage float _RiverFoamFactor = 0.5f;
        stage float _NoiseScale = 0.25f;
        stage float _NoiseSpeed = 2.0f;
        stage float _FlattenMult = 1.0f;
        stage float _OceanFadeRate = 0.8f;
        stage float FlatMapLerp = 0.0f;
        stage float ZoomBlendOut = 1.0f;
        stage float ShadowTermFallback = 1.0f;
        stage float CloudMaskFallback = 0.0f;
        stage float4 WaterColorShallow = float4(0.0f, 0.3f, 0.5f, 0.7f);
        stage float4 WaterColorDeep = float4(0.0f, 0.05f, 0.15f, 0.85f);
    }
}
```

- [ ] **Step 4: Add project shader metadata**

In `Terrain.Editor/Terrain.Editor.csproj`, add entries matching existing river shader metadata for:

```xml
<None Update="Effects\RiverVertexStreams.sdsl">
  <Generator>StrideShaderKeyGenerator</Generator>
  <LastGenOutput>RiverVertexStreams.sdsl.cs</LastGenOutput>
</None>
<None Update="Effects\RiverCommon.sdsl">
  <Generator>StrideShaderKeyGenerator</Generator>
  <LastGenOutput>RiverCommon.sdsl.cs</LastGenOutput>
</None>
<None Update="Effects\RiverWaterCommon.sdsl">
  <Generator>StrideShaderKeyGenerator</Generator>
  <LastGenOutput>RiverWaterCommon.sdsl.cs</LastGenOutput>
</None>
<Compile Update="Effects\RiverVertexStreams.sdsl.cs">
  <DesignTime>True</DesignTime>
  <AutoGen>True</AutoGen>
  <DependentUpon>RiverVertexStreams.sdsl</DependentUpon>
</Compile>
<Compile Update="Effects\RiverCommon.sdsl.cs">
  <DesignTime>True</DesignTime>
  <AutoGen>True</AutoGen>
  <DependentUpon>RiverCommon.sdsl</DependentUpon>
</Compile>
<Compile Update="Effects\RiverWaterCommon.sdsl.cs">
  <DesignTime>True</DesignTime>
  <AutoGen>True</AutoGen>
  <DependentUpon>RiverWaterCommon.sdsl</DependentUpon>
</Compile>
```

Adapt to the exact style already used in the csproj.

- [ ] **Step 5: Run generated-file update**

```bash
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
```

Expected: generated `.sdsl.cs` files appear or existing generator warnings identify syntax to adjust.

- [ ] **Step 6: Commit shader common files**

```bash
git add Terrain.Editor/Effects/RiverVertexStreams.sdsl Terrain.Editor/Effects/RiverCommon.sdsl Terrain.Editor/Effects/RiverWaterCommon.sdsl Terrain.Editor/Terrain.Editor.csproj Terrain.Editor/Effects/*.sdsl.cs
git commit -m "feat: add river shader stream definitions"
```

---

## Task 7: River Bottom and Surface Shaders

**Files:**
- Modify: `Terrain.Editor/Effects/RiverBottom.sdsl`
- Modify: `Terrain.Editor/Effects/RiverSurface.sdsl`
- Modify: `Terrain.Editor/Effects/RiverEffect.sdfx`

- [ ] **Step 1: Rewrite `RiverBottom.sdsl` to use river streams**

Replace the placeholder body with a shader that includes river streams/common code and writes bottom output. If SDSL supports custom output structs, use dual-source output:

```c
namespace Terrain.Editor
{
    shader RiverBottom : ShaderBase, Transformation, PositionStream4, RiverVertexStreams, RiverCommon
    {
        stage Texture2D BottomDiffuseTexture;
        stage Texture2D BottomNormalTexture;
        stage Texture2D BottomPropertiesTexture;
        stage SamplerState LinearWrapSampler;

        stage override void VSMain()
        {
            base.VSMain();
        }

        stage override void PSMain()
        {
            float2 uv = streams.RiverUV;
            float depth = CalcRiverDepth(uv);
            float edgeFade1 = smoothstep(0.0f, _BankFade, uv.y);
            float edgeFade2 = smoothstep(0.0f, _BankFade, 1.0f - uv.y);
            float fadeToConnection = saturate((streams.RiverDistanceToMain - 0.6f * abs(uv.y - 0.5f)) * 5.0f);
            float alpha = streams.RiverTransparency * fadeToConnection * edgeFade1 * edgeFade2;

            float2 bottomUv = float2(uv.x * _TextureUvScale, uv.y);
            float4 diffuse = BottomDiffuseTexture.Sample(LinearWrapSampler, bottomUv);
            float3 worldPos = streams.PositionWS;
            worldPos.y -= depth * streams.RiverWidth * max(MapSize.x, MapSize.y);

            streams.ColorTarget = float4(diffuse.rgb, CompressRiverWorldSpace(worldPos));
            // Implement secondary source output after confirming Stride SDSL syntax for SV_Target0_SRC1.
        }
    }
}
```

If `streams.PositionWS` is unavailable, explicitly pass world position from VS to PS using the supported Stride stream for this shader.

- [ ] **Step 2: Verify dual-source SDSL syntax**

Search Stride shader examples for `SRC1` or secondary output support. If found, update `RiverBottom.sdsl` to emit the secondary blend target. If not found, document the exact compiler limitation and temporarily emit a second MRT target only to validate bottom/surface resource flow.

Expected target: bottom pass has primary compressed color output and secondary blend alpha output.

- [ ] **Step 3: Rewrite `RiverSurface.sdsl` to sample bottom/refraction texture**

Use river streams/common/water common:

```c
namespace Terrain.Editor
{
    shader RiverSurface : ShaderBase, Transformation, PositionStream4, RiverVertexStreams, RiverCommon, RiverWaterCommon
    {
        stage Texture2D RefractionTexture;
        stage Texture2D WaterColorTexture;
        stage Texture2D AmbientNormalTexture;
        stage Texture2D FlowNormalTexture;
        stage Texture2D FoamTexture;
        stage Texture2D FoamRampTexture;
        stage Texture2D FoamMapTexture;
        stage Texture2D FoamNoiseTexture;
        stage TextureCube ReflectionCubeMap;
        stage SamplerState LinearWrapSampler;
        stage SamplerState LinearClampSampler;

        stage override void VSMain()
        {
            base.VSMain();
        }

        stage override void PSMain()
        {
            float2 uv = streams.RiverUV;
            float depth = CalcRiverDepth(uv);
            float2 flowUv = uv.yx * float2(1.0f, -1.0f);
            flowUv *= float2(streams.RiverWidth * max(MapSize.x, MapSize.y), 1.0f) * _FlowNormalUvScale;
            flowUv.y += GlobalTime * _FlowNormalSpeed;
            float4 flowNormalSample = FlowNormalTexture.Sample(LinearWrapSampler, flowUv);

            float2 screenUv = streams.Position.xy / max(streams.Position.w, 0.0001f);
            screenUv = screenUv * 0.5f + 0.5f;
            float4 bottomColor = RefractionTexture.Sample(LinearClampSampler, screenUv);

            float depthFactor = saturate(depth / max(_Depth, 0.001f));
            float4 waterColor = lerp(WaterColorShallow, WaterColorDeep, depthFactor);
            float edgeFade1 = smoothstep(0.0f, _BankFade, uv.y);
            float edgeFade2 = smoothstep(0.0f, _BankFade, 1.0f - uv.y);
            float alpha = waterColor.a;
            alpha *= streams.RiverTransparency;
            alpha *= saturate((streams.RiverDistanceToMain - 0.1f) * 5.0f);
            alpha *= edgeFade1 * edgeFade2;
            alpha *= saturate(ZoomBlendOut);
            alpha *= 1.0f - saturate(FlatMapLerp);

            float foamMask = flowNormalSample.a * _RiverFoamFactor;
            float3 finalColor = lerp(bottomColor.rgb, waterColor.rgb, saturate(alpha));
            finalColor += foamMask * 0.05f;
            streams.ColorTarget = float4(finalColor, alpha);
        }
    }
}
```

Adjust `streams.Position` access to the actual Stride clip-position stream if the compiler reports a different name.

- [ ] **Step 4: Run shader generation and compile assets**

```bash
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug
```

Expected: shader compilation succeeds. If not, fix SDSL syntax while preserving required shader behavior.

- [ ] **Step 5: Commit river shader rewrite**

```bash
git add Terrain.Editor/Effects/RiverBottom.sdsl Terrain.Editor/Effects/RiverSurface.sdsl Terrain.Editor/Effects/RiverEffect.sdfx Terrain.Editor/Effects/*.sdsl.cs Terrain.Editor/Effects/*.sdfx.cs
git commit -m "feat: implement river bottom and surface shaders"
```

---

## Task 8: River Resources

**Files:**
- Create directories under `Terrain.Editor/Assets/River/`
- Create: `Terrain.Editor/Assets/River/README.md`
- Create: `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`
- Check/modify: `Terrain.Editor/Terrain.Editor.sdpkg`

- [ ] **Step 1: Create neutral resource directories**

Create:

```text
Terrain.Editor/Assets/River/Water/
Terrain.Editor/Assets/River/Bottom/
Terrain.Editor/Assets/River/Environment/
```

- [ ] **Step 2: Copy and rename required textures**

Copy source textures into:

```text
Terrain.Editor/Assets/River/Water/water_color.dds
Terrain.Editor/Assets/River/Water/ambient_normal.dds
Terrain.Editor/Assets/River/Water/flow_normal.dds
Terrain.Editor/Assets/River/Water/foam.dds
Terrain.Editor/Assets/River/Water/foam_ramp.dds
Terrain.Editor/Assets/River/Water/foam_map.dds
Terrain.Editor/Assets/River/Water/foam_noise.dds
Terrain.Editor/Assets/River/Bottom/bottom_diffuse.dds
Terrain.Editor/Assets/River/Bottom/bottom_normal.dds
Terrain.Editor/Assets/River/Bottom/bottom_properties.dds
Terrain.Editor/Assets/River/Environment/reflection_cube.dds
```

- [ ] **Step 3: Add `README.md`**

Create `Terrain.Editor/Assets/River/README.md`:

```markdown
# River Rendering Assets

These files are local reference textures used by the river renderer. File and directory names use neutral project terminology.

| Project file | Purpose | Source path |
|---|---|---|
| Water/water_color.dds | Water color/spec lookup | E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\water\watercolor_rgb_waterspec_a.dds |
| Water/ambient_normal.dds | Ambient water normal | E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\water\ambient_normal.dds |
| Water/flow_normal.dds | Flow normal animation | E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\water\flow_normal.dds |
| Water/foam.dds | Foam texture | E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\water\foam.dds |
| Water/foam_ramp.dds | Foam ramp lookup | E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\water\foam_ramp.dds |
| Water/foam_map.dds | Foam mask/map | E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\water\foam_map.dds |
| Water/foam_noise.dds | Foam noise | E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\water\foam_noise.dds |
| Bottom/bottom_diffuse.dds | River bed diffuse | E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\rivers\river_bottom_diffuse.dds |
| Bottom/bottom_normal.dds | River bed normal/depth | E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\rivers\river_bottom_normal.dds |
| Bottom/bottom_properties.dds | River bed material properties | E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\rivers\river_bottom_gloss.dds |
| Environment/reflection_cube.dds | Water reflection cubemap | E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\environment\cape_hill_8k_cube_specular.dds |
```

- [ ] **Step 4: Create `RiverResourceLoader.cs`**

```csharp
#nullable enable

using System;
using System.IO;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverResourceLoader : IDisposable
{
    private readonly GraphicsDevice graphicsDevice;
    private readonly string assetRoot;

    public Texture? WaterColor { get; private set; }
    public Texture? AmbientNormal { get; private set; }
    public Texture? FlowNormal { get; private set; }
    public Texture? Foam { get; private set; }
    public Texture? FoamRamp { get; private set; }
    public Texture? FoamMap { get; private set; }
    public Texture? FoamNoise { get; private set; }
    public Texture? BottomDiffuse { get; private set; }
    public Texture? BottomNormal { get; private set; }
    public Texture? BottomProperties { get; private set; }
    public Texture? ReflectionCube { get; private set; }

    public RiverResourceLoader(GraphicsDevice graphicsDevice, string assetRoot)
    {
        this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        this.assetRoot = assetRoot ?? throw new ArgumentNullException(nameof(assetRoot));
    }

    public void Load()
    {
        WaterColor = LoadTexture("Water/water_color.dds");
        AmbientNormal = LoadTexture("Water/ambient_normal.dds");
        FlowNormal = LoadTexture("Water/flow_normal.dds");
        Foam = LoadTexture("Water/foam.dds");
        FoamRamp = LoadTexture("Water/foam_ramp.dds");
        FoamMap = LoadTexture("Water/foam_map.dds");
        FoamNoise = LoadTexture("Water/foam_noise.dds");
        BottomDiffuse = LoadTexture("Bottom/bottom_diffuse.dds");
        BottomNormal = LoadTexture("Bottom/bottom_normal.dds");
        BottomProperties = LoadTexture("Bottom/bottom_properties.dds");
        ReflectionCube = LoadTexture("Environment/reflection_cube.dds");
    }

    private Texture LoadTexture(string relativePath)
    {
        string path = Path.Combine(assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            throw new FileNotFoundException($"River resource is missing: {path}", path);
        return Texture.Load(graphicsDevice, path);
    }

    public void Dispose()
    {
        WaterColor?.Dispose();
        AmbientNormal?.Dispose();
        FlowNormal?.Dispose();
        Foam?.Dispose();
        FoamRamp?.Dispose();
        FoamMap?.Dispose();
        FoamNoise?.Dispose();
        BottomDiffuse?.Dispose();
        BottomNormal?.Dispose();
        BottomProperties?.Dispose();
        ReflectionCube?.Dispose();
    }
}
```

- [ ] **Step 5: Commit resources**

```bash
git add Terrain.Editor/Assets/River Terrain.Editor/Rendering/River/RiverResourceLoader.cs Terrain.Editor/Terrain.Editor.sdpkg
git commit -m "feat: add river rendering resources"
```

---

## Task 9: RiverRenderFeature

**Files:**
- Create: `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- Modify: `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

- [ ] **Step 1: Create feature skeleton**

Create `RiverRenderFeature.cs` with constructor, resources, effects, and processor reference:

```csharp
#nullable enable

using System;
using Stride.Graphics;
using Stride.Rendering;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverRenderFeature : RootRenderFeature
{
    private readonly RiverRenderResources resources = new();
    private RiverProcessor? processor;
    private DynamicEffectInstance? bottomEffect;
    private DynamicEffectInstance? surfaceEffect;

    public void SetProcessor(RiverProcessor? riverProcessor)
    {
        processor = riverProcessor;
    }

    protected override void InitializeCore()
    {
        base.InitializeCore();
        bottomEffect = new DynamicEffectInstance("RiverBottom");
        bottomEffect.Initialize(Context.Services);
        surfaceEffect = new DynamicEffectInstance("RiverSurface");
        surfaceEffect.Initialize(Context.Services);
    }

    protected override void Destroy()
    {
        bottomEffect?.Dispose();
        surfaceEffect?.Dispose();
        resources.Dispose();
        base.Destroy();
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage)
    {
        if (processor == null || processor.RenderObjects.Count == 0)
            return;

        int width = context.CommandList.RenderTarget?.Width ?? 1;
        int height = context.CommandList.RenderTarget?.Height ?? 1;
        resources.Ensure(context.GraphicsDevice, width, height);

        DrawBottomPass(context, renderView);
        DrawSurfacePass(context, renderView);
    }

    private void DrawBottomPass(RenderDrawContext context, RenderView renderView)
    {
        if (bottomEffect == null || resources.BottomColor == null || resources.BottomDepth == null || processor == null)
            return;
        // Fill in pipeline/effect binding in later steps.
    }

    private void DrawSurfacePass(RenderDrawContext context, RenderView renderView)
    {
        if (surfaceEffect == null || resources.BottomColor == null || processor == null)
            return;
        // Fill in pipeline/effect binding in later steps.
    }
}
```

Adjust base class and method signatures to match the existing `TerrainRenderFeature`/Stride API if `RootRenderFeature` requires a different override shape.

- [ ] **Step 2: Implement buffer binding helper**

Add:

```csharp
private static void BindBuffers(RenderDrawContext context, RiverRenderObject renderObject)
{
    if (renderObject.VertexBuffer == null || renderObject.IndexBuffer == null)
        return;
    context.CommandList.SetVertexBuffer(0, renderObject.VertexBuffer, 0, RiverVertex.Layout.VertexStride);
    context.CommandList.SetIndexBuffer(renderObject.IndexBuffer, 0, true);
}
```

If `VertexStride` is not exposed on `VertexDeclaration`, use `Utilities.SizeOf<RiverVertex>()` or `Marshal.SizeOf<RiverVertex>()`.

- [ ] **Step 3: Implement bottom pass RT binding**

Inside `DrawBottomPass`:

```csharp
using (context.PushRenderTargetsAndRestore())
{
    context.CommandList.SetRenderTargetAndViewport(resources.BottomDepth, resources.BottomColor);
    context.CommandList.Clear(resources.BottomColor, Color4.Transparent);
    context.CommandList.Clear(resources.BottomDepth, DepthStencilClearOptions.DepthBuffer, 1.0f, 0);

    foreach (var renderObject in processor.RenderObjects)
    {
        if (renderObject.SourceComponent == null || !renderObject.SourceComponent.Enabled || !renderObject.SourceComponent.Settings.Visible || !renderObject.SourceComponent.Settings.ShowBottom)
            continue;

        BindBuffers(context, renderObject);
        bottomEffect.UpdateEffect(context.GraphicsDevice);
        bottomEffect.Apply(context.GraphicsContext);
        context.CommandList.DrawIndexed(renderObject.IndexCount);
    }
}
```

Then add proper pipeline state configuration in the next step.

- [ ] **Step 4: Configure bottom/surface pipeline states**

Use `MutablePipelineState` following Stride manual draw examples. Configure:

Bottom:
- input layout: `RiverVertex.Layout`
- primitive type: triangle list
- render output: captured half-res target
- blend: secondary source alpha / inverse secondary source alpha
- depth write disabled
- depth bias configurable
- cull none

Surface:
- input layout: `RiverVertex.Layout`
- primitive type: triangle list
- output captured current target
- blend: source alpha / inverse source alpha
- depth write disabled
- depth bias configurable
- cull none

If exact API fields differ, inspect `Terrain/Rendering/TerrainRenderFeature.cs` and `Stride.BepuPhysics.Debug/Effects/RenderFeatures/SinglePassWireframeRenderFeature.cs`, then implement equivalent state setup.

- [ ] **Step 5: Bind shader parameters**

Before `Apply`, set at least:

```csharp
bottomEffect.Parameters.Set(RiverCommonKeys.MapSize, new Vector2(4096, 4096));
bottomEffect.Parameters.Set(RiverCommonKeys.GlobalTime, (float)context.RenderContext.Time.Total.TotalSeconds);
surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionTexture, resources.BottomColor);
surfaceEffect.Parameters.Set(RiverCommonKeys.MapSize, new Vector2(4096, 4096));
surfaceEffect.Parameters.Set(RiverCommonKeys.GlobalTime, (float)context.RenderContext.Time.Total.TotalSeconds);
```

Adjust key class names to generated names.

- [ ] **Step 6: Implement surface pass loop**

```csharp
foreach (var renderObject in processor.RenderObjects)
{
    if (renderObject.SourceComponent == null || !renderObject.SourceComponent.Enabled || !renderObject.SourceComponent.Settings.Visible || !renderObject.SourceComponent.Settings.ShowSurface)
        continue;

    BindBuffers(context, renderObject);
    surfaceEffect.UpdateEffect(context.GraphicsDevice);
    surfaceEffect.Apply(context.GraphicsContext);
    context.CommandList.DrawIndexed(renderObject.IndexCount);
}
```

- [ ] **Step 7: Build**

```bash
dotnet build Terrain.sln -c Debug
```

Expected: compile succeeds or points to exact Stride API adjustments.

- [ ] **Step 8: Commit render feature**

```bash
git add Terrain.Editor/Rendering/River/RiverRenderFeature.cs
git commit -m "feat: add river render feature"
```

---

## Task 10: Viewport Integration

**Files:**
- Modify: `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

- [ ] **Step 1: Add river feature field**

```csharp
private RiverRenderFeature? riverRenderFeature;
```

- [ ] **Step 2: Ensure feature registration**

Add an `EnsureRiverRenderFeature(GraphicsCompositor compositor)` method mirroring existing terrain/decal ensure methods:

```csharp
private void EnsureRiverRenderFeature(GraphicsCompositor compositor)
{
    var renderFeatures = compositor.RenderFeatures;
    riverRenderFeature = renderFeatures.OfType<RiverRenderFeature>().FirstOrDefault();
    if (riverRenderFeature == null)
    {
        riverRenderFeature = new RiverRenderFeature();
        renderFeatures.Add(riverRenderFeature);
    }
    riverRenderFeature.SetProcessor(riverProcessor);
}
```

Add required `using Terrain.Editor.Rendering.River;` and `using System.Linq;` if missing.

- [ ] **Step 3: Call ensure method during scene initialization**

After existing feature ensure calls:

```csharp
EnsureRiverRenderFeature(_graphicsCompositor);
```

- [ ] **Step 4: Connect service/component/processor**

Ensure initialization order is:

```text
create scene
create compositor
create river processor
create river entity/component
processor.SetComponent(component)
create RiverRenderingService(graphicsDevice, scene, component)
ensure river render feature
riverRenderFeature.SetProcessor(processor)
```

- [ ] **Step 5: Run build and editor smoke test**

```bash
dotnet build Terrain.sln -c Debug
```

Then run the editor using the existing project launch command. If none is documented, use the main editor project command:

```bash
dotnet run --project Terrain.Editor/Terrain.Editor.csproj -c Debug
```

Expected: editor starts; terrain and existing UI still work.

- [ ] **Step 6: Commit viewport integration**

```bash
git add Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs
git commit -m "feat: integrate river render feature"
```

---

## Task 11: Wireframe and Debug

**Files:**
- Modify: `Terrain.Editor/Rendering/RiverWireframeModeController.cs`
- Modify: `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

- [ ] **Step 1: Add debug mode enum**

Create `Terrain.Editor/Rendering/River/RiverDebugMode.cs`:

```csharp
namespace Terrain.Editor.Rendering.River;

public enum RiverDebugMode
{
    None,
    Wireframe,
    BottomOnly,
    SurfaceOnly,
}
```

- [ ] **Step 2: Add debug mode property to render feature**

```csharp
public RiverDebugMode DebugMode { get; set; }
```

In draw:

```csharp
if (DebugMode != RiverDebugMode.SurfaceOnly)
    DrawBottomPass(context, renderView);
if (DebugMode != RiverDebugMode.BottomOnly)
    DrawSurfacePass(context, renderView);
```

- [ ] **Step 3: Bridge existing wireframe controller**

In `RiverWireframeModeController`, if mesh-selector route no longer applies, set:

```csharp
riverRenderFeature.DebugMode = sceneViewMode == SceneViewMode.Wireframe
    ? RiverDebugMode.Wireframe
    : RiverDebugMode.None;
```

Pass `RiverRenderFeature` into the controller or expose a method from `EmbeddedStrideViewportGame`.

- [ ] **Step 4: Implement wireframe rasterizer state**

When `DebugMode == RiverDebugMode.Wireframe`, configure surface/bottom rasterizer fill mode as wireframe or add a dedicated debug pipeline.

- [ ] **Step 5: Manual verify wireframe**

Run editor, generate rivers, switch to wireframe mode. Expected: river mesh is inspectable and no duplicate normal-mode rendering appears.

- [ ] **Step 6: Commit debug mode**

```bash
git add Terrain.Editor/Rendering/River/RiverDebugMode.cs Terrain.Editor/Rendering/River/RiverRenderFeature.cs Terrain.Editor/Rendering/RiverWireframeModeController.cs
git commit -m "feat: add river render debug modes"
```

---

## Task 12: Verification and Documentation

**Files:**
- Modify: `docs/ARCHITECTURE_OVERVIEW.md`
- Modify: `docs/CURRENT_FEATURES.md`
- Create: `docs/log/YYYY/MM/DD/YYYY-MM-DD-*-river-rendering.md`
- Optional create: `docs/log/decisions/adr-xxx-river-render-feature.md`

- [ ] **Step 1: Run tests**

```bash
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj
```

Expected: all river mesh/component tests pass.

- [ ] **Step 2: Run shader workflow**

```bash
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug
```

Expected: asset compilation succeeds. Report any shader warnings/errors exactly.

- [ ] **Step 3: Build solution**

```bash
dotnet build Terrain.sln -c Debug
```

Expected: build succeeds. Existing warnings may remain; new errors must be fixed.

- [ ] **Step 4: Manual editor verification**

Run:

```bash
dotnet run --project Terrain.Editor/Terrain.Editor.csproj -c Debug
```

Manual steps:
1. Open or create a terrain project.
2. Load river map.
3. Click Generate.
4. Toggle Show Rivers off/on.
5. Switch wireframe mode.

Expected:
- Rivers render visibly.
- Toggle hides/shows rivers.
- Wireframe/debug mode remains useful.
- Terrain and brush/decal behavior remain functional.

- [ ] **Step 5: RenderDoc verification**

Capture a frame and check:

Bottom pass:
- half-resolution render target
- river draw count matching generated segments or documented grouping
- vertex input `POSITION + TEXCOORD0..5`
- dual-source output or documented temporary fallback
- src1 alpha / inverse src1 alpha blend
- depth write disabled
- bottom diffuse/normal/properties bound

Surface pass:
- full-resolution target
- bottom/refraction target bound as shader resource
- water color, ambient normal, flow normal, foam, reflection resources bound
- src alpha / inverse src alpha blend
- depth write disabled
- flow normal animation and edge fade visible

- [ ] **Step 6: Update architecture docs**

In `docs/ARCHITECTURE_OVERVIEW.md`, update Rendering layer status and key files to include `RiverRenderFeature`, `RiverComponent`, and shader resources.

In `docs/CURRENT_FEATURES.md`, update river rendering status from placeholder/simplified to completed or in-progress according to verification results.

- [ ] **Step 7: Create session log**

Use `docs/log/TEMPLATE.md` and create:

```text
docs/log/2026/06/06/2026-06-06-1-river-rendering.md
```

Include:
- what changed
- verification commands
- RenderDoc findings
- remaining risks
- next steps

- [ ] **Step 8: Create ADR if stable**

If the render feature architecture is stable, create:

```text
docs/log/decisions/adr-014-river-render-feature.md
```

Use neutral naming. Include decision: `RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature`, dual-source bottom pass, and neutral fallback global inputs.

- [ ] **Step 9: Final code review**

Run a code review agent or equivalent review over the final diff. Fix blocking issues and rerun affected tests/build.

- [ ] **Step 10: Final commit**

```bash
git status --short
git add <changed-files>
git commit -m "feat: implement river render feature"
```

Do not add Claude attribution or co-author lines.

---

## Self-Review

Spec coverage:
- `river-component-rendering`: Tasks 1, 3, 4, 10, 12 cover component, service compatibility, processor sync, visibility, clear.
- `river-vertex-mesh`: Task 2 covers vertex layout and mesh output.
- `river-multipass-rendering`: Tasks 5, 7, 9, 10, 12 cover half-res bottom pass, dual-source, surface pass, render states, RenderDoc checks.
- `river-shader-resources`: Tasks 6, 7, 8, 12 cover shader streams, bottom/surface behavior, neutral fallback, resources, asset workflow.

Known implementation hot spots:
- Exact Stride API signatures for `RootRenderFeature.Draw`, `MutablePipelineState`, and SDSL secondary source output must be checked against engine source during implementation.
- `CompressRiverWorldSpace` starts with a project-local equivalent; if the reference function is located later, replace the helper while preserving its interface.
- Texture loading may switch from direct DDS file loading to asset-pipeline loading if Stride content import is preferable.
