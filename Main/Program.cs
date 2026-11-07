using Shared;
using T2G_Cookie.Gui;

namespace T2G_Cookie.Main;

/// <summary>
/// The application entry point. Optionally loads a map/code file from the args, then
/// builds the block editor and runs it (this bootstrap used to live in Gui/Program.cs,
/// but Main is the one executable in the solution).
/// </summary>
internal class Program
{
    static void Main(string[] args)
    {
        if (TryReadFile(args, out string? fileName))
        {
            HandleInputFile handler = new(fileName!);
            Console.WriteLine($"Code blocks: {handler.Code.Count}");
            Console.WriteLine($"Map: {(handler.Map != null ? $"{handler.Map.GetLength(0)}x{handler.Map.GetLength(1)}" : "null")}");
        }

        App app = new();

        Frame root = new(Pos.Start, Pos.Start, 100, 100) { DrawBackground = false };

        var categories = BlockLibrary.CreateCategories();
        Toolbox toolbox = new(Pos.Start, Pos.Start, 0, 100, categories);
        root.AddChild(toolbox);

        // 100% width: the workspace starts at the toolbox's right edge and Update() clamps
        // it to the window, so it always fills everything to the right of the toolbox.
        Workspace workspace = new(Pos.Right(toolbox), Pos.Start, 100, 100);
        root.AddChild(workspace);

        // The palette greys out the Start hat while the workspace already has one.
        toolbox.StartAvailable = () => !workspace.HasStartBlock;

        app.Toolbox = toolbox;
        app.Workspace = workspace;

        app.Run();
    }

    /// <summary>Resolves the input file from the args (`-q` takes the next argument as
    /// the path). False when nothing usable was passed; the editor still starts.</summary>
    static bool TryReadFile(string[] args, out string? fileName)
    {
        fileName = null;
        if (args.Length == 0) return false;

        int pathIndex = args[0].Equals("-q") ? 1 : 0;
        if (args.Length <= pathIndex)
        {
            Console.WriteLine("Nincs fájl megadva.");
            return false;
        }

        fileName = args[pathIndex];
        Console.WriteLine(fileName);
        return true;
    }
}
