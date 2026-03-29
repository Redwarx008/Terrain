# Requirements: Terrain Slot Editor

**Defined:** 2026-03-29
**Core Value:** Real-time 3D preview brush-based terrain editing - WYSIWYG height and material editing experience

## v1 Requirements

### Height Editing

- [ ] **HEIGHT-01**: User can raise terrain height with circular brush
- [ ] **HEIGHT-02**: User can lower terrain height with circular brush
- [ ] **HEIGHT-03**: User can smooth terrain heights (averaging neighbors)
- [ ] **HEIGHT-04**: User can flatten terrain to target height

### Brush System

- [ ] **BRUSH-01**: User can adjust brush size via slider/input
- [ ] **BRUSH-02**: User can adjust brush strength/opacity via slider/input
- [ ] **BRUSH-03**: User can select circular brush shape
- [ ] **BRUSH-04**: User can select square brush shape
- [ ] **BRUSH-05**: User can select noise-based brush shape
- [ ] **BRUSH-06**: User can adjust brush falloff/feathering

### Material Painting

- [ ] **MAT-01**: User can paint material slots on terrain (R8 SplatMap)
- [ ] **MAT-02**: User can select active material slot (1-256)
- [ ] **MAT-03**: Material rendering uses bilinear blending between slots
- [ ] **MAT-04**: Material rendering applies noise perturbation to blend edges

### Material Management

- [ ] **MGMT-01**: User can view available material slots with preview thumbnails
- [ ] **MGMT-02**: User can import custom material textures (albedo, normal)
- [ ] **MGMT-03**: System provides default material pack

### File I/O

- [ ] **FILE-01**: User can open heightmap PNG via file dialog
- [ ] **FILE-02**: User can open splatmap PNG via file dialog (creates default if missing)
- [ ] **FILE-03**: User can export heightmap as PNG
- [ ] **FILE-04**: User can export splatmap as PNG
- [ ] **FILE-05**: User can export to .terrain format for runtime

### Preview & Navigation

- [ ] **PREV-01**: User can see real-time 3D preview with terrain LOD
- [x] **PREV-02**: User can orbit camera around terrain
- [x] **PREV-03**: User can pan camera
- [x] **PREV-04**: User can zoom camera in/out
- [ ] **PREV-05**: User can see brush preview cursor in viewport

### Undo/Redo

- [ ] **UNDO-01**: User can undo last edit operation
- [ ] **UNDO-02**: User can redo undone operation
- [ ] **UNDO-03**: User can configure max undo history depth
- [ ] **UNDO-04**: Undo/Redo works for all height editing operations
- [ ] **UNDO-05**: Undo/Redo works for material painting operations

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Enhanced Brushes

- **BRUSH-07**: User can import custom brush shapes from grayscale images
- **BRUSH-08**: User can save brush presets

### Advanced Height Editing

- **HEIGHT-05**: User can apply noise perturbation to heights
- **HEIGHT-06**: User can mirror editing on X or Z axis

### Large Terrain Support

- **PAGE-01**: Heightmap paging for terrains > 4K resolution
- **PAGE-02**: Background loading/unloading of heightmap pages

### Advanced Material

- **MAT-05**: Material layer blending preview in editor
- **MAT-06**: Per-material height offset for parallax effect

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Erosion simulation | Complex physics simulation, not core to slot editing |
| Procedural generation | Different product category (World Machine/Gaea) |
| Vegetation/object placement | Marked out-of-scope, GUI space reserved for future |
| Runtime terrain deformation | Editor is offline tool, not game runtime |
| Multiplayer collaboration | Significant complexity for niche use case |
| Terrain holes/caves | Requires mesh manipulation beyond heightmap |
| Road/river tools | Specialized tools requiring pathfinding logic |
| Auto-LOD generation during editing | LOD is for export, generation at export time only |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| PREV-01 | Phase 1 | Pending |
| PREV-02 | Phase 1 | Complete |
| PREV-03 | Phase 1 | Complete |
| PREV-04 | Phase 1 | Complete |
| BRUSH-01 | Phase 2 | Pending |
| BRUSH-02 | Phase 2 | Pending |
| BRUSH-03 | Phase 2 | Pending |
| BRUSH-06 | Phase 2 | Pending |
| HEIGHT-01 | Phase 3 | Pending |
| HEIGHT-02 | Phase 3 | Pending |
| HEIGHT-03 | Phase 3 | Pending |
| HEIGHT-04 | Phase 3 | Pending |
| PREV-05 | Phase 3 | Pending |
| UNDO-01 | Phase 4 | Pending |
| UNDO-02 | Phase 4 | Pending |
| UNDO-03 | Phase 4 | Pending |
| UNDO-04 | Phase 4 | Pending |
| UNDO-05 | Phase 4 | Pending |
| BRUSH-04 | Phase 5 | Pending |
| BRUSH-05 | Phase 5 | Pending |
| MGMT-01 | Phase 6 | Pending |
| MGMT-02 | Phase 6 | Pending |
| MGMT-03 | Phase 6 | Pending |
| MAT-01 | Phase 7 | Pending |
| MAT-02 | Phase 7 | Pending |
| MAT-03 | Phase 7 | Pending |
| MAT-04 | Phase 7 | Pending |
| FILE-01 | Phase 8 | Pending |
| FILE-02 | Phase 8 | Pending |
| FILE-03 | Phase 8 | Pending |
| FILE-04 | Phase 8 | Pending |
| FILE-05 | Phase 8 | Pending |

**Coverage:**
- v1 requirements: 32 total
- Mapped to phases: 32
- Unmapped: 0

---
*Requirements defined: 2026-03-29*
*Last updated: 2026-03-29 after roadmap creation*
