# External Integrations

**Analysis Date:** 2026-03-27

## APIs & External Services

**No external cloud APIs or web services detected.**

This is a standalone desktop application with no network dependencies.

## Data Storage

**Custom Binary Format:**
- `.terrain` files - Proprietary terrain data format
  - Binary format with structured header
  - Contains: MinMaxErrorMap data, heightmap SVT (Sparse Virtual Texture), optional splatmap SVT
  - Header magic value: `0x5452414E` ("TRAN")
  - Current version: 1
  - Implementation: `TerrainPreProcessor/Models/TerrainFileHeader.cs`
  - Runtime loading: `Terrain/Core/TerrainComponent.cs`

**Image Formats (Input Only):**
- Heightmaps: PNG with L16 (16-bit grayscale) pixel format
- Splatmaps: PNG with L8, L16, RG32, or RGBA32 formats
- Processing: SixLabors.ImageSharp for mipmap generation and tile extraction

**File System:**
- Local file system only
- No database or cloud storage integration
- Resources loaded from relative paths at runtime

**Caching:**
- GPU texture caching via Stride's resource system
- Terrain chunk streaming buffer: `PageBufferAllocator.cs`
- Max resident chunks: configurable (default 1024)
- Max uploads per frame: configurable (default 8)

## Authentication & Identity

**Not applicable.**

No user authentication, licensing, or identity management detected.

## Monitoring & Observability

**Error Tracking:**
- None - standard .NET exception handling only

**Logging:**
- Stride's built-in logging via `GlobalLogger`
- Logger name: "Quantum" (in `TerrainRenderFeature.cs`)

**Profiling:**
- Stride Profiler integration
- Custom profiling keys:
  - `TerrainRenderFeature.Extract`
  - `TerrainRenderFeature.PreparePermutationsImpl`
  - `TerrainRenderFeature.Prepare`
  - `TerrainRenderFeature.Draw`
- GameProfiler for FPS display (enabled in `TerrainApp.cs`)

## CI/CD & Deployment

**Hosting:**
- Desktop application - no server hosting

**CI Pipeline:**
- None detected

**Build Configuration:**
- Debug and Release configurations
- Platform targets: Any CPU, x64, x86 (though win-x64 runtime specified)

## Environment Configuration

**Required settings:**
- None - all configuration is code-based or asset-based

**Graphics Settings (configured in code):**
- VSync disabled (`SynchronizeWithVerticalRetrace = false`)
- Editor default resolution: 1920x1080
- Fullscreen: false (windowed mode)

**Terrain Configuration (per-component):**
- `TerrainDataPath` - Path to .terrain file
- `HeightScale` - Vertical exaggeration (default 24.0)
- `MaxScreenSpaceErrorPixels` - LOD quality threshold (default 8.0)
- `MaxVisibleChunkInstances` - Instance buffer size (default 65536)
- `MaxResidentChunks` - Chunk cache size (default 1024)
- `MaxStreamingUploadsPerFrame` - Streaming bandwidth limit (default 8)

## Webhooks & Callbacks

**Incoming:**
- None

**Outgoing:**
- None

## Localization

**Limited i18n support:**
- TerrainPreProcessor has resource files for localization
- `Resources/Strings.resx` - Default (English)
- `Resources/Strings.zh-CN.resx` - Chinese (Simplified)
- `LocalizationService.cs` - Runtime localization

## Graphics Hardware Integration

**GPU Compute:**
- DirectX 12 Compute Shaders for LOD map generation
- StructuredBuffer read/write operations
- RWTexture2D for LOD map output

**Render Pipeline:**
- Custom `TerrainRenderFeature` integrates with Stride's render system
- Shadow map integration via `TerrainSharedShadowMapRendererProxy`
- Forward shading with custom effect `TerrainForwardShadingEffect`

---

*Integration audit: 2026-03-27*
