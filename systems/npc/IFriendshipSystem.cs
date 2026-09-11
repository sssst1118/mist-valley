namespace XingGame.Systems.Npc;

/// <summary>
/// 好感度。桥接层与将来的对话/任务系统都只认这个接口（M2-A 契约：注册表按接口注册）。
/// </summary>
/// <remarks>
/// <b>点数只按 NPC id 记账，不管「今天聊过没有」</b>：§10.2 里「每日对话 +20」「忽略对话 -2/天」
/// 这类按天判定的条目，前提是有一个记录「今天见过谁」的对话系统——那是 M6 的内容，
/// M2-A 不提前实现（铁律 3）。需要时由调用方自己算好增量再调 <see cref="AddPoints"/>。
/// </remarks>
public interface IFriendshipSystem
{
    /// <summary>当前好感度点数。没交往过的 NPC 是 0。</summary>
    int GetPoints(string npcId);

    /// <summary>心数（§10.2「每心 250 点」），0..10。</summary>
    int GetHearts(string npcId);

    /// <summary>阶段（§10.4 陌生 / 友好 / 亲密 / 挚爱）。</summary>
    FriendshipLevel GetLevel(string npcId);

    /// <summary>
    /// 加（或减）好感度，落在 0..2500 之间。
    /// </summary>
    /// <returns>
    /// **实际**变动量，不是传入的 <paramref name="amount"/>——封顶或触底时两者不同。
    /// 返回实际值，调用方才能判断「这一下白给了」，而不用自己再读一次前后差值。
    /// </returns>
    int AddPoints(string npcId, int amount);

    /// <summary>
    /// 送礼：按 <see cref="NpcDefinition.TasteOf"/> 定档，再加 §10.2 的档位点数；
    /// <paramref name="isBirthday"/> 为真时按 §10.2「生日送礼：×8 倍」翻倍。
    /// </summary>
    /// <returns>同 <see cref="AddPoints"/>：实际变动量。</returns>
    int ReceiveGift(string npcId, string itemName, bool isBirthday);
}
