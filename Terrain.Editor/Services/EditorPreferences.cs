#nullable enable

using System;
using System.IO;
using Tommy;

namespace Terrain.Editor.Services;

/// <summary>
/// 编辑器用户偏好设置。存储在用户 AppData 目录中，独立于项目。
/// </summary>
public class EditorPreferences
{
    private static readonly string PreferencesFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TerrainEditor",
        "preferences.toml");

    private static EditorPreferences? instance;

    /// <summary>
    /// 单例实例。
    /// </summary>
    public static EditorPreferences Instance => instance ??= Load();

    /// <summary>
    /// 相机速度档位索引（对应 HybridCameraController.SpeedPresets）。
    /// </summary>
    public int CameraSpeedIndex { get; set; } = 2; // 默认 50 单位/秒

    /// <summary>
    /// 从文件加载偏好设置。
    /// </summary>
    public static EditorPreferences Load()
    {
        var preferences = new EditorPreferences();

        try
        {
            if (File.Exists(PreferencesFilePath))
            {
                using var reader = File.OpenText(PreferencesFilePath);
                var root = TOML.Parse(reader);

                if (root.HasKey("camera") && root["camera"].IsTable)
                {
                    var camera = root["camera"];
                    preferences.CameraSpeedIndex = camera.HasKey("speed_index") && camera["speed_index"].IsInteger
                        ? Math.Clamp((int)camera["speed_index"].AsInteger, 0, 8) // HybridCameraController 有 9 个档位
                        : 2;
                }
            }
        }
        catch (Exception ex)
        {
            // 加载失败时使用默认值
            System.Diagnostics.Debug.WriteLine($"Failed to load editor preferences: {ex.Message}");
        }

        return preferences;
    }

    /// <summary>
    /// 保存偏好设置到文件。
    /// </summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(PreferencesFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var root = new TomlTable();

            var camera = new TomlTable();
            camera["speed_index"] = CameraSpeedIndex;
            root["camera"] = camera;

            using var writer = File.CreateText(PreferencesFilePath);
            root.WriteTo(writer);
            writer.Flush();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save editor preferences: {ex.Message}");
        }
    }
}
