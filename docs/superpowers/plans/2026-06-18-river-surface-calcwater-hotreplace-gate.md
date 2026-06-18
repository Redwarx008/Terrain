# River Surface CalcWater Hot-Replace Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a reproducible RenderDoc hot-replace proof that the target `CalcWater` semantics can move the current river surface output into the target frame's dark-water energy range before any SDSL/C# implementation work.

**Architecture:** This plan is intentionally limited to the pre-SDSL gate. It creates neutral extraction artifacts under `artifacts/renderdoc/river_surface_calcwater_gate/`, validates external water inputs and project `Assets/River` resources, builds a current-like replacement shader as a control, then builds a target-style replacement shader as the proof. After this gate passes, a separate implementation plan should update `RiverSurface.sdsl`, generated shader keys, C# bindings, and Stride assets.

**Tech Stack:** RenderDoc frame captures, renderdoc-mcp, PowerShell, Python 3, Stride SDSL/HLSL shader replacement, existing `Terrain.Editor.Tests` console harness.

---

## Scope Boundary

This plan does not edit:

- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Rendering/River/*.cs`
- `Terrain.Editor/Assets/River/**/*.dds`
- `Terrain.Editor/Assets/River/**/*.sdtex`
- `Terrain.Editor/Terrain.Editor.sdpkg`

This plan may create or update:

- `artifacts/renderdoc/river_surface_calcwater_gate/`
- `tools/river/compare_river_surface_samples.py`
- `docs/log/2026/06/18/2026-06-18-river-surface-calcwater-hotreplace-gate.md`

The artifact directory is diagnostic output. Do not stage it unless the user explicitly asks to preserve the evidence files in git.

## File Structure

- Create: `tools/river/compare_river_surface_samples.py`  
  Reads exported PNGs plus sample-point JSON, writes per-point RGB deltas and an aggregate report.
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/session-env.ps1`  
  Local-only helper that resolves current capture, target capture, and external game root without putting external product names into committed code symbols.
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/samples.json`  
  Fixed sample points and expected current/target RGB ranges from the previous diagnosis.
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/resource-inventory.json`  
  Generated inventory of external water resources vs `Terrain.Editor/Assets/River`.
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/cbuffer-target.json`  
  Target surface water cbuffer dump.
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/surface_current_like.hlsl`  
  Control shader replacement that reproduces the current surface output.
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/surface_calcwater_gate.hlsl`  
  Gate shader replacement that ports the target `CalcWater` path using extracted parameters.
- Create: `docs/log/2026/06/18/2026-06-18-river-surface-calcwater-hotreplace-gate.md`  
  Session log containing pass IDs, exported files, sample reports, and the pass/fail decision.

---

### Task 1: Prepare Neutral Local Gate Workspace

**Files:**
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/session-env.ps1`
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/samples.json`

- [ ] **Step 1: Create the local diagnostic directory**

Run:

```powershell
New-Item -ItemType Directory -Force 'artifacts/renderdoc/river_surface_calcwater_gate' | Out-Null
```

Expected: the directory exists and remains untracked unless explicitly staged later.

- [ ] **Step 2: Create `session-env.ps1`**

Create `artifacts/renderdoc/river_surface_calcwater_gate/session-env.ps1` with:

```powershell
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$env:RIVER_CURRENT_RDC = Join-Path $env:USERPROFILE 'Desktop\debug1.rdc'
$env:RIVER_TARGET_RDC = (Get-ChildItem (Join-Path $env:USERPROFILE 'Desktop') -Filter '*-river.rdc' |
    Where-Object { $_.Name -ne 'debug1.rdc' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1).FullName
$env:RIVER_TARGET_GAME_ROOT = (Get-ChildItem 'E:\SteamLibrary\steamapps\common' -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'jomini\gfx\FX\jomini\jomini_water.fxh') } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1).FullName
$env:RIVER_GATE_DIR = Join-Path $root 'artifacts\renderdoc\river_surface_calcwater_gate'

foreach ($path in @($env:RIVER_CURRENT_RDC, $env:RIVER_TARGET_RDC, $env:RIVER_TARGET_GAME_ROOT, $env:RIVER_GATE_DIR)) {
    if (-not $path -or -not (Test-Path $path)) {
        throw "Required river gate path does not exist: $path"
    }
}

[pscustomobject]@{
    current_rdc = $env:RIVER_CURRENT_RDC
    target_rdc = $env:RIVER_TARGET_RDC
    target_game_root = $env:RIVER_TARGET_GAME_ROOT
    gate_dir = $env:RIVER_GATE_DIR
} | ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 (Join-Path $env:RIVER_GATE_DIR 'session-env.json')

Get-Content (Join-Path $env:RIVER_GATE_DIR 'session-env.json')
```

- [ ] **Step 3: Verify local path discovery**

Run:

```powershell
. .\artifacts\renderdoc\river_surface_calcwater_gate\session-env.ps1
```

Expected: JSON prints four valid paths. The target capture path must be the desktop river target frame, and the external game root must contain `jomini_water.fxh`.

- [ ] **Step 4: Create `samples.json`**

Create `artifacts/renderdoc/river_surface_calcwater_gate/samples.json` with the previously measured points:

```json
{
  "current_surface_event": 305,
  "target_surface_event": 466,
  "target_bottom_event": 338,
  "current_points": [
    { "name": "center_0", "x": 678, "y": 522, "current_rgb": [0.260, 0.499, 0.634], "refraction_rgb": [0.296, 0.217, 0.139] },
    { "name": "center_1", "x": 841, "y": 454, "current_rgb": [0.293, 0.520, 0.660], "refraction_rgb": [0.285, 0.199, 0.128] },
    { "name": "center_2", "x": 1078, "y": 362, "current_rgb": [0.284, 0.523, 0.649], "refraction_rgb": [0.298, 0.217, 0.130] },
    { "name": "center_3", "x": 1315, "y": 263, "current_rgb": [0.267, 0.517, 0.655], "refraction_rgb": [0.280, 0.198, 0.120] },
    { "name": "center_4", "x": 1479, "y": 119, "current_rgb": [0.275, 0.526, 0.659], "refraction_rgb": [0.268, 0.187, 0.123] }
  ],
  "target_surface_rgb_range": {
    "min": [0.008, 0.017, 0.021],
    "max": [0.023, 0.029, 0.032]
  },
  "gate": {
    "current_like_max_abs_error": 0.002,
    "target_style_rgb_max": [0.090, 0.120, 0.130],
    "target_style_blue_green_ratio_max": 1.40
  }
}
```

- [ ] **Step 5: Commit only the reusable comparison script later**

Do not commit `session-env.ps1` or `samples.json` in this task. They are local diagnostics tied to the user's captures and desktop paths.

---

### Task 2: Create The Sample Comparison Script

**Files:**
- Create: `tools/river/compare_river_surface_samples.py`

- [ ] **Step 1: Create the tools directory**

Run:

```powershell
New-Item -ItemType Directory -Force 'tools/river' | Out-Null
```

- [ ] **Step 2: Write `compare_river_surface_samples.py`**

Create `tools/river/compare_river_surface_samples.py` with:

```python
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from PIL import Image


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def sample_rgb(image: Image.Image, x: int, y: int) -> list[float]:
    pixel = image.convert("RGBA").getpixel((x, y))
    return [round(channel / 255.0, 6) for channel in pixel[:3]]


def max_abs_delta(a: list[float], b: list[float]) -> float:
    return max(abs(left - right) for left, right in zip(a, b))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--samples", required=True, type=Path)
    parser.add_argument("--image", required=True, type=Path)
    parser.add_argument("--expected", choices=["current", "refraction"], required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    spec = load_json(args.samples)
    image = Image.open(args.image)
    key = "current_rgb" if args.expected == "current" else "refraction_rgb"
    rows: list[dict[str, Any]] = []
    worst = 0.0

    for point in spec["current_points"]:
        actual = sample_rgb(image, point["x"], point["y"])
        expected = point[key]
        delta = max_abs_delta(actual, expected)
        worst = max(worst, delta)
        rows.append({
            "name": point["name"],
            "x": point["x"],
            "y": point["y"],
            "actual_rgb": actual,
            "expected_rgb": expected,
            "max_abs_delta": round(delta, 6),
        })

    report = {
        "image": str(args.image),
        "expected": args.expected,
        "worst_max_abs_delta": round(worst, 6),
        "points": rows,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 3: Install or expose Pillow if needed**

Run:

```powershell
python - <<'PY'
import PIL
print(PIL.__version__)
PY
```

Expected: prints the installed Pillow version. If it fails, use the bundled workspace Python dependencies or install Pillow into the local Python environment before running the comparison script.

- [ ] **Step 4: Commit the reusable script**

Run:

```powershell
git add tools/river/compare_river_surface_samples.py
git commit -m "test: add river surface sample comparison tool"
```

Expected: commit contains only the new reusable script.

---

### Task 3: Extract Target Water Shader Inputs And Resource Inventory

**Files:**
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/cbuffer-target.json`
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/resource-inventory.json`
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/shader-inputs.md`

- [ ] **Step 1: Load environment**

Run:

```powershell
. .\artifacts\renderdoc\river_surface_calcwater_gate\session-env.ps1
```

Expected: `$env:RIVER_TARGET_RDC`, `$env:RIVER_TARGET_GAME_ROOT`, and `$env:RIVER_GATE_DIR` are set.

- [ ] **Step 2: Record the exact source shader files**

Run:

```powershell
$shaderFiles = @(
  'jomini\gfx\FX\jomini\jomini_river_surface.fxh',
  'jomini\gfx\FX\jomini\jomini_water.fxh',
  'jomini\gfx\FX\jomini\jomini_water_default.fxh',
  'game\gfx\FX\river_surface.shader'
)
$shaderFiles | ForEach-Object {
  $full = Join-Path $env:RIVER_TARGET_GAME_ROOT $_
  if (-not (Test-Path $full)) { throw "Missing shader source: $full" }
  Get-FileHash $full -Algorithm SHA256 | Select-Object @{Name='relative_path';Expression={$_}}, Hash
} | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 (Join-Path $env:RIVER_GATE_DIR 'shader-source-hashes.json')
```

Expected: `shader-source-hashes.json` contains four SHA256 hashes.

- [ ] **Step 3: Inventory existing project water resources**

Run:

```powershell
$projectResources = @(
  'Terrain.Editor\Assets\River\Water\ambient-normal.dds',
  'Terrain.Editor\Assets\River\Water\flow-normal.dds',
  'Terrain.Editor\Assets\River\Water\foam.dds',
  'Terrain.Editor\Assets\River\Water\foam-ramp.dds',
  'Terrain.Editor\Assets\River\Water\foam-map.dds',
  'Terrain.Editor\Assets\River\Water\foam-noise.dds',
  'Terrain.Editor\Assets\River\Water\water-color.dds',
  'Terrain.Editor\Assets\River\Environment\reflection-specular.dds'
)
$inventory = foreach ($path in $projectResources) {
  if (-not (Test-Path $path)) { throw "Missing project resource: $path" }
  $hash = Get-FileHash $path -Algorithm SHA256
  [pscustomobject]@{
    path = $path.Replace('\','/')
    bytes = (Get-Item $path).Length
    sha256 = $hash.Hash
  }
}
$inventory | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 (Join-Path $env:RIVER_GATE_DIR 'resource-inventory.json')
```

Expected: every existing project water/environment resource has size and SHA256 recorded.

- [ ] **Step 4: Dump target surface cbuffer and SRV bindings**

Use renderdoc-mcp on `$env:RIVER_TARGET_RDC`:

1. Open the target capture.
2. Select event `466`.
3. Export the pixel shader constant buffers to `cbuffer-target.json`.
4. Export the pixel shader SRV/sampler table to `target-surface-bindings.json`.

Expected required cbuffer keys include:

```text
_ScreenResolution
_WaterReflectionNormalFlatten
_WaterZoomedInZoomedOutFactor
_WaterToSunDir
_WaterDiffuseMultiplier
_WaterColorShallow
_WaterSpecular
_WaterColorDeep
_WaterSpecularFactor
_WaterColorMapTint
_WaterColorMapTintFactor
_WaterGlossScale
_WaterGlossBase
_WaterFresnelBias
_WaterFresnelPow
_WaterCubemapIntensity
_WaterFoamScale
_WaterFoamDistortFactor
_WaterFoamShoreMaskDepth
_WaterFoamShoreMaskSharpness
_WaterFoamNoiseScale
_WaterFoamNoiseSpeed
_WaterFoamStrength
_WaterRefractionScale
_WaterRefractionShoreMaskDepth
_WaterRefractionShoreMaskSharpness
_WaterRefractionFade
_WaterWave1Scale
_WaterWave1Rotation
_WaterWave1Speed
_WaterWave2Scale
_WaterWave2Rotation
_WaterWave2Speed
_WaterWave3Scale
_WaterWave3Rotation
_WaterWave3Speed
_WaterWave1NormalFlatten
_WaterWave2NormalFlatten
_WaterWave3NormalFlatten
_WaterFlowTime
_WaterFlowMapSize
_WaterFlowNormalScale
_WaterFlowNormalFlatten
_WaterHeight
_WaterFadeShoreMaskDepth
_WaterFadeShoreMaskSharpness
_WaterSeeThroughDensity
_WaterSeeThroughShoreMaskDepth
_WaterSeeThroughShoreMaskSharpness
```

- [ ] **Step 5: Write `shader-inputs.md`**

Create `artifacts/renderdoc/river_surface_calcwater_gate/shader-inputs.md` with:

```markdown
# River Surface Shader Inputs

## Source Files

- `jomini/gfx/FX/jomini/jomini_river_surface.fxh`
- `jomini/gfx/FX/jomini/jomini_water.fxh`
- `jomini/gfx/FX/jomini/jomini_water_default.fxh`
- `game/gfx/FX/river_surface.shader`

## Target Events

- Current surface event: `305`
- Target surface event: `466`
- Target bottom event: `338`

## Required Gate Artifacts

- `cbuffer-target.json`
- `target-surface-bindings.json`
- `resource-inventory.json`
- `shader-source-hashes.json`
```

Expected: this file contains no project symbol names derived from the external product name.

---

### Task 4: Export Current And Target Render Targets

**Files:**
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/current_surface_original.png`
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/current_surface_refraction_only.png`
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/target_surface.png`
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/target_bottom.png`
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/current-like-report.json`
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/refraction-report.json`

- [ ] **Step 1: Export current original surface RT**

Use renderdoc-mcp:

1. Open `$env:RIVER_CURRENT_RDC`.
2. Select event `305`.
3. Export RT0 to `current_surface_original.png`.

Expected: exported image visually matches the blue current surface output.

- [ ] **Step 2: Export current refraction-only control**

Use the already validated refraction-only replacement on event `305`, or re-run the replacement shader that returns:

```hlsl
return RefractionTexture.Sample(RefractionSampler, input.Position.xy / float2(1672.0, 996.0));
```

Export RT0 to `current_surface_refraction_only.png`.

Expected: exported image shows brown raw refraction rather than blue water.

- [ ] **Step 3: Export target surface and target bottom RTs**

Use renderdoc-mcp:

1. Open `$env:RIVER_TARGET_RDC`.
2. Export event `466` RT0 to `target_surface.png`.
3. Export event `338` RT0 to `target_bottom.png`.

Expected: target surface is dark low-energy water; target bottom is brown raw riverbed/refraction input.

- [ ] **Step 4: Run the sample comparison script on current exports**

Run:

```powershell
python tools/river/compare_river_surface_samples.py `
  --samples artifacts/renderdoc/river_surface_calcwater_gate/samples.json `
  --image artifacts/renderdoc/river_surface_calcwater_gate/current_surface_original.png `
  --expected current `
  --output artifacts/renderdoc/river_surface_calcwater_gate/current-like-report.json

python tools/river/compare_river_surface_samples.py `
  --samples artifacts/renderdoc/river_surface_calcwater_gate/samples.json `
  --image artifacts/renderdoc/river_surface_calcwater_gate/current_surface_refraction_only.png `
  --expected refraction `
  --output artifacts/renderdoc/river_surface_calcwater_gate/refraction-report.json
```

Expected: each report has `worst_max_abs_delta <= 0.01`. If the delta is higher, update `samples.json` only after confirming the exported RT resolution and event are correct.

---

### Task 5: Build A Current-Like Replacement Shader As The Control

**Files:**
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/surface_current_like.hlsl`
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/current-like-hotreplace-report.json`

- [ ] **Step 1: Export current event `305` pixel shader reflection**

Use renderdoc-mcp to export event `305` pixel shader reflection into:

```text
artifacts/renderdoc/river_surface_calcwater_gate/current-surface-reflection.json
```

Expected: input signature contains the same semantic payload as the compiled `RiverSurface` dynamic effect: screen position, world position, river UV, tangent, normal, width, distance-to-main, and color target.

- [ ] **Step 2: Create `surface_current_like.hlsl`**

Write a minimal replacement shader that:

1. Declares the exact input struct from `current-surface-reflection.json`.
2. Declares the exact texture/sampler/register set from event `305`.
3. Returns the same output as current `RiverSurface.sdsl`.

The first control pass can be reduced to a direct refraction output:

```hlsl
float4 PSMain(PSInput input) : SV_Target0
{
    float2 uv = input.Position.xy / float2(1672.0, 996.0);
    return RefractionTexture.Sample(RefractionSampler, saturate(uv));
}
```

Expected: shader replacement compiles and produces the same refraction-only image exported in Task 4.

- [ ] **Step 3: Replace event `305` with the control shader and export**

Use renderdoc-mcp to:

1. Compile `surface_current_like.hlsl`.
2. Replace event `305` pixel shader.
3. Export RT0 to `current_like_hotreplace.png`.

Run:

```powershell
python tools/river/compare_river_surface_samples.py `
  --samples artifacts/renderdoc/river_surface_calcwater_gate/samples.json `
  --image artifacts/renderdoc/river_surface_calcwater_gate/current_like_hotreplace.png `
  --expected refraction `
  --output artifacts/renderdoc/river_surface_calcwater_gate/current-like-hotreplace-report.json
```

Expected: `worst_max_abs_delta <= 0.01`. This proves replacement wiring is active before porting target water logic.

---

### Task 6: Build The Target-Style CalcWater Replacement Shader

**Files:**
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/surface_calcwater_gate.hlsl`
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/calcwater-hotreplace-report.json`
- Create: `artifacts/renderdoc/river_surface_calcwater_gate/calcwater-output.png`

- [ ] **Step 1: Create the gate shader from the target source functions**

Create `surface_calcwater_gate.hlsl` by porting these functions and structs into D3D HLSL compatible with event `305`:

```text
SWaterParameters
SWaterOutput
SampleNormalMapTexture
CalcFoamFactor
CalcTerrainUnderwaterSeeThrough
CalcRefraction
CalcReflection
GetNonLinearGlossiness
FresnelSchlick
ImprovedBlinnPhong
CalculateSunLight
AmbientLight
ComposeLight
CalcWater
CalcRiverAdvanced
```

Use these current-project adaptations:

```text
CameraPosition -> _CameraWorldPosition
JOMINIWATER_GlobalTime -> _GlobalTime
JOMINIWATER_MapSize -> _MapWorldSize
MapSize -> _MapWorldSize
PdxTex2D / PdxTex2DLod0 -> Texture.Sample / Texture.SampleLevel
PdxTexCube -> TextureCube.Sample
DecompressWorldSpace -> RiverDecompressWorldSpace
```

Expected: the gate shader compiles in RenderDoc against event `305` without changing project SDSL.

- [ ] **Step 2: Populate all water constants from `cbuffer-target.json`**

In `surface_calcwater_gate.hlsl`, declare constants for every key listed in Task 3. Copy the numeric values from `cbuffer-target.json` into the replacement shader, using explicit float suffixes where applicable:

```hlsl
static const float3 GateWaterColorShallow = float3(0.0055146f, 0.0078107f, 0.0120865f);
static const float3 GateWaterColorDeep = float3(0.0001385f, 0.0001975f, 0.0002263f);
static const float GateWaterSpecular = 0.05f;
static const float GateWaterSpecularFactor = 0.01f;
static const float GateWaterGlossBase = 0.7f;
```

Expected: every target water constant used by the port is sourced from `cbuffer-target.json` or explicitly listed as an event-305 current input that cannot come from the target cbuffer.

- [ ] **Step 3: Use event `305` textures first, then swap any mismatched resource**

Bind existing event `305` SRVs first:

```text
AmbientNormalTexture
FlowNormalTexture
FoamTexture
FoamRampTexture
FoamMapTexture
FoamNoiseTexture
WaterColorTexture
ReflectionSpecularTexture
RefractionTexture
```

If `target-surface-bindings.json` shows a water SRV that differs from the project `Assets/River` resource hash, copy the external `.dds` into the existing neutral `Terrain.Editor/Assets/River/...` path only after recording the mismatch in `resource-inventory.json`. Do not change `.sdtex` IDs or asset URLs during this gate.

Expected: the first calcwater gate run identifies whether the remaining mismatch is shader math or resource data.

- [ ] **Step 4: Replace event `305` and export calcwater output**

Use renderdoc-mcp to:

1. Compile `surface_calcwater_gate.hlsl`.
2. Replace event `305` pixel shader.
3. Export RT0 to `calcwater-output.png`.

Expected: `calcwater-output.png` is visibly dark and no longer resembles the blue current surface.

- [ ] **Step 5: Write `calcwater-hotreplace-report.json`**

Sample the same five points and write a JSON report with:

```json
{
  "gate_passed": true,
  "criteria": {
    "rgb_max_under": [0.09, 0.12, 0.13],
    "blue_green_ratio_max": 1.4,
    "current_like_control_delta_max": 0.01
  },
  "points": []
}
```

Expected pass condition:

- All sampled RGB values are below `[0.09, 0.12, 0.13]`.
- Blue/green ratio does not exceed `1.4`.
- Current-like control remained within `0.01` max absolute error.

If the gate fails, do not edit `RiverSurface.sdsl`. Split the shader output into packed debug channels for water diffuse, foam, refraction, reflection, fresnel, and lighting, then rerun this task.

---

### Task 7: Record The Gate Result

**Files:**
- Create: `docs/log/2026/06/18/2026-06-18-river-surface-calcwater-hotreplace-gate.md`

- [ ] **Step 1: Create the session log**

Create `docs/log/2026/06/18/2026-06-18-river-surface-calcwater-hotreplace-gate.md` with:

```markdown
# River Surface CalcWater Hot-Replace Gate

**Date**: 2026-06-18
**Session**: River surface CalcWater hot-replace gate
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Prove or disprove the target `CalcWater` surface path in RenderDoc before changing project SDSL or C#.

**Success Criteria:**
- Current-like replacement reproduces the known refraction-only output.
- Target-style replacement moves current surface samples into dark-water energy range.
- Every water constant and SRV used by the replacement is traceable to a cbuffer dump, a capture binding, or an existing `Assets/River` resource.

---

## Artifacts

- `artifacts/renderdoc/river_surface_calcwater_gate/session-env.json`
- `artifacts/renderdoc/river_surface_calcwater_gate/cbuffer-target.json`
- `artifacts/renderdoc/river_surface_calcwater_gate/target-surface-bindings.json`
- `artifacts/renderdoc/river_surface_calcwater_gate/resource-inventory.json`
- `artifacts/renderdoc/river_surface_calcwater_gate/current-like-hotreplace-report.json`
- `artifacts/renderdoc/river_surface_calcwater_gate/calcwater-hotreplace-report.json`

---

## Result

Record pass/fail after Task 6:

- Current-like control:
- Target-style output:
- Gate decision:

---

## Next Step

If the gate passed, write the SDSL/C# implementation plan using the extracted constants, resource inventory, and target-style replacement shader as the source of truth.
If the gate failed, continue RenderDoc variable isolation and keep project SDSL unchanged.
```

- [ ] **Step 2: Fill the result section**

After Task 6, replace the three blank result lines with actual report numbers from the JSON outputs.

Expected: the session log contains enough evidence for another agent to decide whether SDSL implementation is allowed.

- [ ] **Step 3: Commit the reusable script and log only**

Run:

```powershell
git add tools/river/compare_river_surface_samples.py docs/log/2026/06/18/2026-06-18-river-surface-calcwater-hotreplace-gate.md
git commit -m "docs: record river surface calcwater hotreplace gate"
```

Expected: commit excludes `artifacts/renderdoc/river_surface_calcwater_gate/` unless the user explicitly requests preserving diagnostic artifacts in git.

---

## Verification Before Moving To SDSL/C#

Run:

```powershell
git status --short
rg -n "C[kK]3|c[kK]3" Terrain.Editor Terrain.Editor.Tests tools docs/superpowers docs/log/2026/06/18/2026-06-18-river-surface-calcwater-hotreplace-gate.md
dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-build
```

Expected:

- `git status --short` shows no unintended shader/C# edits.
- The search command does not find newly introduced source-label naming in touched code or docs.
- The test project builds and runs at the same baseline state as before this gate.

## Self-Review Notes

- Spec coverage: This first implementation plan covers Phase 0 and Phase 1 of the approved design: cbuffer/SRV extraction, resource inventory, current-like control, target-style hot-replace gate, and documented pass/fail evidence.
- Scope control: SDSL/C# binding, shader key regeneration, asset RootAssets, and scene input completion are intentionally deferred until the hot-replace gate produces a passing report.
- Naming constraint: The plan uses neutral project paths and does not create code symbols, directories, or asset URLs from external product or source-label names.
