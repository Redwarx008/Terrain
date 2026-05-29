## ADDED Requirements

### Requirement: River pixel type enumeration SHALL define 6 pixel types
The system SHALL define `RiverPixelType` enum with values: Land (0), River (1), Source (2), Confluence (3), Bifurcation (4), Ocean (5).

#### Scenario: River pixel from color map
- **WHEN** a pixel matches `#00e5ff` through `#00d400` (or `#00ff00`, `#ff0000`, `#ffc000`) within ±2 RGB tolerance
- **THEN** the system SHALL parse it to the corresponding RiverPixelType
- **AND** River type pixels SHALL carry a Width value from 0 to 12

### Requirement: RiverCell record SHALL store pixel type and width
The system SHALL use `RiverCell(RiverPixelType Type, byte Width)` as the in-memory representation of each river map pixel.

#### Scenario: RiverCell round-trip serialization
- **WHEN** a RiverCell is converted to Rgba32 via ToRgba32() and back via FromRgba32()
- **THEN** the result SHALL equal the original RiverCell

### Requirement: RiverSegment SHALL represent a connected river path
`RiverSegment` SHALL contain: Cells list, StartKind/EndKind segment end semantics, StartNodeKey/EndNodeKey, AvgHalfWidth, Centerline (Catmull-Rom interpolated), WorldLength, TaperStart/TaperEnd flags.

#### Scenario: Segment creation from pixel trace
- **WHEN** a contiguous chain of River pixels is traced
- **THEN** the system SHALL create a RiverSegment with one end at a special pixel (Source/Confluence/Bifurcation)
- **AND** the other end SHALL have no special color

### Requirement: Width palette SHALL map 13 colors to half-widths
The system SHALL define a static palette mapping 13 colors to half-widths from 0.625 (narrowest, `#00e5ff`) to 1.375 (widest, `#00d400`) with step 0.0625.

#### Scenario: Width lookup from color
- **WHEN** a River cell pixel color is `#00d400`
- **THEN** the system SHALL return half-width 1.375 (total width 2.75)

#### Scenario: Width lookup for narrowest river
- **WHEN** a River cell pixel color is `#00e5ff`
- **THEN** the system SHALL return half-width 0.625 (total width 1.25)
