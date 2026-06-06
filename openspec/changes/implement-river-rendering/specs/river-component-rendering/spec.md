## ADDED Requirements

### Requirement: River scene component
The system SHALL represent generated river render data through a scene component that can be enabled, disabled, updated, and cleared without relying on a ModelComponent as the primary render path.

#### Scenario: River component receives generated meshes
- **WHEN** river meshes are generated from the editor river workflow
- **THEN** the river scene component SHALL store the generated mesh data
- **AND** the component SHALL increment a version value that downstream rendering code can observe

#### Scenario: River component is cleared
- **WHEN** the river rendering service clears river meshes
- **THEN** the river scene component SHALL remove all stored river mesh data
- **AND** the component SHALL increment its version value

### Requirement: Editor façade compatibility
The river rendering service SHALL preserve its external editor-facing operations for updating meshes, toggling visibility, clearing meshes, and disposing resources while delegating scene state to the river component.

#### Scenario: Generate workflow remains compatible
- **WHEN** the existing river Generate workflow calls the river rendering service to update meshes
- **THEN** the call SHALL update the river component state
- **AND** the Generate workflow SHALL NOT need to create river render entities manually

#### Scenario: Visibility toggle remains compatible
- **WHEN** the editor Show Rivers setting changes
- **THEN** the river rendering service SHALL update the river component visibility state
- **AND** the renderer SHALL skip river drawing when rivers are hidden

### Requirement: River processor synchronization
The system SHALL synchronize river component mesh data into render objects through a processor or equivalent render-system integration.

#### Scenario: Component version changes
- **WHEN** a river component version changes after mesh update or clear
- **THEN** the river processor SHALL rebuild or release corresponding river render objects
- **AND** old GPU buffers SHALL NOT remain referenced by active render objects

#### Scenario: Component disabled
- **WHEN** a river component is disabled
- **THEN** the river processor or render feature SHALL prevent its render objects from being drawn
