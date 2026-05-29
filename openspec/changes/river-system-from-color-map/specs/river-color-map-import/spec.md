## ADDED Requirements

### Requirement: Color map import SHALL load PNG as RiverCell[,]
The system SHALL load a river.png (RiverPixelType format) and convert it to `RiverCell[,]` with dimensions matching the PNG resolution.

#### Scenario: Import valid river PNG
- **WHEN** a valid river.png is imported
- **THEN** the system SHALL create a RiverCell[,] array
- **AND** array dimensions SHALL equal PNG (width, height)

### Requirement: Color matching SHALL use ±2 RGB tolerance
Each color SHALL be matched with per-channel tolerance of ±2, consistent with the existing `RiverPixelType.IsMatch` pattern.

#### Scenario: Near-match color
- **WHEN** a pixel has RGB=(1, 253, 1)
- **THEN** it SHALL match `#00ff00` (Source) within tolerance

### Requirement: Source pixel validation SHALL enforce single source per system
Each connected river system SHALL have exactly one Source pixel. Violations SHALL be reported and SHALL prevent mesh generation.

#### Scenario: Multiple sources
- **WHEN** a river system has two green Source pixels
- **THEN** the system SHALL report an error "Multiple source pixels detected"
- **AND** SHALL NOT generate the river mesh

### Requirement: Orthogonal adjacency SHALL be validated
All River/Confluence/Bifurcation pixels SHALL have ≤ 2 orthogonal neighbors. Diagonal adjacency or 3+ orthogonal neighbors SHALL be reported as invalid.

#### Scenario: Invalid diagonal connection
- **WHEN** a river has pixels connected only diagonally
- **THEN** the system SHALL report the invalid pixels
- **AND** SHALL NOT generate the river mesh

### Requirement: Confluence/Bifurcation SHALL be orthogonally adjacent to main river
Red and yellow pixels SHALL be orthogonally adjacent (not diagonal) to the main river and SHALL NOT split the main river into multiple segments.

#### Scenario: Red pixel adjacency
- **WHEN** a red Confluence pixel is orthogonally adjacent to a main river pixel
- **THEN** the system SHALL treat it as a valid confluence point
- **AND** connect the tributary segment to the main river at that point

### Requirement: PNG preview SHALL display in inspector
After import, the river.png SHALL be shown as a thumbnail preview in the inspector panel.

#### Scenario: Preview after import
- **WHEN** a river.png is imported
- **THEN** the inspector SHALL display a scaled-down preview of the image
