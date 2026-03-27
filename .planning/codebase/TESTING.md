# Testing Patterns

**Analysis Date:** 2026-03-27

## Test Framework

**Status:** No formal test projects detected

**Test Projects:** None found (no `*.Tests.csproj`, no `*Test*.cs` files)

**Build Configuration:**
- DEBUG builds include additional validation and debug output
- Release builds strip `Debug.Assert` calls

## Testing Approach

**Current Strategy:**
This codebase relies primarily on:
1. **Debug Assertions** - Extensive use of `Debug.Assert` for internal invariants
2. **Defensive Programming** - Try-pattern methods with boolean returns
3. **Runtime Validation** - File format validation, bounds checking
4. **Manual Testing** - Through the Terrain.Editor application

## Debug Assertion Usage

**Pattern:**
`Debug.Assert` used extensively to validate preconditions and invariants:

```csharp
// In TerrainRenderFeature.cs
Debug.Assert(component.QuadTree != null);
Debug.Assert(component.ChunkNodeData.Length > 0);
Debug.Assert(renderObject.ChunkNodeBuffer != null);

// In TerrainComputeDispatcher.cs
Debug.Assert(buildLodLookupEffect != null);
Debug.Assert(renderObject.LodMapTexture != null);

// In TerrainQuadTree.cs
Debug.Assert(minMaxErrorMaps.Length > 0);
Debug.Assert(leafChunkSize > 0);
```

**Conditional Compilation:**
Some assertions wrapped in `[Conditional("DEBUG")]` methods:
```csharp
[Conditional("DEBUG")]
private void ValidateConfiguration()
{
    Debug.Assert(terrainRenderFeature != null);
    // ...
}
```

## Validation Patterns

**File Format Validation:**
```csharp
private static void ValidateHeader(TerrainFileHeader header, int mapCount)
{
    if (header.Version != TerrainFileHeader.SupportedVersion)
    {
        throw new InvalidDataException($"Unsupported terrain file version {header.Version}.");
    }
    // ... additional validation
}
```

**Bounds Checking:**
```csharp
public void ReadHeightPage(TerrainPageKey key, Span<byte> destination)
{
    if ((uint)key.MipLevel >= (uint)mipLayouts.Length)
    {
        throw new ArgumentOutOfRangeException(nameof(key), $"Invalid mip level {key.MipLevel}.");
    }
    // ...
}
```

**Try-Pattern for Recoverable Failures:**
```csharp
public bool TryGetResidentSlice(TerrainPageKey key, out int sliceIndex)
{
    if (pageToSlice.TryGetValue(key, out sliceIndex))
    {
        TouchSlice(sliceIndex);
        return true;
    }
    sliceIndex = -1;
    return false;
}
```

## Debug Output

**DEBUG-only Features:**

The TerrainPreProcessor project includes DEBUG-only debug output:

```csharp
#if DEBUG
string debugDir = GetDebugOutputDirectory(config);
MipmapDebugOutput.SaveMipmapLevel(currentMip, debugDir, "HeightMap", mip);
#endif
```

**Debug Renderer:**
- `TerrainWireframeModeController` provides runtime debug visualization
- Toggles wireframe rendering with hotkey
- Displays debug overlay with current state

## Test Coverage Gaps

**Areas Without Automated Tests:**

1. **Terrain Streaming Logic**
   - File: `Terrain/Streaming/TerrainStreaming.cs`
   - Risk: LRU eviction, page allocation, async I/O
   - Current: Manual testing through gameplay

2. **Quad Tree Selection**
   - File: `Terrain/Rendering/TerrainQuadTree.cs`
   - Risk: LOD selection, frustum culling, node subdivision
   - Current: Visual verification in editor

3. **Compute Shader Dispatch**
   - File: `Terrain/Rendering/TerrainComputeDispatcher.cs`
   - Risk: Thread group counts, resource barriers
   - Current: GPU validation layers

4. **File Format Reading**
   - File: `Terrain/Streaming/TerrainStreaming.cs` (TerrainFileReader)
   - Risk: Binary format compatibility, endianness
   - Current: Format validation on load

5. **Editor UI Components**
   - Files: `Terrain.Editor/UI/**/*.cs`
   - Risk: Layout calculations, input handling
   - Current: Interactive manual testing

## Recommended Testing Strategy

**Unit Test Candidates:**

1. **VirtualTextureLayout**
   - Pure functions, no dependencies
   - Test: `GetMipCount`, `GetMipLayout`, `ComputeTileCount`

2. **TerrainConfig**
   - Record struct with equality
   - Test: Equality, hash code, capture behavior

3. **PageBufferAllocator**
   - Memory management logic
   - Test: Rent/Return cycles, exhaustion handling

4. **TerrainFileReader validation**
   - Header validation logic
   - Test: Invalid headers, version mismatches

**Integration Test Candidates:**

1. **Terrain Streaming Pipeline**
   - Setup: Mock file system
   - Test: Request → Load → Upload → Render cycle

2. **Quad Tree Selection**
   - Setup: Synthetic heightmap data
   - Test: LOD selection matches expected behavior

**Manual Testing Checklist:**

```markdown
- [ ] Terrain loads from .terrain file
- [ ] LOD transitions are smooth
- [ ] Wireframe toggle works
- [ ] Streaming keeps up with camera movement
- [ ] Editor UI panels render correctly
- [ ] No memory leaks during extended play
```

## Profiling and Diagnostics

**Built-in Profiling:**
```csharp
private static readonly ProfilingKey ExtractKey = new("TerrainRenderFeature.Extract");
private static readonly ProfilingKey DrawKey = new("TerrainRenderFeature.Draw");

using var _ = Profiler.Begin(DrawKey);
// ... work
```

**Stride Profiler Integration:**
- Custom profiling keys for terrain-specific operations
- Visible in Stride's built-in profiler

---

*Testing analysis: 2026-03-27*
