using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using XingGame.Systems.Items;

namespace XingGame.Systems.Combat;

/// <summary>
/// 静态怪物表，数据在 <c>data/combat/monsters.json</c>。加载时逐条校验，并<b>用物品表交叉校验掉落</b>
/// （照 <see cref="ItemTable"/> 与 <c>CropTable</c> 的先例）：掉落里写了一个物品表里没有的 id，
/// 玩家打死怪的那一刻才会发现东西没进背包，而那时现场离病因已经很远。
/// </summary>
/// <remarks>
/// <para><b>动手前读了哪四节，各给了什么</b>（行号对 <c>docs/public/design.md</c>）：</para>
/// <list type="bullet">
/// <item><b>§7.1 矿洞（384-398）</b>：120 层、每 10 层电梯、每 10 层宝箱、四种特殊层（怪物层/黑暗层/
/// 岩浆层/蘑菇层）、危险规则（生命归零则昏倒，损失金币和物品）、每 30 层有灵气浓郁区域（修炼速度 +50%）、
/// 怪物六个名字（史莱姆、蝙蝠、骷髅、幽灵、史莱姆王、石魔，原文末尾有「等」）、矿石六种（铜、铁、金、铱、煤、宝石）。
/// 矿洞那一半见 <see cref="MineTable"/>。</item>
/// <item><b>§7.2 战斗系统（399-411）</b>：武器五类（剑/匕首/锤/弹弓/法宝）、操作五种、武器属性四项
/// （伤害/速度/暴击率/击退）、掉落<b>大类</b>（矿石、宝石、怪物战利品、稀有装备、灵材）、
/// 战斗等级提升生命与攻击、境界压制。</item>
/// <item><b>§8.9 妖兽与怪物体系（1035-1122）</b>：妖兽阶位一至十二阶及对应修士境界、妖兽类型四种
/// （灵兽/凶兽/妖兽/上古异兽）、灵宠驯养与进化。</item>
/// <item><b>§4.2-4.4 灵根（163-190，本模块只关心战斗相关的部分）</b>：灵根品级的修炼速度倍率与突破概率、
/// 雷灵根对妖兽和魔修伤害 +50%、剑灵根剑类法宝威力 +100%、变异/先天异灵根的机制描述。</item>
/// </list>
///
/// <para><b>文档没给的（本表因此留空或填 0，一个都不编）</b>：任何一只怪物的血量与攻击力；
/// 每只怪物出现于哪几层；每只怪物掉落哪件具体物品（§7.2 只给了大类）；玩家一侧的血量、攻击力、
/// 伤害公式，以及速度/暴击率/击退的数值；境界压制的数值；六只怪物与妖兽阶位的对应关系。</para>
///
/// <para><b>文档给了但本模块不做</b>（写明去处，免得下一个人以为漏了）：
/// 灵根伤害加成与境界压制（要等 M3 的灵根/修为系统，本模块连玩家一侧的数值都是调用方传进来的）；
/// 灵宠驯养与进化（§8.9，M3+）；实时战斗操作（契约规定只做可测的回合式结算，不做动画与手感）；
/// 妖兽阶位与类型（§8.9 给了两张表，但 §7.1 的六只怪物一只都没被文档标上阶位或类型——
/// 建了枚举也没有一行数据可填，等文档补）。</para>
/// </remarks>
public sealed class MonsterTable : IMonsterTable
{
    /// <summary>缺省怪物表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/combat/monsters.json";

    private readonly Dictionary<string, MonsterDefinition> _byId;
    private readonly ReadOnlyCollection<MonsterDefinition> _all;

    private MonsterTable(Dictionary<string, MonsterDefinition> byId, ReadOnlyCollection<MonsterDefinition> all)
    {
        _byId = byId;
        _all = all;
    }

    /// <summary>按 JSON 里的顺序排列，图鉴列表直接照用。</summary>
    public IReadOnlyCollection<MonsterDefinition> All => _all;

    public bool TryGet(string id, out MonsterDefinition definition)
    {
        if (_byId.TryGetValue(id, out MonsterDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;   // out 必须先赋值：找不到以返回值 false 表达（同 ItemTable）
        return false;
    }

    public MonsterDefinition Get(string id) =>
        _byId.TryGetValue(id, out MonsterDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"怪物表里没有 id 为「{id}」的怪物");

    public IReadOnlyList<MonsterDefinition> ForLayer(int layer)
    {
        if (layer < 1)
            throw new ArgumentOutOfRangeException(nameof(layer), layer, "矿洞层号从 1 起");

        // 层号的上界归矿洞管（§7.1 的 120 层是矿洞的属性，不是怪物表的）——这里只做「哪些怪可能出现」的过滤
        var candidates = new List<MonsterDefinition>(_all.Count);
        foreach (MonsterDefinition monster in _all)
        {
            if (monster.AppearsAt(layer)) candidates.Add(monster);
        }

        return candidates;
    }

    /// <param name="items">用于交叉校验掉落是否都在物品表里——这是本方法需要物品表的理由，不是可选装饰。</param>
    public static MonsterTable FromJson(string json, IItemTable items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("monsters", out JsonElement monsters) ||
            monsters.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("怪物表缺少 monsters 数组");
        }

        var byId = new Dictionary<string, MonsterDefinition>(StringComparer.Ordinal);
        var all = new List<MonsterDefinition>();

        foreach (JsonElement element in monsters.EnumerateArray())
        {
            MonsterDefinition definition = ParseMonster(element, items);

            // 重复 id 会让「按 id 取到的是哪一份」取决于文件顺序，且两份的数值可能不同——数据错误，启动即报
            if (!byId.TryAdd(definition.Id, definition))
                throw new InvalidDataException($"怪物表出现重复 id：{definition.Id}");

            all.Add(definition);
        }

        return new MonsterTable(byId, all.AsReadOnly());
    }

    /// <param name="items">同 <see cref="FromJson"/>。</param>
    public static MonsterTable FromFile(string path, IItemTable items) => FromJson(File.ReadAllText(path), items);

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省怪物表，与 <c>ItemTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    /// <param name="items">同 <see cref="FromJson"/>。</param>
    public static MonsterTable LoadDefault(IItemTable items)
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path, items);
    }

    private static MonsterDefinition ParseMonster(JsonElement element, IItemTable items)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("怪物表出现不是对象的条目");

        string? id = OptionalString(element, "id");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("怪物表出现空 id");

        string? name = OptionalString(element, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"怪物 {id} 缺少 name");

        int maxHealth = RequiredInt(element, "maxHealth", id!);
        if (maxHealth < 0)
            throw new InvalidDataException($"怪物 {id} 的 maxHealth 为 {maxHealth}，不能为负");

        int attack = RequiredInt(element, "attack", id!);
        if (attack < 0)
            throw new InvalidDataException($"怪物 {id} 的 attack 为 {attack}，不能为负");

        int minLayer = RequiredInt(element, "minLayer", id!);
        int maxLayer = RequiredInt(element, "maxLayer", id!);
        ValidateLayers(id!, minLayer, maxLayer);

        return new MonsterDefinition(id!, name!, maxHealth, attack, minLayer, maxLayer, ParseDrops(element, id!, items));
    }

    /// <summary>
    /// 两个端点要么都是 0（文档未给层数），要么构成一个合法区间。
    /// 只给一个端点的数据会让 <see cref="MonsterDefinition.AppearsAt"/> 变成「凭空多出一个 0 层」，
    /// 而 0 层根本不存在——这种数据错误必须在加载时拦下。
    /// </summary>
    private static void ValidateLayers(string id, int minLayer, int maxLayer)
    {
        if (minLayer == 0 && maxLayer == 0) return;

        if (minLayer == 0 || maxLayer == 0)
            throw new InvalidDataException(
                $"怪物 {id} 的出现层数只给了一个端点（minLayer={minLayer}, maxLayer={maxLayer}）；" +
                "未给层数时两个都要是 0");

        if (minLayer > maxLayer)
            throw new InvalidDataException($"怪物 {id} 的 minLayer（{minLayer}）大于 maxLayer（{maxLayer}）");

        if (minLayer < 1)
            throw new InvalidDataException($"怪物 {id} 的 minLayer 为 {minLayer}，矿洞层号从 1 起");
    }

    /// <summary>
    /// 掉落逐条校验：物品表里没有的 id、非正的数量、同一只怪物重复写同一个物品。
    /// 一次把全部问题都列出来——一条一条修比每次重跑才发现下一条快得多。
    /// </summary>
    private static IReadOnlyList<ItemStack> ParseDrops(JsonElement element, string id, IItemTable items)
    {
        if (!element.TryGetProperty("drops", out JsonElement drops) || drops.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"怪物 {id} 缺少 drops 数组（没有掉落就写空数组）");

        var parsed = new List<ItemStack>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonElement drop in drops.EnumerateArray())
        {
            if (drop.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"怪物 {id} 的掉落里出现不是对象的条目");

            string? itemId = OptionalString(drop, "itemId");
            if (string.IsNullOrWhiteSpace(itemId))
                throw new InvalidDataException($"怪物 {id} 的掉落缺少 itemId");

            int count = RequiredInt(drop, "count", id);
            if (count <= 0)
                throw new InvalidDataException($"怪物 {id} 的掉落 {itemId} 数量为 {count}，必须为正");

            if (!items.TryGet(itemId, out _))
                throw new InvalidDataException($"怪物 {id} 的掉落「{itemId}」不在物品表里");

            // 同一只怪物写两遍同一个物品，两处数量谁生效取决于读的顺序，且「掉落表」从此有两份真相
            if (!seen.Add(itemId))
                throw new InvalidDataException($"怪物 {id} 重复写了掉落「{itemId}」");

            parsed.Add(new ItemStack(itemId, count));
        }

        return parsed.AsReadOnly();
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上 id 才定位得到那一行。</summary>
    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int RequiredInt(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"怪物 {id} 缺少数字字段 {property}");

        return value.GetInt32();
    }

    private static string? FindDefaultFile()
    {
        foreach (string root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, DefaultRelativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
