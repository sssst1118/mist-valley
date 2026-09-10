using System;
using System.Collections.Generic;
using XingGame.Core.Events;

namespace XingGame.Tests;

public class EventBusTests
{
    private readonly record struct Tick(int Value);
    private readonly record struct Ping(string Text);

    [Fact]
    public void Publish_DeliversEventArgsToSubscriber()
    {
        var bus = new EventBus();
        var received = new List<Tick>();

        bus.Subscribe<Tick>(evt => received.Add(evt));
        bus.Publish(new Tick(42));

        var only = Assert.Single(received);
        Assert.Equal(42, only.Value);
    }

    [Fact]
    public void Publish_InvokesEverySubscriberInSubscriptionOrder()
    {
        var bus = new EventBus();
        var calls = new List<string>();

        bus.Subscribe<Tick>(_ => calls.Add("first"));
        bus.Subscribe<Tick>(_ => calls.Add("second"));
        bus.Subscribe<Tick>(_ => calls.Add("third"));
        bus.Publish(new Tick(1));

        Assert.Equal(new[] { "first", "second", "third" }, calls);
    }

    [Fact]
    public void Publish_ReachesOnlySubscribersOfThatEventType()
    {
        var bus = new EventBus();
        var pings = 0;

        bus.Subscribe<Ping>(_ => pings++);
        bus.Publish(new Tick(1));

        Assert.Equal(0, pings);

        bus.Publish(new Ping("hello"));

        Assert.Equal(1, pings);
    }

    [Fact]
    public void Dispose_StopsDelivery()
    {
        var bus = new EventBus();
        var calls = 0;
        var subscription = bus.Subscribe<Tick>(_ => calls++);

        bus.Publish(new Tick(1));
        subscription.Dispose();
        bus.Publish(new Tick(2));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var bus = new EventBus();
        var subscription = bus.Subscribe<Tick>(_ => { });

        subscription.Dispose();

        Assert.Null(Record.Exception(() => subscription.Dispose()));
    }

    [Fact]
    public void UnsubscribeSelfDuringPublish_DoesNotThrow()
    {
        var bus = new EventBus();
        IDisposable? self = null;
        var firstCalls = 0;
        var secondCalls = 0;

        self = bus.Subscribe<Tick>(_ =>
        {
            firstCalls++;
            self!.Dispose();
        });
        bus.Subscribe<Tick>(_ => secondCalls++);

        // 遍历副本，故本轮退订改的是原表、遍历本身不受影响
        Assert.Null(Record.Exception(() => bus.Publish(new Tick(1))));
        Assert.Equal(1, firstCalls);
        Assert.Equal(1, secondCalls);

        bus.Publish(new Tick(2));

        Assert.Equal(1, firstCalls);   // 退订后不再收到
        Assert.Equal(2, secondCalls);
    }

    [Fact]
    public void UnsubscribeLaterSubscriberDuringPublish_SkipsItInCurrentDispatch()
    {
        var bus = new EventBus();
        IDisposable? later = null;
        var firstCalls = 0;
        var secondCalls = 0;

        // A 在派发中退订排在它后面的 B：B 本轮就不该再被回调。
        // 桥接层里 A 的回调很可能让 B 所在节点离树，再回调 B 就是去碰一个正在销毁的 Godot 对象。
        bus.Subscribe<Tick>(_ =>
        {
            firstCalls++;
            later!.Dispose();
        });
        later = bus.Subscribe<Tick>(_ => secondCalls++);

        Assert.Null(Record.Exception(() => bus.Publish(new Tick(1))));
        Assert.Equal(1, firstCalls);
        Assert.Equal(0, secondCalls);   // 退订当轮生效

        bus.Publish(new Tick(2));

        Assert.Equal(2, firstCalls);
        Assert.Equal(0, secondCalls);
    }

    [Fact]
    public void UnsubscribeAlreadyInvokedSubscriberDuringPublish_DoesNotThrow()
    {
        var bus = new EventBus();
        IDisposable? first = null;
        var firstCalls = 0;
        var secondCalls = 0;

        first = bus.Subscribe<Tick>(_ => firstCalls++);
        bus.Subscribe<Tick>(_ =>
        {
            secondCalls++;
            first!.Dispose();   // 退订一个本轮已经跑过的订阅者
        });

        Assert.Null(Record.Exception(() => bus.Publish(new Tick(1))));
        Assert.Equal(1, firstCalls);
        Assert.Equal(1, secondCalls);

        bus.Publish(new Tick(2));

        Assert.Equal(1, firstCalls);
        Assert.Equal(2, secondCalls);
    }

    [Fact]
    public void SubscribeDuringPublish_DoesNotReceiveCurrentEvent()
    {
        var bus = new EventBus();
        var firstCalls = 0;
        var lateCalls = 0;
        var subscribed = false;

        // 快照在派发开始时取定：中途加入的订阅者本轮不参与，下一轮起正常收
        bus.Subscribe<Tick>(_ =>
        {
            firstCalls++;
            if (subscribed) return;

            subscribed = true;
            bus.Subscribe<Tick>(_ => lateCalls++);
        });

        bus.Publish(new Tick(1));

        Assert.Equal(1, firstCalls);
        Assert.Equal(0, lateCalls);

        bus.Publish(new Tick(2));

        Assert.Equal(2, firstCalls);
        Assert.Equal(1, lateCalls);
    }

    [Fact]
    public void PublishInsideHandler_ReentrantDispatch_DeliversBothEvents()
    {
        var bus = new EventBus();
        var log = new List<string>();

        // 处理函数里再发事件是常见形状：DayStarted 的处理很可能立刻触发 WeatherChanged
        bus.Subscribe<Ping>(evt => log.Add($"ping:{evt.Text}"));
        bus.Subscribe<Tick>(evt =>
        {
            log.Add($"tick-a:{evt.Value}");
            bus.Publish(new Ping("nested"));
        });
        bus.Subscribe<Tick>(evt => log.Add($"tick-b:{evt.Value}"));

        Assert.Null(Record.Exception(() => bus.Publish(new Tick(1))));

        // 内层派发先跑完，外层再接着走 —— 每次 Publish 各持一份快照，互不侵占
        Assert.Equal(new[] { "tick-a:1", "ping:nested", "tick-b:1" }, log);
    }

    [Fact]
    public void PublishSameEventTypeInsideHandler_KeepsIterationIntact()
    {
        var bus = new EventBus();
        var log = new List<string>();

        // 同类型重入：快照若改成按类型复用的缓冲区，内层会就地覆盖外层正在遍历的那一份
        bus.Subscribe<Tick>(evt =>
        {
            log.Add($"a:{evt.Value}");
            if (evt.Value < 3) bus.Publish(new Tick(evt.Value + 1));
        });
        bus.Subscribe<Tick>(evt => log.Add($"b:{evt.Value}"));

        Assert.Null(Record.Exception(() => bus.Publish(new Tick(1))));

        Assert.Equal(new[] { "a:1", "a:2", "a:3", "b:3", "b:2", "b:1" }, log);
    }

    [Fact]
    public void UnsubscribeOuterSubscriberFromInnerDispatch_SkipsItInBothDispatches()
    {
        var bus = new EventBus();
        var log = new List<string>();
        IDisposable? outerVictim = null;
        IDisposable? innerVictim = null;

        // B 同时订阅了外层 Tick 与内层 Ping；C 在内层派发中把 B 两边的订阅一起退掉
        bus.Subscribe<Ping>(_ =>
        {
            log.Add("c");
            outerVictim!.Dispose();
            innerVictim!.Dispose();
        });
        innerVictim = bus.Subscribe<Ping>(_ => log.Add("b:ping"));
        bus.Subscribe<Ping>(_ => log.Add("d"));

        bus.Subscribe<Tick>(_ =>
        {
            log.Add("a");
            bus.Publish(new Ping("nested"));
        });
        outerVictim = bus.Subscribe<Tick>(_ => log.Add("b:tick"));

        Assert.Null(Record.Exception(() => bus.Publish(new Tick(1))));

        // b:ping 排在 c 之后 → 内层当轮跳过；内层返回外层后 b:tick 已失效 → 外层当轮也跳过
        Assert.Equal(new[] { "a", "c", "d" }, log);
    }

    [Fact]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        var bus = new EventBus();

        Assert.Null(Record.Exception(() => bus.Publish(new Tick(1))));
    }

    [Fact]
    public void Subscribe_AfterAllSubscribersUnsubscribed_StillDelivers()
    {
        var bus = new EventBus();
        var calls = 0;

        bus.Subscribe<Tick>(_ => calls++).Dispose();
        bus.Subscribe<Tick>(_ => calls++);
        bus.Publish(new Tick(1));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Subscribe_WithNullHandler_Throws()
    {
        var bus = new EventBus();

        Assert.Throws<ArgumentNullException>(() => bus.Subscribe<Tick>(null!));
    }
}
