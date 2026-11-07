using Raylib_CsLo;

namespace T2G_Cookie.Gui;

/// <summary>A colored group of blocks in the toolbox. Holds factories, not instances.</summary>
sealed class BlockCategory
{
    readonly List<Func<Block>> _factories = new();

    public string Name { get; }
    public Color Color { get; }
    public Color DarkColor { get; }
    public IReadOnlyList<Func<Block>> Factories => _factories;

    public BlockCategory(string name, Color color, Color darkColor)
    {
        Name = name;
        Color = color;
        DarkColor = darkColor;
    }

    public BlockCategory Add(Func<Block> factory)
    {
        _factories.Add(factory);
        return this;
    }
}

/// <summary>
/// The block catalogue. Every block maps to a language construct in the Backend AST
/// (see BlockKind) and every &lt;&gt; placeholder is a real Slot the user drops reporters into.
/// </summary>
static class BlockLibrary
{
    public static List<BlockCategory> CreateCategories()
    {
        var motion = new BlockCategory("Motion", Theme.Rgb(76, 151, 255), Theme.Rgb(51, 115, 204));
        var output = new BlockCategory("Output", Theme.Rgb(153, 102, 255), Theme.Rgb(119, 77, 203));
        var control = new BlockCategory("Control", Theme.Rgb(255, 140, 26), Theme.Rgb(207, 111, 21));
        var variables = new BlockCategory("Variables", Theme.Rgb(232, 91, 91), Theme.Rgb(190, 60, 60));
        var values = new BlockCategory("Value", Theme.Rgb(14, 152, 140), Theme.Rgb(9, 116, 108));
        var operators = new BlockCategory("Operators", Theme.Rgb(76, 201, 124), Theme.Rgb(51, 151, 84));
        var sensing = new BlockCategory("Sensing", Theme.Rgb(102, 178, 255), Theme.Rgb(68, 139, 204));

        motion
            // Label matches the lexer keyword (STEP: 'step forward'), so what the block
            // shows is what MineCode actually accepts.
            .Add(() => new StackBlock([Seg.T("step forward "), Seg.V()], motion, Pos.Start, Pos.Start) { Kind = BlockKind.Step })
            .Add(() => new StackBlock([Seg.T("turn by "), Seg.V()], motion, Pos.Start, Pos.Start) { Kind = BlockKind.Turn })
            .Add(() => new StackBlock([Seg.T("mine here")], motion, Pos.Start, Pos.Start) { Kind = BlockKind.Mine });

        output
            .Add(() => new StackBlock([Seg.T("print "), Seg.V()], output, Pos.Start, Pos.Start) { Kind = BlockKind.Print });

        control
            // The green-flag hat: exactly one may exist (the toolbox greys it out while one does).
            .Add(() => new HatBlock([Seg.T("Start clicked")], control, Pos.Start, Pos.Start) { Kind = BlockKind.Start })
            .Add(() => new CBlock([Seg.T("if "), Seg.V(), Seg.T(" then")], control, Pos.Start, Pos.Start) { Kind = BlockKind.If })
            .Add(() => new CBlock([Seg.T("if "), Seg.V(), Seg.T(" then")], control, Pos.Start, Pos.Start, hasElse: true) { Kind = BlockKind.If })
            .Add(() => new CBlock([Seg.T("while "), Seg.V(), Seg.T(" do")], control, Pos.Start, Pos.Start) { Kind = BlockKind.While });

        variables
            .Add(() => new StackBlock([Seg.T("set "), Seg.N(), Seg.T(" to "), Seg.V()], variables, Pos.Start, Pos.Start) { Kind = BlockKind.Set })
            .Add(() => new VariableBlock(variables, Pos.Start, Pos.Start));

        // The literal atoms of the grammar (NUMBER, TRUE, FALSE): the two you can type
        // or toggle into any field. They live apart from `is there` because sensing is a
        // function call, not a value.
        values
            .Add(() => new NumberBlock(values, Pos.Start, Pos.Start))
            .Add(() => new BooleanBlock(values, Pos.Start, Pos.Start) { Value = true })
            .Add(() => new BooleanBlock(values, Pos.Start, Pos.Start) { Value = false });

        operators
            .Add(() => new ReporterBlock([Seg.V(), Seg.T(" + "), Seg.V()], operators, Pos.Start, Pos.Start) { Kind = BlockKind.Add })
            .Add(() => new ReporterBlock([Seg.V(), Seg.T(" - "), Seg.V()], operators, Pos.Start, Pos.Start) { Kind = BlockKind.Sub })
            .Add(() => new ReporterBlock([Seg.V(), Seg.T(" * "), Seg.V()], operators, Pos.Start, Pos.Start) { Kind = BlockKind.Mul })
            .Add(() => new ReporterBlock([Seg.V(), Seg.T(" = "), Seg.V()], operators, Pos.Start, Pos.Start) { Kind = BlockKind.Eq })
            .Add(() => new ReporterBlock([Seg.V(), Seg.T(" <> "), Seg.V()], operators, Pos.Start, Pos.Start) { Kind = BlockKind.Neq })
            .Add(() => new ReporterBlock([Seg.V(), Seg.T(" < "), Seg.V()], operators, Pos.Start, Pos.Start) { Kind = BlockKind.Lt })
            .Add(() => new ReporterBlock([Seg.V(), Seg.T(" > "), Seg.V()], operators, Pos.Start, Pos.Start) { Kind = BlockKind.Gt })
            .Add(() => new ReporterBlock([Seg.V(), Seg.T(" <= "), Seg.V()], operators, Pos.Start, Pos.Start) { Kind = BlockKind.Leq })
            .Add(() => new ReporterBlock([Seg.V(), Seg.T(" >= "), Seg.V()], operators, Pos.Start, Pos.Start) { Kind = BlockKind.Geq })
            .Add(() => new ReporterBlock([Seg.V(), Seg.T(" and "), Seg.V()], operators, Pos.Start, Pos.Start) { Kind = BlockKind.And })
            .Add(() => new ReporterBlock([Seg.V(), Seg.T(" or "), Seg.V()], operators, Pos.Start, Pos.Start) { Kind = BlockKind.Or })
            .Add(() => new ReporterBlock([Seg.T("not "), Seg.V()], operators, Pos.Start, Pos.Start) { Kind = BlockKind.Not });

        sensing
            .Add(() => new ReporterBlock([Seg.T("is there "), Seg.V()], sensing, Pos.Start, Pos.Start) { Kind = BlockKind.IsThere });

        return new List<BlockCategory> { motion, output, control, variables, values, operators, sensing };
    }
}
