# Terrain — Stride 地形编辑器与运行时

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Stride](https://img.shields.io/badge/Stride-4.3.0.1-blue)](https://github.com/stride3d/stride)

基于 [Stride 引擎](https://stride3d.net/) 的开源地形成套件，包含**编辑器**与**运行时**两部分，支持大地形编辑、笔刷系统、气候/植被/路径编辑、虚拟纹理流式加载等完整管线。

---

## 项目概览

```
Terrain.sln
├── Terrain/                  # 核心运行时库 (类库)
│   ├── Core/                 #   地形组件、LOD、高度数据
│   ├── Rendering/            #   GPU 渲染特性
│   ├── Effects/              #   SDSL 着色器 (材质、构建、路径)
│   ├── Materials/            #   运行时材质管理、Biome 配置
│   ├── Streaming/            #   虚拟纹理流式加载
│   ├── Utilities/            #   工具类
│   └── Assets/               #   游戏资产 (场景、材质、贴图)
│
├── Terrain.Windows/          # Windows 启动器 (WinExe)
│   └── TerrainApp.cs         #   程序入口
│
├── Terrain.Editor/           # 地形编辑器 (WinExe, Avalonia UI)
│   ├── Views/                #   Avalonia 窗口/控件
│   ├── ViewModels/           #   MVVM ViewModel 层
│   ├── Services/             #   编辑服务 (高度/绘制/气候/导出等)
│   ├── Brushes/              #   笔刷系统
│   ├── Effects/              #   编辑器专用着色器
│   ├── Rendering/            #   编辑器渲染管线
│   └── Styles/               #   Avalonia 样式
│
├── TerrainPreProcessor/      # 预处理器 (命令行工具, 开发中)
│
└── Shared/                   # 运行时与编辑器共享代码
    ├── VirtualTextureLayout.cs
    └── TerrainDetailGeneration.cs
```

---

## 技术栈

| 层 | 技术 |
|------|------|
| 引擎 | [Stride](https://stride3d.net/) **4.3.0.1** — [Fork](https://github.com/stride3d/stride) → `E:\WorkSpace\stride` |
| 框架 | .NET **10.0**-windows (C# 14) |
| Editor UI | [Avalonia](https://www.avaloniaui.net/) 11.3.9 + [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) |
| 着色器 | Stride SDSL (类 HLSL) |
| 序列化 | [TOML](https://toml.io/) ([Tommy](https://github.com/dezhidki/Tommy) 3.1.2) |
| 图片处理 | [SixLabors.ImageSharp](https://sixlabors.com/products/imagesharp/) 3.1.12 |
| 数学 | Stride.Core.Mathematics (SIMD Vector/Matrix) |
| 渲染后端 | DirectX 11 (Stride 默认) |

---

## 环境要求

| 依赖 | 版本 | 说明 |
|------|------|------|
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | ≥ 10.0.201 | 项目目标 `net10.0-windows` |
| Stride 引擎 Fork | 4.3.0.1 | 本地编译，见下方构建流程 |
| Visual Studio 2022 | ≥ 17.13 | 可选，用于 IDE 开发 |
| Windows | ≥ 10 | 仅 Windows 平台 |
| GPU | DX11 支持 | 集成显卡可运行，独立显卡性能更佳 |

---

## 构建与部署

### 1. 构建 Stride 引擎 Fork

本项目依赖本地编译的 Stride 引擎 fork，位于 `E:\WorkSpace\stride\`。**任何时候修改了引擎代码**，都需要重新编译引擎包。

```bash
# 在引擎目录执行
cd /e/WorkSpace/stride

# Debug 编译
build\compile.bat debug

# Release 编译
build\compile.bat release
```

编译完成后，NuGet 包输出到 `bin\packages\`，共约 72 个 `Stride.*.4.3.0.1.nupkg`。

### 2. 清空 NuGet 缓存（关键步骤）

由于引擎版本号固定为 `4.3.0.1`，重新编译后必须清空 NuGet 全局缓存，否则 `dotnet restore` 会跳过本地源、使用旧缓存。

> ⚠️ **NuGet 缓存逻辑：**
> `StrideLocal (E:\WorkSpace\stride\bin\packages)`
>  &nbsp;&nbsp;&nbsp;&nbsp;↓
>  检查 `~/.nuget/packages/` → **同版本已存在 → 跳过！**
>  只有缓存中没有才会从本地源复制。

```bash
# 方式一：清空全部 NuGet 缓存（简单但慢，会重新下载所有第三方包）
dotnet nuget locals all --clear

# 方式二（推荐）：仅清除 Stride 包缓存（保留第三方包）
# Windows PowerShell:
Remove-Item -Path "$env:USERPROFILE\.nuget\packages\stride.*" -Recurse -Force

# 或 Git Bash:
rm -rf ~/.nuget/packages/stride.*
```

### 3. 构建地形项目

```bash
cd /e/Stride Projects/Terrain

# 还原（从本地源 + nuget.org 获取包）
dotnet restore

# Debug 编译
dotnet build -c Debug

# Release 编译
dotnet build -c Release
```

### 4. 运行

```bash
# 运行游戏
dotnet run --project Terrain.Windows -c Debug

# 运行编辑器
dotnet run --project Terrain.Editor -c Debug

# 指定 Release
dotnet run --project Terrain.Windows -c Release
```

### 5. 发布独立可执行文件

```bash
# 发布游戏版本（自包含，不需要预装 .NET）
dotnet publish Terrain.Windows\Terrain.Windows.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o ./Publish/Game

# 发布编辑器版本
dotnet publish Terrain.Editor\Terrain.Editor.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o ./Publish/Editor
```

产物输出到 `Publish/` 目录，可直接拷贝到其他 Windows 机器运行。

### 6. 构建产物位置

| 配置 | 游戏 | 编辑器 |
|------|------|--------|
| Debug | `Bin/Windows/Debug/` | `Bin/Editor/Debug/` |
| Release | `Bin/Windows/Release/` | `Bin/Editor/Release/` |

---

## 日常开发工作流

### 仅修改地形项目代码

```bash
dotnet run --project Terrain.Windows -c Debug
```

### 同时修改引擎代码

```bash
# 1. 编译引擎
cd /e/WorkSpace/stride && build\compile.bat debug

# 2. 清 Stride 包缓存
rm -rf ~/.nuget/packages/stride.*

# 3. 回到地形项目，重新还原并运行
cd /e/Stride Projects/Terrain
dotnet restore && dotnet run --project Terrain.Windows -c Debug
```

---

## 功能总览

### 已完成 ✅

- **地形核心**：地形组件、高度数据、四叉树 LOD、流式加载
- **地形渲染**：GPU 实例化、IndexMap 材质混合、虚拟纹理流式
- **编辑器核心**：Avalonia UI、笔刷系统、笔刷投影 (Decal)
- **高度编辑**：多笔刷类型、Undo/Redo (Chunk 事务模型)
- **纹理绘制**：气候蒙版 (ClimateMask) + 规则栈驱动材质索引
- **路径系统**：道路/河流路径渲染、深度偏移、河流线框调试
- **导入/导出**：TOML 作者态资源写回、.terrain v8 导出、baked DetailIndex/DetailWeight VT
- **资产浏览器**：纹理导入（含法线自动生成）、材质槽管理

### 进行中 🚧

- 植被系统编辑

### 规划中 📋

- 植物 LOD / GPU Instancing、Compute Shader 剔除
- 侵蚀模拟、程序化生成
- Hi-Z 遮挡剔除、GPU LOD 迁移

> 完整功能清单见 docs/CURRENT_FEATURES.md

---

## NuGet 配置

nuget.config 定义了两个包源：

```xml
<packageSources>
  <clear />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  <add key="StrideLocal" value="E:\WorkSpace\stride\bin\packages" />
</packageSources>
```

- `nuget.org` — 第三方包 (Avalonia, ImageSharp, Tommy 等)
- `StrideLocal` — 本地 Stride 引擎 fork 编译产物

包版本统一在 Directory.Packages.props 中央管理。

---

## 项目文档

| 文档 | 说明 |
|------|------|
| docs/ARCHITECTURE_OVERVIEW.md | 架构概览与关键决策 |
| docs/CURRENT_FEATURES.md | 功能完成度总览 |
| docs/design/ | 设计文档 (Phase 1–7) |
| docs/log/ | 会话日志 |
| docs/log/decisions/ | 架构决策记录 (ADR) |
| CLAUDE.md | AI 助手开发指南 |
| AGENTS.md | Trellis 代理指令 |

---

## 相关仓库

- [Stride 引擎上游](https://github.com/stride3d/stride) — 本项目的引擎基础
- 本项目的引擎 Fork: `E:\WorkSpace\stride`
- [Godot MTerrain Plugin](https://github.com/PortaMx/Pmx-MTerrain-plugin) — 参考实现

---

## 许可

© 2026 Redwarx008。本项目基于 [Stride 引擎](https://stride3d.net/) 构建，遵循其开源许可协议。
