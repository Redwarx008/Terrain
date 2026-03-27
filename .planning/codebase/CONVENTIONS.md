# Coding Conventions

**Analysis Date:** 2026-03-27

## Naming Patterns

**Files:**
- PascalCase for all C# files (e.g., `TerrainComponent.cs`, `TerrainRenderFeature.cs`)
- Descriptive names matching the primary type defined within
- Shader effect files use `.sdsl` extension with generated `.sdsl.cs` counterparts

**Classes and Structs:**
- PascalCase for all type names
- Prefixed with domain when appropriate: `TerrainComponent`, `TerrainRenderObject`, `TerrainChunkNode`
- Internal implementation details marked `internal` or `private`
- Sealed classes preferred unless inheritance is explicitly designed: `sealed class TerrainProcessor`

**Interfaces:**
- Not explicitly observed in codebase (relies on Stride framework interfaces)

**Methods:**
- PascalCase for all methods: `GenerateComponentData`, `UpdateRenderObject`
- Private helper methods use PascalCase: `TryLoadTerrainData`, `ValidateTerrainDataPath`
- Async methods not prevalent (uses threaded approach with `TerrainStreamingManager`)

**Properties:**
- PascalCase for all properties
- Auto-properties preferred: `public VisibilityGroup VisibilityGroup { get; set; } = null!;`
- Nullable reference types annotated: `public string? TerrainDataPath { get; set; }`

**Fields:**
- Private fields: camelCase with underscore prefix OR PascalCase for static readonly
- Constants: PascalCase or ALL_CAPS for const values
```csharp
private const float DiffuseWorldRepeatSize = 8.0f;
private static readonly Logger Log = GlobalLogger.GetLogger("Quantum");
private readonly ThreadLocal<DescriptorSet[]> descriptorSets = new();
```

**Variables:**
- camelCase for local variables and parameters
- Descriptive names: `chunkNodeCapacity`, `lodLookupEntryCount`

## Code Style

**Nullable Reference Types:**
- All files start with `#nullable enable`
- Nullable annotations used throughout: `string?`, `Texture?`
- Null-forgiving operator used where appropriate: `null!`

**Formatting:**
- 4-space indentation
- Opening braces on same line (K&R style)
- Single blank line between members
- No trailing whitespace

**Expression-bodied members:**
- Used for simple one-liners:
```csharp
public int Capacity => slots.Length;
public bool IsPageResident(TerrainPageKey key) => pageToSlice.ContainsKey(key);
```

**Pattern Matching:**
- Modern C# switch expressions used:
```csharp
return renderer switch
{
    LightAmbientRenderer => new LightAmbientRenderer(),
    LightDirectionalGroupRenderer => new LightDirectionalGroupRenderer(),
    _ => null,
};
```

**Records:**
- Used for immutable data structures:
```csharp
internal readonly record struct LoadedTerrainData(...);
internal readonly record struct TerrainMipLayout(int Width, int Height, int TilesX, int TilesY, long Offset);
```

## Import Organization

**Order:**
1. System namespaces
2. Third-party framework (Stride)
3. Project namespaces

**Example:**
```csharp
using System;
using System.Diagnostics;
using System.IO;
using Stride.Core.Annotations;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Terrain.Shared;  // Project namespace last
```

**Alias usage:**
- Used for disambiguation: `using Buffer = Stride.Graphics.Buffer;`
- Global namespace for generated code: `global::System.ReadOnlySpan<TerrainChunkNode>`

## Error Handling

**Patterns:**
- Try-Parse pattern for recoverable failures: `TryLoadTerrainData`, `TryGetResidentSlice`
- Early returns with logging for validation failures
- Exception handling at boundaries (file I/O, external resources)

**Example:**
```csharp
private bool TryLoadTerrainData(TerrainComponent component, out LoadedTerrainData loadedData)
{
    loadedData = default;
    try
    {
        // ... validation and loading
        return true;
    }
    catch (Exception exception)
    {
        Log.Warning($"Terrain data could not be read: {exception.Message}");
        return false;
    }
}
```

**Validation:**
- Guard clauses at method entry
- `Debug.Assert` for internal invariants (stripped in release builds)
- `ArgumentOutOfRangeException`, `InvalidDataException` for invalid inputs

## Logging

**Framework:** Stride's `GlobalLogger`

**Patterns:**
- Static readonly logger per class: `private static readonly Logger Log = GlobalLogger.GetLogger("Quantum");`
- Log level appropriate to message severity
- Warning for recoverable errors, Info for diagnostics

**Example:**
```csharp
Log.Warning("Terrain component is missing TerrainDataPath.");
Log.Warning($"Terrain data could not be read: {exception.Message}");
Log.Info("Terrain streaming thread exited.");
```

## Comments

**When to Comment:**
- Complex algorithms explained in detail
- Non-obvious design decisions
- Workarounds for framework limitations

**Example:**
```csharp
// The terrain can be drawn by shadow views that see different chunks than the main camera,
// so shrinking bounds to the current selection would cull shadow-casting terrain too early.
```

**Chinese Comments:**
- Editor UI code contains Chinese comments for UI elements (e.g., `// 主窗口 - 编辑器主界面`)
- This is intentional for the editor's Chinese localization context

**XML Documentation:**
- Minimal use in runtime code
- Used in Editor UI framework for IntelliSense support

## Function Design

**Size:**
- Methods kept focused (generally under 50 lines)
- Large initialization logic broken into helper methods

**Parameters:**
- `ref` and `out` used for performance-critical structs
- `in` parameters not observed
- Nullable parameters clearly annotated

**Return Values:**
- Tuple returns for multiple values: `(int RenderCount, int NodeCount)`
- `out` parameters for Try-pattern methods

## Module Design

**Namespace Organization:**
- Flat namespace for core terrain: `namespace Terrain;`
- Sub-namespaces for Editor UI: `namespace Terrain.Editor.UI.Controls;`

**Access Modifiers:**
- `internal` for implementation details
- `public` for API surface
- `sealed` for classes not designed for inheritance

**Disposable Pattern:**
- `IDisposable` implemented for resource-heavy types
- Proper cleanup of GPU resources, file handles, threads

**Example:**
```csharp
internal sealed class TerrainStreamingManager : IDisposable
{
    public void Dispose()
    {
        cancellation.Cancel();
        pendingRequests.CompleteAdding();
        ioThread.Join();
        // ... cleanup
    }
}
```

## Unsafe Code

**Usage:**
- `unsafe` blocks for native memory operations
- `NativeMemory.Alloc/Free` for page buffer allocation
- `MemoryMarshal` for span conversions

**Example:**
```csharp
internal sealed unsafe class PageBufferAllocator : IDisposable
{
    nint memory = (nint)NativeMemory.Alloc((nuint)bytesPerPage);
}
```

## Struct Layout

**Explicit Layout:**
- Used for GPU-bound structures:
```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct TerrainChunkNode
{
    public Int4 NodeInfo;    // chunkX, chunkY, lodLevel, state
    public Int4 StreamInfo;  // sliceIndex, pageOffsetX, pageOffsetY, pageTexelStride
}
```

---

*Convention analysis: 2026-03-27*
