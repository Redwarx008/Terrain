## ADDED Requirements

### Requirement: Dedicated river render feature
The system SHALL render rivers through a dedicated render feature or equivalent renderer that owns river bottom and river surface pass execution.

#### Scenario: River render feature is registered
- **WHEN** the editor viewport initializes its graphics compositor or render system
- **THEN** the river render feature SHALL be registered so river render objects can be drawn
- **AND** terrain, decal, camera, and viewport input systems SHALL remain functional

### Requirement: Half-resolution bottom pass
The river render feature SHALL render a bottom/refraction pass into a half-resolution render target before rendering the river surface pass.

#### Scenario: Bottom pass executes
- **WHEN** visible river render objects exist
- **THEN** the renderer SHALL bind a half-resolution bottom render target
- **AND** the renderer SHALL draw river bottom geometry before drawing river surface geometry

#### Scenario: Bottom target resizes
- **WHEN** the viewport render size changes
- **THEN** the bottom render target SHALL be recreated or resized to match half of the current viewport dimensions

### Requirement: Dual-source bottom blending
The river bottom pass SHALL use dual-source blending on target backends that are in scope for this project.

#### Scenario: Bottom blend state is configured
- **WHEN** the bottom pass pipeline state is created
- **THEN** the blend state SHALL use the secondary source alpha as the source blend factor
- **AND** it SHALL use inverse secondary source alpha as the destination blend factor

#### Scenario: Bottom shader outputs
- **WHEN** the bottom pixel shader executes
- **THEN** it SHALL output river bottom color and compressed world/refraction data to the primary color output
- **AND** it SHALL output the blend alpha mask to the secondary source output

### Requirement: Full-resolution surface pass
The river render feature SHALL render a full-resolution river surface pass that samples the bottom/refraction render target.

#### Scenario: Surface pass executes
- **WHEN** the bottom pass has completed for visible rivers
- **THEN** the surface pass SHALL bind the bottom/refraction render target as a shader resource
- **AND** it SHALL draw the river surface into the current full-resolution render target

### Requirement: River pass render states
River bottom and surface passes SHALL use render states appropriate for terrain-overlaid translucent water.

#### Scenario: Bottom render state
- **WHEN** the bottom pass draws river geometry
- **THEN** depth writing SHALL be disabled
- **AND** depth bias SHALL be configurable

#### Scenario: Surface render state
- **WHEN** the surface pass draws river geometry
- **THEN** alpha blending SHALL be enabled
- **AND** depth writing SHALL be disabled
- **AND** depth bias SHALL be configurable
