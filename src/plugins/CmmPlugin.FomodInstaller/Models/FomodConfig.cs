using System.Collections.Generic;

namespace CmmPlugin.FomodInstaller.Models;

public class FomodModuleConfig
{
    public string ModuleName { get; set; } = string.Empty;
    /// <summary>
    /// Non-null when the archive has a wrapper folder (e.g. "MyMod_v1.0/fomod/ModuleConfig.xml" →
    /// WrapperPrefix = "MyMod_v1.0/"). Source paths from the FOMOD XML are relative to the module
    /// root inside this wrapper; the prefix must be prepended before matching against archive entries.
    /// </summary>
    public string? WrapperPrefix { get; set; }
    public List<FomodInstallFile> RequiredInstallFiles { get; set; } = new();
    public List<FomodInstallStep> InstallSteps { get; set; } = new();

    /// <summary>
    /// Files from <c>conditionalFileInstalls</c>, each with the condition that governs it. These
    /// used to be poured straight into <see cref="RequiredInstallFiles"/> with their conditions
    /// discarded, so every branch of a mod installed at once — "The Eyes Of Beauty" shipped both
    /// the Standalone textures and the Looking Stranger meshes no matter what was chosen.
    /// </summary>
    public List<FomodConditionalInstall> ConditionalInstalls { get; set; } = new();
}

public class FomodConditionalInstall
{
    public FomodCondition? When { get; set; }
    public List<FomodInstallFile> Files { get; set; } = new();
}

/// <summary>
/// A <c>visible</c> or <c>dependencies</c> block, reduced to the flag tests inside it.
///
/// The format also allows fileDependency, gameDependency and nesting. Those are not evaluated:
/// they describe the user's game install rather than choices made in this wizard, and guessing at
/// them would hide steps the user needs more often than it would help. A block with no flag test at
/// all is therefore treated as imposing no condition.
/// </summary>
public class FomodCondition
{
    /// <summary>operator="And" (the default) versus operator="Or".</summary>
    public bool RequireAll { get; set; } = true;

    public List<FomodFlagDependency> FlagDependencies { get; set; } = new();

    public bool IsSatisfiedBy(IReadOnlyDictionary<string, string> flags)
    {
        if (FlagDependencies.Count == 0) return true;

        static bool Holds(FomodFlagDependency d, IReadOnlyDictionary<string, string> f) =>
            // An unset flag reads as empty, which is how a dependency on value="False" is satisfied
            // before anything has set it — matching how the reference installers behave.
            string.Equals(
                f.TryGetValue(d.Flag, out var v) ? v : string.Empty,
                d.Value, System.StringComparison.OrdinalIgnoreCase);

        return RequireAll
            ? FlagDependencies.TrueForAll(d => Holds(d, flags))
            : FlagDependencies.Exists(d => Holds(d, flags));
    }
}

public record FomodFlagDependency(string Flag, string Value);

public class FomodInstallStep
{
    public string Name { get; set; } = string.Empty;
    public List<FomodGroup> Groups { get; set; } = new();

    /// <summary>The step's <c>visible</c> block, or null when it is always shown.</summary>
    public FomodCondition? VisibleWhen { get; set; }
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

    /// <summary>
    /// Flags this option sets when selected, from its <c>conditionalFlags</c> block. These are what
    /// later steps test in their <c>visible</c> condition — the mechanism by which choosing "no
    /// custom textures" is supposed to skip the thirty steps that pick textures.
    /// </summary>
    public List<FomodFlagDependency> ConditionalFlags { get; set; } = new();
}

public class FomodInstallFile
{
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public int Priority { get; set; }
}

