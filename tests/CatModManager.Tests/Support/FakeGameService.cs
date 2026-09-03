using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CatModManager.Core.Models;
using CatModManager.Core.Services;

namespace CatModManager.Tests.Support;

/// <summary>Games in a dictionary, the counterpart to <see cref="FakeProfileService"/>.</summary>
public sealed class FakeGameService : IGameService
{
    private readonly Dictionary<long, Game> _games = new();
    private long _nextId = 1;

    public Task<IReadOnlyList<Game>> ListGamesAsync()
        => Task.FromResult((IReadOnlyList<Game>)_games.Values.OrderBy(g => g.DisplayName).ToList());

    public Task<Game?> LoadGameAsync(long gameId)
        => Task.FromResult(_games.TryGetValue(gameId, out var g) ? g : null);

    public Task<Game?> FindByBasePathAsync(string baseDataPath)
        => Task.FromResult(string.IsNullOrWhiteSpace(baseDataPath)
            ? null
            : _games.Values.FirstOrDefault(g => g.BaseDataPath == baseDataPath));

    public Task<long> SaveGameAsync(Game game)
    {
        if (game.Id == 0) game.Id = _nextId++;
        _games[game.Id] = game;
        return Task.FromResult(game.Id);
    }

    public Task DeleteGameAsync(long gameId)
    {
        _games.Remove(gameId);
        return Task.CompletedTask;
    }
}
