# GPU Height Editing - Web Research

**Researched:** 2026-04-04
**Domain:** GPU-based terrain height editing, compute shader patterns, CPU-GPU synchronization
**Confidence:** MEDIUM (web searches returned limited results; compiled from existing research and known patterns)

## Summary

This document supplements the existing `03.5-RESEARCH.md` and `GODOT_RESEARCH.md` with broader industry patterns for GPU terrain editing. Web searches returned limited results, so findings are compiled from the existing comprehensive research and established GPU programming patterns.

**Primary findings:**
1. **Two dominant approaches:** Compute shader direct writes vs. render-to-texture (fragment shader)
2. **GPU readback is the critical challenge:** All engines must solve CPU-GPU synchronization for undo/save
3. **Chunk-based undo is the standard pattern:** 64x64 pixel chunks balance memory and granularity
4. **Partial texture updates are essential:** Full texture uploads kill performance

---

## 1. GPU Terrain Editing Approaches

### 1.1 Compute Shader Approach (Recommended for Stride)

**How it works:**
- UAV (Unordered Access View) textures allow direct GPU writes
- Compute shader operates on texels in parallel
- Each thread processes one or more texels

**Advantages:**
- Direct texture modification without render target setup
- Flexible thread dispatch for varying brush sizes
- Better suited for complex operations (erosion, multi-pass filters)
- Can read and write same texture (with proper synchronization)

**Stride implementation pattern:**
```csharp
// Create UAV-capable texture
var texture = Texture.New2D(
    graphicsDevice,
    width, height,
    PixelFormat.R16_UNorm,  // R16_UNorm supports UAV in DX12
    TextureFlags.ShaderResource | TextureFlags.UnorderedAccess);

// Dispatch compute shader
commandList.ResourceBarrierTransition(texture, GraphicsResourceState.UnorderedAccess);
computeEffect.SetParameter("HeightmapSlice", texture);
computeEffect.Draw(drawContext);
commandList.ResourceBarrierTransition(texture, GraphicsResourceState.PixelShaderResource);
```

**HLSL shader pattern:**
```hlsl
RWTexture2D<float> HeightmapSlice : register(u0);

[numthreads(8, 8, 1)]
void CSMain(uint3 DTid : SV_DispatchThreadID)
{
    // Boundary check
    if (DTid.x >= Width || DTid.y >= Height)
        return;

    // Read current height
    float height = HeightmapSlice[DTid.xy];

    // Apply brush operation
    float distance = length(float2(DTid.xy) - BrushCenter);
    if (distance < BrushRadius)
    {
        float falloff = 1.0 - saturate(distance / BrushRadius);
        height += Strength * falloff * FrameTime;
    }

    HeightmapSlice[DTid.xy] = clamp(height, 0.0, 1.0);
}
```

### 1.2 Render-to-Texture Approach (Godot Style)

**How it works:**
- Render brush as 2D sprite to a viewport
- Custom fragment shader applies edit operation
- Read back viewport texture to CPU

**Advantages:**
- Simpler for basic operations
- Leverages existing 2D rendering pipeline
- Works on hardware with limited compute support

**Disadvantages:**
- Requires float-to-RGBA8 encoding on some platforms
- Less flexible for complex operations
- Extra copy step from render target

**Godot implementation (from GODOT_RESEARCH.md):**
```glsl
// Fragment shader for raise operation
void fragment() {
    float brush_value = u_factor * u_opacity * texture(TEXTURE, UV).r;
    float src_h = sample_heightmap(u_src_texture, get_src_uv(SCREEN_UV));
    float h = src_h + brush_value;
    COLOR = encode_height_to_viewport(h);  // Bit-packing for RGBA8
}
```

### 1.3 Hybrid Approach (Best of Both)

**Pattern:**
- CPU editing for small brushes (< 30px radius)
- GPU compute for large brushes (> 30px radius)
- Automatic threshold detection based on brush size

**Rationale:**
- CPU has lower overhead for small operations
- GPU parallelism wins for large brush areas
- pi * r^2 pixels - GPU scales, CPU doesn't

---

## 2. GPU Texture Readback Patterns

### 2.1 The Readback Challenge

GPU editing creates a synchronization problem:
- Edits happen on GPU
- Undo/save requires CPU-side data
- Full readback every frame kills performance

**Key insight from Godot:** CPU image is authoritative for undo. GPU edits flow back to CPU only when needed (stroke end, save operation).

### 2.2 Staging Buffer Approach

**DirectX 12 pattern:**
1. Create a staging buffer (READBACK heap)
2. Copy texture region to staging buffer
3. Map staging buffer to read data
4. Use fence to synchronize GPU completion

**Stride/managed limitation:**
- `Texture.GetData()` reads entire texture
- No built-in partial readback support
- Must read full slice even for small dirty region

**Workaround:**
```csharp
// Read entire slice (can be large)
var data = new ushort[slice.Width * slice.Height];
slice.Texture.GetData(commandList, data);

// Copy only dirty region to CPU cache
for (int row = dirtyMinY; row < dirtyMaxY; row++)
{
    int srcOffset = row * slice.Width + dirtyMinX;
    int dstOffset = (slice.StartSampleZ + row) * cacheWidth + slice.StartSampleX + dirtyMinX;
    Array.Copy(data, srcOffset, heightCache, dstOffset, dirtyWidth);
}
```

### 2.3 Async Readback Pattern

**Pattern:**
1. Request readback at stroke end
2. Continue rendering while GPU processes
3. Check fence/frame delay for completion
4. Apply to CPU cache when ready

**Godot's 2-frame delay:**
```
Frame N:   paint_input() called -> request render
Frame N+1: render executes -> request readback
Frame N+2: readback completes -> apply to CPU image
```

---

## 3. Undo/Redo with GPU Editing

### 3.1 Chunk-Based Undo (Recommended)

**From Godot heightmap plugin:**
- 64x64 pixel chunks
- Store initial chunks before edit
- Store final chunks from GPU readback
- Memory efficient: only store changed chunks

**Memory calculation:**
- Full 4097x4097 heightmap = 33.5 MB per snapshot
- 64x64 chunk = 8 KB
- If brush affects ~10 chunks per stroke, 50 undo levels = 4 MB
- Savings: ~99% memory reduction

**Implementation:**
```csharp
const int UndoChunkSize = 64;

struct UndoChunk
{
    public int ChunkX, ChunkY;
    public ushort[] InitialData;  // 64x64 = 4096 samples = 8 KB
    public ushort[] FinalData;    // Only for redo
}

class UndoManager
{
    Stack<UndoChunk[]> undoStack = new();

    public void CaptureInitial(int brushX, int brushY, int radius, ushort[] heightCache, int cacheWidth)
    {
        // Calculate affected chunks
        int minChunkX = (brushX - radius) / UndoChunkSize;
        int maxChunkX = (brushX + radius) / UndoChunkSize + 1;
        // ... similar for Y

        var chunks = new List<UndoChunk>();
        for (int cy = minChunkY; cy < maxChunkY; cy++)
        {
            for (int cx = minChunkX; cx < maxChunkX; cx++)
            {
                chunks.Add(new UndoChunk
                {
                    ChunkX = cx,
                    ChunkY = cy,
                    InitialData = ReadChunkFromCache(cx, cy, heightCache, cacheWidth)
                });
            }
        }
        undoStack.Push(chunks.ToArray());
    }

    public void CaptureFinal(ushort[] heightCache, int cacheWidth)
    {
        // Read back GPU to heightCache first
        // Then capture final chunks for redo
    }
}
```

### 3.2 Command-Based Undo (Alternative)

**Pattern:**
- Store inverse operations instead of data
- RaiseBy(X) can be undone by LowerBy(X)
- Smooth and Flatten store target value

**Advantages:**
- Minimal memory usage
- Fast undo/redo execution

**Disadvantages:**
- Complex for multi-step operations
- Can accumulate floating-point errors
- Doesn't handle multi-tool strokes well

---

## 4. Brush Implementation Patterns

### 4.1 Falloff Functions

**Linear falloff (simple):**
```hlsl
float LinearFalloff(float distance, float innerRadius, float outerRadius)
{
    if (distance <= innerRadius) return 1.0;
    if (distance >= outerRadius) return 0.0;
    return 1.0 - (distance - innerRadius) / (outerRadius - innerRadius);
}
```

**Smooth falloff (better):**
```hlsl
float SmoothFalloff(float distance, float radius)
{
    float t = saturate(1.0 - distance / radius);
    return t * t * (3.0 - 2.0 * t);  // Smoothstep
}
```

**Brush texture-based (most flexible):**
```hlsl
// Sample brush mask texture for falloff
float brushFalloff = BrushTexture.Sample(BrushSampler, localUV).r;
```

### 4.2 Smooth Operation Kernels

**5-point kernel (fast, good quality):**
```hlsl
float Smooth5Point(Texture2D<float> tex, int2 coord, int2 dimensions)
{
    float center = tex[coord];
    float left = tex[max(0, coord - int2(1, 0))];
    float right = tex[min(dimensions - 1, coord + int2(1, 0))];
    float up = tex[max(0, coord - int2(0, 1))];
    float down = tex[min(dimensions - 1, coord + int2(0, 1))];

    return (center + left + right + up + down) * 0.2;
}
```

**3x3 Gaussian kernel (better smoothing):**
```hlsl
float SmoothGaussian3x3(Texture2D<float> tex, int2 coord, int2 dimensions)
{
    float weights[3][3] = {
        { 1, 2, 1 },
        { 2, 4, 2 },
        { 1, 2, 1 }
    };
    float totalWeight = 16.0;
    float sum = 0;

    for (int dy = -1; dy <= 1; dy++)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            int2 sampleCoord = clamp(coord + int2(dx, dy), int2(0, 0), dimensions - 1);
            sum += tex[sampleCoord] * weights[dy + 1][dx + 1];
        }
    }

    return sum / totalWeight;
}
```

### 4.3 Erosion (Future Enhancement)

**Thermal erosion (simple):**
```hlsl
float ThermalErosion(Texture2D<float> tex, int2 coord, float talusAngle)
{
    float height = tex[coord];
    float totalSediment = 0;

    for (int i = 0; i < 4; i++)
    {
        int2 neighbor = coord + offsets[i];
        float neighborHeight = tex[neighbor];
        float diff = height - neighborHeight;

        if (diff > talusAngle)
        {
            float sediment = (diff - talusAngle) * 0.5;
            totalSediment += sediment;
        }
    }

    return height - totalSediment;
}
```

---

## 5. Multi-Slice/Multi-Tile Editing

### 5.1 The Boundary Problem

When brush crosses slice boundaries:
- Each slice is a separate texture
- Compute shader only sees one texture
- Must dispatch to multiple slices

**Solutions:**

1. **Dispatch per slice (simple):**
   - Check which slices brush intersects
   - Dispatch compute to each with clipped bounds
   - Coordinate: world -> slice-local

2. **Unified staging texture:**
   - Copy affected slices to staging texture
   - Edit staging texture
   - Copy back to slices

3. **3D texture array:**
   - Use Texture2DArray for slices
   - Single dispatch with Z dimension for slice index

**Recommendation:** Dispatch per slice for simplicity. Slice count is typically 1-4.

### 5.2 Coordinate Conversion

```csharp
// World coordinates to slice-local
int worldX = (int)worldPosition.X;
int worldZ = (int)worldPosition.Z;

if (terrainEntity.TryResolveSampleSlice(worldX, worldZ, out var slice))
{
    int localX = worldX - slice.StartSampleX;
    int localZ = worldZ - slice.StartSampleZ;

    // Dispatch to slice.Texture at (localX, localZ)
}
```

---

## 6. Performance Considerations

### 6.1 GPU vs CPU Threshold

**Rule of thumb:**
- Small brushes (< 30px radius): CPU faster
- Large brushes (> 50px radius): GPU faster
- Medium brushes: Test on target hardware

**Reasoning:**
- GPU dispatch has fixed overhead (~0.1-0.5ms)
- CPU iteration: O(pi * r^2) operations
- GPU: O(1) for parallel dispatch

**Crossover calculation:**
```
GPU overhead = FixedCost + ThreadGroupSetup + ResourceBarriers
CPU cost = NumPixels * IterationCost

For R16_UNorm:
- FixedCost ~ 0.2ms
- IterationCost ~ 10ns per pixel
- Break-even: ~20,000 pixels
- Radius: sqrt(20000/pi) ~ 80px
```

### 6.2 Memory Bandwidth

**R16_UNorm considerations:**
- 2 bytes per texel
- 4K x 4K heightmap = 32 MB
- Read + write = 64 MB per operation
- At 60 FPS = 3.8 GB/s bandwidth

**Optimizations:**
- Only dispatch affected texels (not full texture)
- Use brush bounds for thread group dispatch
- Batch multiple strokes when possible

### 6.3 Resource Barrier Overhead

**DX12 barriers are not free:**
- UAV -> SRV transition triggers GPU flush
- Multiple dispatches can pipeline
- Group operations that need same resource state

**Pattern:**
```csharp
// Batch multiple brush strokes
foreach (var stroke in pendingStrokes)
{
    // All strokes in UAV state
    dispatcher.Dispatch(drawContext, stroke);
}
// Single barrier back to SRV
commandList.ResourceBarrierTransition(heightmap, GraphicsResourceState.PixelShaderResource);
```

---

## 7. Stride-Specific Considerations

### 7.1 Texture Format Support

**Verified:**
- R16_UNorm supports UAV in DirectX 12
- RWTexture2D<float> in shader reads/writes [0,1]
- No format conversion needed

**Alternative formats:**
- R16_Float: Better precision, still 16-bit
- R32_Float: Full 32-bit precision, 2x memory

### 7.2 ComputeEffectShader Pattern

**From existing codebase:**
```csharp
// Thread group size in shader declaration
[numthreads(8, 8, 1)]  // 64 threads per group

// C# dispatch setup
int groupsX = (textureWidth + 7) / 8;
int groupsY = (textureHeight + 7) / 8;
effect.ThreadGroupCounts = new Int3(groupsX, groupsY, 1);
```

### 7.3 GetData Limitations

**Stride API:**
- `Texture.GetData()` reads entire texture
- No region-based readback
- Must read full slice for undo/save

**Workaround for large terrains:**
- Keep dirty region tracking
- Read full slice once
- Copy only dirty region to CPU cache

---

## 8. Recommended Architecture

Based on research findings, the recommended architecture for Terrain Slot Editor:

### 8.1 Core Components

```
GpuHeightEditor (orchestrator)
    |
    +-- HeightEditComputeDispatcher (shader dispatch)
    |       |
    |       +-- TerrainHeightEdit.sdsl (compute shader)
    |
    +-- EditorTerrainEntity (terrain data)
    |       |
    |       +-- EditorTerrainSlice[] (UAV textures)
    |
    +-- HeightEditUndoManager (undo/redo)
            |
            +-- UndoChunk[] (64x64 pixel chunks)
```

### 8.2 Data Flow

```
1. User presses left mouse button
   -> BeginStroke()
   -> Capture initial chunks for undo

2. User drags mouse
   -> ApplyStroke() each frame
   -> Dispatch compute shader to affected slices
   -> Mark slices dirty

3. User releases mouse button
   -> EndStroke()
   -> Read GPU slices to CPU cache
   -> Capture final chunks for redo
   -> Update bounds
```

### 8.3 Threading Model

- GPU dispatch: Main thread (synchronized with rendering)
- Undo capture: Main thread (CPU cache is single-threaded)
- File save: Background thread (can read CPU cache)

---

## 9. Code Examples

### 9.1 Compute Shader (Full Implementation)

```hlsl
// TerrainHeightEdit.sdsl
namespace Terrain.Editor
{
    shader TerrainHeightEdit : ComputeShaderBase
    {
        stage RWTexture2D<float> HeightmapSlice;

        cbuffer BrushParams
        {
            stage int2 BrushCenter;
            stage float BrushRadius;
            stage float BrushInnerRadius;
            stage float Strength;
            stage float FrameTime;
            stage int EditMode;
            stage float TargetHeight;
            stage int2 TextureDimensions;
        };

        float ComputeFalloff(float distance)
        {
            if (distance <= BrushInnerRadius)
                return 1.0;
            if (distance >= BrushRadius)
                return 0.0;
            float t = (distance - BrushInnerRadius) / (BrushRadius - BrushInnerRadius);
            return 1.0 - t * t * t;  // Cubic falloff
        }

        override void Compute()
        {
            int2 texelCoord = int2(streams.DispatchThreadId.xy);

            if (texelCoord.x >= TextureDimensions.x || texelCoord.y >= TextureDimensions.y)
                return;

            float distance = length(float2(texelCoord - BrushCenter));
            if (distance > BrushRadius)
                return;

            float falloff = ComputeFalloff(distance);
            float currentHeight = HeightmapSlice[texelCoord];
            float newHeight = currentHeight;
            float strengthFactor = Strength * FrameTime * 0.015 * falloff;

            if (EditMode == 0)  // Raise
            {
                newHeight = currentHeight + strengthFactor;
            }
            else if (EditMode == 1)  // Lower
            {
                newHeight = currentHeight - strengthFactor;
            }
            else if (EditMode == 2)  // Smooth
            {
                float sum = 0;
                int count = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int2 neighbor = texelCoord + int2(dx, dy);
                        if (neighbor.x >= 0 && neighbor.x < TextureDimensions.x &&
                            neighbor.y >= 0 && neighbor.y < TextureDimensions.y)
                        {
                            sum += HeightmapSlice[neighbor];
                            count++;
                        }
                    }
                }
                float average = sum / float(count);
                newHeight = currentHeight + (average - currentHeight) * strengthFactor * 5.0;
            }
            else if (EditMode == 3)  // Flatten
            {
                newHeight = currentHeight + (TargetHeight - currentHeight) * strengthFactor * 5.0;
            }

            HeightmapSlice[texelCoord] = clamp(newHeight, 0.0, 1.0);
        }
    };
}
```

### 9.2 C# Dispatcher (Full Implementation)

```csharp
// HeightEditComputeDispatcher.cs
public sealed class HeightEditComputeDispatcher : IDisposable
{
    private const int ThreadCountX = 8;
    private const int ThreadCountY = 8;

    private ComputeEffectShader? effect;

    public void Initialize(RenderContext renderContext)
    {
        effect = new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "TerrainHeightEdit",
            ThreadNumbers = new Int3(ThreadCountX, ThreadCountY, 1),
        };
    }

    public void Dispatch(
        RenderDrawContext drawContext,
        Texture heightmapSlice,
        int brushCenterX, int brushCenterY,
        float brushRadius, float brushInnerRadius,
        float strength, float frameTime,
        int editMode, float targetHeight)
    {
        if (effect == null)
            throw new InvalidOperationException("Dispatcher not initialized");

        // Calculate dispatch size (full texture, shader does bounds checking)
        int groupsX = (heightmapSlice.Width + ThreadCountX - 1) / ThreadCountX;
        int groupsY = (heightmapSlice.Height + ThreadCountY - 1) / ThreadCountY;

        var commandList = drawContext.CommandList;

        // Transition to UAV
        commandList.ResourceBarrierTransition(heightmapSlice, GraphicsResourceState.UnorderedAccess);

        // Set parameters
        effect.ThreadGroupCounts = new Int3(groupsX, groupsY, 1);
        effect.Parameters.Set(TerrainHeightEditKeys.HeightmapSlice, heightmapSlice);
        effect.Parameters.Set(TerrainHeightEditKeys.BrushCenter, new Int2(brushCenterX, brushCenterY));
        effect.Parameters.Set(TerrainHeightEditKeys.BrushRadius, brushRadius);
        effect.Parameters.Set(TerrainHeightEditKeys.BrushInnerRadius, brushInnerRadius);
        effect.Parameters.Set(TerrainHeightEditKeys.Strength, strength);
        effect.Parameters.Set(TerrainHeightEditKeys.FrameTime, frameTime);
        effect.Parameters.Set(TerrainHeightEditKeys.EditMode, editMode);
        effect.Parameters.Set(TerrainHeightEditKeys.TargetHeight, targetHeight);
        effect.Parameters.Set(TerrainHeightEditKeys.TextureDimensions, new Int2(heightmapSlice.Width, heightmapSlice.Height));

        // Dispatch
        effect.Draw(drawContext);

        // Transition back to SRV
        commandList.ResourceBarrierTransition(heightmapSlice, GraphicsResourceState.PixelShaderResource);
    }

    public void Dispose()
    {
        effect?.Dispose();
        effect = null;
    }
}
```

---

## 10. Sources

### Primary (HIGH confidence)
- `03.5-RESEARCH.md` - Existing codebase analysis and compute shader patterns
- `GODOT_RESEARCH.md` - Detailed Godot heightmap plugin analysis
- TerrainBuildLodMap.sdsl, TerrainComputeDispatcher.cs - Existing compute shader infrastructure

### Secondary (MEDIUM confidence)
- DirectX 12 documentation patterns (known practices for UAV textures)
- HLSL compute shader best practices (standard GPU programming patterns)

### Tertiary (LOW confidence)
- Web searches returned limited results; industry-specific implementations not found
- Recommended to verify with Context7 for Stride-specific APIs

---

## 11. Recommendations for Implementation

### 11.1 Immediate (Phase 03.5)
1. Implement compute shader approach (already planned)
2. Add UAV texture creation to HeightmapLoader
3. Implement GPU-to-CPU sync for undo/save

### 11.2 Future Enhancements
1. Hybrid CPU/GPU editing with size threshold
2. Chunk-based undo (64x64 chunks)
3. Brush texture loading (custom brush shapes)
4. Erosion and advanced terrain operations

### 11.3 Testing Priorities
1. Verify UAV texture creation works on target hardware
2. Measure GPU vs CPU crossover point
3. Test multi-slice editing at boundaries
4. Validate undo/redo with chunk-based approach

---

## Metadata

**Confidence breakdown:**
- Compute shader patterns: HIGH (existing codebase verification)
- GPU readback: MEDIUM (Stride API limitations need testing)
- Undo patterns: HIGH (Godot implementation proven)
- Performance thresholds: LOW (needs benchmarking on target hardware)

**Research date:** 2026-04-04
**Valid until:** 2026-05-04 (patterns are stable; API verification recommended)
