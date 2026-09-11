using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;

namespace XingGame.Systems.Ranching;

/// <summary>一只动物的读数快照。外部只能读——改状态一律走 <see cref="Ranch"/> 的动作。</summary>
public sealed record AnimalInfo(
    int Id,
    AnimalDefinition Definition,
    string Name,
    int AgeDays,
    bool FedToday,
    int DaysSinceProduce,
    int Mood,
    int Affection)
{
    /// <summary>
    /// 是否已成年。<b>缺省数据里 <see cref="AnimalDefinition.GrowthDays"/> 全是 0（§6.5 没给成长天数），
    /// 所以一买入就是成年</b>——这道门槛只在文档补上数值之后才起作用。
    /// </summary>
    public bool IsAdult => AgeDays >= Definition.GrowthDays;
}

/// <summary>
/// 某一天产出的东西。<see cref="Ranch.AdvanceDay"/> 只报告「谁产了什么」，
/// 送进背包是 <see cref="RanchingSystem"/> 的事——牧场不认识背包。
/// </summary>
public readonly record struct AnimalProduce(int AnimalId, string ItemId, int Count);

/// <summary>
/// 牧场的全部状态。纯 C#：不认识 Godot，也<b>不认识事件总线与背包</b>——
/// 「什么时候推进一天」「产出进哪个背包」是 <see cref="RanchingSystem"/> 的事，
/// 本类只负责动作本身（与 <c>Farmland</c> 的分工一致）。
/// </summary>
/// <remarks>
/// <para>
/// 动物按 id 存在字典里，<b>不在字典里的个体就是不存在</b>（<see cref="Get"/> 抛）。
/// 不存「空动物」占位：牧场里没有动物是常态，存档不必为此写一堆空对象。
/// </para>
/// <para>
/// <b>没喂食只是停滞，不会有任何损失</b>：§6.5 只说「喂养：干草或放牧」，没说没喂会怎样。
/// 没喂的那天动物不长产出计时、不掉心情、不会饿死——忘喂一天就掉一档是玩家最恨的那种惩罚，
/// 文档没写就不加（与 ADR-014「不浇水只是停滞」同一条取舍）。
/// </para>
/// <para>
/// <b>心情与好感度只存值，不做加成</b>：§6.5 说心情影响产出品质、好感度影响产出频率，
/// 但一个公式都没给。编一个出来等于替文档决定玩家的收成，故留到文档补上数值。
/// </para>
/// </remarks>
public sealed class Ranch : ISaveable
{
    /// <summary>
    /// 产出一次给几个。<b>§6.5 没有产量列，待裁决</b>——与 <c>Farmland.HarvestYield</c> 同款：
    /// 取 1 是最保守的取值，将来补上产量表只改这一个常量，不影响存档（存档不记产量）。
    /// </summary>
    public const int ProduceYield = 1;

    /// <summary>§6.5「心情值：0-100」。</summary>
    public const int MaxMood = 100;

    /// <summary>§6.5「好感度：0-5 心」。</summary>
    public const int MaxAffection = 5;

    /// <summary>
    /// 新买入动物的初始心情取<b>上限</b>：§6.5 只说心情影响产出品质，没给初始值。
    /// 取低值等于替文档给玩家一个减益。
    /// </summary>
    public const int InitialMood = MaxMood;

    /// <summary>
    /// 初始好感度取 0：好感度是攒出来的（§6.5 只说它影响产出频率），而频率加成文档没给公式、
    /// 本模块不做，故 0 不带来任何减益。
    /// </summary>
    public const int InitialAffection = 0;

    private readonly IAnimalTable _animals;
    private readonly Dictionary<int, Animal> _byId = new();

    /// <summary>从 1 起编号：0 留给「没有动物」，读档后由已有 id 推出（见 <see cref="Deserialize"/>）。</summary>
    private int _nextId = 1;

    public Ranch(IAnimalTable animals) =>
        _animals = animals ?? throw new ArgumentNullException(nameof(animals));

    /// <summary>全部动物，按 id 升序（也就是加入顺序）。UI 列表与存档都用这个顺序。</summary>
    public IReadOnlyList<AnimalInfo> Animals
    {
        get
        {
            List<Animal> ordered = Ordered();
            var infos = new List<AnimalInfo>(ordered.Count);
            foreach (Animal animal in ordered) infos.Add(animal.ToInfo());

            return infos;
        }
    }

    /// <summary>未记录的个体不存在——找不到以返回值 false 表达。</summary>
    public bool TryGet(int id, out AnimalInfo animal)
    {
        if (_byId.TryGetValue(id, out Animal? found))
        {
            animal = found.ToInfo();
            return true;
        }

        animal = null!;   // out 必须先赋值：找不到以返回值 false 表达（同 ItemTable）
        return false;
    }

    public AnimalInfo Get(int id) => Require(id).ToInfo();

    /// <summary>
    /// 新增一只动物，返回它的 id。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 未知的动物 id 是<b>编程错误</b>，抛 <c>KeyNotFoundException</c> 而不是返回 0——
    /// 与 <c>Farmland.TryPlant</c> 对未知种子的处理一致：凭空加一只表里没有的动物，
    /// 必然是 id 写错了，静默失败会让这个错拖到收畜产那天才炸。
    /// </para>
    /// <para>
    /// <b>这里不收钱</b>：购买价在表里（<see cref="AnimalDefinition.BuyPrice"/>），扣金币是经济模块
    /// 与桥接层的事。牧场不认识金币，也就不会因为并行开发的经济模块改签名而跟着炸。
    /// </para>
    /// </remarks>
    public int Add(string animalId, string name = "")
    {
        AnimalDefinition definition = _animals.Get(animalId);

        int id = _nextId++;
        _byId.Add(id, new Animal(id, definition) { Name = NormalizeName(name) });

        return id;
    }

    /// <summary>
    /// 改名。<b>空白名字等同于「未命名」</b>——§6.5 只说「每只动物可命名」，
    /// 没规定名字必须非空，一个纯空白的名字只在界面上添乱。
    /// </summary>
    public void Rename(int id, string name) => Require(id).Name = NormalizeName(name);

    /// <summary>§6.5「心情值：0-100」。越界是调用方的 bug，当场抛——钳一下会让多出来的部分静默消失。</summary>
    public void SetMood(int id, int mood)
    {
        if (mood < 0 || mood > MaxMood)
            throw new ArgumentOutOfRangeException(nameof(mood), mood, $"心情值必须在 0..{MaxMood}（§6.5）");

        Require(id).Mood = mood;
    }

    /// <summary>§6.5「好感度：0-5 心」。越界同 <see cref="SetMood"/>。</summary>
    public void SetAffection(int id, int hearts)
    {
        if (hearts < 0 || hearts > MaxAffection)
            throw new ArgumentOutOfRangeException(nameof(hearts), hearts, $"好感度必须在 0..{MaxAffection} 心（§6.5）");

        Require(id).Affection = hearts;
    }

    /// <summary>
    /// 标记这只动物今天已喂过。<b>饲料的扣除在 <see cref="RanchingSystem"/> 里</b>——
    /// 本类不认识背包，只记「今天喂过了」这个事实。
    /// </summary>
    public void MarkFed(int id) => Require(id).FedToday = true;

    /// <summary>
    /// 推进一天：所有动物长一天，喂过的（且已成年的）按产出周期产出，随后喂食状态一律清空。
    /// </summary>
    /// <returns>这一天产出的东西，由 <see cref="RanchingSystem"/> 送进背包。</returns>
    /// <remarks>
    /// <b>喂食状态在这里清空</b>：§6.5 的喂养是每天的事，与 <c>Farmland.AdvanceDay</c> 清浇水状态
    /// 同一个道理——不清的话，喂一次就永久生效，「每天要喂」这条规则就不存在了。
    /// </remarks>
    public IReadOnlyList<AnimalProduce> AdvanceDay()
    {
        List<Animal> ordered = Ordered();
        var produced = new List<AnimalProduce>();

        foreach (Animal animal in ordered) AdvanceOne(animal, produced);

        return produced;
    }

    public string SaveKey => "ranching";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    public int Version => 1;

    /// <summary>
    /// 按 id 升序输出：字典的遍历顺序随插入顺序而变，稳定排序让存档的 diff 可读，
    /// 也让「同一份状态序列化两次结果相同」成立。
    /// </summary>
    public string Serialize()
    {
        List<Animal> ordered = Ordered();
        var saved = new List<SavedAnimal>(ordered.Count);

        foreach (Animal animal in ordered)
        {
            saved.Add(new SavedAnimal(
                animal.Id,
                animal.Definition.AnimalId,
                animal.Name,
                animal.AgeDays,
                animal.FedToday,
                animal.DaysSinceProduce,
                animal.Mood,
                animal.Affection));
        }

        return JsonSerializer.Serialize(new SavedRanch(saved.ToArray()), SaveJsonOptions);
    }

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009 同款理由）
        if (fromVersion > Version)
            throw new NotSupportedException($"牧场存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedRanch saved = JsonSerializer.Deserialize<SavedRanch>(json, SaveJsonOptions)
            ?? throw new InvalidDataException("牧场存档内容为空");

        SavedAnimal[] animals = saved.Animals ?? throw new InvalidDataException("牧场存档缺少动物数组");

        // 先整份校验再落盘：坏存档不该让牧场停在「读了一半」的状态（同 Farmland/Inventory）
        var restored = new Dictionary<int, Animal>(animals.Length);
        foreach (SavedAnimal one in animals)
        {
            // 数组里可以写 null，System.Text.Json 照收。不判就是下面第一行一个 NRE 冒出去——
            // 按 ADR-009 坏档该抛 InvalidDataException，将来 catch 坏档的代码要接得住
            if (one is null)
                throw new InvalidDataException("牧场存档里有一只空动物");

            // 同一只动物出现两次时，谁生效取决于文件顺序——那是「有时对有时不对」的幽灵 bug
            if (!restored.TryAdd(one.Id, Restore(one)))
                throw new InvalidDataException($"牧场存档出现重复的动物 id {one.Id}");
        }

        // 整状态覆盖：存档里没有的动物一律不存在，而不是把旧状态留着
        _byId.Clear();
        foreach (KeyValuePair<int, Animal> pair in restored) _byId[pair.Key] = pair.Value;

        // 下一号 id 由现有 id 推出，**不单独存**：同一个事实存两处早晚会对不上（ADR-009 的 worldSeed 同理）
        _nextId = restored.Count == 0 ? 1 : MaxId(restored) + 1;
    }

    /// <summary>存档是外部输入，坏值当场抛——越界值渗进牧场后，症状会出现在离病因很远的地方。</summary>
    private Animal Restore(SavedAnimal saved)
    {
        // 表里没有的动物 id：写它的人与读它的人对格式的理解不一致，这正是该停下来的时刻
        if (!_animals.TryGet(saved.AnimalId, out AnimalDefinition definition))
            throw new InvalidDataException($"牧场存档里的动物 id「{saved.AnimalId}」不在动物表里");

        if (saved.Id < 1)
            throw new InvalidDataException($"牧场存档出现非法动物 id {saved.Id}（id 从 1 起）");

        // 编号到顶时 Add 的 _nextId++ 会回绕成 int.MinValue，新动物拿到一个负数 id——
        // 与其在很久以后看见一只编号为负的鸡，不如在读档这一步就拦下（ADR-009：读错数据比读不出来更糟）
        if (saved.Id == int.MaxValue)
            throw new InvalidDataException($"牧场存档的动物 id {saved.Id} 已到编号上限，无法再分配下一个编号");

        // 下面五个字段的 0 / false 都是合法状态（刚买回来的动物就是这样），所以「字段不在」必须与
        // 「字段是 0」分开——可空类型是这两者的唯一分界线（同 Wallet 的 SavedWallet）。
        // 静默读成 0 是把状态抹平了，而不只是少读一次：这份 0 会被下一次 Serialize 写死
        if (saved.AgeDays is not int ageDays)
            throw new InvalidDataException($"牧场存档 {saved.AnimalId}#{saved.Id} 缺少 AgeDays 字段");

        if (saved.FedToday is not bool fedToday)
            throw new InvalidDataException($"牧场存档 {saved.AnimalId}#{saved.Id} 缺少 FedToday 字段");

        if (saved.DaysSinceProduce is not int daysSinceProduce)
            throw new InvalidDataException($"牧场存档 {saved.AnimalId}#{saved.Id} 缺少 DaysSinceProduce 字段");

        if (saved.Mood is not int mood)
            throw new InvalidDataException($"牧场存档 {saved.AnimalId}#{saved.Id} 缺少 Mood 字段");

        if (saved.Affection is not int affection)
            throw new InvalidDataException($"牧场存档 {saved.AnimalId}#{saved.Id} 缺少 Affection 字段");

        if (ageDays < 0)
            throw new InvalidDataException($"牧场存档 {saved.AnimalId}#{saved.Id} 的年龄为 {ageDays}");

        if (daysSinceProduce < 0)
            throw new InvalidDataException($"牧场存档 {saved.AnimalId}#{saved.Id} 的产出计时为 {daysSinceProduce}");

        // 产出计时到周期就该清零，超过只能是存档坏了；也可能是表里的周期被改小了，
        // 故消息里把当前周期一并写出来——这条信息正是修它需要的
        if (daysSinceProduce > definition.ProductionIntervalDays)
            throw new InvalidDataException(
                $"牧场存档 {saved.AnimalId}#{saved.Id} 的产出计时 {daysSinceProduce} 超过它的产出周期 {definition.ProductionIntervalDays} 天");

        if (mood < 0 || mood > MaxMood)
            throw new InvalidDataException($"牧场存档 {saved.AnimalId}#{saved.Id} 的心情值 {mood} 不在 0..{MaxMood}");

        if (affection < 0 || affection > MaxAffection)
            throw new InvalidDataException(
                $"牧场存档 {saved.AnimalId}#{saved.Id} 的好感度 {affection} 不在 0..{MaxAffection}");

        return new Animal(saved.Id, definition)
        {
            Name = saved.Name ?? string.Empty,   // 名字为空 = 未命名，是合法状态（同 Rename 对空白的处理）
            AgeDays = ageDays,
            FedToday = fedToday,
            DaysSinceProduce = daysSinceProduce,
            Mood = mood,
            Affection = affection,
        };
    }

    /// <summary>按 id 升序取内部对象。产出顺序会影响背包槽位，故不能听凭字典的遍历顺序。</summary>
    private List<Animal> Ordered()
    {
        var ordered = new List<Animal>(_byId.Values);
        ordered.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        return ordered;
    }

    private static int MaxId(Dictionary<int, Animal> animals)
    {
        int max = 0;
        foreach (int id in animals.Keys)
        {
            if (id > max) max = id;
        }

        return max;
    }

    /// <summary>找不到的个体就是不存在——抛而不是返回一个空动物，免得调用方拿着假数据往下跑。</summary>
    private Animal Require(int id) =>
        _byId.TryGetValue(id, out Animal? animal)
            ? animal
            : throw new KeyNotFoundException($"牧场里没有 id 为 {id} 的动物");

    private static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
    }

    /// <summary>推进一只动物的一天。产出计时只在喂过的日子前进（见类注释的「停滞」取舍）。</summary>
    private static void AdvanceOne(Animal animal, List<AnimalProduce> produced)
    {
        animal.AgeDays++;

        // 幼年不产出。缺省数据里成年的门槛是 0 天（§6.5 未给成长天数），故这条只在表里填了天数后才起作用
        if (!animal.IsAdult)
        {
            animal.FedToday = false;
            return;
        }

        if (animal.FedToday)
        {
            animal.DaysSinceProduce++;

            if (animal.DaysSinceProduce >= animal.Definition.ProductionIntervalDays)
            {
                animal.DaysSinceProduce = 0;
                produced.Add(new AnimalProduce(animal.Id, animal.Definition.ProduceItemId, ProduceYield));
            }
        }

        animal.FedToday = false;
    }

    /// <summary>一只动物的全部可变状态。私有：外部只该经本类的动作改它，绕过动作直接改就绕过了所有规则。</summary>
    private sealed class Animal
    {
        public Animal(int id, AnimalDefinition definition)
        {
            Id = id;
            Definition = definition;

            // 初值只在这里定一次：读档走 Restore，存档里的值才是权威（不重放「新买入」的初值）
            Mood = InitialMood;
            Affection = InitialAffection;
        }

        public int Id { get; }

        public AnimalDefinition Definition { get; }

        public string Name { get; set; } = string.Empty;

        public int AgeDays { get; set; }

        public bool FedToday { get; set; }

        public int DaysSinceProduce { get; set; }

        public int Mood { get; set; }

        public int Affection { get; set; }

        public bool IsAdult => AgeDays >= Definition.GrowthDays;

        public AnimalInfo ToInfo() =>
            new(Id, Definition, Name, AgeDays, FedToday, DaysSinceProduce, Mood, Affection);
    }

    /// <summary>存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。</summary>
    private sealed record SavedRanch(SavedAnimal[] Animals);

    /// <param name="AnimalId">动物表里的 id，不写序号——序号会随表序变动错位（ADR-012）。</param>
    /// <remarks>
    /// 状态字段用可空类型，为的是把「字段不在」与「字段是 0 / false」分开：后者是刚买回来的
    /// 动物的合法状态，不可空的话两者混为一谈，读档就变成猜着读了。
    /// </remarks>
    private sealed record SavedAnimal(
        int Id,
        string AnimalId,
        string? Name,
        int? AgeDays,
        bool? FedToday,
        int? DaysSinceProduce,
        int? Mood,
        int? Affection);

    /// <summary>字段名都写出来，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private static readonly JsonSerializerOptions SaveJsonOptions = new() { WriteIndented = true };
}
