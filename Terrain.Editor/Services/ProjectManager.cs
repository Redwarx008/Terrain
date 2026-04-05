#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Terrain.Editor.Services;

/// <summary>
/// 项目管理器，管理项目目录结构和配置。
/// </summary>
public sealed class ProjectManager
{
    private static readonly Lazy<ProjectManager> _instance = new(() => new());
    public static ProjectManager Instance => _instance.Value;

    public string ProjectPath { get; private set; } = "";
    public string MaterialsPath => Path.Combine(ProjectPath, "materials");
    public string SplatMapsPath => Path.Combine(ProjectPath, "splatmaps");
    public string HeightmapsPath => Path.Combine(ProjectPath, "heightmaps");
    public string ProjectConfigPath => Path.Combine(ProjectPath, "project.json");

    public bool IsProjectOpen => !string.IsNullOrEmpty(ProjectPath);

    private ProjectManager() { }

    /// <summary>
    /// 创建新项目。
    /// </summary>
    public bool CreateProject(string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath))
            return false;

        ProjectPath = projectPath;
        EnsureDirectoryExists(MaterialsPath);
        EnsureDirectoryExists(SplatMapsPath);
        EnsureDirectoryExists(HeightmapsPath);

        // 创建默认项目配置
        var config = new ProjectConfig();
        SaveConfig(config);

        return true;
    }

    /// <summary>
    /// 打开现有项目。
    /// </summary>
    public bool OpenProject(string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
            return false;

        var configPath = Path.Combine(projectPath, "project.json");
        if (!File.Exists(configPath))
            return false;

        ProjectPath = projectPath;
        return true;
    }

    /// <summary>
    /// 关闭当前项目。
    /// </summary>
    public void CloseProject()
    {
        ProjectPath = "";
    }

    /// <summary>
    /// 获取材质纹理的保存路径。
    /// </summary>
    public string GetMaterialTexturePath(int slotIndex, TextureSize size, string type = "albedo")
        => Path.Combine(MaterialsPath, $"slot_{slotIndex:D3}_{type}_{(int)size}.png");

    /// <summary>
    /// 获取材质索引图的保存路径。
    /// </summary>
    public string GetMaterialIndexPath(string terrainName)
        => Path.Combine(SplatMapsPath, $"{terrainName}_material_index.png");

    /// <summary>
    /// 保存项目配置。
    /// </summary>
    public void SaveConfig(ProjectConfig config)
    {
        if (!IsProjectOpen)
            return;

        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ProjectConfigPath, json);
    }

    /// <summary>
    /// 加载项目配置。
    /// </summary>
    public ProjectConfig? LoadConfig()
    {
        if (!IsProjectOpen || !File.Exists(ProjectConfigPath))
            return null;

        string json = File.ReadAllText(ProjectConfigPath);
        return JsonSerializer.Deserialize<ProjectConfig>(json);
    }

    private void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }
}

/// <summary>
/// 项目配置。
/// </summary>
public class ProjectConfig
{
    public int Version { get; set; } = 1;
    public string? HeightmapPath { get; set; }
    public List<MaterialSlotConfig> MaterialSlots { get; set; } = new();
    public TextureSize DefaultTextureSize { get; set; } = TextureSize.Size512;
}

/// <summary>
/// 材质槽位配置。
/// </summary>
public class MaterialSlotConfig
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string? AlbedoTexturePath { get; set; }
    public string? NormalTexturePath { get; set; }
    public float TilingScale { get; set; } = 1.0f;
    public TextureSize TextureSize { get; set; } = TextureSize.Size512;
}
