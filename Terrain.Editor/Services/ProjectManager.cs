#nullable enable

using System;
using System.IO;

namespace Terrain.Editor.Services;

/// <summary>
/// 项目管理器，管理项目目录结构和 TOML 配置。
/// </summary>
public sealed class ProjectManager
{
    private static readonly Lazy<ProjectManager> _instance = new(() => new());
    public static ProjectManager Instance => _instance.Value;

    /// <summary>
    /// 当前项目 .toml 文件的完整路径。
    /// </summary>
    public string ProjectFilePath { get; private set; } = "";

    /// <summary>
    /// 当前项目所在目录。
    /// </summary>
    public string ProjectPath => string.IsNullOrEmpty(ProjectFilePath)
        ? ""
        : Path.GetDirectoryName(Path.GetFullPath(ProjectFilePath)) ?? "";

    public string MaterialsPath => Path.Combine(ProjectPath, "materials");
    public string SplatMapsPath => Path.Combine(ProjectPath, "splatmaps");
    public string HeightmapsPath => Path.Combine(ProjectPath, "heightmaps");

    public bool IsProjectOpen => !string.IsNullOrEmpty(ProjectFilePath);

    /// <summary>
    /// 项目显示名称。从 TOML name 字段或文件名派生。
    /// </summary>
    public string ProjectName { get; private set; } = "";

    /// <summary>
    /// 是否有未保存的更改。
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// 未保存更改状态变化时触发。
    /// </summary>
    public event EventHandler? DirtyChanged;

    /// <summary>
    /// 缓存的项目配置，打开项目后持有。
    /// </summary>
    private TomlProjectConfig? cachedConfig;

    private ProjectManager() { }

    /// <summary>
    /// 创建新项目：创建目录结构并写入最小 TOML。
    /// </summary>
    public bool CreateProject(string projectFilePath, string projectName)
    {
        if (string.IsNullOrEmpty(projectFilePath))
            return false;

        // 先设置路径，这样 MaterialsPath/SplatMapsPath/HeightmapsPath 才能正确计算
        ProjectFilePath = projectFilePath;
        ProjectName = projectName;

        EnsureDirectoryExists(ProjectPath);
        EnsureDirectoryExists(MaterialsPath);
        EnsureDirectoryExists(SplatMapsPath);
        EnsureDirectoryExists(HeightmapsPath);

        // 此时还没选 heightmap，先写一个最小配置
        var config = new TomlProjectConfig { Name = projectName };
        config.WriteTo(projectFilePath);

        cachedConfig = config;
        MarkClean();
        return true;
    }

    /// <summary>
    /// 打开现有项目。
    /// </summary>
    public bool OpenProject(string projectFilePath)
    {
        if (string.IsNullOrEmpty(projectFilePath) || !File.Exists(projectFilePath))
            return false;

        try
        {
            cachedConfig = TomlProjectConfig.ReadFrom(projectFilePath);
            ProjectFilePath = projectFilePath;
            ProjectName = cachedConfig.Name;
            MarkClean();
            return true;
        }
        catch (Exception)
        {
            cachedConfig = null;
            return false;
        }
    }

    /// <summary>
    /// 关闭当前项目。
    /// </summary>
    public void CloseProject()
    {
        ProjectFilePath = "";
        ProjectName = "";
        cachedConfig = null;
        MarkClean();
    }

    /// <summary>
    /// 加载当前项目的配置。必须在 OpenProject 之后调用。
    /// </summary>
    public TomlProjectConfig? LoadConfig()
    {
        if (!IsProjectOpen)
            return null;

        // 优先使用缓存（OpenProject 已读取），仅在文件不存在时返回 null
        if (cachedConfig != null)
            return cachedConfig;

        if (!File.Exists(ProjectFilePath))
            return null;

        cachedConfig = TomlProjectConfig.ReadFrom(ProjectFilePath);
        return cachedConfig;
    }

    /// <summary>
    /// 保存项目配置到 .toml 文件。
    /// </summary>
    public void SaveConfig(TomlProjectConfig config)
    {
        if (!IsProjectOpen)
            return;

        config.WriteTo(ProjectFilePath);
        cachedConfig = config;
        MarkClean();
    }

    /// <summary>
    /// 另存为新路径。
    /// </summary>
    public void SaveProjectAs(string newFilePath)
    {
        if (cachedConfig == null)
            return;

        cachedConfig.WriteTo(newFilePath);
        ProjectFilePath = newFilePath;
        ProjectName = cachedConfig.Name;
        MarkClean();
    }

    /// <summary>
    /// 标记为有未保存更改。
    /// </summary>
    public void MarkDirty()
    {
        if (!IsDirty)
        {
            IsDirty = true;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 清除未保存标记。
    /// </summary>
    public void MarkClean()
    {
        if (IsDirty)
        {
            IsDirty = false;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 获取材质索引图的保存路径。
    /// </summary>
    public string GetMaterialIndexPath(string terrainName)
        => Path.Combine(SplatMapsPath, $"{terrainName}_material_index.png");

    private void EnsureDirectoryExists(string? path)
    {
        if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
            Directory.CreateDirectory(path);
    }
}
