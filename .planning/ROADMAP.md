# Roadmap: Terrain Slot Editor

**Created:** 2026-03-29
**Granularity:** fine
**Core Value:** Real-time 3D preview brush-based terrain editing - WYSIWYG height and material editing experience

## Phases

- [ ] **Phase 1: Project Foundation** - Real-time 3D preview with camera navigation
- [ ] **Phase 2: Brush System Core** - Circular brush with size, strength, and falloff
- [ ] **Phase 3: Height Editing** - Sculpt terrain with raise/lower/smooth/flatten tools
- [ ] **Phase 4: Undo/Redo System** - Configurable history for all editing operations
- [ ] **Phase 5: Enhanced Brushes** - Square and noise brush shapes
- [ ] **Phase 6: Material Management** - Material slots with thumbnails and import
- [ ] **Phase 7: Material Slot Painting** - R8 SplatMap painting with bilinear blending
- [ ] **Phase 8: File I/O** - Load and export heightmap, splatmap, and .terrain files

---

## Phase Details

### Phase 1: Project Foundation

**Goal**: Users can see real-time 3D terrain preview and navigate around it

**Depends on**: Nothing (first phase)

**Requirements**: PREV-01, PREV-02, PREV-03, PREV-04

**Success Criteria** (what must be TRUE):
1. User can see terrain rendered with LOD in the viewport
2. User can orbit camera around terrain center
3. User can pan camera to view different terrain areas
4. User can zoom camera in/out to examine details

**Plans**: 4 plans in 3 waves

**Plans List**:
- [x] 01-01-PLAN.md - Create HybridCameraController for orbit and free-fly camera modes
- [ ] 01-02-PLAN.md - Create HeightmapLoader and TerrainManager services
- [ ] 01-03-PLAN.md - Integrate camera and terrain into SceneViewPanel with RenderTarget
- [ ] 01-04-PLAN.md - Wire File -> Open menu and complete editor integration

**UI hint**: yes

---

### Phase 2: Brush System Core

**Goal**: Users can configure brush parameters for terrain editing

**Depends on**: Phase 1

**Requirements**: BRUSH-01, BRUSH-02, BRUSH-03, BRUSH-06

**Success Criteria** (what must be TRUE):
1. User can adjust brush size via UI slider or input field
2. User can adjust brush strength/opacity via UI slider or input field
3. User can select circular brush shape for editing
4. User can adjust brush falloff/feathering for smooth edges

**Plans**: TBD

**UI hint**: yes

---

### Phase 3: Height Editing

**Goal**: Users can sculpt terrain height with brush-based tools

**Depends on**: Phase 2

**Requirements**: HEIGHT-01, HEIGHT-02, HEIGHT-03, HEIGHT-04, PREV-05

**Success Criteria** (what must be TRUE):
1. User can raise terrain height by painting with brush
2. User can lower terrain height by painting with brush
3. User can smooth terrain heights to average neighbors
4. User can flatten terrain to a target height
5. User can see brush preview cursor in viewport before applying

**Plans**: TBD

**UI hint**: yes

---

### Phase 4: Undo/Redo System

**Goal**: Users can undo and redo all editing operations with configurable history

**Depends on**: Phase 3

**Requirements**: UNDO-01, UNDO-02, UNDO-03, UNDO-04, UNDO-05

**Success Criteria** (what must be TRUE):
1. User can undo last edit operation (height or material)
2. User can redo previously undone operation
3. User can configure max undo history depth in settings
4. Undo/Redo works correctly for all height editing operations
5. Undo/Redo works correctly for all material painting operations

**Plans**: TBD

**UI hint**: yes

---

### Phase 5: Enhanced Brushes

**Goal**: Users have additional brush shape options for varied editing

**Depends on**: Phase 4

**Requirements**: BRUSH-04, BRUSH-05

**Success Criteria** (what must be TRUE):
1. User can select square brush shape for sharp edges
2. User can select noise-based brush shape for organic variation

**Plans**: TBD

**UI hint**: yes

---

### Phase 6: Material Management

**Goal**: Users can manage material slots for terrain painting

**Depends on**: Phase 5

**Requirements**: MGMT-01, MGMT-02, MGMT-03

**Success Criteria** (what must be TRUE):
1. User can view available material slots with preview thumbnails
2. User can import custom material textures (albedo, normal maps)
3. User has access to a default material pack for immediate use

**Plans**: TBD

**UI hint**: yes

---

### Phase 7: Material Slot Painting

**Goal**: Users can paint material slots on terrain with R8 SplatMap storage

**Depends on**: Phase 6

**Requirements**: MAT-01, MAT-02, MAT-03, MAT-04

**Success Criteria** (what must be TRUE):
1. User can paint material slots on terrain surface
2. User can select active material slot (1-256) for painting
3. Material rendering shows bilinear blending between slots
4. Material rendering applies noise perturbation for natural blend edges

**Plans**: TBD

**UI hint**: yes

---

### Phase 8: File I/O

**Goal**: Users can save and load terrain data in multiple formats

**Depends on**: Phase 7

**Requirements**: FILE-01, FILE-02, FILE-03, FILE-04, FILE-05

**Success Criteria** (what must be TRUE):
1. User can open heightmap PNG via file dialog
2. User can open splatmap PNG via file dialog (or create default if missing)
3. User can export heightmap as PNG
4. User can export splatmap as PNG
5. User can export to .terrain format for Stride runtime

**Plans**: TBD

**UI hint**: yes

---

## Progress

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Project Foundation | 0/4 | Ready for execution | - |
| 2. Brush System Core | 0/4 | Not started | - |
| 3. Height Editing | 0/5 | Not started | - |
| 4. Undo/Redo System | 0/5 | Not started | - |
| 5. Enhanced Brushes | 0/2 | Not started | - |
| 6. Material Management | 0/3 | Not started | - |
| 7. Material Slot Painting | 0/4 | Not started | - |
| 8. File I/O | 0/5 | Not started | - |

---

## Coverage Summary

- **Total v1 Requirements:** 32
- **Total Phases:** 8
- **Coverage:** 32/32 (100%)

### Requirement-to-Phase Mapping

| Requirement | Phase |
|-------------|-------|
| PREV-01 | Phase 1 |
| PREV-02 | Phase 1 |
| PREV-03 | Phase 1 |
| PREV-04 | Phase 1 |
| BRUSH-01 | Phase 2 |
| BRUSH-02 | Phase 2 |
| BRUSH-03 | Phase 2 |
| BRUSH-06 | Phase 2 |
| HEIGHT-01 | Phase 3 |
| HEIGHT-02 | Phase 3 |
| HEIGHT-03 | Phase 3 |
| HEIGHT-04 | Phase 3 |
| PREV-05 | Phase 3 |
| UNDO-01 | Phase 4 |
| UNDO-02 | Phase 4 |
| UNDO-03 | Phase 4 |
| UNDO-04 | Phase 4 |
| UNDO-05 | Phase 4 |
| BRUSH-04 | Phase 5 |
| BRUSH-05 | Phase 5 |
| MGMT-01 | Phase 6 |
| MGMT-02 | Phase 6 |
| MGMT-03 | Phase 6 |
| MAT-01 | Phase 7 |
| MAT-02 | Phase 7 |
| MAT-03 | Phase 7 |
| MAT-04 | Phase 7 |
| FILE-01 | Phase 8 |
| FILE-02 | Phase 8 |
| FILE-03 | Phase 8 |
| FILE-04 | Phase 8 |
| FILE-05 | Phase 8 |

---

*Roadmap created: 2026-03-29*
*Last updated: 2026-03-29 - Phase 1 plans created*
