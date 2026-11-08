using System.Numerics;
using Raylib_CsLo;

namespace T2G_Cookie.Gui;

/// <summary>
/// The "Control Robot" checkbox frame under the map panel. Checking it hands the
/// run over to the manual buttons (the code gets veiled and Start is disabled);
/// the workspace reads <see cref="Checked"/> after every <see cref="Click"/>.
/// </summary>
sealed class RobotToggle : Frame
{
    const int BoxSize = 18;
    const int PadX = 10;

    readonly Action<bool> _onChanged;

    public bool Checked { get; private set; }
    public bool IsHovered { get; private set; }

    public RobotToggle(Func<int> x, Func<int> y, int wPercent, Action<bool> onChanged)
        : base(x, y, wPercent, 0)
    {
        DrawBackground = true;
        Color = Raylib.WHITE;
        Height = Theme.RobotToggleH;
        _onChanged = onChanged;
    }

    public void Click()
    {
        Checked = !Checked;
        _onChanged(Checked);
    }

    public void OnHover()
    {
        IsHovered = true;
        Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_POINTING_HAND);
    }

    public void OnUnhover()
    {
        IsHovered = false;
        Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_ARROW);
    }

    public override void Draw(int offsetX, int offsetY)
    {
        base.Draw(offsetX, offsetY); // square white panel, no children

        (int drawX, int drawY) = ResolvePosition(offsetX, offsetY);
        if (IsHovered)
            Raylib.DrawRectangle(drawX + 1, drawY + 1, Width - 2, Height - 2, Theme.PopupHover);
        Raylib.DrawRectangleLines(drawX, drawY, Width, Height, Theme.Border);

        int boxX = drawX + PadX;
        int boxY = drawY + (Height - BoxSize) / 2;
        var box = new Rectangle(boxX, boxY, BoxSize, BoxSize);
        if (Checked)
        {
            Raylib.DrawRectangleRounded(box, 0.25f, 4, Theme.MapRobot);
            // Checkmark drawn as two strokes so it stays crisp at 18px.
            Raylib.DrawLineEx(new Vector2(boxX + 4, boxY + 9),
                new Vector2(boxX + 8, boxY + 13), 2.4f, Raylib.WHITE);
            Raylib.DrawLineEx(new Vector2(boxX + 8, boxY + 13),
                new Vector2(boxX + 14, boxY + 5), 2.4f, Raylib.WHITE);
        }
        else
        {
            Raylib.DrawRectangleRoundedLines(box, 0.25f, 4, 1.5f,
                IsHovered ? Theme.SnapEdge : Theme.Border);
        }

        Raylib.DrawText("Control Robot", boxX + BoxSize + 8,
            drawY + (Height - 14) / 2, 14, Theme.TextDark);
    }
}

/// <summary>
/// The frame of manual robot buttons under the checkbox: TURN LEFT, TURN RIGHT and
/// FORWARD in the top row - mapping 1:1 onto the block vocabulary (turn by ±90,
/// step forward) - and a full-width MINE button under them that mines the gold the
/// robot stands on, the rule MachineSpirit's Mine block follows. Nothing fires
/// while the checkbox is off - <see cref="Click"/> checks the enabled predicate the
/// workspace hands in - and the same state greys the buttons out.
/// </summary>
sealed class RobotButtons : Frame
{
    const int PadX = 8;
    const int PadY = 8;
    const int ButtonH = 24;
    const int LabelMinSize = 9;
    const int LabelMaxSize = 14;

    static readonly string[] Labels = ["TURN LEFT", "TURN RIGHT", "FORWARD", "MINE"];
    const int MineIndex = 3;

    readonly Func<bool> _enabled;
    readonly Action[] _actions;

    public bool IsHovered { get; private set; }

    public RobotButtons(Func<int> x, Func<int> y, int wPercent,
                        Func<bool> enabled, params Action[] actions)
        : base(x, y, wPercent, 0)
    {
        DrawBackground = true;
        Color = Raylib.WHITE;
        Height = Theme.RobotButtonsH;
        _enabled = enabled;
        _actions = actions;
    }

    public void OnHover()
    {
        IsHovered = true;
        Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_POINTING_HAND);
    }

    public void OnUnhover()
    {
        IsHovered = false;
        Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_ARROW);
    }

    /// <summary>Fires the button under a workspace-local point; a no-op while the
    /// checkbox is off, which is what makes the controls dead in code-run mode.</summary>
    public void Click(Vector2 localMouse)
    {
        if (!_enabled()) return;
        for (int i = 0; i < Labels.Length && i < _actions.Length; i++)
        {
            Rectangle b = ButtonBounds(i);
            if (localMouse.X >= b.X && localMouse.X < b.X + b.width &&
                localMouse.Y >= b.Y && localMouse.Y < b.Y + b.height)
            {
                _actions[i]();
                return;
            }
        }
    }

    /// <summary>Button <paramref name="index"/> in workspace-local coordinates.</summary>
    public Rectangle ButtonBounds(int index)
    {
        (int x, int y, int w, int h) = ButtonRect(index, X, Y);
        return new Rectangle(x, y, w, h);
    }

    (int X, int Y, int W, int H) ButtonRect(int index, int panelX, int panelY)
    {
        if (index == MineIndex)
            return (panelX + PadX, panelY + PadY + ButtonH + Theme.RobotButtonGap,
                    Width - PadX * 2, ButtonH);

        int w = (Width - PadX * 2 - Theme.RobotButtonGap * (Labels.Length - 2)) / (Labels.Length - 1);
        return (panelX + PadX + index * (w + Theme.RobotButtonGap),
                panelY + PadY, w, ButtonH);
    }

    public override void Draw(int offsetX, int offsetY)
    {
        base.Draw(offsetX, offsetY); // square white panel, no children

        (int drawX, int drawY) = ResolvePosition(offsetX, offsetY);
        Raylib.DrawRectangleLines(drawX, drawY, Width, Height, Theme.Border);

        bool enabled = _enabled();
        Vector2 mouse = Raylib.GetMousePosition();

        for (int i = 0; i < Labels.Length; i++)
        {
            (int bx, int by, int bw, int bh) = ButtonRect(i, drawX, drawY);
            var rect = new Rectangle(bx, by, bw, bh);
            bool hot = enabled && IsHovered &&
                mouse.X >= rect.X && mouse.X < rect.X + rect.width &&
                mouse.Y >= rect.Y && mouse.Y < rect.Y + rect.height;

            bool isMine = i == MineIndex;
            Color fill = enabled ? (isMine ? Theme.MapGold : Theme.MapRobot) : Theme.ScrollTrack;
            if (hot) fill = Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT)
                ? Theme.Darken(fill, 0.12f)
                : Theme.Lighten(fill, 0.14f);
            Raylib.DrawRectangleRounded(rect, 0.45f, 8, fill);

            // Shrink the label until it fits the button: the panels scale with the
            // window and TURN RIGHT is the widest word in the row.
            int size = LabelMaxSize;
            while (size > LabelMinSize && Raylib.MeasureText(Labels[i], size) > bw - 8)
                size--;
            Raylib.DrawText(Labels[i], bx + (bw - Raylib.MeasureText(Labels[i], size)) / 2,
                by + (bh - size) / 2, size,
                !enabled ? Theme.PlaceholderText
                         : isMine ? Theme.TextDark : Raylib.WHITE);
        }
    }
}
