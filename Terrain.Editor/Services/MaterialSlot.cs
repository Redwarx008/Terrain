#nullable enable

using Stride.Graphics;

namespace Terrain.Editor.Services;

/// <summary>
/// 材质槽位配置，存储单个材质的属性。
/// 最多支持 256 个材质槽位（索引 0-255）。
/// </summary>
public sealed class MaterialSlot
{
    /// <summary>
    /// 槽位索引 (0-255)。
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// 材质显示名称。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 稳定材质标识，用于 descriptor / biome settings 的跨次保存引用。
    /// </summary>
    public string? MaterialId { get; set; }

    /// <summary>
    /// 该槽位是否为仅运行时存在的缺失材质占位。
    /// </summary>
    public bool IsRuntimeFallbackPlaceholder { get; set; }

    /// <summary>
    /// 该槽位当前是否使用默认 diffuse/albedo 表现。
    /// </summary>
    public bool UsesFallbackAlbedo { get; set; }

    /// <summary>
    /// 该槽位当前是否使用默认法线表现。
    /// </summary>
    public bool UsesFallbackNormal { get; set; }

    /// <summary>
    /// Albedo 纹理文件路径（相对或绝对）。
    /// </summary>
    public string? AlbedoTexturePath { get; set; }

    /// <summary>
    /// Normal 纹理文件路径（相对或绝对）。
    /// </summary>
    public string? NormalTexturePath { get; set; }

    /// <summary>
    /// Properties 纹理文件路径（相对或绝对）。
    /// </summary>
    public string? PropertiesTexturePath { get; set; }

    /// <summary>
    /// GPU Albedo 纹理引用（用于预览渲染）。
    /// </summary>
    internal Texture? AlbedoTexture { get; set; }

    /// <summary>
    /// GPU Normal 纹理引用（用于预览渲染）。
    /// </summary>
    internal Texture? NormalTexture { get; set; }

    /// <summary>
    /// GPU Properties 纹理引用（用于预览渲染）。
    /// </summary>
    internal Texture? PropertiesTexture { get; set; }

    /// <summary>
    /// 纹理导入尺寸。
    /// </summary>
    public TextureSize ImportSize { get; set; } = TextureSize.Size512;

    /// <summary>
    /// 纹理平铺缩放比例。
    /// </summary>
    public float TilingScale { get; set; } = 1.0f;

    /// <summary>
    /// 槽位是否为空（未配置纹理）。
    /// </summary>
    public bool IsEmpty =>
        string.IsNullOrEmpty(MaterialId)
        && string.IsNullOrEmpty(AlbedoTexturePath)
        && string.IsNullOrEmpty(NormalTexturePath)
        && string.IsNullOrEmpty(PropertiesTexturePath)
        && !IsRuntimeFallbackPlaceholder;

    /// <summary>
    /// 清除槽位配置并释放 GPU 资源。
    /// </summary>
    public void Clear()
    {
        MaterialId = null;
        IsRuntimeFallbackPlaceholder = false;
        UsesFallbackAlbedo = false;
        UsesFallbackNormal = false;
        AlbedoTexturePath = null;
        NormalTexturePath = null;
        PropertiesTexturePath = null;
        Name = $"Texture {Index}";

        // 先置空引用，再释放资源，避免 Dispose 异常导致状态不一致
        var albedo = AlbedoTexture;
        var normal = NormalTexture;
        var properties = PropertiesTexture;
        AlbedoTexture = null;
        NormalTexture = null;
        PropertiesTexture = null;

        albedo?.Dispose();
        normal?.Dispose();
        properties?.Dispose();
        TilingScale = 1.0f;
    }
}
