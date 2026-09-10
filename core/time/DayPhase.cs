namespace XingGame.Core.Time;

/// <summary>时段（设计文档 §3.1）。由 <see cref="GameTime.Phase"/> 从 Hour 推导，不单独持久化。</summary>
public enum DayPhase
{
    Morning,
    Forenoon,
    Afternoon,
    Evening,
    LateNight,
    Collapsed,
}
