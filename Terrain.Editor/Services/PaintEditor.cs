#nullable enable

using System;
using Stride.Core.Mathematics;
using Terrain.Editor.Rendering;
using Terrain.Editor.Services;
using Terrain.Editor.Services.Commands;

namespace Terrain.Editor.Services;

/// <summary>
/// 材质绘制编辑器，管理绘制操作的生命周期。
/// 三阶段生命周期：BeginStroke / ApplyStroke / EndStroke。
/// 参考 HeightEditor 的设计模式。
/// </summary>
public sealed class PaintEditor
{
    private static readonly Lazy<PaintEditor> _instance = new(() => new());
    public static PaintEditor Instance => _instance.Value;

    private IPaintTool? currentTool;
    private bool isStrokeActive;
    private int strokeSeed; // 笔触内的随机种子

    /// <summary>
    /// 开始新的绘制笔触。
    /// </summary>
    /// <param name="toolName">工具名称 ("Paint", "Erase")。</param>
    /// <param name="terrainManager">地形管理器，用于撤销/重做命令。</param>
    public void BeginStroke(string toolName, TerrainManager terrainManager)
    {
        isStrokeActive = true;

        // 生成笔触内的随机种子
        strokeSeed = Random.Shared.Next();

        currentTool = toolName switch
        {
            "Paint" => new PaintMaterialTool(),
            "Erase" => new EraseTool(),
            _ => throw new ArgumentException($"Unknown paint tool: {toolName}", nameof(toolName))
        };

        // Create and begin command for undo/redo
        var command = new PaintEditCommand(terrainManager, toolName);
        HistoryManager.Instance.BeginCommand(command);
    }

    /// <summary>
    /// 在指定位置应用当前笔触。
    /// </summary>
    /// <param name="worldPosition">世界坐标位置。</param>
    /// <param name="indexMap">材质索引图。</param>
    /// <param name="mapWidth">索引图宽度。</param>
    /// <param name="mapHeight">索引图高度。</param>
    /// <param name="terrainManager">地形管理器，用于标记数据脏。</param>
    public void ApplyStroke(Vector3 worldPosition, MaterialIndexMap indexMap, int mapWidth, int mapHeight, TerrainManager terrainManager)
    {
        if (!isStrokeActive || currentTool == null)
            return;

        // 转换世界坐标到 heightmap 像素坐标
        int pixelX = (int)MathF.Round(worldPosition.X);
        int pixelZ = (int)MathF.Round(worldPosition.Z);

        // 缩放到 splatmap 坐标空间（splatmap 是 heightmap 的 1/2）
        int splatPixelX = pixelX / 2;
        int splatPixelZ = pixelZ / 2;

        // 获取笔刷参数
        var brushParams = BrushParameters.Instance;
        float brushRadius = brushParams.Size * 0.5f;
        float brushInnerRadius = brushRadius * brushParams.EffectiveFalloff;

        // 缩放笔刷半径到 splatmap 空间
        float splatBrushRadius = MathF.Ceiling(brushRadius) / 2.0f;
        float splatBrushInnerRadius = MathF.Ceiling(brushInnerRadius) / 2.0f;

        // 获取目标材质索引
        byte targetIndex = ResolveTargetMaterialIndex();
        if (targetIndex == byte.MaxValue)
            return;

        // 构建编辑上下文
        var context = new PaintEditContext
        {
            IndexMap = indexMap,
            DataWidth = mapWidth,
            DataHeight = mapHeight,
            CenterX = splatPixelX,
            CenterZ = splatPixelZ,
            BrushRadius = splatBrushRadius,
            BrushInnerRadius = splatBrushInnerRadius,
            Strength = brushParams.Strength,
            TargetMaterialIndex = targetIndex,

            // 新增参数
            Weight = brushParams.Weight,
            RandomRotation = brushParams.RandomRotation,
            FixedRotationDegrees = brushParams.FixedRotationDegrees,
            Use3DProjection = brushParams.Use3DProjection,
            RandomSeed = strokeSeed,
            HeightData = terrainManager.HeightDataCache,
            HeightDataWidth = terrainManager.HeightCacheWidth,
            HeightDataHeight = terrainManager.HeightCacheHeight,

            // 坡度过滤参数
            UseSlopeFilter = brushParams.UseSlopeFilter,
            MinSlopeDegrees = brushParams.MinSlopeDegrees,
            MaxSlopeDegrees = brushParams.MaxSlopeDegrees,
            HeightScale = terrainManager.HeightScale
        };

        // Mark chunks before mutation so each chunk can cache its true "before" state.
        HistoryManager.Instance.MarkCommandChunks(splatPixelX, splatPixelZ, splatBrushRadius);

        // 应用工具
        currentTool.Apply(ref context);

        // 标记脏区域（使用 heightmap 坐标，因为 MarkDataDirty 按 heightmap 切片工作）
        terrainManager.MarkDataDirty(TerrainDataChannel.MaterialIndex, pixelX, pixelZ, brushRadius);
    }

    /// <summary>
    /// 结束当前笔触。
    /// </summary>
    public void EndStroke()
    {
        isStrokeActive = false;
        currentTool = null;

        // Commit command for undo/redo
        HistoryManager.Instance.CommitCommand();
    }

    /// <summary>
    /// 计算线性 falloff。
    /// </summary>
    public static float ComputeLinearFalloff(float distance, float outerRadius, float innerRadius)
    {
        if (distance <= innerRadius)
            return 1.0f;

        if (distance >= outerRadius)
            return 0.0f;

        return 1.0f - (distance - innerRadius) / (outerRadius - innerRadius);
    }

    private static byte ResolveTargetMaterialIndex()
    {
        var manager = MaterialSlotManager.Instance;
        int selectedIndex = manager.SelectedSlotIndex;
        if (selectedIndex >= 0 && selectedIndex < 256 && !manager[selectedIndex].IsEmpty)
            return (byte)selectedIndex;

        foreach (var slot in manager.GetActiveSlots())
        {
            manager.SelectedSlotIndex = slot.Index;
            return (byte)slot.Index;
        }

        return byte.MaxValue;
    }
}
