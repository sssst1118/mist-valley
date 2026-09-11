using System;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;

namespace XingGame.Systems.Economy;

/// <summary>
/// 金币钱包。<b>只装金币</b>——灵石不在这里，它是物品。
/// </summary>
/// <remarks>
/// <para>
/// 纯 C#，不认识 Godot，也<b>不认识物品表与商店</b>：钱怎么来的（种地、卖货、任务）是别人的事。
/// </para>
/// <para>
/// <b>为什么灵石不是货币：</b>§4.6 把「金币：500、灵石：0」并列成初始资源、§17.1 的 HUD 也把它
/// 显示出来，看着像两种货币。但灵石同时是 §6.7 的建筑材料（「灵石×100」），而
/// <b>同一个事实不能存两处</b>——钱包与背包各存一份灵石的话，配方从背包吃掉 100 个之后，
/// 钱包那份就对不上了，而这种分歧只会在很久以后以莫名其妙的现象显形
/// （同 <c>worldSeed</c> 不许在 meta 与 blob 各存一份的理由）。
/// </para>
/// <para>
/// 于是灵石是物品（<c>material_spirit_stone</c>，住在背包里）：§4.6 的「灵石：0」就是
/// 「背包里有 0 个」，§17.1 的 HUD 只是把它读出来显示，不需要第二份状态。
/// <b>这是刻意的，不是漏做</b>——有用例钉着（见 <c>EconomyTests.灵石走背包_不走钱包</c>）。
/// </para>
/// </remarks>
public sealed class Wallet : IEconomySystem, ISaveable
{
    public int Gold { get; private set; }

    /// <summary>
    /// 加钱。<b>溢出当场抛</b>（<see cref="OverflowException"/>）而不是回绕：余额回绕成负数
    /// 会让「钱不够」这个判断从此为真，症状出现在离病因很远的购买失败上；天文数字的入账
    /// 必然是某处算错了数量。
    /// </summary>
    public void AddGold(int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "加钱的数量必须为正；扣钱请用 TrySpendGold");

        checked { Gold += amount; }
    }

    /// <summary>
    /// 扣钱，全有或全无。先比较、后相减的顺序就是这条承诺本身——
    /// 反过来写（先扣再比）在余额不足时会留下一个已经被改小的余额。
    /// </summary>
    public bool TrySpendGold(int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "扣钱的数量必须为正");

        if (Gold < amount) return false;

        Gold -= amount;
        return true;
    }

    public string SaveKey => "economy";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    public int Version => 1;

    public string Serialize() => JsonSerializer.Serialize(new SavedWallet(Gold), SaveJsonOptions);

    /// <summary>
    /// 读档。<b>缺字段要抛，多字段要忍。</b>
    /// </summary>
    /// <remarks>
    /// 缺了说明写档的那一半没写完，必须停下来；多了说明存档来自另一个形态——比如曾经那份
    /// 「钱包里的灵石」余额（那次裁定已废，灵石归了背包）。多一个字段就炸的解析会让自己的
    /// 旧档读不回来，而 <c>System.Text.Json</c> 默认就忽略认不出的字段，这里不必额外收紧。
    /// </remarks>
    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009 同款理由）
        if (fromVersion > Version)
            throw new NotSupportedException($"钱包存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedWallet saved = JsonSerializer.Deserialize<SavedWallet>(json, SaveJsonOptions)
            ?? throw new InvalidDataException("钱包存档内容为空");

        int gold = saved.Gold ?? throw new InvalidDataException("钱包存档缺少 Gold 字段");

        if (gold < 0)
            throw new InvalidDataException($"钱包存档的金币为 {gold}");

        // 整状态覆盖：读回来的就是全部，不跟内存里那份做任何加法
        Gold = gold;
    }

    /// <summary>
    /// 存档 JSON 的形态。<c>int?</c> 是为了把「字段不在」与「字段是 0」分开——不可空的话
    /// 缺字段会静默读成 0，那就是在猜着读了。私有：外部只该经
    /// <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。
    /// </summary>
    private sealed record SavedWallet(int? Gold);

    /// <summary>字段名写出来，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private static readonly JsonSerializerOptions SaveJsonOptions = new() { WriteIndented = true };
}
