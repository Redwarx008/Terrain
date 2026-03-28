---
phase: 01
slug: project-foundation
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-29
---

# Phase 01 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.6+ |
| **Config file** | `tests/Terrain.Editor.Tests/xunit.json` |
| **Quick run command** | `dotnet test tests/Terrain.Editor.Tests --filter "FullyQualifiedName~Unit" --no-build` |
| **Full suite command** | `dotnet test tests/Terrain.Editor.Tests` |
| **Estimated runtime** | ~5 seconds |

---

## Sampling Rate

- **After every task commit:** Run quick unit tests
- **After every plan wave:** Run full suite
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 10 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 01-01-01 | 01-01 | 1 | PREV-02, PREV-03, PREV-04 | unit | `dotnet test --filter "HybridCameraController"` | ❌ W0 | ⬜ pending |
| 01-02-01 | 01-02 | 1 | PREV-01 | unit | `dotnet test --filter "HeightmapLoader"` | ❌ W0 | ⬜ pending |
| 01-02-02 | 01-02 | 1 | PREV-01 | unit | `dotnet test --filter "TerrainManager"` | ❌ W0 | ⬜ pending |
| 01-03-01 | 01-03 | 2 | PREV-01 | integration | `dotnet test --filter "SceneRenderTargetManager"` | ❌ W0 | ⬜ pending |
| 01-03-02 | 01-03 | 2 | PREV-02, PREV-03, PREV-04 | integration | `dotnet test --filter "SceneViewPanel"` | ❌ W0 | ⬜ pending |
| 01-04-01 | 01-04 | 3 | PREV-01 | integration | `dotnet test --filter "EditorGame"` | ❌ W0 | ⬜ pending |
| 01-04-02 | 01-04 | 3 | PREV-01 | integration | `dotnet test --filter "MainWindow"` | ❌ W0 | ⬜ pending |
| 01-04-03 | 01-04 | 3 | PREV-01, PREV-02, PREV-03, PREV-04 | manual | Visual inspection of terrain rendering | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/Terrain.Editor.Tests/Terrain.Editor.Tests.csproj` — xUnit test project
- [ ] `tests/Terrain.Editor.Tests/HybridCameraControllerTests.cs` — unit tests for PREV-02, PREV-03, PREV-04
- [ ] `tests/Terrain.Editor.Tests/HeightmapLoaderTests.cs` — unit tests for PNG loading
- [ ] `tests/Terrain.Editor.Tests/TerrainManagerTests.cs` — unit tests for terrain management
- [ ] `tests/Terrain.Editor.Tests/xunit.json` — test configuration (parallelization, max threads)
- [ ] Add project reference to `Terrain.Editor/Terrain.Editor.csproj` in test project

*Wave 0 must be completed before Phase 1 execution begins.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Terrain renders with LOD in viewport | PREV-01 | Requires GPU and visual inspection | 1. Open editor 2. Load a heightmap via File→Open 3. Verify terrain mesh appears with proper LOD transitions |
| Camera orbit feels natural | PREV-02 | Subjective UX quality | 1. Right-drag to orbit 2. Verify smooth rotation around terrain center |
| Camera pan works correctly | PREV-03 | Visual correctness | 1. Middle-drag to pan 2. Verify camera moves parallel to ground |
| Camera zoom feels responsive | PREV-04 | Subjective UX quality | 1. Scroll to zoom 2. Verify zoom is smooth and centered on orbit point |
| Free-fly mode switch | D-04 | Interaction quality | 1. Hold Shift 2. Verify WASD + mouse look works 3. Release Shift, verify returns to orbit |
| Double-click reset | D-05 | Interaction correctness | 1. Pan/orbit to move focus 2. Double-click in viewport 3. Verify camera resets to terrain center |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 10s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
