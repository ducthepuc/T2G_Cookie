using Raylib_CsLo;

namespace T2G_Cookie.Gui;

/// <summary>
/// The panel under the save/start bar where Print output lands: white, rounded, with
/// a border and a hint until the first line arrives. Lines are plain text drawn
/// top-down; nothing scrolls yet, so the oldest stay visible and the list simply
/// stops drawing at the bottom edge. A footer row under the text shows the gold
/// tally - x/n gold collected - fed from the map by the workspace.
/// </summary>
sealed class OutputField : Frame
{
    public const int PadX = 10;
    public const int PadY = 8;
    public const int LineSpacing = 4;
    public const int TextSize = 14;
    public const int FooterH = 22;
    public const int FooterTextSize = 12;

    /// <summary>The print output, oldest first; whatever runs Print appends here.</summary>
    public List<string> Lines { get; } = new();

    /// <summary>Hands a printed value to the panel; the VM runner calls this when
    /// the instruction it just executed is a print.</summary>
    public void Append(string text) => Lines.Add(text);

    // Indices into Lines that are runtime errors, drawn in the badge colour.
    readonly HashSet<int> _errorLines = new();

    /// <summary>Adds a runtime error to the panel.</summary>
    public void AppendError(string text)
    {
        _errorLines.Add(Lines.Count);
        Lines.Add(text);
    }

    /// <summary>Empties the panel.</summary>
    public void Clear()
    {
        Lines.Clear();
        _errorLines.Clear();
    }

    /// <summary>Gold mined so far / gold the map shipped with; the workspace keeps
    /// these in step with the map every frame.</summary>
    public int GoldMined { get; set; }
    public int TotalGold { get; set; }

    public OutputField(Func<int> x, Func<int> y, int wPercent, int hPercent)
        : base(x, y, wPercent, hPercent)
    {
        DrawBackground = true;
        Color = Raylib.WHITE;
    }

    public override void Draw(int offsetX, int offsetY)
    {
        base.Draw(offsetX, offsetY); // background, own scissor, (future) children

        // Border and text draw after the frame's scissor closes: the workspace
        // scissor still wraps everything, so neither can escape the panel's area
        // or the workspace. The position comes from the same resolver Frame used.
        (int drawX, int drawY) = ResolvePosition(offsetX, offsetY);
        var rect = new Rectangle(drawX, drawY, Width, Height);
        Raylib.DrawRectangleRoundedLines(rect, 0.08f, 8, 1.5f, Theme.Border);

        // The gold footer owns the bottom of the panel; text only gets the rest.
        int textH = Height - FooterH;

        if (Lines.Count == 0)
        {
            Raylib.DrawText("Print output appears here",
                drawX + PadX, drawY + (textH - TextSize) / 2, TextSize, Theme.PlaceholderText);
        }
        else
        {
            // Tail window: the newest lines win when the panel is full, so the value
            // the running script just printed is always the visible one at the bottom.
            int maxLines = Math.Max(0, (textH - PadY * 2) / (TextSize + LineSpacing));
            int start = Math.Max(0, Lines.Count - maxLines);
            for (int i = start; i < Lines.Count; i++)
            {
                int y = drawY + PadY + (i - start) * (TextSize + LineSpacing);
                Raylib.DrawText(Lines[i], drawX + PadX, y, TextSize,
                    _errorLines.Contains(i) ? Theme.Badge : Theme.TextDark);
            }
        }

        DrawGoldFooter(drawX, drawY);
    }

    /// <summary>The "x/n gold collected" row pinned under the printed text: divider,
    /// a gold swatch like the map's cell, the counts, then the label.</summary>
    void DrawGoldFooter(int drawX, int drawY)
    {
        int fy = drawY + Height - FooterH;
        Raylib.DrawLine(drawX + PadX, fy, drawX + Width - PadX, fy, Theme.Border);

        int swatch = 9;
        Raylib.DrawRectangle(drawX + PadX, fy + (FooterH - swatch) / 2, swatch, swatch, Theme.MapGold);

        int tx = drawX + PadX + swatch + 6;
        int textY = fy + (FooterH - FooterTextSize) / 2;
        string counts = $"{GoldMined}/{TotalGold}";
        Raylib.DrawText(counts, tx, textY, FooterTextSize, Theme.TextDark);
        Raylib.DrawText("gold collected", tx + Raylib.MeasureText(counts, FooterTextSize) + 6,
            textY, FooterTextSize, Theme.PlaceholderText);
    }
}
