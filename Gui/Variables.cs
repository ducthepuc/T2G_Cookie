using System.Numerics;
using Raylib_CsLo;

namespace T2G_Cookie.Gui;

/// <summary>Where variable names come from: everything the user creates.</summary>
sealed class VariableRegistry
{
    /// <summary>The language's keywords, reserved in every letter case.</summary>
    static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "by", "else", "false", "forward", "here", "if", "isThere", "mine",
        "not", "or", "print", "set", "step", "to", "true", "turn", "while"
    };

    readonly List<string> _names = new();

    /// <summary>The creatable variables, in menu order.</summary>
    public IReadOnlyList<string> Names => _names;

    public bool Contains(string name) => _names.Contains(name);

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
        if (Keywords.Contains(name))
        {
            error = $"{name} is a keyword, pick another name";
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

    public bool Remove(string name) => _names.Remove(name);
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

    /// <summary>The variable whose delete button was just clicked.</summary>
    public string? DeleteRequested { get; private set; }

    /// <summary>True when the last click hit the delete button of a variable in use.</summary>
    public bool DeleteRefused { get; private set; }

    readonly List<string> _entries = new();
    IReadOnlySet<string> _inUse = new HashSet<string>();
    int _hover = -1;
    bool _hoverDelete;
    string? _selected;

    public void Open(Rectangle anchor, IEnumerable<string> entries, IReadOnlySet<string> inUse,
                     string? selected, int maxW, int maxH)
    {
        _entries.Clear();
        _entries.AddRange(entries);
        // Only the variable rows carry a delete button, so only they need room for it.
        int w = 140;
        foreach (string label in _entries)
            w = Math.Max(w, Raylib.MeasureText(label, Theme.FontSize) + 28 + Theme.DeleteButtonSize + 6);
        _entries.Add("Make a variable...");
        w = Math.Max(w, Raylib.MeasureText(_entries[^1], Theme.FontSize) + 28);
        _inUse = inUse;
        _selected = selected;

        if (_selected != null) w += 16;
        w = Math.Min(w, Math.Max(80, maxW - 16));

        float h = _entries.Count * Theme.EntryHeight + 8;
        float x = Math.Clamp(anchor.X, 8, Math.Max(8, maxW - w - 8));
        float y = anchor.Y + anchor.height + 2;
        if (y + h > maxH - 8) y = Math.Max(8, anchor.Y - h - 2); // flip above when it would overflow

        LocalBounds = new Rectangle(x, y, w, h);
        IsOpen = true;
        Picked = null;
        DeleteRequested = null;
        DeleteRefused = false;
        _hover = -1;
        _hoverDelete = false;
    }

    public void Close()
    {
        IsOpen = false;
        _hover = -1;
        _hoverDelete = false;
        _selected = null;
    }

    public void Move(Vector2 localMouse)
    {
        _hover = IndexAt(localMouse);
        _hoverDelete = OverDelete(_hover, localMouse);
    }

    /// <summary>True when the click landed inside the popup (handled, even on a disabled row).</summary>
    public bool Click(Vector2 localMouse)
    {
        DeleteRequested = null;
        DeleteRefused = false;

        int i = IndexAt(localMouse);
        if (i < 0) return false;

        if (OverDelete(i, localMouse))
        {
            if (_inUse.Contains(_entries[i])) DeleteRefused = true;
            else DeleteRequested = _entries[i];
            return true;
        }

        Picked = i == _entries.Count - 1 ? MakeNew : _entries[i];
        return true;
    }

    /// <summary>The delete button of a variable row, left of the selected row's check mark.</summary>
    public Rectangle DeleteBounds(int row)
    {
        float size = Theme.DeleteButtonSize;
        float right = LocalBounds.X + LocalBounds.width - (_selected != null ? 22 : 6);
        float top = LocalBounds.Y + 4 + row * Theme.EntryHeight + (Theme.EntryHeight - size) / 2f;
        return new Rectangle(right - size, top, size, size);
    }

    bool OverDelete(int row, Vector2 p)
    {
        if (row < 0 || row >= _entries.Count - 1) return false; // "Make a variable..." has none
        Rectangle r = DeleteBounds(row);
        return p.X >= r.X && p.X < r.X + r.width && p.Y >= r.Y && p.Y < r.Y + r.height;
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
            Raylib.DrawText(_entries[i], x + 10, ey + (Theme.EntryHeight - Theme.FontSize) / 2, Theme.FontSize, Theme.TextDark);

            if (_entries[i] == _selected)
                DrawCheck(x + w - 18, ey + 7, Theme.StartGreen);
        }

        if (_hover >= 0 && _hover < _entries.Count - 1)
            DrawDeleteButton(_hover, offsetX, offsetY);
    }

    void DrawDeleteButton(int row, int offsetX, int offsetY)
    {
        Rectangle local = DeleteBounds(row);
        int x = (int)local.X + offsetX;
        int y = (int)local.Y + offsetY;
        int size = (int)local.width;
        bool inUse = _inUse.Contains(_entries[row]);

        Color fill = inUse ? Theme.PopupBorder
            : _hoverDelete ? Theme.Lighten(Theme.Badge, 0.2f) : Theme.Badge;
        Raylib.DrawRectangleRounded(new Rectangle(x, y, size, size), 0.3f, 6, fill);
        const int inset = 5;
        Raylib.DrawLineEx(new Vector2(x + inset, y + inset),
            new Vector2(x + size - inset, y + size - inset), 2f, Raylib.WHITE);
        Raylib.DrawLineEx(new Vector2(x + size - inset, y + inset),
            new Vector2(x + inset, y + size - inset), 2f, Raylib.WHITE);

        if (inUse && _hoverDelete)
        {
            const string hint = "In use";
            int tipW = Raylib.MeasureText(hint, 13) + 12;
            int tipX = x + size - tipW;
            Raylib.DrawRectangle(tipX, y - 22, tipW, 20, new Color(20, 22, 30, 215));
            Raylib.DrawText(hint, tipX + 6, y - 19, 13, Raylib.WHITE);
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
