using System;
using XingGame.Core;

namespace XingGame.Tests;

public class ServiceRegistryTests
{
    private interface IClock
    {
        int Hour { get; }
    }

    private sealed class Clock : IClock
    {
        public int Hour => 7;
    }

    private sealed class LateClock : IClock
    {
        public int Hour => 21;
    }

    [Fact]
    public void Register_ThenGet_ReturnsSameInstance()
    {
        var registry = new ServiceRegistry();
        var clock = new Clock();

        registry.Register<IClock>(clock);

        Assert.Same(clock, registry.Get<IClock>());
    }

    [Fact]
    public void TryGet_WhenRegistered_ReturnsTrueAndInstance()
    {
        var registry = new ServiceRegistry();
        var clock = new Clock();

        registry.Register<IClock>(clock);

        Assert.True(registry.TryGet<IClock>(out var service));
        Assert.Same(clock, service);
    }

    [Fact]
    public void Get_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        var registry = new ServiceRegistry();

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Get<IClock>());

        Assert.Contains(nameof(IClock), exception.Message);
    }

    [Fact]
    public void TryGet_WhenNotRegistered_ReturnsFalseAndNull()
    {
        var registry = new ServiceRegistry();

        Assert.False(registry.TryGet<IClock>(out var service));
        Assert.Null(service);
    }

    [Fact]
    public void Register_SameTypeTwice_LastRegistrationWins()
    {
        var registry = new ServiceRegistry();

        registry.Register<IClock>(new Clock());
        registry.Register<IClock>(new LateClock());

        Assert.IsType<LateClock>(registry.Get<IClock>());
    }

    [Fact]
    public void Register_KeysByDeclaredTypeArgument_NotRuntimeType()
    {
        var registry = new ServiceRegistry();

        registry.Register(new Clock());   // T 推断为 Clock，而非 IClock

        Assert.True(registry.TryGet<Clock>(out _));
        Assert.False(registry.TryGet<IClock>(out _));
    }

    [Fact]
    public void Register_Null_ThrowsArgumentNullException()
    {
        var registry = new ServiceRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register<IClock>(null!));
    }
}
