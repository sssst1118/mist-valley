using System.Collections.Generic;
using System.Text;
using Godot;
using XingGame.Systems.Farming;

namespace XingGame.World;

/// <summary>
/// 耕地渲染（桥接层）：把 <see cref="Farmland"/> 的格子画成色块、把当前目标格画成高亮（ADR-007）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么每帧轮询</b>：<see cref="Farmland"/> 没有任何变更事件，而玩家随时会走动、转向——
/// 「视野里有哪些格、目标格是哪一格」每一帧都可能不同。判据与被观察的量是离散还是连续一致
/// （ARCHITECTURE「关于『每帧轮询』——这里可以，别处不行」）：时间是离散量才用订阅。
/// </para>
/// <para>
/// <b>但内容没变就不重画</b>：轮询的结果先攒成一份快照，并同步攒出一行签名，
/// 签名与上一帧逐字符相同就直接返回，不调 <c>QueueRedraw()</c>。签名必须覆盖画面上会变的一切——
/// 每格的坐标、土壤状态、有没有作物、已生长天数、成熟与否，以及目标格。
/// 只比「格数」或「有没有作物」是不够的：浇一次水、过一天都不改变格数。
/// </para>
/// </remarks>
public partial class FarmView : Node2D
{
    /// <summary>作物占位方块的边长：刚播下时的大小（像素）。美术一律先用占位符（铁律 7）。</summary>
    private const float CropBaseSize = 4f;

    /// <summary>
    /// 作物每长一天的边长增量（像素）。取小值，让最长寿的作物（20 天）到第 16 天才铺满格子——
    /// 按短命作物（防风草 4 天）校准的话，长命作物会提前十几天就长得「满了」，看着像成熟了却没熟。
    /// </summary>
    private const float CropSizePerDay = 0.5f;

    /// <summary>作物方块的边长上限（像素）。留一圈边，免得相邻两格的作物糊成一片。</summary>
    private const float CropMaxSize = FarmGrid.TileSize - 4f;

    /// <summary>视口尺寸取不到时的兜底（无头模式等）。给一块固定窗口，总比一格都画不出来强。</summary>
    private static readonly Vector2 FallbackViewportSize = new(1280f, 720f);

    private static readonly Vector2 TileSize = new(FarmGrid.TileSize, FarmGrid.TileSize);
    private static readonly Vector2 HalfTile = TileSize / 2f;

    private static readonly Color TilledColor = new(0.34f, 0.24f, 0.16f);
    private static readonly Color WateredColor = new(0.18f, 0.12f, 0.08f);
    private static readonly Color CropGrowingColor = new(0.44f, 0.70f, 0.36f);
    private static readonly Color CropRipeColor = new(0.93f, 0.78f, 0.28f);
    private static readonly Color TargetFillColor = new(1f, 1f, 1f, 0.16f);
    private static readonly Color TargetOutlineColor = new(1f, 0.96f, 0.72f, 0.85f);

    /// <summary>目标格要从玩家那里问（位置与朝向都在它身上），路径由场景注入。</summary>
    [Export] public NodePath PlayerPath { get; set; } = "../Player";

    /// <summary>本帧要画的格。`_Draw` 只认它，不再回查 <see cref="Farmland"/>。</summary>
    private readonly List<PlotVisual> _plots = new();

    /// <summary>复用同一块缓冲：每帧都要重建签名，不能每帧再 new 一个（同 <c>ui/InventoryHud</c>）。</summary>
    private readonly StringBuilder _signatureBuilder = new();

    private Farmland _farmland = null!;
    private Player? _player;

    private TileCoord _targetTile;
    private bool _hasTarget;

    /// <summary>上一次真正画出去的内容的签名。空串表示还没画过。</summary>
    private string _rendered = string.Empty;

    public override void _Ready()
    {
        _farmland = GameRoot.Services.Get<Farmland>();

        // 场景被单独打开时树里没有玩家：照画耕地，只是没有高亮
        _player = GetNodeOrNull<Player>(PlayerPath);

        // 这里**不能**先刷新一次：同级节点按树序 ready，本节点排在 Player 之前，
        // 此刻问它朝向只会拿到还没构造好的 PlayerMotor（NullReferenceException，已实测踩过）。
        // 首帧的 _Process 一定在所有 _Ready 之后，让它去攒第一份快照。
    }

    public override void _Process(double delta)
    {
        Refresh();

        string signature = _signatureBuilder.ToString();
        if (string.Equals(signature, _rendered)) return;

        _rendered = signature;
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (PlotVisual plot in _plots)
        {
            Rect2 rect = TileRect(plot.Tile);

            // 浇过水的土更深：这是玩家判断「今天还用不用再浇」的唯一视觉依据
            DrawRect(rect, plot.State == SoilState.Watered ? WateredColor : TilledColor);

            float cropSize = CropSizeOf(plot);
            if (cropSize > 0f)
                DrawRect(CenteredRect(rect.GetCenter(), cropSize), plot.Ripe ? CropRipeColor : CropGrowingColor);
        }

        // 没有高亮的话，按了没反应时分不清是「没瞄准」还是「规则不允许」
        if (!_hasTarget) return;

        Rect2 target = TileRect(_targetTile);
        DrawRect(target, TargetFillColor);
        DrawRect(target, TargetOutlineColor, filled: false, width: 1f);
    }

    /// <summary>重建快照与签名。两者必须来自同一趟遍历，否则会出现「签名没变而画面该变」的漏画。</summary>
    private void Refresh()
    {
        _plots.Clear();
        _signatureBuilder.Clear();

        _hasTarget = _player is not null;
        if (_player is not null) _targetTile = _player.TargetTile;

        AppendVisiblePlots();

        if (!_hasTarget) return;
        _signatureBuilder.Append('@').Append(_targetTile.X).Append(',').Append(_targetTile.Y);
    }

    /// <summary>
    /// 只收视野范围内的格。耕地是稀疏的，但整张地图不是——每帧遍历全图纯属浪费，
    /// 而玩家看不见的格就算画了也没人知道。
    /// </summary>
    private void AppendVisiblePlots()
    {
        Rect2 visible = VisibleWorldRect();
        TileCoord min = FarmGrid.ToTile(ToPure(visible.Position));
        TileCoord max = FarmGrid.ToTile(ToPure(visible.End));

        // 多取一圈：贴着视野边缘、只露出半格的也要画，否则走一步才补上，画面边缘会闪
        for (int y = min.Y - 1; y <= max.Y + 1; y++)
        {
            for (int x = min.X - 1; x <= max.X + 1; x++)
            {
                var tile = new TileCoord(x, y);

                // 未开垦的格不画：地上本来就有底色，整片荒地画满色块只会盖住地面
                SoilState state = _farmland.StateOf(tile);
                if (state == SoilState.Untilled) continue;

                bool hasCrop = _farmland.HasCrop(tile);
                int daysGrown = hasCrop ? _farmland.DaysGrown(tile) : 0;
                bool ripe = hasCrop && _farmland.IsReadyToHarvest(tile);

                _plots.Add(new PlotVisual(tile, state, hasCrop, daysGrown, ripe));

                _signatureBuilder.Append(x).Append(',').Append(y).Append(':')
                    .Append((int)state).Append(',').Append(daysGrown).Append(ripe ? 'R' : '-').Append(';');
            }
        }
    }

    /// <summary>视野的世界矩形。相机在别处（<c>world/Player.tscn</c> 里），从视口问它。</summary>
    private Rect2 VisibleWorldRect()
    {
        Vector2 size = GetViewportRect().Size;
        if (size.X <= 0f || size.Y <= 0f) size = FallbackViewportSize;

        Vector2 center = GetViewport().GetCamera2D()?.GetScreenCenterPosition() ?? Vector2.Zero;

        return new Rect2(center - size / 2f, size);
    }

    /// <summary>
    /// 作物方块的边长。
    /// </summary>
    /// <remarks>
    /// <b>这里画不了「已生长 / 总天数」那个比例</b>：<see cref="Farmland"/> 只暴露
    /// <c>StateOf</c>/<c>HasCrop</c>/<c>DaysGrown</c>/<c>IsReadyToHarvest</c> 四个读数，
    /// 没有「这一格种的是什么」——桥接层拿不到某一格的 <see cref="CropDefinition.GrowthDays"/>。
    /// 所以先用「每长一天加宽固定像素、成熟再封顶换色」的占位表现：逐日的变化看得见，
    /// 成熟与否由颜色给准确信号。契约补上「按格读作物」的入口后改回真实比例（已作为阻塞项回报）。
    /// </remarks>
    private static float CropSizeOf(in PlotVisual plot)
    {
        if (!plot.HasCrop) return 0f;
        if (plot.Ripe) return CropMaxSize;

        return Mathf.Min(CropBaseSize + plot.DaysGrown * CropSizePerDay, CropMaxSize);
    }

    /// <summary>格的绘制矩形。中心点走 <see cref="FarmGrid.ToWorldCenter"/>——格与像素的换算只有系统里那一份。</summary>
    private static Rect2 TileRect(TileCoord tile)
    {
        Vector2 center = ToGodot(FarmGrid.ToWorldCenter(tile));
        return new Rect2(center - HalfTile, TileSize);
    }

    private static Rect2 CenteredRect(Vector2 center, float size)
    {
        var extent = new Vector2(size, size);
        return new Rect2(center - extent / 2f, extent);
    }

    /// <summary>纯 C# 的向量 → Godot 的向量，只在边界处转换（ADR-010）。</summary>
    private static Vector2 ToGodot(System.Numerics.Vector2 value) => new(value.X, value.Y);

    private static System.Numerics.Vector2 ToPure(Vector2 value) => new(value.X, value.Y);

    /// <summary>
    /// 一格的画法。`_Draw` 不再回查 <see cref="Farmland"/>：整帧的画面内容与签名必须同源，
    /// 否则两次查询之间耕地变了，就会出现「签名没变所以不重画」的漏画。
    /// </summary>
    private readonly record struct PlotVisual(TileCoord Tile, SoilState State, bool HasCrop, int DaysGrown, bool Ripe);
}
