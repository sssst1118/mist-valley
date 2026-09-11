using System;
using System.Numerics;
using XingGame.Systems.Interaction;

namespace XingGame.Tests;

/// <summary>
/// 交互系统的边界（ADR-011）。<see cref="InteractionSystemTests"/> 已把范围、最近者、等距裁决
/// 与遍历中改注册表守得很密，这里只补两条它没走到的分支：**退订后再注册**，以及
/// **提示与下手必须是同一个目标**。
/// </summary>
public sealed class EdgeCaseTests_Interaction
{
    private static readonly Vector2 Origin = Vector2.Zero;

    [Fact]
    public void Unregister_之后再_Register_同一个目标_又能被找到()
    {
        // Godot 节点离树再入树（切场景、被移出再挂回去）走的就是这条：Unregister 再 Register。
        // 若哪天给退订加个「墓碑」以免快照漏判，第二次注册就会被墓碑挡住——
        // 表现为「这个物件再也交互不了」，而且只有重进过场景的玩家才会遇到。
        var system = new InteractionSystem();
        var target = new FakeInteractable(new Vector2(10f, 0f), radius: 50f);

        system.Register(target);
        system.Unregister(target);
        Assert.Null(system.FindNearest(Origin));

        system.Register(target);

        Assert.Same(target, system.FindNearest(Origin));
        Assert.True(system.TryInteract(Origin));
        Assert.Equal(1, target.InteractCount);
    }

    [Fact]
    public void TryInteract_下手的目标与_FindNearest_报的是同一个()
    {
        // 每帧的提示文案来自 FindNearest，按 E 的效果来自 TryInteract。两者算法一旦分岔
        // （例如 TryInteract 自己再挑一次、或跳过不可用的那个另找一个），玩家会看到
        // 「提示写着浇水、按下去浇的是别的田」——这类错位没有任何报错，只能靠用例钉住。
        var system = new InteractionSystem();
        var locked = new FakeInteractable(new Vector2(10f, 0f), radius: 50f, canInteract: false);
        var open = new FakeInteractable(new Vector2(40f, 0f), radius: 50f);
        system.Register(locked);
        system.Register(open);

        Assert.Same(locked, system.FindNearest(Origin));       // 提示显示的是「更近但暂时不可用」的它

        Assert.False(system.TryInteract(Origin));              // 于是按 E 什么都不该发生
        Assert.Equal(0, locked.InteractCount);
        Assert.Equal(0, open.InteractCount);

        // 近的那个恢复可用后，两者必须同时指向它
        locked.CanInteract = true;

        Assert.Same(locked, system.FindNearest(Origin));
        Assert.True(system.TryInteract(Origin));
        Assert.Equal(1, locked.InteractCount);
        Assert.Equal(0, open.InteractCount);
    }

    [Fact]
    public void Register_半径为负_当场抛_而不是变成_更够得着()
    {
        // `distanceSquared > radius * radius` 里负半径被平方成了正数，于是 Radius = -5 的目标
        // 在距离 3 处照样被找到——Inspector 里多打一个负号，效果是「范围反而变大」，方向正好相反，
        // 且没有任何报错。本仓库同类输入（PlayerConfig 的速度、TimeConfig 的流速、Inventory 的槽位数）
        // 一律是构造/注册时就抛，半径没有理由例外。
        var system = new InteractionSystem();

        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            system.Register(new FakeInteractable(new Vector2(3f, 0f), radius: -5f)));

        Assert.Contains("-5", error.Message);
    }

    [Fact]
    public void Register_半径为0或非有限数_也当场抛()
    {
        // 「非法」不只是负数：0 让目标永远够不着，NaN 让所有比较恒为 false（也永远够不着），
        // +∞ 让全世界都在范围内——三者都是配置写错后的静默失效，症状离病因都很远。
        // 只写 `if (radius <= 0) throw` 会漏掉后两个（NaN <= 0 与 +∞ <= 0 都是 false），
        // 所以这条拿它们三个一起钉住「正值**有限**数」这个说法。
        var system = new InteractionSystem();

        foreach (float radius in new[] { 0f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                system.Register(new FakeInteractable(Origin, radius)));
        }
    }

    [Fact]
    public void Register_合法半径_照常工作_且边界仍是闭区间()
    {
        // 校验不该误伤合法值，也不该顺手改掉「距离恰好等于半径算范围内」这条契约定死的开闭
        // （ADR-011 第 2 条）：改成开区间会让「走到边上却没反应」重新变成随坐标漂移的灵异现象。
        // 取 0.5 是因为它在浮点上精确，平方后仍精确相等，能干净地卡在闭区间边界上。
        var system = new InteractionSystem();
        var target = new FakeInteractable(new Vector2(0.5f, 0f), radius: 0.5f);

        system.Register(target);

        Assert.Same(target, system.FindNearest(Origin));
    }

    /// <summary>手写测试替身（不引 Moq），只保留本文件用得着的两个可变项。</summary>
    private sealed class FakeInteractable : IInteractable
    {
        public FakeInteractable(Vector2 position, float radius, bool canInteract = true)
        {
            Position = position;
            Radius = radius;
            CanInteract = canInteract;
        }

        public Vector2 Position { get; }

        public float Radius { get; }

        public bool CanInteract { get; set; }

        public string Prompt => "交互";

        public int InteractCount { get; private set; }

        public void Interact() => InteractCount++;
    }
}
