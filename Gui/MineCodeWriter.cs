using System.Text;
using T2G_Cookie.Shared.Ast;

namespace T2G_Cookie.Gui;

/// <summary>
/// Renders the converted AST back into MineCode source. This is the exact textual
/// mirror of the block vocabulary: every token written here exists in
/// <c>lang/MineCodeLexer.g4</c> and every shape matches <c>lang/MineCodeParser.g4</c>.
/// Used by the Start dump and by the parity test that parses the text with the
/// Backend and compares the resulting AST.
/// </summary>
static class MineCodeWriter
{
    const int IndentSize = 4;

    public static string Write(Body body)
    {
        var sb = new StringBuilder();
        WriteBody(body, 0, sb);
        return sb.ToString();
    }

    static void WriteBody(Body body, int depth, StringBuilder sb)
    {
        foreach (Instruction instruction in body.Instructions)
            WriteInstruction(instruction, depth, sb);
    }

    static void WriteInstruction(Instruction instruction, int depth, StringBuilder sb)
    {
        string pad = new(' ', depth * IndentSize);
        switch (instruction)
        {
            case StepInstruction step:
                sb.Append(pad).Append("step forward ").AppendLine(WriteValue(step.Value));
                break;
            case TurnInstruction turn:
                sb.Append(pad).Append("turn by ").AppendLine(WriteValue(turn.Value));
                break;
            case MineInstruction:
                sb.Append(pad).AppendLine("mine here");
                break;
            case PrintInstruction print:
                sb.Append(pad).Append("print ").AppendLine(WriteValue(print.Value));
                break;
            case SetInstruction set:
                sb.Append(pad).Append("set ").Append(set.variableName).Append(" to ")
                  .AppendLine(WriteValue(set.Value));
                break;
            case IfInstruction ifInstruction:
                sb.Append(pad).Append("if ").AppendLine(WriteValue(ifInstruction.Expression));
                WriteBody(ifInstruction.body, depth + 1, sb);
                if (ifInstruction.elseBody != null)
                {
                    sb.Append(pad).AppendLine("else");
                    WriteBody(ifInstruction.elseBody, depth + 1, sb);
                }
                break;
            case WhileInstruction whileInstruction:
                sb.Append(pad).Append("while ").AppendLine(WriteValue(whileInstruction.Expression));
                WriteBody(whileInstruction.body, depth + 1, sb);
                break;
        }
    }

    static string WriteValue(Value value) => value switch
    {
        Literal literal => literal.value.ToString(),
        LiteralBool literal => literal.value ? "true" : "false",
        Variable variable => variable.variable,
        Not negated => "not " + WriteValue(negated.Value),
        IsThere isThere => "isThere " + WriteValue(isThere.Arg),
        BinOp binOp => $"{WriteValue(binOp.left)} {Symbol(binOp.operation)} {WriteValue(binOp.right)}",
        _ => "0"
    };

    static string Symbol(BinOp.Operation operation) => operation switch
    {
        BinOp.Operation.Add => "+",
        BinOp.Operation.Sub => "-",
        BinOp.Operation.Mul => "*",
        BinOp.Operation.Eq => "=",
        BinOp.Operation.Neq => "<>",
        BinOp.Operation.Lt => "<",
        BinOp.Operation.Leq => "<=",
        BinOp.Operation.Gt => ">",
        BinOp.Operation.Geq => ">=",
        BinOp.Operation.And => "and",
        BinOp.Operation.Or => "or",
        _ => "+"
    };
}
