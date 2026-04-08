#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Tommy;

namespace Terrain.Editor.Services;

/// <summary>
/// TOML 项目配置数据模型。
/// 所有文件路径相对于 .toml 文件所在目录存储。
/// </summary>
public class TomlProjectConfig
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Untitled";
    public string? HeightmapPath { get; set; }
    public string? IndexMapPath { get; set; }
    public List<TomlMaterialSlotConfig> MaterialSlots { get; set; } = new();

    /// <summary>
    /// 从 .toml 文件读取配置。路径自动解析为绝对路径。
    /// </summary>
    public static TomlProjectConfig ReadFrom(string tomlFilePath)
    {
        using var reader = File.OpenText(tomlFilePath);
        var root = TOML.Parse(reader);

        var config = new TomlProjectConfig();
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(tomlFilePath)) ?? "";

        config.Version = root.HasKey("version") && root["version"].IsInteger
            ? (int)root["version"].AsInteger : 1;
        config.Name = root.HasKey("name") && root["name"].IsString
            ? root["name"].AsString.Value : "Untitled";

        if (root.HasKey("terrain"))
        {
            var terrain = root["terrain"];
            config.HeightmapPath = terrain.HasKey("heightmap") && terrain["heightmap"].IsString
                ? ResolvePath(terrain["heightmap"].AsString.Value, baseDir) : null;
            config.IndexMapPath = terrain.HasKey("indexmap") && terrain["indexmap"].IsString
                ? ResolvePath(terrain["indexmap"].AsString.Value, baseDir) : null;
        }

        if (root.HasKey("material_slots") && root["material_slots"].IsArray)
        {
            foreach (TomlNode slotNode in root["material_slots"].AsArray)
            {
                var slot = new TomlMaterialSlotConfig
                {
                    Index = (int)slotNode["index"].AsInteger,
                    Name = slotNode["name"].AsString.Value,
                    AlbedoPath = slotNode.HasKey("albedo") && slotNode["albedo"].IsString
                        ? ResolvePath(slotNode["albedo"].AsString.Value, baseDir)
                        : null,
                    NormalPath = slotNode.HasKey("normal") && slotNode["normal"].IsString
                        ? ResolvePath(slotNode["normal"].AsString.Value, baseDir)
                        : null,
                    TilingScale = slotNode.HasKey("tiling_scale")
                        ? (float)slotNode["tiling_scale"].AsFloat
                        : 1.0f,
                };
                config.MaterialSlots.Add(slot);
            }
        }

        return config;
    }

    /// <summary>
    /// 将配置写入 .toml 文件。绝对路径自动转换为相对路径。
    /// </summary>
    public void WriteTo(string tomlFilePath)
    {
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(tomlFilePath)) ?? "";

        var root = new TomlTable();
        root["version"] = Version;
        root["name"] = Name;

        var terrain = new TomlTable();
        if (!string.IsNullOrEmpty(HeightmapPath))
            terrain["heightmap"] = MakeRelative(HeightmapPath, baseDir);
        if (!string.IsNullOrEmpty(IndexMapPath))
            terrain["indexmap"] = MakeRelative(IndexMapPath, baseDir);
        root["terrain"] = terrain;

        if (MaterialSlots.Count > 0)
        {
            var slotsArray = new TomlArray();
            foreach (var slot in MaterialSlots)
            {
                var slotTable = new TomlTable();
                slotTable["index"] = slot.Index;
                slotTable["name"] = slot.Name;
                if (!string.IsNullOrEmpty(slot.AlbedoPath))
                    slotTable["albedo"] = MakeRelative(slot.AlbedoPath, baseDir);
                if (!string.IsNullOrEmpty(slot.NormalPath))
                    slotTable["normal"] = MakeRelative(slot.NormalPath, baseDir);
                slotTable["tiling_scale"] = Math.Round(slot.TilingScale, 2);
                slotsArray.Add(slotTable);
            }
            root["material_slots"] = slotsArray;
        }

        // 确保目录存在
        Directory.CreateDirectory(baseDir);

        using var writer = File.CreateText(tomlFilePath);
        root.WriteTo(writer);
    }

    /// <summary>
    /// 将绝对路径转换为相对于 baseDir 的路径，使用 / 分隔符。
    /// </summary>
    internal static string MakeRelative(string absPath, string baseDir)
    {
        if (string.IsNullOrEmpty(absPath) || string.IsNullOrEmpty(baseDir))
            return absPath ?? "";

        try
        {
            string relative = Path.GetRelativePath(baseDir, absPath);
            return relative.Replace('\\', '/');
        }
        catch (Exception)
        {
            return absPath;
        }
    }

    /// <summary>
    /// 将相对路径解析为绝对路径。如果已经是绝对路径则直接返回。
    /// </summary>
    internal static string? ResolvePath(string? relativeOrAbsolute, string baseDir)
    {
        if (string.IsNullOrEmpty(relativeOrAbsolute))
            return null;

        if (Path.IsPathRooted(relativeOrAbsolute))
            return relativeOrAbsolute;

        return Path.GetFullPath(Path.Combine(baseDir, relativeOrAbsolute.Replace('/', Path.DirectorySeparatorChar)));
    }
}

/// <summary>
/// 材质槽位配置。
/// </summary>
public class TomlMaterialSlotConfig
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string? AlbedoPath { get; set; }
    public string? NormalPath { get; set; }
    public float TilingScale { get; set; } = 1.0f;
}
