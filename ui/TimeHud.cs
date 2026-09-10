using System;
using System.Collections.Generic;
using Godot;
using XingGame.Core.Events;
using XingGame.Core.Time;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 时间 HUD。文本只由订阅到的<b>事件</b>驱动更新，不逐帧读 <c>Now</c>——这条链路存在的意义就是
/// 证明 EventBus 端到端真的通了。除把枚举译成中文外不含任何逻辑（ADR-007）。
/// </summary>
public partial class TimeHud : Control
{
    private readonly List<IDisposable> _subscriptions = new();

    private Label _label = null!;

    // 事件只带自己那部分新值（天气事件不带时间，日界事件不带天气），故各自记住最后看到的状态
    private GameTime _time;
    private Weather _weather;

    public override void _Ready()
    {
        _label = GetNode<Label>("TimeLabel");

        var bus = GameRoot.Services.Get<IEventBus>();
        _subscriptions.Add(bus.Subscribe<HourChanged>(e => { _time = e.Time; Render(); }));
        _subscriptions.Add(bus.Subscribe<DayStarted>(e => { _time = e.Time; Render(); }));
        _subscriptions.Add(bus.Subscribe<SeasonChanged>(e => { _time = e.Time; Render(); }));
        _subscriptions.Add(bus.Subscribe<WeatherChanged>(e => { _weather = e.Current; Render(); }));

        // WeatherChanged 只在天气真的变了才发（ADR-006），开局首日等不到它，不同步一次就永远空白。
        // 这是一次性初始化，不是每帧轮询：此后每次刷新都来自上面的事件。
        var timeService = GameRoot.Services.Get<ITimeService>();
        _time = timeService.Now;
        _weather = timeService.Weather;
        Render();
    }

    public override void _ExitTree()
    {
        // 退订当轮生效（ADR-005）：本节点离树后排在后面的订阅者不会再收到事件
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
    }

    private void Render() =>
        _label.Text = $"第 {_time.Year} 年 {SeasonName(_time.Season)} {_time.Day} 日 " +
                      $"{_time.Hour:D2}:{_time.Minute:D2} · {WeatherName(_weather)} · {PhaseName(_time.Phase)}";

    private static string SeasonName(Season season) => season switch
    {
        Season.Spring => "春",
        Season.Summer => "夏",
        Season.Autumn => "秋",
        _             => "冬",
    };

    private static string WeatherName(Weather weather) => weather switch
    {
        Weather.Sunny => "晴",
        Weather.Rainy => "雨",
        Weather.Storm => "暴风雨",
        Weather.Snowy => "雪",
        Weather.Windy => "风",
        _             => "雾",
    };

    private static string PhaseName(DayPhase phase) => phase switch
    {
        DayPhase.Morning   => "清晨",
        DayPhase.Forenoon  => "上午",
        DayPhase.Afternoon => "下午",
        DayPhase.Evening   => "傍晚",
        DayPhase.Collapsed => "昏倒",
        _                  => "深夜",
    };
}
