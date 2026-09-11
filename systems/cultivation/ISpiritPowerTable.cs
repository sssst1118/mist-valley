namespace XingGame.Systems.Cultivation;

/// <summary>
/// 灵力池这张账：上限的公式系数 + 两个恢复速率。数据在 <c>data/cultivation/spirit_power.json</c>，
/// 只读、进程内共享一份。
/// </summary>
/// <remarks>
/// <para>
/// <b>这组数不是设计文档直给</b>：§8.2 只说炼气 4-6 层的「灵气储备可维持**短时**施法」，
/// 上限与恢复速率出自 ARCHITECTURE.md 的未定义项备案 #69（<c>100 + 25 × (层 - 1)</c>，
/// 取值的含义就是让「短时」成立）与 #71（清醒 2 / 打坐 5 / 睡眠全恢复）。
/// </para>
/// <para>
/// <b>法术的消耗刻意不在这里</b>（备案 #70 的轻身术 10 / 小回春术 30 / 灵雨术 50 / 灵锄术 50）：
/// 那四个数跟着**法术**那一刀走，现在录进来就是四个没有调用方的常量（铁律 11）。本表只答
/// 「池子多大、回得多快」，「花得起哪几个法术」是法术表的事。
/// </para>
/// <para>
/// <b>上限由层号算出来，不存第二处</b>：存档里只有「当前灵力」一个数，上限是
/// <see cref="MaxSpiritAt"/> 现算的。存一份上限、又存一份层数，改系数或改层数时两份就会对不上，
/// 而症状要等到某个玩家的灵力条画得比上限还长才显形（同 <c>worldSeed</c> 不许存两处的理由）。
/// </para>
/// </remarks>
public interface ISpiritPowerTable
{
    /// <summary>
    /// 第 <paramref name="stage"/> 层的灵力上限：<c>100 + 25 × (层 - 1)</c>（备案 #69）。
    /// 1 层 100、4 层 175、13 层 400。
    /// </summary>
    /// <remarks>
    /// 层号的上界由境界表回答（炼气 13 层、渡劫期 2 层……），那是 <see cref="IRealmTable"/> 的知识，
    /// 本表不复制一份——调用方是 <see cref="CultivationSystem"/>，它的层号在构造与读档两处都验过。
    /// </remarks>
    /// <exception cref="System.ArgumentOutOfRangeException">层号不是从 1 起的。</exception>
    int MaxSpiritAt(int stage);

    /// <summary>
    /// 某一档活动的恢复速率，单位「灵力 / 游戏小时」（备案 #71：清醒 2、打坐 5）。
    /// </summary>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// 表里没录这一档——枚举有而数据没有是数据缺口，不猜（同 <see cref="ICultivationSpeedTable.SeasonMultiplier"/>）。
    /// 加载时已经要求每一档都有，所以走到这里就是有人给枚举加了新档位却没补数据。
    /// </exception>
    int RecoveryPerHour(SpiritRecovery recovery);
}
