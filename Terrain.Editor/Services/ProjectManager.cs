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
        : GetProjectDirectory(ProjectFilePath);

    public string MaterialsPath => GetMaterialsPath(ProjectFilePath);
    public string SplatMapsPath => GetSplatMapsPath(ProjectFilePath);
    public string HeightmapsPath => GetHeightmapsPath(ProjectFilePath);

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

        var config = new TomlProjectConfig { Name = projectName };
        SaveConfigAs(projectFilePath, config);
        return true;
    }

    /// <summary>
    /// 打开现有项目。
    /// </summary>
    public bool OpenProject(string projectFilePath)
    {
        if (string.IsNullOrEmpty(projectFilePath) || !File.Exists(projectFilePath))
        {
            CloseProject();
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(projectFilePath);
            cachedConfig = TomlProjectConfig.ReadFrom(fullPath);
            ProjectFilePath = fullPath;
            ProjectName = cachedConfig.Name;
            MarkClean();
            return true;
        }
        catch (Exception)
        {
            CloseProject();
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

        SaveConfigAs(ProjectFilePath, config);
    }

    /// <summary>
    /// 将配置保存到指定路径，并将该路径设为当前项目。
    /// </summary>
    public void SaveConfigAs(string projectFilePath, TomlProjectConfig config)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            return;

        string fullPath = Path.GetFullPath(projectFilePath);
        EnsureProjectDirectories(fullPath);
        config.WriteTo(fullPath);
        cachedConfig = config;
        ProjectFilePath = fullPath;
        ProjectName = config.Name;
        MarkClean();
    }

    /// <summary>
    /// 另存为新路径。
    /// </summary>
    public void SaveProjectAs(string newFilePath)
    {
        if (cachedConfig == null)
            return;

        SaveConfigAs(newFilePath, cachedConfig);
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

    public static string GetProjectDirectory(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            return "";

        return Path.GetDirectoryName(Path.GetFullPath(projectFilePath)) ?? "";
    }

    public static string GetMaterialsPath(string projectFilePath)
    {
        return Path.Combine(GetProjectDirectory(projectFilePath), "materials");
    }

    public static string GetSplatMapsPath(string projectFilePath)
    {
        return Path.Combine(GetProjectDirectory(projectFilePath), "splatmaps");
    }

    public static string GetHeightmapsPath(string projectFilePath)
    {
        return Path.Combine(GetProjectDirectory(projectFilePath), "heightmaps");
    }

    private static void EnsureProjectDirectories(string projectFilePath)
    {
        string projectDirectory = GetProjectDirectory(projectFilePath);
        if (string.IsNullOrEmpty(projectDirectory))
            return;

        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(GetMaterialsPath(projectFilePath));
        Directory.CreateDirectory(GetSplatMapsPath(projectFilePath));
        Directory.CreateDirectory(GetHeightmapsPath(projectFilePath));
    }
}
