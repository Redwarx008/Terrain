#nullable enable

using System;
using Stride.Core.Mathematics;

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

    /// <summary>
    /// 开始新的绘制笔触。
    /// </summary>
    /// <param name="toolName">工具名称 ("Paint", "Erase")。</param>
    public void BeginStroke(string toolName)
    {
        isStrokeActive = true;

        currentTool = toolName switch
        {
            "Paint" => new PaintMaterialTool(),
            "Erase" => new EraseTool(),
            _ => throw new ArgumentException($"Unknown paint tool: {toolName}", nameof(toolName))
        };
    }

    /// <summary>
    /// 在指定位置应用当前笔触。
    /// </summary>
    /// <param name="worldPosition">世界坐标位置。</param>
    /// <param name="indexMap">材质索引图。</param>
    /// <param name="mapWidth">索引图宽度。</param>
    /// <param name="mapHeight">索引图高度。</param>
    public void ApplyStroke(Vector3 worldPosition, MaterialIndexMap indexMap, int mapWidth, int mapHeight)
    {
        if (!isStrokeActive || currentTool == null)
            return;

        // 转换世界坐标到像素坐标
        int pixelX = (int)MathF.Round(worldPosition.X);
        int pixelZ = (int)MathF.Round(worldPosition.Z);

        // 获取笔刷参数
        var brushParams = BrushParameters.Instance;
        float brushRadius = brushParams.Size * 0.5f;
        float brushInnerRadius = brushRadius * brushParams.EffectiveFalloff;

        // 获取目标材质索引
        byte targetIndex = (byte)MaterialSlotManager.Instance.SelectedSlotIndex;

        // 构建编辑上下文
        var context = new PaintEditContext
        {
            IndexMap = indexMap,
            DataWidth = mapWidth,
            DataHeight = mapHeight,
            CenterX = pixelX,
            CenterZ = pixelZ,
            BrushRadius = brushRadius,
            BrushInnerRadius = brushInnerRadius,
            Strength = brushParams.Strength,
            TargetMaterialIndex = targetIndex
        };

        // 应用工具
        currentTool.Apply(ref context);
    }

    /// <summary>
    /// 结束当前笔触。
    /// </summary>
    public void EndStroke()
    {
        isStrokeActive = false;
        currentTool = null;
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
}
