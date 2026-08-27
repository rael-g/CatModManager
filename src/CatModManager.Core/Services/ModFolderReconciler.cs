using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatModManager.Core.Models;

namespace CatModManager.Core.Services;

/// <summary>
/// Works out what changed between the mod list a profile remembers and what is actually on disk.
///
/// The profile is the source of truth for everything the user decided — order, enabled state,
/// category, mount point — and the folder is the source of truth for what exists. So a mod present
/// in both is kept untouched: re-reading it from disk would silently throw away those decisions.
/// Only the difference is applied.
/// </summary>
public static class ModFolderReconciler
{
    /// <param name="existing">The current list, highest priority first.</param>
    /// <param name="scanned">What the scanner found, in whatever order it found it.</param>
    public static ModReconcileResult Reconcile(IReadOnlyList<Mod> existing, IReadOnlyList<Mod> scanned)
    {
        var onDisk = new HashSet<string>(scanned.Select(m => Key(m.ModRootPath)), StringComparer.Ordinal);
        var known  = new HashSet<string>(existing.Select(m => Key(m.ModRootPath)), StringComparer.Ordinal);

        // A mod still being unpacked has no folder yet — dropping it would delete a row from under an
        // install in progress and orphan its cancel button.
        var removed = existing.Where(m => !m.IsInstalling && !onDisk.Contains(Key(m.ModRootPath))).ToList();

        // Appended, so a newly discovered mod lands at the lowest priority and loses every conflict.
        // Guessing it should win would silently change what the game loads.
        var added = scanned.Where(m => !known.Contains(Key(m.ModRootPath)))
                           .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                           .ToList();

        var removedSet = new HashSet<Mod>(removed);
        var kept = existing.Where(m => !removedSet.Contains(m)).ToList();

        foreach (var mod in added)
        {
            // Off by default: an unannounced mod turning itself on is the kind of surprise that
            // breaks a working load order.
            mod.IsEnabled = false;
            kept.Add(mod);
        }

        return new ModReconcileResult(kept, added, removed);
    }

    /// <summary>
    /// Paths compared as text, case-sensitively. This runs on Linux, where two paths differing only
    /// in case are two different folders — folding them would merge unrelated mods into one.
    /// </summary>
    private static string Key(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return path; }
    }
}

/// <param name="Mods">The reconciled list, highest priority first. Priorities are not renumbered.</param>
/// <param name="Added">Newly discovered mods, already appended to <paramref name="Mods"/>.</param>
/// <param name="Removed">Mods whose folder is gone, already absent from <paramref name="Mods"/>.</param>
public readonly record struct ModReconcileResult(
    IReadOnlyList<Mod> Mods,
    IReadOnlyList<Mod> Added,
    IReadOnlyList<Mod> Removed);
