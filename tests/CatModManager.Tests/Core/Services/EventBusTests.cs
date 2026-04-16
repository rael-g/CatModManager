using System;
using Xunit;
using CatModManager.Core.Services;

namespace CatModManager.Tests.Core.Services;

public class EventBusTests
{
    private class TestEvent { public string Message { get; set; } = ""; }

    [Fact]
    public void Subscribe_And_Publish_Should_Invoke_Handler()
    {
        var bus = new EventBus();
        string received = "";
        bus.Subscribe<TestEvent>(e => received = e.Message);

        bus.Publish(new TestEvent { Message = "Hello" });

        Assert.Equal("Hello", received);
    }

    [Fact]
    public void Unsubscribe_Should_Stop_Invoking_Handler()
    {
        var bus = new EventBus();
        int count = 0;
        Action<TestEvent> handler = e => count++;
        
        bus.Subscribe(handler);
        bus.Publish(new TestEvent());
        Assert.Equal(1, count);

        bus.Unsubscribe(handler);
        bus.Publish(new TestEvent());
        Assert.Equal(1, count);
    }

    [Fact]
    public void Publish_Without_Subscribers_Should_Not_Throw()
    {
        var bus = new EventBus();
        var ex = Record.Exception(() => bus.Publish(new TestEvent()));
        Assert.Null(ex);
    }
}
