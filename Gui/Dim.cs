using Raylib_CsLo;

namespace T2G_Cookie.Gui;

public static class Dim
{
    public static int GetWSize(int percent) => Raylib.GetScreenWidth() * percent / 100;

    public static int GetHSize(int percent) => Raylib.GetScreenHeight() * percent / 100;
}
