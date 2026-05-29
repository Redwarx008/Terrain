## ADDED Requirements

### Requirement: river.png path SHALL be saved in TOML project
The TOML project config SHALL store the `RiverMapImagePath` field. The path SHALL be relative to the .toml file directory.

#### Scenario: Save river path
- **WHEN** saving the project with an imported river.png
- **THEN** the .toml file SHALL contain `RiverMapImagePath = "relative/path/river.png"`

### Requirement: River data SHALL be restored on project load
When opening a project that has a `RiverMapImagePath`, the system SHALL load the river.png and create the RiverCell[,] data. The user SHALL still need to click Generate to create the mesh.

#### Scenario: Load project with river path
- **WHEN** a project with `RiverMapImagePath` is opened
- **THEN** the system SHALL load the PNG from the stored path
- **AND** populate RiverCell[,] data
- **AND** display the image path and resolution in the inspector
- **AND** the Generate button SHALL be enabled

### Requirement: River exports SHALL not be required
River mesh data SHALL NOT be included in `.terrain` file exports (river is editor-only visualization).

#### Scenario: Export without river
- **WHEN** exporting terrain
- **THEN** the .terrain file SHALL NOT contain river mesh data
