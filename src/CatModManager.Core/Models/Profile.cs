using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace CatModManager.Core.Models;

/// <summary>
/// An external tool, launched exactly the way the game is: a command plus arguments.
///
/// Observable because the Tools tab now edits these in place — without change notification the
/// list entry keeps showing the old name and path until the profile is reloaded.
/// </summary>
public partial class ExternalTool : ObservableObject
{
    [ObservableProperty] private string _name = "";

    /// <summary>
    /// A path to an executable, or a bare command resolved through PATH.
    ///
    /// The bare-command form is the point on Linux, where a tool usually has to be started through
    /// something else: "steam" with "steam://rungameid/..." to reach a non-Steam shortcut's Proton
    /// prefix, or a wine invocation for anything Steam does not know about. The same trick the
    /// game's own launch line already relies on.
    /// </summary>
    [ObservableProperty] private string _executablePath = "";

    [ObservableProperty] private string _arguments = "";
    [ObservableProperty] private bool   _mountBeforeLaunch;
}

/// <summary>
/// One arrangement of a game's mods: which are on, in what order, and where each is mounted.
///
/// The paths that used to live here are on <see cref="Game"/> now. What is left is only what can
/// differ between two profiles of the same installation.
/// </summary>
public class Profile
{
    /// <summary>Zero for a profile that has not been saved yet.</summary>
    public long Id { get; set; }

    /// <summary>
    /// The game this profile arranges, or null for a profile parked without one. Unique together
    /// with <see cref="Name"/>, so every game gets to have its own "Default".
    /// </summary>
    public long? GameId { get; set; }

    public string Name { get; set; } = "Default";

    public List<Mod> Mods { get; set; } = new();
}


