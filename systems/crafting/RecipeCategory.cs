namespace XingGame.Systems.Crafting;

/// <summary>
/// 配方分类。九个取值<b>全部照抄</b> §12.3（<c>docs/public/design.md</c> 第 1341 行）：
/// 「配方分类：工具、设备、装饰、消耗品、武器、戒指、丹药、法宝、阵法」。
/// </summary>
/// <remarks>
/// 刻意<b>不复用</b> <c>ItemCategory</c>：那是「这东西在背包里算什么」（见 ADR-013 的分工），
/// 而这里是「这条配方做出来的是哪一类东西」。两套分类并不重合——§12.3 的「消耗品」「阵法」
/// 在背包分类里根本没有对应值，硬塞进去只能靠 <c>Misc</c> 兜底，等于把「这条配方是什么」
/// 这一位信息丢掉。反过来也一样：背包分类里的「种子」不是配方分类。
/// </remarks>
public enum RecipeCategory
{
    Tool,        // 工具
    Device,      // 设备
    Decor,       // 装饰
    Consumable,  // 消耗品
    Weapon,      // 武器
    Ring,        // 戒指
    Pill,        // 丹药
    Artifact,    // 法宝
    Formation,   // 阵法
}
