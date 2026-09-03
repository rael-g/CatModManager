using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CmmPlugin.FomodInstaller.Models;
using CatModManager.PluginSdk;

namespace CmmPlugin.FomodInstaller.Parser;

public static class FomodParser
{
    private const string ConfigPath = "fomod/ModuleConfig.xml";

    private static bool IsConfigEntry(string? key) =>
        key != null &&
        (key.Replace('\\', '/').Equals(ConfigPath, StringComparison.OrdinalIgnoreCase) ||
         key.Replace('\\', '/').EndsWith("/" + ConfigPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns true if the archive contains a FOMOD ModuleConfig.xml.</summary>
    public static bool IsFomod(string archivePath, IArchiveExtractor extractor)
    {
        if (!File.Exists(archivePath)) return false;
        try
        {
            var files = extractor.GetFileList(archivePath);
            return files.Any(IsConfigEntry);
        }
        catch { return false; }
    }

    private static readonly string[] PreviewExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    /// <summary>
    /// The config, plus every preview image in the archive, read in a single pass.
    ///
    /// The previews are effectively free. In a solid .7z the cost of reading any one entry is
    /// decoding the whole stream, so reading ModuleConfig.xml out of Cridow's 335 MB skin set takes
    /// 25 seconds — and reading it *together with* its 16 previews also takes 25 seconds. Doing
    /// them as two operations doubled that, and the first one ran on the UI thread.
    ///
    /// Previews are picked by extension rather than by what the config references, because the
    /// config is only readable once the pass is already underway.
    /// </summary>
    public static FomodPackage Read(string archivePath, IArchiveExtractor extractor)
    {
        var files = extractor.GetFileList(archivePath).ToList();
        var configKey = files.FirstOrDefault(IsConfigEntry)
            ?? throw new InvalidOperationException("ModuleConfig.xml not found in archive.");

        var previewKeys = files
            .Where(f => PreviewExtensions.Any(x => f.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var opened = extractor.OpenFileStreams(archivePath, previewKeys.Append(configKey));

        if (!opened.TryGetValue(configKey, out var configStream))
            throw new InvalidOperationException($"Could not open {configKey} from archive.");

        FomodModuleConfig config;
        using (configStream)
            config = ParseDocument(XDocument.Load(configStream));

        config.WrapperPrefix = WrapperPrefixOf(configKey);

        var previews = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in previewKeys)
        {
            if (!opened.TryGetValue(key, out var s)) continue;
            using (s)
            {
                var ms = new MemoryStream();
                s.CopyTo(ms);
                previews[NormalizeKey(key)] = ms.ToArray();
            }
        }

        return new FomodPackage(config, previews);
    }

    /// <summary>Both sides of the preview lookup go through here, so separators and leading slashes cannot disagree.</summary>
    public static string NormalizeKey(string path) => path.Replace('\\', '/').TrimStart('/');

    /// <summary>
    /// Detect wrapper folder: if ModuleConfig.xml is not at "fomod/..." but at
    /// "WrapperName/fomod/...", FOMOD source paths are relative to "WrapperName/".
    /// Found by trimming the known "fomod/ModuleConfig.xml" suffix rather than by searching for
    /// "fomod/": a wrapper folder whose own name ends in "fomod" — Starfield's "Jiggle_Fomod/" —
    /// matches the search inside its own name, yielding the prefix "Jiggle_". Every source then
    /// pointed at a path that does not exist and the install silently produced an empty folder.
    /// </summary>
    private static string? WrapperPrefixOf(string configKey)
    {
        var normalized = configKey.Replace('\\', '/');
        return normalized.Length > ConfigPath.Length ? normalized[..^ConfigPath.Length] : null;
    }

    /// <summary>
    /// Just the config. Delegates to <see cref="Read"/> rather than reading the archive itself, so
    /// there is one parsing path and the tests that call this exercise the one production uses.
    /// </summary>
    public static FomodModuleConfig Parse(string archivePath, IArchiveExtractor extractor)
        => Read(archivePath, extractor).Config;

    private static FomodModuleConfig ParseDocument(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidOperationException("Empty FOMOD XML.");
        var ns = root.GetDefaultNamespace();

        var config = new FomodModuleConfig
        {
            ModuleName = (string?)root.Element(ns + "moduleName") ?? string.Empty
        };

        var reqFiles = root.Element(ns + "requiredInstallFiles");
        if (reqFiles != null)
            config.RequiredInstallFiles.AddRange(ParseFileList(reqFiles, ns));

        var stepsEl = root.Element(ns + "installSteps");
        if (stepsEl != null)
        {
            foreach (var stepEl in stepsEl.Elements(ns + "installStep"))
                config.InstallSteps.Add(ParseStep(stepEl, ns));
        }

        var conditionalEl = root.Element(ns + "conditionalFileInstalls");
        if (conditionalEl != null)
        {
            var patternsEl = conditionalEl.Element(ns + "patterns");
            if (patternsEl != null)
            {
                foreach (var patternEl in patternsEl.Elements(ns + "pattern"))
                {
                    var filesEl = patternEl.Element(ns + "files");
                    if (filesEl == null) continue;

                    // The condition travels with the files. Dropping it here and treating every
                    // pattern as required is what installed every branch of a mod at once.
                    config.ConditionalInstalls.Add(new FomodConditionalInstall
                    {
                        When  = ParseCondition(patternEl.Element(ns + "dependencies"), ns),
                        Files = ParseFileList(filesEl, ns).ToList()
                    });
                }
            }
        }

        return config;
    }

    /// <summary>
    /// A visible/dependencies block, or null when there is none. Only flagDependency tests are read
    /// — see <see cref="FomodCondition"/> for why the other kinds are left alone.
    ///
    /// The tests are taken from anywhere below the block, not just from its direct children. FOMOD
    /// Creation Tool writes <c>&lt;visible&gt;&lt;dependencies operator="And"&gt;…&lt;/dependencies&gt;&lt;/visible&gt;</c>,
    /// one level deeper than the hand-written form, and reading only direct children found nothing
    /// there — which reads as an empty condition, which means "always visible". In "MTM 3BBB CBP
    /// OCBP OCBPC Physics Preset" (Fallout 4, Nexus 39195) that showed both the CBBE and the Fusion
    /// Girl step, and since both write the same <c>cbp.ini</c>, whichever came last silently won.
    ///
    /// Flattening loses the grouping of a block that nests <c>dependencies</c> with mixed operators,
    /// which no definition seen so far does — and the alternative was ignoring the condition whole.
    /// </summary>
    private static FomodCondition? ParseCondition(XElement? el, XNamespace ns)
    {
        if (el == null) return null;

        // The operator sits on whichever element actually holds the tests.
        var scope = el.Element(ns + "dependencies") ?? el;

        var condition = new FomodCondition
        {
            RequireAll = !string.Equals((string?)scope.Attribute("operator"), "Or", StringComparison.OrdinalIgnoreCase)
        };

        foreach (var dep in el.Descendants(ns + "flagDependency"))
        {
            var flag = (string?)dep.Attribute("flag");
            if (string.IsNullOrEmpty(flag)) continue;
            condition.FlagDependencies.Add(new FomodFlagDependency(flag, (string?)dep.Attribute("value") ?? string.Empty));
        }

        return condition;
    }

    private static FomodInstallStep ParseStep(XElement stepEl, XNamespace ns)
    {
        var step = new FomodInstallStep
        {
            Name        = (string?)stepEl.Attribute("name") ?? string.Empty,
            VisibleWhen = ParseCondition(stepEl.Element(ns + "visible"), ns)
        };

        var groupsEl = stepEl.Element(ns + "optionalFileGroups");
        if (groupsEl != null)
        {
            foreach (var groupEl in groupsEl.Elements(ns + "group"))
                step.Groups.Add(ParseGroup(groupEl, ns));
        }

        return step;
    }

    private static FomodGroup ParseGroup(XElement groupEl, XNamespace ns)
    {
        var group = new FomodGroup
        {
            Name = (string?)groupEl.Attribute("name") ?? string.Empty,
            Type = ParseGroupType((string?)groupEl.Attribute("type"))
        };

        var pluginsEl = groupEl.Element(ns + "plugins");
        if (pluginsEl != null)
        {
            foreach (var pluginEl in pluginsEl.Elements(ns + "plugin"))
                group.Plugins.Add(ParsePlugin(pluginEl, ns));
        }

        return group;
    }

    private static FomodPlugin ParsePlugin(XElement pluginEl, XNamespace ns)
    {
        var plugin = new FomodPlugin
        {
            Name = (string?)pluginEl.Attribute("name") ?? string.Empty,
            Description = (string?)pluginEl.Element(ns + "description") ?? string.Empty,
            ImagePath = (string?)pluginEl.Element(ns + "image")?.Attribute("path")
        };

        // The schema calls it "conditionFlags"; "conditionalFlags" is a plausible enough misreading
        // that it is worth accepting, and costs one comparison.
        var flagsEl = pluginEl.Element(ns + "conditionFlags") ?? pluginEl.Element(ns + "conditionalFlags");
        if (flagsEl != null)
        {
            foreach (var flagEl in flagsEl.Elements(ns + "flag"))
            {
                var name = (string?)flagEl.Attribute("name");
                if (string.IsNullOrEmpty(name)) continue;
                // Value() and not the element's string, so the designer comment these files carry
                // inside the flag element does not end up as part of the value.
                plugin.ConditionalFlags.Add(new FomodFlagDependency(
                    name, string.Concat(flagEl.Nodes().OfType<XText>().Select(t => t.Value)).Trim()));
            }
        }

        var filesEl = pluginEl.Element(ns + "files");
        if (filesEl != null)
            plugin.Files.AddRange(ParseFileList(filesEl, ns));

        var typeEl = pluginEl.Element(ns + "typeDescriptor")?.Element(ns + "type");
        string? typeName = (string?)typeEl?.Attribute("name");
        plugin.IsDefault = typeName is "Recommended" or "Required";

        return plugin;
    }

    private static IEnumerable<FomodInstallFile> ParseFileList(XElement parent, XNamespace ns)
    {
        foreach (var el in parent.Elements())
        {
            bool isFolder = el.Name.LocalName.Equals("folder", StringComparison.OrdinalIgnoreCase);
            string source = (string?)el.Attribute("source") ?? string.Empty;
            string dest = (string?)el.Attribute("destination") ?? source;
            int priority = (int?)el.Attribute("priority") ?? 0;
            yield return new FomodInstallFile
            {
                Source = source.Replace('\\', '/').TrimStart('/'),
                Destination = dest.Replace('\\', '/').TrimStart('/'),
                IsFolder = isFolder,
                Priority = priority
            };
        }
    }

    private static GroupType ParseGroupType(string? raw) => raw?.Trim() switch
    {
        "SelectAll"          => GroupType.SelectAll,
        "SelectExactlyOne"   => GroupType.SelectExactlyOne,
        "SelectAtLeastOne"   => GroupType.SelectAtLeastOne,
        "SelectAtMostOne"    => GroupType.SelectAtMostOne,
        _                    => GroupType.SelectAny
    };
}
