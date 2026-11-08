using System.Runtime.InteropServices;
using Antlr4.Runtime.Misc;
using T2G_Cookie.Shared.Ast;

namespace T2G_Cookie.Backend.Parsing;

public class MineCodeVisitor : MineCodeParserBaseVisitor<object>
{
    public override Body VisitProgram([NotNull] MineCodeParser.ProgramContext context)
    {
        if (context.instructions() == null) return new Body([]);

        return new Body(VisitInstructions(context.instructions()));
    }

    public override Instruction[] VisitInstructions([NotNull] MineCodeParser.InstructionsContext context)
    {
        if (context.instruction() == null) return [];       

        var instructions = context.instruction();
        var parsedInstructions = new Instruction[instructions.Length];
        for (int i = 0; i < instructions.Length; i++)
        {
            var instruction = instructions[i];
            parsedInstructions[i] = VisitInstruction(instruction);
        }

        return parsedInstructions;
    }

    public override Instruction VisitInstruction([NotNull] MineCodeParser.InstructionContext context)
    {
        return (Instruction) Visit(context.GetChild(0));
    }

    public override Instruction VisitStepInstruction([NotNull] MineCodeParser.StepInstructionContext context)
    {
        return new StepInstruction(VisitExpression(context.expression()), context.Start.Line);
    }

    public override Instruction VisitSetInstruction([NotNull] MineCodeParser.SetInstructionContext context)
    {
        return new SetInstruction(context.IDENTIFIER().GetText(), VisitExpression(context.expression()), context.Start.Line);
    }

    public override Instruction VisitMineInstruction([NotNull] MineCodeParser.MineInstructionContext context)
    {
        return new MineInstruction(context.Start.Line);
    }

    public override Instruction VisitTurnInstruction([NotNull] MineCodeParser.TurnInstructionContext context)
    {
        return new TurnInstruction(VisitExpression(context.expression()), context.Start.Line);
    }

    public override object VisitPrintInstruction([NotNull] MineCodeParser.PrintInstructionContext context)
    {
        return new PrintInstruction(VisitExpression(context.expression()), context.Start.Line);
    }

    public override Instruction VisitIfInstruction([NotNull] MineCodeParser.IfInstructionContext context)
    {
        var mainBody = VisitBody(context.body()[0]);
        var elseBody = context.body().Length == 2 ? VisitBody(context.body()[1]) : null;
        return new IfInstruction(VisitExpression(context.expression()), 
            mainBody, elseBody, 
            context.Start.Line, elseBody != null ? context.ELSE().Symbol.Line : -1);
    }

    public override Instruction VisitWhileInstruction([NotNull] MineCodeParser.WhileInstructionContext context)
    {
        return new WhileInstruction(VisitExpression(context.expression()), VisitBody(context.body()), context.Start.Line);
    }

    public override Body VisitBody([NotNull] MineCodeParser.BodyContext context)
    {
        if (context.instructions() == null) return new Body([]);

        return new Body(VisitInstructions(context.instructions()));
    }

    public override Value VisitExpression([NotNull] MineCodeParser.ExpressionContext context)
    {
        return (Value) Visit(context.GetChild(0));
    }

    public override object VisitUnaryExpression([NotNull] MineCodeParser.UnaryExpressionContext context)
    {
        return new Not(VisitAtom(context.atom()));
    }

    static BinOp.Operation GetOperation(MineCodeParser.BinExpressionContext ctx)
    {
        if (ctx.ADD()   != null) return BinOp.Operation.Add;
        if (ctx.MINUS() != null) return BinOp.Operation.Sub;
        if (ctx.STAR()  != null) return BinOp.Operation.Mul;
        if (ctx.EQ()    != null) return BinOp.Operation.Eq;
        if (ctx.NEQ()   != null) return BinOp.Operation.Neq;
        if (ctx.LT()    != null) return BinOp.Operation.Lt;
        if (ctx.LEQ()   != null) return BinOp.Operation.Leq;
        if (ctx.GT()    != null) return BinOp.Operation.Gt;
        if (ctx.GEQ()   != null) return BinOp.Operation.Geq;
        if (ctx.AND()   != null) return BinOp.Operation.And;
        if (ctx.OR()    != null) return BinOp.Operation.Or;

        throw new InvalidOperationException($"Unknown binary operator in '{ctx.GetText()}'");
    }

    public override Value VisitBinExpression([NotNull] MineCodeParser.BinExpressionContext context)
    {
        return new BinOp(VisitAtom(context.atom()[0]), VisitAtom(context.atom()[1]), GetOperation(context));
    }

    public override object VisitFunctionExpression([NotNull] MineCodeParser.FunctionExpressionContext context)
    {
        return new IsThere(VisitAtom(context.atom()));
    }

    public override SimpleValue VisitAtom([NotNull] MineCodeParser.AtomContext context)
    {
        if (context.TRUE() != null || context.FALSE() != null)
        {
            return new LiteralBool(context.TRUE() != null);
        } 

        if (context.NUMBER() != null)
        {
            if (long.TryParse(context.NUMBER().GetText(), out long value))
            {
                return new Literal(value);
            }
        }

        return new Variable(context.IDENTIFIER().GetText());
    }
}