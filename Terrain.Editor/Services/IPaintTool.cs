#nullable enable

namespace Terrain.Editor.Services;

/// <summary>
/// 材质绘制编辑上下文，传递给绘制工具的 Apply 方法。
/// 使用 readonly struct 避免堆分配，通过 ref 传递提高性能。
/// </summary>
public readonly struct PaintEditContext
{
    /// <summary>
    /// 材质索引图引用。工具直接修改此数据。
    /// </summary>
    public MaterialIndexMap IndexMap { get; init; }

    /// <summary>
    /// 索引图宽度（像素）。
    /// </summary>
    public int DataWidth { get; init; }

    /// <summary>
    /// 索引图高度（像素）。
    /// </summary>
    public int DataHeight { get; init; }

    /// <summary>
    /// 笔刷中心 X 坐标（像素空间）。
    /// </summary>
    public int CenterX { get; init; }

    /// <summary>
    /// 笔刷中心 Z 坐标（像素空间）。
    /// </summary>
    public int CenterZ { get; init; }

    /// <summary>
    /// 笔刷外半径（像素）。
    /// </summary>
    public float BrushRadius { get; init; }

    /// <summary>
    /// 笔刷内半径（像素），100% 强度区域。
    /// 计算方式: Size * 0.5f * EffectiveFalloff
    /// </summary>
    public float BrushInnerRadius { get; init; }

    /// <summary>
    /// 笔刷强度 (0-1)。
    /// </summary>
    public float Strength { get; init; }

    /// <summary>
    /// 目标材质索引（要绘制或擦除的材质）。
    /// </summary>
    public byte TargetMaterialIndex { get; init; }
}

/// <summary>
/// 材质绘制工具接口。
/// 实现 Strategy 模式，支持不同的绘制行为（绘制、擦除等）。
/// </summary>
public interface IPaintTool
{
    /// <summary>
    /// 工具名称，用于显示和识别。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 应用工具效果到材质索引数据。
    /// </summary>
    /// <param name="context">编辑上下文，包含索引数据和笔刷参数。</param>
    void Apply(ref PaintEditContext context);
}
