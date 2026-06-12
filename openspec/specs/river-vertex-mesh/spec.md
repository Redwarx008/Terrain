## Purpose
River vertex mesh defines the dedicated mesh contract emitted by river mesh generation and consumed by the river rendering pipeline.

## ADDED Requirements

### Requirement: River vertex contract
River meshes SHALL use a dedicated river vertex layout containing position, transparency, UV, tangent, normal, width, and distance-to-main attributes.

#### Scenario: Vertex declaration is created
- **WHEN** river vertex buffers are created
- **THEN** their vertex declaration SHALL expose `POSITION` plus `TEXCOORD0` through `TEXCOORD5`
- **AND** the attributes SHALL map to transparency, UV, tangent, normal, width, and distance-to-main according to the river shader input contract

### Requirement: River mesh output
The river mesh service SHALL output river mesh data containing river vertices, indices, bounds, segment identity, and draw metadata required by the river render feature.

#### Scenario: Ribbon mesh is generated
- **WHEN** a river segment has a valid centerline
- **THEN** the river mesh service SHALL generate river vertices and indices for that segment
- **AND** the generated mesh data SHALL include bounding information for culling or debugging

#### Scenario: Empty centerline is provided
- **WHEN** a river segment has fewer than two centerline points
- **THEN** the river mesh service SHALL return empty river mesh data
- **AND** the renderer SHALL NOT create an active draw for that segment

### Requirement: River vertex attributes
The river mesh service SHALL compute normalized UVs, normalized tangents and normals, world or normalized width values, transparency, and distance-to-main values for every river vertex.

#### Scenario: Vertex attributes are generated
- **WHEN** river vertices are generated for a valid segment
- **THEN** each vertex SHALL have a normalized tangent
- **AND** each vertex SHALL have a normalized normal
- **AND** UV.x SHALL represent normalized distance along the river
- **AND** UV.y SHALL represent cross-section position from one bank to the other

#### Scenario: Connection fade attributes are generated
- **WHEN** a river segment has taper or connection semantics at either endpoint
- **THEN** vertex distance-to-main or transparency values SHALL provide enough data for shader-based endpoint or junction fade
