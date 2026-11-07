using System.Numerics;
using System.Reflection;
using Raylib_CsLo;
using T2G_Cookie.Gui;

namespace GuiSelfTest;

/// <summary>
/// Drives the real Workspace/Block code in a hidden window and checks the structural
/// invariants after every operation. Reproduces the reported crash first, then fuzzes.
/// </summary>
static class SelfTest
{
    sealed class FirstFailure : Exception { }

    static int _failures;
    static int _checks;
    static string _context = "";
    static bool _dumped;

    static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.FLAG_WINDOW_HIDDEN);
        Raylib.InitWindow(1000, 650, "gui self test");

        try
        {
            Run("Above drop out of a C-block mouth", TestAboveOutOfMouth);
            Run("Hat block snapping", TestHatSnap);
            Run("Dropping straight from the toolbox", TestToolboxDropSnap);
            Run("Dropping outside the visible area", TestClamp);
            Run("Fuzz", TestFuzz);
        }
        catch (FirstFailure)
        {
            Console.WriteLine();
            Console.WriteLine("stopping at the first failure");
        }

        Raylib.CloseWindow();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? $"ALL PASS ({_checks} checks)"
            : $"{_failures} FAILED out of {_checks} checks");
        Environment.Exit(_failures == 0 ? 0 : 1);
    }

    static void Run(string name, Action test)
    {
        Console.WriteLine("-- " + name + " --");
        try
        {
            test();
        }
        catch (FirstFailure)
        {
            throw;
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine("FAIL  threw " + ex.GetType().Name + ": " + ex.Message);
        }
        Console.WriteLine();
    }

    static void Check(bool ok, string what)
    {
        _checks++;
        if (ok) return;
        Fail("check", what);
    }

    static void Fail(string stage, string what)
    {
        _failures++;
        Console.WriteLine($"FAIL  [{stage}] {what}");
        if (!_dumped)
        {
            _dumped = true;
            Console.WriteLine("  while: " + _context);
            DumpWorkspaces();
        }
        throw new FirstFailure();
    }

    static readonly List<Workspace> _workspaces = new();

    static void DumpWorkspaces()
    {
        foreach (Workspace ws in _workspaces)
        {
            Console.WriteLine("  workspace:");
            int i = 0;
            foreach (Block root in Roots(ws))
            {
                Console.Write($"    root[{i++}]: ");
                DumpChain(root);
                Console.WriteLine();
            }

            object? drag = typeof(Workspace)
                .GetField("_drag", BindingFlags.Instance | BindingFlags.NonPublic)?
                .GetValue(ws);
            if (drag != null)
            {
                Block dragged = (Block)drag.GetType().GetProperty("Root")!.GetValue(drag)!;
                Console.Write("    dragging: ");
                DumpChain(dragged);
                Console.WriteLine();
            }
        }
    }

    static void DumpChain(Block root)
    {
        var seen = new HashSet<Block>();
        var parts = new List<string>();

        void Walk(Block? b, int depth)
        {
            while (b != null)
            {
                if (!seen.Add(b)) { parts.Add("!!CYCLE->" + b.Label); return; }

                string prev = b.Previous == null ? "-" : b.Previous.Label + (ReferenceEquals(b.Previous.Next, b) ? "" : "!BAD");
                string ctr = b.Container == null ? "" : "@" + b.Container.Label +
                    (ReferenceEquals(b.Container.Inner, b) ? "" : "!BAD");
                parts.Add($"{b.Label}@({b.X},{b.Y})p={prev}{ctr}");

                if (b is CBlock c && c.Inner != null)
                {
                    parts.Add("{");
                    Walk(c.Inner, depth + 1);
                    parts.Add("}");
                }
                b = b.Next;
            }
        }

        Walk(root, 0);
        Console.Write(string.Join(" ", parts));
    }

    static List<Block> Roots(Workspace ws) =>
        (List<Block>)typeof(Workspace)
            .GetField("_roots", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ws)!;

    static BlockCategory Cat() => new("t", Theme.Rgb(10, 20, 30), Theme.Rgb(5, 10, 15));

    static Workspace NewWorkspace()
    {
        Frame root = new(Pos.Start, Pos.Start, 100, 100);
        Workspace ws = new(Pos.Start, Pos.Start, 75, 100);
        root.AddChild(ws);
        Element.UpdateAll();
        _workspaces.Add(ws);
        Check(ws.Width > 0 && ws.Height > 0, "workspace got a real size");
        return ws;
    }

    /// <summary>
    /// The structure has to stay a forest: no block in two stacks at once, no cycle,
    /// and every back-pointer pointing at the block that claims it.
    /// <paramref name="extra"/> covers chains that are in flight (a block just grabbed
    /// and not yet dropped), which no root reaches yet.
    /// </summary>
    static void Healthy(Workspace ws, string stage, params Block[] extra)
    {
        var owned = new HashSet<Block>();

        foreach (Block root in Roots(ws).Concat(extra))
        {
            if (root.Previous != null || root.Container != null)
                Fail(stage, "a root is still attached to something");

            var reached = new HashSet<Block>();
            var stack = new Stack<Block>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                Block b = stack.Pop();
                if (!reached.Add(b))
                {
                    Fail(stage, "cycle or the same block reachable twice in one stack");
                    break;
                }

                if (b.Next != null) stack.Push(b.Next);
                if (b is CBlock c && c.Inner != null) stack.Push(c.Inner);

                if (b.Previous != null && !ReferenceEquals(b.Previous.Next, b))
                    Fail(stage, "Previous back-pointer does not match Next");

                if (b.Container != null)
                {
                    if (b.Previous != null)
                        Fail(stage, "block has both Previous and Container");
                    if (!ReferenceEquals(b.Container.Inner, b))
                        Fail(stage, "stale Container back-pointer");
                }

                if (b is CBlock cc && cc.Inner != null)
                {
                    if (cc.Inner.Previous != null)
                        Fail(stage, "inner head has a Previous");
                    if (!ReferenceEquals(cc.Inner.Container, cc))
                        Fail(stage, "inner head points at the wrong container");
                }
            }

            foreach (Block b in reached)
                if (!owned.Add(b))
                    Fail(stage, "a block lives in two stacks at once");
        }
    }

    // ---------------------------------------------------------------- tests --

    static void TestAboveOutOfMouth()
    {
        Workspace ws = NewWorkspace();
        BlockCategory cat = Cat();

        CBlock c = new("if <> then", cat, Pos.Start, Pos.Start);
        StackBlock inner = new("inner", cat, Pos.Start, Pos.Start);
        Check(c.AttachInside(inner), "inner block placed in the mouth");

        ws.AddRootBlock(c, new Vector2(100, 100));
        Check(Roots(ws).Count == 1 && ReferenceEquals(Roots(ws)[0], c), "c-block became a root");
        Healthy(ws, "setup");

        // Line a free block up with the top of the mouth: this is the "Above" snap
        // onto a block whose Previous == null but whose Container != null.
        StackBlock d = new("drag", cat, Pos.Start, Pos.Start);
        d.Layout(inner.X, inner.Y - d.Height);

        SnapTarget? snap = ws.FindSnap(d);
        Check(snap != null && snap.Kind == SnapKind.Above && ReferenceEquals(snap.Target, inner),
            "finds the Above snap onto the mouth head");

        ws.Drop(d, snap!);
        Healthy(ws, "after Above drop");
        Check(c.Inner == null, "emptied mouth no longer points at the block");
        Check(inner.Container == null, "Container back-pointer cleared with it");
        Check(ReferenceEquals(d.Next, inner) && inner.Previous == d, "block moved into the dragged chain");
        Check(Roots(ws).Count == 2 && Roots(ws).Contains(c) && Roots(ws).Contains(d),
            "c-block and dragged chain are two separate roots");

        // Grab that block back and drop it into the same mouth again. Before the fix
        // this ended with inner.Next == inner and Layout() never returning.
        Vector2 mouse = new(inner.X + 3, inner.Y + 3);
        Block? grabbed = ws.HandlePress(mouse, out _);
        Check(ReferenceEquals(grabbed, inner), "grabbed the block back");

        SnapTarget? snap2 = ws.FindSnap(grabbed!);
        Check(snap2 != null && snap2.Kind == SnapKind.Inside, "snaps back into the mouth");

        ws.Drop(grabbed!, snap2);
        Healthy(ws, "after re-inserting into the mouth");
        Check(ReferenceEquals(c.Inner, inner) && inner.Container == c, "block lives in the mouth again");
        Check(Roots(ws).Count == 2 && Roots(ws).Contains(c) && Roots(ws).Contains(d),
            "the emptied drag is still its own root");

        // And the direct call that used to knot the list.
        Check(!c.AttachInside(inner), "re-attaching a block that is already inside is refused");
        Check(!c.AttachInside(c), "attaching a c-block inside itself is refused");
        Healthy(ws, "after refused attaches");
    }

    static void TestHatSnap()
    {
        Workspace ws = NewWorkspace();
        BlockCategory cat = Cat();

        StackBlock stack = new("move", cat, Pos.Start, Pos.Start);
        ws.AddRootBlock(stack, new Vector2(500, 400));
        Check(Roots(ws).Count == 1, "stack added");

        HatBlock hat = new("when started", cat, Pos.Start, Pos.Start);
        hat.Layout(stack.X, stack.Y - hat.Height); // hat's bottom sits on the stack's top

        SnapTarget? snap = ws.FindSnap(hat);
        Check(snap != null && snap.Kind == SnapKind.Above && ReferenceEquals(snap.Target, stack),
            "a hat (no previous connection) still snaps on top of a stack");

        ws.Drop(hat, snap!);
        Healthy(ws, "after hat drop");
        Check(Roots(ws).Count == 1 && ReferenceEquals(Roots(ws)[0], hat), "hat became the root");
        Check(ReferenceEquals(hat.Next, stack) && stack.Previous == hat, "stack hangs under the hat");
    }

    static void TestToolboxDropSnap()
    {
        Workspace ws = NewWorkspace();
        BlockCategory cat = Cat();

        StackBlock a = new("a", cat, Pos.Start, Pos.Start);
        ws.AddRootBlock(a, new Vector2(300, 300));

        StackBlock b = new("b", cat, Pos.Start, Pos.Start);
        ws.AddRootBlock(b, new Vector2(300, 300 + a.Height + 4));

        Check(ReferenceEquals(a.Next, b) && b.Previous == a, "toolbox drop snapped below the stack");
        Check(Roots(ws).Count == 1, "still a single stack");
        Healthy(ws, "after toolbox drops");

        // A reporter has no connections at all, it must stay free floating.
        ReporterBlock r = new("<> + <>", cat, Pos.Start, Pos.Start);
        ws.AddRootBlock(r, new Vector2(600, 500));
        Check(r.Previous == null && r.Next == null && Roots(ws).Contains(r), "reporter stays a root");
        Healthy(ws, "after reporter drop");
    }

    static void TestClamp()
    {
        Workspace ws = NewWorkspace();
        BlockCategory cat = Cat();

        StackBlock far = new("far", cat, Pos.Start, Pos.Start);
        ws.AddRootBlock(far, new Vector2(5000, 5000));
        Check(far.X >= 8 && far.X + far.Width <= ws.Width, "far right drop stays inside");
        Check(far.Y >= 8 && far.Y + far.Height <= ws.Height, "far below drop stays inside");
        Healthy(ws, "after clamped drop");

        StackBlock off = new("off", cat, Pos.Start, Pos.Start);
        ws.AddRootBlock(off, new Vector2(-500, -500));
        Check(off.X >= 8 && off.Y >= 8, "negative drop clamped to the near edge");
        Healthy(ws, "after negative drop");
    }

    static void TestFuzz()
    {
        foreach (int seed in new[] { 20261107, 1, 99991, 424242 })
            FuzzOne(seed, 8000);
        Check(true, "fuzz finished without hanging or throwing");
    }

    static void FuzzOne(int seed, int iterations)
    {
        Console.WriteLine($"  seed {seed}, {iterations} iterations");
        _context = $"seed {seed}";

        Workspace ws = NewWorkspace();
        BlockCategory cat = Cat();
        var rng = new Random(seed);

        for (int k = 0; k < 6; k++)
            ws.AddRootBlock(new StackBlock("seed" + k, cat, Pos.Start, Pos.Start),
                new Vector2(rng.Next(40, 600), rng.Next(40, 500)));

        for (int it = 0; it < iterations; it++)
        {
            ws.Update(0, 0); // refresh layout, hover and screen offsets
            List<Block> roots = Roots(ws);
            var all = roots.SelectMany(r => r.CollectBlocks()).ToList();

            if (all.Count > 150)
            {
                Block doomed = all[rng.Next(all.Count)];
                _context = $"trim {doomed.Label}";
                ws.RemoveBlock(doomed);
                Healthy(ws, "fuzz/after-trim");
            }

            int action = rng.Next(100);
            all = Roots(ws).SelectMany(r => r.CollectBlocks()).ToList();
            if (all.Count == 0)
            {
                ws.AddRootBlock(new StackBlock("refill", cat, Pos.Start, Pos.Start),
                    new Vector2(rng.Next(40, 600), rng.Next(40, 500)));
                Healthy(ws, "fuzz/refill");
                continue;
            }

            if (action < 55)
            {
                // Grab a block and drop it lined up with a connection, so snaps fire.
                Block target = all[rng.Next(all.Count)];
                Block? grabbed = ws.HandlePress(new Vector2(target.X + 3, target.Y + 3), out _);
                if (grabbed == null) continue;

                _context = $"grab {target.Label}";
                Healthy(ws, "fuzz/after-grab", grabbed);

                Block other = all[rng.Next(all.Count)];
                Vector2 point = rng.Next(3) switch
                {
                    0 => other.NextConnection,
                    1 => other.PreviousConnection,
                    _ => other is CBlock cb ? cb.InnerConnection : other.NextConnection,
                };
                grabbed.Layout((int)point.X, (int)point.Y);

                SnapTarget? snap = ws.FindSnap(grabbed);
                _context = $"snap-drop grabbed={grabbed.Label} at ({grabbed.X},{grabbed.Y}) " +
                           $"target={(snap == null ? "none" : snap.Kind + " " + snap.Target.Label)} " +
                           $"point={(snap == null ? "-" : snap.Position.ToString())}";
                ws.Drop(grabbed, snap);
                Healthy(ws, "fuzz/after-snap-drop");
            }
            else if (action < 75)
            {
                // Grab and drop somewhere arbitrary.
                Block target = all[rng.Next(all.Count)];
                Block? grabbed = ws.HandlePress(new Vector2(target.X + 3, target.Y + 3), out _);
                if (grabbed == null) continue;

                grabbed.Layout(rng.Next(0, 900), rng.Next(0, 700));
                _context = $"free-drop {grabbed.Label} (grab was {target.Label})";
                ws.Drop(grabbed, null);
                Healthy(ws, "fuzz/after-free-drop");
            }
            else if (action < 90)
            {
                _context = "add from toolbox";
                ws.AddRootBlock(new StackBlock("new" + it, cat, Pos.Start, Pos.Start),
                    new Vector2(rng.Next(-100, 1100), rng.Next(-100, 750)));
                Healthy(ws, "fuzz/after-add");
            }
            else
            {
                Block doomed = all[rng.Next(all.Count)];
                _context = $"remove {doomed.Label}";
                ws.RemoveBlock(doomed);
                Healthy(ws, "fuzz/after-remove");
            }
        }

        Console.WriteLine($"  seed {seed}: ok");
    }
}
