using Raylib_CsLo;
using T2G_Cookie.Backend;
using T2G_Cookie.Backend.VM;
using T2G_Cookie.Gui;
using T2G_Cookie.Shared;
using T2G_Cookie.Shared.Values;

namespace T2G_Cookie.Main;

/// <summary>
/// The bridge between the block editor and the Backend VM: compiles the workspace's
/// converted script to bytecode and hands back a <see cref="RunSession"/> the app loop
/// advances, one source line at a time.
/// </summary>
static class Runner
{
    /// <summary>MachineSpirit's execution log: the output file named on the command
    /// line, or this default when none was given.</summary>
    public static string LogPath { get; set; } = "output.log";

    /// <summary>Creates the session for a validated script, or null (with the reason on
    /// the console) when there is nothing that can run.</summary>
    public static RunSession? Begin(Workspace ws, AstResult validation)
    {
        if (validation.Errors.Count > 0)
        {
            Console.WriteLine("run: fix the problems first");
            return null;
        }
        if (validation.Scripts.Length == 0)
        {
            Console.WriteLine("run: nothing to run (add a Start block)");
            return null;
        }

        MapHandler? map = ws.MapPanel.Map;
        if (map == null || map.IdentifyRobot(out _) == null)
        {
            Console.WriteLine("run: load a map with a robot first");
            return null;
        }

        try
        {
            return new RunSession(ws, map, validation);
        }
        catch (Exception ex)
        {
            ws.StopRun();
            ws.HighlightLine(null);
            Console.WriteLine("run: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Runs the script to completion without waiting for frames. <paramref name="present"/>
    /// is invoked after every processed line. The self-test drives the runner this way.
    /// </summary>
    public static void Run(Workspace ws, AstResult validation, Action? present = null)
    {
        RunSession? session = Begin(ws, validation);
        if (session == null) return;

        while (!session.Done)
        {
            session.Advance();
            while (session.Walking) session.Advance();
            if (!session.Done) present?.Invoke();
        }
    }
}

/// <summary>
/// One program run on the VM. The app loop calls <see cref="Tick"/> every frame, so the
/// window stays alive during a run: Stop, the manual-control checkbox, Next and closing
/// the window are all ordinary input handled by the workspace.
/// </summary>
sealed class RunSession : IRunSession
{
    readonly Workspace _ws;
    readonly MapHandler _map;
    readonly MachineSpirit _spirit;
    readonly VMInstance _vm;
    readonly OutputApi _api;
    readonly string[] _names;

    // Cells a step already walked in the VM but the screen has not shown yet.
    readonly Queue<Vector2Int> _trail = new();

    VMInstance.ExecutionResult? _outcome;
    double _nextAt;

    public bool Done { get; private set; }

    /// <summary>True while a multi-cell step is still being animated.</summary>
    public bool Walking => _trail.Count > 0;

    public RunSession(Workspace ws, MapHandler map, AstResult validation)
    {
        _ws = ws;
        _map = map;

        BytecodeHeader header = BytecodeEmitter.CreateEmitter(validation.Scripts[0]).Generate();
        _names = header.VariableNames;
        _vm = VMInstance.CreateFromBytecode(header);

        _spirit = new MachineSpirit(Runner.LogPath, map);
        _spirit.Setup(_vm);
        _spirit.CellEntered += _trail.Enqueue;
        _spirit.CellSensed += map.MarkExplored;

        // Wrap the spirit: the VM clears the print parameter before OnInstruction
        // fires, so the wrapper is the only place it survives.
        _api = new OutputApi(_spirit);
        _vm.Implementor = _api;
        _vm.OnInstruction += OnLine;

        if (_vm.Prepare(out VMInstance.ExecutionResult result)) _outcome = result;
        if (ws.Tracing) ws.HighlightLine(_vm.NextLine);
    }

    bool OnLine(BytecodeInstruction instruction)
    {
        _ws.HighlightLine(instruction.metadata.line >= 0 ? instruction.metadata.line : null);

        if (instruction.instructionKind == BytecodeInstruction.InstructionKind.Print &&
            _api.TakePrint() is { } printed)
            _ws.AppendPrint(printed);

        return true;
    }

    /// <summary>One frame of the run: waits out the step delay and, while tracing, the
    /// Next request, then advances.</summary>
    public void Tick()
    {
        if (Done) return;

        double now = Raylib.GetTime();
        if (now < _nextAt) return;

        bool waitsForUser = _ws.Tracing && !Walking && _outcome == null;
        if (waitsForUser && !_ws.TakeStepRequest()) return;

        Advance();
        _nextAt = now + _ws.StepDelaySeconds;
    }

    /// <summary>Does the next visible thing: walks one cell, closes a finished run, or
    /// processes one source line.</summary>
    public void Advance()
    {
        if (Done) return;

        if (_trail.TryDequeue(out Vector2Int cell))
        {
            _map.MarkExplored(cell);
            _ws.SyncRunState(cell, _spirit.direction);
            return;
        }

        if (_outcome is { } outcome)
        {
            Finish(outcome);
            return;
        }

        try
        {
            if (_vm.StepLine(out VMInstance.ExecutionResult result)) _outcome = result;
        }
        catch (Exception ex)
        {
            Done = true;
            _ws.StopRun();
            _ws.HighlightLine(null);
            Console.WriteLine("run: " + ex.Message);
            return;
        }

        if (_ws.Tracing && _outcome == null) _ws.HighlightLine(_vm.NextLine);
        ShowVariables();
        if (!Walking) _ws.SyncRunState(_spirit.position, _spirit.direction);
    }

    void Finish(VMInstance.ExecutionResult outcome)
    {
        Done = true;
        _ws.SyncRunState(_spirit.position, _spirit.direction);

        if (outcome.Ok)
        {
            _ws.HighlightLine(null);
            Console.WriteLine($"run: finished, {_spirit.GoldMined} gold mined");
            return;
        }

        var error = outcome.Error;
        bool limit = error.ErrorKind == VMInstance.ExecutionResult.ErrorKind.INSTRUCTION_COUNT_LIMIT_EXCEEDED;
        _ws.AppendError(error.ErrorKind.ToString(), limit ? null : error.Line);
        _ws.HighlightLine(limit ? null : error.Line);
        Console.WriteLine($"run: stopped with {error.ErrorKind}, {_spirit.GoldMined} gold mined");
    }

    void ShowVariables()
    {
        var lines = new List<string>();
        for (int i = 0; i < _names.Length; i++)
            if (OutputApi.Format(_vm.Variables[i]) is { } value)
                lines.Add($"{_names[i]} = {value}");
        _ws.ShowVariables(lines);
    }

    /// <summary>Ends a run that is still going: the log gets its end marker and the
    /// map shows where the program really is.</summary>
    public void Abort()
    {
        if (Done) return;
        Done = true;

        while (_trail.TryDequeue(out Vector2Int cell)) _map.MarkExplored(cell);
        _ws.SyncRunState(_spirit.position, _spirit.direction);
        _api.logEor(false);
    }
}

/// <summary>
/// MachineSpirit, wrapped for the run: every API call is delegated unchanged, but the
/// wrapper remembers the parameter the VM handed to log for the statement it just
/// executed - for a print that is the resolved print value.
/// </summary>
sealed class OutputApi : VMInstance.IVMAPI
{
    readonly VMInstance.IVMAPI _spirit;
    ValueUnion? _logParam;
    bool _logOk;

    public OutputApi(VMInstance.IVMAPI spirit) => _spirit = spirit;

    public void log(BytecodeInstruction instruction, VMInstance.ExecutionResult result,
                    ValueUnion? param, ValueUnion? ret)
    {
        _logParam = param;
        _logOk = result.Ok;
        _spirit.log(instruction, result, param, ret);
    }

    public void logEor(bool exceeded) => _spirit.logEor(exceeded);
    public VMInstance.ExecutionResult move(ValueUnion amount, int line) => _spirit.move(amount, line);
    public VMInstance.ExecutionResult turn(ValueUnion amount, int line) => _spirit.turn(amount, line);
    public VMInstance.ExecutionResult mine(int line) => _spirit.mine(line);
    public VMInstance.ExecutionResult isThere(ValueUnion query, out bool result, int line)
        => _spirit.isThere(query, out result, line);

    /// <summary>The value the just-executed statement printed, or null when it was
    /// not a successful print with a printable (number/boolean) argument.</summary>
    public string? TakePrint()
    {
        string? text = _logOk && _logParam is { } v ? Format(v) : null;
        _logParam = null;
        _logOk = false;
        return text;
    }

    /// <summary>Same rendering the VM itself uses when it writes the value to the
    /// console; null for a value that is not a number or boolean yet.</summary>
    public static string? Format(ValueUnion v) => v.type switch
    {
        ValueUnion.ValueType.Number => v.value.ToString(),
        ValueUnion.ValueType.Boolean => v.boolean ? "true" : "false",
        _ => null
    };
}
