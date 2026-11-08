using Raylib_CsLo;

namespace T2G_Cookie.Gui;

static class Theme
{
    // ---- toolbox layout ----
    public const int ToolboxWidth = 300;
    public const int CategoryStripWidth = 76;
    public const int PalettePadding = 16;
    public const int PaletteGap = 12;
    public const int CategoryTitleHeight = 28;

    // blocks
    public const int FontSize = 16;
    public const int HeaderHeight = 34;
    public const int PadX = 14;
    public const int MinBlockWidth = 96;
    // Reporters are pills sized to their content; a shared 96px minimum made a tiny
    // number literal as wide as a statement and bloated every operator slot.
    public const int MinReporterWidth = 52;
    public const int Indent = 22;
    public const int FooterHeight = 22;
    public const int EmptySlotHeight = 26;
    public const int HatExtra = 16;
    public const float SnapRadius = 30f;

    // slots / fields
    public const int MinSlotWidth = 44;

    // c-block else mouth
    public const int ElseBarHeight = 26;

    // interaction. Below this a press is a click (the block does not move at all),
    // at or past it the press becomes a drag.
    public const int DragThreshold = 6;
    public const int EntryHeight = 24;

    // slots / placeholders
    public const int PlaceholderFontSize = 12;

    // start button & badges
    public const int StartButtonW = 72;
    public const int StartButtonH = 28;
    public const int BadgeRadius = 7;

    // the save button shares Start's metrics but stays grey: it reports no state
    public const int SaveButtonW = StartButtonW;
    public const int SaveButtonH = StartButtonH;
    public const int ButtonGap = 8;

    // print-output field under the bar: status line (3 + 12px text) plus a gap
    public const int OutputTopGap = 21;
    public const int OutputWPercent = 20;
    public const int OutputHPercent = 10;

    // map panel under the print-output field
    public const int MapTopGap = 8;

    // robot control stack under the map: checkbox frame, buttons frame, gaps
    public const int RobotPanelGap = 8;
    public const int RobotToggleH = 34;

    // two rows: TURN LEFT / TURN RIGHT / FORWARD, then the full-width MINE button
    public const int RobotButtonsH = 70;
    public const int RobotButtonGap = 6;

    /// <summary>Everything the panels below the map need; the map clamps its height
    /// so the stack always fits on screen.</summary>
    public const int RobotStackBelow =
        RobotPanelGap + RobotToggleH + RobotPanelGap + RobotButtonsH + RobotPanelGap;

    // colors
    public static readonly Color WorkspaceBg = Rgb(240, 244, 255);
    public static readonly Color ToolboxBg = Rgb(255, 255, 255);
    public static readonly Color StripBg = Rgb(249, 249, 249);
    public static readonly Color Border = Rgb(217, 221, 231);
    public static readonly Color TextDark = Rgb(87, 94, 117);
    public static readonly Color SnapEdge = Rgb(90, 100, 130);
    public static readonly Color SlotFill = new Color(255, 255, 255, 175);
    public static readonly Color Badge = Rgb(230, 60, 60);
    public static readonly Color StartGreen = Rgb(57, 181, 74);
    public static readonly Color StartWarn = Rgb(232, 163, 61);
    public static readonly Color StartDisabled = Rgb(190, 196, 210);
    public static readonly Color SaveGray = Rgb(143, 150, 165);
    public static readonly Color PlaceholderText = Rgb(150, 158, 178);
    public static readonly Color ScrollTrack = Rgb(235, 238, 245);
    public static readonly Color ScrollThumb = Rgb(190, 197, 214);
    public static readonly Color PopupBg = Rgb(255, 255, 255);
    public static readonly Color PopupHover = Rgb(224, 232, 255);
    public static readonly Color PopupBorder = Rgb(170, 176, 196);

    // map cells
    public static readonly Color MapGridLine = Rgb(225, 228, 238);
    public static readonly Color MapEmptyDot = Rgb(150, 158, 178);
    public static readonly Color MapGold = Rgb(255, 200, 39);
    public static readonly Color MapWall = Rgb(226, 61, 61);
    public static readonly Color MapRobot = Rgb(52, 120, 246);

    public static Color Rgb(int r, int g, int b) => new Color((byte)r, (byte)g, (byte)b, (byte)255);

    /// <summary>Mix a colour toward white; used for hover states so blocks keep their identity.</summary>
    public static Color Lighten(Color c, float amount)
    {
        float t = Math.Clamp(amount, 0f, 1f);
        return new Color(
            (byte)(c.r + (255 - c.r) * t),
            (byte)(c.g + (255 - c.g) * t),
            (byte)(c.b + (255 - c.b) * t),
            c.a);
    }

    /// <summary>Mix a colour toward black; used for pressed states.</summary>
    public static Color Darken(Color c, float amount)
    {
        float t = 1f - Math.Clamp(amount, 0f, 1f);
        return new Color((byte)(c.r * t), (byte)(c.g * t), (byte)(c.b * t), c.a);
    }
}
