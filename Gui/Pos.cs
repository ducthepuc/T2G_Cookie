using Raylib_CsLo;

namespace T2G_Cookie.Gui;

public static class Pos
{
    public static Func<int> Start => () => 0;

    public static Func<int> Center() => () => Raylib.GetScreenWidth() / 2;

    public static Func<int> Middle() => () => Raylib.GetScreenHeight() / 2;

    public static Func<int> Left(Element el) => () => el.X;

    public static Func<int> Right(Element el) => () => el.X + el.Width;

    public static Func<int> Top(Element el) => () => el.Y;

    public static Func<int> Bottom(Element el) => () => el.Y + el.Height;

    public static Func<int> AnchorHStart() => () => 0;

    public static Func<int> AnchorHEnd() => () => Raylib.GetScreenHeight();

    public static Func<int> AnchorWStart() => () => 0;

    public static Func<int> AnchorWEnd() => () => Raylib.GetScreenWidth();
}
