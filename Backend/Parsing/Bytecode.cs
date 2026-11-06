using System.Runtime.InteropServices;
using T2G_Cookie.Shared.Ast;

namespace T2G_Cookie.Backend;

public struct BytecodeHeader
{
    public BytecodeInstruction[] Main;
    public Dictionary<int, BytecodeInstruction[]> Bodies;

    public int variableCount;
}

[StructLayout(LayoutKind.Explicit)]
public struct ValueUnion
{
    public enum ValueType : byte
    {
        Uninitialized,
        Variable,     // reference
        NumberStack,  // pops from the stack one final value
        Number,       // literal

        BooleanStack, // pops from the stack one final value but it's a boolean
        Boolean       // literal
    }

    [FieldOffset(0)] public ValueType type;
    [FieldOffset(8)] public bool boolean;
    [FieldOffset(8)] public int variableId;
    [FieldOffset(8)] public long value;

    public static bool operator ==(ValueUnion left, ValueUnion right)
    {
        if (left.type != right.type) return false;

        if (left.type == ValueType.Number)
        {
            return left.value == right.value;
        }

        if (left.type == ValueType.Boolean)
        {
            return left.boolean == right.boolean;
        }

        return false;
    }

    public static bool operator !=(ValueUnion left, ValueUnion right) => !(left == right);

    public static bool operator >(ValueUnion left, ValueUnion right)
    {
        if (left.type != right.type) return false;

        if (left.type == ValueType.Number)
        {
            return left.value > right.value;
        }

        return false;
    }

    public static bool operator <(ValueUnion left, ValueUnion right)
    {
        if (left.type != right.type) return false;

        if (left.type == ValueType.Number)
        {
            return left.value < right.value;
        }

        return false;
    }

    public static bool operator >=(ValueUnion left, ValueUnion right)
    {
        if (left.type != right.type) return false;

        if (left.type == ValueType.Number)
        {
            return left.value >= right.value;
        }

        return false;
    }

    public static bool operator <=(ValueUnion left, ValueUnion right)
    {
        if (left.type != right.type) return false;

        if (left.type == ValueType.Number)
        {
            return left.value <= right.value;
        }

        return false;
    }

    public static bool And(ValueUnion left, ValueUnion right)
    {
        if (left.type != right.type) return false;

        if (left.type == ValueType.Boolean)
        {
            return left.boolean && right.boolean;
        }

        return false;
    }

    public static bool Or(ValueUnion left, ValueUnion right)
    {
        if (left.type != right.type) return false;

        if (left.type == ValueType.Boolean)
        {
            return left.boolean || right.boolean;
        }

        return false;
    }

    public override string ToString()
    {
        return $"{type} {value} {variableId} {boolean}";
    }
}

// A VM-nek van jmp-ja. De csakis conditional. Az If.
// De mivel van ugrás, ezért a fasznak lenne a whilenak is opcode. Csak vissza ugrunk a conditionhöz, ha igaz vissza a bodyba
public struct BytecodeInstruction
{
    public static BytecodeInstruction Ret(int body, int pos) => new BytecodeInstruction
    {
        instructionKind = BytecodeInstruction.InstructionKind.Ret, // Jump to condition
        instruction = new BytecodeInstruction.Instruction
        {
            binaryInstruction = new BytecodeInstruction.Instruction.BinInstruction
            {
                left = new ValueUnion
                {
                    value = body
                },
                right = new ValueUnion
                {
                    value = pos
                }
            }
        }
    };

    public static BytecodeInstruction If(int body, bool hasElse, int elseBody) => new()
    {
        instructionKind = InstructionKind.If,
        instruction = new Instruction
        {
            ifInstruction = new Instruction.IfInstruction
            {
                body = body,
                hasElse = hasElse,
                elseBody = elseBody
            }
        }
    };

    public static BytecodeInstruction Push(ValueUnion value) => new()
    {
        instructionKind = InstructionKind.Push,
        instruction = new Instruction
        {
            singleValueInstruction = new()
            {
                valueUnion = value
            }
        }
    };


    public enum InstructionKind : byte
    {
        // Also while isn't a concept.
        Step, Turn, Mine, Print, Set, If,

        // Math
        Add, Sub, Mul, // Puts on stack

        // Logic
        And, Or, Eq, Neq, Lt, Gt, Leq, Geq, // Puts on stack

        // Unary
        Not, // Puts on stack

        // Function
        Call, // Puts on stack

        Ret,   // Pops the value from the stack. If that value is a number, it returns to Program counter to that number

        Push,   // nice and simple and used for a bit more direct stack manipulation purely from codegen

        End    // marks the end of the program
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct Instruction
    {
        public struct SingleValueInstruction
        {
            public ValueUnion valueUnion;
        }

        public struct SetInstruction
        {
            public int variableId;
            public ValueUnion value;
        }

        public struct IfInstruction
        {
            public int body;
            public int elseBody;
            public bool hasElse;
        }

        public struct WhileInstruction
        {
            public ValueUnion condition;
            public int body;
        }

        public struct BinInstruction
        {
            public ValueUnion left;
            public ValueUnion right;
        }

        [FieldOffset(0)] public SingleValueInstruction singleValueInstruction; // Step, Turn, Print, Not, Call
        [FieldOffset(0)] public SetInstruction setInstruction;
        [FieldOffset(0)] public IfInstruction ifInstruction;
        [FieldOffset(0)] public WhileInstruction whileInstruction;
        [FieldOffset(0)] public BinInstruction binaryInstruction;      // add, sub, mul, and, or, 
    }

    public struct Metadata
    {
        public int line;
    }

    public InstructionKind instructionKind;
    public Instruction instruction;

    public Metadata metadata;
}