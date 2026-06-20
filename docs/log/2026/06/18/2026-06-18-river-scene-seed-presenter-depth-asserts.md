# River Scene Seed Presenter Depth Asserts
**Date**: 2026-06-18
**Session**: River scene seed depth source cleanup
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Remove fallback-style river scene seed depth selection and replace the preconditions with direct `Debug.Assert(...)` checks.

**Success Criteria:**
- `RiverRenderFeature` uses the known windowed editor/runtime source: `GraphicsDevice.Presenter.DepthStencilBuffer`.
- No `SelectSceneDepthSource`, command-list depth fallback, custom assertion helper, or explicit assertion-to-exception conversion remains in the scene seed path.
- River tests and Editor build pass.

---

## Context & Background

**Previous Work:**
- `2026-06-18-river-scene-seed-depth-payload.md` added `RiverSceneSeed` and RenderDoc recheck notes.
- New `debug.rdc` showed `RiverSceneSeed` RGB compression was effective, but alpha was still constant near clip because `commandList.DepthStencilBuffer` was not a valid scene-depth source in this path.

**Current State:**
- Stride source review established that normal windowed editor/runtime presenters create and expose `Presenter.DepthStencilBuffer`.
- `CommandList.DepthStencilBuffer` is transient output-merger state and is not a reliable handle to original scene depth after ForwardRenderer has resolved/rebound depth for transparent rendering.

---

## What We Did

### 1. Simplified Scene Seed Depth Source
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
```csharp
var sceneDepthSource = GetPresenterSceneDepthSource(context.GraphicsDevice, seedSource);
var sceneDepth = context.Resolver.ResolveDepthStencil(sceneDepthSource);
Debug.Assert(sceneDepth != null, "River scene seed requires a depth buffer that can be resolved as a shader resource.");
```

**Rationale:**
- For the current windowed path, Presenter depth is the correct scene-depth source.
- Fallback selection hid the bug that RenderDoc had already isolated: command-list depth produced a constant near-clip payload.

### 2. Removed Assertion Helper
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- Removed `AssertSceneSeed(...)`.
- Replaced helper calls with direct `Debug.Assert(...)`.
- Kept `try/finally` only for `ReleaseDepthStenctilAsShaderResource(sceneDepth)`; it does not catch or suppress exceptions.

**Rationale:**
- The user requested `Debug.Assert` directly and no explicit assertion-to-exception conversion.

### 3. Updated Documentation
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/log/2026/06/18/2026-06-18-river-scene-seed-depth-payload.md`

**Implementation:**
- Replaced old `SelectSceneDepthSource` / command-list fallback wording with direct Presenter depth + `Debug.Assert` semantics.
- Documented that offscreen/render-target presenters without depth need an explicit future scene-depth source instead of fallback.

---

## Decisions Made

### Decision 1: Presenter Depth Is the Current Source of Truth
**Context:** `debug.rdc` proved command-list depth produced invalid seed alpha.
**Decision:** Use `GraphicsDevice.Presenter.DepthStencilBuffer` directly for the current windowed editor/runtime path.
**Trade-offs:** Offscreen/render-target presenter support is not silently approximated; it needs explicit scene-depth injection later.
**Documentation Impact:** Updated architecture overview, current features, and the river scene seed session log.

### Decision 2: Assertions Stay Assertions
**Context:** The previous helper converted failed assertions into `InvalidOperationException`.
**Decision:** Use direct `Debug.Assert(...)` only.
**Trade-offs:** Release builds do not get a custom diagnostic exception from these preconditions.

---

## What Worked ✅

1. **Direct Presenter depth binding**
   - The code path is now explicit and test-covered.

2. **Text regression tests**
   - Tests now assert the absence of `SelectSceneDepthSource`, `AssertSceneSeed`, and `InvalidOperationException(message)`.

---

## What Didn't Work ❌

1. **Parallel verification**
   - Running tests and Editor build in parallel caused both MSBuild processes to write `Terrain.Editor.dll` under the same `obj` path.
   - Result was a false file-lock failure, not a code error.
   - Future verification should run these two commands serially.

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md`.
- [x] Update `docs/CURRENT_FEATURES.md`.
- [x] Update the previous river scene seed session log.

### Architectural Decisions That Changed
- **From:** depth selector with command-list fallback.
- **To:** explicit Presenter depth for windowed editor/runtime, with direct `Debug.Assert` preconditions.
- **Scope:** River pre-bottom scene seed pass.

---

## Code Quality Notes

### Testing
- `dotnet run --project 'E:\Stride Projects\Terrain\Terrain.Editor.Tests\Terrain.Editor.Tests.csproj' -c Debug` passed.
- `dotnet build 'E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj' -c Debug` passed.

### Technical Debt
- A 2026-06-18 03:00 RenderDoc capture confirmed `RiverSceneSeed` alpha is no longer constant near clip after Presenter depth binding: EID 248 alpha range was `12.3984..24.4375`.
- Code review found that the then-current seed alpha was still raw view-space depth while surface decode expected camera-relative distance. `RiverSceneSeed` was updated to reconstruct world position with `ProjectionInverse/ViewInverse/Eye` and write `length(positionWS.xyz - Eye.xyz)`.
- A new RenderDoc capture is still needed after the camera-distance shader revision to confirm the final alpha distribution and bank behavior.
- Offscreen/render-target river rendering still needs explicit scene-depth source design if it must support presenters without depth.

---

## Next Session

### Immediate Next Steps
1. Capture a fresh `debug.rdc` after the camera-distance seed shader revision and inspect `RiverSceneSeed` output alpha distribution.
2. Compare bank behavior against CK3 now that seed-only and bottom pixels share camera-distance payload semantics.
3. If offscreen river rendering becomes required, add an explicit scene-depth source API instead of reintroducing fallback selection.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `RiverRenderFeature.SeedSceneColorFromScene` now uses `GraphicsDevice.Presenter.DepthStencilBuffer`.
- Preconditions are direct `Debug.Assert(...)`; there is no helper and no explicit throw.
- `try/finally` remains only to release the resolved depth SRV.
- `RiverSceneSeed` alpha is camera-relative distance, matching `RiverCompressWorldSpace`.

**Gotchas for Next Session:**
- Do not reintroduce `commandList.DepthStencilBuffer` fallback for the scene seed pass.
- Do not run test and Editor build in parallel against the same `obj` output path.
