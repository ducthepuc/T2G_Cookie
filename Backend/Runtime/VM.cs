namespace T2G_Cookie.Backend.VM;

public class VMInstance
{
    public interface IVMAPI
    {
        void log(BytecodeInstruction instruction, ExecutionResult result, ValueUnion? param, ValueUnion? ret);
        void logEor(bool exceeded);

        // API
        ExecutionResult move(ValueUnion amount, int line);
        ExecutionResult turn(ValueUnion amount, int line);
        ExecutionResult mine(int line);
        ExecutionResult isThere(ValueUnion query, out bool result, int line);
    }

    int programCounter;
    int instructionCounter; // counts non injected instructions.
    const int InstructionLimit = 1000;
    bool shouldRun;
    bool ended;
    ExecutionResult outcome;
    BytecodeInstruction[] instructions;
    readonly BytecodeInstruction[] copyInstructions;
    readonly Dictionary<int, BytecodeInstruction[]> bodies;
    public Stack<ValueUnion> executionStack = new();
    public ValueUnion[] Variables;

    BytecodeInstruction? lineApi;
    ValueUnion? lineParam;
    ValueUnion? lineRet;

    IVMAPI? implementor;
    bool isReady = false;

    public delegate bool InstructionRan(BytecodeInstruction instruction);
    public event InstructionRan OnInstruction;

    public IVMAPI Implementor
    {
        get
        {
            if (isReady) return implementor!;   
            throw new Exception("IT ISN'T READY!");
        }

        set
        {
            isReady = true;
            implementor = value;
        }
    }


    public struct ExecutionResult
    {
        public enum ErrorKind
        {
            E_TYPE_MISMATCH,
            E_UNINITIALIZED,
            E_NON_BOOL_CONDITION,
            E_BAD_OPERAND,
            E_BEDROCK_HIT,
            E_NO_GOLD,
            E_BAD_ARG,
            INSTRUCTION_COUNT_LIMIT_EXCEEDED
        }

            
        public struct ErrorObj
        {
            public ErrorKind ErrorKind;
            public int Line;
        }

        public bool Ok;
        public ErrorObj Error;

        public readonly static ExecutionResult OK = new() { Ok = true };
        public static ExecutionResult ERROR(ErrorKind errorKind, int line) => new() { Error = new ErrorObj { ErrorKind = errorKind, Line = line}};
    }

    VMInstance(BytecodeInstruction[] instructions, Dictionary<int, BytecodeInstruction[]> bodies, int variableCount)
    {
        this.instructions = instructions;
        this.bodies = bodies;
        copyInstructions = instructions;
        Variables = new ValueUnion[variableCount];
        shouldRun = true;
        programCounter = 0;
    }

    public static VMInstance CreateFromBytecode(BytecodeHeader header)
    {
        return new VMInstance(header.Main, header.Bodies, header.variableCount);
    }

    public delegate bool Mine();
    public void Setup()
    {
        
    }

    // public static VMInstance CreateFromAST()
    // {
        
    // }

    public ExecutionResult RunCode(bool pause)
    {
        if (!isReady)
            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_UNINITIALIZED, 0);

        ExecutionResult result;
        while (!StepLine(out result)) { }
        return result;
    }

    public int? NextLine =>
        !ended && shouldRun && programCounter < instructions.Length && instructions[programCounter].metadata.line >= 0
            ? instructions[programCounter].metadata.line
            : null;

    public bool Prepare(out ExecutionResult result) => ReachedEnd(out result);

    public bool StepLine(out ExecutionResult result)
    {
        if (ReachedEnd(out result)) return true;

        BytecodeInstruction executed;
        ExecutionResult lineResult;
        do
        {
            ref var instruction = ref FetchDecode();
            lineResult = Execute(ref instruction);
            executed = instruction;
        } while (executed.metadata.injected && lineResult.Ok);

        Implementor.log(lineApi ?? executed, lineResult, lineParam, lineRet);
        lineApi = null;
        lineParam = null;
        lineRet = null;
        instructionCounter++;

        OnInstruction?.Invoke(executed);

        if (!lineResult.Ok)
        {
            ended = true;
            outcome = lineResult;
            Implementor.logEor(false);
            result = lineResult;
            return true;
        }

        return ReachedEnd(out result);
    }

    bool ReachedEnd(out ExecutionResult result)
    {
        if (ended)
        {
            result = outcome;
            return true;
        }

        while (shouldRun && programCounter < instructions.Length && instructions[programCounter].metadata.line < 0)
        {
            ref var instruction = ref FetchDecode();
            Execute(ref instruction);
        }

        if (!shouldRun || programCounter >= instructions.Length)
        {
            ended = true;
            outcome = ExecutionResult.OK;
            Implementor.logEor(false);
            result = outcome;
            return true;
        }

        if (instructionCounter >= InstructionLimit)
        {
            ended = true;
            outcome = ExecutionResult.ERROR(ExecutionResult.ErrorKind.INSTRUCTION_COUNT_LIMIT_EXCEEDED, instructions[programCounter].metadata.line);
            Implementor.logEor(true);
            result = outcome;
            return true;
        }

        result = ExecutionResult.OK;
        return false;
    }

    public ref BytecodeInstruction FetchDecode()
    {
        ref BytecodeInstruction instruction = ref instructions[programCounter++];

        return ref instruction;
    }

    ExecutionResult FetchValue(ref ValueUnion valueUnion, out ValueUnion cleanUnion, int line)
    {
        if (valueUnion.type == ValueUnion.ValueType.Number)
        {
            cleanUnion = valueUnion;
            return ExecutionResult.OK;
        }

        if (valueUnion.type == ValueUnion.ValueType.NumberStack)
        {
            ValueUnion stackValue = executionStack.Peek();
            if (stackValue.type != ValueUnion.ValueType.Number)
            {
                cleanUnion = new();

                return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_TYPE_MISMATCH, line);  
            }else if (stackValue.type == ValueUnion.ValueType.NumberStack)
            {
                cleanUnion = executionStack.Pop();
                return FetchValue(ref cleanUnion, out cleanUnion, line);
            }else
            {
                cleanUnion = executionStack.Pop();
                return ExecutionResult.OK;
            }
        }

        if (valueUnion.type == ValueUnion.ValueType.Boolean)
        {
            cleanUnion = valueUnion;
            return ExecutionResult.OK;
        }

        if (valueUnion.type == ValueUnion.ValueType.BooleanStack)
        {
            ValueUnion stackValue = executionStack.Peek();
            if (stackValue.type != ValueUnion.ValueType.Boolean)
            {
                cleanUnion = new();

                return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_TYPE_MISMATCH, line);  
            }else if (stackValue.type == ValueUnion.ValueType.BooleanStack)
            {
                cleanUnion = executionStack.Pop();
                return FetchValue(ref cleanUnion, out cleanUnion, line);
            }else
            {
                cleanUnion = executionStack.Pop();
                return ExecutionResult.OK;
            }
        }

        if (valueUnion.type == ValueUnion.ValueType.Variable)
        {
            ValueUnion union = Variables[valueUnion.variableId];
            cleanUnion = new();

            if (union.type == ValueUnion.ValueType.Uninitialized) return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_UNINITIALIZED, line);
            cleanUnion = union;

            return ExecutionResult.OK;
        }
        cleanUnion = new();
        return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_UNINITIALIZED, line);
    }

    ExecutionResult FetchLeftRight(ref BytecodeInstruction.Instruction.BinInstruction bin, out (long, long) result, int line)
    {
        result = (0,0);
        var leftResult = FetchValue(ref bin.left, out ValueUnion cleanUnionLeft, line);
        if (!leftResult.Ok)
            return leftResult;
        if (cleanUnionLeft.type != ValueUnion.ValueType.Number) return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_OPERAND,line);

        var rightResult = FetchValue(ref bin.right, out ValueUnion cleanUnionRight, line);
        if (!rightResult.Ok)
            return rightResult;
        if (cleanUnionRight.type != ValueUnion.ValueType.Number) return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_OPERAND, line);

        result = (cleanUnionLeft.value, cleanUnionRight.value);
        return ExecutionResult.OK;
    }

    ExecutionResult HandleBinOp(ref BytecodeInstruction instruction, BytecodeInstruction.InstructionKind kind, int line)
    {
        var result = FetchLeftRight(ref instruction.instruction.binaryInstruction, out (long l, long r) resultNum, line);
        if (!result.Ok) return result;

        long finalResult;
        if (kind == BytecodeInstruction.InstructionKind.Add)
        {
            finalResult = resultNum.l + resultNum.r;
        }else if (kind == BytecodeInstruction.InstructionKind.Sub)
        {
            finalResult = resultNum.l - resultNum.r;
        }else if (kind == BytecodeInstruction.InstructionKind.Mul)
        {
            finalResult = resultNum.l * resultNum.r;
        }
        else
        {
            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_OPERAND, line);
        }

        executionStack.Push(new ValueUnion
        {
            value = finalResult,
            type = ValueUnion.ValueType.Number
        });

        return result;
    }

    ExecutionResult HandleBolBinOp(ref BytecodeInstruction instruction, BytecodeInstruction.InstructionKind kind, int line)
    {
        ValueUnion.ValueType? required = kind switch
        {
            BytecodeInstruction.InstructionKind.Gt or BytecodeInstruction.InstructionKind.Lt or
            BytecodeInstruction.InstructionKind.Leq or BytecodeInstruction.InstructionKind.Geq => ValueUnion.ValueType.Number,
            BytecodeInstruction.InstructionKind.And or BytecodeInstruction.InstructionKind.Or => ValueUnion.ValueType.Boolean,
            _ => null
        };

        var resultLeft = FetchValue(ref instruction.instruction.binaryInstruction.left, out ValueUnion cleanLeftUnion, line);
        if (!resultLeft.Ok) return resultLeft;
        if (required.HasValue && cleanLeftUnion.type != required.Value) return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_OPERAND, line);

        var resultRight = FetchValue(ref instruction.instruction.binaryInstruction.right, out ValueUnion cleanRightUnion, line);
        if (!resultRight.Ok) return resultRight;
        if (required.HasValue && cleanRightUnion.type != required.Value) return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_OPERAND, line);

        if (cleanLeftUnion.type != cleanRightUnion.type)
        {
            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_OPERAND, line);
        }
        
        bool finalResult;
        if (kind == BytecodeInstruction.InstructionKind.Eq)
        {
            finalResult = cleanLeftUnion == cleanRightUnion;
        }
        else if (kind == BytecodeInstruction.InstructionKind.Neq)
        {
            finalResult = cleanLeftUnion != cleanRightUnion;
        }else if (kind == BytecodeInstruction.InstructionKind.Gt)
        {
            finalResult = cleanLeftUnion > cleanRightUnion;
        }else if (kind == BytecodeInstruction.InstructionKind.Lt)
        {
            finalResult = cleanLeftUnion < cleanRightUnion;
        }else if (kind == BytecodeInstruction.InstructionKind.Leq)
        {
            finalResult = cleanLeftUnion <= cleanRightUnion;
        }else if (kind == BytecodeInstruction.InstructionKind.Geq)
        {
            finalResult = cleanLeftUnion >= cleanRightUnion;
        }else if (kind == BytecodeInstruction.InstructionKind.And)
        {
            finalResult = ValueUnion.And(cleanLeftUnion, cleanRightUnion);
        }else if (kind == BytecodeInstruction.InstructionKind.Or)
        {
            finalResult = ValueUnion.Or(cleanLeftUnion, cleanRightUnion);
        }
        else
        {
            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_OPERAND, line);
        }

        executionStack.Push(new ValueUnion
        {
            boolean = finalResult,
            type = ValueUnion.ValueType.Boolean
        });

        return ExecutionResult.OK;
    }

    

    public void Jump(int bodyId, int pos)
    {
        // System.Console.WriteLine($"Jumping to {bodyId}");
        if (bodyId == -1) instructions = copyInstructions;
        else              instructions = bodies[bodyId]; 

        programCounter = pos;
    }

    public ExecutionResult Execute(ref BytecodeInstruction instruction)
    {
        // set cucc to 3 + 6
        // add 3, 6 -> on stack
        // set cucc <- from stack

        switch (instruction.instructionKind)
        {
            case BytecodeInstruction.InstructionKind.End:
                shouldRun = false;
                break;
            case BytecodeInstruction.InstructionKind.Add:
            case BytecodeInstruction.InstructionKind.Sub:
            case BytecodeInstruction.InstructionKind.Mul:
                var binOpResult = HandleBinOp(ref instruction, instruction.instructionKind, instruction.metadata.line);
                if (!binOpResult.Ok)
                {
                    return binOpResult;
                }
                break;
            case BytecodeInstruction.InstructionKind.Eq:
            case BytecodeInstruction.InstructionKind.Neq:
            case BytecodeInstruction.InstructionKind.Gt:
            case BytecodeInstruction.InstructionKind.Lt:
            case BytecodeInstruction.InstructionKind.Leq:
            case BytecodeInstruction.InstructionKind.Geq:
            case BytecodeInstruction.InstructionKind.And:
            case BytecodeInstruction.InstructionKind.Or:
                var bolBinOpResult = HandleBolBinOp(ref instruction, instruction.instructionKind, instruction.metadata.line);
                if (!bolBinOpResult.Ok)
                {
                    return bolBinOpResult;
                }
                break;

            case BytecodeInstruction.InstructionKind.Print:
                {
                    var valueUnion = instruction.instruction.singleValueInstruction.valueUnion;
                    var result = FetchValue(ref valueUnion, out ValueUnion value, instruction.metadata.line);

                    if (result.Ok)
                    {
                        lineParam = value;
                        if (value.type == ValueUnion.ValueType.Boolean)
                            System.Console.WriteLine(value.boolean ? "true" : "false");
                        else if (value.type == ValueUnion.ValueType.Number)
                            System.Console.WriteLine(value.value);
                    }else
                    {
                        return result;
                    }
                }
                break;

            case BytecodeInstruction.InstructionKind.Set:
                {
                    var result = FetchValue(ref instruction.instruction.setInstruction.value, out ValueUnion cleanUnion, instruction.metadata.line);
                    if (result.Ok)
                    {
                        if (Variables[instruction.instruction.setInstruction.variableId].type == ValueUnion.ValueType.Uninitialized)
                            goto set;

                        if (cleanUnion.type != Variables[instruction.instruction.setInstruction.variableId].type)
                        {
            
                            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_TYPE_MISMATCH, instruction.metadata.line);
                        }


                        set:
                            Variables[instruction.instruction.setInstruction.variableId] = cleanUnion;
                    }else
                    {
                        return result;
                    }
                }
                break;
            case BytecodeInstruction.InstructionKind.If:
                {
                    var value = executionStack.Pop();
                    var result = FetchValue(ref value, out ValueUnion cleanUnion, instruction.metadata.line);

                    if (result.Ok)
                    {
                        if (cleanUnion.type != ValueUnion.ValueType.Boolean)
                        {
                            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_NON_BOOL_CONDITION, instruction.metadata.line);
                        }

                        if (cleanUnion.boolean)
                        {
                            Jump(instruction.instruction.ifInstruction.body, 0);
                        }else if (instruction.instruction.ifInstruction.hasElse)
                        {
                            Jump(instruction.instruction.ifInstruction.elseBody, 0);
                        }
                    }else
                        return result;

                }
                break;
            case BytecodeInstruction.InstructionKind.Ret:
                {
                    Jump((int)instruction.instruction.binaryInstruction.left.value, (int)instruction.instruction.binaryInstruction.right.value);
                }
                break;
            case BytecodeInstruction.InstructionKind.Push:
                executionStack.Push(instruction.instruction.singleValueInstruction.valueUnion);
                break;

            case BytecodeInstruction.InstructionKind.Not:
                {
                    var result = FetchValue(ref instruction.instruction.singleValueInstruction.valueUnion, out ValueUnion cleanValue, instruction.metadata.line);
                    if (result.Ok)
                    {
                        if (cleanValue.type !=  ValueUnion.ValueType.Boolean)
                        {
                            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_OPERAND, instruction.metadata.line);
                        }
                        
                        cleanValue.boolean = !cleanValue.boolean;
                        executionStack.Push(cleanValue);
                    }else
                    {
                        return result;
                    }
                }
                break;
            case BytecodeInstruction.InstructionKind.Call:
                {
                    lineApi = instruction;
                    var result = FetchValue(ref instruction.instruction.singleValueInstruction.valueUnion, out ValueUnion cleanValue, instruction.metadata.line);
                    if (result.Ok)
                    {
                        lineParam = cleanValue;
                        if (cleanValue.type != ValueUnion.ValueType.Number || cleanValue.value < 0 || cleanValue.value > 2)
                        {
                            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_ARG, instruction.metadata.line);
                        }
                        
                        var isThereResult = Implementor.isThere(cleanValue, out bool isItThere, instruction.metadata.line);

                        if (isThereResult.Ok)
                        {
                            executionStack.Push(
                                new ValueUnion
                                {
                                    type = ValueUnion.ValueType.Boolean,
                                    boolean = isItThere
                                }
                            );
                            lineRet = executionStack.Peek();
                        }else
                        {
                            return isThereResult;
                        }

                    }else
                    {
                        return result;
                    }
                }
                break;
            case BytecodeInstruction.InstructionKind.Step:
                {
                    var result = FetchValue(ref instruction.instruction.singleValueInstruction.valueUnion, out ValueUnion cleanValue, instruction.metadata.line);
                    if (result.Ok)
                    {
                        lineParam = cleanValue;
                        if (cleanValue.type != ValueUnion.ValueType.Number || cleanValue.value < 0)
                            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_ARG, instruction.metadata.line);

                        var stepResult = Implementor.move(cleanValue, instruction.metadata.line);
                        if (!stepResult.Ok)
                        {
                            return stepResult;
                        }
                    }else
                    {
                        return result;
                    }
                }
                break;

            case BytecodeInstruction.InstructionKind.Turn:
                {
                    var result = FetchValue(ref instruction.instruction.singleValueInstruction.valueUnion, out ValueUnion cleanValue, instruction.metadata.line);
                    if (result.Ok)
                    {
                        lineParam = cleanValue;
                        if (cleanValue.type != ValueUnion.ValueType.Number || cleanValue.value < 0)
                            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_ARG, instruction.metadata.line);

                        var turnResult = Implementor.turn(cleanValue, instruction.metadata.line);
                        if (!turnResult.Ok)
                        {
                            return turnResult;
                        }
                    }else
                    {
                        return result;
                    }
                }
                break;

            case BytecodeInstruction.InstructionKind.Mine:
                {
                    var mineResult = Implementor.mine(instruction.metadata.line);
                    if (!mineResult.Ok)
                    {
                        return mineResult;
                    }
                }
                break;
            case BytecodeInstruction.InstructionKind.Nop:
                break;
        }

        return ExecutionResult.OK;
    }
}