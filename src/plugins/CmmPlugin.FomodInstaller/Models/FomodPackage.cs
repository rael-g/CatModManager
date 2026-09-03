using System.Collections.Generic;

namespace CmmPlugin.FomodInstaller.Models;

/// <summary>
/// Everything one read of the archive yields: the parsed config, and the preview images already in
/// memory. They travel together because in a solid archive they cost the same as the config alone —
/// see <c>FomodParser.Read</c>.
/// </summary>
/// <param name="Previews">
/// Raw image bytes, keyed by the archive entry path normalized through
/// <c>FomodParser.NormalizeKey</c>. Bytes rather than Bitmaps so the parser stays free of UI types
/// and the decode happens on whichever thread the wizard wants it on.
/// </param>
public sealed record FomodPackage(
    FomodModuleConfig Config,
    IReadOnlyDictionary<string, byte[]> Previews);
