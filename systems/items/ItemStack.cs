namespace XingGame.Systems.Items;

/// <summary>一格：物品 id + 数量。空槽的 Count 为 0、ItemId 为空串。</summary>
public readonly record struct ItemStack(string ItemId, int Count);
