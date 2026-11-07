namespace T2G_Cookie.Shared.Ast;

public abstract record class Instruction;
public record class Body(Instruction[] Instructions);
public record class StepInstruction(Value Value, int Line) : Instruction;
public record class TurnInstruction(Value Value, int Line) : Instruction;
public record class MineInstruction(int Line) : Instruction;
public record class PrintInstruction(Value Value, int Line) : Instruction;
public record class SetInstruction(string variableName, Value Value, int Line) : Instruction;
public record class IfInstruction(Value Expression, Body body, Body? elseBody, int Line) : Instruction;
public record class WhileInstruction(Value Expression, Body body, int Line) : Instruction;