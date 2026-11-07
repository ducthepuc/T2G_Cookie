using Raylib_CsLo;

namespace T2G_Cookie.Gui;

internal class Program
{
    static void Main()
    {
        App app = new();

        Frame root = new(Pos.Start, Pos.Start, 100, 100) { DrawBackground = false };

        var categories = BlockLibrary.CreateCategories();
        Toolbox toolbox = new(Pos.Start, Pos.Start, 0, 100, categories);
        root.AddChild(toolbox);

        Workspace workspace = new(Pos.Right(toolbox), Pos.Start, 75, 100);
        root.AddChild(workspace);

        app.Toolbox = toolbox;
        app.Workspace = workspace;

        app.Run();
    }
}
