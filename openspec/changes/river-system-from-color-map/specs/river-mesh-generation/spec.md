## ADDED Requirements

### Requirement: Pixel tracing SHALL extract connected river segments
The system SHALL trace River-category pixels using orthogonal connectivity (4-direction) to extract segments. Each segment SHALL be a contiguous path where every interior pixel has exactly 2 orthogonal neighbors and endpoints have 1 or 0 river neighbors.

#### Scenario: Simple river segment trace
- **WHEN** a straight horizontal line of 10 River pixels exists
- **THEN** the system SHALL extract one RiverSegment with 10 cells

#### Scenario: Confluence trace
- **WHEN** a Source pixel connects to a River path that ends at a Confluence pixel
- **THEN** the system SHALL create one segment from Source to Confluence

### Requirement: Centerline SHALL use Catmull-Rom interpolation
The system SHALL generate a smooth centerline from pixel path coordinates using Catmull-Rom spline interpolation. Control points SHALL be the world-space centers of each River pixel.

#### Scenario: Centerline generation
- **WHEN** a RiverSegment has N pixel coordinates
- **THEN** the system SHALL generate M ≥ N interpolated centerline points at `CurveSampleSpacing` (~2 world units) intervals

#### Scenario: Height sampling
- **WHEN** generating centerline world positions
- **THEN** the Y coordinate SHALL be sampled from the terrain height cache
- **AND** SHALL be offset by `SurfaceOffset` (0.02) to avoid z-fighting

### Requirement: Ribbon mesh SHALL generate left/right vertices
For each centerline point, the system SHALL compute tangent T and perpendicular S, then generate left vertex at `P - S * halfWidth` and right vertex at `P + S * halfWidth`.

#### Scenario: Vertex generation
- **WHEN** a centerline has M points
- **THEN** the system SHALL generate 2M vertices
- **AND** UV coordinates SHALL be (u, 0) for left and (u, 1) for right, where u is the normalized distance along the river

### Requirement: Segment SHALL generate as independent Entity
Each RiverSegment SHALL produce one Entity with its own MeshDraw (VertexBuffer + IndexBuffer). The Entity SHALL be added to the scene under a RiverSystem parent Entity.

#### Scenario: Entity creation
- **WHEN** Generate is triggered and N segments are extracted
- **THEN** the system SHALL create N river segment Entities
- **AND** each Entity SHALL have a unique Material instance

### Requirement: Taper SHALL fade width at segment ends
When `TaperStart = true`, the half-width SHALL smoothly transition from 0 to full width over `ConnectionTaperDistance` (~6 world units). When `TaperEnd = true`, the inverse.

#### Scenario: Source taper
- **WHEN** a segment starts at a Source pixel
- **THEN** `TaperStart` SHALL be true
- **AND** the first ribbon row SHALL have half-width approaching 0

### Requirement: Re-generate SHALL clean previous meshes
When Generate is called again, all previously created river Entities SHALL be removed from the scene and disposed before new ones are created.

#### Scenario: Re-generate
- **WHEN** Generate is clicked a second time
- **THEN** all previous river Entities SHALL be removed
- **AND** new Entities SHALL be created from the current RiverCell[,] data
