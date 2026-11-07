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
    public const int Indent = 22;
    public const int FooterHeight = 22;
    public const int EmptySlotHeight = 26;
    public const int HatExtra = 16;
    public const float SnapRadius = 30f;

    // colors
    public static readonly Color WorkspaceBg = Rgb(240, 244, 255);
    public static readonly Color ToolboxBg = Rgb(255, 255, 255);
    public static readonly Color StripBg = Rgb(249, 249, 249);
    public static readonly Color Border = Rgb(217, 221, 231);
    public static readonly Color TextDark = Rgb(87, 94, 117);
    public static readonly Color SnapEdge = Rgb(90, 100, 130);

    public static Color Rgb(int r, int g, int b) => new Color((byte)r, (byte)g, (byte)b, (byte)255);
}
