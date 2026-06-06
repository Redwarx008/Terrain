## ADDED Requirements

### Requirement: River shader streams
River shader files SHALL declare and consume dedicated river vertex streams instead of relying on the generic texture coordinate stream alone.

#### Scenario: River shader consumes vertex attributes
- **WHEN** river bottom or surface shaders compile
- **THEN** they SHALL consume transparency, UV, tangent, normal, width, and distance-to-main inputs from their declared river streams

### Requirement: River bottom shader behavior
The river bottom shader SHALL implement bottom color, depth, parallax, fade, and compressed world/refraction output behavior needed by the river bottom pass.

#### Scenario: Bottom shader samples resources
- **WHEN** the river bottom shader renders a fragment
- **THEN** it SHALL sample bottom diffuse, bottom normal, and bottom properties resources
- **AND** it SHALL use configured depth and fade parameters to compute bottom output

#### Scenario: Bottom shader outputs refraction data
- **WHEN** the river bottom shader writes its primary color output
- **THEN** the alpha channel SHALL contain compressed world or refraction data
- **AND** the ordinary blend alpha SHALL be written through the secondary source output

### Requirement: River surface shader behavior
The river surface shader SHALL implement animated flow normal, water color, ambient normal, reflection, foam, edge fade, distance-to-main fade, and bottom/refraction sampling.

#### Scenario: Surface shader samples resources
- **WHEN** the river surface shader renders a fragment
- **THEN** it SHALL sample the bottom/refraction render target
- **AND** it SHALL sample configured water, flow, foam, and reflection resources

#### Scenario: Surface shader applies fade
- **WHEN** a surface fragment is near a bank or connection fade region
- **THEN** the shader SHALL reduce alpha using edge, transparency, and distance-to-main fade inputs

### Requirement: Neutral fallback inputs
Shader logic that depends on unavailable global systems SHALL use explicit neutral fallback inputs rather than removing the corresponding shader structure.

#### Scenario: Global masks are unavailable
- **WHEN** fog, cloud, shadow, or flat-map inputs are unavailable in the editor renderer
- **THEN** the river shader settings SHALL provide neutral values that preserve visible river rendering
- **AND** the shader parameter names and call boundaries SHALL remain available for future integration

### Requirement: River asset resources
The project SHALL store required river texture resources under neutral project paths and names, with their source and purpose documented separately.

#### Scenario: River resources are copied
- **WHEN** required water, bottom, foam, and reflection textures are added to the project
- **THEN** their project file and directory names SHALL use neutral river rendering names
- **AND** a README SHALL document source paths and intended usage

### Requirement: Shader asset workflow
River shader changes SHALL be compatible with Stride shader key generation and asset compilation.

#### Scenario: Shader files are added or modified
- **WHEN** river shader files are added or modified
- **THEN** the project SHALL include the shader files in the Stride asset folders and project generator metadata
- **AND** generated shader key files SHALL be refreshed before build verification
