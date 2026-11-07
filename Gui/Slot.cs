using Raylib_CsLo;

namespace T2G_Cookie.Gui;

/// <summary>What a field accepts: a variable-name dropdown, or a reporter block.</summary>
enum SlotKind
{
    Name,
    Value
}

/// <summary>One piece of a block's label: either literal text or a drop-in field.</summary>
abstract record Segment;
sealed record TextSegment(string Text) : Segment;
sealed record SlotSegment(Slot Slot) : Segment;

/// <summary>
/// A field inside a block ("the &lt;&gt; placeholder made real"). The host block owns it,
/// <see cref="Content"/> optionally holds a parked reporter, and <see cref="Bounds"/> is
/// refreshed on every Layout so hit-testing and snapping always see fresh geometry.
/// </summary>
sealed class Slot
{
    public Block? Host { get; internal set; }
    public SlotKind Kind { get; }
    public Rectangle Bounds { get; internal set; }
    public Block? Content { get; internal set; }

    string? _selectedName;

    /// <summary>The chosen variable name for a Name slot (dropdown), null when unset.</summary>
    public string? SelectedName
    {
        get => _selectedName;
        set
        {
            if (_selectedName == value) return;
            _selectedName = value;
            Host?.RecomputeWidth();
        }
    }

    /// <summary>
    /// The width this field needs: a parked reporter's width, or the selected name plus
    /// padding. Name slots have no content, so without this they would stay at the minimum
    /// width and hide the variable the block is actually using.
    /// </summary>
    public int DisplayWidth => Content?.Width ??
        (_selectedName is { Length: > 0 } name
            ? Math.Max(Theme.MinSlotWidth, Raylib.MeasureText(name, Theme.FontSize) + Theme.PadX)
            : Theme.MinSlotWidth);

    internal Slot(SlotKind kind) => Kind = kind;
}

/// <summary>Segment builders used by block factories and tests.</summary>
static class Seg
{
    public static Segment T(string text) => new TextSegment(text);
    public static Segment V() => new SlotSegment(new Slot(SlotKind.Value));
    public static Segment N() => new SlotSegment(new Slot(SlotKind.Name));
}
