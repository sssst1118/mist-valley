using System;

namespace XingGame.Core.Events;

/// <summary>同步事件总线（ARCHITECTURE.md ADR-005）。</summary>
public interface IEventBus
{
    void Publish<T>(in T evt);

    /// <summary>返回的 <see cref="IDisposable"/> 即退订凭据，Dispose 一次即退订。</summary>
    IDisposable Subscribe<T>(Action<T> handler);
}
