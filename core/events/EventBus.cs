using System;
using System.Collections.Generic;

namespace XingGame.Core.Events;

/// <summary>
/// 类型键控的同步事件总线（ADR-005）。<see cref="Publish{T}"/> 立即调用订阅者，不排队、不延迟。
/// </summary>
/// <remarks>
/// 四条约定及其理由：
/// <list type="number">
///   <item>事件类型一律用 <c>readonly record struct</c> —— 时间/天气事件可能每帧都发，值类型不给 GC 添压力。</item>
///   <item>按<b>声明的事件类型</b>精确匹配，不做向上转型派发 —— 订阅 <c>Base</c> 收不到 <c>Derived</c>，
///         免得一个事件引发意料之外的瀑布式回调；跨层通信用各自领域的事件类型显式订阅。</item>
///   <item><b>退订当轮生效</b>：派发每个订阅者之前先查它是否仍在订阅状态，已退订的跳过。
///         桥接层节点的习惯写法是在 <c>_ExitTree()</c> 里 Dispose 订阅，而一轮派发中靠前的回调完全可能
///         让某个节点离树 —— 若还照着快照把本轮剩下的回调走完，排在后面的订阅者就会去碰一个正在销毁的
///         Godot 对象。stale reference 是 Godot 里的经典崩溃源，事后极难定位；
///         相比之下「本轮少收一次事件」是各系统都能容忍的偏差。别改回「本轮发完」。</item>
///   <item>只支持单线程，故不加锁 —— 游戏循环本身是单线程，且订阅者会在派发中改订阅表，
///         锁既换不来正确性又要给每帧派发付同步开销。真需要跨线程投递，就先排队、回主线程再 Publish。</item>
/// </list>
/// </remarks>
public sealed class EventBus : IEventBus
{
    // 订阅表就地增删；派发只取一次快照，不重建表（派发是热路径）
    private readonly Dictionary<Type, List<Subscription>> _handlers = new();

    public void Publish<T>(in T evt)
    {
        if (!_handlers.TryGetValue(typeof(T), out var subscriptions)) return;

        // 遍历副本：订阅者可能在派发中订阅/退订（含退订自己），那会改到原表（ADR-005）
        var snapshot = subscriptions.ToArray();
        foreach (var subscription in snapshot)
        {
            // 快照只保证遍历不炸；要不要调用得看此刻的订阅状态（见类注释第 3 条）
            if (!subscription.IsActive) continue;

            ((Action<T>)subscription.Callback)(evt);
        }
    }

    public IDisposable Subscribe<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!_handlers.TryGetValue(typeof(T), out var subscriptions))
        {
            subscriptions = new List<Subscription>();
            _handlers[typeof(T)] = subscriptions;
        }

        var subscription = new Subscription(this, typeof(T), handler);
        subscriptions.Add(subscription);
        return subscription;
    }

    private void Unsubscribe(Type eventType, Subscription subscription)
    {
        if (!_handlers.TryGetValue(eventType, out var subscriptions)) return;

        subscriptions.Remove(subscription);

        // 空表不留在字典里：UI 面板这类反复订阅/退订的订阅者不该让字典无限长大
        if (subscriptions.Count == 0) _handlers.Remove(eventType);
    }

    /// <summary>
    /// 一次订阅的凭据，同时也是订阅表里的条目 —— 派发前查 <see cref="IsActive"/> 才知道该不该回调。
    /// </summary>
    private sealed class Subscription : IDisposable
    {
        private readonly EventBus _bus;
        private readonly Type _eventType;

        public Subscription(EventBus bus, Type eventType, Delegate callback)
        {
            _bus = bus;
            _eventType = eventType;
            Callback = callback;
        }

        public Delegate Callback { get; }

        public bool IsActive { get; private set; } = true;

        public void Dispose()
        {
            // 幂等：重复退订按「已退订」处理，不该让调用方去记自己退过没有
            if (!IsActive) return;

            IsActive = false;
            _bus.Unsubscribe(_eventType, this);
        }
    }
}
