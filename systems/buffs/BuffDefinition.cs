using System;
using XingGame.Core.Time;

namespace XingGame.Systems.Buffs;

/// <summary>
/// 一条限时增益：作用在哪个属性上、乘多少、持续多久。数据在 <c>data/buffs/buffs.json</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>时长只记一种单位——游戏分钟</b>（一天 = 1440 分钟，见 <see cref="GameTime"/> 的日长）：
/// 聚气散的「7 天」在表里就是 10080、轻身术的「1 游戏小时」是 60。同一个字段只有一个含义，
/// 读表的人不用先判断「这一行是天还是小时」（那种字段迟早会被按另一边的单位填一次）。
/// </para>
/// <para>
/// <b>只有乘数，没有「加法修正」</b>：今天有出处的两条增益（聚气散 +50%、轻身术 +20%）都是乘数，
/// 而唯一那条加法（南瓜派「耕种 +2」）连目标属性都还不存在——见 <see cref="BuffTarget"/>。
/// </para>
/// </remarks>
public sealed record BuffDefinition(
    string Id,
    string Name,
    BuffTarget Target,
    double Multiplier,
    int DurationMinutes)
{
    /// <summary>
    /// 在 <paramref name="appliedAt"/> 这一刻施加，什么时候到期（绝对游戏分钟，**右端不含**）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>到期时刻是绝对时间，不是「还剩几分钟」</b>：相对量在跨日、跨季、跨年与读档之后都会漂，
    /// 而绝对时刻只由「施加那一刻 + 时长」决定，重算多少次都是同一个数——跨过午夜也算得对，
    /// 因为 <see cref="GameTime.TotalMinutes"/> 在午夜并不倒流（见它的注释）。
    /// </para>
    /// <para>
    /// <b>溢出当场抛</b>：<see cref="GameTime.TotalMinutes"/> 是 int，两个 int 相加溢出会得到一个
    /// **过去的**到期时刻——症状是「这颗丹吃了跟没吃一样」，而没有任何报错。它只在手改存档把年份
    /// 写到一万三千年（那时 TotalMinutes 已逼近 int.MaxValue）之后才可能发生，但那一下该发生的是
    /// 当场抛，不是悄悄吞掉（同备案 #65 对金币溢出取的做法）。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">加上时长会溢出 int。</exception>
    public int ExpiresAt(GameTime appliedAt)
    {
        int start = appliedAt.TotalMinutes;

        if (start > int.MaxValue - DurationMinutes)
            throw new ArgumentOutOfRangeException(
                nameof(appliedAt), start,
                $"在游戏第 {start} 分钟施加「{Name}」会让到期时刻溢出 int——这个时刻不是正常游玩能到的");

        return start + DurationMinutes;
    }
}
