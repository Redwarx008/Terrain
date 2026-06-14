using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Terrain.Resources;

public static class LaunchSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static LaunchSettings Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("LaunchSetting.json was not found.", filePath);

        LaunchSettings settings = JsonSerializer.Deserialize<LaunchSettings>(File.ReadAllText(filePath), SerializerOptions)
            ?? throw new InvalidDataException("LaunchSetting.json could not be parsed.");
        if (settings.Version != 1)
            throw new InvalidDataException($"Unsupported LaunchSetting.json version: {settings.Version}.");

        return settings;
    }

    public static LaunchSettings LoadOrCreateForAppDirectory(string appDirectory)
    {
        if (string.IsNullOrWhiteSpace(appDirectory))
            throw new ArgumentException("App directory is required.", nameof(appDirectory));

        string normalizedDirectory = Path.GetFullPath(appDirectory);
        Directory.CreateDirectory(normalizedDirectory);

        string filePath = Path.Combine(normalizedDirectory, "LaunchSetting.json");
        if (!File.Exists(filePath))
            File.WriteAllText(filePath, JsonSerializer.Serialize(new LaunchSettings(), SerializerOptions));

        return Load(filePath);
    }

    public static IReadOnlyList<LaunchSettingsMod> GetEnabledMods(LaunchSettings settings)
    {
        List<LaunchSettingsMod> enabledMods = settings.Mods.Where(mod => mod.Enabled).ToList();
        foreach (LaunchSettingsMod mod in enabledMods)
        {
            if (string.IsNullOrWhiteSpace(mod.Id))
                throw new InvalidDataException("Enabled mod id is empty.");
            if (string.IsNullOrWhiteSpace(mod.Root))
                throw new InvalidDataException($"Enabled mod root is empty: {mod.Id}");
            if (!Path.IsPathFullyQualified(mod.Root))
                throw new InvalidDataException($"Enabled mod root must be an absolute path: {mod.Id}");
            if (!Directory.Exists(mod.Root))
                throw new InvalidDataException($"Enabled mod root does not exist: {mod.Root}");
        }

        return enabledMods;
    }
}
