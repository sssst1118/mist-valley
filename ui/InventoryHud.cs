using System.Text;
using Godot;
using XingGame.Systems.Items;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 背包 HUD（桥接层）：把背包里非空的槽位画成一段文本。除「id 换名字」外不含任何逻辑（ADR-007）。
/// </summary>
/// <remarks>
/// <b>这里每帧轮询是对的</b>（ARCHITECTURE「关于『每帧轮询』——这里可以，别处不行」）：
/// 背包是连续变化的量，而 M1-4 的背包没有变更事件可订阅——判据是被观察的量离散还是连续，
/// 与 <c>TimeHud</c> 订阅事件并不矛盾。
/// <para>
/// 文本先在 <see cref="StringBuilder"/> 里攒好、再与上一次逐字符比对，**变了才写 <c>Label.Text</c>**：
/// 赋值会让 Label 重排重绘，内容没变时每秒几十次是无谓的。
/// </para>
/// </remarks>
public partial class InventoryHud : Control
{
    /// <summary>复用同一块缓冲：每帧都要重建文本，不能每帧再 new 一个。</summary>
    private readonly StringBuilder _builder = new();

    private Label _label = null!;
    private IInventory _inventory = null!;
    private IItemTable _items = null!;

    private string _rendered = string.Empty;

    public override void _Ready()
    {
        _label = GetNode<Label>("InventoryLabel");

        // 背包与物品表都由组合根构造并注册（ADR-007）。名字要回物品表查——背包里只有 id，
        // 这也是本类与背包之间唯一的一处领域知识。
        _inventory = GameRoot.Services.Get<IInventory>();
        _items = GameRoot.Services.Get<IItemTable>();

        // 读档在 GameRoot._Ready 里已经跑完（autoload 先于主场景），先画一次，免得第一帧空白
        Render();
    }

    public override void _Process(double delta) => Render();

    private void Render()
    {
        BuildText();
        if (SameAsRendered()) return;

        _rendered = _builder.ToString();
        _label.Text = _rendered;
    }

    /// <summary>只列非空槽位——空槽是背包的常态，逐个列出来只会把有用的信息挤走。</summary>
    private void BuildText()
    {
        _builder.Clear();

        foreach (ItemStack slot in _inventory.Slots)
        {
            if (slot.Count == 0) continue;

            if (_builder.Length > 0) _builder.Append('\n');
            _builder.Append(_items.Get(slot.ItemId).Name).Append(" ×").Append(slot.Count);
        }

        if (_builder.Length == 0) _builder.Append("背包是空的");
    }

    /// <summary>先比长度再逐字符比：内容没变就不分配新字符串（这是本类每帧都跑的热路径）。</summary>
    private bool SameAsRendered()
    {
        if (_builder.Length != _rendered.Length) return false;

        for (int index = 0; index < _builder.Length; index++)
        {
            if (_builder[index] != _rendered[index]) return false;
        }

        return true;
    }
}
