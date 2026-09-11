using System;
using System.IO;
using System.Numerics;
using XingGame.Systems.Player;

namespace XingGame.Tests;

public class PlayerMotorTests
{
    private const float Speed = 100f;

    /// <summary>
    /// 「身上没有任何移速增益」的那一档（×1.0）：本文件量的是走位手感（归一化、朝向、配置速度），
    /// 每一项都不该掺进轻身术那一份。增益那一侧的账在 <c>BuffSystemTests</c> 里量。
    /// </summary>
    private const float NoBonus = 1f;

    /// <summary>浮点比较容差。比斜向速度 bug（√2 倍，误差约 41）小五个数量级，足够宽松也足够灵敏。</summary>
    private const float Tolerance = 1e-3f;

    private static readonly float DiagonalComponent = Speed / MathF.Sqrt(2f);

    private static PlayerMotor NewMotor(float speed = Speed) => new(speed);

    private static void AssertClose(float expected, float actual, string what) =>
        Assert.True(MathF.Abs(expected - actual) <= Tolerance, $"{what}：期望 {expected}，实际 {actual}");

    private static void AssertFacingIs(PlayerMotor motor, float x, float y)
    {
        AssertClose(x, motor.Facing.X, "朝向 X 分量");
        AssertClose(y, motor.Facing.Y, "朝向 Y 分量");
    }

    [Fact]
    public void Step_直线输入_速度等于配置速度()
    {
        PlayerMotor motor = NewMotor();

        motor.Step(new Vector2(1f, 0f), NoBonus);

        AssertClose(Speed, motor.Velocity.X, "速度 X 分量");
        AssertClose(0f, motor.Velocity.Y, "速度 Y 分量");
    }

    [Fact]
    public void Step_斜向输入_速度大小仍等于配置速度()
    {
        PlayerMotor motor = NewMotor();

        motor.Step(new Vector2(1f, 1f), NoBonus);

        // 不归一化的话这里是 √2 × 100 ≈ 141.4，玩家会本能地贴着斜线走
        AssertClose(Speed, motor.Velocity.Length(), "速度大小");
        AssertClose(DiagonalComponent, motor.Velocity.X, "斜向速度 X 分量");
        AssertClose(DiagonalComponent, motor.Velocity.Y, "斜向速度 Y 分量");
    }

    [Fact]
    public void Step_四十五度以外的斜向_速度大小同样等于配置速度()
    {
        PlayerMotor motor = NewMotor();

        motor.Step(new Vector2(2f, 1f), NoBonus);

        AssertClose(Speed, motor.Velocity.Length(), "速度大小");
    }

    [Fact]
    public void Step_分量不足一的输入_同样按方向拉满速度()
    {
        PlayerMotor motor = NewMotor();

        motor.Step(new Vector2(0.5f, 0.5f), NoBonus);

        // 规格是「无条件归一化」（§接口契约 M1-3），不是「按摇杆幅度缩放」——
        // 摇杆死区与幅度映射是将来加手柄时才该定的事，现在不预先发明。
        AssertClose(Speed, motor.Velocity.Length(), "速度大小");
    }

    [Fact]
    public void Step_分量超过正负一_仍被正确归一化()
    {
        PlayerMotor motor = NewMotor();

        motor.Step(new Vector2(3f, 4f), NoBonus);   // 长度 5，方向仍是 3:4

        AssertClose(Speed, motor.Velocity.Length(), "速度大小");
        AssertClose(Speed * 0.6f, motor.Velocity.X, "速度 X 分量");
        AssertClose(Speed * 0.8f, motor.Velocity.Y, "速度 Y 分量");
    }

    [Fact]
    public void Step_反向输入_速度取负分量()
    {
        PlayerMotor motor = NewMotor();

        motor.Step(new Vector2(-1f, -1f), NoBonus);

        AssertClose(-DiagonalComponent, motor.Velocity.X, "速度 X 分量");
        AssertClose(-DiagonalComponent, motor.Velocity.Y, "速度 Y 分量");
        AssertClose(Speed, motor.Velocity.Length(), "速度大小");
    }

    [Fact]
    public void Step_零输入_速度为零向量()
    {
        PlayerMotor motor = NewMotor();
        motor.Step(new Vector2(1f, 0f), NoBonus);

        motor.Step(Vector2.Zero, NoBonus);

        Assert.Equal(Vector2.Zero, motor.Velocity);
    }

    [Fact]
    public void Step_零输入_朝向保持上一次的非零方向()
    {
        PlayerMotor motor = NewMotor();
        motor.Step(new Vector2(1f, 0f), NoBonus);

        motor.Step(Vector2.Zero, NoBonus);

        // 一松手朝向就跳回默认值的话，动画与交互朝向会闪
        Assert.Equal(Vector2.Zero, motor.Velocity);
        AssertFacingIs(motor, 1f, 0f);
    }

    [Fact]
    public void Step_零输入连喂多帧_朝向保持不变()
    {
        PlayerMotor motor = NewMotor();
        motor.Step(new Vector2(0f, -1f), NoBonus);

        for (int frame = 0; frame < 5; frame++) motor.Step(Vector2.Zero, NoBonus);

        AssertFacingIs(motor, 0f, -1f);
    }

    [Fact]
    public void Facing_未喂过输入_为零向量()
    {
        PlayerMotor motor = NewMotor();

        Assert.Equal(Vector2.Zero, motor.Facing);
    }

    [Fact]
    public void Step_斜向输入_朝向为单位向量()
    {
        PlayerMotor motor = NewMotor();

        motor.Step(new Vector2(1f, 1f), NoBonus);

        AssertClose(1f, motor.Facing.Length(), "朝向长度");
        AssertClose(DiagonalComponent / Speed, motor.Facing.X, "朝向 X 分量");
    }

    [Fact]
    public void Step_换一个配置速度_结果随之变()
    {
        PlayerMotor motor = NewMotor(250f);

        motor.Step(new Vector2(1f, 1f), NoBonus);

        AssertClose(250f, motor.Velocity.Length(), "速度大小");
        AssertClose(250f / MathF.Sqrt(2f), motor.Velocity.X, "速度 X 分量");
    }

    [Fact]
    public void Step_移速修正_速度按它放大()
    {
        // M3-6 起移动速度上挂得到限时增益（§8.2 的轻身术 +20% ⇒ 修正 1.2）。
        // 修正乘在**速度**上：100 × 1.2 = 120
        PlayerMotor motor = NewMotor();

        motor.Step(new Vector2(1f, 0f), 1.2f);

        AssertClose(120f, motor.Velocity.X, "速度 X 分量");
        AssertClose(0f, motor.Velocity.Y, "速度 Y 分量");
    }

    [Fact]
    public void Step_移速修正_不改朝向()
    {
        // 朝向是「玩家朝哪边」，增益不该改它：乘进朝向会让交互目标格与动画在增益到期那一刻跳一下
        PlayerMotor motor = NewMotor();

        motor.Step(new Vector2(1f, 1f), 1.2f);

        AssertClose(1f, motor.Facing.Length(), "朝向长度");
        AssertClose(DiagonalComponent / Speed, motor.Facing.X, "朝向 X 分量");
    }

    [Fact]
    public void Step_逐帧换修正_每帧各自生效()
    {
        // 增益会到期，所以修正是**每帧**喂进来的：同一台马达两帧可以走不同的速度
        PlayerMotor motor = NewMotor();

        motor.Step(new Vector2(1f, 0f), 1.2f);
        AssertClose(120f, motor.Velocity.X, "有增益那一帧");

        motor.Step(new Vector2(1f, 0f), NoBonus);
        AssertClose(Speed, motor.Velocity.X, "增益到期之后那一帧");
    }

    [Fact]
    public void Step_非正或非有限的修正_抛异常()
    {
        // 0 把玩家钉在原地、负数让他反着走、NaN/∞ 让位置发散——三种都不报错，
        // 只让操作变得莫名其妙（同构造里那条速度校验的理由）。零输入时也要拦：
        // 写错的修正值该在每一帧都被看见，而不是等玩家真的动了才显形
        PlayerMotor motor = NewMotor();

        Assert.Throws<ArgumentOutOfRangeException>(() => motor.Step(new Vector2(1f, 0f), 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => motor.Step(new Vector2(1f, 0f), -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => motor.Step(new Vector2(1f, 0f), float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => motor.Step(new Vector2(1f, 0f), float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => motor.Step(Vector2.Zero, 0f));
    }

    [Fact]
    public void 构造_非正或非有限速度_抛异常()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerMotor(0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerMotor(-1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerMotor(float.NaN));
    }

    [Fact]
    public void 配置_缺省文件可加载_速度为正数()
    {
        // 这条守住的是「配置真的接上了」：路径、键名、解析、取值任一环写错都会在这里炸
        PlayerConfig config = PlayerConfig.LoadDefault();

        Assert.True(config.MoveSpeed > 0f, $"缺省 moveSpeed 应为正数，实际为 {config.MoveSpeed}");
    }

    [Fact]
    public void 配置_缺键取默认值()
    {
        PlayerConfig config = PlayerConfig.FromJson("{}");

        Assert.Equal(PlayerConfig.DefaultMoveSpeed, config.MoveSpeed);
    }

    [Fact]
    public void 配置_键名大小写不敏感()
    {
        PlayerConfig config = PlayerConfig.FromJson("{ \"MoveSpeed\": 42 }");

        Assert.Equal(42f, config.MoveSpeed);
    }

    [Fact]
    public void 配置_非正速度_抛异常()
    {
        Assert.Throws<InvalidDataException>(() => PlayerConfig.FromJson("{ \"moveSpeed\": 0 }"));
        Assert.Throws<InvalidDataException>(() => PlayerConfig.FromJson("{ \"moveSpeed\": -5 }"));
    }
}
