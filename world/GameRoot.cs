using Godot;
using XingGame.Core;
using XingGame.Core.Events;
using XingGame.Core.Time;

namespace XingGame.World;

/// <summary>
/// 组合根（ADR-007）：全游戏唯一 new 具体实现的地方。除「现实秒换算成游戏分钟」外不含任何规则。
/// </summary>
public partial class GameRoot : Node
{
    /// <summary>
    /// 决定天气序列的世界种子。契约要求它随存档走（ARCHITECTURE 未定义项备案 #8），
    /// M1-2 存档切片起改从存档读取；现在先用类内常量，不引 JSON、不做半套存档。
    /// </summary>
    private const int WorldSeed = 1;

    /// <summary>
    /// 攒下的零头分钟。每帧增量是小数（10 分/秒 ÷ 60fps ≈ 0.167）而 Advance 只收 int，
    /// 不留余数就只能一直 Advance(0)，时间永远不走。
    /// </summary>
    private double _pendingMinutes;

    /// <summary>持有具体类型才能读到 Config —— 流速是桥接层做现实/游戏换算的依据。</summary>
    private TimeService _time = null!;

    /// <summary>其他桥接节点的取服务入口。</summary>
    public static IServiceRegistry Services { get; private set; } = null!;

    public override void _Ready()
    {
        var services = new ServiceRegistry();
        var bus = new EventBus();
        var weatherGenerator = new WeatherGenerator(WeatherTable.LoadDefault());
        var time = new TimeService(bus, weatherGenerator, worldSeed: WorldSeed);

        // 键是声明的类型参数：这里注册接口，取用方也只能按接口取（ServiceRegistry 的约定）
        services.Register<IEventBus>(bus);
        services.Register<IWeatherGenerator>(weatherGenerator);
        services.Register<ITimeService>(time);

        _time = time;
        Services = services;
    }

    public override void _Process(double delta)
    {
        _pendingMinutes += delta * _time.Config.MinutesPerRealSecond;

        int wholeMinutes = (int)_pendingMinutes;
        if (wholeMinutes == 0) return;   // 不足一分钟，零头留到下一帧

        _pendingMinutes -= wholeMinutes;
        _time.Advance(wholeMinutes);
    }
}
