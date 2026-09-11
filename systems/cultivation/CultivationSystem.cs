using System;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 玩家的灵根与境界/层数。数据出处：§4.2 六档灵根（<c>docs/public/design.md</c> 162-171 行）、
/// §8.1 九大境界与炼气 1-13 层（458-473 行）、§8.2 的三条游戏绑定（496-502 行）。
/// </summary>
/// <remarks>
/// <para>
/// <b>状态是本类自己持有的，不是从别处推出来的</b>：灵根与境界都是玩家做过的选择/经历的结果，
/// 没有第二个来源可以算出来，所以要进存档（<see cref="ISaveable"/>，键 <c>cultivation</c>）。
/// </para>
/// <para>
/// <b>构造与读档共用一份查表逻辑，只有层号越界抛的异常不同</b>：构造参数里写错层号是编程错误
/// （<see cref="ArgumentOutOfRangeException"/>），存档里的层号越界是数据错误
/// （<see cref="InvalidDataException"/>，ADR-009 要求坏档一律是它，将来 catch 坏档的代码要接得住）。
/// id 查不到在两处都是 <see cref="InvalidDataException"/>——灵根与境界都是**数据**（表或存档），
/// 对不上就是数据错误，不许「猜着读」（同 <c>FriendshipSystem</c> 先例：认不出的 id 不接给谁）。
/// </para>
/// </remarks>
public sealed class CultivationSystem : ICultivationSystem, ISaveable
{
    private readonly ISpiritRootTable _roots;
    private readonly IRealmTable _realms;

    private SpiritRootGrade _grade = null!;
    private SpiritRootDefinition? _root;
    private RealmDefinition _realm = null!;
    private int _stage;

    /// <param name="gradeId">§4.2 的品级 id，必填。</param>
    /// <param name="rootId">§4.3/§4.4 的具体灵根 id；四档普通品级传 null。</param>
    /// <param name="stage">从 1 起的小境界层号。</param>
    public CultivationSystem(
        ISpiritRootTable roots,
        IRealmTable realms,
        string gradeId,
        string? rootId,
        string realmId,
        int stage)
    {
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _realms = realms ?? throw new ArgumentNullException(nameof(realms));

        (SpiritRootGrade grade, SpiritRootDefinition? root, RealmDefinition realm) =
            Resolve(gradeId, rootId, realmId, "构造参数");

        if (stage < 1 || stage > realm.StageCount)
            throw new ArgumentOutOfRangeException(
                nameof(stage), stage, $"{realm.Name}的层号必须在 1..{realm.StageCount} 之间");

        Assign(grade, root, realm, stage);
    }

    public SpiritRootGrade Grade => _grade;

    public SpiritRootDefinition? Root => _root;

    public RealmDefinition Realm => _realm;

    public int Stage => _stage;

    public bool Reaches(string realmId, int stage)
    {
        RealmDefinition target = _realms.Get(realmId);

        // 复用境界自己那条越界判据，不在这里写第二份规则——两份边界判据迟早会漂
        target.StageName(stage);

        int targetOrder = _realms.OrderOf(realmId);
        int currentOrder = _realms.OrderOf(_realm.Id);

        // 跨大境界只看 §8.1 的表序：筑基初期高过炼气十三层。只比层号的话，一位筑基修士会
        // 因为「层号 1 < 4」而种不了灵植——那正是「至少炼气 4 层」这条门槛最不该有的行为
        return currentOrder != targetOrder ? currentOrder > targetOrder : _stage >= stage;
    }

    public bool Meets(CultivationGate gate)
    {
        CultivationGateRequirement requirement = _realms.RequirementOf(gate);

        return Reaches(requirement.RealmId, requirement.Stage);
    }

    public string SaveKey => "cultivation";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    public int Version => 1;

    public string Serialize() =>
        JsonSerializer.Serialize(new SavedCultivation(_grade.Id, _root?.Id, _realm.Id, _stage), _saveJsonOptions);

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009）
        if (fromVersion > Version)
            throw new NotSupportedException($"修仙存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedCultivation saved = JsonSerializer.Deserialize<SavedCultivation>(json, _saveJsonOptions)
            ?? throw new InvalidDataException("修仙存档内容为空");

        if (string.IsNullOrWhiteSpace(saved.GradeId))
            throw new InvalidDataException("修仙存档缺少 gradeId");

        if (string.IsNullOrWhiteSpace(saved.RealmId))
            throw new InvalidDataException("修仙存档缺少 realmId");

        // 「字段不在」与「字段是 0」必须分开（ADR-009 / AGENT-BRIEF）：层号的合法值从 1 起，
        // 缺字段会静默读成 0，而 0 一旦被当成合法值，玩家就会停在一个不存在的层次上
        if (saved.Stage is not int stage)
            throw new InvalidDataException("修仙存档缺少 stage 字段");

        (SpiritRootGrade grade, SpiritRootDefinition? root, RealmDefinition realm) =
            Resolve(saved.GradeId!, saved.RootId, saved.RealmId!, "修仙存档");

        if (stage < 1 || stage > realm.StageCount)
            throw new InvalidDataException(
                $"修仙存档里的层号 {stage} 超出{realm.Name}的 1..{realm.StageCount}");

        // 先整份校验再落盘：坏存档不该让境界停在「读了一半」的状态（照 Inventory / FriendshipSystem 先例）
        Assign(grade, root, realm, stage);
    }

    /// <summary>
    /// 按 id 查出灵根与境界，并把两处对不上的情况拦下。
    /// </summary>
    /// <remarks>
    /// 「具体灵根」的品级与记录里的品级必须一致：<c>root_sword</c>（先天异）配 <c>grade_mutation</c>
    /// 会让修炼速度与特效各说各话——而这正是「同一个事实存两处」的典型症状，宁可当场抛。
    /// </remarks>
    private (SpiritRootGrade Grade, SpiritRootDefinition? Root, RealmDefinition Realm) Resolve(
        string gradeId, string? rootId, string realmId, string owner)
    {
        if (!_roots.TryGetGrade(gradeId, out SpiritRootGrade grade))
            throw new InvalidDataException($"{owner}里的灵根品级「{gradeId}」不在灵根表里");

        SpiritRootDefinition? root = null;
        if (rootId is not null)
        {
            if (!_roots.TryGetRoot(rootId, out SpiritRootDefinition? found))
                throw new InvalidDataException($"{owner}里的具体灵根「{rootId}」不在灵根表里");

            if (!string.Equals(found.GradeId, gradeId, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"{owner}里的灵根自相矛盾：具体灵根「{rootId}」属于品级「{found.GradeId}」，记的却是「{gradeId}」");

            root = found;
        }

        if (!_realms.TryGet(realmId, out RealmDefinition realm))
            throw new InvalidDataException($"{owner}里的境界「{realmId}」不在境界表里");

        return (grade, root, realm);
    }

    /// <summary>四个字段一起换：分开赋值会让「读了一半」成为可能。</summary>
    private void Assign(SpiritRootGrade grade, SpiritRootDefinition? root, RealmDefinition realm, int stage)
    {
        _grade = grade;
        _root = root;
        _realm = realm;
        _stage = stage;
    }

    /// <summary>
    /// 存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。
    /// </summary>
    /// <remarks>
    /// <c>Stage</c> 可空是为了把「字段不在」与「写了 0」分开（ADR-009）。
    /// <c>RootId</c> 不设这个区分：null 与「字段不在」在这里本来就是一个意思——没有具体灵根。
    /// </remarks>
    private sealed record SavedCultivation(string? GradeId, string? RootId, string? RealmId, int? Stage);

    /// <summary>字段名写全，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private static readonly JsonSerializerOptions _saveJsonOptions = new() { WriteIndented = true };
}
