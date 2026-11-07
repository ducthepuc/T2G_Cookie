using Raylib_CsLo;

namespace T2G_Cookie.Gui;

/// <summary>
/// The panel under the save/start bar where Print output lands: white, rounded, with
/// a border and a hint until the first line arrives. Lines are plain text drawn
/// top-down; nothing scrolls yet, so the oldest stay visible and the list simply
/// stops drawing at the bottom edge.
/// </summary>
sealed class OutputField : Frame
{
    public const int PadX = 10;
    public const int PadY = 8;
    public const int LineSpacing = 4;
    public const int TextSize = 14;

    /// <summary>The print output, oldest first; whatever runs Print appends here.</summary>
    public List<string> Lines { get; } = new();

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

        if (Lines.Count == 0)
        {
            Raylib.DrawText("Print output appears here",
                drawX + PadX, drawY + (Height - TextSize) / 2, TextSize, Theme.PlaceholderText);
            return;
        }

        int maxLines = Math.Max(0, (Height - PadY * 2) / (TextSize + LineSpacing));
        for (int i = 0; i < Lines.Count && i < maxLines; i++)
        {
            int y = drawY + PadY + i * (TextSize + LineSpacing);
            Raylib.DrawText(Lines[i], drawX + PadX, y, TextSize, Theme.TextDark);
        }
    }
}
