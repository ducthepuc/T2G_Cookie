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
/// The block catalogue. Every block maps to a language construct in the Backend AST.
/// </summary>
static class BlockLibrary
{
    public static List<BlockCategory> CreateCategories()
    {
        var motion = new BlockCategory("Motion", Theme.Rgb(76, 151, 255), Theme.Rgb(51, 115, 204));
        var looks = new BlockCategory("Looks", Theme.Rgb(153, 102, 255), Theme.Rgb(119, 77, 203));
        var control = new BlockCategory("Control", Theme.Rgb(255, 140, 26), Theme.Rgb(207, 111, 21));
        var variables = new BlockCategory("Variables", Theme.Rgb(232, 91, 91), Theme.Rgb(190, 60, 60));
        var operators = new BlockCategory("Operators", Theme.Rgb(76, 201, 124), Theme.Rgb(51, 151, 84));
        var sensing = new BlockCategory("Sensing", Theme.Rgb(102, 178, 255), Theme.Rgb(68, 139, 204));

        motion
            .Add(() => new StackBlock("set forward <>", motion, Pos.Start, Pos.Start))
            .Add(() => new StackBlock("turn by <>", motion, Pos.Start, Pos.Start))
            .Add(() => new StackBlock("mine here", motion, Pos.Start, Pos.Start));

        looks
            .Add(() => new StackBlock("print <>", looks, Pos.Start, Pos.Start));

        control
            .Add(() => new CBlock("if <> then", control, Pos.Start, Pos.Start))
            .Add(() => new CBlock("if <> then <> else", control, Pos.Start, Pos.Start))
            .Add(() => new CBlock("while <> do", control, Pos.Start, Pos.Start, hasNext: false));

        variables
            .Add(() => new StackBlock("set <> to <>", variables, Pos.Start, Pos.Start));

        operators
            .Add(() => new ReporterBlock("<> + <>", operators, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("<> - <>", operators, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("<> * <>", operators, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("<> = <>", operators, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("<> <> <>", operators, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("<> > <>", operators, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("<> <= <>", operators, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("<> >= <>", operators, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("<> and <>", operators, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("<> or <>", operators, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("not <>", operators, Pos.Start, Pos.Start));

        sensing
            .Add(() => new ReporterBlock("is there <>", sensing, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("true", sensing, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("false", sensing, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("<number>", sensing, Pos.Start, Pos.Start))
            .Add(() => new ReporterBlock("<variable>", sensing, Pos.Start, Pos.Start));

        return new List<BlockCategory> { motion, looks, control, variables, operators, sensing };
    }
}
