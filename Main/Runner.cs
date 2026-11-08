using System.Threading;
using Raylib_CsLo;
using T2G_Cookie.Backend;
using T2G_Cookie.Backend.VM;
using T2G_Cookie.Gui;
using T2G_Cookie.Shared;

namespace T2G_Cookie.Main;

/// <summary>
/// The bridge between the block editor and the Backend VM: compiles the workspace's
/// converted script to bytecode, runs it through MachineSpirit, and - on every
/// instruction the VM reports through OnInstruction - pushes the spirit's position
/// onto the map panel and moves the gold highlight to the block segment owning that
/// instruction's source line, presenting the frame so the step is visible.
/// The Backend stays untouched; everything it needs comes in through its own API.
/// </summary>
static class Runner
{
    /// <summary>MachineSpirit's execution log, next to the app's working directory.</summary>
    public const string LogPath = "output.log";

    /// <summary>How long one executed instruction stays on screen, so the robot
    /// movement and the highlight can actually be followed.</summary>
    public const int StepDelayMs = 60;

    /// <summary>
    /// Runs the script to completion. <paramref name="present"/> is invoked after
    /// every instruction, once the robot and the highlight are updated - the live
    /// window passes <see cref="PresentStep"/>; the self-test passes a recorder or
    /// nothing at all (there is no frame to swap there).
    /// </summary>
    public static void Run(Workspace ws, AstResult validation, Action? present = null)
    {
        if (validation.Errors.Count > 0)
        {
            Console.WriteLine("run: fix the problems first");
            return;
        }
        if (validation.Scripts.Length == 0)
        {
            Console.WriteLine("run: nothing to run (add a Start block)");
            return;
        }

        MapHandler? map = ws.MapPanel.Map;
        if (map == null || map.IdentifyRobot(out _) == null)
        {
            Console.WriteLine("run: load a map with a robot first");
            return;
        }

        OutputApi? api = null;
        try
        {
            BytecodeHeader header = BytecodeEmitter.CreateEmitter(validation.Scripts[0]).Generate();
            VMInstance vm = VMInstance.CreateFromBytecode(header);
            var spirit = new MachineSpirit(LogPath, map);
            spirit.Setup(vm); // makes the spirit the VM's API (move/turn/mine/isThere)

            // Wrap it: everything is still MachineSpirit's job, but the wrapper keeps
            // the value the VM resolved into log's parameter - it is the only place
            // it survives (the VM clears it before OnInstruction fires).
            api = new OutputApi(spirit);
            vm.Implementor = api;

            vm.OnInstruction += instruction =>
            {
                // Runs after the instruction: the map marker is the spirit now, and the
                // highlight jumps to the block segment the instruction's line belongs to.
                ws.SyncRunState(spirit.position, spirit.direction);
                ws.HighlightLine(instruction.metadata.line >= 0 ? instruction.metadata.line : null);

                // When the executed statement is a print, show what it printed: the
                // argument was resolved moments ago and handed to log. The line tells
                // the workspace whether it was a variable, so the name can go with it.
                if (instruction.instructionKind == BytecodeInstruction.InstructionKind.Print &&
                    api.TakePrint() is { } printed)
                    ws.AppendPrint(printed, instruction.metadata.line);

                present?.Invoke();

                // The Stop button, polled on the input the step's frame just delivered:
                // close the session (which restores the pre-start map) and break out of
                // RunCode - the VM has no pause, so an exception is the only way out.
                if (ws.StopRequested())
                {
                    ws.StopRun();
                    throw new RunAborted();
                }
                return true; // the VM ignores it today; "keep going" is the honest answer
            };

            VMInstance.ExecutionResult result = vm.RunCode(false);

            ws.SyncRunState(spirit.position, spirit.direction);
            ws.HighlightLine(null);
            Console.WriteLine(result.Ok
                ? $"run: finished, {spirit.GoldMined} gold mined"
                : $"run: stopped with {result.Error.ErrorKind}, {spirit.GoldMined} gold mined");
        }
        catch (RunAborted)
        {
            // Stop was clicked mid-run: the session is closed and the map restored
            // already - only the spirit's log still needs the end-of-run marker the
            // unwind skipped.
            if (api is { } finished) finished.logEor(false);
            Console.WriteLine("run: stopped");
        }
        catch (Exception ex)
        {
            ws.StopRun(); // close the session Start opened (restore is a no-op if nothing ran)
            ws.HighlightLine(null);
            Console.WriteLine("run: " + ex.Message);
        }
    }

    /// <summary>
    /// Presents the state an instruction just produced: compose the whole UI, swap it
    /// onto the screen, hold the step, then open a fresh frame for the app loop to
    /// continue where it left off (UpdateAll is mid-flight, so the clear here is the
    /// frame's own background clear).
    /// </summary>
    public static void PresentStep()
    {
        Element.DrawAll();
        Raylib.EndDrawing();
        Thread.Sleep(StepDelayMs);
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Raylib.WHITE);
    }
}

/// <summary>Thrown by the instruction handler when Stop was clicked: the only way
/// out of a RunCode in flight, since the VM has no pause and the Backend stays
/// untouched.</summary>
sealed class RunAborted : Exception { }

/// <summary>
/// MachineSpirit, wrapped for the run: every API call is delegated unchanged (the
/// spirit still owns the robot, the map and the execution log), but the wrapper
/// remembers the parameter the VM handed to log for the statement it just executed -
/// for a print that is the resolved print value, and it is the only place it
/// survives, because the VM clears it before OnInstruction fires.
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
    public VMInstance.ExecutionResult move(ValueUnion amount) => _spirit.move(amount);
    public VMInstance.ExecutionResult turn(ValueUnion amount) => _spirit.turn(amount);
    public VMInstance.ExecutionResult mine() => _spirit.mine();
    public VMInstance.ExecutionResult isThere(ValueUnion query, out bool result)
        => _spirit.isThere(query, out result);

    /// <summary>The value the just-executed statement printed, or null when it was
    /// not a successful print with a printable (number/boolean) argument.</summary>
    public string? TakePrint()
    {
        string? text = _logOk && _logParam is { } v ? Format(v) : null;
        _logParam = null;
        _logOk = false;
        return text;
    }

    /// <summary>Same rendering the VM itself uses when it writes the value to the console.</summary>
    static string? Format(ValueUnion v) => v.type switch
    {
        ValueUnion.ValueType.Number => v.value.ToString(),
        ValueUnion.ValueType.Boolean => v.boolean ? "true" : "false",
        _ => null
    };
}
