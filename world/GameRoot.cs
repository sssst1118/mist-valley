using System;
using Godot;
using XingGame.Core;
using XingGame.Core.Events;
using XingGame.Core.Save;
using XingGame.Core.Time;
using XingGame.Systems.Interaction;

namespace XingGame.World;

/// <summary>
/// 组合根（ADR-007）：全游戏唯一 new 具体实现的地方。除「现实秒换算成游戏分钟」与「存档装配」外不含任何规则。
/// </summary>
public partial class GameRoot : Node
{
    /// <summary>
    /// 存档位。M1 还没有存档菜单（那是 M1-6），先用固定的 slot 1；
    /// **M1-6 存档菜单起改为可选择**——届时由 UI 传入，组合根不再自己决定用哪位。
    /// </summary>
    private const int SaveSlot = 1;

    /// <summary>§16.1 的农场名 M1 尚无命名入口，先占位；M1-6 起由玩家输入。</summary>
    private const string DefaultFarmName = "未命名农场";

    /// <summary>
    /// 攒下的零头分钟。每帧增量是小数（10 分/秒 ÷ 60fps ≈ 0.167）而 Advance 只收 int，
    /// 不留余数就只能一直 Advance(0)，时间永远不走。
    /// </summary>
    private double _pendingMinutes;

    /// <summary>种子决定天气序列，随存档走（ARCHITECTURE 未定义项备案 #8）。留着是为了自动保存时原样写回 meta。</summary>
    private int _worldSeed;

    /// <summary>持有具体类型才能读到 Config —— 流速是桥接层做现实/游戏换算的依据。</summary>
    private TimeService _time = null!;

    private ISaveService _saves = null!;

    private IDisposable? _dayStartedSubscription;

    /// <summary>其他桥接节点的取服务入口。</summary>
    public static IServiceRegistry Services { get; private set; } = null!;

    public override void _Ready()
    {
        var services = new ServiceRegistry();
        var bus = new EventBus();
        var weatherGenerator = new WeatherGenerator(WeatherTable.LoadDefault());

        // core/ 是纯 C#，解析不了 user:// —— 存档目录由桥接层换算后注入（ADR-009）
        var saves = new SqliteSaveService(ProjectSettings.GlobalizePath("user://saves"));

        // 两段式读档，顺序不能变（ADR-009）：TimeService 构造时就要世界种子，而种子在存档里。
        // 这个鸡生蛋的顺序决定了必须先 peek meta、再构造、最后 load blob，不能合并成一次。
        if (!saves.TryPeekWorldSeed(SaveSlot, out int worldSeed))
            worldSeed = NewWorldSeed();

        var time = new TimeService(bus, weatherGenerator, worldSeed: worldSeed);

        // 键是声明的类型参数：这里注册接口，取用方也只能按接口取（ServiceRegistry 的约定）。
        // 先注册再接存档：反序列化期间若某个可存档系统要取服务，注册表已经就绪。
        services.Register<IEventBus>(bus);
        services.Register<IWeatherGenerator>(weatherGenerator);
        services.Register<ITimeService>(time);
        services.Register<ISaveService>(saves);

        // 交互系统由本类构造（ADR-007：全游戏只在这里 new 具体实现）。
        // 桥接层的 Interactable 与 InteractPrompt 都必须拿到同一个实例，否则提示永远找不到目标。
        services.Register<IInteractionSystem>(new InteractionSystem());

        _worldSeed = worldSeed;
        _time = time;
        _saves = saves;
        Services = services;

        var saveables = new ISaveable[] { time };
        if (saves.Load(SaveSlot, saveables))
            GD.Print($"[存档] 已读档 slot {SaveSlot}：世界种子 {_worldSeed}，{GameTimeText(time.Now)}");
        else
            SaveState("新档");   // 新游戏：初始状态立刻落盘，下次启动就走读档那条路

        // §16.2「每日结束时自动保存」。日界在 6:00，跨入即意味着前一天结束（ARCHITECTURE「日界与事件时序」）
        _dayStartedSubscription = bus.Subscribe<DayStarted>(_ => SaveState("自动保存"));
    }

    /// <summary>
    /// GameRoot 是 autoload，正常不会离树；留着退订是为了不在总线上留无主订阅——
    /// 编辑器里重载程序集时，无主订阅会去碰已经死掉的对象。
    /// </summary>
    public override void _ExitTree() => _dayStartedSubscription?.Dispose();

    public override void _Process(double delta)
    {
        _pendingMinutes += delta * _time.Config.MinutesPerRealSecond;

        int wholeMinutes = (int)_pendingMinutes;
        if (wholeMinutes == 0) return;   // 不足一分钟，零头留到下一帧

        _pendingMinutes -= wholeMinutes;
        _time.Advance(wholeMinutes);
    }

    /// <summary>
    /// 写档失败不往上抛：两个调用点分别是 <c>_Ready</c> 与事件派发（自动保存），抛出去会把游戏拖崩，
    /// 而丢一次自动保存只意味着回到上一次存档——下一个日界还会再写一次。
    /// </summary>
    private void SaveState(string reason)
    {
        try
        {
            _saves.Save(SaveSlot, BuildMeta(), new ISaveable[] { _time });
            GD.Print($"[存档] {reason}已写入 slot {SaveSlot}：{GameTimeText(_time.Now)}");
        }
        catch (Exception exception)
        {
            GD.PushError($"[存档] {reason}写入 slot {SaveSlot} 失败：{exception.Message}");
        }
    }

    private SaveMeta BuildMeta() => new(_worldSeed, DefaultFarmName, GameTimeText(_time.Now));

    /// <summary>
    /// 存档列表要显示的时间文本（§16.1）。季节名译法与 <c>ui/TimeHud</c> 重复了一处，
    /// 等 M1-6 存档菜单做出来，两处一起收敛到同一个格式化器。
    /// </summary>
    private static string GameTimeText(GameTime time) =>
        $"第 {time.Year} 年 {SeasonName(time.Season)} {time.Day} 日 {time.Hour:D2}:{time.Minute:D2}";

    private static string SeasonName(Season season) => season switch
    {
        Season.Spring => "春",
        Season.Summer => "夏",
        Season.Autumn => "秋",
        _             => "冬",
    };

    /// <summary>新游戏的世界种子。避开 0——0 是 <c>TimeService</c> 的缺省种子，混在一起分不清「没读到」还是「真是 0」。</summary>
    private static int NewWorldSeed() => Random.Shared.Next(1, int.MaxValue);
}
