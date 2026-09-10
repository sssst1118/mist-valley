namespace XingGame.Core.Time;

/// <summary>
/// 时间系统只负责「报时」，不负责「效果」（ADR-006）：雨天自动浇水、暴风雨毁作物、灵气浓度波动
/// 等一律由各自领域订阅 <c>TimeEvents</c> 后自行实现。
/// </summary>
public interface ITimeService
{
    GameTime Now { get; }

    Weather Weather { get; }

    bool IsPaused { get; set; }

    /// <summary>推进游戏分钟。由 Godot 桥接层每帧按 delta 换算后调用。</summary>
    void Advance(int gameMinutes);

    /// <summary>睡到次日 6:00，并按新的一天重掷天气。</summary>
    void Sleep();
}
