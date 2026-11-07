using System.Numerics;
using Raylib_CsLo;

namespace T2G_Cookie.Gui;

/// <summary>
/// Where variable names come from: three read-only built-ins (the cell kinds, so
/// `isThere wall` reads naturally) plus everything the user creates.
/// </summary>
sealed class VariableRegistry
{
    public static readonly string[] BuiltinNames = ["empty", "wall", "gold"];
    static readonly Dictionary<string, int> BuiltinValues = new()
    {
        ["empty"] = 0,
        ["wall"] = 1,
        ["gold"] = 2
    };

    readonly List<string> _names = new(BuiltinNames);

    public IReadOnlyList<string> Names => _names;

    public bool IsBuiltin(string name) => BuiltinValues.ContainsKey(name);
    public bool Contains(string name) => _names.Contains(name);

    /// <summary>The cell code a built-in stands for, or -1 when the name isn't a built-in.</summary>
    public int GetBuiltinValue(string name) => BuiltinValues.GetValueOrDefault(name, -1);

    /// <summary>MineCode identifiers: [a-zA-Z][a-zA-Z0-9]* (see MineCodeLexer.g4).</summary>
    public static bool IsValidName(string name)
        => name.Length > 0 && char.IsAsciiLetter(name[0]) && name.All(char.IsAsciiLetterOrDigit);

    public bool TryCreate(string name, out string? error)
    {
        if (!IsValidName(name))
        {
            error = "names start with a letter, then letters/digits";
            return false;
        }
        if (_names.Contains(name))
        {
            error = $"{name} already exists";
            return false;
        }
        _names.Add(name);
        error = null;
        return true;
    }
}

/// <summary>
/// Shared popup list for variable fields (Scratch-style dropdown). Pure state machine:
/// Open/Move/Click take workspace-local coordinates so tests can drive it without a mouse.
/// </summary>
sealed class DropDown
{
    public const string MakeNew = "\u0000make-new";

    public bool IsOpen { get; private set; }
    public Rectangle LocalBounds { get; private set; }
    public string? Picked { get; private set; }

    // Muted entries (the built-ins) stay selectable: picking one is how you use it.
    // The read-only rule for `set` shows up as a badge, not as a dead menu row.
    readonly List<(string Label, bool Muted)> _entries = new();
    int _hover = -1;
    string? _selected;

    public void Open(Rectangle anchor, IEnumerable<(string Label, bool Muted)> entries,
                     string? selected, int maxW, int maxH)
    {
        _entries.Clear();
        _entries.AddRange(entries);
        _entries.Add(("Make a variable...", false));
        _selected = selected;

        int w = 140;
        foreach ((string label, _) in _entries)
            w = Math.Max(w, Raylib.MeasureText(label, Theme.FontSize) + 28);
        if (_selected != null) w += 16;
        w = Math.Min(w, Math.Max(80, maxW - 16));

        float h = _entries.Count * Theme.EntryHeight + 8;
        float x = Math.Clamp(anchor.X, 8, Math.Max(8, maxW - w - 8));
        float y = anchor.Y + anchor.height + 2;
        if (y + h > maxH - 8) y = Math.Max(8, anchor.Y - h - 2); // flip above when it would overflow

        LocalBounds = new Rectangle(x, y, w, h);
        IsOpen = true;
        Picked = null;
        _hover = -1;
    }

    public void Close()
    {
        IsOpen = false;
        _hover = -1;
        _selected = null;
    }

    public void Move(Vector2 localMouse) => _hover = IndexAt(localMouse);

    /// <summary>True when the click landed inside the popup (handled, even on a disabled row).</summary>
    public bool Click(Vector2 localMouse)
    {
        int i = IndexAt(localMouse);
        if (i < 0) return false;
        Picked = i == _entries.Count - 1 ? MakeNew : _entries[i].Label;
        return true;
    }

    int IndexAt(Vector2 p)
    {
        if (!IsOpen) return -1;
        if (p.X < LocalBounds.X || p.X >= LocalBounds.X + LocalBounds.width ||
            p.Y < LocalBounds.Y || p.Y >= LocalBounds.Y + LocalBounds.height) return -1;
        int i = (int)((p.Y - LocalBounds.Y - 4) / Theme.EntryHeight);
        return i >= 0 && i < _entries.Count ? i : -1;
    }

    public void Draw(int offsetX, int offsetY)
    {
        if (!IsOpen) return;
        int x = (int)LocalBounds.X + offsetX;
        int y = (int)LocalBounds.Y + offsetY;
        int w = (int)LocalBounds.width;
        int h = (int)LocalBounds.height;

        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, h), 0.15f, 6, Theme.PopupBorder);
        Raylib.DrawRectangleRounded(new Rectangle(x + 1, y + 1, w - 2, h - 2), 0.15f, 6, Theme.PopupBg);

        for (int i = 0; i < _entries.Count; i++)
        {
            int ey = y + 4 + i * Theme.EntryHeight;
            if (i == _hover)
                Raylib.DrawRectangle(x + 2, ey, w - 4, Theme.EntryHeight, Theme.PopupHover);
            Color c = _entries[i].Muted ? Raylib.LIGHTGRAY : Theme.TextDark;
            Raylib.DrawText(_entries[i].Label, x + 10, ey + (Theme.EntryHeight - Theme.FontSize) / 2, Theme.FontSize, c);

            if (_entries[i].Label == _selected)
                DrawCheck(x + w - 18, ey + 7, Theme.StartGreen);
        }
    }

    static void DrawCheck(int x, int y, Color color)
    {
        Raylib.DrawLineEx(new Vector2(x, y + 4), new Vector2(x + 3, y + 7), 2f, color);
        Raylib.DrawLineEx(new Vector2(x + 3, y + 7), new Vector2(x + 9, y), 2f, color);
    }
}

/// <summary>One-line inline prompt used by "Make a variable...".</summary>
sealed class NamePrompt
{
    public bool IsOpen { get; private set; }
    public string Text { get; private set; } = "";
    public string? Error { get; private set; }
    public Rectangle LocalBounds { get; private set; }

    public void Open(float x, float y)
    {
        IsOpen = true;
        Text = "";
        Error = null;
        LocalBounds = new Rectangle(x, y, 268, 88);
    }

    public void Close() => IsOpen = false;

    public void InputChar(char c)
    {
        if (Text.Length >= 24) return;
        if (!char.IsAsciiLetterOrDigit(c)) return;
        Text += c;
        Error = null;
    }

    public void Backspace()
    {
        if (Text.Length > 0) Text = Text[..^1];
        Error = null;
    }

    /// <summary>Creates the variable in the registry; false keeps the prompt open with an error.</summary>
    public bool Confirm(VariableRegistry registry)
    {
        if (!registry.TryCreate(Text, out string? error))
        {
            Error = error;
            return false;
        }
        return true;
    }

    public void Draw(int offsetX, int offsetY)
    {
        if (!IsOpen) return;
        int x = (int)LocalBounds.X + offsetX;
        int y = (int)LocalBounds.Y + offsetY;
        int w = (int)LocalBounds.width;
        int h = (int)LocalBounds.height;

        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, h), 0.15f, 6, Theme.PopupBorder);
        Raylib.DrawRectangleRounded(new Rectangle(x + 1, y + 1, w - 2, h - 2), 0.15f, 6, Theme.PopupBg);

        Raylib.DrawText("New variable name", x + 12, y + 8, 14, Theme.TextDark);

        Raylib.DrawRectangle(x + 12, y + 30, w - 24, 26, Raylib.WHITE);
        Raylib.DrawRectangleLines(x + 12, y + 30, w - 24, 26, Theme.PopupBorder);
        Raylib.DrawText(string.IsNullOrEmpty(Text) ? "_" : Text, x + 18, y + 36, Theme.FontSize,
            string.IsNullOrEmpty(Text) ? Raylib.GRAY : Theme.TextDark);

        // A visible caret makes it obvious the prompt is taking keystrokes.
        int caretX = x + 18 + Raylib.MeasureText(Text, Theme.FontSize) + 1;
        Raylib.DrawRectangle(caretX, y + 35, 2, Theme.FontSize + 2, Theme.TextDark);

        string hint = Error ?? "Enter = create, Esc = cancel";
        Raylib.DrawText(hint, x + 12, y + 62, 13, Error != null ? Theme.Badge : Raylib.GRAY);
    }
}
