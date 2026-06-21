# River Width Gradient Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add configurable river min/max full-width settings and preserve local `rivers.png` palette width gradients during river mesh generation.

**Architecture:** `RuntimeMapDefinition` owns the authoring/runtime map settings. Editor bootstrap stores the width range on `TerrainManager`, `RiverViewModel` passes it into `RiverMeshGenerator`, and `RiverMapService` maps palette indices into local half-width samples. `RiverMeshService` carries those local width samples through centerline smoothing/interpolation and uses them per vertex.

**Tech Stack:** C#/.NET 10, Tommy TOML parser, existing `Terrain.Editor.Tests` executable harness, Stride math types.

---

## File Structure

- Modify `Terrain/Resources/RuntimeMapDefinition.cs`: add `RiverMinWidth` and `RiverMaxWidth` defaults.
- Modify `Terrain/Resources/RuntimeMapDefinitionReader.cs`: allow and read optional `river_min_width` / `river_max_width` settings.
- Modify `Terrain.Editor/Services/Resources/MapDefinitionWriter.cs`: write the new settings and validate them.
- Modify `Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs`: scaffold defaults.
- Modify `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`: preserve width settings during save.
- Modify `game/map/default.toml`: add explicit defaults.
- Modify `Terrain.Editor/Services/IRiverMapSource.cs`: expose `RiverMinWidth` / `RiverMaxWidth`.
- Modify `Terrain.Editor/Services/TerrainManager.cs`: store width settings from `EditorResourceSession`.
- Modify `Terrain.Editor/ViewModels/RiverViewModel.cs`: pass width settings to mesh generation.
- Modify `Terrain.Editor/Services/IRiverMeshGenerator.cs`: update `Generate` signature to include min/max full width.
- Modify `Terrain.Editor/Services/RiverMeshGenerator.cs`: pass min/max full width into `RiverMapService`.
- Modify `Terrain.Editor/Services/RiverMapService.cs`: map palette indices to configured local half-widths and store samples on segments.
- Modify `Terrain.Editor/Models/RiverPixelType.cs`: expose palette count and normalized width factor instead of hardcoded world-space half-widths.
- Modify `Terrain.Editor/Models/RiverSegment.cs`: add width samples aligned with cells/centerline.
- Modify `Terrain.Editor/Services/RiverMeshService.cs`: carry local widths through centerline processing and consume per-point width in mesh generation.
- Modify `Terrain.Editor.Tests/Program.cs`, `Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs`, and `Terrain.Editor.Tests/RiverViewModelAutoGenerationTests.cs`: add regression tests.
- Modify `docs/ARCHITECTURE_OVERVIEW.md` and `docs/CURRENT_FEATURES.md`: document new width semantics after implementation.
- Add `docs/log/2026/06/21/2026-06-21-river-width-gradient-implementation.md`: session completion log.

---

### Task 1: Map Definition Settings

**Files:**
- Modify: `Terrain/Resources/RuntimeMapDefinition.cs`
- Modify: `Terrain/Resources/RuntimeMapDefinitionReader.cs`
- Modify: `Terrain.Editor/Services/Resources/MapDefinitionWriter.cs`
- Modify: `Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs`
- Modify: `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`
- Modify: `Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs`

- [ ] **Step 1: Write failing reader/writer tests**

Add the following tests to `EditorResourceWriterTests.RunAll()`:

```csharp
TestHarness.Run("map definition reader defaults river width range", MapDefinitionReaderDefaultsRiverWidthRange);
TestHarness.Run("map definition reader reads explicit river width range", MapDefinitionReaderReadsExplicitRiverWidthRange);
TestHarness.Run("map definition reader rejects invalid river width range", MapDefinitionReaderRejectsInvalidRiverWidthRange);
```

Add these methods to `EditorResourceWriterTests`:

```csharp
private static void MapDefinitionReaderDefaultsRiverWidthRange()
{
    string root = CreateWorkspace();
    string output = Path.Combine(root, "mod", "map", "default.toml");
    WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
""");

    RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);

    TestHarness.AssertEqual(1.0f, map.RiverMinWidth, "default river min full width");
    TestHarness.AssertEqual(4.0f, map.RiverMaxWidth, "default river max full width");
}

private static void MapDefinitionReaderReadsExplicitRiverWidthRange()
{
    string root = CreateWorkspace();
    string output = Path.Combine(root, "mod", "map", "default.toml");
    WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
river_min_width = 2
river_max_width = 6
""");

    RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);

    TestHarness.AssertEqual(2.0f, map.RiverMinWidth, "explicit river min full width");
    TestHarness.AssertEqual(6.0f, map.RiverMaxWidth, "explicit river max full width");
}

private static void MapDefinitionReaderRejectsInvalidRiverWidthRange()
{
    string root = CreateWorkspace();
    string output = Path.Combine(root, "mod", "map", "default.toml");
    WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
river_min_width = 5
river_max_width = 4
""");

    TestHarness.AssertThrows<InvalidDataException>(
        () => RuntimeMapDefinitionReader.ReadFrom(output),
        "river_max_width below river_min_width should be rejected");
}
```

Update `MapDefinitionWriterPreservesMapDataEntriesAndHeightScale` to set and assert:

```csharp
RiverMinWidth = 2.0f,
RiverMaxWidth = 6.0f,
```

and after readback:

```csharp
TestHarness.AssertEqual(2.0f, map.RiverMinWidth, "river min full width");
TestHarness.AssertEqual(6.0f, map.RiverMaxWidth, "river max full width");
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: build fails because `RuntimeMapDefinition.RiverMinWidth` and `RiverMaxWidth` do not exist.

- [ ] **Step 3: Implement map definition settings**

In `RuntimeMapDefinition.cs`, add:

```csharp
public float RiverMinWidth { get; init; } = 1.0f;
public float RiverMaxWidth { get; init; } = 4.0f;
```

In `RuntimeMapDefinitionReader.ReadFrom`, update settings whitelist:

```csharp
ValidateTableKeys(settings, filePath, "settings", "height_scale", "river_min_width", "river_max_width");
```

After `heightScale` validation, add:

```csharp
float riverMinWidth = ReadOptionalFloat(settings, "river_min_width", filePath, 1.0f);
float riverMaxWidth = ReadOptionalFloat(settings, "river_max_width", filePath, 4.0f);
if (riverMinWidth <= 0.0f)
    throw new InvalidDataException($"river_min_width must be greater than 0: {filePath}");
if (riverMaxWidth < riverMinWidth)
    throw new InvalidDataException($"river_max_width must be greater than or equal to river_min_width: {filePath}");
```

Add to the returned object:

```csharp
RiverMinWidth = riverMinWidth,
RiverMaxWidth = riverMaxWidth,
```

Add this helper next to `RequireFloat`:

```csharp
private static float ReadOptionalFloat(TomlNode node, string key, string filePath, float fallback)
{
    if (!node.HasKey(key))
        return fallback;

    TomlNode value = node[key];
    if (value.IsFloat)
        return (float)value.AsFloat.Value;
    if (value.IsInteger)
        return (float)value.AsInteger.Value;

    throw new InvalidDataException($"TOML value '{key}' must be numeric: {filePath}");
}
```

In `MapDefinitionWriter.Write`, validate:

```csharp
if (mapDefinition.RiverMinWidth <= 0.0f)
    throw new InvalidDataException("Map definition river_min_width must be greater than 0.");
if (mapDefinition.RiverMaxWidth < mapDefinition.RiverMinWidth)
    throw new InvalidDataException("Map definition river_max_width must be greater than or equal to river_min_width.");
```

Write settings as:

```csharp
["settings"] = new TomlTable
{
    ["height_scale"] = mapDefinition.HeightScale,
    ["river_min_width"] = mapDefinition.RiverMinWidth,
    ["river_max_width"] = mapDefinition.RiverMaxWidth,
},
```

In `EditorMapDataScaffoldService.EnsureMapDefinition`, include:

```csharp
RiverMinWidth = 1.0f,
RiverMaxWidth = 4.0f,
```

In `EditorResourceSaveService.Save`, copy from session model:

```csharp
RiverMinWidth = session.MapDefinitionModel.RiverMinWidth,
RiverMaxWidth = session.MapDefinitionModel.RiverMaxWidth,
```

- [ ] **Step 4: Run tests to verify pass**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: PASS for the new map definition tests.

- [ ] **Step 5: Commit**

```powershell
git add -- Terrain/Resources/RuntimeMapDefinition.cs Terrain/Resources/RuntimeMapDefinitionReader.cs Terrain.Editor/Services/Resources/MapDefinitionWriter.cs Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs Terrain.Editor/Services/Resources/EditorResourceSaveService.cs Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs
git commit -m "Add river width settings to map definition"
```

---

### Task 2: Propagate Width Settings to River Generation

**Files:**
- Modify: `Terrain.Editor/Services/IRiverMapSource.cs`
- Modify: `Terrain.Editor/Services/TerrainManager.cs`
- Modify: `Terrain.Editor/ViewModels/RiverViewModel.cs`
- Modify: `Terrain.Editor/Services/IRiverMeshGenerator.cs`
- Modify: `Terrain.Editor/Services/RiverMeshGenerator.cs`
- Modify: `Terrain.Editor.Tests/RiverViewModelAutoGenerationTests.cs`

- [ ] **Step 1: Write failing view model propagation test**

In `RiverViewModelAutoGenerationTests`, update `FakeRiverMapSource` to include:

```csharp
public float RiverMinWidth { get; set; } = 1.0f;
public float RiverMaxWidth { get; set; } = 4.0f;
```

Update the fake generator with:

```csharp
public float LastRiverMinWidth { get; private set; }
public float LastRiverMaxWidth { get; private set; }
```

Change its `Generate` signature in the test fake to:

```csharp
public RiverGenerationResult? Generate(RiverCell[,] cells, float widthScale, float riverMinWidth, float riverMaxWidth)
{
    GenerateCalls++;
    LastWidth = cells.GetLength(0);
    LastHeight = cells.GetLength(1);
    LastWidthScale = widthScale;
    LastRiverMinWidth = riverMinWidth;
    LastRiverMaxWidth = riverMaxWidth;
    return Result;
}
```

Add to `RunAll()`:

```csharp
TestHarness.Run("river view model passes map width range to generator", PassesMapWidthRangeToGenerator);
```

Add:

```csharp
private static void PassesMapWidthRangeToGenerator()
{
    var source = new FakeRiverMapSource(new RiverCell[2, 2], "map/rivers.png")
    {
        RiverMinWidth = 2.0f,
        RiverMaxWidth = 6.0f,
    };
    var generator = new FakeGenerator();
    var viewModel = new RiverViewModel(source);

    viewModel.SetGenerator(generator);

    TestHarness.AssertEqual(2.0f, generator.LastRiverMinWidth, "generator min full width");
    TestHarness.AssertEqual(6.0f, generator.LastRiverMaxWidth, "generator max full width");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: compile fails because `IRiverMapSource` and `IRiverMeshGenerator` do not expose these values.

- [ ] **Step 3: Implement propagation**

In `IRiverMapSource.cs`, add:

```csharp
float RiverMinWidth { get; }
float RiverMaxWidth { get; }
```

In `TerrainManager`, add public properties:

```csharp
public float RiverMinWidth { get; private set; } = 1.0f;
public float RiverMaxWidth { get; private set; } = 4.0f;
```

In `LoadFromResourceSession`, after height scale:

```csharp
RiverMinWidth = session.MapDefinitionModel.RiverMinWidth;
RiverMaxWidth = session.MapDefinitionModel.RiverMaxWidth;
```

In `IRiverMeshGenerator.cs`, change the method to:

```csharp
RiverGenerationResult? Generate(RiverCell[,] cells, float widthScale, float riverMinWidth, float riverMaxWidth);
```

In `RiverViewModel.TryGenerateLoadedRiverMesh`, call:

```csharp
RiverGenerationResult? result = generator.Generate(
    cells,
    (float)WidthScale,
    riverMapSource.RiverMinWidth,
    riverMapSource.RiverMaxWidth);
```

In `RiverMeshGenerator.Generate`, update the signature and create map service as:

```csharp
public RiverGenerationResult? Generate(RiverCell[,] cells, float widthScale, float riverMinWidth, float riverMaxWidth)
{
    ArgumentNullException.ThrowIfNull(cells);

    var mapService = new RiverMapService(riverMinWidth, riverMaxWidth);
    mapService.Load(cells);
    ...
}
```

- [ ] **Step 4: Run tests to verify pass**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: PASS for river view model tests.

- [ ] **Step 5: Commit**

```powershell
git add -- Terrain.Editor/Services/IRiverMapSource.cs Terrain.Editor/Services/TerrainManager.cs Terrain.Editor/ViewModels/RiverViewModel.cs Terrain.Editor/Services/IRiverMeshGenerator.cs Terrain.Editor/Services/RiverMeshGenerator.cs Terrain.Editor.Tests/RiverViewModelAutoGenerationTests.cs
git commit -m "Pass river width range into generation"
```

---

### Task 3: Palette Index to Configured Width Mapping

**Files:**
- Modify: `Terrain.Editor/Models/RiverPixelType.cs`
- Modify: `Terrain.Editor/Models/RiverSegment.cs`
- Modify: `Terrain.Editor/Services/RiverMapService.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [ ] **Step 1: Write failing palette mapping test**

Add to `Program.cs` after the existing average width test registration:

```csharp
Run("river palette maps to configured local width samples", RiverPaletteMapsToConfiguredLocalWidthSamples);
```

Add:

```csharp
void RiverPaletteMapsToConfiguredLocalWidthSamples()
{
    var path = Path.Combine(tempDir, "width-gradient.png");
    using (var image = new Image<Rgba32>(5, 3))
    {
        image[0, 1] = new Rgba32(0, 255, 0);
        image[1, 1] = new Rgba32(0x00, 0xe1, 0xff);
        image[2, 1] = new Rgba32(0x00, 0x00, 0xff);
        image[3, 1] = new Rgba32(0x18, 0xce, 0x00);
        image[4, 1] = new Rgba32(255, 0, 0);
        image.Save(path);
    }

    var service = new RiverMapService(riverMinWidth: 1.0f, riverMaxWidth: 4.0f);
    Assert(service.Load(path), "river map should load");
    var segments = service.ExtractSegments();

    AssertEqual(1, segments.Count, "segment count");
    var samples = segments[0].CellHalfWidths;
    AssertEqual(5, samples.Count, "width samples include semantic endpoint cells");
    AssertNearlyEqual(0.5f, samples[0], 0.0001f, "source endpoint should inherit first river width");
    AssertNearlyEqual(0.5f, samples[1], 0.0001f, "light blue should map to min half-width");
    AssertNearlyEqual(1.1f, samples[2], 0.0001f, "middle blue should interpolate configured half-width");
    AssertNearlyEqual(2.0f, samples[3], 0.0001f, "green should map to max half-width");
    AssertNearlyEqual(2.0f, samples[4], 0.0001f, "confluence endpoint should inherit previous river width");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: compile fails because `RiverMapService(float,float)` and `CellHalfWidths` do not exist.

- [ ] **Step 3: Implement palette mapping**

In `RiverPixelType.cs`, replace `GetHalfWidth` with:

```csharp
public static int WidthPaletteCount => WidthPalette.Length;

public static float GetWidthFactor(int paletteIndex)
{
    if (WidthPalette.Length <= 1)
        return 0.0f;

    int clamped = Math.Clamp(paletteIndex, 0, WidthPalette.Length - 1);
    return clamped / (float)(WidthPalette.Length - 1);
}

public static float GetHalfWidth(int paletteIndex, float minFullWidth, float maxFullWidth)
{
    float min = MathF.Max(minFullWidth, 0.0001f);
    float max = MathF.Max(maxFullWidth, min);
    float fullWidth = min + (max - min) * GetWidthFactor(paletteIndex);
    return fullWidth * 0.5f;
}
```

Keep the existing `WidthPalette` color list, but comments should no longer state fixed full widths. Update comments to index-only labels if needed.

In `RiverSegment.cs`, add:

```csharp
public List<float> CellHalfWidths { get; set; } = new();
public List<float> CenterlineHalfWidths { get; set; } = new();
```

In `RiverMapService`, add fields and constructors:

```csharp
private readonly float riverMinWidth;
private readonly float riverMaxWidth;

public RiverMapService()
    : this(1.0f, 4.0f)
{
}

public RiverMapService(float riverMinWidth, float riverMaxWidth)
{
    if (riverMinWidth <= 0.0f)
        throw new ArgumentOutOfRangeException(nameof(riverMinWidth), "River min width must be greater than 0.");
    if (riverMaxWidth < riverMinWidth)
        throw new ArgumentOutOfRangeException(nameof(riverMaxWidth), "River max width must be greater than or equal to min width.");

    this.riverMinWidth = riverMinWidth;
    this.riverMaxWidth = riverMaxWidth;
}
```

Replace `seg.AvgHalfWidth = ComputeAvgWidth(seg, Cells);` with:

```csharp
PopulateWidthSamples(seg, Cells);
```

Add:

```csharp
private void PopulateWidthSamples(RiverSegment seg, RiverCell[,] cells)
{
    seg.CellHalfWidths.Clear();
    float total = 0.0f;
    int count = 0;
    float lastWidth = RiverCell.GetHalfWidth(0, riverMinWidth, riverMaxWidth);

    for (int i = 0; i < seg.Cells.Count; i++)
    {
        var (x, y) = seg.Cells[i];
        RiverCell cell = cells[x, y];
        if (cell.Type == RiverPixelType.River)
        {
            lastWidth = RiverCell.GetHalfWidth(cell.Width, riverMinWidth, riverMaxWidth);
            total += lastWidth;
            count++;
        }
        else if (i == 0)
        {
            lastWidth = FindNearestSegmentRiverHalfWidth(seg, cells, startIndex: 0, direction: 1, fallback: lastWidth);
        }

        seg.CellHalfWidths.Add(lastWidth);
    }

    seg.AvgHalfWidth = count > 0 ? total / count : RiverCell.GetHalfWidth(1, riverMinWidth, riverMaxWidth);
}

private float FindNearestSegmentRiverHalfWidth(RiverSegment seg, RiverCell[,] cells, int startIndex, int direction, float fallback)
{
    for (int i = startIndex; i >= 0 && i < seg.Cells.Count; i += direction)
    {
        var (x, y) = seg.Cells[i];
        RiverCell cell = cells[x, y];
        if (cell.Type == RiverPixelType.River)
            return RiverCell.GetHalfWidth(cell.Width, riverMinWidth, riverMaxWidth);
    }

    return fallback;
}
```

After `NormalizeDirection(seg)`, if direction was reversed, `Cells` are reversed before width sampling, so widths will align with final direction.

- [ ] **Step 4: Run tests to verify pass**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: PASS for palette mapping and existing average width tests.

- [ ] **Step 5: Commit**

```powershell
git add -- Terrain.Editor/Models/RiverPixelType.cs Terrain.Editor/Models/RiverSegment.cs Terrain.Editor/Services/RiverMapService.cs Terrain.Editor.Tests/Program.cs
git commit -m "Map river palette to configured widths"
```

---

### Task 4: Preserve Width Gradient Through Mesh Generation

**Files:**
- Modify: `Terrain.Editor/Services/RiverMeshService.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [ ] **Step 1: Write failing mesh width gradient test**

Add to `Program.cs` registrations near mesh tests:

```csharp
Run("river mesh preserves local width gradient", RiverMeshPreservesLocalWidthGradient);
```

Add:

```csharp
void RiverMeshPreservesLocalWidthGradient()
{
    var segment = new RiverSegment
    {
        Centerline =
        [
            new Vector3(0, 0, 0),
            new Vector3(2, 0, 0),
            new Vector3(4, 0, 0),
        ],
        CenterlineHalfWidths = [0.5f, 1.0f, 2.0f],
        WorldLength = 2,
        AvgHalfWidth = 1.0f,
    };

    var mesh = new RiverMeshService(null!).BuildRiverMesh(segment, 1.0f);

    float mapExtent = mesh.MapExtent;
    float firstHalfWidth = mesh.Vertices[0].Width * mapExtent;
    float middleHalfWidth = mesh.Vertices[2].Width * mapExtent;
    float lastHalfWidth = mesh.Vertices[^1].Width * mapExtent;

    AssertNearlyEqual(0.5f, firstHalfWidth, 0.0001f, "first mesh sample should use local narrow half-width");
    AssertNearlyEqual(1.0f, middleHalfWidth, 0.0001f, "middle mesh sample should use local intermediate half-width");
    AssertNearlyEqual(2.0f, lastHalfWidth, 0.0001f, "last mesh sample should use local wide half-width");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: test fails because all generated widths use `AvgHalfWidth`.

- [ ] **Step 3: Consume per-centerline widths in `BuildRiverMesh`**

In `RiverMeshService.BuildRiverMesh`, replace:

```csharp
float baseHalfWidth = Math.Max(MinVisibleHalfWidth, segment.AvgHalfWidth * widthScale);
```

with:

```csharp
IReadOnlyList<float> halfWidths = GetCenterlineHalfWidths(segment, centerline.Count);
```

Inside the vertex loop, replace:

```csharp
float halfWidth = baseHalfWidth * taperScale;
```

with:

```csharp
float baseHalfWidth = Math.Max(MinVisibleHalfWidth, halfWidths[i] * widthScale);
float halfWidth = baseHalfWidth * taperScale;
```

Add helper:

```csharp
private static IReadOnlyList<float> GetCenterlineHalfWidths(RiverSegment segment, int count)
{
    if (segment.CenterlineHalfWidths.Count == count)
        return segment.CenterlineHalfWidths;

    var fallback = new float[count];
    float width = Math.Max(MinVisibleHalfWidth, segment.AvgHalfWidth);
    Array.Fill(fallback, width);
    return fallback;
}
```

- [ ] **Step 4: Run tests to verify pass**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: new mesh gradient test passes.

- [ ] **Step 5: Commit**

```powershell
git add -- Terrain.Editor/Services/RiverMeshService.cs Terrain.Editor.Tests/Program.cs
git commit -m "Use local river widths in mesh generation"
```

---

### Task 5: Resample Widths With Centerline Processing

**Files:**
- Modify: `Terrain.Editor/Services/RiverMeshService.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [ ] **Step 1: Write failing integrated generation test**

Add to `Program.cs` registrations near centerline tests:

```csharp
Run("river centerline generation preserves palette width gradient", RiverCenterlineGenerationPreservesPaletteWidthGradient);
```

Add:

```csharp
void RiverCenterlineGenerationPreservesPaletteWidthGradient()
{
    var segment = new RiverSegment
    {
        Cells = [(0, 1), (1, 1), (2, 1), (3, 1)],
        CellHalfWidths = [0.5f, 0.75f, 1.25f, 2.0f],
    };

    var service = new RiverMeshService(null!);
    service.BuildCenterlines([segment], mapWidth: 4, mapHeight: 3);

    Assert(segment.Centerline.Count > 2, "centerline generation should produce samples");
    AssertEqual(segment.Centerline.Count, segment.CenterlineHalfWidths.Count, "centerline widths should align with centerline samples");
    Assert(segment.CenterlineHalfWidths[0] < segment.CenterlineHalfWidths[^1], "width gradient should remain increasing after resampling");
}
```

This uses `RiverMeshService(null!)`. Update `BuildCenterlines` in implementation to allow null terrain manager by sampling height `0`, matching existing mesh tests.

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: failure because `BuildCenterlines` currently requires `TerrainManager` and does not populate `CenterlineHalfWidths`.

- [ ] **Step 3: Refactor centerline processing to carry widths**

In `BuildCenterlines`, remove the null-terrain exception:

```csharp
if (terrainManager == null)
    throw new InvalidOperationException("River centerline generation requires a TerrainManager for height sampling.");
```

Replace it with no exception; `SampleTerrainHeight` already returns `0` when `terrainManager == null`.

Build raw widths next to raw points:

```csharp
var rawWidths = new List<float>();
for (int cellIndex = 0; cellIndex < seg.Cells.Count; cellIndex++)
{
    var (x, y) = seg.Cells[cellIndex];
    float wx = (x + 0.5f) * pixelToWorld;
    float wz = (y + 0.5f) * pixelToWorld;
    float wy = SampleTerrainHeight(wx, wz) + SurfaceOffset;
    rawPoints.Add(new Vector3(wx, wy, wz));
    rawWidths.Add(GetCellHalfWidth(seg, cellIndex));
}
```

Add helper:

```csharp
private static float GetCellHalfWidth(RiverSegment segment, int index)
{
    if (index >= 0 && index < segment.CellHalfWidths.Count)
        return segment.CellHalfWidths[index];

    return Math.Max(MinVisibleHalfWidth, segment.AvgHalfWidth);
}
```

Replace `SimplifyCenterline(rawPoints, ...)` call with a paired simplification:

```csharp
var (simplifiedPoints, simplifiedWidths) = SimplifyCenterlineWithWidths(rawPoints, rawWidths, CenterlineSimplificationTolerance);
```

Add paired helper:

```csharp
private static (List<Vector3> Points, List<float> Widths) SimplifyCenterlineWithWidths(List<Vector3> points, List<float> widths, float tolerance)
{
    var simplified = SimplifyCenterline(points, tolerance);
    if (simplified.Count == points.Count)
        return (simplified, new List<float>(widths));

    var resultWidths = new List<float>(simplified.Count);
    int searchStart = 0;
    foreach (Vector3 point in simplified)
    {
        int index = FindPointIndex(points, point, searchStart);
        resultWidths.Add(index >= 0 && index < widths.Count ? widths[index] : widths[Math.Min(searchStart, widths.Count - 1)]);
        searchStart = Math.Max(index, searchStart) + 1;
    }

    return (simplified, resultWidths);
}

private static int FindPointIndex(List<Vector3> points, Vector3 point, int start)
{
    for (int i = Math.Max(0, start); i < points.Count; i++)
    {
        if (Vector3.DistanceSquared(points[i], point) <= 0.000001f)
            return i;
    }

    return -1;
}
```

Add width smoothing matching Chaikin:

```csharp
private static List<float> SmoothWidths(List<float> widths, int iterations)
{
    if (widths.Count <= 2 || iterations <= 0)
        return new List<float>(widths);

    var current = new List<float>(widths);
    for (int iteration = 0; iteration < iterations; iteration++)
    {
        var next = new List<float>(current.Count * 2) { current[0] };
        for (int i = 0; i < current.Count - 1; i++)
        {
            float a = current[i];
            float b = current[i + 1];
            next.Add(a * 0.75f + b * 0.25f);
            next.Add(a * 0.25f + b * 0.75f);
        }
        next.Add(current[^1]);
        current = next;
    }

    return current;
}
```

Create a paired Catmull-Rom interpolation:

```csharp
private static (List<Vector3> Points, List<float> Widths) CatmullRomInterpolateWithWidths(List<Vector3> controlPoints, List<float> controlWidths)
{
    if (controlPoints.Count < 2)
        return (new List<Vector3>(controlPoints), new List<float>(controlWidths));

    var points = new List<Vector3> { controlPoints[0] };
    var widths = new List<float> { controlWidths.Count > 0 ? controlWidths[0] : MinVisibleHalfWidth };
    float accumulated = 0.0f;

    for (int i = 0; i < controlPoints.Count - 1; i++)
    {
        Vector3 p0 = controlPoints[Math.Max(0, i - 1)];
        Vector3 p1 = controlPoints[i];
        Vector3 p2 = controlPoints[i + 1];
        Vector3 p3 = controlPoints[Math.Min(controlPoints.Count - 1, i + 2)];

        float w0 = controlWidths[Math.Max(0, i - 1)];
        float w1 = controlWidths[i];
        float w2 = controlWidths[i + 1];
        float w3 = controlWidths[Math.Min(controlWidths.Count - 1, i + 2)];

        float spacing = ComputeAdaptiveSampleSpacing(p0, p1, p2, p3);
        float segmentLength = HorizontalDistance(p1, p2);
        if (segmentLength < 0.001f) continue;

        int steps = Math.Max(1, (int)(segmentLength / spacing));
        for (int s = 1; s <= steps; s++)
        {
            float t = s / (float)steps;
            Vector3 point = CatmullRom(p0, p1, p2, p3, t);
            float width = Math.Max(MinVisibleHalfWidth, CatmullRomScalar(w0, w1, w2, w3, t));
            float dist = HorizontalDistance(points[^1], point);
            accumulated += dist;
            if (accumulated >= spacing)
            {
                points.Add(point);
                widths.Add(width);
                accumulated = 0;
            }
        }
    }

    if (Vector3.Distance(points[^1], controlPoints[^1]) > 0.01f)
    {
        points.Add(controlPoints[^1]);
        widths.Add(controlWidths[^1]);
    }

    return (points, widths);
}

private static float CatmullRomScalar(float p0, float p1, float p2, float p3, float t)
{
    float t2 = t * t, t3 = t2 * t;
    return 0.5f * ((2f * p1) +
        (-p0 + p2) * t +
        (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
        (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
}
```

In `BuildCenterlines`, use:

```csharp
var smoothedPoints = SmoothCenterline(simplifiedPoints, CenterlineSmoothingIterations);
var smoothedWidths = SmoothWidths(simplifiedWidths, CenterlineSmoothingIterations);
var (interpolatedPoints, interpolatedWidths) = CatmullRomInterpolateWithWidths(smoothedPoints, smoothedWidths);
seg.Centerline = ResampleTerrainHeights(interpolatedPoints);
seg.CenterlineHalfWidths = interpolatedWidths;
seg.WorldLength = ComputeMapUnitPolylineLength(seg.Centerline);
```

- [ ] **Step 4: Run tests to verify pass**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: PASS for centerline width resampling and existing smoothing tests.

- [ ] **Step 5: Commit**

```powershell
git add -- Terrain.Editor/Services/RiverMeshService.cs Terrain.Editor.Tests/Program.cs
git commit -m "Preserve river width gradients through centerlines"
```

---

### Task 6: Defaults, Documentation, and Full Verification

**Files:**
- Modify: `game/map/default.toml`
- Modify: `docs/ARCHITECTURE_OVERVIEW.md`
- Modify: `docs/CURRENT_FEATURES.md`
- Add: `docs/log/2026/06/21/2026-06-21-river-width-gradient-implementation.md`

- [ ] **Step 1: Update default map TOML**

In `game/map/default.toml`, change `[settings]` to:

```toml
[settings]
height_scale = 200
river_min_width = 1
river_max_width = 4
```

- [ ] **Step 2: Update architecture docs**

In `docs/ARCHITECTURE_OVERVIEW.md`, update the river mesh generation paragraph to include:

```markdown
2026-06-21 river width generation now reads `[settings].river_min_width` / `river_max_width` as full-width map units from `game/map/default.toml` and maps each `rivers.png` palette index linearly into that range. Mesh generation carries local half-width samples through centerline smoothing/interpolation, so shallow-blue to green local width gradients are preserved instead of being collapsed into `AvgHalfWidth`.
```

In `docs/CURRENT_FEATURES.md`, update the river mesh generation row with the same concise statement.

- [ ] **Step 3: Add session log**

Create `docs/log/2026/06/21/2026-06-21-river-width-gradient-implementation.md`:

```markdown
# River Width Gradient Implementation
**Date**: 2026-06-21
**Session**: river width gradient implementation
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Add configurable river width range to `game/map/default.toml`.
- Preserve local `rivers.png` palette width gradients during mesh generation.

---

## What We Did

### 1. Map settings
- Added `[settings] river_min_width` and `river_max_width` as full-width values.
- Defaults are `1` and `4`.
- Reader, writer, scaffold, and save path preserve the settings.

### 2. Local river widths
- Palette indices now map linearly into the configured full-width range.
- `RiverSegment` carries width samples aligned with cells and centerlines.
- `RiverMeshService` uses per-centerline half-widths instead of one segment average.

---

## Testing

- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore`

---

## Quick Reference for Future Claude

- TOML values are full-width.
- Shader width stream remains normalized half-width.
- Do not reintroduce `AvgHalfWidth` as the mesh's only width source.
```

- [ ] **Step 4: Run full verification**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
dotnet build Terrain.sln --no-restore
git diff --check
```

Expected:

- Tests pass.
- Solution builds.
- `git diff --check` reports no whitespace errors.

- [ ] **Step 5: Commit**

```powershell
git add -- game/map/default.toml docs/ARCHITECTURE_OVERVIEW.md docs/CURRENT_FEATURES.md docs/log/2026/06/21/2026-06-21-river-width-gradient-implementation.md
git commit -m "Document river width gradient implementation"
```

---

## Self-Review

Spec coverage:

- Config fields and defaults: Task 1 and Task 6.
- Full-width units: Task 1 tests and docs.
- Editor save preservation: Task 1.
- Editor generation propagation: Task 2.
- Palette linear mapping: Task 3.
- Local width samples through mesh: Task 4 and Task 5.
- Regression tests: Tasks 1 through 5.
- Documentation/session log: Task 6.

Placeholder scan:

- No deferred implementation markers or vague delayed-work steps remain.

Type consistency:

- `RiverMinWidth` and `RiverMaxWidth` are full-width floats on map/session/source paths.
- `CellHalfWidths` and `CenterlineHalfWidths` are half-width floats used only by river extraction and mesh generation.
- `RiverVertex.Width` remains normalized half-width.
