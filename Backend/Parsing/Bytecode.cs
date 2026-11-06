using System.Runtime.InteropServices;

namespace T2G_Cookie.Backend;

public struct BytecodeInstruction
{
    [StructLayout(LayoutKind.Explicit)]
    public struct ValueUnion
    {
        public enum ValueType: byte
        {
            Uninitialized,
            Variable,
            Number,
            Boolean
        }

        [FieldOffset(0)] public ValueType type;
        [FieldOffset(8)] public bool boolean;
        [FieldOffset(8)] public int variableId;
        [FieldOffset(8)] public long number;
    }

    public struct Instruction
    {
        public enum InstructionKind : byte
        {
            Step, Turn, Mine, Print, Set, If, While
        }
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
            public ValueUnion condition;
            public InstructionHolder[] instructions;
            public InstructionHolder[]? elseInstructions;
        }

        public struct WhileInstruction
        {
            public ValueUnion condition;
            public InstructionHolder[] instructions;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InstructionHolder
        {
            [FieldOffset(0)] public SingleValueInstruction singleValueInstruction; // Step, Turn, Mine, Print
            [FieldOffset(0)] public SetInstruction         setInstruction;
            [FieldOffset(0)] public IfInstruction          ifInstruction;
            [FieldOffset(0)] public WhileInstruction       whileInstruction;
            
        }

        public InstructionKind instructionKind;
        public InstructionHolder instruction;
    }
}