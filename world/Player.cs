using Godot;
using XingGame.Systems.Player;

namespace XingGame.World;

/// <summary>
/// 玩家节点（桥接层）。只做三件事：读输入 → 调 <see cref="PlayerMotor"/> → 把结果喂给引擎（ADR-007）。
/// 位置由引擎物理持有，纯 C# 区不掺和（ADR-011）——碰撞与滑动只有引擎知道结果。
/// </summary>
public partial class Player : CharacterBody2D
{
    /// <summary>占位外观边长（像素）。美术一律先用占位符（铁律 7）。</summary>
    private const float PlaceholderSize = 14f;

    private static readonly Color PlaceholderColor = new(0.42f, 0.72f, 0.94f);

    private PlayerMotor _motor = null!;

    public override void _Ready() => _motor = new PlayerMotor(PlayerConfig.LoadDefault().MoveSpeed);

    public override void _PhysicsProcess(double delta)
    {
        _motor.Step(ReadInput());

        // System.Numerics.Vector2 → Godot 的 Vector2 的转换只在边界处发生（ADR-010）
        System.Numerics.Vector2 velocity = _motor.Velocity;
        Velocity = new Vector2(velocity.X, velocity.Y);

        MoveAndSlide();
    }

    /// <summary>
    /// 取各轴的原始按下状态，而不是 <c>Input.GetVector</c>——后者会顺手把长度钳到 1，
    /// 归一化就有一半发生在引擎里了。ADR-011 说归一化归纯 C# 的 <see cref="PlayerMotor"/> 管，
    /// 桥上只负责把原始轴读出来。
    /// </summary>
    private static System.Numerics.Vector2 ReadInput()
    {
        float x = (Input.IsActionPressed("move_right") ? 1f : 0f) - (Input.IsActionPressed("move_left") ? 1f : 0f);
        float y = (Input.IsActionPressed("move_down") ? 1f : 0f) - (Input.IsActionPressed("move_up") ? 1f : 0f);

        return new System.Numerics.Vector2(x, y);
    }

    /// <summary>占位方块。外观不随朝向变化，故只画一次，不逐帧 QueueRedraw。</summary>
    public override void _Draw() => DrawRect(new Rect2(-PlaceholderSize / 2f, -PlaceholderSize / 2f, PlaceholderSize, PlaceholderSize), PlaceholderColor);
}
