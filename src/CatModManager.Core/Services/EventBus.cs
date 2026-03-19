using System;
using System.Collections.Generic;

namespace CatModManager.Core.Services;

public class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public void Publish<T>(T eventData)
    {
        if (!_handlers.TryGetValue(typeof(T), out var list)) return;
        foreach (var handler in list)
            ((Action<T>)handler)(eventData!);
    }

    public void Subscribe<T>(Action<T> handler)
    {
        if (!_handlers.TryGetValue(typeof(T), out var list))
        {
            list = new List<Delegate>();
            _handlers[typeof(T)] = list;
        }
        list.Add(handler);
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        if (_handlers.TryGetValue(typeof(T), out var list))
            list.Remove(handler);
    }
}
