# Fix Undo/Redo After Avalonia Migration

## Goal

Restore undo/redo functionality after Avalonia UI migration by adding keyboard shortcuts (Ctrl+Z / Ctrl+Y) and ensuring toolbar buttons work correctly.

## What I already know

* Undo/redo backend is fully implemented: `HistoryManager` (singleton), `ICommand` pattern, `HeightEditCommand`, `PaintEditCommand`, `StrokeChunkTracker`
* ViewModel wiring is complete: `EditorShellViewModel` has `UndoCommand` / `RedoCommand` with `[RelayCommand]`, `CanUndo`/`CanRedo` observables, `OnHistoryChanged` handler
* Toolbar buttons exist in `MainWindow.axaml` (lines 70-75) binding to `UndoCommand` / `RedoCommand`
* **Keyboard shortcuts are completely missing** — no `KeyBinding`, `HotKey`, `KeyGesture`, or `KeyDown` handler anywhere in the codebase
* Design docs (spec, design phase 1, dev log) all specify Ctrl+Z / Ctrl+Y as expected shortcuts

## Assumptions (temporary)

* Toolbar buttons currently work (they are properly bound via MVVM)
* The issue is only keyboard shortcuts — users expect Ctrl+Z/Ctrl+Y to work
* Avalonia `KeyBinding` with `KeyGesture` is the standard approach

## Open Questions

* Should we also add Ctrl+Shift+Z as an alternative redo shortcut? (common in many editors)

## Requirements

* Add Ctrl+Z keyboard shortcut for undo
* Add Ctrl+Y keyboard shortcut for redo
* Optionally add Ctrl+Shift+Z as alternative redo shortcut
* Ensure toolbar undo/redo buttons continue working (regression check)
* Undo/redo buttons should visually reflect CanUndo/CanRedo state (disabled when no history)

## Acceptance Criteria

* [ ] Pressing Ctrl+Z triggers undo when CanUndo is true
* [ ] Pressing Ctrl+Y triggers redo when CanRedo is true
* [ ] Toolbar undo/redo buttons still work correctly
* [ ] Buttons appear disabled when CanUndo/CanRedo is false

## Definition of Done

* Keyboard shortcuts implemented and tested
* No regressions in toolbar button functionality
* Lint / typecheck green

## Out of Scope

* Customizable keyboard shortcuts
* Undo/redo for operations other than height paint and material paint
* Undo/redo history UI panel

## Technical Notes

* Key files:
  * `Terrain.Editor/Views/MainWindow.axaml` — add `KeyBinding` elements
  * `Terrain.Editor/ViewModels/EditorShellViewModel.cs` — UndoCommand/RedoCommand already exist
  * `Terrain.Editor/Services/Commands/HistoryManager.cs` — backend already works
* Avalonia approach: Add `<Window.KeyBindings>` with `<KeyBinding Gesture="Ctrl+Z" Command="{Binding UndoCommand}" />` in MainWindow.axaml
