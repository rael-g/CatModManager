using System.Collections.Generic;
using System.Linq;
using Xunit;
using CmmPlugin.FomodInstaller.Models;
using CmmPlugin.FomodInstaller.Wizard;

namespace CatModManager.Tests.Plugins.FomodInstaller;

/// <summary>
/// FOMOD's conditional flags: an option sets a flag, and later steps declare a &lt;visible&gt;
/// condition on it. None of it was parsed, so every step was shown and every conditional file was
/// installed. "The Eyes Of Beauty" has 39 steps, 36 of them conditional — picking "no custom
/// textures" was supposed to skip thirty of them and skipped none.
/// </summary>
public class FomodFlagTests
{
    private static FomodPlugin Option(string name, string? flag = null, string value = "True", string? file = null)
    {
        var p = new FomodPlugin { Name = name };
        if (flag != null) p.ConditionalFlags.Add(new FomodFlagDependency(flag, value));
        if (file != null) p.Files.Add(new FomodInstallFile { Source = file });
        return p;
    }

    private static FomodInstallStep Step(string name, FomodCondition? visible, params FomodPlugin[] options)
        => new()
        {
            Name = name,
            VisibleWhen = visible,
            Groups = { new FomodGroup { Name = name + " group", Type = GroupType.SelectExactlyOne, Plugins = options.ToList() } }
        };

    private static FomodCondition WhenFlag(string flag, string value)
        => new() { FlagDependencies = { new FomodFlagDependency(flag, value) } };

    /// <summary>Choose "no", and the steps that only matter when "yes" are not walked through.</summary>
    [Fact]
    public void AStepIsSkipped_WhenItsFlagConditionIsNotMet()
    {
        var config = new FomodModuleConfig
        {
            InstallSteps =
            {
                Step("Choose", null,
                     Option("Custom", "CustomTextures", "True"),
                     Option("Vanilla", "CustomTextures", "False")),
                Step("Pick a texture", WhenFlag("CustomTextures", "True"), Option("Red")),
                Step("Always here", null, Option("Whatever")),
            }
        };

        var vm = new FomodWizardViewModel(config);

        // "Custom" is the group's first option, so it is the default: all three steps are reachable.
        Assert.Equal(3, vm.TotalSteps);

        vm.TogglePlugin(config.InstallSteps[0], config.InstallSteps[0].Groups[0], config.InstallSteps[0].Groups[0].Plugins[1]);

        Assert.Equal(2, vm.TotalSteps);
        Assert.Equal([0, 2], vm.VisibleStepIndices);

        // And Next lands past the skipped step rather than on it.
        vm.GoNext();
        Assert.Equal("Always here", vm.CurrentStep!.Name);
        Assert.True(vm.IsLastStep);
    }

    /// <summary>A choice left behind in a step that is no longer reachable must not install files.</summary>
    [Fact]
    public void FilesFromASkippedStep_AreNotInstalled()
    {
        var config = new FomodModuleConfig
        {
            InstallSteps =
            {
                Step("Choose", null,
                     Option("Custom", "CustomTextures", "True"),
                     Option("Vanilla", "CustomTextures", "False")),
                Step("Pick a texture", WhenFlag("CustomTextures", "True"), Option("Red", file: "textures/red.dds")),
            }
        };

        var vm = new FomodWizardViewModel(config);
        Assert.Contains("textures/red.dds", vm.BuildFileMapping().Keys);

        vm.TogglePlugin(config.InstallSteps[0], config.InstallSteps[0].Groups[0], config.InstallSteps[0].Groups[0].Plugins[1]);

        Assert.DoesNotContain("textures/red.dds", vm.BuildFileMapping().Keys);
    }

    /// <summary>
    /// conditionalFileInstalls carries its own dependencies. They used to be discarded and every
    /// pattern treated as required, so a mod shipped all of its mutually exclusive branches at once.
    /// </summary>
    [Fact]
    public void ConditionalInstalls_ObeyTheirDependencies()
    {
        var config = new FomodModuleConfig
        {
            InstallSteps = { Step("Choose", null, Option("A", "Variant", "A"), Option("B", "Variant", "B")) },
            ConditionalInstalls =
            {
                new FomodConditionalInstall { When = WhenFlag("Variant", "A"), Files = { new FomodInstallFile { Source = "a.esp" } } },
                new FomodConditionalInstall { When = WhenFlag("Variant", "B"), Files = { new FomodInstallFile { Source = "b.esp" } } },
            }
        };

        var vm = new FomodWizardViewModel(config);
        var mapping = vm.BuildFileMapping();

        Assert.Contains("a.esp", mapping.Keys);
        Assert.DoesNotContain("b.esp", mapping.Keys);
    }
}
