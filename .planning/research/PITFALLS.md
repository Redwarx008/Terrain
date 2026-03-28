# Pitfalls Research

**Domain:** Terrain Slot Editor (Heightmap + Splatmap Editing)
**Researched:** 2026-03-29
**Confidence:** MEDIUM

## Critical Pitfalls

### Pitfall 1: GPU-CPU Synchronization Stall on Heightmap Readback

**What goes wrong:**
When the editor needs to read modified heightmap data back from GPU (e.g., for undo snapshots, export, or precise editing operations), a naive `GetData()` call on a GPU texture causes a full pipeline stall. The CPU waits for all pending GPU commands to complete, causing the editor to freeze momentarily. With large heightmaps (4K+), this can introduce 50-200ms hitches per operation.

**Why it happens:**
GPU and CPU operate asynchronously. The GPU may be several frames behind the CPU. Calling `GetData()` forces synchronization. Developers often do this on every brush stroke for "real-time" updates, not realizing the performance impact until the heightmap scales up.

**How to avoid:**
1. Maintain a CPU-side copy of the heightmap as the source of truth for editing operations
2. Write changes to CPU copy first, then upload to GPU asynchronously
3. Never read back from GPU during interactive editing - only at save/export time
4. Use staged async readback via staging buffers if GPU readback is required
5. Design the undo system to work with CPU-side data snapshots, not GPU texture reads

**Warning signs:**
- Editor UI freezes during brush strokes on large terrains
- Frame time spikes correlate with editing operations
- `GetData()` or similar calls in the hot path of editing code
- Users report "laggy" brush response on larger heightmaps

**Phase to address:** Height Editing Core - establishes the data flow architecture

---

### Pitfall 2: Undo/Redo Memory Explosion with Large Brush Operations

**What goes wrong:**
A naive undo system stores complete heightmap copies for each operation. With 4K heightmaps (16M samples), each undo snapshot is 32MB (R16). A single smoothing pass over the terrain creates dozens of snapshots, quickly consuming gigabytes of memory. Users with configurable history depth set high (100+) experience crashes or swap thrashing.

**Why it happens:**
Developers underestimate the memory footprint of heightmap data and the frequency of edits. A user dragging a brush across terrain generates continuous change events. If each event creates a full snapshot, memory grows unbounded. The "undo depth" setting makes this worse, not better.

**How to avoid:**
1. Use delta-based compression: store only the modified region (bounding box of brush stroke)
2. Implement operation merging: combine consecutive strokes of the same tool into one undo entry
3. Use a memory budget for undo history, evicting oldest entries when exceeded
4. Consider run-length encoding or other compression for height data (heightmap regions often have low entropy)
5. Track dirty regions instead of full snapshots - the existing MinMaxErrorMap system already has spatial chunking concepts

**Warning signs:**
- Memory usage climbs steadily during editing sessions
- Undo history depth of 50+ causes noticeable slowdown
- Large brush operations (100+ pixel radius) cause memory allocation spikes
- Process working set exceeds available RAM during extended sessions

**Phase to address:** Undo/Redo System - core architectural decision

---

### Pitfall 3: LOD Inconsistency During Editing

**What goes wrong:**
When heightmap is modified, the MinMaxErrorMaps used for LOD selection become stale. The GPU-driven LOD system selects chunks based on outdated geometric error values, causing:
- Cracks at LOD boundaries (one chunk thinks it's flat, neighbor knows it's not)
- Incorrect culling (chunks with new mountains not rendered when they should be)
- Popping artifacts as camera moves and LOD suddenly updates

**Why it happens:**
The MinMaxErrorMaps are precomputed by TerrainPreProcessor for static terrain. The editor modifies heights but doesn't update these maps. The existing codebase already notes this issue - CONCERNS.md flags that there's no runtime terrain modification API, and the MinMaxErrorMap generation is an expensive preprocessing step.

**How to avoid:**
1. For MVP: Regenerate affected LOD levels' MinMaxErrorMaps on edit completion (not during stroke)
2. Mark edited chunks as "dirty" and force them to highest LOD detail temporarily
3. Use conservative bounds: if height increases, expand the min/max range; never shrink until regeneration
4. Consider compute shader-based incremental MinMaxErrorMap updates for production
5. Document that heavy editing may require "Rebuild LOD Data" operation for optimal quality

**Warning signs:**
- Visible seams or cracks appear after editing, especially at chunk boundaries
- Areas that were edited show popping when camera moves
- Terrain detail "disappears" at certain distances after editing
- LOD selection doesn't reflect edited terrain shape

**Phase to address:** Height Editing Core - must be designed into the edit pipeline

---

### Pitfall 4: Splatmap Bilinear Filtering Artifacts

**What goes wrong:**
When painting splatmaps (texture layer indices), bilinear filtering creates smooth transitions between pixel values. But splatmap values are discrete material indices (0-255 in R8 format). Bilinear interpolation produces invalid intermediate values (e.g., 127.5 between material 0 and material 255), causing:
- Random/noisy texture blending at material boundaries
- "Sparkling" artifacts when camera moves (interpolation varies)
- Wrong textures appearing at transition zones

**Why it happens:**
R8 splatmaps store material indices as integer values. Bilinear filtering treats them as continuous data. The GPU doesn't know these are indices, not intensities. This is a fundamental tension: you want smooth brush falloff, but splatmap values must remain discrete per-material.

**How to avoid:**
1. Use nearest-neighbor sampling for the splatmap texture itself (no filtering of indices)
2. Implement a weight-based system: store blend weights for N materials per pixel, not a single index
3. For R8 with 256 materials: use a separate weight/blend texture for transitions
4. Design the brush system to "paint" with proper weight blending, not just value replacement
5. Consider the existing terrain's design: if splatmap is R8 with 256 materials, you likely need a separate blend factor per-pixel

**Warning signs:**
- Textures appear to "shimmer" at boundaries when camera moves
- Invalid material indices appear in rendering (textures not in palette show up)
- Smooth brush strokes create noisy/tesselated-looking edges
- Exported splatmaps show "dirty" values that aren't clean material indices

**Phase to address:** Material Slot Painting - fundamental to splatmap data design

---

### Pitfall 5: Noise Perturbation Quality Issues

**What goes wrong:**
Noise-based terrain扰动 (perturbation brushes) show obvious tiling patterns, repetitive features, or unnatural artifacts. Common issues include:
- Obvious grid alignment of noise features
- Terrain looks "synthetic" or "generated"
- Same noise pattern repeats across large terrains
- Seams appear where noise tiles meet

**Why it happens:**
Standard Perlin/Simplex noise has a finite period (typically 256). For terrain-scale features, this period is often visible. Additionally, using the same noise parameters across the entire terrain creates uniformity that looks artificial. Layering noise incorrectly (wrong frequencies, amplitudes) creates unnatural patterns.

**How to avoid:**
1. Use domain repetition with rotation or offset to hide tiling
2. Implement fractal Brownian motion (fBm) with carefully tuned octaves
3. Vary noise seed/parameters based on spatial position for large terrains
4. Consider noise with larger period (e.g., OpenSimplex, custom noise functions)
5. Test noise quality at the target terrain scale early - don't assume "Perlin noise" is sufficient
6. Provide noise preview in brush settings so users can tune before applying

**Warning signs:**
- Generated terrain shows obvious grid patterns
- Multiple brushes create identical-looking features
- Terrain looks "samey" across large areas
- Users immediately notice "that's procedural noise"

**Phase to address:** Height Editing Core - noise brush implementation

---

### Pitfall 6: Heightmap Precision Loss

**What goes wrong:**
Terrain heights lose precision during editing, causing:
- "Stair-step" artifacts on smooth slopes (quantization)
- Smoothing operations have no effect (heights already at minimum step)
- Import/export round-trip degrades terrain quality
- Brush operations produce unexpected results (values snap to quantized levels)

**Why it happens:**
The heightmap format (R16 = 16-bit unsigned integer) has 65536 discrete levels. If height scale is large (e.g., 1000m terrain height), each step is ~1.5cm. For smooth terrain, this is usually adequate. But aggressive smoothing, multiple edit passes, or poor height scale choices can accumulate precision loss. Floating-point intermediate calculations that snap back to R16 on write amplify this.

**How to avoid:**
1. Design height scale appropriately: smaller height range = more precision per meter
2. Keep intermediate calculations in float32, only quantize on final write
3. Consider R32F format for editing internal representation (convert to R16 on export)
4. Implement dithering for visual smoothness if quantization is unavoidable
5. Document the precision limits to users (e.g., "minimum height step is X cm at current scale")

**Warning signs:**
- Smooth slopes show visible "staircase" pattern
- Smoothing brush has no visible effect in some areas
- Repeated import/export degrades terrain quality
- Height values "snap" to specific levels during editing

**Phase to address:** File I/O Core - format and precision decisions

---

## Technical Debt Patterns

Shortcuts that seem reasonable but create long-term problems.

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Store full heightmap per undo step | Simple implementation | Memory explosion (32MB+ per step) | Never - use delta compression |
| Read back GPU texture for undo | Accurate state capture | Pipeline stalls, UI freezing | Never - maintain CPU copy |
| Skip MinMaxErrorMap updates | Faster edit response | LOD cracks, visual artifacts | Only for MVP with "Rebuild" button |
| Use R8 splatmap directly with bilinear filter | Simple texture sampling | Invalid blends, artifacts | Never - design proper blend system |
| Single noise function for all brushes | Less code to write | Repetitive, artificial terrain | Only for prototype |
| Immediate mode edits (no batching) | Responsive feel | Performance issues with large brushes | Only for small terrains (<2K) |
| Skip dirty region tracking | Simpler code | O(n) operations on full heightmap | Never for production |

## Integration Gotchas

Common mistakes when connecting to external services or systems.

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| Stride Terrain Runtime | Assuming terrain is mutable at runtime | Terrain is read-only after load; editor exports processed .terrain files |
| TerrainPreProcessor | Ignoring its page/tile layout | Editor must respect same page structure for edit-in-place |
| ImGui Input | Not handling io.WantCaptureMouse correctly | Check before processing brush input; ImGui may consume events |
| File Dialog | Not validating file format before loading | Validate PNG dimensions, bit depth, format before allocating memory |
| Stride Graphics Device | Creating resources during edit operations | Pre-allocate buffers; edit operations should only update existing resources |

## Performance Traps

Patterns that work at small scale but fail as usage grows.

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Full-heightmap operations per stroke | Lag increases with terrain size | Use dirty region tracking; only process modified tiles | 2K+ heightmaps with any brush size |
| Undo snapshot on every mouse move | Memory grows linearly with stroke length | Batch strokes into single undo operation; timer-based commit | Strokes >100 pixels traveled |
| Immediate GPU upload per edit | Frame time spikes, stuttering | Batch uploads; use staging buffer; upload per-frame budget | Any interactive editing |
| Single-threaded MinMaxErrorMap update | Editor freezes after large edits | Background thread updates; mark chunks dirty for update | Editing areas >10% of terrain |
| No memory budget for undo | Eventual out-of-memory crash | Implement memory budget with eviction policy | Extended editing sessions |

## Security Mistakes

Domain-specific security issues beyond general application security.

| Mistake | Risk | Prevention |
|---------|------|------------|
| Path traversal in file open | Loading arbitrary files, data exfiltration | Validate paths; use safe file dialogs; sanitize user input |
| Unbounded memory allocation on load | Denial of service via crafted heightmaps | Validate dimensions before allocating; cap max terrain size |
| Native memory without bounds check | Buffer overflow, code execution | Use checked math for all buffer sizes; validate before NativeMemory.Alloc |
| Unvalidated PNG dimensions | Integer overflow, memory corruption | Check width * height doesn't overflow; use checked multiplication |

## UX Pitfalls

Common user experience mistakes in this domain.

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| No brush preview | Users can't predict edit result | Show brush outline, falloff preview, and affected area |
| No undo history visualization | Users don't know what undo will do | Show history list with preview thumbnails |
| Silent LOD artifacts | Users don't know terrain is degraded | Warning indicator when LOD data is stale |
| No edit confirmation for large operations | Accidental destructive edits | Confirm before operations affecting >10% of terrain |
| No autosave/recovery | Lost work on crash | Implement autosave with recovery on startup |
| Missing coordinate feedback | Users can't return to specific location | Show cursor height, world position in status bar |
| No layer lock in paint mode | Accidental painting on wrong material | Allow locking specific material slots |

## "Looks Done But Isn't" Checklist

Things that appear complete but are missing critical pieces.

- [ ] **Height Editing:** Often missing undo/redo after brush operations - verify history is recorded per-stroke-commit
- [ ] **Splatmap Painting:** Often missing bilinear blend handling - verify no artifacts at material boundaries
- [ ] **LOD Update:** Often missing MinMaxErrorMap regeneration - verify LOD selection works after edits
- [ ] **File Export:** Often missing mipmap generation - verify exported files include all mip levels
- [ ] **Large Heightmap Support:** Often tested only on small files - verify memory/paging works at 4K+ dimensions
- [ ] **Noise Brush:** Often missing scale-independent noise - verify noise looks good at different terrain sizes
- [ ] **Brush Falloff:** Often missing in splatmap painting - verify smooth weight transitions, not hard edges
- [ ] **Performance:** Often missing profiling under load - verify frame time with continuous painting on 4K heightmap

## Recovery Strategies

When pitfalls occur despite prevention, how to recover.

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| GPU sync stall in existing code | MEDIUM | Refactor to CPU-side source of truth; add staging buffers |
| Undo memory explosion | MEDIUM | Implement delta compression; add memory budget with eviction |
| LOD cracks after editing | LOW | Add "Rebuild LOD Data" menu item; runs MinMaxErrorMap regeneration |
| Splatmap blend artifacts | HIGH | Redesign splatmap format to support proper blending; migration required |
| Noise quality issues | LOW | Tune noise parameters; add rotation/offset; no data migration needed |
| Heightmap precision loss | MEDIUM | Switch to float32 internal format; add quantization at export only |

## Pitfall-to-Phase Mapping

How roadmap phases should address these pitfalls.

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| GPU-CPU Sync Stall | Height Editing Core | Profile frame time during continuous brush strokes on 4K heightmap |
| Undo Memory Explosion | Undo/Redo System | Test memory usage with 100+ undo steps on large terrain |
| LOD Inconsistency | Height Editing Core | Verify no cracks appear after editing across LOD boundaries |
| Splatmap Blend Artifacts | Material Slot Painting | Visual inspection of material transitions; no "sparkle" artifacts |
| Noise Perturbation Quality | Height Editing Core | User review of noise-generated terrain; no obvious tiling |
| Heightmap Precision Loss | File I/O Core | Import/export round-trip test; no quality degradation |

## Domain-Specific Notes from Existing Codebase

Based on CONCERNS.md analysis:

1. **No runtime terrain modification API exists** - This is the core feature gap the editor fills. The existing system assumes read-only terrain after load.

2. **MinMaxErrorMap generation is expensive** - The preprocessor generates these offline. Editor must either regenerate or use incremental updates.

3. **Buffer pool exhaustion in streaming** - The existing streaming system has a fixed buffer pool. Editing operations that need to upload modified pages compete with regular streaming.

4. **R8 SplatMap storage exists but unused** - The terrain file format supports splatmap, but rendering doesn't use it. Editor must implement both painting and rendering.

5. **ImGui integration is complete** - The UI framework exists but editor panels are stubs. This is implementation work, not architectural risk.

## Sources

- Existing codebase analysis: Terrain/Streaming/TerrainStreaming.cs, Terrain/Rendering/TerrainQuadTree.cs, TerrainPreProcessor/Models/MinMaxErrorMap.cs
- CONCERNS.md analysis of existing technical debt and known issues
- PROJECT.md requirements and constraints
- Domain knowledge: GPU pipeline synchronization patterns
- Domain knowledge: Heightmap/splatmap data representation trade-offs
- Domain knowledge: LOD terrain systems and crack prevention
- Domain knowledge: Procedural noise generation quality factors

---
*Pitfalls research for: Terrain Slot Editor*
*Researched: 2026-03-29*
