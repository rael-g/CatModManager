using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CmmPlugin.FomodInstaller.Models;
using SharpCompress.Archives;

namespace CmmPlugin.FomodInstaller.Parser;

public static class FomodParser
{
    private const string ConfigPath = "fomod/ModuleConfig.xml";

    private static bool IsConfigEntry(string? key) =>
        key != null &&
        (key.Replace('\\', '/').Equals(ConfigPath, StringComparison.OrdinalIgnoreCase) ||
         key.Replace('\\', '/').EndsWith("/" + ConfigPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns true if the archive contains a FOMOD ModuleConfig.xml (at root or inside a wrapper folder).</summary>
    public static bool IsFomod(string archivePath)
    {
        if (!File.Exists(archivePath)) return false;
        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            return archive.Entries.Any(e => !e.IsDirectory && IsConfigEntry(e.Key));
        }
        catch { return false; }
    }

    /// <summary>Parses ModuleConfig.xml from the archive and returns the config model.</summary>
    public static FomodModuleConfig Parse(string archivePath)
    {
        using var archive = ArchiveFactory.Open(archivePath);
        var configEntry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && IsConfigEntry(e.Key))
            ?? throw new InvalidOperationException("ModuleConfig.xml not found in archive.");

        using var stream = configEntry.OpenEntryStream();
        var doc = XDocument.Load(stream);
        return ParseDocument(doc);
    }

    private static FomodModuleConfig ParseDocument(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidOperationException("Empty FOMOD XML.");
        // FOMOD XML may or may not have a namespace
        var ns = root.GetDefaultNamespace();

        var config = new FomodModuleConfig
        {
            ModuleName = (string?)root.Element(ns + "moduleName") ?? string.Empty
        };

        // Required install files
        var reqFiles = root.Element(ns + "requiredInstallFiles");
        if (reqFiles != null)
            config.RequiredInstallFiles.AddRange(ParseFileList(reqFiles, ns));

        // Install steps
        var stepsEl = root.Element(ns + "installSteps");
        if (stepsEl != null)
        {
            foreach (var stepEl in stepsEl.Elements(ns + "installStep"))
                config.InstallSteps.Add(ParseStep(stepEl, ns));
        }

        return config;
    }

    private static FomodInstallStep ParseStep(XElement stepEl, XNamespace ns)
    {
        var step = new FomodInstallStep
        {
            Name = (string?)stepEl.Attribute("name") ?? string.Empty
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

        var filesEl = pluginEl.Element(ns + "files");
        if (filesEl != null)
            plugin.Files.AddRange(ParseFileList(filesEl, ns));

        // Type descriptor: "Recommended" or "Required" = IsDefault true
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
