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

### Requirement: Width palette SHALL map 11 actual CK3 colors to half-widths
The system SHALL define a static palette mapping 11 colors (as found in CK3 rivers.png) to half-widths from 0.625 (narrowest) to 1.375 (widest):

| Index | Color | Hex | HalfWidth |
|-------|-------|-----|-----------|
| 0 | narrowest (cyan) | `#00e1ff` | 0.625 |
| 1 | | `#00c8ff` | 0.700 |
| 2 | | `#0096ff` | 0.775 |
| 3 | | `#0064ff` | 0.850 |
| 4 | | `#0000ff` | 0.925 |
| 5 | | `#0000e1` | 1.000 |
| 6 | | `#0000c8` | 1.075 |
| 7 | | `#000096` | 1.150 |
| 8 | | `#000064` | 1.225 |
| 9 | | `#007d00` | 1.300 |
| 10 | widest (green) | `#18ce00` | 1.375 |

Special colors: Source=`#00ff00`, Confluence=`#ff0000`, Bifurcation=`#fffc00`, Ocean=`#ff0080`.

#### Scenario: Width lookup from color—widest
- **WHEN** a River cell pixel color is `#18ce00`
- **THEN** the system SHALL return half-width 1.375 (total width 2.75)

#### Scenario: Width lookup for narrowest river
- **WHEN** a River cell pixel color is `#00e1ff`
- **THEN** the system SHALL return half-width 0.625 (total width 1.25)
