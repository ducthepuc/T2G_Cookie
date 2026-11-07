using System.Text;
using T2G_Cookie.Shared.Ast;

namespace T2G_Cookie.Gui;

/// <summary>One validation problem, pinned to the block that shows its badge.</summary>
sealed record AstError(Block Block, string Message);

/// <summary>The converted scripts plus every problem found on the way.</summary>
sealed class AstResult
{
    public Body[] Scripts { get; init; } = [];
    public IReadOnlyList<AstError> Errors { get; init; } = [];

    /// <summary>The converted scripts rendered back into MineCode source.</summary>
    public string ToMineCode() => string.Join("\n", Scripts.Select(MineCodeWriter.Write));

    public string Dump()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Scripts.Length; i++)
        {
            sb.AppendLine($"script {i}:");
            DumpBody(Scripts[i], "  ", sb);
        }
        if (Errors.Count == 0 && Scripts.Length > 0)
        {
            sb.AppendLine("code:");
            foreach (Body script in Scripts)
                sb.Append(MineCodeWriter.Write(script));
        }
        foreach (AstError e in Errors)
            sb.AppendLine($"error: {e.Message}  [{e.Block.Label}]");
        return sb.ToString();
    }

    internal static void DumpBody(Body body, string indent, StringBuilder sb)
    {
        foreach (Instruction inst in body.Instructions)
            DumpInstruction(inst, indent, sb);
    }

    static void DumpInstruction(Instruction inst, string indent, StringBuilder sb)
    {
        switch (inst)
        {
            case StepInstruction s: sb.AppendLine($"{indent}Step({DumpValue(s.Value)})"); break;
            case TurnInstruction t: sb.AppendLine($"{indent}Turn({DumpValue(t.Value)})"); break;
            case MineInstruction: sb.AppendLine($"{indent}Mine"); break;
            case PrintInstruction p: sb.AppendLine($"{indent}Print({DumpValue(p.Value)})"); break;
            case SetInstruction set: sb.AppendLine($"{indent}Set({set.variableName}, {DumpValue(set.Value)})"); break;
            case IfInstruction i:
                sb.AppendLine($"{indent}If({DumpValue(i.Expression)}) {{");
                DumpBody(i.body, indent + "  ", sb);
                if (i.elseBody != null)
                {
                    sb.AppendLine($"{indent}}} else {{");
                    DumpBody(i.elseBody, indent + "  ", sb);
                }
                sb.AppendLine($"{indent}}}");
                break;
            case WhileInstruction w:
                sb.AppendLine($"{indent}While({DumpValue(w.Expression)}) {{");
                DumpBody(w.body, indent + "  ", sb);
                sb.AppendLine($"{indent}}}");
                break;
            default:
                sb.AppendLine($"{indent}?");
                break;
        }
    }

    internal static string DumpValue(Value v) => v switch
    {
        Literal l => $"Literal({l.value})",
        LiteralBool b => b.value ? "true" : "false",
        Variable var => $"Variable({var.variable})",
        Not n => $"Not({DumpValue(n.Value)})",
        IsThere i => $"IsThere({DumpValue(i.Arg)})",
        BinOp op => $"{op.operation}({DumpValue(op.left)}, {DumpValue(op.right)})",
        _ => "?"
    };
}

/// <summary>
/// Converts block trees into the Shared AST. Pure and cycle safe: it walks from the
/// workspace roots, visits every block at most once, and reports problems instead of
/// throwing - every report becomes a badge on the offending block.
/// </summary>
static class AstConverter
{
    sealed class Ctx(VariableRegistry registry)
    {
        public readonly VariableRegistry Registry = registry;
        public readonly HashSet<Block> Visited = new();
        public readonly List<AstError> Errors = new();

        /// <summary>
        /// The line the next instruction will occupy in the source MineCodeWriter emits.
        /// Keeping it in step with the writer means every instruction carries the line a
        /// backend/VM diagnostic would point at in the generated MineCode.
        /// </summary>
        public int NextLine = 1;

        public int TakeLine() => NextLine++;

        public void Error(Block b, string message) => Errors.Add(new AstError(b, message));
    }

    public static AstResult Convert(Workspace ws)
    {
        var ctx = new Ctx(ws.Registry);
        var scripts = new List<Body>();
        Block? hatRoot = null;

        foreach (Block root in ws.Roots)
        {
            if (root is HatBlock)
            {
                if (hatRoot == null)
                {
                    hatRoot = root;
                    continue;
                }
                ctx.Error(root, "only one Start block is allowed");
                continue;
            }
            ctx.Error(root, "script has no Start block");
        }

        if (hatRoot != null)
        {
            ctx.NextLine = 1; // every script's generated source starts at line 1
            scripts.Add(new Body(ConvertChain(hatRoot, ctx).ToArray()));
        }

        return new AstResult { Scripts = scripts.ToArray(), Errors = ctx.Errors };
    }

    static List<Instruction> ConvertChain(Block? head, Ctx ctx)
    {
        var list = new List<Instruction>();
        for (Block? b = head; b != null; b = b.Next)
        {
            if (!ctx.Visited.Add(b))
            {
                ctx.Error(b, "block appears twice in the script");
                break;
            }
            if (b is HatBlock) continue; // the hat marks the script; it is not an instruction

            Instruction? inst = ConvertStatement(b, ctx);
            if (inst != null) list.Add(inst);
        }
        return list;
    }

    static Instruction? ConvertStatement(Block b, Ctx ctx)
    {
        // Taken before any nested body is converted, so the header of an if/while owns
        // its own line exactly the way MineCodeWriter writes it.
        int line = ctx.TakeLine();

        switch (b.Kind)
        {
            case BlockKind.Step:
                return new StepInstruction(ValueOf(b, 0, ctx), line);
            case BlockKind.Turn:
                return new TurnInstruction(ValueOf(b, 0, ctx), line);
            case BlockKind.Mine:
                return new MineInstruction(line);
            case BlockKind.Print:
                return new PrintInstruction(ValueOf(b, 0, ctx), line);
            case BlockKind.Set:
                {
                    string? name = b.Slots.Count > 0 ? b.Slots[0].SelectedName : null;
                    if (name == null) ctx.Error(b, "no variable chosen");
                    else if (ctx.Registry.IsBuiltin(name)) ctx.Error(b, $"{name} is read-only");
                    else if (!ctx.Registry.Contains(name)) ctx.Error(b, $"unknown variable {name}");
                    return new SetInstruction(name ?? "", ValueOf(b, 1, ctx), line);
                }
            case BlockKind.If:
                {
                    if (b is not CBlock c) return BrokenStatement(b, ctx, "if block is not a C-block");
                    var body = new Body(ConvertChain(c.Inner, ctx).ToArray());
                    if (body.Instructions.Length == 0)
                        ctx.Error(c, "if body can't be empty");

                    Body? elseBody = null;
                    if (c.HasElse)
                    {
                        ctx.NextLine++; // the `else` keyword gets a line of its own
                        elseBody = new Body(ConvertChain(c.Inner2, ctx).ToArray());
                        if (elseBody.Instructions.Length == 0)
                            ctx.Error(c, "else body can't be empty");
                    }
                    return new IfInstruction(ValueOf(b, 0, ctx), body, elseBody, line);
                }
            case BlockKind.While:
                {
                    if (b is not CBlock c) return BrokenStatement(b, ctx, "while block is not a C-block");
                    var body = new Body(ConvertChain(c.Inner, ctx).ToArray());
                    if (body.Instructions.Length == 0)
                        ctx.Error(c, "while body can't be empty");
                    return new WhileInstruction(ValueOf(b, 0, ctx), body, line);
                }
            default:
                ctx.Error(b, "this block can't be a step of a script");
                return null;
        }
    }

    /// <summary>Reads the reporter parked in field <paramref name="index"/>, or reports it empty.</summary>
    static Value ValueOf(Block b, int index, Ctx ctx)
    {
        if (index >= b.Slots.Count)
        {
            ctx.Error(b, "missing field");
            return new Literal(0);
        }
        Slot slot = b.Slots[index];
        if (slot.Content == null)
        {
            ctx.Error(b, "empty field: drop a value block in");
            return new Literal(0);
        }
        return ConvertValue(slot.Content, ctx);
    }

    static Value ConvertValue(Block b, Ctx ctx)
    {
        switch (b.Kind)
        {
            case BlockKind.Number:
                if (b is not NumberBlock num) return BrokenValue(b, ctx, "number block is broken");
                if (!long.TryParse(num.Text, out long n))
                {
                    ctx.Error(b, "number field is empty or invalid");
                    return new Literal(0);
                }
                return new Literal(n);

            case BlockKind.Boolean:
                return b is BooleanBlock flag
                    ? new LiteralBool(flag.Value)
                    : BrokenValue(b, ctx, "boolean block is broken");

            case BlockKind.Variable:
                {
                    if (b is not VariableBlock vb) return BrokenValue(b, ctx, "variable block is broken");
                    string? name = vb.Name;
                    if (name == null)
                    {
                        ctx.Error(b, "no variable chosen");
                        return new Literal(0);
                    }
                    if (!ctx.Registry.Contains(name))
                    {
                        ctx.Error(b, $"unknown variable {name}");
                        return new Literal(0);
                    }
                    // The cell kinds are read-only constants (empty=0, wall=1, gold=2),
                    // not variables the VM ever seeds. Emit the value they stand for so
                    // `isThere wall` is exactly `isThere 1` in MineCode.
                    return ctx.Registry.IsBuiltin(name)
                        ? new Literal(ctx.Registry.GetBuiltinValue(name))
                        : new Variable(name);
                }

            // The cell blocks in the Sensing palette: each stands for its literal
            // cell code, so `isThere wall` is exactly `isThere 1`.
            case BlockKind.Empty:
            case BlockKind.Wall:
            case BlockKind.Gold:
                return new Literal(ctx.Registry.GetBuiltinValue(VariableRegistry.CellName(b.Kind)));

            case BlockKind.IsThere:
                return new IsThere(AsSimple(ValueOf(b, 0, ctx), b, ctx));
            case BlockKind.Not:
                return new Not(AsSimple(ValueOf(b, 0, ctx), b, ctx));

            case BlockKind.Add: return Bin(b, BinOp.Operation.Add, ctx);
            case BlockKind.Sub: return Bin(b, BinOp.Operation.Sub, ctx);
            case BlockKind.Mul: return Bin(b, BinOp.Operation.Mul, ctx);
            case BlockKind.Eq: return Bin(b, BinOp.Operation.Eq, ctx);
            case BlockKind.Neq: return Bin(b, BinOp.Operation.Neq, ctx);
            case BlockKind.Lt: return Bin(b, BinOp.Operation.Lt, ctx);
            case BlockKind.Leq: return Bin(b, BinOp.Operation.Leq, ctx);
            case BlockKind.Gt: return Bin(b, BinOp.Operation.Gt, ctx);
            case BlockKind.Geq: return Bin(b, BinOp.Operation.Geq, ctx);
            case BlockKind.And: return Bin(b, BinOp.Operation.And, ctx);
            case BlockKind.Or: return Bin(b, BinOp.Operation.Or, ctx);

            default:
                ctx.Error(b, "this block doesn't produce a value");
                return new Literal(0);
        }
    }

    static BinOp Bin(Block b, BinOp.Operation op, Ctx ctx)
        => new(AsSimple(ValueOf(b, 0, ctx), b, ctx), AsSimple(ValueOf(b, 1, ctx), b, ctx), op);

    /// <summary>A Kind that does not match the block class: badge it, never throw.</summary>
    static Instruction? BrokenStatement(Block b, Ctx ctx, string message)
    {
        ctx.Error(b, message);
        return null;
    }

    static Value BrokenValue(Block b, Ctx ctx, string message)
    {
        ctx.Error(b, message);
        return new Literal(0);
    }

    /// <summary>
    /// The MineCode grammar only allows atoms inside operators and next to isThere/not
    /// (see `binExpression: atom OP atom`), so a nested expression there gets a badge
    /// instead of silently producing something the backend can't emit.
    /// </summary>
    static SimpleValue AsSimple(Value v, Block at, Ctx ctx)
    {
        if (v is SimpleValue s) return s;
        ctx.Error(at, "an expression can't be nested here");
        return new Literal(0);
    }
}
