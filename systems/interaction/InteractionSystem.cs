using System;
using System.Collections.Generic;
using System.Numerics;

namespace XingGame.Systems.Interaction;

/// <summary>
/// <see cref="IInteractable"/> 的注册表（纯 C#，ADR-002）。桥接层进树时登记、离树时退订，
/// 每帧拿玩家位置问一次「附近有什么」。
/// </summary>
/// <remarks>
/// 三条裁决规则及其理由：
/// <list type="number">
///   <item>够不够得着用<b>目标自己的</b> <see cref="IInteractable.Radius"/> 判，不做全局统一半径——
///         一口井和一座牌坊的「够得着」本就不该一样，统一半径会逼着每个目标长成同一尺寸。</item>
///   <item>半径取<b>闭区间</b>：距离恰好等于半径算范围内。开闭必须挑一个钉死——
///         留着浮点上的模糊地带，「走到边上却没反应」就成了随坐标漂移的灵异现象。</item>
///   <item><b>等距时先注册者胜出</b>。等距是真会发生的（两个目标对称摆在路两侧），
///         若裁决取决于集合内部顺序，同一份场景在不同运行里会给出不同目标。
///         先注册者即先进入场景者，挑它最可预期。</item>
/// </list>
/// </remarks>
public sealed class InteractionSystem : IInteractionSystem
{
    // 必须保持注册顺序：「等距时先注册者胜出」这条规则以列表顺序为准，故不能用无序集合
    private readonly List<Registration> _targets = new();

    public void Register(IInteractable target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // 重复注册按「已在册」忽略，不留下第二份条目。Godot 节点离树再入树会重跑 _Ready，
        // 若此处多出一份，_ExitTree 里的一次 Unregister 只删得掉一份，剩下的条目就指向
        // 已经销毁的节点 —— 正是 ADR-005 里那种事后极难定位的 stale reference。
        foreach (var registration in _targets)
            if (ReferenceEquals(registration.Target, target)) return;

        _targets.Add(new Registration(target));
    }

    public void Unregister(IInteractable target)
    {
        ArgumentNullException.ThrowIfNull(target);

        for (int index = 0; index < _targets.Count; index++)
        {
            if (!ReferenceEquals(_targets[index].Target, target)) continue;

            // 先标记再移除：别处可能正拿着快照在遍历，那条路径靠 IsActive 判断该不该采纳
            _targets[index].Deactivate();
            _targets.RemoveAt(index);
            return;
        }

        // 没注册过（或已退订）就当已经退订，静默返回：调用方不该为了退订先去查自己在不在册
        // （同 ISaveService.Delete 的幂等先例）
    }

    public IInteractable? FindNearest(Vector2 from)
    {
        IInteractable? nearest = null;
        float nearestDistanceSquared = float.PositiveInfinity;

        // 遍历副本：读取目标的过程中注册表可能被改（ADR-005 同款理由——桥接层节点会在别人的
        // 遍历途中离树），直接遍历原表会撞上「集合被修改」的枚举器异常。
        foreach (var registration in _targets.ToArray())
        {
            // 快照只保证遍历不炸；要不要采纳得看此刻是否仍在册（即 EventBus 的「退订当轮生效」）。
            // 已退订的多半是正在销毁的桥接节点，去读它的 Position 就是碰 stale reference。
            if (!registration.IsActive) continue;

            IInteractable target = registration.Target;
            float distanceSquared = Vector2.DistanceSquared(from, target.Position);
            float radius = target.Radius;

            // 比平方：等价于比距离，但省一次开方，也免得开方的舍入让边界判定飘
            if (distanceSquared > radius * radius) continue;

            // 严格小于 → 等距时先到者留任（见类注释第 3 条）
            if (distanceSquared >= nearestDistanceSquared) continue;

            nearestDistanceSquared = distanceSquared;
            nearest = target;
        }

        return nearest;
    }

    public bool TryInteract(Vector2 from)
    {
        IInteractable? target = FindNearest(from);

        // CanInteract 为假时返回 false 且绝不调 Interact()：「条件不满足」是目标的声明，
        // 不该指望 Interact() 自己自觉。更近的不可用目标也不该被跳过让更远的顶上——
        // 否则玩家站在井边按 E，浇的却是远处那块田。
        if (target is null || !target.CanInteract) return false;

        target.Interact();
        return true;
    }

    /// <summary>
    /// 一次注册的条目，同时也是存活凭据 —— 遍历前查 <see cref="IsActive"/> 才知道该不该采纳。
    /// 与 EventBus 的 Subscription 同构：退订可能发生在别人的遍历途中，快照挡不住这种时序。
    /// </summary>
    private sealed class Registration
    {
        public Registration(IInteractable target) => Target = target;

        public IInteractable Target { get; }

        public bool IsActive { get; private set; } = true;

        public void Deactivate() => IsActive = false;
    }
}
