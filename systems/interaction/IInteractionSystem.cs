using System.Numerics;

namespace XingGame.Systems.Interaction;

/// <summary>
/// 可交互目标的注册表。桥接层每帧拿玩家位置问一次 <see cref="FindNearest"/>，
/// 拿到的结果决定屏幕上显示什么（ARCHITECTURE「关于『每帧轮询』」）。
/// </summary>
public interface IInteractionSystem
{
    void Register(IInteractable target);

    void Unregister(IInteractable target);

    /// <summary>
    /// 范围内<b>最近</b>的目标（不论 <see cref="IInteractable.CanInteract"/>），没有则 null。
    /// 不论 CanInteract 是刻意的：UI 要能显示「暂时不能」，而不是什么都不显示。
    /// </summary>
    IInteractable? FindNearest(Vector2 from);

    /// <summary>找到目标、且其 CanInteract 为 true 时调用它的 Interact() 并返回 true；否则返回 false。</summary>
    bool TryInteract(Vector2 from);
}
