using System;
using Godot;
using XingGame.Core;
using XingGame.Core.Events;
using XingGame.Core.Save;
using XingGame.Core.Time;
using XingGame.Systems.Combat;
using XingGame.Systems.Crafting;
using XingGame.Systems.Economy;
using XingGame.Systems.Farming;
using XingGame.Systems.Fishing;
using XingGame.Systems.Interaction;
using XingGame.Systems.Items;
using XingGame.Systems.Npc;
using XingGame.Systems.Ranching;

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

    /// <summary>§4.6 初始工具：锄头、洒水壶、斧头、镐子、镰刀。</summary>
    private static readonly string[] StartingToolIds =
    {
        "tool_hoe", "tool_watering_can", "tool_axe", "tool_pickaxe", "tool_sickle",
    };

    /// <summary>§4.6 初始种子：防风草 ×15。</summary>
    private const string StartingSeedId = "seed_parsnip";

    /// <summary>§4.6 初始种子数量。</summary>
    private const int StartingSeedCount = 15;

    /// <summary>§4.6 初始金币。</summary>
    private const int StartingGold = 500;

    /// <summary>
    /// 缺省矿洞。§7.1 只有矿洞这一座有数值（沙漠矿洞文档一个数都没给，未录），所以「玩家的那座矿洞」
    /// 就是它。将来多矿洞时这里换成玩家选中的那座（备案 #50 的电梯入口）。
    /// </summary>
    private const string DefaultMineId = "mine_valley";

    /// <summary>
    /// 攒下的零头分钟。每帧增量是小数（10 分/秒 ÷ 60fps ≈ 0.167）而 Advance 只收 int，
    /// 不留余数就只能一直 Advance(0)，时间永远不走。
    /// </summary>
    private double _pendingMinutes;

    // 世界种子原先是这里的私有实例字段。改成下面的静态属性是为了让桥接层取得到它——它同时也是
    // 「同一个事实只存一处」的直接落实：写回 meta 与面板掷随机读的是同一份。

    /// <summary>持有具体类型才能读到 Config —— 流速是桥接层做现实/游戏换算的依据。</summary>
    private TimeService _time = null!;

    private ISaveService _saves = null!;

    /// <summary>留一份引用只为了在 <c>_ExitTree</c> 里退掉它的订阅（ADR-005：不留无主订阅）。</summary>
    private FarmingSystem _farming = null!;

    /// <summary>同上：畜牧订阅了 <c>DayStarted</c>，离树时要退掉。</summary>
    private RanchingSystem _ranching = null!;

    /// <summary>
    /// 本存档位要写的系统清单，读档与写档**共用同一份**。不能各写各的：<c>SqliteSaveService.Save</c>
    /// 是「先清空 blob 表再写」，漏掉一个系统就等于把它从存档里抹掉——而症状会出现在下一次读档，
    /// 离这里很远。
    /// </summary>
    private ISaveable[] _saveables = null!;

    private IDisposable? _dayStartedSubscription;

    /// <summary>其他桥接节点的取服务入口。</summary>
    public static IServiceRegistry Services { get; private set; } = null!;

    /// <summary>
    /// 本存档的世界种子：决定天气序列（未定义项备案 #8），也是桥接层掷随机的基准——
    /// 钓鱼的竿、掷怪都拿它起头。
    /// </summary>
    /// <remarks>
    /// <b>为什么在组合根上、而不是某个系统上</b>：种子是「这个世界」的身份，随存档走，
    /// 不属于任何单个系统；<c>TimeService</c> 拿它只为一个用途（排天气），且刻意不把它存进 blob
    /// （见其类注释），所以从那边取等于借道。系统侧一律仍由<b>调用方把种子传进去</b>——
    /// 它们保持纯函数，测试才能靠「同参同果」。
    /// </remarks>
    public static int WorldSeed { get; private set; }

    public override void _Ready()
    {
        var services = new ServiceRegistry();
        var bus = new EventBus();
        var weatherGenerator = new WeatherGenerator(WeatherTable.LoadDefault());

        // 物品表在构造时就把 data/items/items.json 逐条校验过了，坏数据炸在这里、不炸在背包里
        var items = ItemTable.LoadDefault();
        var inventory = new Inventory(items, Inventory.DefaultSlotCount);

        // 作物表额外拿物品表交叉校验两张表对不对得上（ADR-013）：作物表自己的测试发现不了这件事
        var crops = CropTable.LoadDefault(items);
        var farmland = new Farmland(crops);

        // core/ 是纯 C#，解析不了 user:// —— 存档目录由桥接层换算后注入（ADR-009）
        var saves = new SqliteSaveService(ProjectSettings.GlobalizePath("user://saves"));

        // 两段式读档，顺序不能变（ADR-009）：TimeService 构造时就要世界种子，而种子在存档里。
        // 这个鸡生蛋的顺序决定了必须先 peek meta、再构造、最后 load blob，不能合并成一次。
        if (!saves.TryPeekWorldSeed(SaveSlot, out int worldSeed))
            worldSeed = NewWorldSeed();

        var time = new TimeService(bus, weatherGenerator, worldSeed: worldSeed);

        // 种植系统要天气（下雨自动浇水，§3.3），所以它排在 TimeService 之后。
        // 构造即订阅 DayStarted / SeasonChanged，Dispose 即退订——不留无主订阅（ADR-005）。
        var farming = new FarmingSystem(bus, time, crops, farmland, inventory);

        // ——— M2 六个领域模块。顺序就是它们的依赖链，不能随手排 ———
        // 静态表在加载时就要拿物品表交叉校验（矿石、产物、成品、饲料都得在物品表里），
        // 所以必然排在 ItemTable 之后，而下面的系统又要拿表去构造。
        var shops = ShopTable.LoadDefault(items);
        var npcs = NpcTable.LoadDefault();
        var fish = FishTable.LoadDefault(items);
        var animals = AnimalTable.LoadDefault(items);
        var monsters = MonsterTable.LoadDefault(items);
        var mines = MineTable.LoadDefault(items);
        var recipes = RecipeTable.LoadDefault(items);

        // 表是只读数据，这几件才是各自要进存档的状态（见下面的 _saveables）
        var wallet = new Wallet();
        var prices = new MarketPrices(items);
        var friendship = new FriendshipSystem(npcs);
        var codex = new FishCodex(fish);
        var ranch = new Ranch(animals);
        var mineProgress = new MineProgress(mines.Get(DefaultMineId));
        var crafting = new CraftingSystem(recipes, inventory, items);

        // 商店要读时间判营业时间（§5.2），所以排在 TimeService 之后；钱与货都是从构造时注入的
        var shopSystem = new ShopSystem(shops, items, inventory, wallet, time, prices);

        // 钓鱼不取 ITimeService：季节/天气/时段由调用方传进来，它自己是纯函数，掷鱼从不读时钟
        var fishing = new FishingSystem(fish, inventory, codex);

        // 畜牧构造即订阅 DayStarted（产出按天结算，§6.5），Dispose 即退订——不留无主订阅（ADR-005）。
        // 宠物（猫/狗）§6.5 只说「可互动、影响心情」，没有数值，按模块的定论不实现。
        var ranching = new RanchingSystem(bus, ranch, inventory);

        var combat = new CombatSystem(monsters, mineProgress, inventory);

        // 键是声明的类型参数：这里注册接口，取用方也只能按接口取（ServiceRegistry 的约定）。
        // 先注册再接存档：反序列化期间若某个可存档系统要取服务，注册表已经就绪。
        services.Register<IEventBus>(bus);
        services.Register<IWeatherGenerator>(weatherGenerator);
        services.Register<ITimeService>(time);
        services.Register<ISaveService>(saves);
        services.Register<IItemTable>(items);

        services.Register<IInventory>(inventory);

        // 耕地与种植没有接口可注册（M1-5 的契约是具体类，ADR-014），桥接层按具体类型取。
        services.Register<Farmland>(farmland);
        services.Register<FarmingSystem>(farming);

        // 交互系统由本类构造（ADR-007：全游戏只在这里 new 具体实现）。
        // 桥接层的 Interactable 与 InteractPrompt 都必须拿到同一个实例，否则提示永远找不到目标。
        services.Register<IInteractionSystem>(new InteractionSystem());

        // M2 六模块：按接口注册（M2-A 共同规矩第 2 条），别让口子从组合根这里破。
        services.Register<IEconomySystem>(wallet);
        services.Register<IShopSystem>(shopSystem);
        services.Register<IFriendshipSystem>(friendship);
        services.Register<IFishingSystem>(fishing);
        services.Register<IRanchingSystem>(ranching);
        services.Register<ICombatSystem>(combat);
        services.Register<ICraftingSystem>(crafting);

        // 这几件额外注册，因为它们在模块自己的契约里就是给桥接层取用的入口，且没有第二条路能拿到：
        // 「按 id 取 NPC」只有 NPC 表能给（好感度接口只按 id 记账、不认名字）；图鉴列表要鱼表加图鉴；
        // 配方列表 UI、动物目录（买入价）同理。其余的状态件（Ranch / MineProgress / MarketPrices）
        // 已经能从各自的系统接口读到，不重复注册。
        services.Register<INpcTable>(npcs);
        services.Register<IFishTable>(fish);
        services.Register<FishCodex>(codex);
        services.Register<IAnimalTable>(animals);
        services.Register<IRecipeTable>(recipes);

        WorldSeed = worldSeed;
        _time = time;
        _saves = saves;
        _farming = farming;
        _ranching = ranching;
        Services = services;

        // 读档与写档共用这一份。Save 是「先清空 blob 表再写」：漏掉谁就等于把谁从存档里抹掉，
        // 而症状要等下一次读档才显形——所以这里每加一个系统，构造与注册两处必须一起加。
        _saveables = new ISaveable[]
        {
            time, inventory, farmland,
            wallet, prices, friendship, codex, ranch, mineProgress, crafting,
        };
        if (saves.Load(SaveSlot, _saveables))
        {
            GD.Print($"[存档] 已读档 slot {SaveSlot}：世界种子 {WorldSeed}，{GameTimeText(time.Now)}");
        }
        else
        {
            // 只有「这个存档位本地不存在」才算新档。不用「背包是空的就发」这类判据：
            // 玩家把背包清空一次就会白拿一份工具，而且他永远不知道自己触发了什么。
            // 同理，M1 的旧档（没有任何 M2 模块的 blob）读进来时金币是 0：那份存档的世界里
            // 玩家早就开过局了，补发一笔钱会凭空改掉他的进度。
            GrantStartingResources(inventory, wallet);
            SaveState("新档");   // 新游戏：初始状态立刻落盘，下次启动就走读档那条路
        }

        // §16.2「每日结束时自动保存」。日界在 6:00，跨入即意味着前一天结束（ARCHITECTURE「日界与事件时序」）
        _dayStartedSubscription = bus.Subscribe<DayStarted>(_ => SaveState("自动保存"));
    }

    /// <summary>
    /// GameRoot 是 autoload，正常不会离树；留着退订是为了不在总线上留无主订阅——
    /// 编辑器里重载程序集时，无主订阅会去碰已经死掉的对象。种植（两个订阅）与畜牧，同理。
    /// </summary>
    public override void _ExitTree()
    {
        _dayStartedSubscription?.Dispose();
        _farming?.Dispose();
        _ranching?.Dispose();
    }

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
            _saves.Save(SaveSlot, BuildMeta(), _saveables);
            GD.Print($"[存档] {reason}已写入 slot {SaveSlot}：{GameTimeText(_time.Now)}");
        }
        catch (Exception exception)
        {
            GD.PushError($"[存档] {reason}写入 slot {SaveSlot} 失败：{exception.Message}");
        }
    }

    /// <summary>
    /// §4.6 初始资源里已经能落地的那部分：金币 500、五件工具、防风草种子 ×15。
    /// </summary>
    /// <remarks>
    /// <b>灵石不在这里发（刻意的，不是漏了）</b>：§4.6 的「灵石 0」= 背包里有 0 个——灵石是物品
    /// 而不是货币（备案 #55），钱包根本没有灵石入口，所以「0」天然成立，无需一句代码。
    /// 房屋 / 宠物 / 灵根 §4.6 也列了，但要等住宅、宠物与灵根系统，M2 不提前实现（铁律 3）。
    /// </remarks>
    private static void GrantStartingResources(IInventory inventory, IEconomySystem wallet)
    {
        wallet.AddGold(StartingGold);
        foreach (string toolId in StartingToolIds) Grant(inventory, toolId, 1);
        Grant(inventory, StartingSeedId, StartingSeedCount);
    }

    /// <summary>
    /// 装不下只报错、不回滚（<c>Add</c> 的约定）：24 格 × 999 上限下这是理论边界，
    /// 真发生了说明背包被改小了——玩家手上少一件工具是看得见的症状，比静默吞掉强。
    /// </summary>
    private static void Grant(IInventory inventory, string itemId, int count)
    {
        int left = inventory.Add(itemId, count);
        if (left > 0) GD.PushError($"[新档] 初始资源 {itemId} 有 {left} 个没装下");
    }

    private SaveMeta BuildMeta() => new(WorldSeed, DefaultFarmName, GameTimeText(_time.Now));

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
