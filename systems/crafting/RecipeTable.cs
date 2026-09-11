using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using XingGame.Systems.Items;

namespace XingGame.Systems.Crafting;

/// <summary>
/// 静态配方表，数据在 <c>data/crafting/recipes.json</c>。加载时逐条校验，并用物品表交叉校验
/// （照 <see cref="ItemTable"/>、<c>CropTable</c> 的先例）。
/// </summary>
/// <remarks>
/// <para>
/// <b>共录八条，每条都有出处，一条都不多</b>：§12.3 点名的五条制作配方
/// （<c>docs/public/design.md</c> 第 1343-1353 行）：洒水器、稻草人、樱桃炸弹、筑基丹、聚灵阵；
/// §12.4 点名的三道料理（第 1364-1368 行）：煎蛋、南瓜派、灵芽羹。<b>材料与数量逐条照抄，一个数都没编</b>。
/// §12.3 那句「配方分类：工具、设备、装饰、消耗品、武器、戒指、丹药、法宝、阵法」只当作
/// <see cref="RecipeCategory"/> 的取值来源——<b>不照着分类去补配方</b>（那九个词是分类名，不是配方名）。
/// </para>
/// <para>
/// <b>§12.4 那句「配方：100+ 料理」一条都不录</b>：它只给了总数、没给名单，「100+」不是 100 条可抄的
/// 配方。真正的边界是「<b>文档点了名才录</b>」——三道点名的料理进表，那 100 多条没名字的留在原地。
/// 三道料理的<b>效果</b>（恢复体力/灵力、耕种 +2、修炼速度 +20%）本切片<b>不建字段</b>：要有一套增益
/// 系统才有人消费，那是 M3 的事；效果文字先写进产物的 <c>description</c>（游戏里看得见），
/// 等增益系统落地再挪进表里，别提前留一个没人读的字段。
/// </para>
/// <para>
/// 十件产物与两种材料（小麦粉、糖）的物品定义在本模块自己的 <c>data/items/crafting.json</c> 里
/// （M2-B 共同规矩第 4 条），其余材料一律<b>引用</b>既有物品（<c>items.json</c> 与畜牧切片的
/// <c>ranching.json</c>），不在本模块重复定义。三处<b>文档未给</b>的取值如下（都不另立新规矩）：
/// </para>
/// <list type="bullet">
/// <item><b>产物的背包分类</b>取 <see cref="ItemCategory"/>（不是 <see cref="RecipeCategory"/>）：
/// 洒水器 <c>Equipment</c>（§6.8 把洒水器归在「设备」表）、稻草人 <c>Decor</c>、筑基丹 <c>Medicine</c>
/// （§8.7 二阶丹药表）、三道料理 <c>Food</c>（能吃的归食物）、小麦粉与糖 <c>Material</c>
/// （它们是下锅的材料，不是端上桌的料理）、樱桃炸弹与聚灵阵 <c>Misc</c>——后两者的配方分类
/// 「消耗品」「阵法」在背包分类里没有对应值，<c>Misc</c> 是唯一不撒谎的落点。</item>
/// <item><b>三道料理的配方分类</b>用 <c>Consumable</c>：§12.3 那九个分类名里<b>没有「料理」</b>
/// （烹饪本就是 §12.4 的另一套系统，§17.2 里「制作」与「烹饪」也是两个菜单），而料理是一次性吃掉的。
/// 不为了让分类好看而新造一个文档里没有的分类名。</item>
/// <item><b>堆叠上限</b>一律 <c>999</c>，跟随 M1-4 定下的约定：除工具外都可堆叠
/// （<c>ItemTableTests</c> 有断言守着）。「放置类设备该不该可堆叠」文档没说，不在这里替它定；
/// 小麦粉与糖的价格文档也没给，两个价格字段都填 0（文档未给，待补）。</item>
/// </list>
/// </remarks>
public sealed class RecipeTable : IRecipeTable
{
    /// <summary>缺省配方表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/crafting/recipes.json";

    private readonly Dictionary<string, RecipeDefinition> _byId;
    private readonly ReadOnlyCollection<RecipeDefinition> _all;

    private RecipeTable(Dictionary<string, RecipeDefinition> byId, ReadOnlyCollection<RecipeDefinition> all)
    {
        _byId = byId;
        _all = all;
    }

    /// <summary>按 JSON 里的顺序排列，配方列表 UI 直接照用。</summary>
    public IReadOnlyCollection<RecipeDefinition> All => _all;

    public bool TryGet(string id, out RecipeDefinition definition)
    {
        if (_byId.TryGetValue(id, out RecipeDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;   // out 必须先赋值：找不到以返回值 false 表达，输出用 null（同 ItemTable）
        return false;
    }

    public RecipeDefinition Get(string id) =>
        _byId.TryGetValue(id, out RecipeDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"配方表里没有 id 为「{id}」的配方");

    /// <param name="items">用于交叉校验两张表是否对得上——这是本方法需要物品表的原因，不是可选装饰。</param>
    public static RecipeTable FromJson(string json, IItemTable items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("recipes", out JsonElement recipes) ||
            recipes.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("配方表缺少 recipes 数组");
        }

        var byId = new Dictionary<string, RecipeDefinition>(StringComparer.Ordinal);
        var all = new List<RecipeDefinition>();

        foreach (JsonElement element in recipes.EnumerateArray())
        {
            RecipeDefinition definition = ParseRecipe(element);

            // 重复 id 会让「按 id 取到的是哪一条」取决于文件顺序，而两条的材料可能不同——数据错误，启动即报
            if (!byId.TryAdd(definition.Id, definition))
                throw new InvalidDataException($"配方表出现重复 id：{definition.Id}");

            all.Add(definition);
        }

        CrossCheck(all, items);

        return new RecipeTable(byId, all.AsReadOnly());
    }

    /// <param name="items">同 <see cref="FromJson"/>。</param>
    public static RecipeTable FromFile(string path, IItemTable items) => FromJson(File.ReadAllText(path), items);

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省配方表，与 <c>ItemTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    /// <param name="items">同 <see cref="FromJson"/>。</param>
    public static RecipeTable LoadDefault(IItemTable items)
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path, items);
    }

    /// <summary>
    /// 配方与物品表对不上就是数据错误：产物或材料在物品表里找不到，玩家做出成品、或凑齐材料那一刻才发现
    /// 取不到东西。这里一次把全部对不上的 id 都列出来——一条一条修比每次重跑才发现下一条快得多
    /// （同 <c>CropTable</c> 的做法）。
    /// </summary>
    private static void CrossCheck(List<RecipeDefinition> recipes, IItemTable items)
    {
        var missing = new List<string>();

        foreach (RecipeDefinition recipe in recipes)
        {
            if (!items.TryGet(recipe.OutputItemId, out _)) missing.Add(recipe.OutputItemId);

            foreach (RecipeIngredient ingredient in recipe.Ingredients)
                if (!items.TryGet(ingredient.ItemId, out _)) missing.Add(ingredient.ItemId);
        }

        if (missing.Count > 0)
            throw new InvalidDataException($"配方表里有物品表找不到的 id：{string.Join("、", missing)}");
    }

    private static RecipeDefinition ParseRecipe(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("配方表出现不是对象的条目");

        string? id = OptionalString(element, "id");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("配方表出现空 id");

        string categoryText = RequiredString(element, "category", id!);
        if (!TryParseCategory(categoryText, out RecipeCategory category))
            throw new InvalidDataException($"配方 {id} 的分类「{categoryText}」不是合法的 RecipeCategory");

        string outputItemId = RequiredString(element, "outputItemId", id!);
        if (string.IsNullOrWhiteSpace(outputItemId))
            throw new InvalidDataException($"配方 {id} 的 outputItemId 为空");

        // 零产出等于白扣材料，负产出更是无意义；「做几次出几个」是制作份数管的事，不是这里的字段
        int outputCount = RequiredInt(element, "outputCount", id!);
        if (outputCount <= 0)
            throw new InvalidDataException($"配方 {id} 的 outputCount 为 {outputCount}，必须为正");

        return new RecipeDefinition(id!, category, outputItemId, outputCount, ParseIngredients(element, id!));
    }

    /// <summary>
    /// 材料列表。空配方、非正数量、同一样材料写两遍，三种都是数据错误：空配方等于白送成品，
    /// 而同一材料写两遍时「到底要几个」取决于把哪一条算数——真想要两个就该写成一条、数量填 2。
    /// </summary>
    private static IReadOnlyList<RecipeIngredient> ParseIngredients(JsonElement recipe, string id)
    {
        if (!recipe.TryGetProperty("ingredients", out JsonElement ingredients) ||
            ingredients.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"配方 {id} 缺少 ingredients 数组");
        }

        // 没有材料的配方是白送成品：要么是漏写了材料，要么根本不该是一条配方
        if (ingredients.GetArrayLength() == 0)
            throw new InvalidDataException($"配方 {id} 的 ingredients 是空的：配方至少要一样材料");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parsed = new List<RecipeIngredient>(ingredients.GetArrayLength());

        foreach (JsonElement element in ingredients.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"配方 {id} 的 ingredients 里出现不是对象的条目");

            string itemId = RequiredString(element, "itemId", id);
            if (string.IsNullOrWhiteSpace(itemId))
                throw new InvalidDataException($"配方 {id} 的材料缺 itemId");

            int count = RequiredInt(element, "count", id);
            if (count <= 0)
                throw new InvalidDataException($"配方 {id} 的材料 {itemId} 数量为 {count}，必须为正");

            if (!seen.Add(itemId))
                throw new InvalidDataException($"配方 {id} 的材料 {itemId} 出现了两次：要两个就写成一条、数量填 2");

            parsed.Add(new RecipeIngredient(itemId, count));
        }

        return parsed.AsReadOnly();
    }

    /// <summary>
    /// 分类名按<b>大小写不敏感</b>匹配（与 <c>ItemTable</c> 解析物品分类、<c>WeatherTable</c> 解析季节
    /// 一致，Mod 作者手写 JSON 时不必猜大小写）。
    /// <para>
    /// 不用 <c>Enum.TryParse</c>：它会把 "3" 解析成序号为 3 的 <see cref="RecipeCategory"/>，
    /// 于是「按数字序号写分类」这种 ADR-012 明确要避开的事会被静默接受，而序号会随枚举插值错位。
    /// </para>
    /// </summary>
    private static bool TryParseCategory(string text, out RecipeCategory category)
    {
        foreach (RecipeCategory candidate in Enum.GetValues<RecipeCategory>())
        {
            if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                category = candidate;
                return true;
            }
        }

        category = default;
        return false;
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上 id 才定位得到那一行。</summary>
    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string RequiredString(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"配方 {id} 缺少字符串字段 {property}");

        return value.GetString()!;
    }

    private static int RequiredInt(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"配方 {id} 缺少数字字段 {property}");

        // 用 TryGetInt32 而不是 GetInt32：手写 JSON 里把数量写成 1.5 或 1e30 都是常事，
        // 而 GetInt32 会抛一条不带 id 的 FormatException——报错里没有 id，就得自己去翻表
        if (!value.TryGetInt32(out int parsed))
            throw new InvalidDataException($"配方 {id} 的 {property} 不是整数：{value.GetRawText()}");

        return parsed;
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
