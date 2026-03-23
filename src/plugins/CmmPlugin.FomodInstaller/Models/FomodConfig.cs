using System.Collections.Generic;

namespace CmmPlugin.FomodInstaller.Models;

public class FomodModuleConfig
{
    public string ModuleName { get; set; } = string.Empty;
    public List<FomodInstallFile> RequiredInstallFiles { get; set; } = new();
    public List<FomodInstallStep> InstallSteps { get; set; } = new();
}

public class FomodInstallStep
{
    public string Name { get; set; } = string.Empty;
    public List<FomodGroup> Groups { get; set; } = new();
}

public class FomodGroup
{
    public string Name { get; set; } = string.Empty;
    public GroupType Type { get; set; } = GroupType.SelectAny;
    public List<FomodPlugin> Plugins { get; set; } = new();
}

public enum GroupType
{
    SelectAny,
    SelectAll,
    SelectExactlyOne,
    SelectAtLeastOne,
    SelectAtMostOne
}

public class FomodPlugin
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public List<FomodInstallFile> Files { get; set; } = new();
    public bool IsDefault { get; set; }
}

public class FomodInstallFile
{
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public int Priority { get; set; }
}
