namespace XingGame.Systems.Npc;

/// <summary>
/// 送礼的五个喜好档，抄自 §10.2「送礼」那一行（喜爱 / 喜欢 / 中立 / 不喜欢 / 讨厌）。
/// </summary>
public enum GiftTaste
{
    Loved,
    Liked,
    Neutral,
    Disliked,
    Hated,
}

/// <summary>
/// §10.2 送礼的加点表：<c>送礼：+80（喜爱 +120，喜欢 +80，中立 +40，不喜欢 -20，讨厌 -40）</c>。
/// </summary>
/// <remarks>
/// 数值全部照抄，一个都没改——这五个数是设计文档直接给的，不是我们调的平衡。
/// 注意「喜欢 +80」与不带档位的基础 +80 是同一个数，文档自己就是这么写的。
/// </remarks>
public static class GiftPoints
{
    public const int Loved = 120;
    public const int Liked = 80;
    public const int Neutral = 40;
    public const int Disliked = -20;
    public const int Hated = -40;

    /// <summary>§10.2「生日送礼：×8 倍」。</summary>
    public const int BirthdayMultiplier = 8;

    /// <summary>
    /// 档位对应的加点。
    /// </summary>
    /// <remarks>
    /// <b>五档都要有值，哪怕当前只有两档取得到</b>：<see cref="GiftTaste.Liked"/> 与
    /// <see cref="GiftTaste.Disliked"/> 在附录 C 里没有数据来源（那张表只给了「喜爱物品」「讨厌物品」
    /// 两列），但数值本身是文档给的，删掉它们就得等对接物品表时再抄一遍。
    /// </remarks>
    public static int For(GiftTaste taste) => taste switch
    {
        GiftTaste.Loved => Loved,
        GiftTaste.Liked => Liked,
        GiftTaste.Neutral => Neutral,
        GiftTaste.Disliked => Disliked,
        GiftTaste.Hated => Hated,
        _ => throw new System.ArgumentOutOfRangeException(nameof(taste), taste, "未知的送礼档位"),
    };
}
