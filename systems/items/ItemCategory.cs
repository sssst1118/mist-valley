namespace XingGame.Systems.Items;

/// <summary>
/// 物品分类（§17.2 背包菜单的分类）。取值全部来自设计文档：§12.1 商店卖的东西、
/// §12.3 配方里的材料、§17.2 的分类菜单，没有一个是我编的。
/// 序列化成名字（"Crop"）而非数字：往枚举中间插一个新值，旧存档不会错位（ADR-012）。
/// </summary>
public enum ItemCategory
{
    Tool,       // 工具（锄头、水壶、斧、镐、钓竿）
    Seed,       // 种子
    Crop,       // 作物与采集物
    Material,   // 材料（矿石、锭、木材、煤…）
    Food,       // 食物与酒
    Medicine,   // 丹药与药品
    Equipment,  // 装备与戒指
    Artifact,   // 法宝
    Manual,     // 功法秘籍
    Decor,      // 装饰与家具
    Misc,       // 杂货
}
