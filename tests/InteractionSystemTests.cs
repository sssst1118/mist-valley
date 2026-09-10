using System;
using System.Numerics;
using XingGame.Systems.Interaction;

namespace XingGame.Tests;

public class InteractionSystemTests
{
    private const float Radius = 50f;

    /// <summary>「刚好够不着」的偏移。留半像素余量，免得断言卡在浮点边界上说不清。</summary>
    private const float JustOutside = Radius + 0.5f;

    private static FakeInteractable At(
        float x, float y, float radius = Radius, bool canInteract = true, string prompt = "交互") =>
        new(new Vector2(x, y), radius, canInteract, prompt);

    [Fact]
    public void FindNearest_范围内_返回该目标()
    {
        var system = new InteractionSystem();
        var target = At(30f, 0f);
        system.Register(target);

        Assert.Same(target, system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void FindNearest_范围外_返回空()
    {
        var system = new InteractionSystem();
        system.Register(At(JustOutside, 0f));

        Assert.Null(system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void FindNearest_一个目标都没注册_返回空()
    {
        Assert.Null(new InteractionSystem().FindNearest(Vector2.Zero));
    }

    [Fact]
    public void FindNearest_正好落在半径上_算范围内()
    {
        var system = new InteractionSystem();
        var target = At(Radius, 0f);
        system.Register(target);

        // 闭区间是刻意挑的（见 InteractionSystem 类注释第 2 条）。这条把它钉死：
        // 改成开区间、或换个比较方式，都可能在这里翻脸。
        Assert.Same(target, system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void FindNearest_多个目标_返回最近的那个()
    {
        var system = new InteractionSystem();
        var far = At(-40f, 0f);
        var near = At(10f, 0f);
        system.Register(far);    // 先注册的反而更远，确保断言的是「比距离」而不是「先到先得」
        system.Register(near);

        Assert.Same(near, system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void FindNearest_更近的够不着_宁可返回空也不退而求其次()
    {
        // 「范围内最近」不是「最近」：更近的那个在范围外时，不该顺手把更远的那个塞给你
        var system = new InteractionSystem();
        system.Register(At(20f, 0f, radius: 10f));
        system.Register(At(100f, 0f, radius: 10f));

        Assert.Null(system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void FindNearest_半径各用各的_不做全局统一()
    {
        var system = new InteractionSystem();
        var wide = At(80f, 0f, radius: 100f);
        system.Register(At(20f, 0f, radius: 10f));   // 更近，但够不着
        system.Register(wide);

        Assert.Same(wide, system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void FindNearest_等距_先注册者胜出()
    {
        // 对称摆在路两侧是常见的关卡片段，等距必然发生 —— 结果不能取决于集合内部的遍历顺序
        var system = new InteractionSystem();
        var left = At(-30f, 0f);
        var right = At(30f, 0f);
        system.Register(left);
        system.Register(right);

        for (int attempt = 0; attempt < 20; attempt++)
            Assert.Same(left, system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void FindNearest_等距_胜者随注册顺序改变()
    {
        // 与上一条互为对照：调换注册顺序后胜者也跟着换，说明裁决规则确实是「先注册者胜出」，
        // 而不是碰巧总返回同一个对象
        var system = new InteractionSystem();
        var left = At(-30f, 0f);
        var right = At(30f, 0f);
        system.Register(right);
        system.Register(left);

        for (int attempt = 0; attempt < 20; attempt++)
            Assert.Same(right, system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void FindNearest_不可交互的目标_照样能返回()
    {
        // 契约明确要求：FindNearest 不看 CanInteract，UI 才显示得出「暂时不能」
        var system = new InteractionSystem();
        var locked = At(10f, 0f, canInteract: false);
        system.Register(locked);

        Assert.Same(locked, system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void FindNearest_不可交互的更近_仍然返回它()
    {
        // 更近的「暂时不能用」必须压过更远的「能用」，否则玩家站在井边，
        // 屏幕上却提示远处那块田，按 E 还真去浇了那块田
        var system = new InteractionSystem();
        var locked = At(10f, 0f, canInteract: false);
        var open = At(40f, 0f);
        system.Register(open);
        system.Register(locked);

        Assert.Same(locked, system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void Unregister_之后不再被找到()
    {
        var system = new InteractionSystem();
        var target = At(10f, 0f);
        system.Register(target);

        system.Unregister(target);

        Assert.Null(system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void Unregister_只退掉自己_别的还在()
    {
        var system = new InteractionSystem();
        var kept = At(10f, 0f);
        var removed = At(20f, 0f);
        system.Register(kept);
        system.Register(removed);

        system.Unregister(removed);

        Assert.Same(kept, system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void Unregister_没注册过的目标_不抛异常()
    {
        // 幂等（同 ISaveService.Delete 的先例）：调用方不该为了退订先去查自己在不在册
        var system = new InteractionSystem();

        system.Unregister(At(10f, 0f));
    }

    [Fact]
    public void Register_同一目标重复注册_只留一份条目()
    {
        // Godot 节点离树再入树会重跑 _Ready。若重复注册留下两份条目，一次 Unregister
        // 只删得掉一份，剩下的那一条就指向已销毁的节点 —— 之后谁遍历到谁倒霉。
        var system = new InteractionSystem();
        var target = At(10f, 0f);
        system.Register(target);
        system.Register(target);

        system.Unregister(target);

        Assert.Null(system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void Register或Unregister_传null_当场抛异常()
    {
        var system = new InteractionSystem();

        // 与 Subscribe(null) 的先例一致（未定义项备案 #5）：宁可当场炸，
        // 也不要留个空条目等若干帧后炸在别处的 NRE 里
        Assert.Throws<ArgumentNullException>(() => system.Register(null!));
        Assert.Throws<ArgumentNullException>(() => system.Unregister(null!));
    }

    [Fact]
    public void TryInteract_目标可用_返回真且只调用一次()
    {
        var system = new InteractionSystem();
        var target = At(10f, 0f);
        system.Register(target);

        Assert.True(system.TryInteract(Vector2.Zero));
        Assert.Equal(1, target.InteractCount);
    }

    [Fact]
    public void TryInteract_目标不可用_返回假且不调用()
    {
        var system = new InteractionSystem();
        var target = At(10f, 0f, canInteract: false);
        system.Register(target);

        Assert.False(system.TryInteract(Vector2.Zero));
        Assert.Equal(0, target.InteractCount);
    }

    [Fact]
    public void TryInteract_范围外_返回假且不调用()
    {
        var system = new InteractionSystem();
        var target = At(JustOutside, 0f);
        system.Register(target);

        Assert.False(system.TryInteract(Vector2.Zero));
        Assert.Equal(0, target.InteractCount);
    }

    [Fact]
    public void TryInteract_没有目标_返回假()
    {
        Assert.False(new InteractionSystem().TryInteract(Vector2.Zero));
    }

    [Fact]
    public void TryInteract_更近的不可用时_不拿更远的顶上()
    {
        var system = new InteractionSystem();
        var locked = At(10f, 0f, canInteract: false);
        var open = At(40f, 0f);
        system.Register(locked);
        system.Register(open);

        Assert.False(system.TryInteract(Vector2.Zero));
        Assert.Equal(0, locked.InteractCount);
        Assert.Equal(0, open.InteractCount);
    }

    [Fact]
    public void FindNearest_遍历途中被退订_不抛异常且连读都不读它()
    {
        // 桥接层的真实时序：一个目标在别人被读取的过程中离树（_ExitTree 里退订）。
        // 遍历走的是快照，所以不该出现「集合被修改」的枚举器异常；
        // 而存活检查保证已退订的那个连 Position 都不再被碰（同 ADR-005「退订当轮生效」）。
        var system = new InteractionSystem();
        var mutator = At(30f, 0f);
        var victim = At(5f, 0f);      // 更近：只要被读到就会胜出
        system.Register(mutator);     // 先注册 → 先被遍历到，于是能在 victim 之前动手
        system.Register(victim);

        bool victimWasRead = false;
        mutator.OnReadPosition = () => system.Unregister(victim);
        victim.OnReadPosition = () => victimWasRead = true;

        Assert.Same(mutator, system.FindNearest(Vector2.Zero));
        Assert.False(victimWasRead);
    }

    [Fact]
    public void FindNearest_遍历途中被注册的新目标_本轮不参与()
    {
        // 快照的代价：本轮少看一个新来的（与 ADR-005 是同一笔取舍）——宁可晚一帧看见，
        // 也不为了「立刻看见」去遍历原表，那等于把枚举器暴露给别人的回调
        var system = new InteractionSystem();
        var existing = At(40f, 0f);
        var newcomer = At(5f, 0f);    // 若参与就必然胜出
        system.Register(existing);
        existing.OnReadPosition = () => system.Register(newcomer);

        Assert.Same(existing, system.FindNearest(Vector2.Zero));
    }

    [Fact]
    public void TryInteract_目标在交互中退了别人的订_不抛异常()
    {
        // 交互的副作用里退订别的目标是常见写法（例如采集完把自己和别人一起摘掉）
        var system = new InteractionSystem();
        var other = At(60f, 0f);
        var target = At(10f, 0f);
        system.Register(other);
        system.Register(target);
        target.OnInteract = () => system.Unregister(other);

        Assert.True(system.TryInteract(Vector2.Zero));
        Assert.Null(system.FindNearest(new Vector2(100f, 0f)));   // other 已退订：从它附近也找不到
    }

    /// <summary>
    /// 手写测试替身（不引 Moq）。两个钩子各有用处：<see cref="OnReadPosition"/> 用来模拟
    /// 「别人遍历我的途中，注册表被改了」，<see cref="OnInteract"/> 用来验证交互的副作用时序。
    /// </summary>
    private sealed class FakeInteractable : IInteractable
    {
        private readonly Vector2 _position;

        public FakeInteractable(Vector2 position, float radius, bool canInteract, string prompt)
        {
            _position = position;
            Radius = radius;
            CanInteract = canInteract;
            Prompt = prompt;
        }

        public Vector2 Position
        {
            get
            {
                OnReadPosition?.Invoke();
                return _position;
            }
        }

        public float Radius { get; set; }

        public bool CanInteract { get; set; }

        public string Prompt { get; set; }

        public int InteractCount { get; private set; }

        public Action? OnInteract { get; set; }

        public Action? OnReadPosition { get; set; }

        public void Interact()
        {
            InteractCount++;
            OnInteract?.Invoke();
        }
    }
}
