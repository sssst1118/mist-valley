namespace XingGame.Core.Time;

// 事件跟着领域走，不集中放（ARCHITECTURE.md「事件定义」）。EventBus 是泛型的，不认识这些类型，
// 故时间领域与事件总线之间无循环依赖。
// 全部用 readonly record struct：时间事件每帧都可能发，值类型不给 GC 添压力（ADR-005）。

/// <summary>每推进一分钟发一次。</summary>
public readonly record struct MinuteTicked(GameTime Time);

/// <summary>整点发一次（跨小时的那一分钟）。</summary>
public readonly record struct HourChanged(GameTime Time);

/// <summary>跨入日界（6:00）时发一次。0:00–5:59 属于前一个游戏日，故午夜不发此事件。</summary>
public readonly record struct DayStarted(GameTime Time);

/// <summary>季节更替时随当日 <see cref="DayStarted"/> 之后发出。</summary>
public readonly record struct SeasonChanged(GameTime Time, Season Previous);

/// <summary>时段切换时发出（清晨/上午/下午/傍晚/深夜/昏倒）。</summary>
public readonly record struct DayPhaseChanged(DayPhase Current, DayPhase Previous);

/// <summary>当日天气变化时发出。仅天气真的变了才发——否则每天一次的无用通知会淹没订阅者。</summary>
public readonly record struct WeatherChanged(Weather Current, Weather Previous);
