using System.Collections.Generic;
using System.IO;
using System;

namespace Terrain.Resources;

public static class GameResourceResolverBootstrap
{
    public static GameResourceResolver CreateForAppDirectory(string appDirectory)
    {
        string gameRoot = NormalizeDirectoryPath(GameResourceRootLocator.FindFrom(appDirectory));
        LaunchSettings launchSettings = LaunchSettingsService.LoadOrCreateForAppDirectory(appDirectory);
        IReadOnlyList<LaunchSettingsMod> enabledMods = LaunchSettingsService.GetEnabledMods(launchSettings);

        var layers = new List<GameResourceLayer>(enabledMods.Count + 1)
        {
            new("base", gameRoot, isBaseLayer: true),
        };

        foreach (LaunchSettingsMod mod in enabledMods)
        {
            string modRoot = NormalizeDirectoryPath(mod.Root);
            ValidateModRoot(mod, modRoot, gameRoot);
            layers.Add(new GameResourceLayer(mod.Id, modRoot, isBaseLayer: false));
        }

        return new GameResourceResolver(layers);
    }

    private static void ValidateModRoot(LaunchSettingsMod mod, string modRoot, string gameRoot)
    {
        if (string.Equals(modRoot, gameRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Enabled mod root cannot equal the game root: {mod.Id}");

        if (modRoot.StartsWith(gameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || modRoot.StartsWith(gameRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Enabled mod root cannot be inside the game root: {mod.Id}");
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
