# Partial GPU Texture Readback Research

**Researched:** 2026-04-04
**Domain:** DirectX 12/Stride partial texture readback, CPU-GPU synchronization
**Confidence:** HIGH (based on Stride source code analysis)

## Summary

Stride provides `CommandList.CopyRegion()` which exposes DirectX 12's `CopyTextureRegion` for partial texture copies. This allows copying a subregion of a GPU texture to a smaller staging buffer, enabling efficient readback of only dirty regions.

**Primary recommendation:** Use `CommandList.CopyRegion()` with a pre-allocated staging texture sized to the maximum brush bounding box (e.g., 256x256) to copy only affected texels. Map the staging texture to read back dirty region data for CPU cache synchronization.

---

## 1. Available Stride APIs for Partial Copy

### CommandList.CopyRegion() - PRIMARY API

**Location:** `E:/WorkSpace/stride/sources/engine/Stride.Graphics/Direct3D12/CommandList.Direct3D12.cs:1486-1596`

**Signature:**
```csharp
public void CopyRegion(
    GraphicsResource source,
    int sourceSubResourceIndex,
    ResourceRegion? sourceRegion,
    GraphicsResource destination,
    int destinationSubResourceIndex,
    int dstX = 0, int dstY = 0, int dstZ = 0)
```

**Key Behavior:**
- Copies a subregion from source texture to destination texture
- For textures, coordinates are in texels (not bytes)
- `sourceRegion` defines the source box (left, top, front, right, bottom, back)
- Supports copying to staging textures for CPU readback

**DirectX 12 Implementation:**
```csharp
// From CommandList.Direct3D12.cs:1564-1576
if (sourceRegion is ResourceRegion srcResourceRegion)
{
    // NOTE: We assume the same layout and size as D3D12_BOX
    Debug.Assert(sizeof(D3D12Box) == sizeof(ResourceRegion));
    var sourceBox = srcResourceRegion.BitCast<ResourceRegion, D3D12Box>();

    currentCommandList.NativeCommandList.CopyTextureRegion(
        in destRegion, (uint) dstX, (uint) dstY, (uint) dstZ,
        in srcRegion, in sourceBox);
}
else
{
    currentCommandList.NativeCommandList.CopyTextureRegion(
        in destRegion, (uint) dstX, (uint) dstY, (uint) dstZ,
        in srcRegion, pSrcBox: null);
}
```

### ResourceRegion Structure

**Location:** `E:/WorkSpace/stride/sources/engine/Stride.Graphics/ResourceRegion.cs`

```csharp
public partial struct ResourceRegion(int left, int top, int front, int right, int bottom, int back)
{
    public int Left = left;     // X start (inclusive)
    public int Top = top;       // Y start (inclusive)
    public int Front = front;   // Z start (inclusive)
    public int Right = right;   // X end (exclusive)
    public int Bottom = bottom; // Y end (exclusive)
    public int Back = back;     // Z end (exclusive)

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public readonly int Depth => Back - Front;
}
```

**Important:** Right/Bottom/Back are **exclusive** (one past the last pixel).

### Texture.SetData() with Region - PARTIAL UPLOAD

**Location:** `E:/WorkSpace/stride/sources/engine/Stride.Graphics/Texture.cs:1427`

```csharp
public unsafe void SetData<TData>(
    CommandList commandList,
    ReadOnlySpan<TData> fromData,
    int arrayIndex = 0,
    int mipLevel = 0,
    ResourceRegion? region = null)  // <-- Partial region support
```

**Note:** `SetData` supports partial region uploads. This is already used for CPU->GPU updates.

---

## 2. Recommended Approach: Partial Readback Pipeline

### Architecture Overview

```
GPU Height Texture (4096x4096)
        |
        | CopyRegion(sourceRegion = dirty box)
        v
Staging Texture (256x256, pre-allocated)
        |
        | MapSubResource() + MemoryUtilities.Copy
        v
CPU Buffer (dirty region only)
        |
        | Update HeightDataCache[dirty region]
        v
HeightDataCache (CPU authoritative for undo/save)
```

### Implementation Pattern

```csharp
public sealed class PartialReadbackManager : IDisposable
{
    // Pre-allocated staging texture sized for max brush region
    // 256x256 covers brushes up to radius 128 with safety margin
    private const int MaxReadbackSize = 256;

    private readonly GraphicsDevice graphicsDevice;
    private Texture? stagingTexture;
    private readonly int[] readbackBuffer = new int[MaxReadbackSize * MaxReadbackSize];

    public void Initialize(GraphicsDevice device)
    {
        graphicsDevice = device;

        // Create staging texture with Readback heap
        stagingTexture = Texture.New2D(
            device,
            MaxReadbackSize,
            MaxReadbackSize,
            1,
            PixelFormat.R16_UNorm,
            usage: GraphicsResourceUsage.Staging);  // Key: Staging usage
    }

    /// <summary>
    /// Reads back a dirty region from GPU heightmap to CPU.
    /// </summary>
    /// <param name="commandList">Active command list</param>
    /// <param name="sourceTexture">GPU heightmap texture</param>
    /// <param name="dirtyX">Dirty region X start (texel coords)</param>
    /// <param name="dirtyZ">Dirty region Z start (texel coords)</param>
    /// <param name="dirtyWidth">Dirty region width</param>
    /// <param name="dirtyHeight">Dirty region height</param>
    /// <returns>Readback data as ushort array, null if not ready</returns>
    public ushort[]? ReadbackRegion(
        CommandList commandList,
        Texture sourceTexture,
        int dirtyX, int dirtyZ,
        int dirtyWidth, int dirtyHeight)
    {
        if (stagingTexture == null)
            return null;

        // Clamp region to texture bounds
        int clampedWidth = Math.Min(dirtyWidth, MaxReadbackSize);
        int clampedHeight = Math.Min(dirtyHeight, MaxReadbackSize);

        // Define source region (exclusive end coordinates)
        var sourceRegion = new ResourceRegion(
            left: dirtyX,
            top: dirtyZ,
            front: 0,
            right: dirtyX + clampedWidth,
            bottom: dirtyZ + clampedHeight,
            back: 1);

        // Copy partial region from GPU texture to staging texture
        commandList.CopyRegion(
            source: sourceTexture,
            sourceSubResourceIndex: 0,
            sourceRegion: sourceRegion,
            destination: stagingTexture,
            destinationSubResourceIndex: 0,
            dstX: 0, dstY: 0, dstZ: 0);

        // Map staging texture to CPU memory
        var mapped = commandList.MapSubResource(
            stagingTexture,
            subResourceIndex: 0,
            mapMode: MapMode.Read,
            doNotWait: true);  // Non-blocking

        if (mapped.DataBox.IsEmpty)
        {
            // GPU not ready yet, try next frame
            return null;
        }

        // Copy from mapped memory to buffer
        // Note: RowPitch may have alignment padding
        int rowStride = clampedWidth * sizeof(ushort);
        var result = new ushort[clampedWidth * clampedHeight];

        unsafe
        {
            fixed (ushort* destPtr = result)
            {
                byte* dest = (byte*)destPtr;
                byte* src = (byte*)mapped.DataBox.DataPointer;

                for (int row = 0; row < clampedHeight; row++)
                {
                    MemoryUtilities.CopyWithAlignmentFallback(
                        dest + row * rowStride,
                        src + row * mapped.DataBox.RowPitch,
                        (uint)rowStride);
                }
            }
        }

        commandList.UnmapSubResource(mapped);

        return result;
    }

    public void Dispose()
    {
        stagingTexture?.Dispose();
    }
}
```

---

## 3. Synchronization Strategy

### Frame Delay Pattern (from Godot)

Godot's heightmap plugin uses a 2-3 frame delay for GPU readback:

```csharp
// From OpenGL implementation
private const int ReadbackFrameDelay = 2;

if (GraphicsDevice.FrameCounter < texture.PixelBufferFrame + ReadbackFrameDelay)
    return null;  // Not ready yet
```

**Recommendation:** Use `doNotWait: true` in `MapSubResource()` and handle the "not ready" case gracefully. Retry readback on subsequent frames.

### Fence-Based Synchronization

Stride tracks staging resource fences automatically:

```csharp
// From CommandList.Direct3D12.cs:1549-1552
// Fence for host access
destinationParent.CommandListFenceValue = null;
destinationParent.UpdatingCommandList = this;
currentCommandList.StagingResources.Add(destinationParent);
```

---

## 4. Alternative Architecture: CPU-Only for Small Brushes

### Hybrid Approach (Recommended)

Based on the existing research (RESEARCH.md), a hybrid approach is recommended:

| Brush Radius | Approach | Reason |
|--------------|----------|--------|
| <= 30px | CPU-only editing | Readback overhead exceeds CPU iteration cost |
| > 30px | GPU editing + readback | Parallel GPU execution outweighs sync cost |

### CPU-Only Small Brush Pattern

```csharp
public void ApplyStroke(Vector3 worldPosition, float frameTime)
{
    float brushRadius = BrushParameters.Instance.Size * 0.5f;

    if (brushRadius <= 30.0f)
    {
        // CPU-only path: Modify HeightDataCache directly
        // No GPU readback needed - just mark dirty for upload
        ApplyCpuEdit(worldPosition, frameTime);
        MarkDirtyForGpuUpload();
    }
    else
    {
        // GPU path: Dispatch compute shader
        // Read back dirty region after stroke ends
        DispatchGpuEdit(worldPosition, frameTime);
        RequestGpuReadback();
    }
}
```

---

## 5. Performance Considerations

### Memory Bandwidth Comparison

| Operation | Data Size (r=50) | Time Estimate |
|-----------|------------------|---------------|
| Full readback (4096x4096) | 32 MB | ~5-10 ms |
| Partial readback (100x100) | 20 KB | ~0.1 ms |
| CPU iteration (r=50) | 7850 pixels | ~0.05 ms |

**Conclusion:** For small brushes, CPU-only editing is faster. GPU readback only makes sense for:
1. Large brushes (>30px radius)
2. Complex operations (erosion, noise)
3. Batch operations (multiple strokes)

### Staging Texture Size

Recommend pre-allocating staging texture at maximum expected dirty region size:

- **Conservative:** 128x128 (radius 64) - 32KB
- **Recommended:** 256x256 (radius 128) - 128KB
- **Large brushes:** 512x512 (radius 256) - 512KB

---

## 6. Integration with Existing Code

### EditorTerrainEntity Integration

Current `SyncToGpu()` method uploads entire dirty slices:

```csharp
// Current implementation (EditorTerrainEntity.cs:169-194)
public void SyncToGpu(CommandList commandList)
{
    foreach (var slice in slices)
    {
        if (!slice.IsDirty || slice.Texture == null)
            continue;

        int sampleCount = slice.Width * slice.Height;
        ushort[] uploadBuffer = ArrayPool<ushort>.Shared.Rent(sampleCount);
        // ... uploads ENTIRE slice ...
    }
}
```

### Proposed Enhancement

Add dirty region tracking and partial upload/readback:

```csharp
public sealed class EditorTerrainSlice
{
    // Existing
    public Texture? Texture { get; init; }
    public bool IsDirty { get; set; }

    // New: Dirty region tracking
    public int DirtyMinX { get; set; } = int.MaxValue;
    public int DirtyMinZ { get; set; } = int.MaxValue;
    public int DirtyMaxX { get; set; } = int.MinValue;
    public int DirtyMaxZ { get; set; } = int.MinValue;

    public void MarkDirtyRegion(int x, int z, float radius)
    {
        IsDirty = true;
        DirtyMinX = Math.Max(0, Math.Min(DirtyMinX, (int)(x - radius)));
        DirtyMinZ = Math.Max(0, Math.Min(DirtyMinZ, (int)(z - radius)));
        DirtyMaxX = Math.Min(Width - 1, Math.Max(DirtyMaxX, (int)(x + radius)));
        DirtyMaxZ = Math.Min(Height - 1, Math.Max(DirtyMaxZ, (int)(z + radius)));
    }

    public void ClearDirtyRegion()
    {
        IsDirty = false;
        DirtyMinX = int.MaxValue;
        DirtyMinZ = int.MaxValue;
        DirtyMaxX = int.MinValue;
        DirtyMaxZ = int.MinValue;
    }
}
```

---

## 7. Code Examples

### Complete Partial Readback Flow

```csharp
// 1. After GPU editing stroke ends
public void OnStrokeEnd(CommandList commandList, EditorTerrainSlice slice)
{
    if (slice.DirtyWidth <= 0 || slice.DirtyHeight <= 0)
        return;

    // 2. Request partial readback
    var readbackData = readbackManager.ReadbackRegion(
        commandList,
        slice.Texture,
        slice.DirtyMinX,
        slice.DirtyMinZ,
        slice.DirtyWidth,
        slice.DirtyHeight);

    // 3. If readback succeeded, update CPU cache
    if (readbackData != null)
    {
        UpdateHeightDataCache(
            slice,
            slice.DirtyMinX,
            slice.DirtyMinZ,
            readbackData,
            slice.DirtyWidth,
            slice.DirtyHeight);
    }
}

// 4. Update CPU cache from readback data
private void UpdateHeightDataCache(
    EditorTerrainSlice slice,
    int destX, int destZ,
    ushort[] data,
    int width, int height)
{
    for (int row = 0; row < height; row++)
    {
        int srcOffset = row * width;
        int dstOffset = (slice.StartSampleZ + destZ + row) * HeightmapWidth
                      + slice.StartSampleX + destX;

        Array.Copy(data, srcOffset, HeightDataCache, dstOffset, width);
    }
}
```

---

## 8. Limitations and Gotchas

### Current Stride Limitations

1. **Staging to Staging Copy Not Implemented:**
   ```csharp
   // From CommandList.Direct3D12.cs:1506-1509
   if (sourceTexture.Usage == GraphicsResourceUsage.Staging &&
       destinationTexture.Usage == GraphicsResourceUsage.Staging)
   {
       throw new NotImplementedException("Copy region of staging resources is not supported yet");
   }
   ```

2. **Staging Source Not Implemented:**
   ```csharp
   // From CommandList.Direct3D12.cs:1516-1518
   if (sourceTexture.Usage == GraphicsResourceUsage.Staging)
   {
       throw new NotImplementedException("Copy region from staging texture is not supported yet");
   }
   ```

**Workaround:** These are not needed for our use case (GPU texture -> Staging texture is supported).

### RowPitch Alignment

When reading from mapped staging texture, `DataBox.RowPitch` may be larger than the actual row width due to D3D12 alignment requirements (typically 256-byte alignment for textures).

```csharp
// Correct row-by-row copy accounting for RowPitch
for (int row = 0; row < height; row++)
{
    int srcOffset = row * mapped.DataBox.RowPitch;  // GPU row pitch (with padding)
    int dstOffset = row * rowStride;                 // CPU row stride (tight packed)
    // Copy rowStride bytes from srcOffset to dstOffset
}
```

---

## 9. Summary

### Recommended Implementation

1. **Pre-allocate staging texture** (256x256 R16_UNorm, Staging usage)
2. **Track dirty regions** per slice (bounding box of brush strokes)
3. **Use CopyRegion()** to copy dirty region to staging texture
4. **Map with doNotWait: true** and handle "not ready" gracefully
5. **Update CPU cache** from readback data for undo/save
6. **Hybrid approach:** CPU-only for small brushes, GPU + readback for large

### Key APIs

| Operation | API |
|-----------|-----|
| Partial GPU->Staging copy | `CommandList.CopyRegion()` |
| Map staging for read | `CommandList.MapSubResource(..., MapMode.Read, doNotWait: true)` |
| Unmap after read | `CommandList.UnmapSubResource()` |
| Partial CPU->GPU upload | `Texture.SetData(..., region)` |

### Confidence Assessment

| Area | Level | Reason |
|------|-------|--------|
| Stride API availability | HIGH | Verified in Stride source code |
| DirectX 12 behavior | HIGH | Standard D3D12 patterns |
| Performance estimates | MEDIUM | Based on typical GPU memory bandwidth |

---

## Sources

### Primary (HIGH confidence)
- `E:/WorkSpace/stride/sources/engine/Stride.Graphics/Direct3D12/CommandList.Direct3D12.cs:1486-1596` - CopyRegion implementation
- `E:/WorkSpace/stride/sources/engine/Stride.Graphics/ResourceRegion.cs` - Region structure
- `E:/WorkSpace/stride/sources/engine/Stride.Graphics/Texture.cs:1030-1300` - GetData/SetData with region

### Secondary (MEDIUM confidence)
- `E:/WorkSpace/stride/sources/engine/Stride.Graphics/Direct3D12/Texture.Direct3D12.cs:229-237` - Staging texture creation with Readback heap
- Existing RESEARCH.md Godot patterns for readback synchronization

### Tertiary (LOW confidence)
- None required; all research based on Stride source code analysis
