using T2G_Cookie.Shared.Ast;

namespace T2G_Cookie.Gui;

/// <summary>
/// The reverse of <see cref="AstConverter"/>: builds block trees from a parsed script,
/// so the Shared AST the Backend produces from MineCode source can be loaded into the
/// workspace and edited. Every join goes through the same API the mouse uses
/// (Append / AttachInside / AttachToSlot), so a load can never knot the graph the
/// forest invariant guards; anything that cannot be represented is reported as a
/// problem string instead of a half-built stack.
/// </summary>
static class AstLoader
{
    sealed class Ctx(Workspace ws)
    {
        public readonly Workspace Ws = ws;
        public readonly List<string> Problems = new();

        /// <summary>All palette factories, collected once; candidates that don't match
        /// are unregistered so they never show up as ghosts in Element.DrawAll.</summary>
        public readonly List<Func<Block>> Factories =
            BlockLibrary.CreateCategories().SelectMany(c => c.Factories).ToList();

        public void Problem(string message) => Problems.Add(message);
    }

    /// <summary>Loads every script as its own Start stack; returns the problems found.</summary>
    public static List<string> Load(Workspace ws, IReadOnlyList<Body> scripts)
    {
        Ctx ctx = new(ws);
        int y = 24;

        foreach (Body script in scripts)
        {
            HatBlock hat = (HatBlock)Create(ctx, BlockKind.Start);
            Block? head = BuildChain(script, ctx);
            if (head != null && !hat.Append(head))
            {
                ctx.Problem("the script refused to attach under Start");
                head.UnregisterTree();
            }
            ws.LoadRoot(hat, 24, y);
            y += hat.ChainHeight() + 32;
        }

        return ctx.Problems;
    }

    static Block? BuildChain(Body body, Ctx ctx)
    {
        Block? head = null;
        foreach (Instruction instruction in body.Instructions)
        {
            Block? block = BuildStatement(instruction, ctx);
            if (block == null) continue;

            if (head == null)
            {
                head = block;
                continue;
            }
            if (!head.Append(block))
            {
                ctx.Problem($"{block.Label} refused to chain after {head.Tail.Label}");
                block.UnregisterTree();
            }
        }
        return head;
    }

    static Block? BuildStatement(Instruction instruction, Ctx ctx)
    {
        switch (instruction)
        {
            case StepInstruction step:
                return WithValue(Create(ctx, BlockKind.Step), 0, step.Value, ctx);
            case TurnInstruction turn:
                return WithValue(Create(ctx, BlockKind.Turn), 0, turn.Value, ctx);
            case MineInstruction:
                return Create(ctx, BlockKind.Mine);
            case PrintInstruction print:
                return WithValue(Create(ctx, BlockKind.Print), 0, print.Value, ctx);
            case SetInstruction set:
                {
                    Block block = Create(ctx, BlockKind.Set);
                    EnsureVariable(set.variableName, ctx);
                    block.Slots[0].SelectedName = set.variableName;
                    return WithValue(block, 1, set.Value, ctx);
                }
            case IfInstruction ifInstruction:
                {
                    bool hasElse = ifInstruction.elseBody != null;
                    var c = (CBlock)Create(ctx, BlockKind.If,
                        b => b is CBlock cb && cb.HasElse == hasElse);
                    WithValue(c, 0, ifInstruction.Expression, ctx);
                    AttachMouth(c, ifInstruction.body, mouth2: false, ctx);
                    if (ifInstruction.elseBody != null)
                        AttachMouth(c, ifInstruction.elseBody, mouth2: true, ctx);
                    return c;
                }
            case WhileInstruction whileInstruction:
                {
                    var c = (CBlock)Create(ctx, BlockKind.While);
                    WithValue(c, 0, whileInstruction.Expression, ctx);
                    AttachMouth(c, whileInstruction.body, mouth2: false, ctx);
                    return c;
                }
            default:
                ctx.Problem($"don't know how to build '{instruction}'");
                return null;
        }
    }

    /// <summary>Builds the value and parks it in field <paramref name="index"/>.</summary>
    static Block WithValue(Block host, int index, Value value, Ctx ctx)
        => Attach(host, index, BuildValue(value, ctx), ctx);

    /// <summary>Parks a built reporter in field <paramref name="index"/>, or reports the refusal.</summary>
    static Block Attach(Block host, int index, Block reporter, Ctx ctx)
    {
        if (index >= host.Slots.Count)
        {
            ctx.Problem($"{host.Label} has no field {index}");
            reporter.UnregisterTree();
            return host;
        }
        if (!host.AttachToSlot(host.Slots[index], reporter))
        {
            ctx.Problem($"{host.Label} refused the value block");
            reporter.UnregisterTree();
        }
        return host;
    }

    static void AttachMouth(CBlock host, Body body, bool mouth2, Ctx ctx)
    {
        Block? head = BuildChain(body, ctx);
        if (head == null) return; // an empty body is valid: nothing to attach

        bool ok = mouth2 ? host.AttachInside2(head) : host.AttachInside0(head);
        if (!ok)
        {
            ctx.Problem($"{head.Label} refused the mouth of {host.Label}");
            head.UnregisterTree();
        }
    }

    static Block BuildValue(Value value, Ctx ctx)
    {
        switch (value)
        {
            case Literal literal:
                {
                    var number = (NumberBlock)Create(ctx, BlockKind.Number);
                    number.Text = literal.value.ToString();
                    return number;
                }
            case LiteralBool flag:
                {
                    var boolean = (BooleanBlock)Create(ctx, BlockKind.Boolean);
                    boolean.Value = flag.value;
                    return boolean;
                }
            case Variable variable:
                {
                    EnsureVariable(variable.variable, ctx);
                    var reference = (VariableBlock)Create(ctx, BlockKind.Variable);
                    reference.Name = variable.variable;
                    return reference;
                }
            case BinOp binOp:
                {
                    Block host = Create(ctx, KindOf(binOp.operation));
                    WithValue(host, 0, binOp.left, ctx);
                    WithValue(host, 1, binOp.right, ctx);
                    return host;
                }
            case Not not:
                return WithValue(Create(ctx, BlockKind.Not), 0, not.Value, ctx);
            case IsThere isThere:
                return WithValue(Create(ctx, BlockKind.IsThere), 0, isThere.Arg, ctx);
            default:
                ctx.Problem($"don't know how to build the value '{value}'");
                return Create(ctx, BlockKind.Number);
        }
    }

    static BlockKind KindOf(BinOp.Operation operation) => operation switch
    {
        BinOp.Operation.Add => BlockKind.Add,
        BinOp.Operation.Sub => BlockKind.Sub,
        BinOp.Operation.Mul => BlockKind.Mul,
        BinOp.Operation.Eq => BlockKind.Eq,
        BinOp.Operation.Neq => BlockKind.Neq,
        BinOp.Operation.Lt => BlockKind.Lt,
        BinOp.Operation.Leq => BlockKind.Leq,
        BinOp.Operation.Gt => BlockKind.Gt,
        BinOp.Operation.Geq => BlockKind.Geq,
        BinOp.Operation.And => BlockKind.And,
        BinOp.Operation.Or => BlockKind.Or,
        _ => throw new InvalidOperationException($"unknown operator {operation}")
    };

    /// <summary>Variables used by the file are created on the way in.</summary>
    static void EnsureVariable(string name, Ctx ctx)
    {
        if (name.Length == 0 || ctx.Ws.Registry.Contains(name)) return;
        if (!ctx.Ws.Registry.TryCreate(name, out _))
            ctx.Problem($"can't create the variable {name}");
    }

    static Block Create(Ctx ctx, BlockKind kind, Func<Block, bool>? pick = null)
    {
        foreach (Func<Block> factory in ctx.Factories)
        {
            Block block = factory();
            if (block.Kind == kind && (pick == null || pick(block))) return block;

            // The factory registered the candidate on construction; a block that is
            // not the one we wanted must leave the global list again.
            Element.Unregister(block);
        }
        throw new InvalidOperationException("no block for kind " + kind);
    }
}
