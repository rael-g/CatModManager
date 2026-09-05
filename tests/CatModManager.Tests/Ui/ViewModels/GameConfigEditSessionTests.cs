using System.Linq;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Core.Services.GameDiscovery;
using CatModManager.Ui.ViewModels;
using NSubstitute;
using Xunit;

namespace CatModManager.Tests.Ui.ViewModels;

/// <summary>
/// The settings dialog is an editing session now: saving is held off while it is open, and the
/// values are either committed or put back. These cover the two halves that make a Cancel honest —
/// that nothing reaches the game while editing, and that everything goes back afterwards.
/// </summary>
public class GameConfigEditSessionTests
{
    private readonly GameConfigViewModel _vm;
    private int _saves;

    public GameConfigEditSessionTests()
    {
        var supports = Substitute.For<IGameSupportService>();
        var generic  = Substitute.For<IGameSupport>();
        generic.GameId.Returns("generic");
        generic.GameDefinedMountPoints.Returns(new MountPointDef[0]);
        supports.Default.Returns(generic);
        supports.DetectSupport(Arg.Any<string>()).Returns(generic);
        supports.GetAllSupports().Returns(new[] { generic });

        _vm = new GameConfigViewModel(supports, Substitute.For<IGameDiscoveryService>(),
                                      Substitute.For<ILogService>())
        {
            SaveGame = () => _saves++
        };

        _vm.BaseFolderPath      = "/games/Skyrim";
        _vm.ModsFolderPath      = "/games/Skyrim/cmm/mods";
        _vm.LaunchArguments     = "-applaunch 489830";
        _vm.GameExecutablePath  = "steam";
        _vm.UserMountPoints.Add(new MountPointDef { Id = "data", Name = "Data", Path = "Data" });
        _saves = 0;
    }

    /// <summary>
    /// The reason the suppression exists at all: filling the panel in is a dozen assignments, and
    /// each one used to write the whole thing back — including the fields not assigned yet. That is
    /// how a real launch line was overwritten with an empty one.
    /// </summary>
    [Fact]
    public void NothingIsWrittenWhileTheSessionIsOpen()
    {
        using (_vm.SuppressSaving())
        {
            _vm.LaunchArguments = "";
            _vm.ModsFolderPath  = "/somewhere/else";
            _vm.BaseFolderPath  = "/elsewhere";
        }

        Assert.Equal(0, _saves);
    }

    [Fact]
    public void CancellingPutsEveryFieldBack()
    {
        var opened = _vm.TakeSnapshot();

        using (_vm.SuppressSaving())
        {
            _vm.LaunchArguments    = "";
            _vm.ModsFolderPath     = "/somewhere/else";
            _vm.GameExecutablePath = "/other/game.exe";
            _vm.Restore(opened);
        }

        Assert.Equal("-applaunch 489830", _vm.LaunchArguments);
        Assert.Equal("/games/Skyrim/cmm/mods", _vm.ModsFolderPath);
        Assert.Equal("steam", _vm.GameExecutablePath);
        Assert.Equal(0, _saves);
    }

    /// <summary>
    /// The mount points are edited in place by the dialog, so a snapshot holding the same instances
    /// would restore the edited objects to themselves — a Cancel that silently keeps the change.
    /// </summary>
    [Fact]
    public void CancellingUndoesAMountPointEditedInPlace()
    {
        var opened = _vm.TakeSnapshot();

        using (_vm.SuppressSaving())
        {
            _vm.UserMountPoints[0].Path = "Data/Broken";
            _vm.Restore(opened);
        }

        Assert.Equal("Data", _vm.UserMountPoints.Single().Path);
    }

    [Fact]
    public void CancellingUndoesAnAddedMountPoint()
    {
        var opened = _vm.TakeSnapshot();

        using (_vm.SuppressSaving())
        {
            _vm.UserMountPoints.Add(new MountPointDef { Id = "x", Name = "X", Path = "X" });
            _vm.Restore(opened);
        }

        Assert.Single(_vm.UserMountPoints);
    }

    /// <summary>Saving is one write at the end, not one per field touched.</summary>
    [Fact]
    public void CommittingWritesOnce()
    {
        using (_vm.SuppressSaving())
        {
            _vm.LaunchArguments = "-windowed";
            _vm.ModsFolderPath  = "/games/Skyrim/mods2";
        }

        _vm.SaveGame!.Invoke();

        Assert.Equal(1, _saves);
        Assert.Equal("-windowed", _vm.LaunchArguments);
    }
}
