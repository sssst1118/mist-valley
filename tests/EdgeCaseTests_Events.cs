using System;
using System.Collections.Generic;
using XingGame.Core.Events;

namespace XingGame.Tests;

/// <summary>
/// 事件总线的边界与非法使用（ADR-005）。<see cref="EventBusTests"/> 已守住常规派发与
/// 「退订当轮生效」，这里只补那几条**现有用例没走到的分支**：
/// 重复订阅、多份凭据、派发中改订阅表、异常传播。
/// </summary>
public sealed class EdgeCaseTests_Events
{
    private readonly record struct Tick(int Value);
    private readonly record struct Ping(int Value);

    [Fact]
    public void Subscribe_同一处理器订两次_收到两次()
    {
        // 不去重是刻意的：去重会让「两次订阅、两条退订凭据」变成退一次就全没了，
        // 而桥接层里两个节点共用一个静态回调是合法写法（各自的 _ExitTree 只该退掉自己那一份）。
        // 谁若给 Subscribe 加去重，这条会当场翻脸。
        var bus = new EventBus();
        int calls = 0;
        Action<Tick> handler = _ => calls++;

        bus.Subscribe(handler);
        bus.Subscribe(handler);

        bus.Publish(new Tick(1));

        Assert.Equal(2, calls);
    }

    [Fact]
    public void Dispose_只退掉手上这份凭据_另一份照常收()
    {
        // 两份凭据彼此独立。若有一天把 Subscription 改成按 (类型, 处理器) 复用，
        // 退掉一份就会顺手掐掉另一份——正是上一条要防的同一件事，从另一个方向守。
        var bus = new EventBus();
        int calls = 0;
        Action<Tick> handler = _ => calls++;

        IDisposable first = bus.Subscribe(handler);
        bus.Subscribe(handler);

        first.Dispose();
        bus.Publish(new Tick(1));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void 派发中退掉自己并另订一个_退掉的当轮失效_新订的下轮才生效()
    {
        // 快照在 Publish 开始时取定，且**不复读**订阅表：本轮新订的进不了本轮快照。
        // 两个方向都要钉住——退掉的那个当轮立刻失效（ADR-005），新订的当轮收不到（快照不重建）。
        var bus = new EventBus();
        int selfCalls = 0;
        int bystanderCalls = 0;
        int lateCalls = 0;
        bool replaced = false;
        IDisposable? self = null;

        self = bus.Subscribe<Tick>(_ =>
        {
            selfCalls++;
            if (replaced) return;

            replaced = true;
            self!.Dispose();                        // 退掉自己
            bus.Subscribe<Tick>(_ => lateCalls++);  // 同时订一个新的
        });
        bus.Subscribe<Tick>(_ => bystanderCalls++);

        bus.Publish(new Tick(1));

        Assert.Equal(1, selfCalls);         // 本轮只调了退掉前的那一次
        Assert.Equal(1, bystanderCalls);
        Assert.Equal(0, lateCalls);         // 新订的不在本轮快照里

        bus.Publish(new Tick(2));

        Assert.Equal(1, selfCalls);         // 已退订，下一轮起不再收
        Assert.Equal(2, bystanderCalls);
        Assert.Equal(1, lateCalls);         // 新凭据下一轮起生效
    }

    [Fact]
    public void 派发中订阅同类型_内层派发就能收到_外层本轮收不到()
    {
        // 「快照是每次 Publish 各取一份」的直接后果：内层派发取的是**当下**的订阅表，
        // 于是中途加入的订阅者能在内层收到事件，却收不到正在跑的外层。
        // 这不是 bug 而是快照语义的推论；谁把快照改成按类型缓存复用，这里就会变。
        var bus = new EventBus();
        var log = new List<string>();
        bool subscribed = false;

        bus.Subscribe<Tick>(evt =>
        {
            if (subscribed)
            {
                log.Add($"a-again:{evt.Value}");
                return;
            }

            subscribed = true;
            log.Add($"a:{evt.Value}");

            bus.Subscribe<Tick>(late => log.Add($"late:{late.Value}"));
            bus.Publish(new Tick(2));       // 同类型重入
        });
        bus.Subscribe<Tick>(evt => log.Add($"b:{evt.Value}"));

        bus.Publish(new Tick(1));

        // 内层拿到的是含 late 的新快照，外层接着跑自己那份旧快照
        Assert.Equal(new[] { "a:1", "a-again:2", "b:2", "late:2", "b:1" }, log);
    }

    [Fact]
    public void 处理函数抛异常_原样抛给调用方_且不吞掉后续订阅者()
    {
        // 总线不包 try/catch：吞掉异常等于让「派发只跑了一半」变成静默事实，
        // 而调用方（时间系统的 TickOneMinute）根本不知道这一轮少了几个订阅者。
        // 抛出去至少是在离病因最近的地方炸。
        var bus = new EventBus();
        var reached = new List<string>();

        bus.Subscribe<Tick>(_ => throw new InvalidOperationException("订阅者炸了（测试用）"));
        bus.Subscribe<Tick>(_ => reached.Add("第二个"));

        var error = Assert.Throws<InvalidOperationException>(() => bus.Publish(new Tick(1)));

        Assert.Equal("订阅者炸了（测试用）", error.Message);
        Assert.Empty(reached);   // 排在其后的订阅者本轮被跳过：异常不该被吞

        // 抛异常不等于订阅表被弄脏：下一轮照样能派发
        bus.Publish(new Ping(1));
    }
}
