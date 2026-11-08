using Shared;
using T2G_Cookie.Backend.Parsing;
using T2G_Cookie.Gui;
using T2G_Cookie.Shared;
using T2G_Cookie.Shared.Ast;

namespace T2G_Cookie.Main;

/// <summary>
/// The application entry point. Optionally loads a map/code file from the args, then
/// builds the block editor and runs it (this bootstrap used to live in Gui/Program.cs,
/// but Main is the one executable in the solution).
/// </summary>
internal class Program
{
    /// <summary>The biggest map the game accepts, per dimension.</summary>
    const int MaxMapSize = 15;

    static int Main(string[] args)
    {
        HandleInputFile? input = null;
        MapHandler? map = null;
        if (TryReadFile(args, out string? fileName))
        {
            input = new HandleInputFile(fileName!);
            Console.WriteLine($"Code lines: {input.Code.Count}");
            Console.WriteLine($"Map: {(input.Map != null ? $"{input.Map.GetLength(0)}x{input.Map.GetLength(1)}" : "null")}");

            char[,]? mapData = input.Map;
            if (mapData != null &&
                (mapData.GetLength(0) > MaxMapSize || mapData.GetLength(1) > MaxMapSize))
            {
                Console.WriteLine(
                    $"the map is {mapData.GetLength(0)}x{mapData.GetLength(1)}, " +
                    $"the biggest accepted size is {MaxMapSize}x{MaxMapSize}");
                Console.WriteLine("the editor was not started");
                return 1;
            }

            // The same structure the game world drives: it parses the matrix and the
            // GUI map panel renders what it holds.
            if (mapData != null)
            {
                try
                {
                    map = new MapHandler(mapData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"could not read the map: {ex.Message}");
                    Console.WriteLine("the editor was not started");
                    return 1;
                }
            }

            using var logger = new HandleOutputFile("output.log");
            logger.Log("Test line 1");
            logger.Log("Test line 2");
            logger.Finish(false);
            Console.WriteLine("Log test written to output.log");
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

        // The window is open now, so the loader can measure text while it builds blocks.
        if (input != null)
        {
            LoadScript(workspace, input.Code);

            // The map panel shows the handler the same file produced.
            workspace.LoadMap(map);

            // The Save button writes the rendered script back through the same handler,
            // in the format the reader takes (header, map rows, code).
            workspace.SaveScript = input.Write;
        }

        app.Run();
        return 0;
    }

    /// <summary>Parses the loaded lines as MineCode and rebuilds them as blocks in the
    /// workspace; a program the parser can't read is reported, never thrown.</summary>
    static void LoadScript(Workspace workspace, List<string> code)
    {
        if (code.Count == 0) return;

        try
        {
            Body script = MineCode.ParseCode(string.Join("\n", code));
            foreach (string problem in AstLoader.Load(workspace, [script]))
                Console.WriteLine("load: " + problem);
        }
        catch (Exception ex)
        {
            Console.WriteLine("could not load the script: " + ex.Message);
        }
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
