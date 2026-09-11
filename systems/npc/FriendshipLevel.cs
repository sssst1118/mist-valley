namespace XingGame.Systems.Npc;

/// <summary>
/// 好感度阶段。抄自 §10.4：<c>0-2 心（陌生）、3-5 心（友好）、6-8 心（亲密）、9-10 心（挚爱）</c>。
/// </summary>
public enum FriendshipLevel
{
    Stranger,
    Friendly,
    Close,
    Beloved,
}

/// <summary>
/// 心数 → 阶段的换算。**§10.4 是这四个档唯一的一处定义**，别处不许再切一份。
/// </summary>
/// <remarks>
/// 分档按「心」而不是按「点」：文档这两条本来就是分开写的——§10.2 给点数与每心 250 点，
/// §10.4 给阶段。若这里按点数切，每心多少点一变（比如以后调成 200），两个地方就各调各的。
/// </remarks>
public static class FriendshipLevels
{
    /// <summary>友好档的起始心数（§10.4「3-5 心」）。</summary>
    public const int FriendlyHearts = 3;

    /// <summary>亲密档的起始心数（§10.4「6-8 心」）。</summary>
    public const int CloseHearts = 6;

    /// <summary>挚爱档的起始心数（§10.4「9-10 心」）。</summary>
    public const int BelovedHearts = 9;

    /// <summary>越界的心数按就近的档算：上限未封顶时也不该炸在 UI 上。</summary>
    public static FriendshipLevel FromHearts(int hearts) => hearts switch
    {
        < FriendlyHearts => FriendshipLevel.Stranger,
        < CloseHearts => FriendshipLevel.Friendly,
        < BelovedHearts => FriendshipLevel.Close,
        _ => FriendshipLevel.Beloved,
    };
}
