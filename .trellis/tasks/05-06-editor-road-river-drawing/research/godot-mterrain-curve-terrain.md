# Godot MTerrain 道路/河流地形整形参考

参考文件：

- `E:\reference\Godot-MTerrain-plugin\gdextension\src\path\mcurve_terrain.cpp`
- `E:\reference\Godot-MTerrain-plugin\gdextension\src\path\mcurve_terrain.h`
- `E:\reference\Godot-MTerrain-plugin\gizmos\mpath_gizmo.gd`
- `E:\reference\Godot-MTerrain-plugin\inspector\gui\curve_terrain.gd`

## 结论

它的地形整形不是先生成一整块多边形再统一压平，而是：

1. 沿每条 curve connection 按固定步长高密度采样
2. 对每个采样点，沿局部横向方向再扫一遍半径带
3. 把带内像素高度朝“曲线采样点目标高度”插值推进
4. 中心区影响最大，外圈 falloff 渐弱

这本质上是“沿曲线移动的刷子”。

## 关键参数

`MCurveTerrain` 暴露的整形参数：

- `deform_radius`：中心压平半径
- `deform_falloff`：外圈过渡宽度
- `deform_offest`：沿局部 y 方向的偏移
- `apply_scale`：是否把 radius/falloff 跟随曲线局部缩放
- `apply_tilt`：是否让曲线变换带倾斜

UI 在 `curve_terrain.gd` 里把这些参数暴露给 inspector。

## deform_on_conns 的做法

`deform_on_conns()` 的核心流程：

1. 激活指定 terrain layer
2. 采样步长固定为 `grid->get_h_scale() * 0.4`
3. 对每条 connection：
   - 用 `curve->get_conn_lenght(conn_id) / interval_meter` 算采样数
   - 构造 `0..1` 的 ratio 列表
   - 调 `curve->get_conn_transforms(conn_id, ratios, transforms, apply_tilt, apply_scale)` 取每个采样点的局部变换
4. 对每个采样点：
   - `origin` 作为曲线中心
   - `z_dir = basis.column(2)` 作为横向扫带方向
   - `y_dir = basis.column(1)` 作为竖向偏移方向
   - 如果启用 `apply_scale`，用 `z_dir.length()` 缩放 `deform_radius` 和 `deform_falloff`
   - 从 `-(radius + falloff)` 扫到 `+(radius + falloff)`
   - 每步也按 `interval_meter` 前进
5. 对扫到的每个像素：
   - 目标位置：`ppp = origin + z_dir * side_dis + y_dir * deform_offest`
   - 取该像素当前高度 `h`
   - 用 `smoothstep(total_dis, radius, abs(side_dis))` 算影响权重 `t`
   - 用 `h = (ppp.y - h) * t + h` 把当前高度往目标高度 `ppp.y` 推

也就是说：

- 中心 `radius` 内基本会被强力压到曲线目标高度
- `falloff` 区域会做平滑过渡
- 它不是一次性设成绝对高度，而是按权重插值到目标高度

## 它如何“清平”

它有两种清除方式：

### 1. `clear_deform(const PackedInt64Array& conn_ids)`

也是沿曲线高密度采样，再横向扫一条带，但不做插值，而是把当前 layer 对高度的贡献减掉：

- `base = grid->get_height_by_pixel(i,j) - grid->get_height_by_pixel_in_layer(i,j)`
- `grid->set_height_by_pixel(i,j, base)`

这说明它的道路/河流整形是写在单独 terrain layer 里的，清除时能恢复到底层原始地形。

### 2. `clear_deform_aabb(AABB aabb)`

直接清整块包围盒区域，同样也是恢复为“总高度 - 当前 layer 高度贡献”。

它会把 AABB 再扩张：

- `deform_radius + deform_falloff + grid->get_h_scale()`

确保边缘过渡也被覆盖。

## 编辑时为什么不会越刷越脏

在 `mpath_gizmo.gd` 里，移动控制点并开启自动地形整形时，流程是：

1. `curve_terrain.clear_deform_aabb(init_aabb)`
2. `curve_terrain.deform_on_conns(conns)`

也就是先清旧区域，再按新曲线重刷。

这点很关键。它不是在旧结果上无脑叠加，所以拖动道路时不会持续累积误差。

## 对转弯的意义

它的弯道平滑，核心不在“横截面有多少个顶点”，而在于：

1. 曲线本身是连续采样的
2. 采样步长很密：`grid scale * 0.4`
3. 每个采样点都会横向扫带

因此弯道区域天然会出现更多有效采样点，地形压平会跟着弯道细密变化，而不是只在少量折线段上做距离投影。

## 和当前 Terrain.Editor 的直接差异

当前 `PathFeatureService.ApplyFeatureTerrain(...)` 更接近：

- 遍历曲线离散 segment
- 对每个 segment 的包围盒像素，算点到线段距离
- 根据距离对目标高度做一次投影式修改

而 Godot 参考实现更接近：

- 先把路径转成高密度曲线采样点
- 每个采样点像一个沿路径移动的笔刷
- 中心压平，边缘 falloff
- 编辑时先清旧区域，再重建新结果

## 借鉴建议

如果把这个思路迁到当前项目，优先级可以是：

1. 地形整形也改成“沿曲线采样点刷带”，不要只对长 segment 做距离投影
2. 采样密度和弯道细分联动，转弯越急，局部采样越密
3. 增加独立的 `terrain falloff` / `terrain offset`
4. 路径编辑重算时，先恢复旧影响区，再按新路径重刷，避免累计误差

