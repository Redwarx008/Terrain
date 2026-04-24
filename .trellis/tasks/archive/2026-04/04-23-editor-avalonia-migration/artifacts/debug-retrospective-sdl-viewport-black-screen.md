## Bug Analysis: SDL 宿主视口首帧后持续黑屏

### 1. Root Cause Category
- **Category**: B/E - Cross-Layer Contract + Implicit Assumption
- **Specific Cause**: Avalonia `NativeControlHost`、Win32 子窗口、SDL 窗口生命周期和 Stride `GameContextSDL` 之间的宿主契约没有被显式定义。我们一开始默认 `CreateWindowFrom(existing HWND)` 可以稳定把 SDL/Stride 直接绑定到已有宿主窗口，但在当前工程里这会让 `Window.ClientBounds` 退化成 `1x1`，即使 presenter/backbuffer 正常创建并进入过 `Draw()`，最终仍只会看到黑屏。

### 2. Why Fixes Failed
1. **先修 DPI/尺寸换算**：这解决了“视口只覆盖左上角”的表象，但没有触到宿主契约本身，所以黑屏依旧。
2. **再修 backbuffer resize / `ApplyChanges()`**：这让 presenter 尺寸更像对的，但仍然建立在错误宿主路径上，结果只是从“部分区域异常”变成“整块黑屏”。
3. **继续查 scene/compositor / viewport state**：这属于典型的 mental model 偏移。因为 `FirstFrameRendered` 已经触发，我们下意识去怀疑渲染内容链，实际问题还在更下层的窗口宿主。
4. **最后用 presenter-only 洋红清屏对照实验**：这一步才真正把问题分层。先证明 `Present` 是否能到屏幕，再判断是不是 scene/compositor 的锅。

### 3. Prevention Mechanisms
| Priority | Mechanism | Specific Action | Status |
|----------|-----------|-----------------|--------|
| P0 | Documentation | 在 editor spec 中写清楚 SDL 视口必须走 `GameFormSDL + SetParent`，禁止 `CreateWindowFrom(existing HWND)` | DONE |
| P0 | Documentation | 在 thinking guide 中补充“原生窗口/图形宿主边界需要先做 presenter-only 对照实验” | DONE |
| P1 | Runtime Diagnostics | UI 状态栏只显示简洁状态，详细 `BackBuffer/ClientBounds` 走 `Debug.WriteLine` | DONE |
| P1 | Test Coverage | 增加一条手工/自动冒烟清单：启动后验证 `ClientBounds`、presenter、首帧可见、失焦后继续绘制 | TODO |
| P1 | Architecture | 保留最小 presenter-only 诊断开关或测试入口，用于隔离“宿主链 vs 场景链” | DONE |

### 4. Systematic Expansion
- **Similar Issues**:
  - 任何 Avalonia 中承载原生渲染窗口的控件
  - 未来如果重新尝试共享纹理或其他窗口宿主路线，也会遇到同类边界问题
  - 输入转发、焦点、最小化/恢复、DPI resize 这些都属于同一宿主契约族
- **Design Improvement**:
  - 把“SDL 窗口如何创建、如何重挂接、如何同步尺寸”视为单独的宿主层，不和场景/渲染内容层混在一起判断
  - 任何黑屏问题，先验证 presenter-only 输出，再进入 compositor/scene
- **Process Improvement**:
  - 原生宿主问题先做二分：宿主链、presenter 链、scene/compositor 链，不要一开始就全开排查
  - 每次切换宿主实现方式时，都要同步更新 `prd.md` 和 spec，避免文档继续指向旧路线

### 5. Knowledge Capture
- [x] 更新 `.trellis/spec/editor/native-viewport-hosting.md`
- [x] 更新 `.trellis/spec/guides/cross-layer-thinking-guide.md`
- [x] 在任务工件中留下本次 retrospective
- [ ] 后续补一条视口冒烟验证脚本或清单
