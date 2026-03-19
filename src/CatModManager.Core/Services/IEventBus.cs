using System;

namespace CatModManager.Core.Services;

public interface IEventBus
{
    void Publish<T>(T eventData);
    void Subscribe<T>(Action<T> handler);
    void Unsubscribe<T>(Action<T> handler);
}
