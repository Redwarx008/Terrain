using System.Collections.Generic;

namespace Terrain.Resources;

public sealed class LaunchSettings
{
    public int Version { get; set; } = 1;
    public List<LaunchSettingsMod> Mods { get; set; } = new();
}

public sealed class LaunchSettingsMod
{
    public string Id { get; set; } = string.Empty;
    public string Root { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
