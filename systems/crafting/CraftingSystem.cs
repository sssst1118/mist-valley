using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;
using XingGame.Systems.Items;

namespace XingGame.Systems.Crafting;

/// <summary>
/// 制作：把配方表与背包接起来——判定能不能做、扣材料、进成品。
/// </summary>
/// <remarks>
/// <para>
/// <b>三条失败路径，三条都不许留下痕迹。</b>材料不够时一个材料都不能扣（照 <c>Inventory.Remove</c> 的
/// 全有或全无，ADR-012）；产物放不下时同样一个材料都不能扣——材料扣了而产物溢出丢失，是玩家主动点
/// 「制作」时的白扔，而 §12.3 的一条配方动辄 100 灵石；品级不够（§8.7）连试都不该试。所以三件事都在
/// 动手<b>之前</b>查清：材料先逐样核对（<see cref="HasIngredients"/>），背包容量先自己算
/// （<see cref="HasRoomFor"/>），品级由 <see cref="IAlchemyRankTable.RequiredRankForTier"/> 现算。
/// </para>
/// <para>
/// 这三条都由用例钉着（<c>CraftingSystemTests</c> 里逐材料比对、背包放不下那条，
/// 以及 <c>AlchemyCraftingTests</c> 里「差一品就炼不成、差的那一品材料一件不少」），不是注释里的空话。
/// </para>
/// </remarks>
public sealed class CraftingSystem : ICraftingSystem, ISaveable
{
    private readonly IRecipeTable _recipes;
    private readonly IInventory _inventory;
    private readonly IItemTable _items;
    private readonly IAlchemyRankTable _ranks;

    /// <summary>Ordinal 排序：枚举与存盘共用同一个顺序，存档 diff 才稳定、测试才不会时绿时红。</summary>
    private readonly SortedSet<string> _unlocked = new(StringComparer.Ordinal);

    /// <summary>炼丹品级（§8.7）：一品 = 1 … 九品 = 9。是状态，进存档；「能炼几阶」由品级表现算。</summary>
    private int _alchemyRank;

    /// <summary>只读快照，防止调用方拿到内部集合把它改掉（同 <c>Inventory.Slots</c> 的理由）。</summary>
    private ReadOnlyCollection<string> _unlockedView;

    /// <param name="items">
    /// 只为产物的堆叠上限而需要（算背包放不放得下）。配方本身归 <paramref name="recipes"/>。
    /// </param>
    /// <param name="ranks">
    /// 丹药的品级门槛（§8.7）。<b>新档的起点是这张表的最低一档</b>（今天 = 一品炼丹学徒）：
    /// 文档没写玩家从几品起，取最低的那一档——同 <c>CultivationSystem</c> 从境界表的第一个小境界起。
    /// 起点由表回答，所以品级表将来多一档「零品」时这里不必改。
    /// </param>
    public CraftingSystem(IRecipeTable recipes, IInventory inventory, IItemTable items, IAlchemyRankTable ranks)
    {
        _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _ranks = ranks ?? throw new ArgumentNullException(nameof(ranks));

        _alchemyRank = ranks.All[0].Rank;   // 表非空且从一品起连续，由 AlchemyRankTable 的加载校验保证

        _unlockedView = new List<string>(_unlocked).AsReadOnly();
    }

    public IReadOnlyCollection<string> UnlockedRecipes => _unlockedView;

    public int AlchemyRank => _alchemyRank;

    public int? RequiredAlchemyRank(string recipeId) => RequiredRankOf(_recipes.Get(recipeId));

    public bool SetAlchemyRank(int rank)
    {
        // 品级表认不出的值是编程错误（同 Inventory.Add 取到不存在的物品）：不是「等级不够」，是算错了。
        // 不夹逼、不忽略——悄悄收下会让「设成 0 之后谁也炼不动」这种状态没人发现
        if (!_ranks.TryGet(rank, out _))
            throw new ArgumentOutOfRangeException(
                nameof(rank), rank,
                $"品级表里没有 {rank} 品（表里是 {_ranks.All[0].Rank} 品到 {_ranks.All[^1].Rank} 品）");

        if (_alchemyRank == rank) return false;

        _alchemyRank = rank;
        return true;
    }

    public bool IsUnlocked(string recipeId)
    {
        // 未知 id 是编程错误（同 Inventory.Add 取到不存在的物品）：凭空问一个配方表里没有的 id，必然是写错了
        _recipes.Get(recipeId);

        return _unlocked.Contains(recipeId);
    }

    public bool Unlock(string recipeId)
    {
        _recipes.Get(recipeId);

        if (!_unlocked.Add(recipeId)) return false;

        RefreshView();
        return true;
    }

    public bool CanCraft(string recipeId, int count = 1)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), count, "份数必须为正");

        RecipeDefinition recipe = _recipes.Get(recipeId);

        if (!_unlocked.Contains(recipeId)) return false;

        // 判据与 TryCraft 逐条对齐，别让「界面说能做、点下去却没做成」出现
        return MeetsAlchemyRank(recipe)
            && HasIngredients(recipe, count)
            && HasRoomFor(recipe.OutputItemId, (long)recipe.OutputCount * count);
    }

    public bool TryCraft(string recipeId, int count = 1)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), count, "份数必须为正");

        RecipeDefinition recipe = _recipes.Get(recipeId);

        if (!_unlocked.Contains(recipeId)) return false;

        // 品级门槛查在 TryCraft 里，不是只查在 CanCraft 里：直接调 TryCraft 的调用方（脚本、Mod、
        // 将来的自动炼丹炉）绕过 CanCraft 是一条再自然不过的路，而「技能不够却炼成了」不会有任何报错——
        // 它是静默的，所以两条路都必须自己挡住
        if (!MeetsAlchemyRank(recipe)) return false;

        // 全有或全无（ADR-012）：先把「每一样材料都够」全部核对完，再动手扣。边扣边查会让
        // 「扣到第三种才发现不够」变成既成事实——调用方以为没做成，材料却已经少了。
        if (!HasIngredients(recipe, count)) return false;

        // 产物进不去也要在这里挡住：等扣完材料才发现背包满了，玩家就白扔了一条配方的材料
        if (!HasRoomFor(recipe.OutputItemId, (long)recipe.OutputCount * count)) return false;

        // 到这里每一样材料都数过够，中间也没有别的调用能插进来（纯 C#、单线程），后面的 Remove 必然成功
        foreach (RecipeIngredient ingredient in recipe.Ingredients)
            _inventory.Remove(ingredient.ItemId, (int)((long)ingredient.Count * count));

        _inventory.Add(recipe.OutputItemId, recipe.OutputCount * count);
        return true;
    }

    /// <summary>
    /// 每一样材料都够做 <paramref name="count"/> 份。<b>乘积用 long 算</b>：<c>数量 × 份数</c> 溢出 int 会变成
    /// 负数，于是「背包里一个都没有」也被判成够——那是静默放行一次本不该发生的制作。
    /// </summary>
    private bool HasIngredients(RecipeDefinition recipe, int count)
    {
        foreach (RecipeIngredient ingredient in recipe.Ingredients)
        {
            if (_inventory.Count(ingredient.ItemId) < (long)ingredient.Count * count) return false;
        }

        return true;
    }

    /// <summary>
    /// 背包还装不装得下 <paramref name="count"/> 个该物品。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这段检查不是可以省掉的防御，别删。</b>删了它，材料照扣、产物却可能进不了背包白白丢掉——
    /// 这是玩家<b>主动</b>点「制作」时的损失（收获溢出丢一个还能说手滑，这里丢的动辄是 §12.3 一条配方
    /// 的 100 颗灵石），比事后补偿划算得多。<c>CraftingSystemTests</c> 里有两条用例钉着它。
    /// </para>
    /// <para>
    /// 自己算，而不是「先 Add 一次再看返回值」：<c>Add</c> 装不下时<b>已装进去的那部分不回滚</b>
    /// （见 <c>Inventory.Add</c> 的说明），先试一次就等于把背包改成了另一个样子，失败后还得再撤回来——
    /// 撤的过程中槽位布局会变（空出来的格子在前在后与原来不同），凭空多出一个「失败却动了背包」的状态。
    /// </para>
    /// <para>
    /// 数据来自 <see cref="IInventory.Slots"/>（含空槽的定长槽位视图，ADR-012）与物品表的堆叠上限。
    /// 算出来的是<b>下界</b>：之后扣材料只会让槽位更空、可放的地方只会更多，所以过了这一关，Add 一定装得下。
    /// </para>
    /// </remarks>
    private bool HasRoomFor(string itemId, long count)
    {
        int maxStack = _items.Get(itemId).MaxStack;
        long capacity = 0;

        foreach (ItemStack slot in _inventory.Slots)
        {
            // 空槽能放满一整叠；已有同种物品的槽还能放「上限 − 现有」
            if (slot.Count == 0) capacity += maxStack;
            else if (string.Equals(slot.ItemId, itemId, StringComparison.Ordinal)) capacity += maxStack - slot.Count;

            if (capacity >= count) return true;
        }

        return capacity >= count;
    }

    /// <summary>
    /// 这条配方吃不吃品级门槛；吃的话要几品。「几品能炼几阶」<b>只由品级表回答</b>——配方上只写着
    /// 「做出来的是几阶丹药」（<c>RecipeDefinition.PillTier</c>），这里把它翻译成品级，两处不各写一份。
    /// </summary>
    private int? RequiredRankOf(RecipeDefinition recipe) =>
        recipe.PillTier is int tier ? _ranks.RequiredRankForTier(tier) : null;

    /// <summary>
    /// 玩家的品级够不够。<b>不吃门槛的配方（今天是非丹药）直接放行</b>：null 的意思是「这一味没有品级
    /// 门槛」，不是「要零品」，也不是「要最高品」。
    /// </summary>
    private bool MeetsAlchemyRank(RecipeDefinition recipe) =>
        RequiredRankOf(recipe) is not int required || _alchemyRank >= required;

    public string SaveKey => "crafting";

    /// <summary>
    /// JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。
    /// <b>Version 2（M3-7）加了炼丹品级</b>：Version 1 的存档没有这一列，读成最低一档（那时还没有品级）。
    /// </summary>
    public int Version => 2;

    /// <summary>
    /// 解锁的配方按<b>id 字符串</b>存，不存它在配方表里的序号（ADR-012：往表中间插一条配方就会让旧存档的
    /// 序号全部错位）。顺序由 <c>SortedSet</c> 固定为 Ordinal 升序，存档才 diff 得动、测试才不飘。
    /// <para>
    /// 炼丹品级只存<b>品级那一个数</b>：「这一品能炼到几阶」是品级表的输出，存进来就是同一个事实两处，
    /// 而表一改（比如三品改成能炼四阶）旧存档里那份就悄悄过时了。
    /// </para>
    /// </summary>
    public string Serialize() =>
        JsonSerializer.Serialize(new SavedCrafting(new List<string>(_unlocked), _alchemyRank), _saveJsonOptions);

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009）
        if (fromVersion > Version)
            throw new NotSupportedException($"制作存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedCrafting saved = JsonSerializer.Deserialize<SavedCrafting>(json, _saveJsonOptions)
            ?? throw new InvalidDataException("制作存档内容为空");

        List<string> ids = saved.UnlockedRecipes
            ?? throw new InvalidDataException("制作存档缺少 UnlockedRecipes 数组");

        int rank = ReadAlchemyRank(saved, fromVersion);

        // 先整份校验再落盘：坏存档不该让解锁表停在「读了一半」的状态（同 Inventory）
        var restored = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("制作存档里有空的配方 id");

            // 认不出的 id 不能猜着读：多半是配方改了名或 Mod 被卸掉，留着它 UI 照着去表里取，
            // 会在离病因很远的地方炸
            if (!_recipes.TryGet(id, out _))
                throw new InvalidDataException($"制作存档里的配方 id「{id}」不在配方表里");

            restored.Add(id);
        }

        ReplaceUnlocked(restored);
        _alchemyRank = rank;   // 品级也等整份校验过了才落：坏存档不该改掉玩家现在的品级
    }

    /// <summary>
    /// 存档里的炼丹品级。<b>「字段不在」与「字段是某个值」分开对待</b>：
    /// <list type="bullet">
    /// <item>Version 1 的存档本来就没有这一列——那时品级还不存在，读成<b>最低一档</b>（新档的起点同款）。</item>
    /// <item>Version 2 起这一列必须有：缺了就是坏档，<b>不猜成起点</b>——猜着读会把「存档被截断」
    /// 变成「玩家掉了品级」，而两者在游戏里长得一模一样。</item>
    /// <item>认不出的品级（0 品、十品、表里没有的数）同样是坏档：多半是品级表改过或 Mod 被卸掉。</item>
    /// </list>
    /// </summary>
    private int ReadAlchemyRank(SavedCrafting saved, int fromVersion)
    {
        if (fromVersion < 2) return _ranks.All[0].Rank;

        int rank = saved.AlchemyRank
            ?? throw new InvalidDataException("制作存档缺少 AlchemyRank");

        if (!_ranks.TryGet(rank, out _))
            throw new InvalidDataException($"制作存档里的炼丹品级 {rank} 不在品级表里");

        return rank;
    }

    /// <summary>整份替换而不是往现有集合里加：读档是整状态覆盖（ADR-009 未定义项备案 #15）。</summary>
    private void ReplaceUnlocked(IEnumerable<string> ids)
    {
        _unlocked.Clear();
        foreach (string id in ids) _unlocked.Add(id);

        RefreshView();
    }

    /// <summary>视图是快照，改完集合必须重建——漏了这一步，UI 会一直看到旧的解锁表。</summary>
    private void RefreshView() => _unlockedView = new List<string>(_unlocked).AsReadOnly();

    /// <summary>
    /// 存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。
    /// <see cref="AlchemyRank"/> 是<b>可空</b>的，为的是把「这一列不在」（Version 1，该迁移）
    /// 与「这一列是 0」（坏档，该抛）分开——两者混为一谈就是猜着读。
    /// </summary>
    private sealed record SavedCrafting(List<string> UnlockedRecipes, int? AlchemyRank);

    /// <summary>字段名与配方 id 都写出来，存档要能被人和 Mod 读懂（ADR-012），同 <c>Inventory</c>。</summary>
    private static readonly JsonSerializerOptions _saveJsonOptions = new() { WriteIndented = true };
}
