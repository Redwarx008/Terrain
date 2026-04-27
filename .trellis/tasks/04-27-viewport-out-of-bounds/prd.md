# fix viewport out-of-bounds

## Goal

修复 Avalonia 编辑器中 SDL 视口宿主越界覆盖兄弟区域的问题，确保 viewport 只占据自身布局区域，并继续保持 SDL/Stride 使用物理像素尺寸。

## Requirements

* 视口原生宿主窗口不得再覆盖下方 Asset Browser 或其他兄弟 UI。
* SDL/Stride 运行时仍需接收物理像素尺寸，避免高 DPI 下 backbuffer 与可见区域错位。
* 修复应尽量局部，避免改动现有 viewport 宿主生命周期和输入链路。

## Acceptance Criteria

* [ ] `NativeStrideViewportControl` 不再用物理像素直接改写外层原生宿主窗口尺寸。
* [ ] `NativeStrideViewportHost.Attach/Resize` 仍接收物理像素宽高。
* [ ] 项目可成功编译至少 `Terrain.Editor`。

## Out of Scope

* 调整主窗口整体布局样式
* 重写 SDL 视口宿主架构

## Technical Notes

* 相关文件：`Terrain.Editor/Views/Controls/NativeStrideViewportControl.cs`
* 相关规范：`.trellis/spec/editor/native-viewport-hosting.md`、`.trellis/spec/editor/quality-guidelines.md`
* 初步判断：当前 `Bounds * RenderScaling` 的像素尺寸同时用于 SDL 和外层 `HWND`，导致高 DPI 下宿主窗口本身被放大。
