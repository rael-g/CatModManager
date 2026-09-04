using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Services;
using CatModManager.Tests.Support;
using CatModManager.Ui.ViewModels;
using NSubstitute;
using Xunit;

namespace CatModManager.Tests.Ui.ViewModels;

/// <summary>
/// One list on screen, two tables underneath. These pin the seam: which half of the list goes where
/// when it is written, and what the list looks like after switching game.
/// </summary>
public class GlobalToolsTests
{
    private readonly FakeGlobalToolService _globals = new();
    private readonly ExternalToolsViewModel _vm;

    public GlobalToolsTests()
    {
        _vm = new ExternalToolsViewModel(
            Substitute.For<IProcessService>(),
            Substitute.For<IVfsOrchestrationService>(),
            new MockLogService(),
            _globals);
    }

    private static ExternalTool Tool(string name, bool global = false)
        => new() { Name = name, ExecutablePath = "/bin/" + name, IsGlobal = global };

    [Fact]
    public async Task TheGlobalToolsShowUpAfterTheGameOwnOnes()
    {
        await _globals.SaveToolsAsync([Tool("7zip", global: true)]);
        await _vm.InitializeAsync();

        _vm.LoadTools([Tool("xEdit")]);

        Assert.Equal(["xEdit", "7zip"], _vm.Tools.Select(t => t.Name));
    }

    [Fact]
    public async Task OnlyTheGameOwnToolsAreHandedBackToTheGame()
    {
        await _globals.SaveToolsAsync([Tool("7zip", global: true)]);
        await _vm.InitializeAsync();

        _vm.LoadTools([Tool("xEdit")]);

        Assert.Equal(["xEdit"], _vm.GetTools().Select(t => t.Name));
    }

    /// <summary>
    /// Ticking the checkbox in the editor is the only way a tool moves between the two tables, and
    /// it has to move all the way — staying in both is how the same tool shows up twice.
    /// </summary>
    [Fact]
    public async Task TickingGlobalMovesTheToolOutOfTheGame()
    {
        await _vm.InitializeAsync();
        _vm.LoadTools([Tool("xEdit")]);

        _vm.Tools[0].IsGlobal = true;
        _vm.NotifyEdited();

        Assert.Empty(_vm.GetTools());
        Assert.Equal(["xEdit"], _globals.Saved.Select(t => t.Name));
    }

    /// <summary>
    /// Switching game rebuilds the list from the new game's tools. The global ones have to survive
    /// that, which is the entire point of them.
    /// </summary>
    [Fact]
    public async Task SwitchingGameKeepsTheGlobalTools()
    {
        await _globals.SaveToolsAsync([Tool("7zip", global: true)]);
        await _vm.InitializeAsync();

        _vm.LoadTools([Tool("xEdit")]);
        _vm.LoadTools([Tool("Cathedral Assets")]);

        Assert.Equal(["Cathedral Assets", "7zip"], _vm.Tools.Select(t => t.Name));
    }
}
