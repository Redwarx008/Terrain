# Editor Save Dirty Resource Writes
**Date**: 2026-06-23
**Session**: Editor save selective write
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Make Editor Save write only authoring resources that have actually changed, instead of rewriting every resource on each save.

**Success Criteria:**
- Save can distinguish changed `default.toml`, heightmap, biome mask, material descriptor, and biome settings.
- `EditorResourceSaveService` only stages and writes dirty resources.
- Existing atomic rollback behavior remains for resources included in the current save transaction.
- Regression tests cover selective save behavior.

---

## Context & Background

**Previous Work:**
- Current Save path used `EditorShellViewModel -> TerrainManager -> EditorResourceSaveService`.
- Existing `EditorDirtyState` was a single boolean, enough for title state but not enough to decide which file should be written.

**Current State:**
- `EditorDirtyState` now tracks resource-level flags:
  - `MapDefinition`
  - `Heightmap`
  - `BiomeMask`
  - `MaterialDescriptor`
  - `BiomeSettings`
- Editor Save captures a dirty generation snapshot before building `EditorAuthoringSaveSnapshot`, then passes the snapshot to the background writer.

---

## What We Did

### 1. Resource-Level Dirty State
**Files Changed:** `Terrain.Editor/Services/EditorDirtyState.cs`

**Implementation:**
- Added `EditorDirtyResource` flags enum.
- `EditorDirtyState.IsDirty` is now derived from `DirtyResources != None`.
- `MarkDirty(...)` ORs dirty flags and increments per-resource dirty generations.
- `CaptureSnapshot()` records the dirty flags and generation values that belong to the save attempt.
- `ClearDirty(EditorDirtySnapshot)` clears only resources whose generation did not change after the snapshot.
- Dirty flags and generation counters are protected by a shared lock; `DirtyChanged` is raised after leaving the lock.

**Rationale:**
- A boolean dirty state cannot tell Save whether it should write `heightmap.png`, `default.toml`, `biome_settings.toml`, or other authoring files.

### 2. Selective Save Transaction
**Files Changed:** `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`, `Terrain.Editor/Services/Resources/EditorAuthoringSaveSnapshot.cs`

**Implementation:**
- `EditorAuthoringSaveSnapshot` carries the dirty generation snapshot captured on the UI thread.
- `EditorResourceSaveService.Save(...)` still defaults to all resources for compatibility with tool/test call sites.
- When dirty flags are supplied, the save service only:
  - validates writable targets for dirty resources,
  - creates staging files for dirty resources,
  - invokes writer classes for dirty resources,
  - commits a transaction containing only dirty resources.
- `EditorShellViewModel.Save` handles `None` before authoring snapshot cloning; the save service's `None` path only skips target validation, staging, writer calls, and commit for direct callers.

**Rationale:**
- Keeps the current atomic transaction model but narrows the transaction scope to modified files.

### 3. Dirty Source Mapping
**Files Changed:** `Terrain.Editor/Services/TerrainManager.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor/ViewModels/BiomeViewModel.cs`, `Terrain.Editor/Services/BiomeRuleService.cs`

**Implementation:**
- Height edits mark `Heightmap`.
- Height scale and river max visible camera height mark `MapDefinition`.
- Biome mask painting/import marks `BiomeMask`, while load-time mask sync can opt out.
- Albedo import and slot clear mark `MaterialDescriptor | BiomeSettings`, because they can create or remove material ids consumed by biome layers.
- Normal texture import marks only `MaterialDescriptor`.
- Biome rule/layer/modifier edits mark `BiomeSettings`.
- River map load no longer marks all authoring resources dirty, because current Save does not write `rivers.png`.

**Rationale:**
- Save should reflect persistent authoring files, not transient GPU sync state or loaded preview resources.

### 4. Regression Tests
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/EditorResourceSaveServiceTests.cs`, `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`

**Coverage:**
- Dirty `Heightmap` writes only heightmap.
- Dirty `MapDefinition` writes only `default.toml`.
- Dirty `BiomeMask`, `MaterialDescriptor`, and `BiomeSettings` each write only their own resource.
- `None` dirty resources skip commit.
- Partial dirty save failure rolls back staged dirty files without touching unrelated resources.
- Editor Save captures dirty generations before authoring snapshot/background save.
- Editor Save clears only captured dirty resources whose generation did not change, avoiding accidental loss of same-resource dirty flags created while the save was in flight.
- Dirty state tests cover same-resource re-dirty after snapshot and different-resource dirty after snapshot.
- Dirty state tests cover same-resource re-dirty from another thread.
- Editor workflow tests cover terminal failed save progress and material-id-changing operations marking biome settings dirty.
- Authoring snapshot tests cover map-only dirty snapshots skipping unrelated payload cloning.
- Save-service tests cover descriptor/settings joint writes for material-id-changing saves.
- Save-service tests cover missing generated biome mask detection before no-op Save.

### 5. First Code Review Follow-Up
**Files Changed:** `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`, tests

**Implementation:**
- Changed successful Save from `ClearDirty()` to `ClearDirty(dirtyResources)`.
- Added a text regression test so Save must clear only captured dirty flags.
- Added selective save tests for all remaining authoring resource types.
- Added a clearer no-op progress message: `No dirty authoring resources to save.`

**Rationale:**
- A subagent review caught that clearing all dirty state after save could drop dirty flags created while the save was in flight. Even though most UI mutations are blocked during Save, the dirty state itself should preserve unsaved flags by construction.

### 6. Second Code Review Follow-Up
**Files Changed:** `Terrain.Editor/Services/EditorDirtyState.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, tests, docs

**Implementation:**
- Added per-resource dirty generation counters.
- Added `EditorDirtySnapshot` and changed Editor Save to call `CaptureSnapshot()` before building the authoring snapshot.
- Changed successful Save cleanup to `ClearDirty(dirtySnapshot)` so a resource dirtied again after the save snapshot remains dirty.
- Added an early no-op branch in `EditorShellViewModel.Save` so no dirty resources skips authoring snapshot cloning and background file writes.
- Added `.gitignore` coverage for root `LaunchSetting.json`, which is local machine configuration and should not be committed.

**Rationale:**
- A second subagent review identified that clearing by flags alone still loses a same-resource mutation that happens after the save snapshot. Generation matching closes that gap without relying solely on UI input blocking.

### 7. C# Review Follow-Up
**Files Changed:** `Terrain.Editor/Services/TerrainManager.cs`, `Terrain.Editor/Services/Resources/EditorAuthoringSaveSnapshot.cs`, `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`, tests, docs

**Implementation:**
- Changed `TerrainManager.CreateAuthoringSaveSnapshot` to clone/build only payloads required by the dirty resources in the save snapshot.
- Made `EditorAuthoringSaveSnapshot` partial: non-dirty payloads can be `null`.
- Moved save-service payload null checks into the corresponding dirty writer branches.
- Changed the save-service `None` path to report terminal `Completed(...)` progress for direct callers.
- Added regression coverage proving a map-only dirty save does not require heightmap, biome mask, material descriptor, or biome settings payloads.

**Rationale:**
- A C# review subagent caught that the previous implementation avoided unnecessary file writes but still cloned large height/biome data during snapshot creation. That defeated part of the save optimization and could let unrelated payload validation block a resource-specific save.

### 8. Final Review Follow-Up
**Files Changed:** `Terrain.Editor/Services/EditorDirtyState.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, tests, docs

**Implementation:**
- Added locking around dirty flags and per-resource generation read/write paths; events remain lock-free from subscriber perspective.
- Marked material-id-changing operations (`ImportAlbedoTexture`, asset delete, selected slot clear) as `MaterialDescriptor | BiomeSettings`.
- Kept normal texture import scoped to `MaterialDescriptor`.
- Added `AuthoringSaveProgress.Failed(...)` reporting in the Save exception path.
- Added regression coverage for cross-thread re-dirty after snapshot, failed save progress, and material-id dependency dirty marking.

**Rationale:**
- A final review identified two real risks: generation snapshots were not reliable under cross-thread dirty calls without synchronization, and descriptor-only partial saves could write material descriptors that no longer matched biome settings references. The fixes keep the selective-save model while preserving consistency.

### 9. Dispatcher and Behavior-Test Follow-Up
**Files Changed:** `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `.gitignore`, tests, docs

**Implementation:**
- `OnEditorDirtyChanged` now checks `Dispatcher.UIThread.CheckAccess()` and posts `RefreshProjectState` back to the Avalonia UI thread when dirty events are raised from background threads.
- Added `.superpowers/` to `.gitignore` so local skill/server scratch state cannot be accidentally committed.
- Added a direct authoring snapshot regression test proving map-only dirty snapshots keep height, biome mask, descriptor, and biome settings payloads null.
- Added a save-service behavior test proving descriptor and biome settings are written together when material ids may change.

**Rationale:**
- Review confirmed the dirty state should remain UI-agnostic, but UI subscribers must marshal themselves. The additional behavior tests guard the performance and consistency invariants without relying only on source text checks.

### 10. Missing Generated Resource Follow-Up
**Files Changed:** `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor/Services/Resources/EditorGeneratedAuthoringResourceDetector.cs`, `Terrain.Editor/Services/TerrainManager.cs`, tests, docs

**Implementation:**
- Added generated authoring resource detection before the Save no-op gate.
- If `biome_mask.png` is missing but the editor has in-memory biome mask data, Save adds `BiomeMask` to the current save snapshot without permanently marking global dirty state.
- Added `EditorDirtySnapshot.WithAdditionalResources(...)` so Save can merge generated-resource flags while preserving captured dirty generations.
- Removed the unused `markDirty` parameter from `LoadRiverMap`; river maps are currently read-only in authoring Save.
- Added regression coverage for missing generated biome mask detection and updated workflow text tests for generated-resource merge ordering.

**Rationale:**
- Selective Save initially skipped all work when dirty resources were `None`, which broke the documented first-save generation path for missing `biome_mask.png`. Generated-resource detection keeps no-op Save fast while still creating authoring files that can be generated from loaded editor state.

### 11. Material Id Consistency Review Follow-Up
**Files Changed:** `Terrain.Editor/Services/TerrainManager.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, tests, docs

**Implementation:**
- `CreateAuthoringSaveSnapshot` now inspects active material slots before building biome settings snapshots.
- If the save was `BiomeSettings`-only but an active non-runtime material slot has no `MaterialId`, the snapshot adds `MaterialDescriptor` to the resources saved in the same transaction.
- Successful UI Save now clears dirty state using `snapshot.DirtySnapshot`, not the earlier local variable, so generated or promoted save resources use the same final snapshot for writing and cleanup.
- Added a regression test proving a BiomeSettings-only snapshot with a generated material id writes descriptor and biome settings together.

**Rationale:**
- A subagent review caught that `CreateMaterialDescriptorSlots` can generate and assign material ids while building the biome material-id map. If that happened in a BiomeSettings-only save, `biome_settings.toml` could reference an id that was never persisted to `materials/descriptor.toml`.

### 12. Material Id Failure-Retry Follow-Up
**Files Changed:** `Terrain.Editor/Services/Resources/EditorAuthoringResourceMapper.cs`, tests, docs

**Implementation:**
- Made `EditorAuthoringResourceMapper.CreateMaterialDescriptorSlots` pure with respect to `MaterialSlot.MaterialId`: it can derive/export a material id, but it no longer writes that id back to the live material slot.
- Added mapper coverage proving generated ids do not mutate live slots.
- Extended the BiomeSettings-only promotion snapshot test to assert snapshot capture does not mutate the live slot id.

**Rationale:**
- A later subagent review caught that the previous promotion fix still had a failure-retry hole: snapshot creation generated and wrote live `MaterialId`, so if the later file write failed, the next Save could miss the "slot needs generated id" condition and skip descriptor promotion. Keeping snapshot mapping pure preserves retry behavior.

### 13. Committed Material Id Follow-Up
**Files Changed:** `Terrain.Editor/Services/MaterialSlotManager.cs`, `Terrain.Editor/Services/Resources/EditorAuthoringResourceMapper.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, tests, docs

**Implementation:**
- Added `MaterialSlotManager.ApplyCommittedDescriptorIds(...)` to write committed descriptor ids back to live material slots after save succeeds.
- `EditorShellViewModel.Save` now calls the committed-id sync only after `SaveAuthoringResources` returns successfully.
- `EditorAuthoringResourceMapper.CreateMaterialDescriptorSlots` now reserves existing material ids before generating ids for missing slots, so a newly added lower-index duplicate name cannot steal an existing committed id.
- Added tests for committed-id sync and duplicate-name/id stability.

**Rationale:**
- A follow-up review caught the other side of mapper purity: failed saves must not mutate live ids, but successful saves should still stabilize live ids for later Save/Export. Applying committed ids after the transaction succeeds preserves both properties.

---

## Decisions Made

### Decision 1: Use Resource Flags Instead of File Hashing
**Context:** Need to avoid rewriting unchanged authoring files.

**Options Considered:**
1. Compare generated output with existing file contents before replace.
2. Track dirty state by authoring resource.
3. Split each resource into independent save services.

**Decision:** Track dirty state by authoring resource.

**Rationale:**
- Avoids paying serialization cost for unchanged PNG/TOML resources.
- Preserves the existing snapshot and atomic transaction architecture.
- Keeps dirty semantics close to editor mutations.

**Trade-offs:**
- Mutation sites must mark the correct resource flag.
- Future saved resources need a new dirty flag and mapping.

---

## What Worked ✅

1. **Testing save service directly**
   - The existing save fixture made it straightforward to prove untouched files remain unchanged.

2. **Keeping old API default behavior**
   - Existing call sites that do not pass dirty flags still write all resources, reducing compatibility risk.

---

## What Didn't Work ❌

1. **Single boolean dirty state**
   - It was enough for UI title state but not for save routing.

---

## Architecture Impact

### Documentation Updated
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

### New Pattern
**Resource-level authoring dirty generation snapshot**
- Use when an editor mutation maps to a specific saved authoring file and a save snapshot can be in flight.
- Add new flags when Save begins writing a new authoring resource.
- Clear save state with `ClearDirty(EditorDirtySnapshot)`, not plain flags, so a newer same-resource dirty mark is preserved.
- If a mutation can change material ids, mark both `MaterialDescriptor` and `BiomeSettings` so the descriptor/settings relationship is validated and written transactionally.
- If a BiomeSettings-only save needs to generate missing material ids for active slots, promote the snapshot to include `MaterialDescriptor`; settings must not persist references to ids absent from descriptor.
- Snapshot material id generation must not mutate live material slots; otherwise a failed save can hide the condition that requires descriptor promotion on retry.
- Successful descriptor saves should apply committed ids back to live material slots after the transaction succeeds; this keeps later id generation stable.
- Existing material ids must be reserved before generating ids for missing slots, so new slots cannot steal older ids.
- Dirty event subscribers that touch Avalonia-bound state must marshal back to the UI thread because dirty events can be raised from editor/runtime worker paths.
- Generated authoring resources should be merged into the save snapshot before the no-op gate; do not use global dirty state just to create missing generated files.

---

## Testing

**Automated:**
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

**Result:**
- All tests passed after the code review follow-ups.
- Existing NuGet vulnerability and compile warnings remain unrelated to this change.

---

## Next Session

### Immediate Next Steps
1. If `rivers.png` authoring writeback is added later, introduce a dedicated `RiverMap` dirty flag instead of using `All`.

### Gotchas for Next Session
- Do not use `EditorDirtyResource.All` for a new mutation unless it truly affects every saved authoring file.
- `LoadBiomeMask(markDirty: false)` must not mark `BiomeMask` dirty during workspace load.
- Current Save still does not write `rivers.png`.
- Use `ClearDirty(EditorDirtySnapshot)` for Save completion; clearing by flags alone can erase a same-resource dirty mark created after the save snapshot.
- Do not save material-id-changing descriptor edits without `BiomeSettings`; biome layer references are serialized by material id, not slot index.
- Do not let `BiomeSettings` generate new material ids without persisting `MaterialDescriptor` in the same save.
- Do not write generated save-only material ids back to `MaterialSlot.MaterialId` during snapshot creation; commit/reload should be the boundary for persistent ids.
- Do apply committed descriptor ids after a successful save, and only after the writer returns without throwing.
- Do not handle `DirtyChanged` in UI view models without checking `Dispatcher.UIThread.CheckAccess()`.
- Missing `biome_mask.png` is generated on Save only when in-memory biome mask data exists.

---

## Session Statistics

**Files Changed:** 15
**Commits:** 0

---

## Quick Reference

**Key implementation:**
- `Terrain.Editor/Services/EditorDirtyState.cs`
- `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`
- `Terrain.Editor/ViewModels/EditorShellViewModel.cs`

**Current status:**
- Editor Save writes only dirty authoring resources.
- Dirty state snapshot/clear is synchronized and material descriptor changes that affect material ids are saved together with biome settings.
- Dirty change notifications are marshalled by the shell before touching Avalonia-bound state.
- Missing generated `biome_mask.png` no longer gets skipped by the no-op save path.
