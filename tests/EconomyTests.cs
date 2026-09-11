using System;
using System.IO;
using System.Reflection;
using XingGame.Systems.Economy;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 金币钱包（M2 经济与商店）。守三件事：<b>扣款全有或全无</b>、<b>只装金币</b>、<b>存档兼容</b>。
/// </summary>
/// <remarks>
/// 「扣款失败余额一分不变」这一条是本切片最容易写错的地方——先扣再比、或者扣到一半
/// 发现不够，两种写法都不会报错，只会让玩家的钱包莫名其妙少钱。所以它既有正向用例
/// （余额不足返回 false），也有「恰好够」的边界用例，还做过反证。
/// <para>
/// 注意：商店那条「金币不足时买卖双方都不变」<b>验不出钱包这一层</b>——商店在扣款前
/// 先比过一次余额，所以钱包就算写坏了，商店看上去也是对的。防御纵深会让「打坏一层」
/// 在表层看不出来，每一层都得有自己的用例，这一条就是钱包那一层的。
/// </para>
/// </remarks>
public class EconomyTests
{
    private readonly Wallet _wallet = new();

    [Fact]
    public void 新钱包_金币是_0()
    {
        // 初始资源不在这里发：§4.6 的「金币 500」由组合根在建档时 AddGold 进来
        Assert.Equal(0, _wallet.Gold);
    }

    [Fact]
    public void 加钱_余额增加()
    {
        _wallet.AddGold(500);

        Assert.Equal(500, _wallet.Gold);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 加钱_零或负数_抛(int amount)
    {
        // 用负数加法来表达「扣」会让调用点的意图看不出来，扣钱有 TrySpendGold
        Assert.Throws<ArgumentOutOfRangeException>(() => _wallet.AddGold(amount));
    }

    [Fact]
    public void 扣钱_余额够_成功且余额减少()
    {
        _wallet.AddGold(500);

        Assert.True(_wallet.TrySpendGold(120));
        Assert.Equal(380, _wallet.Gold);
    }

    [Fact]
    public void 扣钱_余额不足_返回_false_且余额一分不变()
    {
        _wallet.AddGold(100);

        Assert.False(_wallet.TrySpendGold(101));

        // 「一分不变」是这条用例存在的全部理由：先扣再比也会返回 false，
        // 但余额已经变成 -1 了——调用方以为失败，钱却已经没了
        Assert.Equal(100, _wallet.Gold);
    }

    [Fact]
    public void 扣钱_恰好花光_余额归零而不是负数()
    {
        _wallet.AddGold(500);

        Assert.True(_wallet.TrySpendGold(500));
        Assert.Equal(0, _wallet.Gold);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 扣钱_零或负数_抛(int amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _wallet.TrySpendGold(amount));
    }

    [Fact]
    public void 加钱溢出_抛_且余额不变()
    {
        _wallet.AddGold(int.MaxValue);

        // 回绕成负数会让「钱不够」这个判断从此为真，症状出现在离病因很远的购买失败上
        Assert.Throws<OverflowException>(() => _wallet.AddGold(1));
        Assert.Equal(int.MaxValue, _wallet.Gold);
    }

    // ——— 裁定：灵石是物品，不是货币 ———

    [Fact]
    public void 灵石走背包_不走钱包()
    {
        // §4.6 把「金币：500、灵石：0」并列成初始资源、§17.1 的 HUD 也显示灵石，看着像两种货币；
        // 但灵石同时是 §6.7 的建筑材料（「灵石×100」）。**同一个事实不能存两处**——
        // 钱包与背包各存一份的话，配方从背包吃掉 100 个之后钱包那份就对不上了，
        // 而这种分歧只会在很久以后以莫名其妙的现象显形（同 worldSeed 不许存两份的理由）。
        var inventory = new Inventory(ItemTable.LoadDefault(), slotCount: 4);

        Assert.Equal(0, inventory.Add("material_spirit_stone", 100));
        Assert.Equal(100, inventory.Count("material_spirit_stone"));

        // 钱包上不该存在任何灵石入口。将来谁把它加回来，这条会红——
        // 它的意义就是让人知道「灵石不在钱包里」是刻意的，不是漏做
        Assert.DoesNotContain(
            typeof(Wallet).GetMembers(), member => member.Name.Contains("Spirit", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(IEconomySystem).GetMembers(), member => member.Name.Contains("Spirit", StringComparison.Ordinal));
    }

    // ——— 存档 ———

    [Fact]
    public void 存档键与版本()
    {
        Assert.Equal("economy", _wallet.SaveKey);
        Assert.Equal(1, _wallet.Version);
    }

    [Fact]
    public void 存档往返_金币还原()
    {
        _wallet.AddGold(1234);

        string json = _wallet.Serialize();
        var restored = new Wallet();
        restored.Deserialize(json, _wallet.Version);

        Assert.Equal(1234, restored.Gold);
    }

    [Fact]
    public void 存档往返_金币为_0_也能读写()
    {
        string json = _wallet.Serialize();

        var restored = new Wallet();
        restored.Deserialize(json, _wallet.Version);

        Assert.Equal(0, restored.Gold);
    }

    [Fact]
    public void 存档_字段名是_Gold()
    {
        _wallet.AddGold(500);

        // 字段名写出来，存档要能被人和 Mod 读懂（ADR-012）
        Assert.Contains("\"Gold\": 500", _wallet.Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void 存档_版本高于当前_抛_NotSupportedException()
    {
        // 来自更新版本的存档不能猜着读（ADR-009）
        var wallet = new Wallet();
        Assert.Throws<NotSupportedException>(() => wallet.Deserialize("""{ "Gold": 500 }""", fromVersion: 2));
    }

    [Fact]
    public void 存档_多出来的未知字段被忽略()
    {
        // 旧形态里有一份「钱包里的灵石」（那次裁定已废，灵石归了背包）。多一个字段就炸的
        // 解析会让自己的旧档读不回来，而存档兼容正是要避免这个
        var wallet = new Wallet();
        wallet.Deserialize("""{ "Gold": 500, "SpiritStone": 7 }""", wallet.Version);

        Assert.Equal(500, wallet.Gold);
    }

    [Theory]
    [InlineData("""{ }""")]                          // 写档的那一半没写完
    [InlineData("""{ "SpiritStone": 7 }""")]         // 通篇没有 Gold
    public void 存档_缺_Gold_字段_抛(string json)
    {
        var wallet = new Wallet();
        Assert.Throws<InvalidDataException>(() => wallet.Deserialize(json, wallet.Version));
    }

    [Fact]
    public void 存档_负金币_抛()
    {
        var wallet = new Wallet();
        Assert.Throws<InvalidDataException>(() => wallet.Deserialize("""{ "Gold": -1 }""", wallet.Version));
    }

    [Fact]
    public void 读档是整状态覆盖_不是相加()
    {
        _wallet.AddGold(500);

        _wallet.Deserialize("""{ "Gold": 1 }""", _wallet.Version);

        Assert.Equal(1, _wallet.Gold);
    }
}
