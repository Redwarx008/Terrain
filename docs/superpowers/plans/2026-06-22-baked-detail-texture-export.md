# Baked DetailTexture Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move terrain DetailTexture construction from Runtime startup to Editor Export by writing baked DetailIndex/DetailWeight VT payloads into `.terrain` v8.

**Architecture:** Editor owns authoring rule evaluation and bakes two RGBA8 detail control maps during export. Runtime reads HeightMap, DetailIndex, and DetailWeight pages directly from `.terrain` and never evaluates biome rules for terrain detail. `.terrain` no longer stores BiomeMask VT.

**Tech Stack:** C#/.NET 10, Stride texture arrays, ImageSharp test assets, custom binary `.terrain` VT format, existing `Terrain.Editor.Tests` harness.

---

## File Structure

- Modify `Terrain.Editor/Models/TerrainFileFormat.cs`: bump editor header to v8 and rename splat/river header fields to detail fields used by export.
- Modify `Terrain/Streaming/TerrainStreaming.cs`: bump runtime reader to v8, expose detail index/weight headers and page reads, remove generated runtime detail map storage.
- Modify `Terrain/Core/TerrainProcessor.cs`: remove runtime biome-mask loading and detail builder delegate path.
- Modify `Terrain/Resources/GameRuntimeResourceBootstrap.cs`: stop resolving `biome_mask.png` and `biome_settings.toml` for Runtime terrain loading.
- Modify `Terrain/Resources/TerrainRuntimeResourceBundle.cs`: remove runtime biome mask/settings properties that only existed for terrain detail generation.
- Create `Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs`: editor-only detail evaluator that consumes `BiomeRuleService`, `BiomeMask`, height data, and material slots.
- Modify `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`: bake detail maps and write v8 HeightMap + DetailIndex + DetailWeight VT payloads.
- Delete `Terrain/Materials/RuntimeDetailMapBuilder.cs` and `Terrain/Materials/TerrainDetailGeneration.cs`: Runtime should not retain terrain-detail rule generation.
- Modify `Terrain.Editor.Tests/VirtualResources/FakeTerrainFileReader.cs`: implement new v8 reader interface for tests.
- Modify `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeLoadBehaviorTests.cs`: reverse old runtime detail generation tests.
- Modify `Terrain.Editor.Tests/VirtualResources/GameRuntimeResourceBootstrapTests.cs`: Runtime no longer requires biome mask/settings resources.
- Create `Terrain.Editor.Tests/VirtualResources/BakedDetailTerrainFormatTests.cs`: format/export/reader tests.
- Modify `Terrain.Editor.Tests/Program.cs`: register the new test suite.
- Update `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, and `docs/design/map-data-toml-formats.md` after implementation.
- Add a final session log under `docs/log/2026/06/22/`.

---

### Task 1: Lock v8 Format and Runtime Boundary Tests

**Files:**
- Create: `Terrain.Editor.Tests/VirtualResources/BakedDetailTerrainFormatTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`
- Modify: `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeLoadBehaviorTests.cs`
- Modify: `Terrain.Editor.Tests/VirtualResources/GameRuntimeResourceBootstrapTests.cs`

- [ ] **Step 1: Add the new test suite entry**

Insert this call in `Terrain.Editor.Tests/Program.cs` after `ExportWorkflowTests.RunAll();`:

```csharp
BakedDetailTerrainFormatTests.RunAll();
```

- [ ] **Step 2: Create failing tests for v8 header and no BiomeMask VT**

Create `Terrain.Editor.Tests/VirtualResources/BakedDetailTerrainFormatTests.cs` with these tests:

```csharp
using System.Runtime.InteropServices;
using Terrain.Editor.Models;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class BakedDetailTerrainFormatTests
{
    public static void RunAll()
    {
        TestHarness.Run("terrain format version is v8 baked detail", TerrainFormatVersionIsV8BakedDetail);
        TestHarness.Run("runtime streaming rejects pre baked detail terrain versions", RuntimeStreamingRejectsPreBakedDetailVersions);
        TestHarness.Run("terrain file reader exposes baked detail page reads", TerrainFileReaderExposesBakedDetailPageReads);
        TestHarness.Run("runtime source no longer contains generated detail map state", RuntimeSourceNoLongerContainsGeneratedDetailMapState);
    }

    private static void TerrainFormatVersionIsV8BakedDetail()
    {
        TestHarness.AssertEqual(8, TerrainFileHeader.CURRENT_VERSION, "editor terrain format version");
        string editorFormat = ReadRepoText("Terrain.Editor/Models/TerrainFileFormat.cs");
        TestHarness.Assert(editorFormat.Contains("DetailMapFormat", StringComparison.Ordinal), "editor header should use detail map naming");
        TestHarness.Assert(editorFormat.Contains("DetailMapMipLevels", StringComparison.Ordinal), "editor header should expose detail mip count");
        TestHarness.Assert(editorFormat.Contains("DetailMapResolutionRatio", StringComparison.Ordinal), "editor header should expose detail resolution ratio");
        TestHarness.Assert(!editorFormat.Contains("SplatMapFormat", StringComparison.Ordinal), "editor header should not expose splat/biome mask format");
        TestHarness.Assert(!editorFormat.Contains("RiverMapFormat", StringComparison.Ordinal), "terrain header should not keep unused river map payload fields");
    }

    private static void RuntimeStreamingRejectsPreBakedDetailVersions()
    {
        string streaming = ReadRepoText("Terrain/Streaming/TerrainStreaming.cs");
        TestHarness.Assert(streaming.Contains("MinSupportedVersion = 8", StringComparison.Ordinal), "runtime reader should reject old terrain files");
        TestHarness.Assert(streaming.Contains("MaxSupportedVersion = 8", StringComparison.Ordinal), "runtime reader should only accept current baked detail terrain version");
        TestHarness.Assert(streaming.Contains("re-export", StringComparison.OrdinalIgnoreCase), "old terrain version error should ask for re-export");
    }

    private static void TerrainFileReaderExposesBakedDetailPageReads()
    {
        string streaming = ReadRepoText("Terrain/Streaming/TerrainStreaming.cs");
        TestHarness.Assert(streaming.Contains("DetailIndexMapHeader", StringComparison.Ordinal), "reader should expose detail index header");
        TestHarness.Assert(streaming.Contains("DetailWeightMapHeader", StringComparison.Ordinal), "reader should expose detail weight header");
        TestHarness.Assert(streaming.Contains("ReadDetailIndexPage", StringComparison.Ordinal), "reader should read detail index pages");
        TestHarness.Assert(streaming.Contains("ReadDetailWeightPage", StringComparison.Ordinal), "reader should read detail weight pages");
        TestHarness.Assert(!streaming.Contains("ReadSplatMapPage", StringComparison.Ordinal), "reader should not expose old splat/biome mask pages");
    }

    private static void RuntimeSourceNoLongerContainsGeneratedDetailMapState()
    {
        string streaming = ReadRepoText("Terrain/Streaming/TerrainStreaming.cs");
        string processor = ReadRepoText("Terrain/Core/TerrainProcessor.cs");
        TestHarness.Assert(!streaming.Contains("generatedDetailMaps", StringComparison.Ordinal), "streaming should not store generated detail maps");
        TestHarness.Assert(!streaming.Contains("FillGeneratedDetailPage", StringComparison.Ordinal), "streaming should not synthesize detail pages");
        TestHarness.Assert(!streaming.Contains("SetGeneratedDetailMaps", StringComparison.Ordinal), "streaming should not accept generated detail maps");
        TestHarness.Assert(!processor.Contains("RuntimeDetailMapBuilder.Generate", StringComparison.Ordinal), "runtime processor should not build detail maps");
        TestHarness.Assert(!processor.Contains("DetailMapBuilder", StringComparison.Ordinal), "runtime processor should not carry a detail builder delegate");
    }

    internal static string ReadRepoText(string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), normalized));
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Terrain.sln")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
```

- [ ] **Step 3: Reverse old runtime detail tests**

In `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeLoadBehaviorTests.cs`, remove these `RunAll` entries:

```csharp
TestHarness.Run("runtime detail map builds after terrain streaming is attached", RuntimeDetailMapBuildsAfterTerrainStreamingIsAttached);
TestHarness.Run("runtime detail map uses terrain component height interface", RuntimeDetailMapUsesTerrainComponentHeightInterface);
```

Delete the methods `RuntimeDetailMapBuildsAfterTerrainStreamingIsAttached` and `RuntimeDetailMapUsesTerrainComponentHeightInterface`.

Add this new `RunAll` entry after `RuntimeHeightCpuCacheFollowsTerrainStreamingResidency`:

```csharp
TestHarness.Run("runtime terrain startup does not build detail maps", RuntimeTerrainStartupDoesNotBuildDetailMaps);
```

Add this method:

```csharp
private static void RuntimeTerrainStartupDoesNotBuildDetailMaps()
{
    string repositoryRoot = FindRepositoryRoot();
    string processor = File.ReadAllText(Path.Combine(repositoryRoot, "Terrain", "Core", "TerrainProcessor.cs"));
    string streaming = File.ReadAllText(Path.Combine(repositoryRoot, "Terrain", "Streaming", "TerrainStreaming.cs"));

    TestHarness.Assert(!processor.Contains("RuntimeDetailMapBuilder", StringComparison.Ordinal), "runtime processor should not reference runtime detail builder");
    TestHarness.Assert(!processor.Contains("LoadRuntimeBiomeMask", StringComparison.Ordinal), "runtime processor should not load biome masks for terrain detail");
    TestHarness.Assert(!streaming.Contains("RuntimeDetailMapData", StringComparison.Ordinal), "runtime streaming should not retain full generated detail data");
}
```

- [ ] **Step 4: Update bootstrap tests to expect no runtime biome resources**

In `Terrain.Editor.Tests/VirtualResources/GameRuntimeResourceBootstrapTests.cs`, replace the `RunAll` entry:

```csharp
TestHarness.Run("bootstrap requires biome mask resource", BootstrapRequiresBiomeMaskResource);
```

with:

```csharp
TestHarness.Run("bootstrap does not require biome authoring resources", BootstrapDoesNotRequireBiomeAuthoringResources);
```

Replace `BootstrapRequiresBiomeMaskResource` with:

```csharp
private static void BootstrapDoesNotRequireBiomeAuthoringResources()
{
    string root = CreateWorkspace();
    WriteResourceBundle(root);
    File.Delete(Path.Combine(root, "map", "biome_mask.png"));
    File.Delete(Path.Combine(root, "map", "biome_settings.toml"));

    TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(CreateResolver(root)).Load();

    TestHarness.AssertEqual(FullPath(root, "map", "terrain.terrain"), bundle.TerrainDataPath, "runtime should load terrain data without biome authoring resources");
}
```

Delete these `RunAll` entries and their methods because runtime no longer validates biome material references:

```csharp
TestHarness.Run("bootstrap validates biome material references", BootstrapValidatesBiomeMaterialReferences);
TestHarness.Run("bootstrap validates biome material references case sensitively", BootstrapValidatesBiomeMaterialReferencesCaseSensitively);
```

- [ ] **Step 5: Run tests and confirm they fail for the expected reasons**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: FAIL messages mention v8/detail reader naming or old runtime detail generation still present. Existing unrelated warnings are acceptable.

- [ ] **Step 6: Commit failing tests**

```powershell
git add -- Terrain.Editor.Tests/Program.cs Terrain.Editor.Tests/VirtualResources/BakedDetailTerrainFormatTests.cs Terrain.Editor.Tests/VirtualResources/TerrainRuntimeLoadBehaviorTests.cs Terrain.Editor.Tests/VirtualResources/GameRuntimeResourceBootstrapTests.cs
git commit -m "test: lock baked detail terrain format"
```

---

### Task 2: Update `.terrain` v8 Header and Reader

**Files:**
- Modify: `Terrain.Editor/Models/TerrainFileFormat.cs`
- Modify: `Terrain/Streaming/TerrainStreaming.cs`
- Modify: `Terrain.Editor.Tests/VirtualResources/FakeTerrainFileReader.cs`

- [ ] **Step 1: Rename editor header fields and bump format version**

In `Terrain.Editor/Models/TerrainFileFormat.cs`, replace `TerrainFileHeader` with:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct TerrainFileHeader
{
    public int Magic;
    public int Version;
    public int Width;
    public int Height;
    public int LeafNodeSize;
    public int TileSize;
    public int Padding;
    public int HeightMapMipLevels;
    public int DetailMapFormat;
    public int DetailMapMipLevels;
    public int DetailMapResolutionRatio;

    public const int MAGIC_VALUE = 0x52524554;
    public const int CURRENT_VERSION = 8;

    public readonly bool IsValid => Magic == MAGIC_VALUE;
}
```

- [ ] **Step 2: Update runtime reader interface**

In `Terrain/Streaming/TerrainStreaming.cs`, replace `ITerrainFileReader` members with:

```csharp
TerrainFileHeader Header { get; }
TerrainVirtualTextureHeader HeightmapHeader { get; }
TerrainVirtualTextureHeader DetailIndexMapHeader { get; }
TerrainVirtualTextureHeader DetailWeightMapHeader { get; }
int DetailMapResolutionRatio { get; }
int DetailMapMipCount { get; }
TerrainMinMaxErrorMap[] ReadAllMinMaxErrorMaps();
ushort[] ReadAllHeightData();
void ReadHeightPage(TerrainPageKey key, Span<byte> destination);
void ReadDetailIndexPage(TerrainPageKey key, Span<byte> destination);
void ReadDetailWeightPage(TerrainPageKey key, Span<byte> destination);
```

- [ ] **Step 3: Replace runtime header struct**

In `Terrain/Streaming/TerrainStreaming.cs`, replace `TerrainFileHeader` with:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct TerrainFileHeader
{
    public const int MagicValue = 0x52524554;
    public const int MinSupportedVersion = 8;
    public const int MaxSupportedVersion = 8;

    public int Magic;
    public int Version;
    public int Width;
    public int Height;
    public int LeafNodeSize;
    public int TileSize;
    public int Padding;
    public int HeightMapMipLevels;
    public int DetailMapFormat;
    public int DetailMapMipLevels;
    public int DetailMapResolutionRatio;
}
```

- [ ] **Step 4: Replace splat/river reader fields with detail fields**

In `TerrainFileReader`, replace old splat/river fields with:

```csharp
private readonly TerrainVirtualTextureHeader detailIndexMapHeader;
private readonly TerrainMipLayout[] detailIndexMipLayouts;
private readonly int detailIndexTileByteSize;
private readonly TerrainVirtualTextureHeader detailWeightMapHeader;
private readonly TerrainMipLayout[] detailWeightMipLayouts;
private readonly int detailWeightTileByteSize;
```

After heightmap layout calculation, read both detail payloads:

```csharp
detailIndexMapHeader = ReadStruct<TerrainVirtualTextureHeader>(fileHandle, ref currentOffset);
ValidateDetailHeader(Header, detailIndexMapHeader, "DetailIndex");
detailIndexTileByteSize = ComputeTileByteSize(detailIndexMapHeader);
detailIndexMipLayouts = BuildMipLayouts(detailIndexMapHeader, detailIndexTileByteSize, ref currentOffset);

detailWeightMapHeader = ReadStruct<TerrainVirtualTextureHeader>(fileHandle, ref currentOffset);
ValidateDetailHeader(Header, detailWeightMapHeader, "DetailWeight");
ValidateMatchingDetailHeaders(detailIndexMapHeader, detailWeightMapHeader);
detailWeightTileByteSize = ComputeTileByteSize(detailWeightMapHeader);
detailWeightMipLayouts = BuildMipLayouts(detailWeightMapHeader, detailWeightTileByteSize, ref currentOffset);
```

Add helpers inside `TerrainFileReader`:

```csharp
private static int ComputeTileByteSize(TerrainVirtualTextureHeader header)
{
    int paddedTileSize = checked(header.TileSize + header.Padding * 2);
    return checked(paddedTileSize * paddedTileSize * header.BytesPerPixel);
}

private static TerrainMipLayout[] BuildMipLayouts(TerrainVirtualTextureHeader header, int tileByteSize, ref long currentOffset)
{
    var layouts = new TerrainMipLayout[header.Mipmaps];
    for (int mip = 0; mip < header.Mipmaps; mip++)
    {
        VirtualTextureMipLayoutInfo layoutInfo = VirtualTextureLayout.GetMipLayout(
            header.Width,
            header.Height,
            header.TileSize,
            mip);
        layouts[mip] = new TerrainMipLayout(layoutInfo.Width, layoutInfo.Height, layoutInfo.TilesX, layoutInfo.TilesY, currentOffset);
        currentOffset = checked(currentOffset + checked((long)layoutInfo.TilesX * layoutInfo.TilesY * tileByteSize));
    }

    return layouts;
}
```

- [ ] **Step 5: Add detail header validation and page read methods**

Add these members to `TerrainFileReader`:

```csharp
public TerrainVirtualTextureHeader DetailIndexMapHeader => detailIndexMapHeader;
public TerrainVirtualTextureHeader DetailWeightMapHeader => detailWeightMapHeader;
public int DetailMapResolutionRatio => Header.DetailMapResolutionRatio;
public int DetailMapMipCount => detailIndexMipLayouts.Length;

public void ReadDetailIndexPage(TerrainPageKey key, Span<byte> destination)
{
    ReadVirtualTexturePage(key, destination, detailIndexMipLayouts, detailIndexTileByteSize, "detail index");
}

public void ReadDetailWeightPage(TerrainPageKey key, Span<byte> destination)
{
    ReadVirtualTexturePage(key, destination, detailWeightMipLayouts, detailWeightTileByteSize, "detail weight");
}
```

Refactor `ReadHeightPage` to call:

```csharp
ReadVirtualTexturePage(key, destination, heightmapMipLayouts, tileByteSize, "heightmap");
```

Add:

```csharp
private void ReadVirtualTexturePage(
    TerrainPageKey key,
    Span<byte> destination,
    TerrainMipLayout[] layouts,
    int pageByteSize,
    string payloadName)
{
    if ((uint)key.MipLevel >= (uint)layouts.Length)
        throw new ArgumentOutOfRangeException(nameof(key), $"Invalid {payloadName} mip level {key.MipLevel}.");

    if (destination.Length < pageByteSize)
        throw new ArgumentException($"Destination buffer must be at least {pageByteSize} bytes.", nameof(destination));

    ref readonly var layout = ref layouts[key.MipLevel];
    if ((uint)key.PageX >= (uint)layout.TilesX || (uint)key.PageY >= (uint)layout.TilesY)
        throw new ArgumentOutOfRangeException(nameof(key), $"Invalid {payloadName} page coordinates ({key.PageX}, {key.PageY}) for mip {key.MipLevel}.");

    long offset = layout.Offset + (long)(key.PageY * layout.TilesX + key.PageX) * pageByteSize;
    ReadExactly(fileHandle, destination[..pageByteSize], offset);
}
```

Add validation:

```csharp
private static void ValidateDetailHeader(TerrainFileHeader header, TerrainVirtualTextureHeader detailHeader, string payloadName)
{
    if (detailHeader.BytesPerPixel != 4)
        throw new InvalidDataException($"{payloadName} block must be RGBA8, got {detailHeader.BytesPerPixel} bytes per pixel.");

    int expectedWidth = (header.Width + 1) / 2;
    int expectedHeight = (header.Height + 1) / 2;
    if (detailHeader.Width != expectedWidth || detailHeader.Height != expectedHeight)
        throw new InvalidDataException($"{payloadName} dimensions {detailHeader.Width}x{detailHeader.Height} do not match expected half-resolution {expectedWidth}x{expectedHeight}.");

    if (detailHeader.TileSize != header.TileSize)
        throw new InvalidDataException($"{payloadName} tile size does not match the terrain header.");

    if (detailHeader.Padding != 1)
        throw new InvalidDataException($"{payloadName} padding must be 1.");

    if (detailHeader.Mipmaps != header.DetailMapMipLevels)
        throw new InvalidDataException($"{payloadName} mip count {detailHeader.Mipmaps} does not match terrain header detail mip count {header.DetailMapMipLevels}.");

    int expectedMipCount = VirtualTextureLayout.GetMipCount(detailHeader.Width, detailHeader.Height, detailHeader.TileSize);
    if (detailHeader.Mipmaps != expectedMipCount)
        throw new InvalidDataException($"{payloadName} mip count {detailHeader.Mipmaps} does not match the shared VT layout rule; expected {expectedMipCount}.");
}

private static void ValidateMatchingDetailHeaders(TerrainVirtualTextureHeader indexHeader, TerrainVirtualTextureHeader weightHeader)
{
    if (indexHeader.Width != weightHeader.Width
        || indexHeader.Height != weightHeader.Height
        || indexHeader.TileSize != weightHeader.TileSize
        || indexHeader.Padding != weightHeader.Padding
        || indexHeader.BytesPerPixel != weightHeader.BytesPerPixel
        || indexHeader.Mipmaps != weightHeader.Mipmaps)
    {
        throw new InvalidDataException("DetailIndex and DetailWeight VT headers must match.");
    }
}
```

- [ ] **Step 6: Update old version error**

In `ValidateHeader`, use this message:

```csharp
throw new InvalidDataException($"Unsupported terrain file version {header.Version}. Re-export the terrain to .terrain v8 with baked detail textures.");
```

- [ ] **Step 7: Update `FakeTerrainFileReader`**

Replace old splat members in `Terrain.Editor.Tests/VirtualResources/FakeTerrainFileReader.cs` with:

```csharp
public TerrainVirtualTextureHeader DetailIndexMapHeader { get; } = new()
{
    Width = 2,
    Height = 2,
    TileSize = 2,
    Padding = 1,
    BytesPerPixel = 4,
    Mipmaps = 1,
};

public TerrainVirtualTextureHeader DetailWeightMapHeader => DetailIndexMapHeader;

public int DetailMapResolutionRatio => 2;

public int DetailMapMipCount => 1;

public void ReadDetailIndexPage(TerrainPageKey key, Span<byte> destination)
{
    destination.Clear();
}

public void ReadDetailWeightPage(TerrainPageKey key, Span<byte> destination)
{
    destination.Clear();
}
```

Set `Header.Version = 8` and `Header.DetailMapMipLevels = 1`.

- [ ] **Step 8: Run locked tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: v8 format tests pass; later runtime/export tests may still fail until subsequent tasks are complete.

- [ ] **Step 9: Commit reader format changes**

```powershell
git add -- Terrain.Editor/Models/TerrainFileFormat.cs Terrain/Streaming/TerrainStreaming.cs Terrain.Editor.Tests/VirtualResources/FakeTerrainFileReader.cs
git commit -m "feat: read baked detail terrain format"
```

---

### Task 3: Convert Runtime Streaming to Read Baked Detail Pages

**Files:**
- Modify: `Terrain/Streaming/TerrainStreaming.cs`
- Modify: `Terrain/Core/TerrainProcessor.cs`

- [ ] **Step 1: Rename streaming detail page calculations**

In `TerrainStreamingManager`, replace uses of `fileReader.SplatMapResolutionRatio`, `fileReader.SplatMapHeader`, and `fileReader.SplatMapMipCount` with:

```csharp
fileReader.DetailMapResolutionRatio
fileReader.DetailIndexMapHeader
fileReader.DetailMapMipCount
```

Rename `GetSplatMapPageKey` to `GetDetailMapPageKey`, and `GetSplatMapPageInfo` can keep its public shader-facing name only if the shader keys still say `SplatmapTileSize`; otherwise rename it consistently with material parameter names in the same change.

- [ ] **Step 2: Remove generated detail state**

Delete these members from `TerrainStreamingManager`:

```csharp
private RuntimeDetailMapData? generatedDetailMaps;

public void SetGeneratedDetailMaps(RuntimeDetailMapData detailMaps)
{
    generatedDetailMaps = detailMaps;
}

private void FillGeneratedDetailPage(TerrainPageKey key, Span<byte> destination, byte[] sourceData)
```

- [ ] **Step 3: Read baked detail pages in IO thread**

In `IoThreadMain`, replace detail request generation with:

```csharp
if (request.IsDetailMap)
{
    fileReader.ReadDetailIndexPage(request.Key, request.Data.Memory.Span);
    if (request.WeightData != null)
        fileReader.ReadDetailWeightPage(request.Key, request.WeightData.Memory.Span);
}
else
{
    fileReader.ReadHeightPage(request.Key, request.Data.Memory.Span);
}
```

- [ ] **Step 4: Read baked detail pages during top-level preload**

In `PreloadTopLevelChunks`, replace generated detail fill with:

```csharp
if (splatMapPageData != null && gpuDetailIndexArray != null && detailWeightArray != null)
{
    TerrainPageKey detailPageKey = GetDetailMapPageKey(chunkKey, out _, out _, out _);
    if (!gpuDetailIndexArray.IsPageResident(detailPageKey))
    {
        fileReader.ReadDetailIndexPage(detailPageKey, splatMapPageData.Memory.Span);
        gpuDetailIndexArray.UploadPage(commandList, detailPageKey, splatMapPageData.Memory.Span, pinned: false);
        if (gpuDetailIndexArray.TryGetResidentSlice(detailPageKey, out int sliceIndex))
        {
            using IMemoryOwner<byte> weightPageData = splatMapBufferPool!.Rent();
            fileReader.ReadDetailWeightPage(detailPageKey, weightPageData.Memory.Span);
            detailWeightArray.SetData(commandList, weightPageData.Memory.Span, sliceIndex, 0, null);
        }
    }
}
```

- [ ] **Step 5: Remove detail builder from `TerrainProcessor`**

In `Terrain/Core/TerrainProcessor.cs`, remove the `detailMapBuilder` parameter from:

```csharp
TryLoadRuntimeData(...)
CreateLoadedTerrainData(...)
LoadedTerrainData(...)
```

Delete:

```csharp
RuntimeBiomeMaskData biomeMask = LoadRuntimeBiomeMask(fileReader, bundle);
RuntimeDetailMapData generatedDetailMaps = loadedData.DetailMapBuilder(...);
attachedStreamingManager.SetGeneratedDetailMaps(generatedDetailMaps);
private static RuntimeBiomeMaskData LoadRuntimeBiomeMask(...)
private static RuntimeDetailMapData BuildRuntimeDetailMaps(...)
```

Construct `LoadedTerrainData` without biome/detail builder fields, and call:

```csharp
attachedStreamingManager.PreloadTopLevelChunks(commandList, loadedData.MinMaxErrorMaps[loadedData.MaxLod]);
```

immediately after `component.QuadTree` is attached.

- [ ] **Step 6: Run runtime tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: runtime boundary tests pass; export tests still fail until exporter writes v8 payloads.

- [ ] **Step 7: Commit runtime streaming changes**

```powershell
git add -- Terrain/Streaming/TerrainStreaming.cs Terrain/Core/TerrainProcessor.cs Terrain.Editor.Tests/VirtualResources/TerrainRuntimeLoadBehaviorTests.cs
git commit -m "feat: stream baked detail pages from terrain files"
```

---

### Task 4: Remove Runtime Biome Authoring Resource Dependencies

**Files:**
- Modify: `Terrain/Resources/GameRuntimeResourceBootstrap.cs`
- Modify: `Terrain/Resources/TerrainRuntimeResourceBundle.cs`
- Modify: `Terrain.Editor.Tests/VirtualResources/GameRuntimeResourceBootstrapTests.cs`
- Modify: `Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs`

- [ ] **Step 1: Trim runtime resource bundle**

In `Terrain/Resources/TerrainRuntimeResourceBundle.cs`, remove:

```csharp
public string BiomeMaskPath { get; init; } = string.Empty;
public string BiomeSettingsPath { get; init; } = string.Empty;
public RuntimeBiomeSettings BiomeSettings { get; init; } = new();
```

- [ ] **Step 2: Stop resolving biome authoring files in runtime bootstrap**

In `Terrain/Resources/GameRuntimeResourceBootstrap.cs`, remove:

```csharp
private const string BiomeMaskPath = "map/biome_mask.png";
private const string BiomeSettingsPath = "map/biome_settings.toml";
ResolvedGameResource biomeMaskResource = resolver.ResolveRequiredFile(BiomeMaskPath);
ResolvedGameResource biomeSettingsResource = resolver.ResolveRequiredFile(BiomeSettingsPath);
var materialIds = new HashSet<string>(materialDescriptor.Materials.Select(material => material.Id), StringComparer.Ordinal);
RuntimeBiomeSettings biomeSettings = RuntimeBiomeSettingsReader.ReadFrom(biomeSettingsResource.ResolvedPath, materialIds);
BiomeMaskPath = biomeMaskResource.ResolvedPath,
BiomeSettingsPath = biomeSettingsResource.ResolvedPath,
BiomeSettings = biomeSettings,
```

Keep material descriptor and material texture slot loading unchanged.

- [ ] **Step 3: Update bootstrap fixed companion test**

In `BootstrapLoadsFixedCompanionResources`, remove assertions for `bundle.BiomeMaskPath`, `bundle.BiomeSettingsPath`, and `bundle.BiomeSettings.Layers.Count`.

Keep:

```csharp
TestHarness.AssertEqual(FullPath(root, "map", "terrain.terrain"), bundle.TerrainDataPath, "terrain data path");
TestHarness.AssertEqual(FullPath(root, "map", "materials", "descriptor.toml"), bundle.MaterialDescriptorPath, "material descriptor path");
TestHarness.AssertEqual(1, bundle.MaterialDescriptor.Materials.Count, "material count");
```

- [ ] **Step 4: Add migration text assertion**

In `RuntimeMigrationTextTests.RunAll`, add:

```csharp
TestHarness.Run("runtime no longer requires biome authoring resources", RuntimeNoLongerRequiresBiomeAuthoringResources);
```

Add:

```csharp
private static void RuntimeNoLongerRequiresBiomeAuthoringResources()
{
    AssertContains("Terrain/Resources/GameRuntimeResourceBootstrap.cs", "MaterialDescriptorPath");
    string bootstrap = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain", "Resources", "GameRuntimeResourceBootstrap.cs"));
    TestHarness.Assert(!bootstrap.Contains("BiomeMaskPath", StringComparison.Ordinal), "runtime bootstrap should not resolve biome mask");
    TestHarness.Assert(!bootstrap.Contains("BiomeSettingsPath", StringComparison.Ordinal), "runtime bootstrap should not resolve biome settings");
    TestHarness.Assert(!bootstrap.Contains("RuntimeBiomeSettingsReader.ReadFrom", StringComparison.Ordinal), "runtime bootstrap should not validate biome settings");
}
```

- [ ] **Step 5: Run bootstrap tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: bootstrap tests pass, proving runtime terrain loading does not require `biome_mask.png` or `biome_settings.toml`.

- [ ] **Step 6: Commit bootstrap changes**

```powershell
git add -- Terrain/Resources/GameRuntimeResourceBootstrap.cs Terrain/Resources/TerrainRuntimeResourceBundle.cs Terrain.Editor.Tests/VirtualResources/GameRuntimeResourceBootstrapTests.cs Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs
git commit -m "feat: remove runtime biome authoring dependency"
```

---

### Task 5: Add Editor Baked Detail Builder

**Files:**
- Create: `Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs`
- Modify: `Terrain.Editor.Tests/VirtualResources/BakedDetailTerrainFormatTests.cs`

- [ ] **Step 1: Add an editor-only baked detail data type**

Create `Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs` with:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Terrain.Editor.Services;

namespace Terrain.Editor.Services.Export;

internal readonly record struct BakedDetailMapData(
    byte[] IndexData,
    byte[] WeightData,
    int Width,
    int Height);

internal static class BakedDetailMapBuilder
{
    private const int BytesPerPixel = 4;

    public static BakedDetailMapData Generate(
        ushort[] heightData,
        int heightWidth,
        int heightHeight,
        float heightScale,
        byte[] biomeMaskData,
        int biomeMaskWidth,
        int biomeMaskHeight,
        IReadOnlyList<BiomeRuleLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(heightData);
        ArgumentNullException.ThrowIfNull(biomeMaskData);
        ArgumentNullException.ThrowIfNull(layers);
        if (heightWidth <= 0 || heightHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightWidth), "Height dimensions must be positive.");
        if (heightData.Length != heightWidth * heightHeight)
            throw new ArgumentException("Height data length does not match dimensions.", nameof(heightData));
        if (biomeMaskWidth <= 0 || biomeMaskHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(biomeMaskWidth), "Biome mask dimensions must be positive.");
        if (biomeMaskData.Length != biomeMaskWidth * biomeMaskHeight)
            throw new ArgumentException("Biome mask length does not match dimensions.", nameof(biomeMaskData));

        var indexData = new byte[checked(biomeMaskWidth * biomeMaskHeight * BytesPerPixel)];
        var weightData = new byte[indexData.Length];
        var orderedLayers = layers.OrderBy(static layer => layer.PriorityOrder).ToArray();
        int maskToHeightRatio = Math.Max(1, heightWidth / biomeMaskWidth);

        for (int y = 0; y < biomeMaskHeight; y++)
        {
            for (int x = 0; x < biomeMaskWidth; x++)
            {
                DetailPixel pixel = EvaluatePixel(heightData, heightWidth, heightHeight, heightScale, biomeMaskData, biomeMaskWidth, biomeMaskHeight, maskToHeightRatio, orderedLayers, x, y);
                int offset = (y * biomeMaskWidth + x) * BytesPerPixel;
                indexData[offset] = pixel.Index0;
                indexData[offset + 1] = pixel.Index1;
                indexData[offset + 2] = pixel.Index2;
                indexData[offset + 3] = pixel.Index3;
                weightData[offset] = pixel.Weight0;
                weightData[offset + 1] = pixel.Weight1;
                weightData[offset + 2] = pixel.Weight2;
                weightData[offset + 3] = pixel.Weight3;
            }
        }

        return new BakedDetailMapData(indexData, weightData, biomeMaskWidth, biomeMaskHeight);
    }

    private static DetailPixel EvaluatePixel(
        ushort[] heightData,
        int heightWidth,
        int heightHeight,
        float heightScale,
        byte[] biomeMaskData,
        int biomeMaskWidth,
        int biomeMaskHeight,
        int maskToHeightRatio,
        IReadOnlyList<BiomeRuleLayer> layers,
        int maskX,
        int maskY)
    {
        byte biomeId = biomeMaskData[maskY * biomeMaskWidth + maskX];
        ResolveMaskTexelToHeightCoord(maskX, maskY, maskToHeightRatio, heightWidth, heightHeight, out int heightX, out int heightY);
        float altitude = SampleHeightWorld(heightData, heightWidth, heightHeight, heightScale, heightX, heightY);
        float slope = SampleSlopeDegrees(heightData, heightWidth, heightHeight, heightScale, heightX, heightY);
        float direction = SampleDirectionDegrees(heightData, heightWidth, heightHeight, heightScale, heightX, heightY);

        Span<int> bestIndices = stackalloc int[4] { 255, 255, 255, 255 };
        Span<float> bestWeights = stackalloc float[4];
        bool foundLayer = false;
        float remainingWeight = 1.0f;
        int fallbackMaterialIndex = 0;

        for (int layerIndex = layers.Count - 1; layerIndex >= 0; layerIndex--)
        {
            BiomeRuleLayer layer = layers[layerIndex];
            if (!layer.Enabled || !layer.Visible || layer.BiomeId != biomeId)
                continue;

            foundLayer = true;
            fallbackMaterialIndex = layer.MaterialSlotIndex;
            float weight = 1.0f;
            foreach (BiomeModifier modifier in layer.Modifiers)
            {
                if (!modifier.Enabled)
                    continue;

                float modifierValue = EvaluateModifier(heightData, heightWidth, heightHeight, heightScale, maskToHeightRatio, modifier, maskX, maskY, heightX, heightY, altitude, slope, direction);
                if (modifier.Invert > 0.5f)
                    modifierValue = 1.0f - modifierValue;
                float blended = ApplyBlendMode(weight, modifierValue, modifier.BlendMode);
                weight = Lerp(weight, blended, Saturate(modifier.Opacity));
            }

            weight = Saturate(weight);
            float contribution = weight * remainingWeight;
            if (contribution > 0.0f)
            {
                PushTop4(bestIndices, bestWeights, layer.MaterialSlotIndex, contribution);
                remainingWeight *= 1.0f - weight;
            }

            if (remainingWeight <= 0.0001f)
                break;
        }

        if (!foundLayer)
            return DetailPixel.Default;
        if (remainingWeight > 0.0001f)
            PushTop4(bestIndices, bestWeights, fallbackMaterialIndex, remainingWeight);

        float total = MathF.Max(bestWeights[0] + bestWeights[1] + bestWeights[2] + bestWeights[3], 0.0001f);
        return new DetailPixel(
            (byte)Math.Clamp(bestIndices[0], 0, 255),
            (byte)Math.Clamp(bestIndices[1], 0, 255),
            (byte)Math.Clamp(bestIndices[2], 0, 255),
            (byte)Math.Clamp(bestIndices[3], 0, 255),
            EncodeWeight(bestWeights[0], total),
            EncodeWeight(bestWeights[1], total),
            EncodeWeight(bestWeights[2], total),
            EncodeWeight(bestWeights[3], total));
    }

    private static float EvaluateModifier(ushort[] heightData, int width, int height, float heightScale, int ratio, BiomeModifier modifier, int maskX, int maskY, int heightX, int heightY, float altitude, float slope, float direction)
    {
        return modifier.Type switch
        {
            BiomeModifierType.HeightRange => ComputeRangeModifier(altitude, modifier.Min, modifier.Max, modifier.MinFalloff, modifier.MaxFalloff),
            BiomeModifierType.SlopeRange => ComputeRangeModifier(slope, modifier.Min, modifier.Max, modifier.MinFalloff, modifier.MaxFalloff),
            BiomeModifierType.CurvatureRange => ComputeRangeModifier(SampleCurvature(heightData, width, height, heightScale, heightX, heightY, modifier.Radius), modifier.Min, modifier.Max, modifier.MinFalloff, modifier.MaxFalloff),
            BiomeModifierType.DirectionRange => 1.0f - Saturate(MathF.Min(MathF.Abs(direction - modifier.AngleDegrees), 360.0f - MathF.Abs(direction - modifier.AngleDegrees)) / MathF.Max(modifier.AngleRangeDegrees, 0.0001f)),
            BiomeModifierType.Noise => Saturate(Fbm(heightX * MathF.Max(modifier.Scale, 0.0001f) + modifier.OffsetX, heightY * MathF.Max(modifier.Scale, 0.0001f) + modifier.OffsetY, modifier.Seed, modifier.Octaves)),
            BiomeModifierType.TextureMask => 1.0f,
            _ => 1.0f,
        };
    }

    private static void ResolveMaskTexelToHeightCoord(int maskX, int maskY, int ratio, int width, int height, out int x, out int y)
    {
        x = Math.Clamp(maskX * ratio, 0, width - 1);
        y = Math.Clamp(maskY * ratio, 0, height - 1);
    }

    private static float SampleHeightWorld(ushort[] data, int width, int height, float scale, int x, int y)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        return data[y * width + x] * (1.0f / ushort.MaxValue) * scale;
    }

    private static float SampleSlopeDegrees(ushort[] data, int width, int height, float scale, int x, int y)
    {
        float left = SampleHeightWorld(data, width, height, scale, x - 1, y);
        float right = SampleHeightWorld(data, width, height, scale, x + 1, y);
        float up = SampleHeightWorld(data, width, height, scale, x, y - 1);
        float down = SampleHeightWorld(data, width, height, scale, x, y + 1);
        float nx = left - right;
        float nz = up - down;
        const float ny = 2.0f;
        float length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
        return length <= 0.0001f ? 0.0f : MathF.Acos(Math.Clamp(ny / length, -1.0f, 1.0f)) * (180.0f / MathF.PI);
    }

    private static float SampleDirectionDegrees(ushort[] data, int width, int height, float scale, int x, int y)
    {
        float left = SampleHeightWorld(data, width, height, scale, x - 1, y);
        float right = SampleHeightWorld(data, width, height, scale, x + 1, y);
        float up = SampleHeightWorld(data, width, height, scale, x, y - 1);
        float down = SampleHeightWorld(data, width, height, scale, x, y + 1);
        return MathF.Atan2(down - up, right - left) * (180.0f / MathF.PI);
    }

    private static float SampleCurvature(ushort[] data, int width, int height, float scale, int x, int y, float radius)
    {
        int r = Math.Clamp((int)(radius + 0.5f), 1, 16);
        float center = SampleHeightWorld(data, width, height, scale, x, y);
        float left = SampleHeightWorld(data, width, height, scale, x - r, y);
        float right = SampleHeightWorld(data, width, height, scale, x + r, y);
        float up = SampleHeightWorld(data, width, height, scale, x, y - r);
        float down = SampleHeightWorld(data, width, height, scale, x, y + r);
        float denominator = MathF.Max(MathF.Abs(center - left) + MathF.Abs(right - center) + MathF.Abs(center - up) + MathF.Abs(down - center), 0.0001f);
        return Saturate(((center - left) - (right - center) + (center - up) - (down - center)) / denominator * 0.5f + 0.5f);
    }

    private static float ComputeRangeModifier(float value, float min, float max, float minFalloff, float maxFalloff)
    {
        float minWeight = Saturate((value - (min - minFalloff)) / MathF.Max(minFalloff, 0.001f));
        float maxWeight = Saturate(((max + maxFalloff) - value) / MathF.Max(maxFalloff, 0.001f));
        return Saturate(minWeight * maxWeight);
    }

    private static float ApplyBlendMode(float baseWeight, float modifierValue, BiomeModifierBlendMode mode) => Saturate(mode switch
    {
        BiomeModifierBlendMode.Multiply => baseWeight * modifierValue,
        BiomeModifierBlendMode.Add => baseWeight + modifierValue,
        BiomeModifierBlendMode.Subtract => baseWeight - modifierValue,
        BiomeModifierBlendMode.Min => MathF.Min(baseWeight, modifierValue),
        BiomeModifierBlendMode.Max => MathF.Max(baseWeight, modifierValue),
        _ => baseWeight,
    });

    private static void PushTop4(Span<int> indices, Span<float> weights, int materialIndex, float weight)
    {
        for (int i = 0; i < 4; i++)
        {
            if (indices[i] == materialIndex)
            {
                weights[i] += weight;
                return;
            }
        }

        for (int i = 0; i < 4; i++)
        {
            if (weight <= weights[i])
                continue;

            for (int j = 3; j > i; j--)
            {
                weights[j] = weights[j - 1];
                indices[j] = indices[j - 1];
            }

            weights[i] = weight;
            indices[i] = materialIndex;
            return;
        }
    }

    private static byte EncodeWeight(float value, float total) => (byte)Math.Clamp((int)MathF.Round(Saturate(value / total) * byte.MaxValue), 0, byte.MaxValue);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float Saturate(float value) => Math.Clamp(value, 0.0f, 1.0f);

    private static float Fbm(float x, float y, float seed, float octaves)
    {
        int count = Math.Clamp((int)(octaves + 0.5f), 1, 8);
        float amplitude = 0.5f;
        float frequency = 1.0f;
        float sum = 0.0f;
        float normalization = 0.0f;
        for (int octave = 0; octave < count; octave++)
        {
            sum += Noise2D(x * frequency, y * frequency, seed + octave * 17.0f) * amplitude;
            normalization += amplitude;
            frequency *= 2.0f;
            amplitude *= 0.5f;
        }

        return normalization > 0.0001f ? sum / normalization : 0.0f;
    }

    private static float Noise2D(float x, float y, float seed)
    {
        float ix = MathF.Floor(x);
        float iy = MathF.Floor(y);
        float fx = x - ix;
        float fy = y - iy;
        float a = Hash11(ix * 127.1f + iy * 311.7f + seed);
        float b = Hash11((ix + 1.0f) * 127.1f + iy * 311.7f + seed);
        float c = Hash11(ix * 127.1f + (iy + 1.0f) * 311.7f + seed);
        float d = Hash11((ix + 1.0f) * 127.1f + (iy + 1.0f) * 311.7f + seed);
        float ux = fx * fx * (3.0f - 2.0f * fx);
        float uy = fy * fy * (3.0f - 2.0f * fy);
        return Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uy);
    }

    private static float Hash11(float n)
    {
        float value = MathF.Sin(n) * 43758.5453123f;
        return value - MathF.Floor(value);
    }

    private readonly record struct DetailPixel(byte Index0, byte Index1, byte Index2, byte Index3, byte Weight0, byte Weight1, byte Weight2, byte Weight3)
    {
        public static readonly DetailPixel Default = new(0, 255, 255, 255, 255, 0, 0, 0);
    }
}
```

- [ ] **Step 2: Add baker behavior test**

Add this `RunAll` entry in `BakedDetailTerrainFormatTests`:

```csharp
TestHarness.Run("editor baked detail builder emits RGBA index and weight maps", EditorBakedDetailBuilderEmitsRgbaIndexAndWeightMaps);
```

Add:

```csharp
private static void EditorBakedDetailBuilderEmitsRgbaIndexAndWeightMaps()
{
    var service = Terrain.Editor.Services.BiomeRuleService.Instance;
    service.ClearAll();
    service.AddBiomeFromConfig(1, "Default", new System.Numerics.Vector4(0, 1, 0, 1));
    service.AddLayerFromConfig(1, "Base", true, 0.0f, 1000.0f, 0.0f, 90.0f, 1.0f, 7);

    ushort[] height = new ushort[16];
    byte[] mask = { 1, 1, 1, 1 };

    var data = Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
        height,
        4,
        4,
        100.0f,
        mask,
        2,
        2,
        service.Layers);

    TestHarness.AssertEqual(2, data.Width, "detail width");
    TestHarness.AssertEqual(2, data.Height, "detail height");
    TestHarness.AssertEqual(16, data.IndexData.Length, "index byte length");
    TestHarness.AssertEqual(16, data.WeightData.Length, "weight byte length");
    TestHarness.AssertEqual(7, data.IndexData[0], "first material index");
    TestHarness.AssertEqual(byte.MaxValue, data.WeightData[0], "first material weight");
}
```

- [ ] **Step 3: Run baker test**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: baker test passes.

- [ ] **Step 4: Commit editor baker**

```powershell
git add -- Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs Terrain.Editor.Tests/VirtualResources/BakedDetailTerrainFormatTests.cs
git commit -m "feat: bake detail maps during editor export"
```

---

### Task 6: Write Baked Detail VT Payloads During Export

**Files:**
- Modify: `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`
- Modify: `Terrain.Editor.Tests/VirtualResources/BakedDetailTerrainFormatTests.cs`

- [ ] **Step 1: Add export format test**

Add this `RunAll` entry:

```csharp
TestHarness.Run("terrain exporter source writes detail index and weight VT payloads", TerrainExporterSourceWritesDetailIndexAndWeightVtPayloads);
```

Add:

```csharp
private static void TerrainExporterSourceWritesDetailIndexAndWeightVtPayloads()
{
    string exporter = ReadRepoText("Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs");
    TestHarness.Assert(exporter.Contains("BakedDetailMapBuilder.Generate", StringComparison.Ordinal), "exporter should bake detail maps");
    TestHarness.Assert(exporter.Contains("Writing DetailIndex VT data", StringComparison.Ordinal), "export progress should mention detail index");
    TestHarness.Assert(exporter.Contains("Writing DetailWeight VT data", StringComparison.Ordinal), "export progress should mention detail weight");
    TestHarness.Assert(exporter.Contains("BytesPerPixel = 4", StringComparison.Ordinal), "detail VT payloads should be RGBA8");
    TestHarness.Assert(!exporter.Contains("Writing BiomeMask VT data", StringComparison.Ordinal), "exporter should not write biome mask VT");
}
```

- [ ] **Step 2: Bake detail before writing**

In `TerrainExporter.ExportAsync`, after `biomeMaskData` and dimensions are captured, add:

```csharp
IReadOnlyList<BiomeRuleLayer> layersSnapshot = BiomeRuleService.Instance.Layers
    .Select(CloneLayer)
    .ToArray();
float heightScale = tm.HeightScale;
```

Add helper in `TerrainExporter`:

```csharp
private static BiomeRuleLayer CloneLayer(BiomeRuleLayer source)
{
    var clone = new BiomeRuleLayer
    {
        Id = source.Id,
        Name = source.Name,
        Enabled = source.Enabled,
        Visible = source.Visible,
        BiomeId = source.BiomeId,
        MaterialSlotIndex = source.MaterialSlotIndex,
        PriorityOrder = source.PriorityOrder,
    };

    foreach (BiomeModifier modifier in source.Modifiers)
        clone.Modifiers.Add(modifier.Clone());

    return clone;
}
```

Inside `Task.Run`, before `WriteTerrainFile`, add:

```csharp
progress.Report(ExportProgress.Running(1, 6, "Baking DetailTexture..."));
var bakedDetail = BakedDetailMapBuilder.Generate(
    heightData,
    width,
    height,
    heightScale,
    biomeMaskData,
    biomeMaskWidth,
    biomeMaskHeight,
    layersSnapshot);
```

Pass `bakedDetail` into `WriteTerrainFile`.

- [ ] **Step 3: Update terrain header**

In `WriteTerrainFile`, set:

```csharp
DetailMapFormat = (int)VTFormat.Rgba32,
DetailMapMipLevels = detailMapMipLevels,
DetailMapResolutionRatio = 2,
```

Remove old `SplatMapFormat`, `SplatMapMipLevels`, and `SplatMapResolutionRatio` assignments.

- [ ] **Step 4: Add a packed RGBA8 detail pixel type**

Add:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
private readonly struct DetailControlPixel
{
    public DetailControlPixel(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public readonly byte R;
    public readonly byte G;
    public readonly byte B;
    public readonly byte A;
}
```

Add:

```csharp
private static DetailControlPixel[] ToDetailPixels(byte[] source)
{
    if (source.Length % 4 != 0)
        throw new ArgumentException("Detail control data length must be a multiple of 4.", nameof(source));

    var pixels = new DetailControlPixel[source.Length / 4];
    for (int i = 0; i < pixels.Length; i++)
    {
        int offset = i * 4;
        pixels[i] = new DetailControlPixel(source[offset], source[offset + 1], source[offset + 2], source[offset + 3]);
    }

    return pixels;
}
```

- [ ] **Step 5: Write detail payloads**

Replace the old BiomeMask payload block with:

```csharp
progress.Report(ExportProgress.Running(5, 6, "Writing DetailIndex VT data..."));
var detailIndexHeader = new VTHeader
{
    Width = bakedDetail.Width,
    Height = bakedDetail.Height,
    TileSize = DefaultTileSize,
    Padding = SplatMapPadding,
    BytesPerPixel = 4,
    Mipmaps = detailMapMipLevels,
};
WriteStruct(writer, ref detailIndexHeader);
StreamMipLevels<DetailControlPixel>(writer, ToDetailPixels(bakedDetail.IndexData), bakedDetail.Width, bakedDetail.Height, DefaultTileSize, SplatMapPadding, ct);

progress.Report(ExportProgress.Running(6, 6, "Writing DetailWeight VT data..."));
var detailWeightHeader = detailIndexHeader;
WriteStruct(writer, ref detailWeightHeader);
StreamMipLevels<DetailControlPixel>(writer, ToDetailPixels(bakedDetail.WeightData), bakedDetail.Width, bakedDetail.Height, DefaultTileSize, SplatMapPadding, ct);
```

- [ ] **Step 6: Run exporter tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: export source tests pass.

- [ ] **Step 7: Commit exporter changes**

```powershell
git add -- Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs Terrain.Editor.Tests/VirtualResources/BakedDetailTerrainFormatTests.cs
git commit -m "feat: export baked detail VT payloads"
```

---

### Task 7: Remove Runtime Detail Generation Code

**Files:**
- Delete: `Terrain/Materials/RuntimeDetailMapBuilder.cs`
- Delete: `Terrain/Materials/TerrainDetailGeneration.cs`
- Modify: `Terrain/Terrain.csproj` if the project explicitly includes these files

- [ ] **Step 1: Delete runtime generation files**

Use normal filesystem deletion or `apply_patch` delete hunks for:

```text
Terrain/Materials/RuntimeDetailMapBuilder.cs
Terrain/Materials/TerrainDetailGeneration.cs
```

- [ ] **Step 2: Check project includes**

Run:

```powershell
rg -n "RuntimeDetailMapBuilder|TerrainDetailGeneration" Terrain Terrain.Editor Terrain.Editor.Tests
```

Expected: no results.

- [ ] **Step 3: Run build**

Run:

```powershell
dotnet build Terrain\Terrain.csproj --no-restore
```

Expected: build passes with existing warnings only.

- [ ] **Step 4: Commit cleanup**

```powershell
git add -- Terrain/Materials/RuntimeDetailMapBuilder.cs Terrain/Materials/TerrainDetailGeneration.cs Terrain/Terrain.csproj
git commit -m "refactor: remove runtime detail generation"
```

---

### Task 8: Documentation, Session Log, and Final Verification

**Files:**
- Modify: `docs/ARCHITECTURE_OVERVIEW.md`
- Modify: `docs/CURRENT_FEATURES.md`
- Modify: `docs/design/map-data-toml-formats.md`
- Create: `docs/log/2026/06/22/2026-06-22-baked-detail-texture-export.md`

- [ ] **Step 1: Update architecture docs**

In `docs/ARCHITECTURE_OVERVIEW.md`, replace text that says Runtime builds detail maps from BiomeMask with:

```markdown
Runtime DetailMap is baked during Editor Export into `.terrain` v8 as two RGBA8 VT payloads: DetailIndexMap and DetailWeightMap. Runtime streams those baked detail pages directly and no longer evaluates biome rules or loads `biome_mask.png` / `biome_settings.toml` for terrain detail construction.
```

- [ ] **Step 2: Update feature list**

In `docs/CURRENT_FEATURES.md`, update `Runtime DetailMap 构建` to:

```markdown
| Runtime DetailMap | ✅ | `Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs`, `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`, `Terrain/Streaming/TerrainStreaming.cs` | Editor Export writes baked DetailIndex/DetailWeight RGBA8 VT payloads into `.terrain` v8. Runtime streams those pages directly and no longer constructs detail maps at startup. |
```

- [ ] **Step 3: Update map data format doc**

In `docs/design/map-data-toml-formats.md`, change Runtime consumption notes to state:

```markdown
- `biome_settings.toml`
  - Editor 作者态和 Export 使用其中的 `material_id` 与 modifier 定义烘焙 `.terrain` v8 的 DetailIndex/DetailWeight VT。
  - Runtime terrain detail loading no longer consumes this file.
```

Keep material descriptor Runtime notes because material texture arrays still come from descriptor.

- [ ] **Step 4: Add session log**

Create `docs/log/2026/06/22/2026-06-22-baked-detail-texture-export.md`:

```markdown
# Baked DetailTexture Export
**Date**: 2026-06-22
**Status**: Complete
**Priority**: High

---

## Session Goal

Move terrain DetailTexture construction out of Runtime startup and into Editor Export.

---

## What Changed

- `.terrain` is now v8.
- `.terrain` no longer stores BiomeMask VT.
- Editor Export bakes DetailIndex and DetailWeight RGBA8 VT payloads.
- Runtime streams baked detail pages directly from `.terrain`.
- Runtime terrain loading no longer requires `biome_mask.png` or `biome_settings.toml`.
- Runtime detail generation code was removed.

---

## Verification

- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
- `dotnet build Terrain\Terrain.csproj --no-restore`
- `dotnet build Terrain.Editor\Terrain.Editor.csproj --no-restore`
- `git diff --check`

---

## Notes

Existing v6/v7 `.terrain` files must be re-exported.
```

- [ ] **Step 5: Full verification**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet build Terrain\Terrain.csproj --no-restore
dotnet build Terrain.Editor\Terrain.Editor.csproj --no-restore
git diff --check
```

Expected: all tests/builds pass; existing NuGet advisory or compiler warnings may remain if unchanged.

- [ ] **Step 6: Commit docs and verification result**

```powershell
git add -- docs/ARCHITECTURE_OVERVIEW.md docs/CURRENT_FEATURES.md docs/design/map-data-toml-formats.md docs/log/2026/06/22/2026-06-22-baked-detail-texture-export.md
git commit -m "docs: record baked detail texture export"
```

---

## Self-Review

- Spec coverage: v8 format, no BiomeMask VT, Editor-only generation, Runtime direct page reads, bootstrap dependency removal, old version rejection, tests, docs, and session log are covered by Tasks 1-8.
- Placeholder scan: no unresolved placeholder instructions are present.
- Type consistency: plan uses `DetailIndexMapHeader`, `DetailWeightMapHeader`, `DetailMapResolutionRatio`, `ReadDetailIndexPage`, and `ReadDetailWeightPage` consistently across reader, streaming, and tests.
- Risk note: `BakedDetailMapBuilder` duplicates the old runtime evaluator behavior under Editor. During implementation, compare output against current `RuntimeDetailMapBuilder` before deleting it if extra confidence is needed, but do not keep runtime generation in final source.
