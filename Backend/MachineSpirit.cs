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

    public MachineSpirit(VMInstance vMInstance, string logPath, MapHandler map)
    {
        vMInstance.Implementor = this;
        logger = new HandleOutputFile(logPath);
        this.mapHandler = map;
        var machinePos = mapHandler.IdentifyRobot(out Direction? direction);
        if (!machinePos.HasValue)
            throw new Exception("Robot has no position!");
        
        this.position  = machinePos.Value;
        this.direction = direction!.Value;
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

    public VMInstance.ExecutionResult turn(ValueUnion amount)
    {
        if (amount.type != ValueUnion.ValueType.Number)
        {
            return VMInstance.ExecutionResult.ERROR(VMInstance.ExecutionResult.ErrorKind.E_BAD_ARG);
        }

        for (int i = 0; i < amount.value % 4; i++) direction = direction.TurnLeft();   

        return VMInstance.ExecutionResult.OK;
    }

    public VMInstance.ExecutionResult mine()
    {
        if (mapHandler.Mine(position))
        {
            GoldMined++;
            return VMInstance.ExecutionResult.OK;
        }

        return VMInstance.ExecutionResult.ERROR(VMInstance.ExecutionResult.ErrorKind.E_NO_GOLD);
    }

    public VMInstance.ExecutionResult move(ValueUnion amount)
    {
        if (amount.type != ValueUnion.ValueType.Number)
        {
            return VMInstance.ExecutionResult.ERROR(VMInstance.ExecutionResult.ErrorKind.E_BAD_ARG);
        }

        for (int i = 0; i < amount.value; i++)
        {
            var element = mapHandler.GetForward(direction, position);
            if (element == MapHandler.MapElement.Wall)
            {
                return VMInstance.ExecutionResult.ERROR(VMInstance.ExecutionResult.ErrorKind.E_BEDROCK_HIT);
            }

            position = new Vector2Int(position.X + direction.ModX, position.Y + direction.ModY);
        }

        return VMInstance.ExecutionResult.OK;
    }

    public VMInstance.ExecutionResult isThere(ValueUnion query, out bool result)
    {
        var element = mapHandler.GetForward(direction, position);

        if (query.type != ValueUnion.ValueType.Number)
        {
            result = false;        
            return VMInstance.ExecutionResult.ERROR(VMInstance.ExecutionResult.ErrorKind.E_BAD_ARG);    
        }

        if ((long) element == query.value)
        {
            result = true;
        }else
            result = false;

        return VMInstance.ExecutionResult.OK;
    }
}