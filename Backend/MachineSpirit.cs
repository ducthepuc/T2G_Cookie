using Shared;
using T2G_Cookie.Backend.VM;
using T2G_Cookie.Shared;
using T2G_Cookie.Shared.Values;

namespace T2G_Cookie.Backend;

public class MachineSpirit : VMInstance.IVMAPI
{
    public Vector2Int position;
    public HandleOutputFile logger;
    public Direction direction;
    public readonly MapHandler mapHandler;
    public int GoldMined {get; private set;}

    public event Action<Vector2Int>? CellEntered;

    public event Action<Vector2Int>? CellSensed;


    public MachineSpirit(string logPath, MapHandler map)
    {
        logger = new HandleOutputFile(logPath);
        this.mapHandler = map;
        var machinePos = mapHandler.IdentifyRobot(out Direction? direction);
        if (!machinePos.HasValue)
            throw new Exception("Robot has no position!");
        
        this.position  = machinePos.Value;
        this.direction = direction!.Value;
    }

    public void Setup(VMInstance vMInstance)
    {
        vMInstance.Implementor = this;   
    }

    ~MachineSpirit()
    {
        logger.Dispose();
    }

    public void log(BytecodeInstruction instruction, VMInstance.ExecutionResult result, ValueUnion? param, ValueUnion? ret)
    {
        string line = instruction.metadata.line + instruction.format(param, ret);
        if (!result.Ok) line += " " + result.Error.ErrorKind;
        logger.Log(line);
    }
    public void logEor(bool exceeded)
    {
        logger.Finish(exceeded);
    }

    public VMInstance.ExecutionResult turn(ValueUnion amount, int line)
    {
        if (amount.type != ValueUnion.ValueType.Number)
        {
            return VMInstance.ExecutionResult.ERROR(VMInstance.ExecutionResult.ErrorKind.E_BAD_ARG, line);
        }

        for (int i = 0; i < amount.value % 4; i++) direction = direction.TurnLeft();   

        return VMInstance.ExecutionResult.OK;
    }

    public VMInstance.ExecutionResult mine(int line)
    {
        if (mapHandler.Mine(position))
        {
            GoldMined++;
            return VMInstance.ExecutionResult.OK;
        }

        return VMInstance.ExecutionResult.ERROR(VMInstance.ExecutionResult.ErrorKind.E_NO_GOLD, line);
    }

    public VMInstance.ExecutionResult move(ValueUnion amount, int line)
    {
        if (amount.type != ValueUnion.ValueType.Number)
        {
            return VMInstance.ExecutionResult.ERROR(VMInstance.ExecutionResult.ErrorKind.E_BAD_ARG, line);
        }

        for (int i = 0; i < amount.value; i++)
        {
            var element = mapHandler.GetForward(direction, position);
            if (element == MapHandler.MapElement.Wall)
            {
                return VMInstance.ExecutionResult.ERROR(VMInstance.ExecutionResult.ErrorKind.E_BEDROCK_HIT, line);
            }

            position = new Vector2Int(position.X + direction.ModX, position.Y + direction.ModY);
            CellEntered?.Invoke(position);
        }

        return VMInstance.ExecutionResult.OK;
    }

    public VMInstance.ExecutionResult isThere(ValueUnion query, out bool result, int line)
    {
        var element = mapHandler.GetForward(direction, position);

        if (query.type != ValueUnion.ValueType.Number)
        {
            result = false;        
            return VMInstance.ExecutionResult.ERROR(VMInstance.ExecutionResult.ErrorKind.E_BAD_ARG, line);    
        }

        CellSensed?.Invoke(new Vector2Int(position.X + direction.ModX, position.Y + direction.ModY));

        if ((long) element == query.value)
        {
            result = true;
        }else
            result = false;

        return VMInstance.ExecutionResult.OK;
    }
}