using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Tests.Support;

/// <summary>Global tools in a list, the counterpart to <see cref="FakeGameService"/>.</summary>
public sealed class FakeGlobalToolService : IGlobalToolService
{
    private List<ExternalTool> _tools = new();

    public Task<List<ExternalTool>> ListToolsAsync()
        => Task.FromResult(_tools.ToList());

    public Task SaveToolsAsync(IReadOnlyList<ExternalTool> tools)
    {
        _tools = tools.ToList();
        return Task.CompletedTask;
    }

    /// <summary>What the last save wrote, for asserting on without going through the async path.</summary>
    public IReadOnlyList<ExternalTool> Saved => _tools;
}
