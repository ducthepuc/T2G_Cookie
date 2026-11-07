using System.Numerics;
using Raylib_CsLo;

namespace T2G_Cookie.Gui;

enum SnapKind
{
    Below,
    Inside,
    Above
}

sealed class SnapTarget
{
    public SnapKind Kind { get; }
    public Block Target { get; }
    public Vector2 Position { get; }
    public float Width { get; }

    public SnapTarget(SnapKind kind, Block target, Vector2 position, float width)
    {
        Kind = kind;
        Target = target;
        Position = position;
        Width = width;
    }

    public void Draw(int offsetX, int offsetY)
    {
        int x = (int)Position.X + offsetX;
        int y = (int)Position.Y + offsetY;
        int w = Math.Max(8, (int)Width);
        Raylib.DrawRectangle(x, y - 3, w, 6, Theme.SnapEdge);
        Raylib.DrawRectangle(x + 1, y - 2, w - 2, 4, Raylib.WHITE);
    }
}

sealed class DragSession
{
    public Block Root { get; }
    public Vector2 GrabOffset { get; }

    public DragSession(Block root, Vector2 grabOffset)
    {
        Root = root;
        GrabOffset = grabOffset;
    }

    public void Follow(Vector2 mouse, int screenX, int screenY) =>
        Root.Layout((int)(mouse.X - screenX - GrabOffset.X), (int)(mouse.Y - screenY - GrabOffset.Y));
}

public sealed class Workspace : Frame
{
    readonly List<Block> _roots = new();
    DragSession? _drag;
    SnapTarget? _snap;
    Block? _hovered;
    int _screenX;
    int _screenY;

    public Workspace(Func<int> x, Func<int> y, int wPercent, int hPercent) : base(x, y, wPercent, hPercent)
    {
        DrawBackground = true;
        Color = Theme.WorkspaceBg;
    }

    internal void AddRootBlock(Block block, Vector2 screenMouse)
    {
        int localX = (int)(screenMouse.X - _screenX);
        int localY = (int)(screenMouse.Y - _screenY);
        block.Layout(localX, localY);

        // A block coming out of the toolbox snaps like any other drag instead of
        // always landing as a free floating stack on top of everything else.
        Drop(block, FindSnap(block));
    }

    internal void RemoveBlock(Block block)
    {
        block.Detach();
        _roots.Remove(block);
        block.UnregisterTree();
    }

    public override void Update(int offsetX, int offsetY)
    {
        base.Update(offsetX, offsetY);

        int containerW = Parent is Frame p ? p.Width : Raylib.GetScreenWidth();
        int containerH = Parent is Frame p2 ? p2.Height : Raylib.GetScreenHeight();
        Width = containerW * WPercent / 100;
        Height = containerH * HPercent / 100;

        _screenX = HAnchor == Anchor.End ? containerW - Width : X + offsetX;
        _screenY = VAnchor == Anchor.End ? containerH - Height : Y + offsetY;

        // Never reach past the window: Width/Height are the visible extent and both the
        // drop clamping and the scissor rely on that being true.
        Width = Math.Max(0, Math.Min(Width, containerW - Math.Max(0, _screenX)));
        Height = Math.Max(0, Math.Min(Height, containerH - Math.Max(0, _screenY)));

        Vector2 mouse = Raylib.GetMousePosition();
        Vector2 localMouse = mouse - new Vector2(_screenX, _screenY);
        bool released = Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_LEFT);

        // Over the toolbox a drag means "delete this stack", never "snap it".
        if (_drag != null && mouse.X < Theme.ToolboxWidth)
        {
            _drag.Follow(mouse, _screenX, _screenY);
            _snap = null;
            if (released)
            {
                RemoveBlock(_drag.Root);
                _drag = null;
            }
            ResetHover();
            return;
        }

        if (_drag != null)
        {
            _drag.Follow(mouse, _screenX, _screenY);
            _snap = FindSnap(_drag.Root); // recomputed every frame so the drop is visible
            if (released)
            {
                Drop(_drag.Root, _snap);
                _drag = null;
                _snap = null;
            }
            ResetHover();
            return;
        }

        Block? hit = null;
        LayoutAll();
        // Roots are drawn in list order, so the last one is on top: hit-test top down
        // or a click picks the block that is hidden underneath.
        for (int i = _roots.Count - 1; i >= 0 && hit == null; i--)
            hit = _roots[i].FindAt(localMouse);

        if (hit != _hovered)
        {
            _hovered?.OnUnhover();
            _hovered = hit;
            _hovered?.OnHover();
        }

        if (Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT))
        {
            Block? pressed = HandlePress(localMouse, out Vector2 grabOffset);
            if (pressed != null)
            {
                ResetHover();
                _drag = new DragSession(pressed, grabOffset);

                // Press and release inside a single frame: drop right away, otherwise the
                // drag waits for a release event that already happened and follows the
                // mouse forever.
                if (Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_LEFT))
                {
                    _drag.Follow(mouse, _screenX, _screenY);
                    Drop(_drag.Root, FindSnap(_drag.Root));
                    _drag = null;
                }
            }
        }
    }

    public override void Draw(int offsetX, int offsetY)
    {
        int screenX = _screenX;
        int screenY = _screenY;

        if (DrawBackground)
            Raylib.DrawRectangleRounded(new Rectangle(screenX, screenY, Width, Height), 0f, 1, Color);

        Raylib.BeginScissorMode(screenX, screenY, Width, Height);
        LayoutAll();
        foreach (Block root in _roots)
            root.DrawChain(screenX, screenY);
        if (_drag != null)
            _drag.Root.DrawChain(screenX, screenY);
        _snap?.Draw(screenX, screenY);
        if (_roots.Count == 0 && _drag == null)
            Raylib.DrawText("Drag blocks here", screenX + 40, screenY + 40, 24, Theme.Border);
        Raylib.EndScissorMode();
    }

    internal Block? HandlePress(Vector2 localMouse, out Vector2 grabOffset)
    {
        LayoutAll();

        // Same top-down order as Draw, see Update.
        for (int i = _roots.Count - 1; i >= 0; i--)
        {
            Block? hit = _roots[i].FindAt(localMouse);
            if (hit == null) continue;

            hit.Detach();
            // Detach already unlinked it; this is a no-op unless the hit was a root.
            // (Testing IsRoot instead left a dragged stack inside _roots, which then
            //  offered itself as a snap target and knotted the list.)
            _roots.Remove(hit);

            grabOffset = localMouse - new Vector2(hit.X, hit.Y);
            return hit;
        }

        grabOffset = Vector2.Zero;
        return null;
    }

    internal SnapTarget? FindSnap(Block drag)
    {
        LayoutAll();

        Block tail = drag.Tail;
        Vector2 dragTop = drag.PreviousConnection;
        Vector2 dragBottom = tail.NextConnection;

        // The dragged structure must never be offered back to itself: attaching a stack
        // to itself is exactly how the linked list used to end up with a cycle.
        HashSet<Block> dragBlocks = drag.CollectBlocks();

        SnapTarget? best = null;
        float bestDistance = Theme.SnapRadius;

        void Consider(SnapKind kind, Block target, Vector2 point, Vector2 from, float width)
        {
            float d = Vector2.Distance(from, point);
            if (d >= bestDistance) return;
            bestDistance = d;
            best = new SnapTarget(kind, target, point, Math.Max(width, drag.Width));
        }

        foreach (Block root in _roots)
        {
            if (dragBlocks.Contains(root)) continue;

            foreach (Block b in root.Flatten())
            {
                if (dragBlocks.Contains(b)) continue;

                // The dragged top has to be able to receive b's bottom, and the dragged
                // bottom has to be able to continue b (a cap ends the script).
                if (drag.HasPreviousConnection && b.HasNextConnection &&
                    (tail.HasNextConnection || b.Next == null))
                    Consider(SnapKind.Below, b, b.NextConnection, dragTop, b.Width);

                // b's top has to sit on the dragged bottom. Guarded per case instead of
                // the old blanket `break`, which also blocked the one case a hat needs:
                // landing on top of an existing script.
                if (drag.HasNextConnection && b.HasPreviousConnection)
                    Consider(SnapKind.Above, b, b.PreviousConnection, dragBottom, b.Width);

                // The dragged top drops into the mouth of a C-block.
                if (drag.HasPreviousConnection && b is CBlock c &&
                    (tail.HasNextConnection || c.Inner == null))
                    Consider(SnapKind.Inside, c, c.InnerConnection, dragTop, c.Width - Theme.Indent);
            }
        }

        return best;
    }

    internal void Drop(Block drag, SnapTarget? snap)
    {
        drag.Parent = this;

        if (snap != null)
        {
            switch (snap.Kind)
            {
                case SnapKind.Below:
                    if (snap.Target.AttachBelow(drag)) return;
                    break;

                case SnapKind.Inside:
                    if (((CBlock)snap.Target).AttachInside(drag)) return;
                    break;

                case SnapKind.Above:
                    // Cut the target out of whatever held it before re-homing it. The old
                    // code only unlinked it from _roots, so a block lifted out of a
                    // C-block mouth kept its Container and ended up living in two chains
                    // at once - the corruption that eventually dead-locked Layout().
                    if (!drag.CanJoin(snap.Target)) break;
                    snap.Target.Detach();
                    _roots.Remove(snap.Target);
                    drag.Append(snap.Target);
                    _roots.Add(drag);
                    return;
            }
        }

        // No snap (or an unsafe one): keep the stack free floating, and inside the view
        // so it can always be grabbed again.
        drag.X = ClampX(drag.X, drag.Width);
        drag.Y = ClampY(drag.Y, drag.Height);
        _roots.Add(drag);
    }

    /// <summary>Keeps a dropped block fully inside when it fits, otherwise pinned to the near edge.</summary>
    int ClampX(int value, int size)
    {
        int room = Math.Max(1, Width - 16);
        return Math.Clamp(value, 8, Math.Max(8, Width - Math.Min(size, room) - 8));
    }

    int ClampY(int value, int size)
    {
        int room = Math.Max(1, Height - 16);
        return Math.Clamp(value, 8, Math.Max(8, Height - Math.Min(size, room) - 8));
    }

    void ResetHover()
    {
        _hovered?.OnUnhover();
        _hovered = null;
    }

    void LayoutAll()
    {
        foreach (Block root in _roots)
            root.Layout(root.X, root.Y);
    }
}
