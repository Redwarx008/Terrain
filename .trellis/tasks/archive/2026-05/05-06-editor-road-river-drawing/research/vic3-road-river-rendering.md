# Victoria 3 道路与河流渲染技术调研

## 数据来源

- RenderDoc 捕获文件: `C:\Users\Redwa\Desktop\vic3-river&road.rdc`
- Vic3 游戏目录: `E:\Victoria.3.v1.2.7\game`

## 捕获文件概况

- API: D3D11
- 815 draw calls / 842 events
- 整个帧渲染到 4096×4096 深度目标（阴影/深度通道）
- 所有 PS 使用 `discard_nz` 随机丢弃实现透明度
- 无颜色渲染目标（深度通道 only）

---

## 1. 道路渲染系统 — Jomini Spline

### 1.1 核心着色器

**文件**: `game/gfx/FX/road_mulpass.shader`

4 个 Effect 变体（**非多 pass，是可选策略**）：

| Effect | 用途 | Mask 通道 |
|--------|------|----------|
| `Background` | 道路底层 | Mask[0] |
| `Foreground` | 道路前景层 | Mask[1] |
| `StackedTexturesPass` | 随机纹理变体 | 无（`JominiFlatSplineStackedUV`） |
| `SingleTexturePass` | 单纹理（最常见） | 无 |

大多数道路使用 `SingleTexturePass`，1 个 draw call 完成。

### 1.2 Spline 框架

**文件**: `jomini/gfx/FX/jomini/jomini_spline.fxh`

#### 顶点结构

```hlsl
VertexStruct VS_INPUT
{
    float3  Position    : POSITION;
    float   MaxU         : TEXCOORD0;   // 样条参数长度
    float2  UV           : TEXCOORD1;   // x=沿路径, y=横截面
    float3  Tangent      : TEXCOORD2;
    float3  Normal       : TEXCOORD3;
};
```

#### Mask 系统

```hlsl
// 文件: jomini_spline.fxh, 行 81-116
float2 JominiFlatSplineSampleMask(PdxTextureSampler2D MaskTexture, VS_OUTPUT Input)
{
    float2 MaskUV = Input.UV;
    MaskUV.y *= 0.5f;   // 每条通道占 UV.v 的 0.5

    if (MaskUV.x < 0.5f)
    {
        // 起始段：从第2条通道采样
        float2 HeadUV = float2(MaskUV.x, MaskUV.y + 0.5f);
        Mask = PdxTex2DGrad(MaskTexture, HeadUV, dx, dy).rg;
    }
    else
    {
        // 中段：从第1条通道采样
        Mask = PdxTex2DGrad(MaskTexture, MaskUV, dx, dy).rg;
    }

    // 末端过渡到第2条通道
    float DistanceToEnd = Input.MaxU - MaskUV.x;
    if (DistanceToEnd < 0.5f)
    {
        float BlendValue = RemapClamped(MaskUV.x, BlendStart, BlendStop, 0.0f, 1.0f);
        float2 TailUV = float2(1.0f - DistanceToEnd, MaskUV.y + 0.5f);
        float2 EndSectionMask = PdxTex2DGrad(MaskTexture, TailUV, dx, dy).rg;
        Mask = lerp(Mask, EndSectionMask, BlendValue);
    }
    return Mask;
}
```

#### 边缘淡出

```hlsl
// 文件: jomini_spline.fxh, 行 118-123
float JominiFlatSplineEdgeOpacity(float t, float MaxT, float offset)
{
    float DistanceToEnd = min(t, MaxT - t) + 0.0001f;
    return RemapClamped(DistanceToEnd * EndFadeoutFactor, 0, offset, 0, 1);
}
```

#### 纹理堆叠（多纹理变体）

```hlsl
// 文件: jomini_spline.fxh, 行 128-143
void JominiFlatSplineStackedUV(
    VS_OUTPUT Input, int StackedTextureCount,
    out float2 UV, out float2 dx, out float2 dy)
{
    UV = Input.UV;
    uint Variant = uint(CalcRandom(floor(UV.x)) * StackedTextureCount);
    UV.y = (UV.y + float(Variant)) / StackedTextureCount;
    dx = ddx(UV);
    dy = ddy(UV);
}
```

### 1.3 像素着色器核心逻辑

**文件**: `road_mulpass.shader`, 行 80-148

```hlsl
float4 GetPixelColor(VS_OUTPUT Input, float2 UV, float2 ddx, float2 ddy,
                     float EdgeOpacityThresholdInWorldSpace, float MaskValue)
{
    float4 Diffuse;
    float4 Properties;
    float3 Normal;

    float2 MapCoords = Input.WorldSpacePos.xz * WorldSpaceToTerrain0To1;

    Diffuse = PdxTex2DGrad(DiffuseTexture, UV, ddx, ddy);
    Properties = PdxTex2DGrad(MaterialTexture, UV, ddx, ddy);

    // Alpha = MaskValue * 边缘淡出 * FlatMap淡出
    Diffuse.a *= MaskValue;
    Diffuse.a *= JominiFlatSplineEdgeOpacity(
        Input.UV.x / UVScale, Input.MaxU / UVScale,
        EdgeOpacityThresholdInWorldSpace);
    Diffuse.a *= 1.0f - FlatMapLerp;
    clip(Diffuse.a - 0.0001f);

    // 法线
    float3 TerrainNormal = CalculateNormal(Input.WorldSpacePos.xz);
    float3 RoadNormal = JominiFlatSplineSampleNormal(
        NormalTexture, normalize(TerrainNormal),
        normalize(Input.Tangent), UV, ddx, ddy);

    // Devastation 覆盖（战争破坏）
    ApplyDevastationRoads(Diffuse, Input.WorldSpacePos.xz);

    // PBR
    Properties.a = ScaleRoughnessByDistance(Properties.a, Input.WorldSpacePos);
    SMaterialProperties MaterialProps = GetMaterialProperties(
        Diffuse.rgb, Normal, Properties.a, Properties.g, Properties.b);
    SLightingProperties LightingProps = GetSunLightingProperties(
        Input.WorldSpacePos, ShadowTexture);
    Color = CalculateSunLighting(MaterialProps, LightingProps, EnvironmentMap);

    // 后处理：省份叠加、战争迷雾、距离雾
    Color = ApplyFogOfWar(Color, Input.WorldSpacePos);
    Color = GameApplyDistanceFog(Color, Input.WorldSpacePos);

    return float4(Color.rgb, Diffuse.a);
}
```

### 1.4 Properties 纹理通道

```
Properties.r = 未使用（保留）
Properties.g = Metalness
Properties.b = AO / Cavity
Properties.a = Roughness（带距离缩放）
```

### 1.5 渲染状态

```hlsl
// 文件: road_mulpass.shader, 行 239-257
BlendState BlendState
{
    BlendEnable = yes
    SourceBlend = "src_alpha"
    DestBlend = "inv_src_alpha"
    WriteMask = "RED|GREEN|BLUE"    // 只写 RGB，不写 Alpha
}

RasterizerState RasterizerState
{
    DepthBias = -2000              // 防止渲染在地形下方
    SlopeScaleDepthBias = -10
}

DepthStencilState DepthStencilState
{
    DepthWriteEnable = no          // 不写入深度缓冲
}
```

### 1.6 道路纹理资产

**目录**: `game/gfx/map/spline_network/`

| 类型 | Diffuse | Normal | Properties |
|------|---------|--------|------------|
| 土路 | `road_dirt_diffuse.dds` | `road_dirt_normal.dds` | `road_dirt_properties.dds` |
| 铺砌路 | `roadpaved_diffuse.dds` | `roadpaved_normal.dds` | `roadpaved_properties.dds` |
| 砂岩铺砌 | `roadpavedsandstone_diffuse.dds` | — | — |
| 铁路地面 | `road_rail_ground_diffuse.dds` | `road_rail_ground_normal.dds` | `road_rail_ground_properties.dds` |
| 铁轨 | `road_rail_rails_diffuse.dds` | `road_rail_rails_normal.dds` | `road_rail_rails_properties.dds` |

### 1.7 道路配置数据

**文件**: `game/gfx/map/spline_network/game_road_data.txt`

定义了 `state_region_road_strips` 和 `state_region_adjacency_strips` 数据，控制不同省份区域的道路连接条带。

---

## 2. Devastation/Pollution 覆盖系统

### 2.1 概述

**Devastation（战争破坏）** 和 **Pollution（污染）** 是道路/地形上的动态覆盖效果，使用**三次贝塞尔曲线**定义影响区域。

**核心着色器**: PS 3853（捕获文件中），在所有使用 Devastation 的道路变体中共享。

### 2.2 贝塞尔曲线遮罩算法

```hlsl
// 反汇编中的贝塞尔计算（PS 3853, 行 4-25）
// 三次贝塞尔曲线: P(t) = (1-t)³P0 + 3(1-t)²t·BezierPoint1 + 3(1-t)t²·BezierPoint2 + t³P3

// DevastationBezierPoint1/2 是控制点
// 5次牛顿迭代求根，计算像素到曲线的最近距离
// 结果用于 DevastationAreaContrast/Position 做区域遮罩
// 最终通过随机 discard 实现边缘渐变

// 关键常量缓冲区参数:
// DevastationBezierPoint1   — 贝塞尔控制点1 (x,y)
// DevastationBezierPoint2   — 贝塞尔控制点2 (x,y)
// DevastationForceAdd       — 强制添加量
// DevastationAreaContrast   — 区域对比度
// DevastationAreaPosition   — 区域位置
// DevastationNoiseTiling    — 噪声平铺比例
// DevastationTreeAlphaReduce — 树木透明度减少

// Pollution 使用相同架构但独立参数:
// PollutionBezierPoint1/2
// PollutionForceAdd
```

### 2.3 纹理采样

```hlsl
// 捕获 PS 3853 反汇编, 行 15-17
dcl_resource_texture2d DevastationPollution_Texture (t0)
dcl_resource_texture2d DiffuseMap_Texture (t1)

// 采样 DevastationPollution 纹理获取基础遮罩值
sample r0.y, r1.xyxx, DevastationPollution_Texture.yxzw
// 叠加 DevastationForceAdd
add r0.y, r0.y, DevastationForceAdd.x
```

### 2.4 使用此系统的着色器

| 着色器 ID | 类型 | 使用次数 |
|-----------|------|---------|
| PS 3110 | PS | 70 |
| PS 3111 | PS | 1 |
| PS 3116 | PS | 17 |
| PS 3120 | PS | 78 |
| PS 3125 | PS | 78 |
| PS 3410 | PS | 2 |
| PS 3587 | PS | 3 |
| PS 3637 | PS | 8 |
| PS 3853 | PS | 8 |
| PS 4977 | PS | 4 |

---

## 3. 河流渲染系统

### 3.1 架构概述

河流使用**双 Pass 渲染**：
1. **河流底部** (`river_bottom.shader`) — 视差映射模拟水下深度
2. **河流表面** (`river_surface.shader`) — 水面效果 + 流动动画

### 3.2 河流核心框架

**文件**: `jomini/gfx/FX/jomini/jomini_river.fxh`

#### 顶点结构

```hlsl
// 文件: jomini_river.fxh, 行 23-32
VertexStruct VS_INPUT_RIVER
{
    float3  Position       : POSITION;
    float   Transparency   : TEXCOORD0;   // 透明度
    float2  UV             : TEXCOORD1;    // x=沿河流, y=横截面(0~1)
    float3  Tangent         : TEXCOORD2;
    float3  Normal          : TEXCOORD3;
    float   Width           : TEXCOORD4;    // 河流宽度
    float   DistanceToMain : TEXCOORD5;    // 到主河道距离
};
```

#### 常量缓冲区

```hlsl
// 文件: jomini_river.fxh, 行 4-21
ConstantBuffer(JominiRiver)
{
    float _TextureUvScale;      // 0.015625
    float _FlowNormalUvScale;   // 1.2
    float _FlowNormalSpeed;     // 0.185
    float _RiverFoamFactor;     // 泡沫强度
    float _NoiseScale;          // 0.25
    float _NoiseSpeed;          // 2.0
    float _FlattenMult;         // 1.0
    float _OceanFadeRate;       // 3.0
    float _BankAmount;          // 0.1
    float _BankFade;            // 0.0
    float _Depth;               // 0.15
    float _DepthWidthPower;     // 1.0
    float _DepthFakeFactor;     // 0.4
    int   _ParallaxIterations;  // 20
};
```

### 3.3 河流表面着色器

**文件**: `jomini/gfx/FX/jomini/jomini_river_surface.fxh`

#### 核心渲染逻辑

```hlsl
// 文件: jomini_river_surface.fxh, 行 53-98
SWaterOutput CalcRiverAdvanced(in VS_OUTPUT_RIVER Input)
{
    float Depth = CalcDepth(Input.UV);

    SWaterParameters Params;
    Params._ScreenSpacePos = Input.Position;
    Params._WorldSpacePos = Input.WorldSpacePos;
    Params._WorldUV = Input.WorldSpacePos.xz / MapSize;
    Params._Depth = Depth * Input.Width + 0.1f;

    // 流动法线动画
    float2 FlowNormalUV = Input.UV.yx * float2(1.0f, -1.0f);
    FlowNormalUV *= float2(Input.Width, 1.0f) * _FlowNormalUvScale;
    FlowNormalUV.y += JOMINIRIVER_GlobalTime * _FlowNormalSpeed;

    float4 FlowNormalSample = PdxTex2D(FlowNormalTexture, FlowNormalUV);
    // 双层混合增加丰富度
    FlowNormalUV.y += JOMINIRIVER_GlobalTime * _FlowNormalSpeed * 1.33f;
    FlowNormalSample += PdxTex2D(FlowNormalTexture, FlowNormalUV * 0.713f);
    FlowNormalSample *= 0.5f;

    float3 FlowNormal = UnpackNormal(FlowNormalSample).xzy;
    FlowNormal.y *= _WaterFlowNormalFlatten * _FlattenMult
                   * saturate(dot(Input.Normal, float3(0,1,0)));
    Params._FlowNormal = normalize(FlowNormal);
    Params._FlowFoamMask = FlowNormalSample.a * _RiverFoamFactor;

    SWaterOutput Out = CalcWater(Params);

    // 边缘淡出
    float EdgeFade1 = smoothstep(0.0f, _BankFade, Input.UV.y);
    float EdgeFade2 = smoothstep(0.0f, _BankFade, 1.0f - Input.UV.y);
    Out._Color.a *= EdgeFade1 * EdgeFade2;

    return Out;
}
```

#### 深度计算（余弦曲线）

```hlsl
// 文件: jomini_river.fxh, 行 83-86
float CalcDepth(float2 UV)
{
    return _Depth * (1.0f - pow(cos(UV.y * 2.0f * PI) * 0.5f + 0.5f, 2.0f));
}
// UV.y=0 和 UV.y=1 时深度=0（边缘）
// UV.y=0.5 时深度最大（中心）
```

#### 渲染状态

```hlsl
// 文件: jomini_river_surface.fxh, 行 102-118
BlendState BlendState
{
    BlendEnable = yes
    SourceBlend = "src_alpha"
    DestBlend = "inv_src_alpha"
    WriteMask = "RED|GREEN|BLUE"
}

RasterizerState RasterizerState
{
    DepthBias = -50000     // 比道路的 -2000 更大
}

DepthStencilState DepthStencilState
{
    DepthWriteEnable = no
}
```

### 3.4 河流底部着色器

**文件**: `jomini/gfx/FX/jomini/jomini_river_bottom.fxh`

#### Steep Parallax Mapping

```hlsl
// 文件: jomini_river_bottom.fxh, 行 149-186
void CalculateParallaxOffsetSteep(
    float3 TangentSpaceToCameraDir, float3 WorldSpaceToCameraDir,
    float2 UV, out float2 TangentSpaceOffset, out float2 WorldSpaceOffset,
    PdxTextureSampler2D BottomNormal)
{
    int MinNumLayers = 2;
    int MaxNumLayers = _ParallaxIterations;  // 20

    float NumLayers = lerp(float(MaxNumLayers), float(MinNumLayers),
                           WorldSpaceToCameraDir.y);
    float LayerDepth = _Depth / NumLayers;
    float CurrentDepth = 0.0f;

    // 迭代视差映射
    for (int i = 0; i < MaxNumLayers; i++)
    {
        if (Depth > CurrentDepth)
        {
            CurrentDepth += LayerDepth;
            Offset += Step;
            float NewDepth = CalcDepth(UV + Offset.xy, BottomNormal);
            Depth = NewDepth;
        }
    }
    // 二分法精确定位
    float Weight = NextDepth / (NextDepth - PrevDepth);
    Offset -= Step * Weight;
}
```

#### 河流底部着色

```hlsl
// 文件: jomini_river_bottom.fxh, 行 282-345
PS_RIVER_BOTTOM_OUT CalcRiverBottomAdvanced(in VS_OUTPUT_RIVER Input)
{
    // 视差偏移UV
    CalcParallaxedUvs(Input, TBN, WorldUV, TangentUV, BottomNormal);

    // 深度假因子增强深度感
    float UnderOceanFade = 1.0f - saturate((_WaterHeight - Input.WorldSpacePos.y)
                                             * _OceanFadeRate);
    float FadeOut = min(UnderOceanFade, Input.Transparency);

    // 采样底部纹理（使用视差偏移后的UV）
    float4 Diffuse = PdxTex2DUpscale(BottomDiffuse, TangentUV);
    float4 Properties = PdxTex2DUpscale(BottomProperties, TangentUV);
    float3 NormalSample = UnpackRRxGNormal(PdxTex2DUpscale(BottomNormal, TangentUV));

    // 河岸坡度法线
    float DepthDelta = (CalcDepth(TangentUV - Offset) - CalcDepth(TangentUV + Offset))
                       * UnderOceanFade;
    float Angle = atan(DepthDelta / SampleWidth);
    float3 ParallaxNormal = float3(0, -sin(Angle), cos(Angle));
    Normal = normalize(mul(ParallaxNormal, TBN));

    // 光照（排除水面阴影）
    SLightingProperties LightingProps = GetRiverBottomSunLightingProperties(
        WorldSpacePos, WorldSpaceDepth, ShadowTexture);

    // 边缘淡出
    float EdgeFade1 = smoothstep(0.0f, _BankFade, Input.UV.y);
    float EdgeFade2 = smoothstep(0.0f, _BankFade, 1.0f - Input.UV.y);
    float Alpha = Diffuse.a * FadeOut * FadeToConnection * EdgeFade1 * EdgeFade2;

    // 双输出: 颜色(含压缩世界坐标) + Alpha
    Out.Color.a = CompressWorldSpace(WorldSpacePos);
    Out.Blend = vec4(Alpha);
}
```

#### 双输出混合（Dual-Source Blending）

```hlsl
// 文件: jomini_river_bottom.fxh, 行 349-364
BlendState BlendState
{
    BlendEnable = yes
    SourceBlend = "src1_alpha"       // 使用第2个输出 (Out.Blend) 作为 alpha
    DestBlend = "inv_src1_alpha"
    SourceAlpha = "src1_alpha"
    DestAlpha = "inv_src1_alpha"
    WriteMask = "RED|GREEN|BLUE|ALPHA"
}

RasterizerState RasterizerState
{
    DepthBias = -50000
}
```

### 3.5 河流纹理资产

**目录**: `game/gfx/map/rivers/`

| 用途 | 文件 |
|------|------|
| 底部漫反射 | `river_bottom_diffuse.dds` |
| 底部法线 | `river_bottom_normal.dds` |
| 底部属性 | `river_bottom_properties.dds` |

**配置文件**: `game/gfx/map/rivers/rivers.settings`
```
TextureUvScale = 0.015625
FlowNormalUvScale = 1.2
FlowNormalSpeed = 0.185
Depth = 0.15
DepthWidthPower = 1.0
DepthFakeFactor = 0.4
ParallaxIterations = 20
BankFade = 0.000
```

**水面配置文件**: `game/gfx/map/rivers/riverwater.settings`
```
WaterColorShallow = hsv{ 0.55 0.5 0.1 }
WaterColorDeep = hsv{ 0.62 0.9 0.1 }
FlowNormalTexture = gfx/map/water/flow_normal_temporary.dds
```

### 3.6 河流数据

**文件**: `game/map_data/rivers.png`
**定义文件**: `jomini/common/defines/jomini/rivers.txt`

---

## 4. 捕获文件中的深度通道着色器

### 4.1 地形高度位移 VS

**ResourceId::3182 / 3203** — 两个变体，核心逻辑相同：

```hlsl
// 高度查找两步法:
// 1. WorldSpaceToLookup 将世界坐标映射到查找纹理
// 2. HeightLookupTexture 提供间接寻址 (RG=偏移, BA=比例)
// 3. PackedHeightTexture 提供实际高度值

mul r1.xy, r0.xzxx, WorldSpaceToLookup.xyxx   // 世界坐标→查找UV
sample r2, r1.zwzz, HeightLookupTexture        // 间接寻址
mad r1.xy, r1.xyxx, cb2[r1.w].xyxx, cb2[r1.w].zwzz  // 偏移+缩放
sample r1.x, r1.xyxx, PackedHeightTexture      // 高度采样
mad r0.y, r1.x, HeightScale.x, r0.y           // 应用于Y轴位移
```

### 4.2 透明度 PS（随机丢弃）

**ResourceId::3210 / 3241** — 基于屏幕位置的伪随机丢弃：

```hlsl
// 哈希函数: sin(dot(screenPos, 12.9898, 78.233)) * 43758.5453
dp2 r0.x, v0.xyxx, l(12.989800, 78.233002)
sincos r0.x, null, r0.x
mul r0.x, r0.x, l(43758.546875)
frc r0.x, r0.x

// 从常量缓冲区读取阈值，进行方向性丢弃
add r0.y, v1.z, l(0.5)          // 实例索引 + 4 → 偏移
iadd r0.y, r0.y, l(4)
lt r0.z, l(0), cb0[r0.y + 0].x  // 阈值正负判断
lt r0.w, cb0[r0.y + 0].x, l(0)
mad r0.x, -r0.x, r0.z, cb0[r0.y + 0].x
lt r0.x, r0.x, l(0)
discard_nz r0.x                   // 丢弃像素
```

---

## 5. 关键结论

### 5.1 道路渲染核心要素（按优先级排序）

1. **纹理贴图** — Diffuse + Normal + Properties 三通道，是视觉质量的基础
2. **UV 映射** — U=横截面 0→1，V=沿路径方向按世界距离平铺
3. **边缘淡出** — `smoothstep` 或 `RemapClamped` 在 UV.y 边缘淡出 Alpha
4. **DepthBias** — 负深度偏移解决 z-fighting，不需要修改地形高度
5. **Mask 系统** — 端点过渡遮罩（可选，用于更自然的起始/结束）

### 5.2 河流渲染核心要素

1. **双 Pass** — 底部视差映射 + 表面流动动画
2. **余弦深度曲线** — `1 - pow(cos(UV.y*2π)*0.5+0.5, 2)` 中心深边缘浅
3. **流动法线** — 时间偏移 UV 双层采样混合
4. **边缘淡出** — `smoothstep` 河岸过渡
5. **极大 DepthBias** — -50000 确保河流在所有地形之上

### 5.3 非必要要素

- **贝塞尔曲线遮罩** — 仅用于 Devastation/Pollution 游戏机制覆盖，非渲染基础
- **多 Pass 渲染** — 大多数道路只用 1 个 draw call（SingleTexturePass）
- **地形高度修改** — Vic3 道路不修改地形高度，纯靠 DepthBias