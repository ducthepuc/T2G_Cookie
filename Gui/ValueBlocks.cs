using Raylib_CsLo;

namespace T2G_Cookie.Gui;

/// <summary>
/// An editable number literal: click to type digits, Enter or click-away commits.
/// This is the only block that accepts keyboard content, and it accepts numbers
/// only - MineCode's NUMBER token is <c>[0-9]+</c>, so anything else (letters,
/// punctuation, an empty field) is rejected by the property itself rather than
/// by whoever happens to call it.
/// </summary>
class NumberBlock : ReporterBlock
{
    /// <summary>Field length cap; keeps the pill on screen and the literal sane.</summary>
    public const int MaxDigits = 9;

    string _text = "0";

    /// <summary>The literal. Assigning anything but digits is filtered here.</summary>
    public string Text
    {
        get => _text;
        set
        {
            string clean = Sanitize(value);
            if (clean == _text) return;
            _text = clean;
            RecomputeWidth();
        }
    }

    /// <summary>Digits only, leading zeros dropped, never empty - so the converter
    /// can never see a value the language cannot write.</summary>
    static string Sanitize(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).Take(MaxDigits).ToArray());
        digits = digits.TrimStart('0');
        return digits.Length == 0 ? "0" : digits;
    }

    /// <summary>True while the workspace is routing keyboard input into this block.</summary>
    public bool Editing { get; set; }

    public NumberBlock(BlockCategory category, Func<int> x, Func<int> y)
        : base(Array.Empty<Segment>(), category, x, y)
    {
        Kind = BlockKind.Number;
    }

    /// <summary>The one gate for typed input: digits are appended (a lone `0` is
    /// replaced rather than prefixed), everything else is dropped.</summary>
    public void InputChar(char c)
    {
        if (!char.IsAsciiDigit(c)) return;
        if (_text.Length >= MaxDigits) return;
        Text = _text == "0" ? c.ToString() : _text + c;
    }

    /// <summary>Trims the last digit. Trimming to nothing is fine: the setter
    /// sanitizes an empty string back to `0`, so there is no empty number.</summary>
    public void Backspace()
    {
        if (_text.Length > 0) Text = _text[..^1];
    }

    protected override int ComputeWidth()
        => Math.Max(Theme.MinReporterWidth, Theme.PadX * 2 + Raylib.MeasureText(_text, Theme.FontSize));

    public override void Draw(int offsetX, int offsetY)
    {
        DrawReporterPill(offsetX, offsetY);
        int sx = X + offsetX;
        int sy = Y + offsetY;

        // An inset box inside the pill: the pill says "a value", the box says
        // "type here". Without it a number looks like static label text and the
        // caret is the only hint anything happens on click.
        var field = new Rectangle(sx + 4, sy + 5, Width - 8, Height - 10);
        Color border = Editing ? Category.DarkColor
            : IsHovered ? new Color(0, 0, 0, 110)
            : new Color(0, 0, 0, 45);
        Raylib.DrawRectangleRounded(field, 0.7f, 8, border);
        Raylib.DrawRectangleRounded(
            new Rectangle(field.X + 1, field.Y + 1, field.width - 2, field.height - 2),
            0.7f, 8, Raylib.WHITE);

        int fs = Theme.FontSize;
        int ty = sy + (Height - fs) / 2;
        Raylib.DrawText(_text, sx + Theme.PadX, ty, fs, Theme.TextDark);

        if (Editing)
        {
            int cx = sx + Theme.PadX + Raylib.MeasureText(_text, fs) + 3;
            Raylib.DrawRectangle(cx, ty - 2, 2, fs + 4, Category.DarkColor);
        }
    }
}

/// <summary>A boolean literal; click toggles true / false.</summary>
class BooleanBlock : ReporterBlock
{
    public bool Value { get; set; }

    public BooleanBlock(BlockCategory category, Func<int> x, Func<int> y)
        : base(Array.Empty<Segment>(), category, x, y)
    {
        Kind = BlockKind.Boolean;
    }

    protected override int ComputeWidth()
        => Math.Max(Theme.MinReporterWidth, Theme.PadX * 2 + Raylib.MeasureText("false", Theme.FontSize));

    public override void Draw(int offsetX, int offsetY)
    {
        DrawReporterPill(offsetX, offsetY);
        string text = Value ? "true" : "false";
        int fs = Theme.FontSize;
        int x = X + offsetX + (Width - Raylib.MeasureText(text, fs)) / 2;
        int y = Y + offsetY + (Height - fs) / 2;
        Raylib.DrawText(text, x, y, fs, Raylib.WHITE);
    }
}

/// <summary>A variable-name reporter; click opens the variable dropdown.</summary>
class VariableBlock : ReporterBlock
{
    string? _name;

    public string? Name
    {
        get => _name;
        set
        {
            _name = value;
            RecomputeWidth();
        }
    }

    public VariableBlock(BlockCategory category, Func<int> x, Func<int> y)
        : base(Array.Empty<Segment>(), category, x, y)
    {
        Kind = BlockKind.Variable;
    }

    protected override int ComputeWidth()
        => Math.Max(Theme.MinReporterWidth, Theme.PadX * 2 + Raylib.MeasureText(_name ?? "variable", Theme.FontSize));

    public override void Draw(int offsetX, int offsetY)
    {
        DrawReporterPill(offsetX, offsetY);
        int ty = Y + offsetY + (Theme.HeaderHeight - Theme.FontSize) / 2;
        Raylib.DrawText(_name ?? "variable", X + offsetX + Theme.PadX, ty, Theme.FontSize,
            _name == null ? Raylib.LIGHTGRAY : Raylib.WHITE);
    }
}
