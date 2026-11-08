using Shared;
using T2G_Cookie.Backend;
using T2G_Cookie.Backend.Parsing;
using T2G_Cookie.Backend.VM;
using T2G_Cookie.Gui;
using T2G_Cookie.Shared;
using T2G_Cookie.Shared.Ast;

namespace T2G_Cookie.Main;

/// <summary>
/// The application entry point: `robot input output` opens the editor on that file,
/// `robot -q input output` runs the program without a window and exits.
/// </summary>
internal class Program
{
    /// <summary>The biggest map the game accepts, per dimension.</summary>
    const int MaxMapSize = 15;

    static int Main(string[] args)
    {
        bool quiet = args.Length > 0 && args[0] == "-q";
        string[] paths = quiet ? args[1..] : args;

        if (quiet)
        {
            if (paths.Length < 2)
            {
                Console.Error.WriteLine("usage: robot -q <input file> <output file>");
                return 1;
            }
            return RunQuiet(paths[0], paths[1]);
        }

        HandleInputFile? input = null;
        MapHandler? map = null;
        if (paths.Length > 0)
        {
            try
            {
                input = new HandleInputFile(paths[0]);
                char[,] mapData = input.Map!;
                if (mapData.GetLength(0) > MaxMapSize || mapData.GetLength(1) > MaxMapSize)
                    throw new Exception($"the biggest accepted size is {MaxMapSize}x{MaxMapSize}");
                map = new MapHandler(mapData);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"could not read {paths[0]}: {ex.Message}");
                return 1;
            }
        }
        if (paths.Length > 1) Runner.LogPath = paths[1];

        App app = new();

        Frame root = new(Pos.Start, Pos.Start, 100, 100) { DrawBackground = false };

        var categories = BlockLibrary.CreateCategories();
        Toolbox toolbox = new(Pos.Start, Pos.Start, 0, 100, categories);
        root.AddChild(toolbox);

        // 100% width: the workspace starts at the toolbox's right edge and Update() clamps
        // it to the window, so it always fills everything to the right of the toolbox.
        Workspace workspace = new(Pos.Right(toolbox), Pos.Start, 100, 100);
        root.AddChild(workspace);

        // The palette greys out the Start hat while the workspace already has one, and
        // hands out no blocks while a program is running.
        toolbox.StartAvailable = () => !workspace.HasStartBlock;
        toolbox.Locked = () => workspace.RunActive;

        app.Toolbox = toolbox;
        app.Workspace = workspace;

        // Start hands the converted script to the VM; the workspace then advances the
        // session once per frame.
        workspace.RunProgram = result => workspace.AttachSession(Runner.Begin(workspace, result));

        // The window is open now, so the loader can measure text while it builds blocks.
        LoadScript(workspace, input?.Code ?? []);
        workspace.LoadMap(map);

        // The Save button writes the rendered script back through the same handler,
        // in the format the reader takes (header, map rows, code).
        if (input != null) workspace.SaveScript = input.Write;

        app.Run();
        return 0;
    }

    /// <summary>The -q mode: reads the input, runs the program, writes the log, exits.</summary>
    static int RunQuiet(string inputPath, string outputPath)
    {
        try
        {
            var input = new HandleInputFile(inputPath);
            Body ast = MineCode.ParseCode(string.Join('\n', input.Code));
            BytecodeHeader header = BytecodeEmitter.CreateEmitter(ast).Generate();
            VMInstance vm = VMInstance.CreateFromBytecode(header);
            var spirit = new MachineSpirit(outputPath, new MapHandler(input.Map!));
            spirit.Setup(vm);
            vm.RunCode(false);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{inputPath}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Parses the loaded lines as MineCode and rebuilds them as blocks in the
    /// workspace. An empty program still gets its Start block, so it can be run and
    /// built on; a program the parser can't read is reported, never thrown.</summary>
    static void LoadScript(Workspace workspace, List<string> code)
    {
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
}
