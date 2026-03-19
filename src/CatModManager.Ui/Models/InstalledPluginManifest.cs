using System;
using System.Collections.Generic;

namespace CatModManager.Ui.Models;

public class InstalledPluginManifest
{
    public List<InstalledPlugin> Installed { get; set; } = new();
}

public class InstalledPlugin
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime InstalledAt { get; set; }
}
