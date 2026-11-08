using System.Numerics;
using Raylib_CsLo;

namespace T2G_Cookie.Gui;

/// <summary>Every construct a block can represent; the AST converter switches on it.</summary>
enum BlockKind
{
    None,
    Start,
    Step,
    Turn,
    Mine,
    Print,
    Set,
    If,
    While,
    Number,
    Boolean,
    Variable,
    Empty,
    Wall,
    Gold,
    IsThere,
    Not,
    Add,
    Sub,
    Mul,
    Eq,
    Neq,
    Lt,
    Leq,
    Gt,
    Geq,
    And,
    Or
}

/// <summary>
/// Base class for every block. A script is a linked list (Next/Previous);
/// a C-block additionally owns a nested list (Inner) whose first block points back via Container.
/// Only top-level blocks have a "real" X/Y; everything else is positioned by Layout().
/// </summary>
abstract class Block : Element, IInteractable
{
    public string Label { get; }
    public BlockCategory Category { get; }

    /// <summary>Label pieces: literal text and drop-in fields, in drawing order.</summary>
    public Segment[] Segments { get; }

    /// <summary>This block's fields, in segment order (Slots[i] corresponds to the i-th SlotSegment).</summary>
    public IReadOnlyList<Slot> Slots => _slots;
    readonly List<Slot> _slots = new();

    /// <summary>The field this block is parked in, when it is a reporter sitting in a host.</summary>
    public Slot? Slot { get; internal set; }

    /// <summary>What this block means to the AST converter; None = not a language construct.</summary>
    public BlockKind Kind { get; set; } = BlockKind.None;

    public Block? Next;
    public Block? Previous;
    public CBlock? Container;

    public override int Width { get; set; }
    public override int Height { get; set; }
    public override int WPercent { get; set; }
    public override int HPercent { get; set; }

    public bool IsHovered { get; set; }
    public bool IsActivated { get; set; }

    /// <summary>
    /// The generated source lines this block owns: the statement's own line and, for
    /// an if/else, the `else` keyword's line. AstConverter rewrites them every
    /// validation pass; while the VM runs, the block whose line the current
    /// instruction reports draws the gold highlight.
    /// </summary>
    public int RunLine { get; set; } = -1;
    public int RunLineAlt { get; set; } = -1;

    public Rectangle Bounds => new Rectangle(X, Y, Width, Height);

    /// <summary>
    /// The label row: where this block's text and fields live. A C-block is far taller
    /// than its row (the mouths hang below it) and a hat has a rounded top above it, so
    /// fields have to be measured against the row - otherwise an `if &lt;&gt; then` field
    /// comes out as tall as the whole block and its hole swallows the mouth.
    /// </summary>
    protected virtual int RowTop => Y;
    protected virtual int RowHeight => Theme.HeaderHeight;

    protected Block(string label, BlockCategory category, Func<int> x, Func<int> y)
        : this(new Segment[] { new TextSegment(label) }, category, x, y) { }

    protected Block(Segment[] segments, BlockCategory category, Func<int> x, Func<int> y) : base(x, y)
    {
        Segments = segments;
        foreach (Segment s in segments)
        {
            if (s is SlotSegment ss)
            {
                ss.Slot.Host = this;
                _slots.Add(ss.Slot);
            }
        }
        Label = string.Concat(segments.OfType<TextSegment>().Select(t => t.Text));
        Category = category;
        WPercent = 0;
        HPercent = 0;
        RecomputeWidth();
        Height = Theme.HeaderHeight;
    }

    /// <summary>
    /// Re-measures this block and every host it is parked in, walking up through Slot
    /// back-pointers. The seen-set makes it safe even against an already corrupt graph.
    /// </summary>
    public void RecomputeWidth()
    {
        var seen = new HashSet<Block>();
        for (Block? b = this; b != null && seen.Add(b); b = b.Slot?.Host)
            b.Width = b.ComputeWidth();
    }

    protected virtual int ComputeWidth()
    {
        int w = Theme.PadX * 2;
        foreach (Segment s in Segments)
        {
            w += s switch
            {
                TextSegment t => Raylib.MeasureText(t.Text, Theme.FontSize),
                SlotSegment sl => sl.Slot.DisplayWidth,
                _ => 0
            };
        }
        return Math.Max(Theme.MinBlockWidth, w);
    }

    /// <summary>
    /// Parks a reporter in one of this block's value slots.
    /// Returns false for name slots, occupied slots, non-reporters, or any join that
    /// would knot the graph (slot contents are part of CollectBlocks, so CanJoin sees them).
    /// </summary>
    public bool AttachToSlot(Slot slot, Block reporter)
    {
        if (slot.Host != this) return false;
        if (slot.Kind == SlotKind.Name) return false;
        if (slot.Content != null) return false;
        if (reporter is not ReporterBlock) return false;
        if (reporter.Previous != null || reporter.Next != null ||
            reporter.Container != null || reporter.Slot != null) return false;
        if (!CanJoin(reporter)) return false;

        slot.Content = reporter;
        reporter.Slot = slot;
        RecomputeWidth();
        return true;
    }

    public virtual void OnHover() { IsHovered = true; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_POINTING_HAND); }
    public virtual void OnUnhover() { IsHovered = false; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_ARROW); }
    public virtual void OnActivated() { IsActivated = true; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_RESIZE_ALL); }
    public virtual void OnDrag(Vector2 delta) { }

    public abstract bool HasPreviousConnection { get; }
    public abstract bool HasNextConnection { get; }

    public virtual bool ContainsPoint(Vector2 p) => Inside(p, X, Y, Width, Height);

    public Vector2 PreviousConnection => new(X, Y);
    public Vector2 NextConnection => new(X, Y + Height);
    public bool IsRoot => Previous == null && Container == null;

    public Block Tail
    {
        get
        {
            Block b = this;
            while (b.Next != null) b = b.Next;
            return b;
        }
    }

    public int ChainHeight()
    {
        int h = 0;
        for (Block? b = this; b != null; b = b.Next) h += b.Height;
        return h;
    }

    /// <summary>Positions this block at (x, y) and stacks the rest of the chain below it.</summary>
    public void Layout(int x, int y)
    {
        for (Block? b = this; b != null; b = b.Next)
        {
            b.X = x;
            b.Y = y;
            b.LayoutChildren();
            y += b.Height;
        }
    }

    public void DrawChain(int offsetX, int offsetY)
    {
        for (Block? b = this; b != null; b = b.Next)
        {
            b.Draw(offsetX, offsetY);
            b.DrawRunHighlight(offsetX, offsetY);
        }
    }

    /// <summary>
    /// Gold outline around the label row - the "segment" that is this block's code
    /// line - while the VM is executing the instruction that belongs to it. Blocks
    /// outside the workspace (palette templates) never glow: they carry no workspace.
    /// </summary>
    void DrawRunHighlight(int offsetX, int offsetY)
    {
        if (RunLine < 0 && RunLineAlt < 0) return;
        if (Parent is not Workspace ws || ws.ActiveLine is not int line) return;
        if (line != RunLine && line != RunLineAlt) return;

        Raylib.DrawRectangleRoundedLines(
            new Rectangle(X + offsetX - 1, RowTop + offsetY - 1, Width + 2, RowHeight + 2),
            0.25f, 6, 2f, Theme.RunHighlight);
    }

    /// <summary>This block, everything after it, everything nested in C-block mouths,
    /// and every reporter parked in a field (so snaps can target their fields too).</summary>
    public IEnumerable<Block> Flatten()
    {
        for (Block? b = this; b != null; b = b.Next)
        {
            yield return b;

            foreach (Slot slot in b.Slots)
            {
                if (slot.Content is { } content)
                    foreach (Block nested in content.Flatten())
                        yield return nested;
            }

            if (b is CBlock c)
            {
                if (c.Inner is { } inner)
                    foreach (Block nested in inner.Flatten())
                        yield return nested;
                if (c.Inner2 is { } inner2)
                    foreach (Block nested in inner2.Flatten())
                        yield return nested;
            }
        }
    }

    /// <summary>
    /// Every block that currently belongs to the same structure as this one: down the
    /// chain, into C-block mouths, and back up through Previous/Container.
    /// Traversal is cycle safe so it can inspect an already broken list without hanging.
    /// </summary>
    public HashSet<Block> CollectBlocks()
    {
        var seen = new HashSet<Block>();
        var stack = new Stack<Block>();
        stack.Push(this);

        while (stack.Count > 0)
        {
            Block b = stack.Pop();
            if (!seen.Add(b)) continue;
            if (b.Next != null) stack.Push(b.Next);
            if (b.Previous != null) stack.Push(b.Previous);
            if (b.Container != null) stack.Push(b.Container);
            if (b is CBlock c)
            {
                if (c.Inner != null) stack.Push(c.Inner);
                if (c.Inner2 != null) stack.Push(c.Inner2);
            }
            // Parked reporters belong to the host's structure: without this step a
            // CanJoin check could not see them and a drop could knot the graph.
            foreach (Slot slot in b._slots)
                if (slot.Content != null) stack.Push(slot.Content);
        }

        return seen;
    }

    /// <summary>
    /// True when this block and <paramref name="other"/> belong to two separate structures.
    /// Joining two separate structures can never create a cycle; joining two blocks that
    /// are already part of the same one knots the list, and a knotted list makes Layout(),
    /// Tail and Flatten spin forever - that was the crash.
    /// </summary>
    public bool CanJoin(Block other)
    {
        if (ReferenceEquals(this, other)) return false;

        HashSet<Block> mine = CollectBlocks();
        foreach (Block b in other.CollectBlocks())
            if (mine.Contains(b)) return false;

        return true;
    }

    /// <summary>Removes this chain (and anything nested inside it) from the global element list.</summary>
    public void UnregisterTree()
    {
        var seen = new HashSet<Block>();
        var stack = new Stack<Block>();
        stack.Push(this);

        while (stack.Count > 0)
        {
            Block b = stack.Pop();
            if (!seen.Add(b)) continue;
            Element.Unregister(b);
            if (b.Next != null) stack.Push(b.Next);
            if (b is CBlock c)
            {
                if (c.Inner != null) stack.Push(c.Inner);
                if (c.Inner2 != null) stack.Push(c.Inner2);
            }
            foreach (Slot slot in b._slots)
                if (slot.Content != null) stack.Push(slot.Content);
        }
    }

    public Block? FindAt(Vector2 p)
    {
        for (Block? b = this; b != null; b = b.Next)
        {
            // Slot contents are drawn on top of the host body, so they are hit first
            // (last slot first, matching draw order). Recurse: a parked reporter can
            // hold fields of its own.
            for (int i = b._slots.Count - 1; i >= 0; i--)
            {
                if (b._slots[i].Content is { } inner && inner.FindAt(p) is { } hit)
                    return hit;
            }

            if (b.ContainsPoint(p)) return b;
            if (b is CBlock c)
            {
                if (c.Inner != null && c.Inner.FindAt(p) is { } h1) return h1;
                if (c.Inner2 != null && c.Inner2.FindAt(p) is { } h2) return h2;
            }
        }
        return null;
    }

    /// <summary>Cuts this block (and everything after it) out of whatever it was attached to.</summary>
    public void Detach()
    {
        if (Previous != null)
        {
            if (ReferenceEquals(Previous.Next, this)) Previous.Next = null;
            Previous = null;
        }

        if (Container != null)
        {
            // Only the first block of a mouth owns Container.Inner (or Inner2); a stale
            // pointer must not wipe content that actually belongs there.
            if (ReferenceEquals(Container.Inner, this)) Container.Inner = null;
            else if (ReferenceEquals(Container.Inner2, this)) Container.Inner2 = null;
            Container = null;
        }

        if (Slot != null)
        {
            // Pulled out of a field: empty the field (unless something else claimed it)
            // and let the host shrink back.
            Slot slot = Slot;
            if (ReferenceEquals(slot.Content, this)) slot.Content = null;
            Slot = null;
            slot.Host?.RecomputeWidth();
        }
    }

    /// <summary>
    /// Joins another chain onto the end of this one.
    /// Returns false when the two chains already share a block (that would be a cycle).
    /// </summary>
    public bool Append(Block other)
    {
        if (other.Previous != null || other.Container != null || !CanJoin(other)) return false;

        Block tail = Tail;
        tail.Next = other;
        other.Previous = tail;
        return true;
    }

    /// <summary>
    /// Inserts a chain directly after this block, pushing any existing followers below it.
    /// Returns false when the two chains already share a block (that would be a cycle).
    /// </summary>
    public bool AttachBelow(Block stack)
    {
        if (stack.Previous != null || stack.Container != null || !CanJoin(stack)) return false;

        Block? oldNext = Next;
        // Read the tail exactly once: linking it to oldNext below changes what a second
        // Tail walk would return, and oldNext.Previous has to point at the drag's tail.
        Block tail = stack.Tail;

        Next = stack;
        stack.Previous = this;
        if (oldNext != null)
        {
            tail.Next = oldNext;
            oldNext.Previous = tail;
        }
        return true;
    }

    /// <summary>Computes field geometry and parks field contents; C-blocks extend this for their mouths.</summary>
    protected virtual void LayoutChildren()
    {
        int x = X + Theme.PadX;
        foreach (Segment s in Segments)
        {
            if (s is TextSegment t)
            {
                x += Raylib.MeasureText(t.Text, Theme.FontSize);
            }
            else if (s is SlotSegment ss)
            {
                Slot slot = ss.Slot;
                int w = ss.Slot.DisplayWidth;
                slot.Bounds = new Rectangle(x, RowTop, w, RowHeight);
                slot.Content?.Layout(x, RowTop);
                x += w;
            }
        }
    }

    protected void DrawBody(int x, int y, int w, int h)
    {
        Raylib.DrawRectangle(x, y, w, h, BodyColor);
        Raylib.DrawRectangle(x, y + h - 2, w, 2, Category.DarkColor);
    }

    /// <summary>The fill used for every piece of this block; hovering keeps the category hue.</summary>
    protected Color BodyColor => IsHovered ? Theme.Lighten(Category.Color, 0.16f) : Category.Color;

    /// <summary>
    /// Draws the label segments (text + field holes) for one row and then the parked
    /// reporters, which sit on top of the holes they fill.
    /// </summary>
    protected void DrawSegments(int offsetX, int offsetY, int rowTop)
    {
        int x = X + offsetX + Theme.PadX;
        int ty = rowTop + (Theme.HeaderHeight - Theme.FontSize) / 2;

        foreach (Segment s in Segments)
        {
            if (s is TextSegment t)
            {
                Raylib.DrawText(t.Text, x, ty, Theme.FontSize, Raylib.WHITE);
                x += Raylib.MeasureText(t.Text, Theme.FontSize);
            }
            else if (s is SlotSegment ss)
            {
                DrawSlotHole(ss.Slot, offsetX, offsetY);
                x += ss.Slot.DisplayWidth;
            }
        }

        foreach (Slot slot in _slots)
            slot.Content?.Draw(offsetX, offsetY);
    }

    /// <summary>
    /// Draws one field. Empty fields carry a faint label so a fresh `set` or `print`
    /// block explains itself; name fields show the chosen variable in the hole.
    /// </summary>
    protected void DrawSlotHole(Slot slot, int offsetX, int offsetY)
    {
        int w = slot.DisplayWidth;
        var outer = new Rectangle(slot.Bounds.X + offsetX, slot.Bounds.Y + offsetY, w, slot.Bounds.height);
        Raylib.DrawRectangleRounded(outer, 0.8f, 8, Category.DarkColor);
        Raylib.DrawRectangleRounded(
            new Rectangle(outer.X + 1, outer.Y + 1, outer.width - 2, outer.height - 2), 0.8f, 8, Theme.SlotFill);

        if (slot.Content != null) return;

        string text;
        Color color;
        int fontSize;
        if (slot.Kind == SlotKind.Name)
        {
            text = slot.SelectedName ?? "name";
            color = slot.SelectedName != null ? Category.DarkColor : Theme.PlaceholderText;
            fontSize = slot.SelectedName != null ? Theme.FontSize : Theme.PlaceholderFontSize;
        }
        else
        {
            text = "value";
            color = Theme.PlaceholderText;
            fontSize = Theme.PlaceholderFontSize;
        }

        int textW = Raylib.MeasureText(text, fontSize);
        int textX = (int)outer.X + (w - textW) / 2;
        int textY = (int)outer.Y + ((int)outer.height - fontSize) / 2;
        Raylib.DrawText(text, textX, textY, fontSize, color);
    }

    protected static bool Inside(Vector2 p, int x, int y, int w, int h)
        => p.X >= x && p.X < x + w && p.Y >= y && p.Y < y + h;

    public override void Update(int offsetX, int offsetY) { }
}

/// <summary>Event block: starts a script. Nothing can go above it.</summary>
sealed class HatBlock : Block
{
    public HatBlock(string label, BlockCategory category, Func<int> x, Func<int> y) : base(label, category, x, y) { }
    public HatBlock(Segment[] segments, BlockCategory category, Func<int> x, Func<int> y) : base(segments, category, x, y) { }

    public override bool HasPreviousConnection => false;
    public override bool HasNextConnection => true;
    public override int Height { get => Theme.HatExtra + Theme.HeaderHeight; set => _ = value; }

    // The rounded top sits above the label row.
    protected override int RowTop => Y + Theme.HatExtra;

    public override void Draw(int offsetX, int offsetY)
    {
        int sx = X + offsetX;
        int sy = Y + offsetY;
        Raylib.DrawRectangleRounded(new Rectangle(sx, sy, Width, Theme.HatExtra * 2), 0.8f, 10, BodyColor);
        DrawBody(sx, sy + Theme.HatExtra, Width, Theme.HeaderHeight);
        DrawSegments(offsetX, offsetY, sy + Theme.HatExtra);
    }
}

/// <summary>A normal command block.</summary>
class StackBlock : Block
{
    public StackBlock(string label, BlockCategory category, Func<int> x, Func<int> y) : base(label, category, x, y) { }
    public StackBlock(Segment[] segments, BlockCategory category, Func<int> x, Func<int> y) : base(segments, category, x, y) { }

    public override bool HasPreviousConnection => true;
    public override bool HasNextConnection => true;
    public override int Height { get => Theme.HeaderHeight; set => _ = value; }

    public override void Draw(int offsetX, int offsetY)
    {
        DrawBody(X + offsetX, Y + offsetY, Width, Height);
        DrawSegments(offsetX, offsetY, Y + offsetY);
    }
}

/// <summary>Ends a script. Nothing can go below it.</summary>
sealed class CapBlock : StackBlock
{
    public CapBlock(string label, BlockCategory category, Func<int> x, Func<int> y) : base(label, category, x, y) { }
    public CapBlock(Segment[] segments, BlockCategory category, Func<int> x, Func<int> y) : base(segments, category, x, y) { }

    public override bool HasNextConnection => false;
}

/// <summary>A value reporter block (operator, literal, variable, boolean).</summary>
class ReporterBlock : Block
{
    public ReporterBlock(string label, BlockCategory category, Func<int> x, Func<int> y) : base(label, category, x, y) { }
    public ReporterBlock(Segment[] segments, BlockCategory category, Func<int> x, Func<int> y) : base(segments, category, x, y) { }

    public override bool HasPreviousConnection => false;
    public override bool HasNextConnection => false;
    public override int Height { get => Theme.HeaderHeight; set => _ = value; }

    /// <summary>The rounded pill every reporter is drawn on.</summary>
    protected void DrawReporterPill(int offsetX, int offsetY)
    {
        float r = Height / 2f;
        int sx = X + offsetX;
        int sy = Y + offsetY;
        Raylib.DrawRectangleRounded(new Rectangle(sx, sy, Width, Height), 1f, 10, BodyColor);
        Raylib.DrawRectangle((int)(sx + r), sy, (int)(Width - r * 2), Height, BodyColor);
    }

    public override void Draw(int offsetX, int offsetY)
    {
        DrawReporterPill(offsetX, offsetY);
        DrawSegments(offsetX, offsetY, Y + offsetY);
    }
}

/// <summary>A block that wraps other blocks (if, if/else, while...).</summary>
sealed class CBlock : Block
{
    readonly bool _hasNext;

    public Block? Inner { get; set; }

    /// <summary>The second mouth of an if/else block; null when the block has no else.</summary>
    public Block? Inner2 { get; set; }

    public bool HasElse { get; }

    public CBlock(string label, BlockCategory category, Func<int> x, Func<int> y, bool hasNext = true, bool hasElse = false)
        : base(label, category, x, y)
    {
        _hasNext = hasNext;
        HasElse = hasElse;
    }

    public CBlock(Segment[] segments, BlockCategory category, Func<int> x, Func<int> y, bool hasNext = true, bool hasElse = false)
        : base(segments, category, x, y)
    {
        _hasNext = hasNext;
        HasElse = hasElse;
    }

    public override bool HasPreviousConnection => true;
    public override bool HasNextConnection => _hasNext;

    public int InnerHeight => Inner?.ChainHeight() ?? Theme.EmptySlotHeight;
    public int Inner2Height => Inner2?.ChainHeight() ?? (HasElse ? Theme.EmptySlotHeight : 0);
    public int ElseBarOffset => Theme.HeaderHeight + InnerHeight;
    public override int Height { get =>
        Theme.HeaderHeight + InnerHeight +
        (HasElse ? Theme.ElseBarHeight + Inner2Height : 0) +
        Theme.FooterHeight;
        set => _ = value;
    }
    public Vector2 InnerConnection => new(X + Theme.Indent, Y + Theme.HeaderHeight);
    public Vector2 Inner2Connection => new(X + Theme.Indent, Y + ElseBarOffset + Theme.ElseBarHeight);

    protected override void LayoutChildren()
    {
        base.LayoutChildren();
        Inner?.Layout(X + Theme.Indent, Y + Theme.HeaderHeight);
        Inner2?.Layout(X + Theme.Indent, Y + ElseBarOffset + Theme.ElseBarHeight);
    }

    /// <summary>Only the C-shaped frame counts as "this block"; the hollow insides don't.</summary>
    public override bool ContainsPoint(Vector2 p)
    {
        int ih = InnerHeight;
        if (Inside(p, X, Y, Width, Theme.HeaderHeight)) return true;
        if (Inside(p, X, Y + Theme.HeaderHeight, Theme.Indent, ih)) return true;

        int elseY = Y + Theme.HeaderHeight + ih;
        if (HasElse)
        {
            if (Inside(p, X, elseY, Width, Theme.ElseBarHeight)) return true;
            if (Inside(p, X, elseY + Theme.ElseBarHeight, Theme.Indent, Inner2Height)) return true;
            return Inside(p, X, elseY + Theme.ElseBarHeight + Inner2Height, Width, Theme.FooterHeight);
        }

        return Inside(p, X, elseY, Width, Theme.FooterHeight);
    }

    /// <summary>
    /// Puts a chain at the start of the if-mouth; the old blocks follow it.
    /// Returns false when the chain already lives inside this C-block (that would be a cycle).
    /// </summary>
    public bool AttachInside(Block stack) => AttachInside0(stack);

    /// <summary>Puts a chain at the start of the if-mouth (mouth 0).</summary>
    public bool AttachInside0(Block stack)
    {
        if (stack.Previous != null || stack.Container != null || !CanJoin(stack)) return false;

        Block? oldInner = Inner;
        Block tail = stack.Tail;

        Inner = stack;
        stack.Previous = null;
        stack.Container = this;
        if (oldInner != null)
        {
            tail.Next = oldInner;
            oldInner.Previous = tail;
            oldInner.Container = null;
        }
        return true;
    }

    /// <summary>Puts a chain at the start of the else-mouth (mouth 1); only if/else blocks have one.</summary>
    public bool AttachInside2(Block stack)
    {
        if (!HasElse) return false;
        if (stack.Previous != null || stack.Container != null || !CanJoin(stack)) return false;

        Block? oldInner = Inner2;
        Block tail = stack.Tail;

        Inner2 = stack;
        stack.Previous = null;
        stack.Container = this;
        if (oldInner != null)
        {
            tail.Next = oldInner;
            oldInner.Previous = tail;
            oldInner.Container = null;
        }
        return true;
    }

    public override void Draw(int offsetX, int offsetY)
    {
        int ih = InnerHeight;
        int sx = X + offsetX;
        int sy = Y + offsetY;

        DrawBody(sx, sy, Width, Theme.HeaderHeight);
        DrawSegments(offsetX, offsetY, sy);

        Raylib.DrawRectangle(sx, sy + Theme.HeaderHeight, Theme.Indent, ih, BodyColor);
        Raylib.DrawRectangle(sx + Theme.Indent - 2, sy + Theme.HeaderHeight, 2, ih, Category.DarkColor);

        int elseY = sy + Theme.HeaderHeight + ih;
        if (HasElse)
        {
            DrawBody(sx, elseY, Width, Theme.ElseBarHeight);
            Raylib.DrawText("else", sx + Theme.PadX,
                elseY + (Theme.ElseBarHeight - Theme.FontSize) / 2, Theme.FontSize, Raylib.WHITE);

            Raylib.DrawRectangle(sx, elseY + Theme.ElseBarHeight, Theme.Indent, Inner2Height, BodyColor);
            Raylib.DrawRectangle(sx + Theme.Indent - 2, elseY + Theme.ElseBarHeight, 2, Inner2Height, Category.DarkColor);
            elseY += Theme.ElseBarHeight + Inner2Height;
        }

        DrawBody(sx, elseY, Width, Theme.FooterHeight);

        Inner?.DrawChain(offsetX, offsetY);
        Inner2?.DrawChain(offsetX, offsetY);
    }
}
