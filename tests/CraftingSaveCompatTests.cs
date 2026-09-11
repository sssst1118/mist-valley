using System;
using System.IO;
using XingGame.Core.Save;
using XingGame.Systems.Crafting;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 制作存档的**向后兼容**（M3-7 加了「炼丹品级」这一列后重走一遍，同 <c>CultivationSaveCompatTests</c>）。
/// SQLite 那部分跑在真文件上而非替身：兼容的错大多出在「存下去的和读回来的不是一回事」，
/// 而替身恰好会把这类错抹平。
/// </summary>
/// <remarks>
/// 三种旧档一次验完，它们分别对应三种真实经历：
/// <list type="number">
///   <item><b>M1 的档</b>：blob 表里连 <c>crafting</c> 这个键都没有，读档照常、状态停在构造时。</item>
///   <item><b>M2 的档</b>（Version 1）：有解锁表、没有品级列（那一版还没有炼丹品级这回事）。
///         品级缺席读成**最低一档**——这是迁移决定，理由写在 <c>CraftingSystem.ReadAlchemyRank</c> 上。</item>
///   <item><b>M3-7 的档</b>（Version 2）：解锁表与品级都在。</item>
/// </list>
/// 写老格式的档要用一个「替身可存档件」（<see cref="RawBlob"/>）：用真系统写出来的永远是新格式，
/// 那样验的「兼容」是假兼容。
/// </remarks>
public sealed class CraftingSaveCompatTests : IDisposable
{
    private const int Slot = 1;
    private const int WorldSeed = 20260912;

    private static readonly ItemTable Items = ItemTable.LoadDefault();
    private static readonly AlchemyRankTable Ranks = AlchemyRankTable.LoadDefault();
    private static readonly RecipeTable Recipes = RecipeTable.LoadDefault(Items, Ranks);

    private readonly string _saveDirectory =
        Path.Combine(Path.GetTempPath(), "xing-crafting-save-" + Guid.NewGuid().ToString("N"));

    /// <summary>SQLite 连接池可能还攥着文件句柄，清不掉临时目录不该让测试失败。</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_saveDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static CraftingSystem NewCrafting(IInventory inventory) =>
        new(Recipes, inventory, Items, Ranks);

    private static Inventory NewInventory()
    {
        var inventory = new Inventory(Items, slotCount: 8);
        inventory.Add("crop_spirit_grass", 6);
        inventory.Add("material_spirit_spring_water", 2);
        return inventory;
    }

    [Fact]
    public void M1旧档_完全没有制作_blob_读档照常且保留构造时的状态()
    {
        // M1 的存档里没有 crafting 这个键（那时还没有制作系统）。SqliteSaveService 对缺席的键是
        // 「跳过、让它保持自己的初始状态」，所以这里要验的是：读档整体成功，而且照常能接着做东西
        var saves = new SqliteSaveService(_saveDirectory);
        saves.Save(Slot, new SaveMeta(WorldSeed, "旧农场", "第 1 年 春 3 日 16:00"),
            new ISaveable[] { new RawBlob("time", 1, """{"Year":1,"Season":"Spring","Day":3,"Hour":16,"Minute":0}""") });

        Inventory inventory = NewInventory();
        CraftingSystem crafting = NewCrafting(inventory);

        Assert.True(saves.Load(Slot, new ISaveable[] { crafting }));

        Assert.Empty(crafting.UnlockedRecipes);
        Assert.Equal(1, crafting.AlchemyRank);   // 起点由品级表的最低一档回答

        // 读档之后照常能玩：解锁一条非丹药配方就能做出来（品级只卡丹药）
        crafting.Unlock("recipe_scarecrow");
        inventory.Add("material_wood", 50);
        inventory.Add("material_coal", 1);
        Assert.True(crafting.TryCraft("recipe_scarecrow"));
        Assert.Equal(1, inventory.Count("craft_scarecrow"));
    }

    [Fact]
    public void M2旧档_有解锁表没有品级列_品级读成最低一档且接着能炼()
    {
        // M2 写下的真格式（Version 1，没有 AlchemyRank 这一列）。那份存档里的玩家确实没有品级
        // 这回事，读成最低一档是迁移决定——凭空给一个高品级等于替他改进度
        var saves = new SqliteSaveService(_saveDirectory);
        saves.Save(Slot, new SaveMeta(WorldSeed, "旧农场", "第 1 年 春 3 日 16:00"),
            new ISaveable[]
            {
                new RawBlob("crafting", 1,
                    """{ "UnlockedRecipes": [ "recipe_foundation_pill" ] }"""),
            });

        Inventory inventory = NewInventory();
        CraftingSystem crafting = NewCrafting(inventory);

        Assert.True(saves.Load(Slot, new ISaveable[] { crafting }));

        Assert.Equal(new[] { "recipe_foundation_pill" }, crafting.UnlockedRecipes);
        Assert.Equal(1, crafting.AlchemyRank);

        // 一品炼不了二阶丹：材料一件都不许动（旧档迁移不该顺手把门槛也绕过去）
        Assert.False(crafting.TryCraft("recipe_foundation_pill"));
        Assert.Equal(6, inventory.Count("crop_spirit_grass"));
        Assert.Equal(2, inventory.Count("material_spirit_spring_water"));

        // 升到二品就能做——旧档接着能玩，不是「读进来了但坏了」
        crafting.SetAlchemyRank(2);
        Assert.True(crafting.TryCraft("recipe_foundation_pill"));
        Assert.Equal(1, inventory.Count("craft_foundation_pill"));
    }

    [Fact]
    public void 读档之后写回去_版本升到二_品级也带着走()
    {
        // 迁移只做一半的常见形态：读旧档没问题，但保存时又把旧格式写回去。
        // 这里读一份 Version 1 的档、改一品级、再存一遍，然后用**另一个系统**读回来：
        // 若落盘的还是 Version 1，那份档会走「缺列读成最低一档」那条路，品级会掉回去（3 → 1）
        var saves = new SqliteSaveService(_saveDirectory);
        saves.Save(Slot, new SaveMeta(WorldSeed, "旧农场", "第 1 年 春 3 日 16:00"),
            new ISaveable[]
            {
                new RawBlob("crafting", 1,
                    """{ "UnlockedRecipes": [ "recipe_foundation_pill" ] }"""),
            });

        CraftingSystem migrated = NewCrafting(NewInventory());
        Assert.True(saves.Load(Slot, new ISaveable[] { migrated }));
        migrated.SetAlchemyRank(3);

        saves.Save(Slot, new SaveMeta(WorldSeed, "旧农场", "第 1 年 春 4 日 16:00"),
            new ISaveable[] { migrated });
        Assert.Equal(2, migrated.Version);

        CraftingSystem reloaded = NewCrafting(NewInventory());
        Assert.True(saves.Load(Slot, new ISaveable[] { reloaded }));

        Assert.Equal(3, reloaded.AlchemyRank);
        Assert.Equal(new[] { "recipe_foundation_pill" }, reloaded.UnlockedRecipes);
    }

    /// <summary>
    /// 按指定的键、版本与原始 JSON 往 blob 表里写一条——**只有替身写得出旧格式**：
    /// 用真系统写出来的永远是新格式，那样验的「兼容」是假兼容。
    /// </summary>
    private sealed class RawBlob : ISaveable
    {
        private readonly int _version;
        private readonly string _json;

        public RawBlob(string key, int version, string json)
        {
            SaveKey = key;
            _version = version;
            _json = json;
        }

        public string SaveKey { get; }

        public int Version => _version;

        public string Serialize() => _json;

        public void Deserialize(string json, int fromVersion) => throw new NotSupportedException(
            "替身只用来写旧格式的档，不参与读档");
    }
}
