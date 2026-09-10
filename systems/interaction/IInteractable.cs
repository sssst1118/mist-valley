using System.Numerics;

namespace XingGame.Systems.Interaction;

/// <summary>
/// 场景里一个「可以被玩家互动的东西」。纯 C# 接口，不认识 Godot（ADR-002）——
/// 桥接层的可交互物节点负责把自身位置换算成 <see cref="Vector2"/> 后报上来（ADR-010）。
/// </summary>
public interface IInteractable
{
    /// <summary>世界坐标（像素）。</summary>
    Vector2 Position { get; }

    /// <summary>可交互半径（像素）。每个目标自带一个，不做全局统一半径。</summary>
    float Radius { get; }

    /// <summary>条件不满足时仍能被找到，但 <see cref="Interact"/> 不应有副作用。</summary>
    bool CanInteract { get; }

    /// <summary>显示文案，如「浇水」。</summary>
    string Prompt { get; }

    void Interact();
}
