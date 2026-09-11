using System.Collections.Generic;

namespace XingGame.Systems.Crafting;

/// <summary>
/// 静态配方表。只读，进程内共享一份（同 <c>IItemTable</c>/<c>ICropTable</c>）。
/// </summary>
public interface IRecipeTable
{
    /// <summary>按数据文件里的顺序排列，配方列表 UI 直接照用。</summary>
    IReadOnlyCollection<RecipeDefinition> All { get; }

    /// <summary>找不到以返回值 false 表达，不抛。</summary>
    bool TryGet(string id, out RecipeDefinition definition);

    /// <summary>找不到抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>，消息里带 id。</summary>
    RecipeDefinition Get(string id);
}
