using Shared;
using T2G_Cookie.Backend.Parsing;
using T2G_Cookie.Gui;
using T2G_Cookie.Shared.Ast;

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
        HandleInputFile? input = null;
        if (TryReadFile(args, out string? fileName))
        {
            input = new HandleInputFile(fileName!);
            Console.WriteLine($"Code lines: {input.Code.Count}");
            Console.WriteLine($"Map: {(input.Map != null ? $"{input.Map.GetLength(0)}x{input.Map.GetLength(1)}" : "null")}");

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

            // The Save button writes the rendered script back through the same handler,
            // in the format the reader takes (header, map rows, code).
            workspace.SaveScript = input.Write;
        }

        app.Run();
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
