using System.Numerics;
using Raylib_CsLo;

namespace T2G_Cookie.Gui;

/// <summary>
/// Base class for every block. A script is a linked list (Next/Previous);
/// a C-block additionally owns a nested list (Inner) whose first block points back via Container.
/// Only top-level blocks have a "real" X/Y; everything else is positioned by Layout().
/// </summary>
abstract class Block : Element, IInteractable
{
    public string Label { get; }
    public BlockCategory Category { get; }

    public Block? Next;
    public Block? Previous;
    public CBlock? Container;

    public override int Width { get; set; }
    public override int Height { get; set; }
    public override int WPercent { get; set; }
    public override int HPercent { get; set; }

    public bool IsHovered { get; set; }
    public bool IsActivated { get; set; }

    public Rectangle Bounds => new Rectangle(X, Y, Width, Height);

    protected Block(string label, BlockCategory category, Func<int> x, Func<int> y) : base(x, y)
    {
        Label = label;
        Category = category;
        Width = Math.Max(Theme.MinBlockWidth, Raylib.MeasureText(label, Theme.FontSize) + Theme.PadX * 2);
        WPercent = 0;
        HPercent = 0;
        Height = Theme.HeaderHeight;
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
        for (Block? b = this; b != null; b = b.Next) b.Draw(offsetX, offsetY);
    }

    /// <summary>This block, everything after it, and everything nested inside C-blocks.</summary>
    public IEnumerable<Block> Flatten()
    {
        for (Block? b = this; b != null; b = b.Next)
        {
            yield return b;
            if (b is CBlock c && c.Inner is { } inner)
                foreach (Block nested in inner.Flatten())
                    yield return nested;
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
            if (b is CBlock c && c.Inner != null) stack.Push(c.Inner);
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
            if (b is CBlock c && c.Inner != null) stack.Push(c.Inner);
        }
    }

    public Block? FindAt(Vector2 p)
    {
        for (Block? b = this; b != null; b = b.Next)
        {
            if (b.ContainsPoint(p)) return b;
            if (b is CBlock c && c.Inner != null)
            {
                Block? hit = c.Inner.FindAt(p);
                if (hit != null) return hit;
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
            // Only the first block of a mouth owns Container.Inner; a stale pointer must
            // not wipe content that actually belongs there.
            if (ReferenceEquals(Container.Inner, this)) Container.Inner = null;
            Container = null;
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

    protected virtual void LayoutChildren() { }

    protected void DrawBody(int x, int y, int w, int h)
    {
        Color c = IsHovered ? Raylib.SKYBLUE : Category.Color;
        Raylib.DrawRectangle(x, y, w, h, c);
        Raylib.DrawRectangle(x, y + h - 2, w, 2, Category.DarkColor);
    }

    protected void DrawLabel(int x, int rowTop)
    {
        int ty = rowTop + (Theme.HeaderHeight - Theme.FontSize) / 2;
        Raylib.DrawText(Label, x + Theme.PadX, ty, Theme.FontSize, Raylib.WHITE);
    }

    protected static bool Inside(Vector2 p, int x, int y, int w, int h)
        => p.X >= x && p.X < x + w && p.Y >= y && p.Y < y + h;

    public override void Update(int offsetX, int offsetY) { }
}

/// <summary>Event block: starts a script. Nothing can go above it.</summary>
sealed class HatBlock : Block
{
    public HatBlock(string label, BlockCategory category, Func<int> x, Func<int> y) : base(label, category, x, y) { }

    public override bool HasPreviousConnection => false;
    public override bool HasNextConnection => true;
    public override int Height { get => Theme.HatExtra + Theme.HeaderHeight; set => _ = value; }

    public override void Draw(int offsetX, int offsetY)
    {
        int sx = X + offsetX;
        int sy = Y + offsetY;
        Raylib.DrawRectangleRounded(new Rectangle(sx, sy, Width, Theme.HatExtra * 2), 0.8f, 10, Category.Color);
        DrawBody(sx, sy + Theme.HatExtra, Width, Theme.HeaderHeight);
        DrawLabel(sx, sy + Theme.HatExtra);
    }
}

/// <summary>A normal command block.</summary>
class StackBlock : Block
{
    public StackBlock(string label, BlockCategory category, Func<int> x, Func<int> y) : base(label, category, x, y) { }

    public override bool HasPreviousConnection => true;
    public override bool HasNextConnection => true;
    public override int Height { get => Theme.HeaderHeight; set => _ = value; }

    public override void Draw(int offsetX, int offsetY)
    {
        DrawBody(X + offsetX, Y + offsetY, Width, Height);
        DrawLabel(X + offsetX, Y + offsetY);
    }
}

/// <summary>Ends a script. Nothing can go below it.</summary>
sealed class CapBlock : StackBlock
{
    public CapBlock(string label, BlockCategory category, Func<int> x, Func<int> y) : base(label, category, x, y) { }

    public override bool HasNextConnection => false;
}

/// <summary>A value reporter block (operator, literal, variable, boolean).</summary>
sealed class ReporterBlock : Block
{
    public ReporterBlock(string label, BlockCategory category, Func<int> x, Func<int> y) : base(label, category, x, y) { }

    public override bool HasPreviousConnection => false;
    public override bool HasNextConnection => false;
    public override int Height { get => Theme.HeaderHeight; set => _ = value; }

    public override void Draw(int offsetX, int offsetY)
    {
        float r = Height / 2f;
        int sx = X + offsetX;
        int sy = Y + offsetY;
        Raylib.DrawRectangleRounded(new Rectangle(sx, sy, Width, Height), 1f, 10, Category.Color);
        Raylib.DrawRectangle((int)(sx + r), sy, (int)(Width - r * 2), Height, Category.Color);
        DrawLabel(sx, sy);
    }
}

/// <summary>A block that wraps other blocks (repeat, forever, if...).</summary>
sealed class CBlock : Block
{
    readonly bool _hasNext;

    public Block? Inner { get; set; }

    public CBlock(string label, BlockCategory category, Func<int> x, Func<int> y, bool hasNext = true) : base(label, category, x, y)
    {
        _hasNext = hasNext;
    }

    public override bool HasPreviousConnection => true;
    public override bool HasNextConnection => _hasNext;

    public int InnerHeight => Inner?.ChainHeight() ?? Theme.EmptySlotHeight;
    public override int Height { get => Theme.HeaderHeight + InnerHeight + Theme.FooterHeight; set => _ = value; }
    public Vector2 InnerConnection => new(X + Theme.Indent, Y + Theme.HeaderHeight);

    protected override void LayoutChildren()
        => Inner?.Layout(X + Theme.Indent, Y + Theme.HeaderHeight);

    /// <summary>Only the C-shaped frame counts as "this block"; the hollow inside doesn't.</summary>
    public override bool ContainsPoint(Vector2 p)
    {
        int ih = InnerHeight;
        return Inside(p, X, Y, Width, Theme.HeaderHeight)
            || Inside(p, X, Y + Theme.HeaderHeight, Theme.Indent, ih)
            || Inside(p, X, Y + Theme.HeaderHeight + ih, Width, Theme.FooterHeight);
    }

    /// <summary>
    /// Puts a chain at the start of the inner slot; the old inner blocks follow it.
    /// Returns false when the chain already lives inside this C-block (that would be a cycle).
    /// </summary>
    public bool AttachInside(Block stack)
    {
        if (stack.Previous != null || stack.Container != null || !CanJoin(stack)) return false;

        Block? oldInner = Inner;
        // Read the tail exactly once, see AttachBelow.
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

    public override void Draw(int offsetX, int offsetY)
    {
        int ih = InnerHeight;
        int sx = X + offsetX;
        int sy = Y + offsetY;

        DrawBody(sx, sy, Width, Theme.HeaderHeight);
        DrawLabel(sx, sy);

        Raylib.DrawRectangle(sx, sy + Theme.HeaderHeight, Theme.Indent, ih, Category.Color);
        Raylib.DrawRectangle(sx + Theme.Indent - 2, sy + Theme.HeaderHeight, 2, ih, Category.DarkColor);

        DrawBody(sx, sy + Theme.HeaderHeight + ih, Width, Theme.FooterHeight);

        Inner?.DrawChain(offsetX, offsetY);
    }
}
