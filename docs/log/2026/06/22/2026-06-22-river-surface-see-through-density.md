# River Refraction Clamp From Mesh Height
**Date**: 2026-06-22
**Status**: Complete
**Priority**: High

---

## Session Goal

Diagnose why `C:\Users\Redwa\Desktop\debug2.rdc` showed river bottom visible on the left side but missing on the right side, even though earlier work made `_RefractionMaxCameraHeight` adaptive.

---

## Context & Background

The first pass incorrectly treated the symptom as a see-through density problem and temporarily changed `_WaterSeeThroughDensity` from `0.8f` to `0.4f`. That direction was withdrawn after comparing `debug2.rdc`: the left/right split correlates with actual river height crossing the refraction camera clamp plane, not with a global density mismatch.

Previous `_RefractionMaxCameraHeight` logic adapted only to terrain `HeightScale`. In the failing capture the active cbuffer still had `_RefractionMaxCameraHeight=100`, while the right-side river surface and terrain pixels were around `Y=105`.

---

## RenderDoc Diagnosis

Opened `C:\Users\Redwa\Desktop\debug2.rdc` through `renderdoc-mcp`.

Relevant events:
- `204`: terrain/main scene
- `248`: `RiverSceneSeed`
- `276`, `290`, `304`, `318`, `332`: bottom/refraction writes
- `365`, `385`, `405`, `425`, `445`: surface draws
- `1101`: final post target

Key observations:
- The right-side bottom was present in the half-resolution bottom/refraction target.
- Representative left visible point: full-res `(300,360)`, surface world Y about `98.70`.
- Representative right dark point: full-res `(1012,491)`, surface world Y about `105.07`.
- Corresponding terrain Y values were about `98.75` on the left and `105.22` on the right, so the river surface was not floating above terrain.
- Surface draw `405` pixel shader cbuffer contained `_RefractionMaxCameraHeight=100.0`.

Conclusion: the old adaptive clamp was too low for the generated river mesh. It used terrain height scale as a proxy, but the actual generated river vertices can exceed that value.

---

## Fix

`RiverMeshService.BuildRiverMesh` now derives `RefractionMaxCameraHeight` from the generated mesh bounds:

```csharp
float refractionMaxCameraHeight = float.IsFinite(boundingBox.Maximum.Y)
    ? MathF.Ceiling(boundingBox.Maximum.Y + 1.0f)
    : 50.0f;
```

`RiverCommon.sdsl` still applies `max(_RefractionMaxCameraHeight, 50.0f)` in the shader. That keeps CK3's low-elevation `MaxHeight=50` behavior while allowing high generated river meshes to raise the clamp plane above their actual maximum Y.

The temporary `_WaterSeeThroughDensity=0.4f` source change was reverted. `_WaterSeeThroughDensity` remains `0.8f`.

---

## Files Changed

- `Terrain/Rivers/RiverMeshService.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

---

## Testing

Verified:
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
- `dotnet build Terrain\Terrain.csproj --no-restore`
- `git diff --check`

Notes:
- Test/build passed with existing NuGet advisory warnings and existing compiler warnings.
- `git diff --check` passed with CRLF warnings only.

---

## Next Session

1. Capture a fresh RenderDoc frame after rebuilding/running the editor.
2. Verify seed, bottom, and surface cbuffers all receive a `_RefractionMaxCameraHeight` above the actual visible river `Bounds.MaxY`.
3. If bottom is still hidden after the clamp fix, resume from surface composition or refraction source/timing, not from mesh generation or `_WaterSeeThroughDensity`.
