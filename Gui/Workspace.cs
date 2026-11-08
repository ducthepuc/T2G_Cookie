using System.Numerics;
using Raylib_CsLo;
using T2G_Cookie.Shared;

namespace T2G_Cookie.Gui;

enum SnapKind
{
    Below,
    Inside,
    Above,
    IntoSlot
}

sealed class SnapTarget
{
    public SnapKind Kind { get; }
    public Block Target { get; }
    public Vector2 Position { get; }
    public float Width { get; }

    /// <summary>Set for IntoSlot: the exact field the reporter would land in.</summary>
    public Slot? Slot { get; }

    /// <summary>Set for Inside: 0 = the if-mouth, 1 = the else-mouth.</summary>
    public int Mouth { get; }

    public SnapTarget(SnapKind kind, Block target, Vector2 position, float width, Slot? slot = null, int mouth = 0)
    {
        Kind = kind;
        Target = target;
        Position = position;
        Width = width;
        Slot = slot;
        Mouth = mouth;
    }

    public void Draw(int offsetX, int offsetY)
    {
        // A slot preview highlights the field itself: a ring in the hole it would fill.
        if (Kind == SnapKind.IntoSlot && Slot is { Content: null } s)
        {
            int x = (int)s.Bounds.X + offsetX;
            int y = (int)s.Bounds.Y + offsetY;
            int w = Math.Max(8, (int)s.Bounds.width);
            Raylib.DrawRectangle(x, y, w, (int)s.Bounds.height, Theme.SnapEdge);
            Raylib.DrawRectangle(x + 2, y + 2, w - 4, (int)s.Bounds.height - 4, Raylib.WHITE);
            return;
        }

        int ex = (int)Position.X + offsetX;
        int ey = (int)Position.Y + offsetY;
        int ew = Math.Max(8, (int)Width);
        Raylib.DrawRectangle(ex, ey - 3, ew, 6, Theme.SnapEdge);
        Raylib.DrawRectangle(ex + 1, ey - 2, ew - 2, 4, Raylib.WHITE);
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

    // Variable / popup / editing state.
    readonly VariableRegistry _registry = new();
    readonly DropDown _drop = new();
    readonly NamePrompt _prompt = new();
    NumberBlock? _editing;
    Block? _dropOwner;
    Slot? _dropSlot;

    // Click-vs-drag bookkeeping: a press that ends within DragThreshold of where it
    // started is a click (toggle / edit / dropdown), not a drag.
    Vector2 _pressMouse;
    Block? _clickTarget;
    Slot? _clickSlot;

    // Live validation for badges and the Start button.
    AstResult _validation = new();
    double _flashUntil;
    bool _overStart;

    internal VariableRegistry Registry => _registry;
    internal IReadOnlyList<Block> Roots => _roots;
    internal DropDown Menu => _drop;
    internal NamePrompt Prompt => _prompt;
    internal NumberBlock? Editing => _editing;
    internal AstResult Validation => _validation;

    /// <summary>Where the Save button writes: the host wires it to the input file
    /// when one was given; null means there is no file to save to.</summary>
    internal Action<string>? SaveScript { get; set; }

    readonly StartButton _start;
    readonly SaveButton _save;
    readonly ResetButton _reset;
    readonly OutputField _output;
    readonly MapField _map;
    readonly RobotToggle _robotToggle;
    readonly RobotButtons _robotButtons;
    bool _overSave;
    bool _overReset;
    bool _overToggle;
    bool _overButtons;

    // Manual robot control: checked, the buttons own the run - the code is veiled
    // and Start is disabled - and every press moves the robot through MapHandler,
    // so the matrix is already updated the moment the box gets unchecked.
    bool _robotControl;

    internal SaveButton SaveButton => _save;
    internal StartButton StartButton => _start;

    /// <summary>The yellow mined-gold reset button left of Save.</summary>
    internal ResetButton ResetButton => _reset;

    /// <summary>The 20% x 10% field under the bar buttons where Print output will show.</summary>
    internal OutputField Output => _output;

    /// <summary>The 15x15 map panel under the output field; the host loads the map matrix into it.</summary>
    internal MapField MapPanel => _map;

    /// <summary>The "Control Robot" checkbox frame under the map panel.</summary>
    internal RobotToggle RobotToggle => _robotToggle;

    /// <summary>The TURN LEFT / TURN RIGHT / FORWARD frame under the checkbox.</summary>
    internal RobotButtons RobotButtons => _robotButtons;

    /// <summary>True while manual robot control owns the run.</summary>
    internal bool RobotControl => _robotControl;

    /// <summary>Exactly one "Start clicked" hat may exist; the toolbox greys its item out.</summary>
    internal bool HasStartBlock => _roots.Any(r => r is HatBlock);

    public Workspace(Func<int> x, Func<int> y, int wPercent, int hPercent) : base(x, y, wPercent, hPercent)
    {
        DrawBackground = true;
        Color = Theme.WorkspaceBg;
        _start = new StartButton();
        AddChild(_start);

        // Save sits to the left of Start and follows it; the resolvers re-read Start's
        // position on every access, so resizing the window keeps the pair aligned.
        _save = new SaveButton(
            () => Pos.Left(_start)() - Theme.SaveButtonW - Theme.ButtonGap,
            Pos.Top(_start));
        AddChild(_save);

        // Reset hangs off Save's left edge the same way, in its own yellow so the
        // map action reads differently from the grey file action.
        _reset = new ResetButton(
            () => Pos.Left(_save)() - Theme.ResetButtonW - Theme.ButtonGap,
            Pos.Top(_save));
        AddChild(_reset);

        // The print-output panel hangs under the whole bar (buttons + status line) and
        // shares the bar's right edge: its x resolves from Pos.Right(_start) minus the
        // panel's screen width, so the two stay flush through resizes.
        _output = new OutputField(
            () => Pos.Right(_start)() - Dim.GetWSize(Theme.OutputWPercent),
            () => Pos.Bottom(_start)() + Theme.OutputTopGap,
            Theme.OutputWPercent, Theme.OutputHPercent);
        AddChild(_output);

        // The map panel hangs directly under the print-output field and shares its
        // width: its x resolves from the output's left edge, so the two stay flush
        // through resizes.
        _map = new MapField(
            () => Pos.Left(_output)(),
            () => Pos.Bottom(_output)() + Theme.MapTopGap,
            Theme.OutputWPercent);
        AddChild(_map);

        // The robot-control stack hangs under the map: checkbox first, the button
        // row under it, both sharing the map's width and left edge.
        _robotToggle = new RobotToggle(
            () => Pos.Left(_map)(),
            () => Pos.Bottom(_map)() + Theme.RobotPanelGap,
            Theme.OutputWPercent,
            state => _robotControl = state);
        AddChild(_robotToggle);

        _robotButtons = new RobotButtons(
            () => Pos.Left(_map)(),
            () => Pos.Bottom(_robotToggle)() + Theme.RobotPanelGap,
            Theme.OutputWPercent,
            () => _robotControl,
            () => _map.Map?.TurnRobotLeft(),
            () => _map.Map?.TurnRobotRight(),
            () => _map.Map?.StepRobot(),
            () => _map.Map?.MineRobot());
        AddChild(_robotButtons);
    }

    internal void AddRootBlock(Block block, Vector2 screenMouse) =>
        AddRootBlock(block, screenMouse, Vector2.Zero);

    /// <summary>
    /// Adopts a block released over the workspace. <paramref name="grabOffset"/> is
    /// where the pointer held the block during its drag: keeping it lands the block
    /// exactly where it was drawn, so FindSnap sees the geometry the user aimed at.
    /// Dropping the offset would jump the block's top-left onto the cursor - up to a
    /// whole block width away from the connection - and miss the snap radius.
    /// </summary>
    internal void AddRootBlock(Block block, Vector2 screenMouse, Vector2 grabOffset)
    {
        int localX = (int)(screenMouse.X - grabOffset.X - _screenX);
        int localY = (int)(screenMouse.Y - grabOffset.Y - _screenY);
        block.Layout(localX, localY);

        // A block coming out of the toolbox snaps like any other drag instead of
        // always landing as a free floating stack on top of everything else.
        Drop(block, FindSnap(block));
    }

    /// <summary>Where the workspace draws on screen; previews of blocks dragged out
    /// of the toolbox are drawn with this offset.</summary>
    internal (int X, int Y) ScreenOrigin => (_screenX, _screenY);

    /// <summary>
    /// The snap a block flying out of the toolbox would take if released right now.
    /// FindSnap speaks workspace-local coordinates while the flying block lives in
    /// screen space, so the block is laid out where it would land, asked, and then
    /// put back to screen coordinates for its own draw.
    /// </summary>
    internal SnapTarget? FindSnapForScreen(Block block, Vector2 screenMouse, Vector2 grabOffset)
    {
        block.Layout((int)(screenMouse.X - grabOffset.X - _screenX),
                     (int)(screenMouse.Y - grabOffset.Y - _screenY));
        SnapTarget? snap = FindSnap(block);
        block.Layout((int)(screenMouse.X - grabOffset.X),
                     (int)(screenMouse.Y - grabOffset.Y));
        return snap;
    }

    internal void RemoveBlock(Block block)
    {
        if (_drag != null && ReferenceEquals(_drag.Root, block)) _drag = null;
        block.Detach();
        _roots.Remove(block);
        block.UnregisterTree();
    }

    /// <summary>
    /// Adopts a pre-built chain at workspace-local coordinates: no snap, no clamp, so a
    /// script loaded from a file lands exactly where the loader put it. Every block is
    /// parented to the workspace - Element.UpdateAll/DrawAll only skip children, so a
    /// chain that never went through Drop would be drawn a second time at its raw
    /// coordinates.
    /// </summary>
    internal void LoadRoot(Block root, int localX, int localY)
    {
        foreach (Block block in root.Flatten())
            block.Parent = this;

        root.Layout(localX, localY);
        _roots.Add(root);
    }

    public override void Update(int offsetX, int offsetY)
    {
        base.Update(offsetX, offsetY);

        int containerW = Parent is Frame p ? p.Width : Raylib.GetScreenWidth();
        int containerH = Parent is Frame p2 ? p2.Height : Raylib.GetScreenHeight();
        // Size responds to the screen via Dim (percent of screen width/height).
        Width = Dim.GetWSize(WPercent);
        Height = Dim.GetHSize(HPercent);

        _screenX = HAnchor == Anchor.End ? containerW - Width : X + offsetX;
        _screenY = VAnchor == Anchor.End ? containerH - Height : Y + offsetY;

        // Never reach past the window: Width/Height are the visible extent and both the
        // drop clamping and the scissor rely on that being true.
        Width = Math.Max(0, Math.Min(Width, containerW - Math.Max(0, _screenX)));
        Height = Math.Max(0, Math.Min(Height, containerH - Math.Max(0, _screenY)));

        // Anchor the Start button to the workspace's top-right corner using the clamped
        // width, so it stays fully on screen when the window is maximised.
        _start.Width = Theme.StartButtonW;
        _start.Height = Theme.StartButtonH;
        _start.X = Math.Max(8, Width - Theme.StartButtonW - 8);
        _start.Y = 8;

        Vector2 mouse = Raylib.GetMousePosition();
        Vector2 localMouse = mouse - new Vector2(_screenX, _screenY);
        bool released = Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_LEFT);
        bool pressed = Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT);
        bool chromeBlocked = _drag == null && !_prompt.IsOpen && !_drop.IsOpen;
        // Manual control is decided by the checkbox alone, so Start's state gates its
        // own hover and click in the same frame the box changes.
        _start.Disabled = _robotControl;
        bool overStart = chromeBlocked && !_start.Disabled && InsideRect(localMouse, _start.Bounds);
        bool overSave = chromeBlocked && InsideRect(localMouse, _save.Bounds);
        bool overReset = chromeBlocked && InsideRect(localMouse, _reset.Bounds);
        bool overToggle = chromeBlocked && InsideRect(localMouse, _robotToggle.Bounds);
        bool overButtons = chromeBlocked && InsideRect(localMouse, _robotButtons.Bounds);

        // The bar buttons and the robot-control panels are not Blocks, so route their
        // hover/cursor by hand.
        if (overStart != _overStart)
        {
            if (overStart) _start.OnHover();
            else _start.OnUnhover();
            _overStart = overStart;
        }
        if (overSave != _overSave)
        {
            if (overSave) _save.OnHover();
            else _save.OnUnhover();
            _overSave = overSave;
        }
        if (overReset != _overReset)
        {
            if (overReset) _reset.OnHover();
            else _reset.OnUnhover();
            _overReset = overReset;
        }
        if (overToggle != _overToggle)
        {
            if (overToggle) _robotToggle.OnHover();
            else _robotToggle.OnUnhover();
            _overToggle = overToggle;
        }
        if (overButtons != _overButtons)
        {
            if (overButtons) _robotButtons.OnHover();
            else _robotButtons.OnUnhover();
            _overButtons = overButtons;
        }

        // Live validation: one walk per frame keeps badges, the dropdown and the Start
        // button honest no matter how the forest was mutated.
        _validation = AstConverter.Convert(this);
        _start.HasErrors = _validation.Errors.Count > 0;
        _start.HasScript = _validation.Scripts.Length > 0;

        // 1. The name prompt owns every input while it is open.
        if (_prompt.IsOpen)
        {
            while ((int)Raylib.GetCharPressed() is int ch and not 0)
                _prompt.InputChar((char)ch);
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_ENTER)) ConfirmPrompt();
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE)) _prompt.Close();
            if (pressed && !InsideRect(localMouse, _prompt.LocalBounds)) _prompt.Close();
            return;
        }

        // 2. So does the dropdown: clicks outside just close it.
        if (_drop.IsOpen)
        {
            _drop.Move(localMouse);
            if (pressed)
            {
                if (_drop.Click(localMouse)) ApplyDropPicked();
                else _drop.Close();
            }
            return;
        }

        // 3. Keyboard input while a number field is being edited. The field is the gate:
        //    it only accepts digits, so every pressed character can just be forwarded.
        if (_editing != null)
        {
            while ((int)Raylib.GetCharPressed() is int ch and not 0)
                _editing.InputChar((char)ch);
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_BACKSPACE)) _editing.Backspace();
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_ENTER) ||
                Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE)) CommitEditing();
        }

        // 4. An active drag owns the input. Below the threshold the block stays where
        //    the press landed (a click on release); past it the block follows.
        if (AdvanceDrag(localMouse, released)) return;

        // 5. The Start and Save buttons sit above the scripts and eat their own clicks.
        if (pressed && overStart)
        {
            CommitEditing();
            RunStart();
            return;
        }
        if (pressed && overSave)
        {
            CommitEditing();
            Save();
            return;
        }
        if (pressed && overReset)
        {
            CommitEditing();
            ResetMined();
            return;
        }

        // 6. So do the robot-control panels: the click was already kept off the
        //    blocks by chromeBlocked, here it either flips the checkbox or moves the
        //    robot (RobotButtons itself refuses while the box is unchecked).
        if (pressed && overToggle)
        {
            CommitEditing();
            _robotToggle.Click(); // the callback flips _robotControl
            return;
        }
        if (pressed && overButtons)
        {
            CommitEditing();
            _robotButtons.Click(localMouse);
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

        if (pressed)
        {
            Block? pressHit = HandlePress(localMouse, out _);
            if (pressHit != null)
            {
                ResetHover();

                // Press and release inside a single frame: the pointer has not
                // travelled at all, so AdvanceDrag treats it as the click it is
                // (otherwise the drag waits for a release event that already
                // happened and follows the mouse forever).
                if (released) AdvanceDrag(localMouse, released: true);
            }
        }
    }

    /// <summary>
    /// Drives an active drag for one frame. A press that has not travelled past
    /// <see cref="Theme.DragThreshold"/> is still a click: the block must not slide
    /// under the cursor, and on release it re-homes exactly where it was and runs its
    /// click action (caret / toggle / dropdown). Past the threshold the block follows
    /// the pointer and shows snap previews. Returns true while a drag owns the input.
    /// </summary>
    internal bool AdvanceDrag(Vector2 localMouse, bool released)
    {
        if (_drag == null) return false;

        bool moved = Vector2.Distance(localMouse, _pressMouse) >= Theme.DragThreshold;
        Vector2 worldMouse = localMouse + new Vector2(_screenX, _screenY);

        if (moved)
        {
            _drag.Follow(worldMouse, _screenX, _screenY);

            // Over the toolbox a drag means "delete this stack", never "snap it".
            if (worldMouse.X < _screenX)
            {
                _snap = null;
                if (released)
                {
                    RemoveBlock(_drag.Root);
                    _drag = null;
                }
                ResetHover();
                return true;
            }

            _snap = FindSnap(_drag.Root); // recomputed every frame so the drop is visible
        }
        else if (released)
        {
            _snap = FindSnap(_drag.Root); // never left: re-home it exactly where it was
        }

        if (released)
        {
            Drop(_drag.Root, _snap);
            if (!moved) HandleClick(_clickTarget, _clickSlot);
            _drag = null;
            _snap = null;
        }

        if (moved) ResetHover();
        return true;
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

        foreach (AstError e in _validation.Errors)
            DrawBadge(e.Block, screenX, screenY);

        if (_roots.Count == 0 && _drag == null)
            Raylib.DrawText("Drag blocks here", screenX + 40, screenY + 40, 24, Theme.Border);

        // Manual robot control freezes the code: every script behind a pale veil so
        // the control buttons are unmistakably in charge. Tooltips still work - they
        // draw outside the scissor at the end.
        if (_robotControl)
            foreach (Block root in _roots)
                DrawChainVeil(root, screenX, screenY);

        // The output field lives under the bar: drawn after the scripts so blocks
        // never cover it, before the buttons so the bar stays on top of everything.
        _output.Draw(screenX, screenY);
        // The map panel follows the output down the same stack, and the robot
        // controls hang under the map.
        _map.Draw(screenX, screenY);
        _robotToggle.Draw(screenX, screenY);
        _robotButtons.Draw(screenX, screenY);
        _reset.Draw(screenX, screenY);
        _save.Draw(screenX, screenY);
        _start.Draw(screenX, screenY);
        DrawStatus(screenX, screenY);
        Raylib.EndScissorMode();

        // Popups and the badge tooltip draw outside the scissor: they may never be
        // clipped by the workspace.
        _drop.Draw(screenX, screenY);
        _prompt.Draw(screenX, screenY);
        DrawTooltip(screenX, screenY);
    }

    internal Block? HandlePress(Vector2 localMouse, out Vector2 grabOffset)
    {
        LayoutAll();
        CommitEditing();

        // Same top-down order as Draw, see Update.
        for (int i = _roots.Count - 1; i >= 0; i--)
        {
            Block? hit = _roots[i].FindAt(localMouse);
            if (hit == null) continue;

            Slot? slot = SlotAt(hit, localMouse);

            // Pressing an empty value field never lifts the script: the field swallows
            // the press (there is nothing to drag there).
            if (slot is { Content: null, Kind: SlotKind.Value })
            {
                grabOffset = Vector2.Zero;
                return null;
            }

            // A name field is a dropdown, not a handle: open it on press and never drag
            // the host script out from under the user's cursor.
            if (slot is { Kind: SlotKind.Name })
            {
                HandleClick(hit, slot);
                grabOffset = Vector2.Zero;
                return null;
            }

            hit.Detach();
            // Detach already unlinked it; this is a no-op unless the hit was a root.
            // (Testing IsRoot instead left a dragged stack inside _roots, which then
            //  offered itself as a snap target and knotted the list.)
            _roots.Remove(hit);

            // Remember where the press started so the release can tell click from drag.
            _pressMouse = localMouse;
            _clickTarget = hit;
            _clickSlot = slot;

            grabOffset = localMouse - new Vector2(hit.X, hit.Y);
            // The press owns the session from here: Update only has to advance it
            // (AdvanceDrag), and Drop/RemoveBlock end it.
            _drag = new DragSession(hit, grabOffset);
            return hit;
        }

        grabOffset = Vector2.Zero;
        return null;
    }

    /// <summary>
    /// Routes a press that ended where it started (no drag): toggles booleans, starts
    /// number editing, and opens variable dropdowns. Statement blocks do nothing.
    /// </summary>
    internal void HandleClick(Block? target, Slot? slot)
    {
        CommitEditing();
        if (target == null) return;

        if (slot is { Kind: SlotKind.Name })
        {
            OpenDropdown(slot.Host!, slot);
            return;
        }

        switch (target)
        {
            case BooleanBlock b:
                b.Value = !b.Value;
                break;

            case NumberBlock n:
                n.Editing = true;
                _editing = n;
                break;

            case VariableBlock v:
                OpenDropdown(v, null);
                break;
        }
    }

    /// <summary>Stops keyboard routing into a number field; the typed text stays.</summary>
    internal void CommitEditing()
    {
        if (_editing == null) return;
        _editing.Editing = false;
        _editing = null;
    }

    internal SnapTarget? FindSnap(Block drag)
    {
        LayoutAll();

        Block tail = drag.Tail;
        Vector2 dragTop = drag.PreviousConnection;
        Vector2 dragBottom = tail.NextConnection;
        Vector2 dragCenter = new(drag.X + drag.Width / 2f, drag.Y + drag.Height / 2f);

        // The dragged structure must never be offered back to itself: attaching a stack
        // to itself is exactly how the linked list used to end up with a cycle.
        HashSet<Block> dragBlocks = drag.CollectBlocks();

        SnapTarget? best = null;
        float bestDistance = Theme.SnapRadius;

        void Consider(SnapKind kind, Block target, Vector2 point, Vector2 from, float width,
                      Slot? slot = null, int mouth = 0)
        {
            float d = Vector2.Distance(from, point);
            if (d >= bestDistance) return;
            bestDistance = d;
            best = new SnapTarget(kind, target, point, Math.Max(width, drag.Width), slot, mouth);
        }

        // Only reporters go into fields; stack/hat/c-block tops have nowhere to sit.
        bool dragIsReporter = drag is ReporterBlock;

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

                // The dragged top drops into a mouth of a C-block.
                if (drag.HasPreviousConnection && b is CBlock c)
                {
                    if (tail.HasNextConnection || c.Inner == null)
                        Consider(SnapKind.Inside, c, c.InnerConnection, dragTop, c.Width - Theme.Indent, null, 0);

                    if (c.HasElse && (tail.HasNextConnection || c.Inner2 == null))
                        Consider(SnapKind.Inside, c, c.Inner2Connection, dragTop, c.Width - Theme.Indent, null, 1);
                }

                // A reporter drops into an empty value field of a block outside the drag.
                if (dragIsReporter)
                {
                    foreach (Slot slot in b.Slots)
                    {
                        if (slot.Kind != SlotKind.Value || slot.Content != null) continue;
                        Vector2 center = new(slot.Bounds.X + slot.Bounds.width / 2f,
                                             slot.Bounds.Y + slot.Bounds.height / 2f);
                        Consider(SnapKind.IntoSlot, b, center, dragCenter, slot.Bounds.width, slot, 0);
                    }
                }
            }
        }

        return best;
    }

    internal void Drop(Block drag, SnapTarget? snap)
    {
        // Landing the block ends the session that was carrying it. HandlePress creates
        // the session, so this is also what keeps a hand-driven press/drop cycle from
        // leaving a drag behind for the next Update to advance.
        if (_drag != null && ReferenceEquals(_drag.Root, drag))
        {
            _drag = null;
            _snap = null;
        }

        drag.Parent = this;

        if (snap != null)
        {
            switch (snap.Kind)
            {
                case SnapKind.Below:
                    if (snap.Target.AttachBelow(drag)) return;
                    break;

                case SnapKind.Inside:
                    {
                        var c = (CBlock)snap.Target;
                        bool ok = snap.Mouth == 0 ? c.AttachInside(drag) : c.AttachInside2(drag);
                        if (ok) return;
                        break;
                    }

                case SnapKind.IntoSlot:
                    if (snap.Slot != null && snap.Target.AttachToSlot(snap.Slot, drag)) return;
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
        // so it can always be grabbed again. A chain is clamped by its whole bounding
        // box, otherwise a long script dropped near an edge would disappear off-screen.
        drag.X = ClampX(drag.X, drag.Flatten().Max(b => b.Width));
        drag.Y = ClampY(drag.Y, drag.ChainHeight());
        _roots.Add(drag);
    }

    // ------------------------------------------------------------ start button --

    /// <summary>Converts the workspace and prints the AST; execution comes later.</summary>
    internal AstResult RunStart()
    {
        _validation = AstConverter.Convert(this);
        Console.WriteLine(_validation.Dump());
        _flashUntil = Raylib.GetTime() + 1.5;
        return _validation;
    }

    /// <summary>Loads the map the host parsed from the input file into the map
    /// panel; null (no file) leaves the panel showing its empty state.</summary>
    internal void LoadMap(MapHandler? map) => _map.SetMap(map);

    /// <summary>Puts every mined gold cell back; a no-op without a map or mines.</summary>
    internal void ResetMined() => _map.Map?.ResetMined();

    /// <summary>Converts the workspace and hands the rendered source to the save
    /// handler; false when there is no script or no input file to write to.</summary>
    internal bool Save()
    {
        AstResult result = AstConverter.Convert(this);
        if (result.Scripts.Length == 0)
        {
            Console.WriteLine("save: nothing to save (add a Start block)");
            return false;
        }
        if (SaveScript == null)
        {
            Console.WriteLine("save: no input file to save to");
            return false;
        }

        SaveScript(result.ToMineCode());
        return true;
    }

    // ------------------------------------------------------------------ badges --

    /// <summary>A one-line health readout under the Start button; the button colour says
    /// the same thing, but text makes the state unambiguous.</summary>
    void DrawStatus(int screenX, int screenY)
    {
        string text;
        Color color;
        if (_validation.Errors.Count > 0)
        {
            int n = _validation.Errors.Count;
            text = n == 1 ? "1 problem" : $"{n} problems";
            color = Theme.Badge;
        }
        else if (_validation.Scripts.Length > 0)
        {
            text = "ready";
            color = Theme.StartGreen;
        }
        else
        {
            text = "add a Start block";
            color = Theme.TextDark;
        }

        int w = Raylib.MeasureText(text, 12);
        int x = Math.Max(screenX + 8, screenX + Width - w - 8);
        int y = screenY + 8 + Theme.StartButtonH + 3;
        Raylib.DrawText(text, x, y, 12, color);
    }

    void DrawBadge(Block b, int screenX, int screenY)
    {
        int cx = b.X + b.Width - 5 + screenX;
        int cy = b.Y + 6 + screenY;
        bool flash = Raylib.GetTime() < _flashUntil;
        Raylib.DrawCircle(cx, cy, Theme.BadgeRadius, flash ? Raylib.GOLD : Theme.Badge);
        Raylib.DrawText("!", cx - 2, cy - Theme.BadgeRadius + 1, 14, Raylib.WHITE);
    }

    /// <summary>Pale veil over one script while manual robot control owns the run:
    /// the chain's bounding box, drawn after the chain so it reads as "frozen".</summary>
    static void DrawChainVeil(Block root, int screenX, int screenY)
    {
        List<Block> blocks = root.Flatten().ToList();
        int minX = blocks.Min(b => b.X);
        int maxX = blocks.Max(b => b.X + b.Width);
        int minY = blocks.Min(b => b.Y);
        int maxY = blocks.Max(b => b.Y + b.Height);
        Raylib.DrawRectangle(screenX + minX - 4, screenY + minY - 4,
            maxX - minX + 8, maxY - minY + 8, new Color(255, 255, 255, 170));
    }

    void DrawTooltip(int screenX, int screenY)
    {
        if (_validation.Errors.Count == 0) return;

        Vector2 local = Raylib.GetMousePosition() - new Vector2(screenX, screenY);
        foreach (AstError e in _validation.Errors)
        {
            Block b = e.Block;
            if (local.X < b.X || local.X >= b.X + b.Width || local.Y < b.Y || local.Y >= b.Y + b.Height)
                continue;

            int w = Raylib.MeasureText(e.Message, 14) + 16;
            int x = (int)Raylib.GetMousePosition().X + 14;
            int y = (int)Raylib.GetMousePosition().Y + 14;
            Raylib.DrawRectangle(x, y, w, 24, new Color(20, 22, 30, 215));
            Raylib.DrawText(e.Message, x + 8, y + 5, 14, Raylib.WHITE);
            break;
        }
    }

    // --------------------------------------------------------------- dropdowns --

    void OpenDropdown(Block owner, Slot? nameSlot)
    {
        CommitEditing();
        _dropOwner = owner;
        _dropSlot = nameSlot;

        Rectangle anchor = nameSlot?.Bounds ?? owner.Bounds;
        _drop.Open(anchor,
            _registry.Names.Select(n => (n, _registry.IsBuiltin(n))),
            nameSlot?.SelectedName ?? (owner as VariableBlock)?.Name,
            Width, Height);
    }

    internal void ApplyDropPicked()
    {
        string? picked = _drop.Picked;
        _drop.Close();
        if (picked == null) return;

        if (picked == DropDown.MakeNew)
        {
            _prompt.Open(Math.Clamp(Width / 2f - 134, 8, Math.Max(8, Width - 276)),
                         Math.Clamp(Height / 2f - 44, 8, Math.Max(8, Height - 96)));
            return;
        }
        ApplyName(picked);
    }

    /// <summary>Creates the prompted variable and writes it into the field that asked.</summary>
    internal bool ConfirmPrompt()
    {
        if (!_prompt.Confirm(_registry)) return false;
        string name = _prompt.Text;
        _prompt.Close();
        ApplyName(name);
        return true;
    }

    void ApplyName(string name)
    {
        if (_dropSlot != null)
        {
            _dropSlot.SelectedName = name;
            _dropSlot.Host?.RecomputeWidth();
        }
        else if (_dropOwner is VariableBlock vb)
        {
            vb.Name = name;
            vb.RecomputeWidth();
        }
        _dropSlot = null;
        _dropOwner = null;
    }

    // ------------------------------------------------------------------ helpers --

    static Slot? SlotAt(Block b, Vector2 p)
    {
        foreach (Slot s in b.Slots)
        {
            if (p.X >= s.Bounds.X && p.X < s.Bounds.X + s.Bounds.width &&
                p.Y >= s.Bounds.Y && p.Y < s.Bounds.Y + s.Bounds.height)
                return s;
        }
        return null;
    }

    static bool InsideRect(Vector2 p, Rectangle r)
        => p.X >= r.X && p.X < r.X + r.width && p.Y >= r.Y && p.Y < r.Y + r.height;

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

    internal void LayoutAll()
    {
        foreach (Block root in _roots)
            root.Layout(root.X, root.Y);
    }
}

/// <summary>The workspace's green-flag button: an <see cref="Element"/> that reports
/// validation state through its colour and exposes the <see cref="IInteractable"/> contract.</summary>
sealed class StartButton : Element, IInteractable
{
    public override int Width { get; set; }
    public override int Height { get; set; }
    public override int WPercent { get; set; }
    public override int HPercent { get; set; }

    /// <summary>True when the workspace has at least one Start block to convert.</summary>
    public bool HasScript { get; set; }

    /// <summary>True when live validation reports at least one problem.</summary>
    public bool HasErrors { get; set; }

    /// <summary>True while manual robot control owns the run: greyed out and not
    /// clickable (the workspace keeps it out of the hover and the click).</summary>
    public bool Disabled { get; set; }

    public bool IsHovered { get; set; }
    public bool IsActivated { get; set; }

    public Rectangle Bounds => new Rectangle(X, Y, Width, Height);

    public override void Update(int offsetX, int offsetY) { }

    public void OnHover() { IsHovered = true; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_POINTING_HAND); }
    public void OnUnhover() { IsHovered = false; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_ARROW); }
    public void OnActivated() { IsActivated = true; }
    public void OnDrag(Vector2 delta) { }

    public override void Draw(int offsetX, int offsetY)
    {
        int x = X + offsetX;
        int y = Y + offsetY;
        var screenBounds = new Rectangle(x, y, Width, Height);

        Color color = Disabled ? Theme.StartDisabled
            : HasErrors ? Theme.StartWarn
            : HasScript ? Theme.StartGreen
            : Theme.StartDisabled;
        if (IsHovered) color = Theme.Lighten(color, 0.14f);
        if (IsHovered && Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT))
            color = Theme.Darken(color, 0.1f);

        Raylib.DrawRectangleRounded(screenBounds, 0.45f, 8, color);

        Color textColor = !Disabled && (HasScript || HasErrors) ? Raylib.WHITE : Theme.TextDark;
        int centerY = (int)(y + Height / 2f);
        int textW = Raylib.MeasureText("Start", 14);
        int contentW = 8 + 4 + textW;
        int contentX = x + (Width - contentW) / 2;

        Raylib.DrawTriangle(
            new Vector2(contentX, centerY - 5),
            new Vector2(contentX, centerY + 5),
            new Vector2(contentX + 8, centerY),
            textColor);
        Raylib.DrawText("Start", contentX + 12, centerY - 7, 14, textColor);
    }
}

/// <summary>The workspace's save button: grey, parked to the left of Start by the same
/// resolvers Start uses for its own anchoring. It reports no state, so it stays grey -
/// hover and press only lighten/darken it like Start does.</summary>
sealed class SaveButton : Element, IInteractable
{
    public override int Width { get; set; }
    public override int Height { get; set; }
    public override int WPercent { get; set; }
    public override int HPercent { get; set; }

    public bool IsHovered { get; set; }
    public bool IsActivated { get; set; }

    public Rectangle Bounds => new Rectangle(X, Y, Width, Height);

    public SaveButton(Func<int> x, Func<int> y) : base(x, y)
    {
        Width = Theme.SaveButtonW;
        Height = Theme.SaveButtonH;
    }

    public override void Update(int offsetX, int offsetY) { }

    public void OnHover() { IsHovered = true; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_POINTING_HAND); }
    public void OnUnhover() { IsHovered = false; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_ARROW); }
    public void OnActivated() { IsActivated = true; }
    public void OnDrag(Vector2 delta) { }

    public override void Draw(int offsetX, int offsetY)
    {
        int x = X + offsetX;
        int y = Y + offsetY;
        Color color = Theme.SaveGray;
        if (IsHovered) color = Theme.Lighten(color, 0.14f);
        if (IsHovered && Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT))
            color = Theme.Darken(color, 0.1f);

        Raylib.DrawRectangleRounded(new Rectangle(x, y, Width, Height), 0.45f, 8, color);

        int textW = Raylib.MeasureText("Save", 14);
        Raylib.DrawText("Save", x + (Width - textW) / 2, y + (Height - 14) / 2, 14, Raylib.WHITE);
    }
}

/// <summary>The yellow "Reset Gold" button left of Save: puts every mined gold cell
/// back (the map action, so it wears the gold colour). It reports no state - hover
/// and press only lighten/darken it like Save does.</summary>
sealed class ResetButton : Element, IInteractable
{
    public override int Width { get; set; }
    public override int Height { get; set; }
    public override int WPercent { get; set; }
    public override int HPercent { get; set; }

    public bool IsHovered { get; set; }
    public bool IsActivated { get; set; }

    public Rectangle Bounds => new Rectangle(X, Y, Width, Height);

    public ResetButton(Func<int> x, Func<int> y) : base(x, y)
    {
        Width = Theme.ResetButtonW;
        Height = Theme.SaveButtonH;
    }

    public override void Update(int offsetX, int offsetY) { }

    public void OnHover() { IsHovered = true; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_POINTING_HAND); }
    public void OnUnhover() { IsHovered = false; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_ARROW); }
    public void OnActivated() { IsActivated = true; }
    public void OnDrag(Vector2 delta) { }

    public override void Draw(int offsetX, int offsetY)
    {
        int x = X + offsetX;
        int y = Y + offsetY;
        Color color = Theme.MapGold;
        if (IsHovered) color = Theme.Lighten(color, 0.14f);
        if (IsHovered && Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT))
            color = Theme.Darken(color, 0.1f);

        Raylib.DrawRectangleRounded(new Rectangle(x, y, Width, Height), 0.45f, 8, color);

        int textW = Raylib.MeasureText("Reset Gold", 14);
        Raylib.DrawText("Reset Gold", x + (Width - textW) / 2, y + (Height - 14) / 2, 14, Theme.TextDark);
    }
}
