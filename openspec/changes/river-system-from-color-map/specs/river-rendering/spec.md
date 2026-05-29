## ADDED Requirements

### Requirement: River surface SHALL use alpha-blended rendering
The river surface SHALL use BlendState with SourceBlend=src_alpha, DestBlend=inv_src_alpha, DepthWrite=false, DepthBias=-50000.

#### Scenario: Surface rendering
- **WHEN** a river segment Entity is visible
- **THEN** it SHALL render with alpha blend over the terrain
- **AND** SHALL NOT write to depth buffer

### Requirement: Vertex format SHALL include Position, Normal, UV
Each vertex SHALL contain at minimum: Vector3 Position, Vector3 Normal, Vector2 TextureCoordinate. UV.x = distance along river (0→1), UV.y = cross-section (0=left, 1=right).

#### Scenario: Vertex buffer creation
- **WHEN** a river mesh is generated
- **THEN** the VertexBuffer SHALL use `VertexPositionNormalTexture` format

### Requirement: River bottom SHALL use parallax offset UV
The bottom Pass SHALL sample bottom diffuse/normal textures using parallax-offset UV coordinates based on view angle. (Simplified: fixed 2-5 layers, no binary search refinement.)

#### Scenario: Bottom rendering
- **WHEN** the camera views a river segment from a shallow angle
- **THEN** the bottom texture UV SHALL be offset to simulate depth parallax

### Requirement: Flow animation SHALL use time-offset UV
The surface shader SHALL scroll flow normal UVs over time using `_FlowNormalSpeed` and sample with bilinear interpolation for animated water appearance.

#### Scenario: Animated flow
- **WHEN** the river surface renders over consecutive frames
- **THEN** the flow normal UV SHALL shift over time
- **AND** the resulting surface color SHALL show movement

### Requirement: Edge fade SHALL use smoothstep
The surface alpha SHALL be attenuated at river edges using `smoothstep(0, _BankFade, UV.y) * smoothstep(0, _BankFade, 1 - UV.y)`.

#### Scenario: Edge transparency
- **WHEN** a vertex has UV.y near 0 or 1
- **THEN** its alpha SHALL be near 0 (fully transparent at edges)
- **AND** vertices near UV.y=0.5 SHALL have full alpha

### Requirement: Water color SHALL interpolate between shallow/deep
The surface color SHALL lerp between `WaterColorShallow` and `WaterColorDeep` based on normalized depth (0=shallow, 1=deep).

#### Scenario: Color interpolation
- **WHEN** depth is 0 (river edge)
- **THEN** the surface color SHALL be close to `WaterColorShallow`
- **WHEN** depth is max (river center)
- **THEN** the surface color SHALL blend toward `WaterColorDeep`
