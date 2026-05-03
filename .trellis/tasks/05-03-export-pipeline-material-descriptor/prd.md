# brainstorm: export pipeline material descriptor

## Goal

Understand why the current export pipeline still routes through or emits a material descriptor, and decide whether/how the pipeline should change.

## What I Already Know

* User asked: "现在导出链路为什么还是material descriptor".
* The question is about the Terrain project export pipeline.
* Engine source is located at `E:\WorkSpace\stride`.
* Existing architecture records an intentional decision to export `.terrain` plus a separate `biome_config.toml`.
* Runtime rendering also creates a Stride `MaterialDescriptor` to build `Material.New(...).Passes[0]`; this is separate from the exported TOML descriptor.

## Assumptions (temporary)

* "material descriptor" likely refers to a Stride material asset descriptor or generated material representation in the export path.
* The desired outcome may be explanation first, then a possible design/implementation change.

## Open Questions

* Is the target behavior to remove material descriptor from the export chain entirely, or to understand why it remains before deciding?

## Requirements (evolving)

* Identify the relevant export pipeline files.
* Trace where material descriptor is introduced or preserved.
* Summarize the reason in terms of current code structure and constraints.
* Distinguish export-time material config TOML from render-time Stride `MaterialDescriptor`.
* Rename the export-facing concept from Material Descriptor to Biome Config.

## Acceptance Criteria (evolving)

* [x] The current material descriptor dependency is traced to concrete files/functions.
* [x] The explanation distinguishes intentional design from leftover/legacy behavior.
* [x] Export-facing naming uses Biome Config instead of Material Descriptor.

## Definition of Done (team quality bar)

* Tests added/updated if behavior changes.
* Lint / typecheck / CI green if code changes are made.
* Docs/notes updated if behavior changes.
* Rollout/rollback considered if risky.

## Out of Scope (explicit)

* No implementation until the desired export target is confirmed.

## Technical Notes

* `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs` exports only `.terrain` binary terrain data: height VT, MinMaxErrorMap, biome mask VT.
* `Terrain.Editor/Services/Export/Exporters/BiomeConfigExporter.cs` exports runtime biome config to standalone TOML via `TomlProjectConfig.WriteTo`.
* `Terrain.Editor/ViewModels/EditorShellViewModel.cs` registers both exporters and exposes separate `ExportTerrain` / `ExportBiomeConfig` commands.
* `easysdd/compound/2026-04-20-decision-biome-config-export.md` records the design decision: standalone `biome_config.toml` chosen over runtime reading editor TOML or embedding material data into `.terrain`.
* `Terrain/Core/TerrainProcessor.cs` consumes `TerrainComponent.BiomeConfigPath`, reads `RuntimeBiomeConfig`, builds detail maps and initializes `RuntimeMaterialManager`.
* `Terrain/Core/TerrainProcessor.cs` and `Terrain.Editor/Rendering/EditorTerrainProcessor.cs` both construct `new MaterialDescriptor()` for Stride's render material pass. This is render pipeline plumbing, not the export descriptor file.
