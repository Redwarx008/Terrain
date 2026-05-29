## ADDED Requirements

### Requirement: EditorMode.River SHALL be added
The `EditorMode` enum SHALL include a `River` value, displayed as "River" in the mode toolbar.

#### Scenario: Mode selection
- **WHEN** the user clicks the River mode button
- **THEN** `EditorState.CurrentEditorMode` SHALL be `EditorMode.River`
- **AND** the right inspector panel SHALL show river-specific controls

### Requirement: Import button SHALL open file picker
The inspector SHALL have an "Import PNG" button that opens a file dialog filtered to PNG files (.png).

#### Scenario: Import PNG
- **WHEN** the user clicks "Import PNG"
- **THEN** a system file dialog SHALL open
- **AND** the filter SHALL be set to "PNG files (*.png)"

### Requirement: Generate button SHALL trigger mesh building
The inspector SHALL have a "Generate" button. When clicked, it SHALL execute the full pipeline: import → pixel trace → Catmull-Rom spline → ribbon mesh → Entity creation.

#### Scenario: Generate river mesh
- **WHEN** the user clicks Generate
- **THEN** the system SHALL parse the current RiverCell[,]
- **AND** SHALL create river Entities in the scene
- **AND** SHALL display status: count of segments, vertices, and any errors

### Requirement: Status display SHALL show generation results
After Generate, the inspector SHALL display: number of river segments, total vertices, number of river systems found, and any validation errors.

#### Scenario: Status update
- **WHEN** generation completes successfully
- **THEN** the inspector SHALL show "✓ 3 river systems, 12 segments, 8,420 vertices"
- **WHEN** validation fails
- **THEN** the inspector SHALL show error details

### Requirement: Width scale slider SHALL be configurable
The inspector SHALL provide a "Width Scale" slider (range 0.5–3.0, default 1.0) that globally scales all river widths.

#### Scenario: Width scaling
- **WHEN** Width Scale is set to 2.0
- **THEN** all river segment half-widths SHALL be multiplied by 2.0

### Requirement: Show/Hide toggle SHALL control river visibility
A checkbox "Show Rivers" SHALL toggle the visibility of all river segment Entities.

#### Scenario: Toggle visibility
- **WHEN** "Show Rivers" is unchecked
- **THEN** all river Entities SHALL be hidden (IsEnabled = false)
- **WHEN** checked again
- **THEN** all river Entities SHALL be visible
