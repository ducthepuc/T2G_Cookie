using Raylib_CsLo;
using T2G_Cookie.Shared;
using T2G_Cookie.Shared.Values;

namespace T2G_Cookie.Gui;

/// <summary>
/// A titled read-only list of text lines: the generated source (with line numbers and
/// the running line marked) and the variable values both use it.
/// </summary>
sealed class TextPanel : Frame
{
    const int TitleH = 22;
    const int LineH = 18;
    const int TextSize = 14;
    const int PadX = 8;

    readonly string _title;
    readonly string _placeholder;
    readonly bool _lineNumbers;

    int _firstLine;
    int? _followedLine;

    public IReadOnlyList<string> Lines { get; set; } = [];

    /// <summary>The 1-based line to mark and keep in view; null marks nothing.</summary>
    public int? ActiveLine { get; set; }

    public TextPanel(Func<int> x, Func<int> y, int wPercent, int hPercent,
                     string title, string placeholder, bool lineNumbers)
        : base(x, y, wPercent, hPercent)
    {
        DrawBackground = true;
        Color = Raylib.WHITE;
        _title = title;
        _placeholder = placeholder;
        _lineNumbers = lineNumbers;
    }

    public void Scroll(float wheel) => _firstLine -= (int)wheel;

    public override void Draw(int offsetX, int offsetY)
    {
        base.Draw(offsetX, offsetY);

        (int drawX, int drawY) = ResolvePosition(offsetX, offsetY);
        Raylib.DrawRectangleLines(drawX, drawY, Width, Height, Theme.Border);
        Raylib.DrawText(_title, drawX + PadX, drawY + 5, 12, Theme.PlaceholderText);

        int visible = Math.Max(1, (Height - TitleH - 4) / LineH);
        if (ActiveLine != _followedLine)
        {
            _followedLine = ActiveLine;
            if (ActiveLine is { } line)
            {
                if (line - 1 < _firstLine) _firstLine = line - 1;
                else if (line - 1 >= _firstLine + visible) _firstLine = line - visible;
            }
        }
        _firstLine = Math.Clamp(_firstLine, 0, Math.Max(0, Lines.Count - visible));

        int top = drawY + TitleH;
        if (Lines.Count == 0)
        {
            Raylib.DrawText(_placeholder, drawX + PadX, top + 2, 13, Theme.PlaceholderText);
            return;
        }

        int gutter = _lineNumbers ? Raylib.MeasureText(Lines.Count.ToString(), TextSize) + 10 : 0;

        Raylib.BeginScissorMode(drawX + 1, top, Width - 2, Height - TitleH - 1);
        int end = Math.Min(Lines.Count, _firstLine + visible);
        for (int i = _firstLine; i < end; i++)
        {
            int y = top + (i - _firstLine) * LineH;
            if (ActiveLine == i + 1)
                Raylib.DrawRectangle(drawX + 1, y, Width - 2, LineH, Theme.RunLineBg);
            if (_lineNumbers)
                Raylib.DrawText((i + 1).ToString(), drawX + PadX, y + 2, TextSize, Theme.PlaceholderText);
            Raylib.DrawText(Lines[i], drawX + PadX + gutter, y + 2, TextSize, Theme.TextDark);
        }
        Raylib.EndScissorMode();
    }
}

/// <summary>
/// The robot's sensor: what is directly in front of it right now, read straight from
/// the map every frame, so it follows every move and turn without any isThere call and
/// without marking that cell as explored.
/// </summary>
sealed class SensorField : Frame
{
    const int PadX = 10;
    const int Swatch = 12;

    readonly Func<MapHandler?> _map;

    public SensorField(Func<int> x, Func<int> y, int wPercent, Func<MapHandler?> map)
        : base(x, y, wPercent, 0)
    {
        DrawBackground = true;
        Color = Raylib.WHITE;
        Height = Theme.SensorH;
        _map = map;
    }

    /// <summary>What the sensor reads now, or null without a map or robot.</summary>
    public MapHandler.MapElement? Reading
    {
        get
        {
            MapHandler? map = _map();
            if (map?.IdentifyRobot(out Direction? facing) is not { } robot) return null;
            return map.GetForward(facing!.Value, robot);
        }
    }

    public override void Draw(int offsetX, int offsetY)
    {
        base.Draw(offsetX, offsetY);

        (int drawX, int drawY) = ResolvePosition(offsetX, offsetY);
        Raylib.DrawRectangleLines(drawX, drawY, Width, Height, Theme.Border);

        const string caption = "Sensor ahead:";
        int textY = drawY + (Height - 14) / 2;
        Raylib.DrawText(caption, drawX + PadX, textY, 14, Theme.TextDark);

        (string label, Color color) = Reading switch
        {
            MapHandler.MapElement.Wall => ("wall", Theme.MapWall),
            MapHandler.MapElement.Gold => ("gold", Theme.MapGold),
            MapHandler.MapElement.Empty => ("empty", Theme.MapEmptyDot),
            _ => ("-", Theme.Border),
        };

        int x = drawX + PadX + Raylib.MeasureText(caption, 14) + 10;
        Raylib.DrawRectangle(x, drawY + (Height - Swatch) / 2, Swatch, Swatch, color);
        Raylib.DrawText(label, x + Swatch + 6, textY, 14, Theme.TextDark);
    }
}

/// <summary>A workspace bar button whose face follows the run state.</summary>
sealed class BarButton : Element, IInteractable
{
    readonly Func<string> _label;
    readonly Func<Color> _color;

    public override int Width { get; set; }
    public override int Height { get; set; }
    public override int WPercent { get; set; }
    public override int HPercent { get; set; }

    public bool IsHovered { get; set; }
    public bool IsActivated { get; set; }

    public Rectangle Bounds => new Rectangle(X, Y, Width, Height);

    public BarButton(Func<int> x, Func<int> y, int width, Func<string> label, Func<Color> color)
        : base(x, y)
    {
        Width = width;
        Height = Theme.SaveButtonH;
        _label = label;
        _color = color;
    }

    public void OnHover() { IsHovered = true; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_POINTING_HAND); }
    public void OnUnhover() { IsHovered = false; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_ARROW); }
    public void OnActivated() { IsActivated = true; }
    public void OnDrag(System.Numerics.Vector2 delta) { }

    public override void Draw(int offsetX, int offsetY)
    {
        int x = X + offsetX;
        int y = Y + offsetY;
        Color color = _color();
        if (IsHovered) color = Theme.Lighten(color, 0.14f);
        if (IsHovered && Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT))
            color = Theme.Darken(color, 0.1f);

        Raylib.DrawRectangleRounded(new Rectangle(x, y, Width, Height), 0.45f, 8, color);

        string label = _label();
        int textW = Raylib.MeasureText(label, 14);
        Raylib.DrawText(label, x + (Width - textW) / 2, y + (Height - 14) / 2, 14, Raylib.WHITE);
    }
}
