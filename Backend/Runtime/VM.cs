using System.Runtime.InteropServices;
using T2G_Cookie.Shared.Ast;

namespace T2G_Cookie.Backend.VM;

public class VMInstance
{
    int programCounter;

    bool shouldRun;
    BytecodeInstruction[] instructions;
    readonly BytecodeInstruction[] copyInstructions;
    readonly Dictionary<int, BytecodeInstruction[]> bodies;

    public Stack<ValueUnion> executionStack = new();

    public ValueUnion[] Variables;

    [StructLayout(LayoutKind.Explicit)]
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
            public int Line;
            public ErrorKind ErrorKind;
        }

        [FieldOffset(0)] public bool Ok;
        [FieldOffset(8)] public ErrorObj Error;

        public readonly static ExecutionResult OK = new ExecutionResult { Ok = true };
        public static ExecutionResult ERROR(ErrorKind errorKind) => new ExecutionResult { Error = new ErrorObj { Line = 0, ErrorKind = errorKind}};
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

    // public static VMInstance CreateFromAST()
    // {
        
    // }

    public ExecutionResult RunCode(bool pause)
    {
        while (programCounter<1000 && shouldRun && programCounter < instructions.Length)
        {
            var executionResult = Execute(ref FetchDecode());

            if (executionResult.Ok)
            {
                continue;
            }


            return executionResult;
        }

        return ExecutionResult.OK;
    }

    public ref BytecodeInstruction FetchDecode()
    {
        ref BytecodeInstruction instruction = ref instructions[programCounter++];

        return ref instruction;
    }

    ExecutionResult FetchValue(ref ValueUnion valueUnion, out ValueUnion cleanUnion)
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
                return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_TYPE_MISMATCH);  
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
                return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_TYPE_MISMATCH);  
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

            if (union.type == ValueUnion.ValueType.Uninitialized) return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_UNINITIALIZED);
            cleanUnion = union;

            return ExecutionResult.OK;
        }
        cleanUnion = new();
        return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_UNINITIALIZED);
    }

    ExecutionResult FetchLeftRight(ref BytecodeInstruction.Instruction.BinInstruction bin, out (long, long) result)
    {
        result = (0,0);
        var leftResult = FetchValue(ref bin.left, out ValueUnion cleanUnionLeft);
        if (!leftResult.Ok)
            return leftResult;

        var rightResult = FetchValue(ref bin.right, out ValueUnion cleanUnionRight);
        if (!rightResult.Ok)
            return rightResult;

        if (cleanUnionLeft.type != ValueUnion.ValueType.Number) return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_TYPE_MISMATCH);
        if (cleanUnionRight.type != ValueUnion.ValueType.Number) return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_TYPE_MISMATCH);

        result = (cleanUnionLeft.value, cleanUnionRight.value);
        return ExecutionResult.OK;
    }

    ExecutionResult HandleBinOp(ref BytecodeInstruction instruction, BytecodeInstruction.InstructionKind kind)
    {
        var result = FetchLeftRight(ref instruction.instruction.binaryInstruction, out (long l, long r) resultNum);
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
            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_OPERAND);
        }

        executionStack.Push(new ValueUnion
        {
            value = finalResult,
            type = ValueUnion.ValueType.Number
        });

        return result;
    }

    ExecutionResult HandleBolBinOp(ref BytecodeInstruction instruction, BytecodeInstruction.InstructionKind kind)
    {
        var resultLeft = FetchValue(ref instruction.instruction.binaryInstruction.left, out ValueUnion cleanLeftUnion);
        if (!resultLeft.Ok) return resultLeft;

        var resultRight = FetchValue(ref instruction.instruction.binaryInstruction.right, out ValueUnion cleanRightUnion);
        if (!resultRight.Ok) return resultRight;

        if (cleanLeftUnion.type != cleanRightUnion.type)
        {
            System.Console.WriteLine("This dude?");
            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_TYPE_MISMATCH);
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
            finalResult = cleanLeftUnion >= cleanRightUnion;
        }else if (kind == BytecodeInstruction.InstructionKind.Geq)
        {
            finalResult = cleanLeftUnion <= cleanRightUnion;
        }else if (kind == BytecodeInstruction.InstructionKind.And)
        {
            finalResult = ValueUnion.And(cleanLeftUnion, cleanRightUnion);
        }else if (kind == BytecodeInstruction.InstructionKind.Or)
        {
            finalResult = ValueUnion.Or(cleanLeftUnion, cleanRightUnion);
        }
        else
        {
            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_BAD_OPERAND);
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
                var binOpResult = HandleBinOp(ref instruction, instruction.instructionKind);
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
                var bolBinOpResult = HandleBolBinOp(ref instruction, instruction.instructionKind);
                if (!bolBinOpResult.Ok)
                {
                    return bolBinOpResult;
                }
                break;

            case BytecodeInstruction.InstructionKind.Print:
                {
                    var valueUnion = instruction.instruction.singleValueInstruction.valueUnion;
                    var result = FetchValue(ref valueUnion, out ValueUnion value);

                    if (result.Ok)
                    {
                        if (value.type == ValueUnion.ValueType.Boolean)
                            System.Console.WriteLine(value.boolean);
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
                    var result = FetchValue(ref instruction.instruction.setInstruction.value, out ValueUnion cleanUnion);
                    if (result.Ok)
                    {
                        if (Variables[instruction.instruction.setInstruction.variableId].type == ValueUnion.ValueType.Uninitialized)
                            goto set;

                        if (cleanUnion.type != Variables[instruction.instruction.setInstruction.variableId].type)
                        {
                            System.Console.WriteLine("This dude? 2");
                            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_TYPE_MISMATCH);
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
                    var result = FetchValue(ref value, out ValueUnion cleanUnion);

                    if (result.Ok)
                    {
                        if (cleanUnion.type != ValueUnion.ValueType.Boolean)
                        {
                            System.Console.WriteLine(cleanUnion.type);
                            return ExecutionResult.ERROR(ExecutionResult.ErrorKind.E_TYPE_MISMATCH);
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
        }

        return ExecutionResult.OK;
    }
}