using System.Numerics;
using System.Reflection;
using Raylib_CsLo;
using Shared;
using T2G_Cookie.Backend.Parsing;
using T2G_Cookie.Gui;
using T2G_Cookie.Main;
using T2G_Cookie.Shared;
using T2G_Cookie.Shared.Ast;
using T2G_Cookie.Shared.Values;

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

    /// <summary>The target script in its canonical MineCode form: what the backend
    /// parser reads, what the writer emits, and what the loader has to rebuild.</summary>
    const string TargetSource =
"""
set arany to isThere 2
print arany
set count to 0
set fal to isThere 1
set megy to not fal
while megy
    set arany to isThere 2
    if arany
        step forward 1
        mine here
        set count to count + 1
    else
        step forward 1
        set fal to isThere 1
        set megy to not fal
print count
""";

    static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.FLAG_WINDOW_HIDDEN);
        Raylib.InitWindow(1000, 650, "gui self test");

        try
        {
            Run("Above drop out of a C-block mouth", TestAboveOutOfMouth);
            Run("Hat block snapping", TestHatSnap);
            Run("Dropping straight from the toolbox", TestToolboxDropSnap);
            Run("Robot control under the map", TestRobotControl);
            Run("Reset Map restores the original map", TestResetMap);
            Run("Dropping outside the visible area", TestClamp);
            Run("Slot attach / detach / refusals", TestSlotBasics);
            Run("Snapping a reporter into a field", TestIntoSlotSnap);
            Run("The else mouth", TestElseMouth);
            Run("Vocabulary & Start singleton", TestVocabulary);
            Run("Number / boolean / variable click editing", TestClickEditing);
            Run("Click vs drag threshold", TestPressClickVsDrag);
            Run("Variable dropdown and name prompt", TestDropdowns);
            Run("Deleting variables from the dropdown", TestDeleteVariables);
            Run("Golden AST for the target script", TestGoldenAst);
            Run("Loading a parsed file into blocks", TestLoadRoundTrip);
            Run("Saving the script back to the input file", TestSave);
            Run("Palette covers the whole language", TestPaletteParity);
            Run("Every operator maps to the backend AST", TestOperatorParity);
            Run("Variable names", TestVariableNames);
            Run("Value fields follow the grammar", TestValueSlots);
            Run("Nesting limit", TestNestingLimit);
            Run("Inserting before a block", TestInsertBefore);
            Run("The live run session", TestRunSession);
            Run("Literal reporters map and render", TestLiteralParity);
            Run("Empty C-block mouths are valid", TestEmptyMouths);
            Run("Workspace and toolbox render", TestDrawSmoke);
            Run("Validation badges", TestValidation);
            Run("Running the script on the Backend VM", TestVmRun);
            Run("Print values land in the output field", TestPrintOutput);
            Run("Start turns into Stop and reverts the map", TestStopButton);
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
                    (ReferenceEquals(b.Container.Inner, b) ? "" :
                     ReferenceEquals(b.Container.Inner2, b) ? "2" : "!BAD");
                string fields = b.Slots.Count == 0 ? "" :
                    "[" + string.Join(",", b.Slots.Select(s => s.Content?.Label ?? "_")) + "]";
                parts.Add($"{b.Label}{fields}@({b.X},{b.Y})p={prev}{ctr}");

                foreach (Slot slot in b.Slots)
                {
                    if (slot.Content == null) continue;
                    parts.Add("<");
                    Walk(slot.Content, depth + 1);
                    parts.Add(">");
                }

                if (b is CBlock c)
                {
                    if (c.Inner != null)
                    {
                        parts.Add("{");
                        Walk(c.Inner, depth + 1);
                        parts.Add("}");
                    }
                    if (c.Inner2 != null)
                    {
                        parts.Add("{2");
                        Walk(c.Inner2, depth + 1);
                        parts.Add("}");
                    }
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
            if (root.Previous != null || root.Container != null || root.Slot != null)
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
                if (b is CBlock c)
                {
                    if (c.Inner != null) stack.Push(c.Inner);
                    if (c.Inner2 != null) stack.Push(c.Inner2);
                }

                // A parked reporter belongs to its host's structure: walk it too, and
                // check the back-pointers that keep the field and its content in sync.
                foreach (Slot slot in b.Slots)
                {
                    if (!ReferenceEquals(slot.Host, b))
                        Fail(stage, "slot does not point back at its host");

                    if (slot.Content is { } content)
                    {
                        if (!ReferenceEquals(content.Slot, slot))
                            Fail(stage, "parked reporter does not point back at its slot");
                        if (content.Previous != null || content.Container != null || content.Next != null)
                            Fail(stage, "a parked reporter is also linked into a chain");
                        stack.Push(content);
                    }
                }

                if (b.Previous != null && !ReferenceEquals(b.Previous.Next, b))
                    Fail(stage, "Previous back-pointer does not match Next");

                if (b.Container != null)
                {
                    if (b.Previous != null)
                        Fail(stage, "block has both Previous and Container");
                    if (!ReferenceEquals(b.Container.Inner, b) && !ReferenceEquals(b.Container.Inner2, b))
                        Fail(stage, "stale Container back-pointer");
                }

                if (b is CBlock cc)
                {
                    if (cc.Inner != null)
                    {
                        if (cc.Inner.Previous != null)
                            Fail(stage, "inner head has a Previous");
                        if (!ReferenceEquals(cc.Inner.Container, cc))
                            Fail(stage, "inner head points at the wrong container");
                    }
                    if (cc.Inner2 != null)
                    {
                        if (!cc.HasElse)
                            Fail(stage, "a block with no else has a second mouth");
                        if (cc.Inner2.Previous != null)
                            Fail(stage, "else head has a Previous");
                        if (!ReferenceEquals(cc.Inner2.Container, cc))
                            Fail(stage, "else head points at the wrong container");
                    }
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
        Check(ReferenceEquals(c.Inner, d) && d.Container == c, "the dragged block took the head of the mouth");
        Check(ReferenceEquals(d.Next, inner) && inner.Previous == d && inner.Container == null,
            "the old head moved down one, still in the mouth");
        Check(Roots(ws).Count == 1 && Roots(ws).Contains(c), "nothing left the c-block");

        // Grab that block back and drop it into the same mouth again. Before the fix
        // this ended with inner.Next == inner and Layout() never returning.
        ws.LayoutAll();
        Vector2 mouse = new(inner.X + 3, inner.Y + 3);
        Block? grabbed = ws.HandlePress(mouse, out _);
        Check(ReferenceEquals(grabbed, inner), "grabbed the block back");

        SnapTarget? snap2 = ws.FindSnap(grabbed!);
        Check(snap2 != null && snap2.Kind == SnapKind.Below && ReferenceEquals(snap2.Target, d),
            "snaps back under the new head of the mouth");

        ws.Drop(grabbed!, snap2);
        Healthy(ws, "after re-inserting into the mouth");
        Check(ReferenceEquals(c.Inner, d) && ReferenceEquals(d.Next, inner), "block lives in the mouth again");
        Check(Roots(ws).Count == 1 && Roots(ws).Contains(c), "the c-block is still the only root");

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

        // The grab offset the toolbox carries: the release must land where the block
        // was drawn - without the offset the top-left jumps onto the cursor and the
        // connection the user aimed at ends up outside the snap radius.
        Vector2 grab = new(70, 12);
        Vector2 aim = b.NextConnection + grab;

        StackBlock c = new("c", cat, Pos.Start, Pos.Start);
        SnapTarget? preview = ws.FindSnapForScreen(c, aim, grab);
        Check(preview is { Kind: SnapKind.Below, Target: var below } && ReferenceEquals(below, b),
            "the toolbox drag previews the snap under the cursor");
        Check(c.X == (int)(aim.X - grab.X) && c.Y == (int)(aim.Y - grab.Y),
            "the preview leaves the flying block in screen coordinates");

        ws.AddRootBlock(c, aim, grab);
        Check(ReferenceEquals(b.Next, c) && c.Previous == b,
            "releasing with the grab offset lands on the previewed connection");
        Healthy(ws, "after an offset toolbox drop");

        // The same release without an offset puts the block's top-left on the cursor,
        // 70px past the connection, and misses by design.
        StackBlock d = new("d", cat, Pos.Start, Pos.Start);
        ws.AddRootBlock(d, aim);
        Check(d.Previous == null && Roots(ws).Contains(d),
            "the plain top-left drop misses outside the snap radius");
        Healthy(ws, "after a missed drop");
    }

    /// <summary>The checkbox/button stack under the map: layout, the Start gate, and
    /// the robot walking (or refusing to walk) through MapHandler.</summary>
    static void TestRobotControl()
    {
        Workspace ws = NewWorkspace();

        // '>' robot at (0,0) facing right with a wall beside it; gold at (0,1).
        ws.LoadMap(new MapHandler(new char[,]
        {
            { '>', '#', '.', '.' },
            { '@', '.', '.', '.' },
            { '.', '.', '.', '.' },
        }));

        // One draw pass so the panels take their real heights (MapField sizes in Draw).
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Raylib.WHITE);
        ws.Draw(0, 0);
        Raylib.EndDrawing();

        Check(ws.Sensor.Y == ws.MapPanel.Y + ws.MapPanel.Height + Theme.RobotPanelGap,
            "the sensor sits under the map panel");
        Check(ws.RobotToggle.Y == ws.Sensor.Y + ws.Sensor.Height + Theme.RobotPanelGap,
            "the checkbox frame sits under the sensor");
        Check(ws.RobotButtons.Y == ws.RobotToggle.Y + ws.RobotToggle.Height + Theme.RobotPanelGap,
            "the button frame sits under the checkbox");
        Check(ws.RobotToggle.Width == ws.MapPanel.Width && ws.RobotButtons.Width == ws.MapPanel.Width,
            "both robot-control frames are as wide as the map");
        Check(ws.ResetButton.Bounds.X + ws.ResetButton.Width <= ws.SaveButton.Bounds.X,
            "the reset button sits left of Save");
        Check(Raylib.MeasureText("Reset Gold", 14) <= ws.ResetButton.Width - 8,
            "the Reset Gold label fits its button");

        MapHandler? map = ws.MapPanel.Map;
        Check(!ws.RobotControl && map != null, "control starts unchecked with the map loaded");
        Check(map!.TotalGold == 1 && map.GoldMined == 0, "the map counts one gold, none mined yet");

        Rectangle forwardRect = ws.RobotButtons.ButtonBounds(2);
        Vector2 forward = new(forwardRect.X + forwardRect.width / 2,
                              forwardRect.Y + forwardRect.height / 2);
        Rectangle turnLeftRect = ws.RobotButtons.ButtonBounds(0);
        Vector2 turnLeft = new(turnLeftRect.X + turnLeftRect.width / 2,
                               turnLeftRect.Y + turnLeftRect.height / 2);
        Rectangle mineRect = ws.RobotButtons.ButtonBounds(3);
        Vector2 mine = new(mineRect.X + mineRect.width / 2, mineRect.Y + mineRect.height / 2);

        // Unchecked: the buttons are dead and Start belongs to the code.
        ws.RobotButtons.Click(forward);
        Check(map!.IdentifyRobot(out _) is { X: 0, Y: 0 },
            "an unchecked click does not move the robot");
        ws.Update(0, 0);
        Check(!ws.StartButton.Disabled, "Start is live while control is off");
        Check(ws.Output.GoldMined == 0 && ws.Output.TotalGold == 1,
            "the output gold row follows the map");

        // Checked: the run moves to the buttons and Start goes out.
        ws.RobotToggle.Click();
        Check(ws.RobotControl, "the checkbox turns control on");
        ws.Update(0, 0);
        Check(ws.StartButton.Disabled, "checking the box disables Start");

        // MINE off the gold (the robot is on plain empty here): refused, like the
        // Mine block returning E_NO_GOLD.
        ws.RobotButtons.Click(mine);
        Check(map.ElementAt(0, 0) == MapHandler.MapElement.Empty,
            "mining off the gold does nothing");

        // FORWARD into the wall and off the edge: both refused, position untouched.
        ws.RobotButtons.Click(forward);
        Check(map.IdentifyRobot(out _) is { X: 0, Y: 0 }, "forward into a wall does not move");
        ws.RobotButtons.Click(turnLeft);          // right -> up
        ws.RobotButtons.Click(forward);
        Check(map.IdentifyRobot(out _) is { X: 0, Y: 0 }, "forward off the map does not move");
        Check(map.ElementAt(0, 1) == MapHandler.MapElement.Wall, "the wall is still a wall");

        // Two more left turns face it down the first column, where the gold waits.
        ws.RobotButtons.Click(turnLeft);          // up -> left
        ws.RobotButtons.Click(turnLeft);          // left -> down
        ws.RobotButtons.Click(forward);           // onto the gold at (0,1)
        Check(map.IdentifyRobot(out Direction? facing) is { X: 0, Y: 1 } && facing == Direction.Down,
            "forward onto gold moves the robot");
        Check(map.ElementAt(1, 0) == MapHandler.MapElement.Gold,
            "the gold stays under the robot for the display and the simulator");

        // MINE follows MachineSpirit's rule: the gold under the robot is gone and the
        // cell is empty, and mining anything else is refused.
        ws.RobotButtons.Click(mine);
        Check(map.ElementAt(1, 0) == MapHandler.MapElement.Empty,
            "mining the gold under the robot leaves an empty cell");
        Check(map.IsMined(1, 0), "the mined cell is marked for the green display");
        Check(!map.IsMined(0, 1), "an original wall is not marked as mined");
        Check(map.GoldMined == 1, "mining raises the collected count");
        ws.Update(0, 0);
        Check(ws.Output.GoldMined == 1 && ws.Output.TotalGold == 1,
            "the output gold row shows the collected count");
        Check(map.IdentifyRobot(out _) is { X: 0, Y: 1 }, "mining does not move the robot");
        ws.RobotButtons.Click(mine);
        Check(map.ElementAt(1, 0) == MapHandler.MapElement.Empty && map.GoldMined == 1,
            "mining an already mined cell does nothing");
        ws.RobotButtons.Click(forward);           // downward, off the mined cell
        Check(map.IdentifyRobot(out _) is { X: 0, Y: 2 },
            "the robot can step off the mined cell");
        Check(map.ElementAt(1, 0) == MapHandler.MapElement.Empty,
            "the mined cell stays empty behind");
        Check(map.IsMined(1, 0), "the mined mark stays after the robot walks off");

        // A mined cell is an ordinary empty cell: the robot can walk back over it.
        ws.RobotButtons.Click(turnLeft);          // down -> right
        ws.RobotButtons.Click(turnLeft);          // right -> up
        ws.RobotButtons.Click(forward);
        Check(map.IdentifyRobot(out _) is { X: 0, Y: 1 },
            "the robot walks back over its mined cell");

        // Reset puts the gold back, keeping the robot (and its marker) in place.
        ws.ResetMined();
        Check(!map.IsMined(1, 0), "reset clears the mined marks");
        Check(map.GoldMined == 0 && map.TotalGold == 1, "reset clears the collected count");
        Check(map.ElementAt(1, 0) == MapHandler.MapElement.Gold,
            "reset turns the mined cell back into gold");
        Check(map.IdentifyRobot(out _) is { X: 0, Y: 1 },
            "reset keeps the robot standing on the restored gold");

        // Unchecking leaves the matrix carrying the new position - IdentifyRobot is
        // exactly what the simulator will read to pick the run up.
        ws.RobotToggle.Click();
        Check(!ws.RobotControl && map.IdentifyRobot(out _) is { X: 0, Y: 1 },
            "unchecking keeps the map at the updated position");
        ws.Update(0, 0);
        Check(!ws.StartButton.Disabled, "Start comes back when control is unchecked");
        Healthy(ws, "after robot control");
    }

    /// <summary>The blue Reset Map button: the full reset - terrain, mined gold, and
    /// the robot's cell and orientation all go back to what the map loaded with,
    /// unlike Reset Gold alone, which keeps the robot where it stands.</summary>
    static void TestResetMap()
    {
        Workspace ws = NewWorkspace();

        // Same shape as the control test: '>' at (0,0) facing right, a wall beside it,
        // gold below.
        ws.LoadMap(new MapHandler(new[,]
        {
            { '>', '#', '.', '.' },
            { '@', '.', '.', '.' },
            { '.', '.', '.', '.' },
        }));

        // One draw pass so the panels take their real heights (RobotButtons sizes in Draw).
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Raylib.WHITE);
        ws.Draw(0, 0);
        Raylib.EndDrawing();

        Check(ws.ResetMapButton.Bounds.X + ws.ResetMapButton.Width <= ws.ResetButton.Bounds.X,
            "the Reset Map button sits left of Reset Gold");
        Check(ws.ResetButton.Bounds.X + ws.ResetButton.Width <= ws.SaveButton.Bounds.X,
            "Reset Gold still sits left of Save");
        Check(Raylib.MeasureText("Reset Map", 14) <= ws.ResetMapButton.Width - 8,
            "the Reset Map label fits its button");

        MapHandler map = ws.MapPanel.Map!;
        Vector2 Center(int button)
        {
            Rectangle r = ws.RobotButtons.ButtonBounds(button);
            return new Vector2(r.X + r.width / 2, r.Y + r.height / 2);
        }

        // Drive far from the original state: turn down, walk onto the gold, mine it,
        // then turn again - position, orientation and mined marks all drift.
        ws.RobotToggle.Click();                    // manual control owns the map now
        ws.RobotButtons.Click(Center(1));          // right -> down
        ws.RobotButtons.Click(Center(2));          // onto the gold at (0,1)
        ws.RobotButtons.Click(Center(3));          // mine it
        ws.RobotButtons.Click(Center(0));          // down -> right
        ws.RobotButtons.Click(Center(0));          // right -> up
        Check(map.IdentifyRobot(out Direction? drifted) is { X: 0, Y: 1 } && drifted == Direction.Up,
            "the robot drifted to a new cell facing a new way");
        Check(map.IsMined(1, 0) && map.GoldMined == 1, "the gold is mined before the full reset");

        ws.ResetMap();

        Check(map.IdentifyRobot(out Direction? home) is { X: 0, Y: 0 } && home == Direction.Right,
            "reset map puts the robot back on its original cell, facing the original way");
        Check(map.ElementAt(0, 1) == MapHandler.MapElement.Wall,
            "reset map restores the original wall");
        Check(map.ElementAt(1, 0) == MapHandler.MapElement.Gold && !map.IsMined(1, 0),
            "reset map brings the gold back and clears the mined mark");
        Check(map.GoldMined == 0 && map.TotalGold == 1, "reset map clears the collected count");
        ws.Update(0, 0);
        Check(ws.Output.GoldMined == 0 && ws.Output.TotalGold == 1,
            "the gold row follows the full reset");

        // The robot drives from its start the original way again.
        ws.RobotButtons.Click(Center(1));          // right -> down
        ws.RobotButtons.Click(Center(2));          // onto the gold at (0,1)
        Check(map.IdentifyRobot(out _) is { X: 0, Y: 1 },
            "after the reset the robot starts over from its original cell");
        Healthy(ws, "after reset map");
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

    // ------------------------------------------------------------ new tests --

    /// <summary>Builds a block from the real catalogue by its AST kind.</summary>
    static Block ByKind(BlockKind kind, Func<Block, bool>? extra = null)
    {
        foreach (BlockCategory cat in BlockLibrary.CreateCategories())
            foreach (Func<Block> f in cat.Factories)
            {
                Block b = f();
                if (b.Kind == kind && (extra == null || extra(b))) return b;
            }
        throw new InvalidOperationException("no block for kind " + kind);
    }

    static NumberBlock Num(long n)
    {
        var b = (NumberBlock)ByKind(BlockKind.Number);
        b.Text = n.ToString();
        return b;
    }

    static VariableBlock Var(string name)
    {
        var b = (VariableBlock)ByKind(BlockKind.Variable);
        b.Name = name;
        return b;
    }

    static void TestSlotBasics()
    {
        Workspace ws = NewWorkspace();

        StackBlock set = (StackBlock)ByKind(BlockKind.Set);
        Check(set.Slots.Count == 2, "set block has two fields");
        Check(set.Slots[0].Kind == SlotKind.Name && set.Slots[1].Kind == SlotKind.Value,
            "first field is a Name dropdown, second is a Value");
        Check(set.Slots.All(s => ReferenceEquals(s.Host, set)), "every field points back at its host");

        ws.AddRootBlock(set, new Vector2(200, 150));
        Healthy(ws, "set root added");

        int wideBefore = set.Width;

        // --- refusals -------------------------------------------------------
        Check(!set.AttachToSlot(set.Slots[0], Num(1)), "a Name field refuses a reporter");
        Check(!set.AttachToSlot(set.Slots[1], new StackBlock("cmd", Cat(), Pos.Start, Pos.Start)),
            "a command block cannot park in a Value field");

        NumberBlock already = Num(7);
        Check(set.AttachToSlot(set.Slots[1], already), "Value field accepts a reporter");
        Check(ReferenceEquals(set.Slots[1].Content, already) && ReferenceEquals(already.Slot, set.Slots[1]),
            "field and reporter point at each other");
        Check(set.Width > wideBefore, "host widened to fit the reporter");

        Check(!set.AttachToSlot(set.Slots[1], Num(9)), "an occupied field refuses a second reporter");

        ReporterBlock chained = Num(3);
        chained.Previous = set; // simulate a reporter still linked into a chain
        Check(!set.AttachToSlot(set.Slots[0], chained) && !set.AttachToSlot(set.Slots[1], chained),
            "a reporter already in a chain refuses to park");
        chained.Previous = null;

        // --- cycle guard ----------------------------------------------------
        ReporterBlock outer = (ReporterBlock)ByKind(BlockKind.Add);
        Check(outer.AttachToSlot(outer.Slots[0], Num(1)), "operator field takes a number");
        Check(!outer.AttachToSlot(outer.Slots[0], outer), "a reporter cannot park in its own field");
        Check(outer.Slots[0].Content is NumberBlock, "the field still holds the number");

        // --- detach ---------------------------------------------------------
        NumberBlock parked = (NumberBlock)already;
        parked.Detach();
        Check(set.Slots[1].Content == null && parked.Slot == null, "detach empties both sides");
        Check(set.Width <= wideBefore, "host shrank back after the reporter left");
        Healthy(ws, "after detach");

        // The reporter is now a free block: park it again and pull it out through
        // the workspace API so the forest invariant covers the round trip.
        Check(set.AttachToSlot(set.Slots[1], parked), "re-park after detach");
        Healthy(ws, "after re-park");
        parked.Detach();
        ws.AddRootBlock(parked, new Vector2(520, 420));
        Healthy(ws, "parked reporter became a root");
        Check(Roots(ws).Contains(parked), "reporter is a root again");
    }

    static void TestIntoSlotSnap()
    {
        Workspace ws = NewWorkspace();

        StackBlock set = (StackBlock)ByKind(BlockKind.Set);
        ws.AddRootBlock(set, new Vector2(220, 160));
        ws.Update(0, 0); // refresh slot bounds

        Slot value = set.Slots[1];
        Check(value.Bounds.width > 0, "slot got bounds from layout");

        // A reporter centred over the empty field must find the IntoSlot snap.
        NumberBlock num = Num(5);
        var center = new Vector2(value.Bounds.X + value.Bounds.width / 2f,
                                 value.Bounds.Y + value.Bounds.height / 2f);
        num.Layout((int)center.X - num.Width / 2, (int)center.Y - num.Height / 2);

        SnapTarget? snap = ws.FindSnap(num);
        Check(snap != null && snap.Kind == SnapKind.IntoSlot && ReferenceEquals(snap.Slot, value),
            "reporter over an empty field snaps IntoSlot");

        ws.Drop(num, snap!);
        Healthy(ws, "after IntoSlot drop");
        Check(ReferenceEquals(value.Content, num), "field holds the reporter after the drop");
        Check(!Roots(ws).Contains(num), "parked reporter is not a root");

        // The field is full now: the same snap must not be offered again.
        NumberBlock second = Num(6);
        second.Layout((int)center.X - second.Width / 2, (int)center.Y - second.Height / 2);
        SnapTarget? snap2 = ws.FindSnap(second);
        Check(snap2 == null || snap2.Kind != SnapKind.IntoSlot || !ReferenceEquals(snap2.Slot, value),
            "an occupied field is not offered again");

        // Pressing an empty value field swallows the press: no drag starts.
        StackBlock print = (StackBlock)ByKind(BlockKind.Print);
        ws.AddRootBlock(print, new Vector2(220, 420));
        ws.Update(0, 0);
        Slot emptyField = print.Slots[0];
        Vector2 press = new(emptyField.Bounds.X + 4, emptyField.Bounds.Y + 4);
        Block? swallowed = ws.HandlePress(press, out _);
        Check(swallowed == null, "press on an empty value field is swallowed (no drag)");
        Check(Roots(ws).Count == 2, "the script under the cursor was not lifted");

        // Pressing the parked reporter lifts just the reporter and empties the field.
        Vector2 onNum = new(num.X + 3, num.Y + 3);
        Block? lifted = ws.HandlePress(onNum, out _);
        Check(ReferenceEquals(lifted, num), "pressing a parked reporter grabs the reporter");
        Check(value.Content == null && num.Slot == null, "lifting it empties the field");
        Healthy(ws, "after lifting a parked reporter", lifted!);

        ws.Drop(lifted!, null);
        Healthy(ws, "after dropping the lifted reporter");
    }

    static void TestElseMouth()
    {
        Workspace ws = NewWorkspace();
        BlockCategory cat = Cat();

        CBlock ifElse = (CBlock)ByKind(BlockKind.If, b => b is CBlock { HasElse: true });
        Check(ifElse.HasElse, "if/else block reports an else mouth");
        ws.AddRootBlock(ifElse, new Vector2(200, 140));

        StackBlock thenHead = new("then-1", cat, Pos.Start, Pos.Start);
        StackBlock elseHead = new("else-1", cat, Pos.Start, Pos.Start);
        Check(ifElse.AttachInside(thenHead), "then mouth accepts a chain");
        Check(ifElse.AttachInside2(elseHead), "else mouth accepts a chain");
        Check(ReferenceEquals(ifElse.Inner, thenHead) && ReferenceEquals(ifElse.Inner2, elseHead),
            "both mouths hold their own head");
        Check(thenHead.Container == ifElse && elseHead.Container == ifElse,
            "both heads point back at the container");
        Healthy(ws, "both mouths filled");

        StackBlock then2 = new("then-2", cat, Pos.Start, Pos.Start);
        Check(thenHead.Append(then2), "then chain extends");
        Healthy(ws, "then chain extended");

        // The else mouth must offer its own snap point.
        ws.Update(0, 0);
        StackBlock drag = new("drag", cat, Pos.Start, Pos.Start);
        drag.Layout((int)ifElse.Inner2Connection.X, (int)ifElse.Inner2Connection.Y);
        SnapTarget? snap = ws.FindSnap(drag);
        Check(snap != null && snap.Kind == SnapKind.Inside && snap.Mouth == 1,
            "a chain lined up with the else mouth snaps to mouth 1");

        ws.Drop(drag, snap!);
        Healthy(ws, "after else-mouth drop");
        Check(ReferenceEquals(ifElse.Inner2, drag) && ReferenceEquals(drag.Next, elseHead),
            "new else head pushes the old one down");

        // A plain if has no second mouth.
        CBlock plainIf = (CBlock)ByKind(BlockKind.If, b => b is CBlock { HasElse: false });
        Check(!plainIf.HasElse && !plainIf.AttachInside2(new StackBlock("x", cat, Pos.Start, Pos.Start)),
            "an if with no else refuses the second mouth");
    }

    // -------------------------------------------------------- vocabulary --

    static void TestVocabulary()
    {
        List<BlockCategory> categories = BlockLibrary.CreateCategories();
        Check(categories.Count == 7, "seven toolbox categories");

        // The literals live together: number, true and false are all just atoms you
        // drop into a field, and `isThere` (a function call) is not one of them.
        BlockCategory values = categories.Single(c => c.Name == "Value");
        var literalKinds = values.Factories.Select(f => f().Kind).ToHashSet();
        Check(literalKinds.SetEquals(new[] { BlockKind.Number, BlockKind.Boolean }),
            "the Value category holds number + true + false and nothing else");
        var sensingKinds = categories.Single(c => c.Name == "Sensing").Factories
            .Select(f => f().Kind).ToHashSet();
        Check(sensingKinds.SetEquals(new[] { BlockKind.IsThere }), "Sensing holds isThere");
        Check(!categories.Single(c => c.Name == "Sensing").Factories
                .Any(f => f().Kind == BlockKind.Variable),
            "no variable reporter hides in Sensing");
        Check(categories.Single(c => c.Name == "Variables").Factories
                .Any(f => f().Kind == BlockKind.Variable),
            "variable references stay in Variables");
        Check(categories.Single(c => c.Name == "Variables").Factories
                .All(f => f().Kind is BlockKind.Set or BlockKind.Variable),
            "the Variables group holds set and variable references only");

        var kinds = new HashSet<BlockKind>();
        int factories = 0;

        foreach (BlockCategory cat in categories)
            foreach (Func<Block> factory in cat.Factories)
            {
                Block b = factory();
                factories++;
                Check(b.Kind != BlockKind.None, $"{cat.Name}: a factory forgot its BlockKind");

                bool classOk = b.Kind switch
                {
                    BlockKind.Start => b is HatBlock,
                    BlockKind.If or BlockKind.While => b is CBlock,
                    BlockKind.Number => b is NumberBlock,
                    BlockKind.Boolean => b is BooleanBlock,
                    BlockKind.Variable => b is VariableBlock,
                    BlockKind.Step or BlockKind.Turn or BlockKind.Mine or BlockKind.Print or BlockKind.Set
                        => b is StackBlock,
                    _ => b is ReporterBlock
                };
                Check(classOk, $"{b.Kind} is built from the right block class");
                kinds.Add(b.Kind);
            }

        Check(factories >= 25, $"the palette is populated ({factories} blocks)");
        Check(kinds.Contains(BlockKind.Start) && kinds.Contains(BlockKind.While) &&
              kinds.Contains(BlockKind.Set) && kinds.Contains(BlockKind.IsThere),
            "every construct the target script needs is in the palette");

        // --- the singleton hat --------------------------------------------
        Workspace ws = NewWorkspace();
        Toolbox toolbox = new(Pos.Start, Pos.Start, 0, 100, categories);
        toolbox.StartAvailable = () => !ws.HasStartBlock;

        Block hatTemplate = ByKind(BlockKind.Start);
        Check(toolbox.CanCreate(hatTemplate), "the hat is offered while the workspace has none");
        Check(toolbox.CanCreate(ByKind(BlockKind.Print)), "ordinary blocks are never greyed out");

        HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
        ws.AddRootBlock(hat, new Vector2(80, 80));
        Check(ws.HasStartBlock, "the workspace counts its Start hat");

        // Toolbox.Update only calls hovered.Factory() when CanCreate is true, so a
        // false here really is "the factory refuses a second".
        Check(!toolbox.CanCreate(hatTemplate), "the palette greys the hat out while one exists");

        Healthy(ws, "vocabulary workspace");
    }

    // ------------------------------------------------------- click editing --

    static void TestClickEditing()
    {
        Workspace ws = NewWorkspace();

        // --- booleans toggle on click -------------------------------------
        BooleanBlock flag = (BooleanBlock)ByKind(BlockKind.Boolean, b => b is BooleanBlock { Value: false });
        ws.AddRootBlock(flag, new Vector2(60, 60));
        Healthy(ws, "boolean added");

        ws.HandleClick(flag, null);
        Check(flag.Value, "clicking a false boolean makes it true");
        ws.HandleClick(flag, null);
        Check(!flag.Value, "clicking it again makes it false");
        ws.HandleClick(null, null); // a click that hit nothing is harmless

        // --- numbers take the caret ---------------------------------------
        NumberBlock n = Num(0);
        ws.AddRootBlock(n, new Vector2(60, 150));
        Healthy(ws, "number added");

        ws.HandleClick(n, null);
        Check(n.Editing && ReferenceEquals(ws.Editing, n), "clicking a number focuses its caret");
        n.InputChar('1');
        n.InputChar('2');
        Check(n.Text == "12", "typed digits replace the leading zero");
        n.Backspace();
        Check(n.Text == "1", "backspace trims the last digit");
        n.InputChar('x');
        Check(n.Text == "1", "letters never reach the number field");
        n.Backspace();
        Check(n.Text == "0", "a lone zero never disappears");

        // The property is the restriction, not the keyboard path: no caller can hand
        // this field anything MineCode could not write.
        n.Text = "a1b22c333444d";
        Check(n.Text == "100", "assignment strips letters and caps at the language's 100");
        n.Text = "00042";
        Check(n.Text == "42", "leading zeros drop so the shown number is the literal");
        n.Text = "";
        Check(n.Text == "0", "an empty assignment can never reach the converter");
        n.Text = "1";
        foreach (char digit in "7777777777777777") n.InputChar(digit);
        Check(n.Text == "17", "typing stops before the value would pass 100");
        n.Text = "1";

        ws.CommitEditing();
        Check(!n.Editing && ws.Editing == null, "committing clears the caret");
        Check(n.Text == "1", "the committed text stays");

        // --- the variable reporter opens the dropdown ---------------------
        VariableBlock vb = Var("arany");
        ws.AddRootBlock(vb, new Vector2(60, 240));
        Healthy(ws, "variable added");

        ws.HandleClick(vb, null);
        Check(ws.Menu.IsOpen, "clicking a variable block opens its dropdown");
        Check(!ws.Menu.Click(new Vector2(-500, -500)), "a click outside the popup is ignored");
        Check(ws.Menu.Picked == null, "an outside click picks nothing");
        ws.ApplyDropPicked();
        Check(!ws.Menu.IsOpen, "the popup closed");

        // --- a set's Name field opens the same dropdown -------------------
        StackBlock set = (StackBlock)ByKind(BlockKind.Set);
        ws.AddRootBlock(set, new Vector2(60, 330));
        Healthy(ws, "set added");

        ws.HandleClick(set, set.Slots[0]);
        Check(ws.Menu.IsOpen, "clicking the name field opens the dropdown");
        Check(set.Slots[0].Content == null, "a name field never holds a block");
        ws.ApplyDropPicked();
        Check(!ws.Menu.IsOpen, "the dropdown closed");
        Check(!ws.Menu.IsOpen && !ws.Prompt.IsOpen && ws.Editing == null,
            "no popup or caret is left behind");
    }

    /// <summary>
    /// A press that never travels past <see cref="Theme.DragThreshold"/> has to be a
    /// click: the block must not slide under the cursor and its click action must run.
    /// This is the path the number field went through on every real click - the block
    /// used to start following on press, so a hand's normal jitter turned the click
    /// into a tiny drag and the caret never appeared.
    /// </summary>
    static void TestPressClickVsDrag()
    {
        Workspace ws = NewWorkspace();

        NumberBlock n = Num(7);
        ws.AddRootBlock(n, new Vector2(120, 120));
        ws.Update(0, 0);
        Healthy(ws, "number placed");
        int x0 = n.X, y0 = n.Y;

        Vector2 press = new(n.X + 4, n.Y + 4);
        Check(ws.HandlePress(press, out _) == n, "the press grabbed the number");
        Check(n.X == x0 && n.Y == y0, "grabbing does not move the block");

        // 3px of hand jitter and release: still a click.
        Check(ws.AdvanceDrag(press + new Vector2(3, 0), released: true),
            "the press owns the frame");
        Check(n.X == x0 && n.Y == y0, "a click never slides the block");
        Check(ReferenceEquals(ws.Editing, n) && n.Editing, "a click gives the number its caret");
        ws.CommitEditing();
        Check(!n.Editing && ws.Editing == null, "the caret can be released again");

        // Past the threshold it is a drag: the block follows, and releasing is not a click.
        Check(ws.HandlePress(new Vector2(n.X + 4, n.Y + 4), out _) == n, "grabbed a second time");
        Check(ws.AdvanceDrag(press + new Vector2(60, 40), released: false),
            "the drag owns the frame");
        Check(n.X == x0 + 60 && n.Y == y0 + 40, "past the threshold the block follows the pointer");
        Check(ws.AdvanceDrag(press + new Vector2(60, 40), released: true), "still dragging");
        Check(n.X == x0 + 60 && n.Y == y0 + 40, "it lands where the pointer is");
        Check(ws.Editing == null && !n.Editing, "a drag never starts the caret");
        Healthy(ws, "after click vs drag");

        // A reporter parked in a field must survive a click on it: it detaches on press
        // and has to snap straight back into the same field.
        StackBlock print = (StackBlock)ByKind(BlockKind.Print);
        ws.AddRootBlock(print, new Vector2(320, 300));
        ReporterBlock parked = Num(3);
        Check(print.AttachToSlot(print.Slots[0], parked), "print took its number");
        ws.Update(0, 0);
        ws.LayoutAll(); // parked reporters only get their position from the host's layout
        Healthy(ws, "parked number placed");

        Vector2 onField = new(parked.X + 4, parked.Y + 4);
        Check(ws.HandlePress(onField, out _) == parked, "the press grabbed the parked number");
        Check(parked.Slot == null && print.Slots[0].Content == null, "it left the field");
        Check(ws.AdvanceDrag(onField, released: true), "the click owns the frame");
        Check(print.Slots[0].Content == parked, "a click parks it straight back in the same field");
        Check(ReferenceEquals(ws.Editing, parked), "and the caret still appears");
        ws.CommitEditing();
        Healthy(ws, "after the parked click");
    }

    // ----------------------------------------------------------- dropdowns --

    static Vector2 Center(Rectangle r) => new(r.X + r.width / 2f, r.Y + r.height / 2f);

    /// <summary>The delete button only removes variables no block refers to, wherever
    /// the reference sits, and it never counts as picking the row.</summary>
    static void TestDeleteVariables()
    {
        Workspace ws = NewWorkspace();
        foreach (string v in new[] { "free", "named", "nested" })
            Check(ws.Registry.TryCreate(v, out _), $"registered {v}");

        // One reference through a name field, one through a reporter inside a C-block.
        StackBlock set = (StackBlock)ByKind(BlockKind.Set);
        ws.AddRootBlock(set, new Vector2(140, 140));
        set.Slots[0].SelectedName = "named";

        CBlock loop = (CBlock)ByKind(BlockKind.While);
        ws.AddRootBlock(loop, new Vector2(140, 260));
        StackBlock print = (StackBlock)ByKind(BlockKind.Print);
        Check(loop.AttachInside(print), "the while took the print");
        Check(print.AttachToSlot(print.Slots[0], Var("nested")), "print took its variable");

        var opener = (VariableBlock)ByKind(BlockKind.Variable);
        ws.AddRootBlock(opener, new Vector2(400, 140));
        ws.Update(0, 0);

        ws.HandleClick(opener, null);
        Check(ws.Menu.IsOpen, "clicking the variable opened its menu");

        // Rows follow registry order: free, named, nested.
        ws.Menu.Click(Center(ws.Menu.DeleteBounds(1)));
        Check(ws.Menu.DeleteRefused && ws.Menu.DeleteRequested == null, "named is in use");
        ws.ApplyDropPicked();
        Check(ws.Menu.IsOpen, "a refused delete leaves the menu open");
        Check(ws.Registry.Contains("named"), "a name field keeps its variable alive");
        Check(ws.Menu.Picked == null, "the delete button does not pick the row");

        ws.Menu.Click(Center(ws.Menu.DeleteBounds(2)));
        ws.ApplyDropPicked();
        Check(ws.Registry.Contains("nested"), "a parked reporter in a mouth keeps its variable alive");

        // The workspace refuses on its own too, not just through the button.
        Check(!ws.TryDeleteVariable("named"), "a direct delete of a used variable is refused");

        ws.Menu.Click(Center(ws.Menu.DeleteBounds(0)));
        Check(ws.Menu.DeleteRequested == "free", "the free variable's button fired");
        ws.ApplyDropPicked();
        Check(!ws.Registry.Contains("free"), "the free variable is gone");
        Check(ws.Menu.IsOpen, "the menu stays open after a delete");
        Check(opener.Name == null, "deleting does not fill the field that opened the menu");

        // No delete button on "Make a variable...": the last row picks.
        Rectangle last = ws.Menu.DeleteBounds(ws.Registry.Names.Count);
        Check(ws.Menu.Click(Center(last)), "the last row was hit");
        Check(ws.Menu.Picked == DropDown.MakeNew && ws.Menu.DeleteRequested == null,
            "Make a variable... has no delete button");
        ws.Menu.Close();

        // Once the last reference goes, the variable can go too.
        set.Slots[0].SelectedName = null;
        Check(ws.TryDeleteVariable("named"), "an unreferenced variable can be deleted");
        Check(!ws.Registry.Contains("named"), "named is gone");
        ws.Update(0, 0);
        Check(ws.Validation.Errors.All(e => !e.Message.Contains("unknown variable")),
            "deleting leaves no block pointing at a missing variable");
        Healthy(ws, "after deleting variables");
    }

    static void TestDropdowns()
    {
        Workspace ws = NewWorkspace();

        StackBlock set = (StackBlock)ByKind(BlockKind.Set);
        ws.AddRootBlock(set, new Vector2(140, 140));
        ws.Update(0, 0);
        Healthy(ws, "set added");
        Check(set.Slots[0].SelectedName == null, "a fresh name field is unset");

        // The menu lists only what the registry holds.
        Check(ws.Registry.TryCreate("count", out _), "registered count");
        Check(!ws.Registry.Contains("wall"), "nothing is known until it is created");

        // A name field is a dropdown, not a drag handle.
        Rectangle nameField = set.Slots[0].Bounds;
        Check(ws.HandlePress(new Vector2(nameField.X + 4, nameField.Y + 4), out _) == null,
            "pressing a name field starts no drag");
        Check(ws.Menu.IsOpen, "pressing a name field opens its menu");
        ws.Menu.Close();

        // --- pick an existing variable ------------------------------------
        ws.HandleClick(set, set.Slots[0]);
        Check(ws.Menu.IsOpen, "the name field opened the menu");

        Rectangle menu = ws.Menu.LocalBounds;
        Vector2 firstRow = new(menu.X + 8, menu.Y + 4 + Theme.EntryHeight / 2f);
        Check(ws.Menu.Click(firstRow), "the click landed on a row");
        Check(ws.Menu.Picked == ws.Registry.Names[0], "row 0 is the first registry name");
        ws.ApplyDropPicked();
        Check(set.Slots[0].SelectedName == ws.Registry.Names[0],
            "the first registered row went into the field");
        Check(!ws.Menu.IsOpen, "the menu closed after the pick");

        // --- "Make a variable..." ----------------------------------------
        ws.HandleClick(set, set.Slots[0]);
        Check(ws.Menu.IsOpen, "the menu reopened");

        Rectangle menu2 = ws.Menu.LocalBounds;
        int makeNewRow = ws.Registry.Names.Count; // the menu appends its own last row
        Vector2 lastRow = new(menu2.X + 8, menu2.Y + 4 + (makeNewRow + 0.5f) * Theme.EntryHeight);
        Check(ws.Menu.Click(lastRow), "the last row was hit");
        Check(ws.Menu.Picked == DropDown.MakeNew, "the last row is Make a variable...");
        ws.ApplyDropPicked();
        Check(ws.Prompt.IsOpen, "the one-line prompt opened");
        Check(!ws.Menu.IsOpen, "the menu is gone");

        ws.Prompt.InputChar('9');
        Check(!ws.ConfirmPrompt(), "a name starting with a digit is refused");
        Check(ws.Prompt.IsOpen && ws.Prompt.Error != null, "the prompt stays open and says why");
        ws.Prompt.Backspace();

        foreach (char c in "arany") ws.Prompt.InputChar(c);
        Check(ws.Prompt.Text == "arany", "the prompt collected the typed name");
        Check(ws.ConfirmPrompt(), "arany is a valid identifier");
        Check(!ws.Prompt.IsOpen, "the prompt closed on success");
        Check(ws.Registry.Contains("arany"), "the registry knows the new variable");
        Check(set.Slots[0].SelectedName == "arany", "the field that asked got the new name");

        // The selected name is part of the block geometry: a long identifier must widen
        // the field instead of spilling out of a fixed hole.
        int widthWithArany = set.Width;
        Check(set.Slots[0].DisplayWidth > Theme.MinSlotWidth,
            "the name field asks for room for its selected name");
        set.Slots[0].SelectedName = "averylongvariablename";
        Check(set.Width > widthWithArany, "a long variable name widens the block");
        set.Slots[0].SelectedName = "arany";

        // --- duplicates ---------------------------------------------------
        ws.Prompt.Open(8, 8);
        foreach (char c in "arany") ws.Prompt.InputChar(c);
        Check(!ws.ConfirmPrompt(), "creating the same name twice is refused");
        Check(ws.Prompt.Error != null, "the duplicate is explained");
        ws.Prompt.Close();

        // --- keywords are refused and a typo can be erased -----------------
        ws.Prompt.Open(8, 8);
        foreach (char c in "while") ws.Prompt.InputChar(c);
        Check(!ws.ConfirmPrompt(), "a keyword can't become a variable");
        Check(ws.Prompt.Error != null && ws.Prompt.Error.Contains("keyword"),
            "the refusal says it's a keyword");
        Check(!ws.Registry.Names.Contains("while"), "while is not in the variable list");
        ws.Prompt.Backspace();
        Check(ws.Prompt.Text == "whil" && ws.Prompt.Error == null, "backspace erases and clears the error");
        ws.Prompt.Close();

        ws.Update(0, 0);
        Healthy(ws, "after dropdown work");
        Check(!ws.Menu.IsOpen && !ws.Prompt.IsOpen && ws.Editing == null,
            "no popup or caret is left open");
    }

    // ---------------------------------------------------------- golden AST --

    static void TestGoldenAst()
    {
        Workspace ws = NewWorkspace();

        foreach (string v in new[] { "arany", "count", "fal", "megy" })
            Check(ws.Registry.TryCreate(v, out _), $"registered variable {v}");

        ReporterBlock Rep(BlockKind kind) => (ReporterBlock)ByKind(kind);
        StackBlock Stat(BlockKind kind) => (StackBlock)ByKind(kind);

        ReporterBlock ThereIs(long cell)
        {
            ReporterBlock b = Rep(BlockKind.IsThere);
            Check(b.AttachToSlot(b.Slots[0], Num(cell)), "isThere took its cell");
            return b;
        }

        ReporterBlock NotOf(string name)
        {
            ReporterBlock b = Rep(BlockKind.Not);
            Check(b.AttachToSlot(b.Slots[0], Var(name)), "not took its variable");
            return b;
        }

        ReporterBlock Plus(string left, long right)
        {
            ReporterBlock b = Rep(BlockKind.Add);
            Check(b.AttachToSlot(b.Slots[0], Var(left)), "add took its left operand");
            Check(b.AttachToSlot(b.Slots[1], Num(right)), "add took its right operand");
            return b;
        }

        StackBlock SetTo(string name, Block value)
        {
            StackBlock b = Stat(BlockKind.Set);
            b.Slots[0].SelectedName = name;
            Check(b.AttachToSlot(b.Slots[1], value), $"set {name} took its value");
            return b;
        }

        StackBlock PrintVar(string name)
        {
            StackBlock b = Stat(BlockKind.Print);
            Check(b.AttachToSlot(b.Slots[0], Var(name)), $"print {name} took its variable");
            return b;
        }

        StackBlock StepFwd(long n)
        {
            StackBlock b = Stat(BlockKind.Step);
            Check(b.AttachToSlot(b.Slots[0], Num(n)), "step forward took its number");
            return b;
        }

        HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
        ws.AddRootBlock(hat, new Vector2(60, 60));

        // if ... then ... else ... (lives inside the while)
        CBlock ifElse = (CBlock)ByKind(BlockKind.If, b => b is CBlock { HasElse: true });
        Check(ifElse.AttachToSlot(ifElse.Slots[0], Var("arany")), "if took its condition");

        Block thenHead = StepFwd(1);
        Chain(thenHead, Stat(BlockKind.Mine), SetTo("count", Plus("count", 1)));
        Check(ifElse.AttachInside0(thenHead), "the then branch went into mouth 0");

        Block elseHead = StepFwd(1);
        Chain(elseHead, SetTo("fal", ThereIs(1)), SetTo("megy", NotOf("fal")));
        Check(ifElse.AttachInside2(elseHead), "the else branch went into mouth 1");

        // while ... do (body: one statement, then the if/else)
        CBlock whileBlk = (CBlock)ByKind(BlockKind.While);
        Check(whileBlk.AttachToSlot(whileBlk.Slots[0], Var("megy")), "while took its condition");
        Block whileHead = SetTo("arany", ThereIs(2));
        Chain(whileHead, ifElse);
        Check(whileBlk.AttachInside0(whileHead), "the while body went into its mouth");

        // the script itself, top to bottom
        Chain(hat,
            SetTo("arany", ThereIs(2)),
            PrintVar("arany"),
            SetTo("count", Num(0)),
            SetTo("fal", ThereIs(1)),
            SetTo("megy", NotOf("fal")),
            whileBlk,
            PrintVar("count"));

        ws.Update(0, 0);
        Healthy(ws, "target script assembled");
        Check(Roots(ws).Count == 1, "the whole script is a single root stack");

        AstResult result = ws.RunStart();
        string problems = string.Join(" | ", result.Errors.Select(e => $"[{e.Block.Label}] {e.Message}"));
        Check(result.Errors.Count == 0, $"the target script converts cleanly, got: {problems}");
        Check(result.Scripts.Length == 1, "one script came out");

        // Lines are the generated MineCode's lines (the `else` keyword owns line 12),
        // which is exactly what the Backend parser reports for the same source.
        Body expected = new(
        [
            new SetInstruction("arany", new IsThere(new Literal(2)), 1),
            new PrintInstruction(new Variable("arany"), 2),
            new SetInstruction("count", new Literal(0), 3),
            new SetInstruction("fal", new IsThere(new Literal(1)), 4),
            new SetInstruction("megy", new Not(new Variable("fal")), 5),
            new WhileInstruction(new Variable("megy"), new Body(
            [
                new SetInstruction("arany", new IsThere(new Literal(2)), 7),
                new IfInstruction(new Variable("arany"),
                    new Body(
                    [
                        new StepInstruction(new Literal(1), 9),
                        new MineInstruction(10),
                        new SetInstruction("count",
                            new BinOp(new Variable("count"), new Literal(1), BinOp.Operation.Add), 11),
                    ]),
                    new Body(
                    [
                        new StepInstruction(new Literal(1), 13),
                        new SetInstruction("fal", new IsThere(new Literal(1)), 14),
                        new SetInstruction("megy", new Not(new Variable("fal")), 15),
                    ]), 8, 12),
            ]), 6),
            new PrintInstruction(new Variable("count"), 16),
        ]);

        Check(Same(result.Scripts[0], expected), "the AST is exactly the hand-written target tree");
        Check(result.Scripts[0].Instructions.Length == 7, "seven top-level instructions");

        // --- parity with Backend/MineCode -----------------------------------
        string code = MineCodeWriter.Write(result.Scripts[0]);
        Check(Normalize(code) == Normalize(TargetSource), "the AST writes back as the target MineCode");
        Check(result.ToMineCode() == code, "AstResult exposes the generated source");

        Body reparsed;
        TextWriter originalOut = Console.Out;
        try
        {
            // The backend parser dumps its parse tree; keep the test log readable.
            Console.SetOut(TextWriter.Null);
            reparsed = MineCode.ParseCode(code);
        }
        catch (Exception ex)
        {
            Console.SetOut(originalOut);
            Fail("parity", "the backend parser rejected the generated code: " + ex.Message);
            return;
        }
        finally
        {
            Console.SetOut(originalOut);
        }
        Check(Same(reparsed, result.Scripts[0]),
            "the backend parses the generated code back into the same AST");
    }

    static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');

    /// <summary>
    /// The load path a file takes: source text → backend parser → Shared AST →
    /// AstLoader blocks → AstConverter AST again. The rebuilt workspace has to be a
    /// healthy forest that converts with no problems and no lost line numbers.
    /// </summary>
    static void TestLoadRoundTrip()
    {
        Workspace ws = NewWorkspace();

        Body parsed;
        TextWriter originalOut = Console.Out;
        try
        {
            // The backend parser dumps its parse tree; keep the test log readable.
            Console.SetOut(TextWriter.Null);
            parsed = MineCode.ParseCode(TargetSource);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        List<string> problems = AstLoader.Load(ws, [parsed]);
        Check(problems.Count == 0,
            "the loader reported no problems, got: " + string.Join(" | ", problems));

        Healthy(ws, "after loading the parsed script");
        Check(Roots(ws).Count == 1, "the file loaded as one root stack");
        Check(ws.HasStartBlock, "the loaded stack starts with the Start hat");
        Check(ws.Registry.Contains("arany") && ws.Registry.Contains("count") &&
              ws.Registry.Contains("fal") && ws.Registry.Contains("megy"),
            "the variables the file uses were registered on the way in");

        ws.Update(0, 0);
        Healthy(ws, "after the first layout pass");
        Check(Roots(ws)[0].X >= 0 && Roots(ws)[0].Y >= 0, "the loaded script was placed inside the workspace");

        AstResult result = ws.RunStart();
        string errors = string.Join(" | ", result.Errors.Select(e => $"[{e.Block.Label}] {e.Message}"));
        Check(result.Errors.Count == 0, $"the loaded script converts cleanly, got: {errors}");
        Check(result.Scripts.Length == 1, "one script came out");
        Check(Same(result.Scripts[0], parsed), "load → blocks → convert is the same AST, lines included");

        // Round it back to source: the editor's own writer has to reproduce the file.
        Check(Normalize(MineCodeWriter.Write(result.Scripts[0])) == Normalize(TargetSource),
            "the loaded script writes back as the source it came from");

        // Words like wall are ordinary variable names, and isThere's argument stays
        // the number or variable the file wrote.
        Workspace cells = NewWorkspace();
        Body cellScript;
        try
        {
            Console.SetOut(TextWriter.Null);
            cellScript = MineCode.ParseCode("set wall to 1\nset a to isThere wall\nset b to isThere 0\n");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        List<string> cellProblems = AstLoader.Load(cells, [cellScript]);
        Check(cellProblems.Count == 0,
            "the script loaded cleanly, got: " + string.Join(" | ", cellProblems));
        Healthy(cells, "after loading the script");

        AstResult cellResult = cells.RunStart();
        Check(cellResult.Errors.Count == 0, "the script converts cleanly");
        Check(Normalize(MineCodeWriter.Write(cellResult.Scripts[0])) ==
              "set wall to 1\nset a to isThere wall\nset b to isThere 0",
            "the blocks write back exactly as the file had them");

        // `not` and `isThere` conditions survive text -> blocks -> text.
        const string negated = "set a to not isThere 1\nif not isThere 2\n    print 1\nif not a\n    print 2\n" +
                               "if isThere 2\n    print 3\nwhile isThere 0\n    step forward 1\n" +
                               "while not isThere 1\n    step forward 1\nwhile not a\n    print 4";
        Workspace negatedWs = NewWorkspace();
        Body negatedScript;
        try
        {
            Console.SetOut(TextWriter.Null);
            negatedScript = MineCode.ParseCode(negated + "\n");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        List<string> negatedProblems = AstLoader.Load(negatedWs, [negatedScript]);
        Check(negatedProblems.Count == 0,
            "the negated script loaded cleanly, got: " + string.Join(" | ", negatedProblems));
        AstResult negatedResult = negatedWs.RunStart();
        Check(negatedResult.Errors.Count == 0, "the negated script converts cleanly");
        Check(Normalize(MineCodeWriter.Write(negatedResult.Scripts[0])) == negated,
            "if not / not isThere write back exactly as the file had them");
    }

    /// <summary>
    /// Save: converts the workspace, writes through the shared file handler in the
    /// input format (header, map rows, code), refuses without a script or a file,
    /// and parks itself to the left of Start.
    /// </summary>
    static void TestSave()
    {
        string path = Path.Combine(Path.GetTempPath(), $"guiselftest-save-{Guid.NewGuid():N}.txt");
        try
        {
            // A minimal input file: header, one map row, one instruction.
            File.WriteAllLines(path, ["1 1", ".", "print 1"]);
            HandleInputFile handler = new(path);
            Check(handler.Code.Count == 1 && handler.Code[0] == "print 1", "the sample file reads");

            Workspace ws = NewWorkspace();
            Body script;
            TextWriter originalOut = Console.Out;
            try
            {
                Console.SetOut(TextWriter.Null);
                script = MineCode.ParseCode(string.Join("\n", handler.Code));
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            List<string> problems = AstLoader.Load(ws, [script]);
            Check(problems.Count == 0,
                "the script loaded, got: " + string.Join(" | ", problems));

            ws.SaveScript = handler.Write;
            Check(ws.Save(), "saving the loaded script works");

            HandleInputFile reloaded = new(path);
            Check(reloaded.Map != null && reloaded.Map.GetLength(0) == 1 && reloaded.Map[0, 0] == '.',
                "the map survived the save");
            Check(string.Join("\n", reloaded.Code) == "print 1",
                "the code round trips through the file");

            // A save replaces only the code section: the map part goes back exactly as
            // the file had it, never as memory has it (a walked robot mutates the very
            // array the file was read into - that is how the map used to drift).
            handler.Map![0, 0] = '#';
            handler.Write("step forward 1");
            string[] disk = File.ReadAllLines(path);
            Check(disk.Length == 3 && disk[0] == "1 1" && disk[1] == ".",
                "the save writes the file's map part, not the in-memory one");
            HandleInputFile after = new(path);
            Check(after.Code.Count == 1 && after.Code[0] == "step forward 1",
                "the code section was replaced");
            Check(after.Map![0, 0] == '.', "the map reloads exactly as the file had it");

            // Save sits to the left of Start, on the same row.
            ws.Update(0, 0);
            Check(ws.SaveButton.Bounds.X + ws.SaveButton.Bounds.width <= ws.StartButton.Bounds.X &&
                  ws.SaveButton.Bounds.Y == ws.StartButton.Bounds.Y,
                "Save sits to the left of Start");

            // No Start block: nothing to write.
            string? saved = null;
            Workspace bare = NewWorkspace();
            bare.SaveScript = s => saved = s;
            Check(!bare.Save() && saved == null, "a workspace without a Start block saves nothing");

            // No input file: refused even with a script.
            Workspace noFile = NewWorkspace();
            HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
            StackBlock print = (StackBlock)ByKind(BlockKind.Print);
            Check(print.AttachToSlot(print.Slots[0], Num(7)), "print took its number");
            Check(hat.Append(print), "print chained");
            noFile.AddRootBlock(hat, new Vector2(60, 60));
            Check(!noFile.Save(), "saving is refused when no file was loaded");
        }
        finally
        {
            File.Delete(path);
        }
    }


    /// <summary>
    /// The palette is the whole language surface: every BlockKind except the UI-only
    /// None must have a factory, and there must be no extra kind that the AST cannot
    /// represent. This is the guard that catches a language construct being added to
    /// Backend without its block (or the reverse).
    /// </summary>
    static void TestPaletteParity()
    {
        var expected = Enum.GetValues<BlockKind>()
            .Where(k => k != BlockKind.None)
            .ToHashSet();

        var actual = new HashSet<BlockKind>();
        foreach (BlockCategory category in BlockLibrary.CreateCategories())
            foreach (Func<Block> factory in category.Factories)
                actual.Add(factory().Kind);

        Check(actual.SetEquals(expected),
            "palette kinds match BlockKind exactly; missing: " +
            string.Join(",", expected.Except(actual)) +
            " extra: " + string.Join(",", actual.Except(expected)));

        Check(actual.Contains(BlockKind.Start), "Start is the one UI-only kind");
        Check(actual.Contains(BlockKind.If) && actual.Contains(BlockKind.While),
            "control flow blocks are present");
        Check(actual.Contains(BlockKind.IsThere) && actual.Contains(BlockKind.Not),
            "the prefix expression blocks are present");
    }

    /// <summary>
    /// Each operator block must produce the matching BinOp.Operation, and its rendered
    /// MineCode must parse as that same operator. One table keeps block, AST and lexer
    /// from drifting apart.
    /// </summary>
    static void TestOperatorParity()
    {
        var operations = new (BlockKind Kind, BinOp.Operation Operation, string Symbol)[]
        {
            (BlockKind.Add, BinOp.Operation.Add, "+"),
            (BlockKind.Sub, BinOp.Operation.Sub, "-"),
            (BlockKind.Mul, BinOp.Operation.Mul, "*"),
            (BlockKind.Eq, BinOp.Operation.Eq, "="),
            (BlockKind.Neq, BinOp.Operation.Neq, "<>"),
            (BlockKind.Lt, BinOp.Operation.Lt, "<"),
            (BlockKind.Gt, BinOp.Operation.Gt, ">"),
            (BlockKind.Leq, BinOp.Operation.Leq, "<="),
            (BlockKind.Geq, BinOp.Operation.Geq, ">="),
            (BlockKind.And, BinOp.Operation.And, "and"),
            (BlockKind.Or, BinOp.Operation.Or, "or"),
        };

        foreach ((BlockKind kind, BinOp.Operation operation, string symbol) in operations)
        {
            Workspace ws = NewWorkspace();
            HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
            ws.AddRootBlock(hat, new Vector2(60, 60));

            StackBlock set = (StackBlock)ByKind(BlockKind.Set);
            ws.Registry.TryCreate("r", out _);
            set.Slots[0].SelectedName = "r";
            ReporterBlock bin = (ReporterBlock)ByKind(kind);
            Check(bin.AttachToSlot(bin.Slots[0], Num(1)), $"{kind}: left operand");
            Check(bin.AttachToSlot(bin.Slots[1], Num(2)), $"{kind}: right operand");
            Check(set.AttachToSlot(set.Slots[1], bin), $"{kind}: set value");
            Check(hat.Append(set), $"{kind}: chained under the hat");

            AstResult result = AstConverter.Convert(ws);
            Check(result.Errors.Count == 0, $"{kind}: converts cleanly");
            Check(result.Scripts.Length == 1, $"{kind}: one script");

            var value = (SetInstruction)result.Scripts[0].Instructions[0];
            Check(value.Value is BinOp { operation: var actual } && actual == operation,
                $"{kind}: maps to {operation}");

            Check(Normalize(MineCodeWriter.Write(result.Scripts[0])) == $"set r to 1 {symbol} 2",
                $"{kind}: renders as 'set r to 1 {symbol} 2'");
        }
    }

    /// <summary>Variable naming: keywords are refused in any case, invalid names are
    /// refused, and everything else is an ordinary variable.</summary>
    static void TestVariableNames()
    {
        var registry = new VariableRegistry();
        foreach (string keyword in new[]
                 {
                     "and", "by", "else", "false", "forward", "here", "if", "isThere", "mine",
                     "not", "or", "print", "set", "step", "to", "true", "turn", "while",
                     "WHILE", "While", "ISTHERE", "If",
                 })
            Check(!registry.TryCreate(keyword, out _), $"{keyword} is refused as a name");
        Check(!registry.TryCreate("9lives", out _), "9lives is refused");
        Check(registry.TryCreate("gold", out _) && registry.TryCreate("wall", out _) &&
              registry.TryCreate("empty", out _), "cell words are ordinary names");

        Workspace ws = NewWorkspace();
        HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
        ws.AddRootBlock(hat, new Vector2(60, 60));
        ws.Registry.TryCreate("gold", out _);
        StackBlock print = (StackBlock)ByKind(BlockKind.Print);
        Check(print.AttachToSlot(print.Slots[0], Var("gold")), "print took gold");
        Check(hat.Append(print), "chained");
        AstResult result = AstConverter.Convert(ws);
        Check(result.Errors.Count == 0 && Normalize(MineCodeWriter.Write(result.Scripts[0])) == "print gold",
            "gold stays a variable reference");
    }

    /// <summary>Operators and isThere fit only right of `set ... to`; every other
    /// field is a simpleval and value blocks do not nest, except `not` and `isThere`
    /// as an `if` or `while` condition, and `not isThere x`.</summary>
    static void TestValueSlots()
    {
        Block Op()
        {
            ReporterBlock add = (ReporterBlock)ByKind(BlockKind.Add);
            add.AttachToSlot(add.Slots[0], Num(1));
            add.AttachToSlot(add.Slots[1], Num(2));
            return add;
        }

        foreach (BlockKind kind in new[] { BlockKind.Step, BlockKind.Turn, BlockKind.Print })
        {
            Block host = ByKind(kind);
            Check(!host.AttachToSlot(host.Slots[0], Op()), $"{kind} refuses an operator");
            Check(!host.AttachToSlot(host.Slots[0], ByKind(BlockKind.IsThere)), $"{kind} refuses isThere");
            Check(host.AttachToSlot(host.Slots[0], Num(1)), $"{kind} takes a number");
        }
        foreach (BlockKind kind in new[] { BlockKind.If, BlockKind.While })
        {
            Block host = ByKind(kind);
            Check(!host.AttachToSlot(host.Slots[0], Op()), $"{kind} refuses an operator");
            Check(host.AttachToSlot(host.Slots[0], Var("x")), $"{kind} takes a variable");
        }

        Block set = ByKind(BlockKind.Set);
        Check(set.AttachToSlot(set.Slots[1], Op()), "set takes an operator");
        ReporterBlock not = (ReporterBlock)ByKind(BlockKind.Not);
        Check(!not.AttachToSlot(not.Slots[0], Op()), "value blocks do not nest");
        Check(not.AttachToSlot(not.Slots[0], Var("x")), "not takes a variable");

        // The deliberate exceptions, and their limits.
        foreach (BlockKind kind in new[] { BlockKind.If, BlockKind.While })
        {
            Block takesNot = ByKind(kind);
            ReporterBlock notIsThere = (ReporterBlock)ByKind(BlockKind.Not);
            ReporterBlock sensing = (ReporterBlock)ByKind(BlockKind.IsThere);
            Check(sensing.AttachToSlot(sensing.Slots[0], Num(1)), "isThere takes a number");
            Check(notIsThere.AttachToSlot(notIsThere.Slots[0], sensing), "not takes isThere");
            Check(takesNot.AttachToSlot(takesNot.Slots[0], notIsThere), $"{kind} takes not holding isThere");

            Block takesIsThere = ByKind(kind);
            Check(takesIsThere.AttachToSlot(takesIsThere.Slots[0], ByKind(BlockKind.IsThere)), $"{kind} takes isThere");
        }

        ReporterBlock notOuter = (ReporterBlock)ByKind(BlockKind.Not);
        Check(!notOuter.AttachToSlot(notOuter.Slots[0], ByKind(BlockKind.Not)), "not refuses not");
    }

    /// <summary>Control blocks nest three levels at most, however the chain is built.</summary>
    static void TestNestingLimit()
    {
        CBlock C() => (CBlock)ByKind(BlockKind.While);

        CBlock one = C(), two = C(), three = C();
        Check(one.AttachInside0(two), "level 2 fits");
        Check(two.AttachInside0(three), "level 3 fits");
        Check(!three.AttachInside0(C()), "level 4 is refused inside");
        StackBlock leaf = (StackBlock)ByKind(BlockKind.Mine);
        Check(three.AttachInside0(leaf), "a plain block still fits at the deepest level");
        Check(!leaf.AttachBelow(C()), "level 4 is refused below a block at level 3 body");
        Check(!leaf.Append(C()), "level 4 is refused by Append too");
        Check(three.Append(C()), "a sibling at level 3 is fine");

        // A chain that is itself two levels deep needs two levels of room.
        CBlock outer = C(), middle = C();
        CBlock deep = C(), deeper = C();
        Check(outer.AttachInside0(middle), "outer holds middle");
        Check(deep.AttachInside0(deeper), "the dragged chain is two deep");
        Check(!middle.AttachInside0(deep), "two more levels do not fit at level 2");

        // The drag path offers no snap that would exceed the limit.
        Workspace ws = NewWorkspace();
        CBlock a = C(), b = C(), c = C();
        Check(a.AttachInside0(b) && b.AttachInside0(c), "three levels built");
        ws.AddRootBlock(a, new Vector2(60, 60));
        ws.LayoutAll();
        CBlock extra = C();
        extra.Layout((int)c.InnerConnection.X, (int)c.InnerConnection.Y);
        SnapTarget? snap = ws.FindSnap(extra);
        Check(snap is null || snap.Kind != SnapKind.Inside || !ReferenceEquals(snap.Target, c),
            "no snap into the third level for a fourth control block");
        ws.Drop(extra, snap);
        Check(c.Inner == null, "the deepest mouth stayed empty");
        Healthy(ws, "after the refused drop");
    }

    /// <summary>Dropping above a block keeps that block in its script.</summary>
    static void TestInsertBefore()
    {
        Workspace ws = NewWorkspace();
        HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
        StackBlock first = (StackBlock)ByKind(BlockKind.Mine);
        StackBlock second = (StackBlock)ByKind(BlockKind.Mine);
        Check(hat.Append(first) && first.Append(second), "script built");
        ws.AddRootBlock(hat, new Vector2(60, 60));

        StackBlock inserted = (StackBlock)ByKind(BlockKind.Mine);
        ws.Drop(inserted, new SnapTarget(SnapKind.Above, second, second.PreviousConnection, second.Width));
        Check(ReferenceEquals(first.Next, inserted) && ReferenceEquals(inserted.Next, second),
            "the block landed between its neighbours");
        Check(Roots(ws).Count == 1 && ReferenceEquals(Roots(ws)[0], hat), "the script is still one chain under Start");
        Healthy(ws, "after inserting before");
    }

    /// <summary>The live session: trace stepping, variables, the sensor, explored cells,
    /// the reset on start, and manual control interrupting the run.</summary>
    static void TestRunSession()
    {
        Workspace ws = NewWorkspace();
        ws.LoadMap(new MapHandler(new[,] { { '>', '.', '.', '@', '.' } }));
        MapHandler map = ws.MapPanel.Map!;

        TextWriter savedOut = Console.Out;
        Body script;
        try
        {
            Console.SetOut(TextWriter.Null);
            script = MineCode.ParseCode("set n to 1\nset g to isThere 2\nstep forward 3\nmine here\nprint n\n");
        }
        finally
        {
            Console.SetOut(savedOut);
        }
        Check(AstLoader.Load(ws, [script]).Count == 0, "the script loaded");
        ws.Update(0, 0);
        ws.RunProgram = r => ws.AttachSession(Runner.Begin(ws, r));

        Check(map.IsExplored(0, 0) && !map.IsExplored(0, 1), "only the robot's cell starts explored");
        Check(ws.Sensor.Reading == MapHandler.MapElement.Empty, "the sensor reads the cell ahead");
        Check(!map.IsExplored(0, 1), "the sensor does not explore");

        // A robot moved by hand must not carry into the run.
        map.StepRobot();
        Check(map.IdentifyRobot(out _) is { X: 1, Y: 0 } && map.IsExplored(0, 1), "a manual step explores");
        ws.PressTrace();
        Check(map.IdentifyRobot(out _) is { X: 0, Y: 0 } && !map.IsExplored(0, 1), "starting a run resets the map");
        Check(ws.RunActive && ws.Tracing, "a trace is running");
        Check(ws.ActiveLine == 1, "the trace waits on line 1");

        void Next()
        {
            ws.PressTrace();
            int guard = 0;
            int? before = ws.ActiveLine;
            do
            {
                ws.Update(0, 0);
                System.Threading.Thread.Sleep(2);
            } while (ws.ActiveLine == before && ws.RunActive && ++guard < 1000);
        }

        ws.Update(0, 0);
        Check(ws.ActiveLine == 1 && ws.VariablesPanel.Lines.Count == 0, "without Next nothing runs");
        Next();
        Check(ws.ActiveLine == 2 && ws.VariablesPanel.Lines.SequenceEqual(new[] { "n = 1" }),
            "after line 1 the value of n shows, line 2 is next");
        Next();
        Check(map.IsExplored(0, 1) && !map.IsExplored(0, 2), "isThere explored the cell ahead");
        Check(ws.VariablesPanel.Lines.Contains("g = false"), "g shows its value");

        // Taking manual control ends the run where it is, without restoring the map.
        Next();
        ws.RobotToggle.Click();
        Check(!ws.RunActive && !ws.Running, "manual control ended the run");
        Check(map.IdentifyRobot(out _) is { X: 3, Y: 0 }, "the robot stays where the program left it");
        Check(map.IsExplored(0, 2), "the walked cells are explored");
        Check(map.MineRobot() && map.GoldMined == 1, "manual mining works from there");
        ws.RobotToggle.Click();

        string log = File.ReadAllText(Runner.LogPath);
        Check(log.TrimEnd().EndsWith("EOR"), "the interrupted run's log is closed");

        if (File.Exists(Runner.LogPath)) File.Delete(Runner.LogPath);
    }

    /// <summary>Number and boolean reporters cover the two literal atoms in the grammar.</summary>
    static void TestLiteralParity()
    {
        foreach ((bool value, string text) in new[] { (true, "true"), (false, "false") })
        {
            Workspace ws = NewWorkspace();
            HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
            ws.AddRootBlock(hat, new Vector2(60, 60));

            StackBlock print = (StackBlock)ByKind(BlockKind.Print);
            var boolean = (BooleanBlock)ByKind(BlockKind.Boolean);
            boolean.Value = value;
            Check(print.AttachToSlot(print.Slots[0], boolean), $"{text}: print operand");
            Check(hat.Append(print), $"{text}: chained");

            AstResult result = AstConverter.Convert(ws);
            Check(result.Errors.Count == 0, $"{text}: converts cleanly");
            var instruction = (PrintInstruction)result.Scripts[0].Instructions[0];
            Check(instruction.Value is LiteralBool actual && actual.value == value,
                $"{text}: maps to LiteralBool({value})");
            Check(Normalize(MineCodeWriter.Write(result.Scripts[0])) == $"print {text}",
                $"{text}: renders as 'print {text}'");
        }

        Workspace numbers = NewWorkspace();
        HatBlock numbersHat = (HatBlock)ByKind(BlockKind.Start);
        numbers.AddRootBlock(numbersHat, new Vector2(60, 60));
        StackBlock printNumber = (StackBlock)ByKind(BlockKind.Print);
        var number = (NumberBlock)ByKind(BlockKind.Number);
        number.Text = "42";
        Check(printNumber.AttachToSlot(printNumber.Slots[0], number), "42: print operand");
        Check(numbersHat.Append(printNumber), "42: chained");
        AstResult numberResult = AstConverter.Convert(numbers);
        Check(numberResult.Errors.Count == 0, "42: converts cleanly");
        Check(((PrintInstruction)numberResult.Scripts[0].Instructions[0]).Value is Literal { value: 42 },
            "42: maps to Literal(42)");
        Check(Normalize(MineCodeWriter.Write(numberResult.Scripts[0])) == "print 42",
            "42: renders as 'print 42'");
    }

    /// <summary>The EBNF has block = { command }, so an empty mouth is a valid program
    /// and survives the trip to text and back.</summary>
    static void TestEmptyMouths()
    {
        Workspace ws = NewWorkspace();
        HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
        ws.AddRootBlock(hat, new Vector2(60, 60));

        CBlock ifBlock = (CBlock)ByKind(BlockKind.If, b => b is CBlock { HasElse: false });
        Check(ifBlock.AttachToSlot(ifBlock.Slots[0], Var("x")), "if took its condition");
        Check(hat.Append(ifBlock), "empty if chained");
        ws.Registry.TryCreate("x", out _);

        AstResult result = AstConverter.Convert(ws);
        Check(result.Errors.Count == 0, "an empty body is not a problem");
        Check(Normalize(MineCodeWriter.Write(result.Scripts[0])) == "if x", "it writes as a bare header");
        Check(MineCode.ParseCode("if x\nprint 1\n").Instructions.Length == 2,
            "the parser reads an empty body and carries on");
    }

    /// <summary>
    /// Exercises the draw paths the structural tests never touch: populated name slots,
    /// empty-field placeholders, badges, the Start button/status and the palette chrome.
    /// </summary>
    static void TestDrawSmoke()
    {
        Workspace ws = NewWorkspace();
        HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
        ws.AddRootBlock(hat, new Vector2(60, 60));

        StackBlock set = (StackBlock)ByKind(BlockKind.Set);
        set.Slots[0].SelectedName = "gold";
        Check(set.AttachToSlot(set.Slots[1], Num(7)), "draw smoke: set took its value");
        Check(hat.Append(set), "draw smoke: set chained");

        CBlock ifBlock = (CBlock)ByKind(BlockKind.If);
        Check(set.Append(ifBlock), "draw smoke: empty if chained");

        StackBlock print = (StackBlock)ByKind(BlockKind.Set);
        print.Slots[0].SelectedName = "sensed";
        ReporterBlock isThere = (ReporterBlock)ByKind(BlockKind.IsThere);
        Check(isThere.AttachToSlot(isThere.Slots[0], Var("wall")), "draw smoke: isThere took wall");
        Check(print.AttachToSlot(print.Slots[1], isThere), "draw smoke: set took isThere");
        Check(ifBlock.Append(print), "draw smoke: set chained");

        // Put the number into edit mode so the input box, focus border and caret
        // all get drawn by this pass, not just the resting state.
        ws.HandleClick(set.Slots[1].Content, null);
        Check(ws.Editing != null, "draw smoke: the number field takes the caret");

        ws.Update(0, 0);

        // The print-output field: themed size, flush with the bar's right edge, under it.
        Check(ws.Output.WPercent == Theme.OutputWPercent && ws.Output.HPercent == Theme.OutputHPercent, "the output field has its themed size");
        Check(ws.Output.X + ws.Output.Width == ws.StartButton.X + ws.StartButton.Width,
            "the output field's right edge lines up with the bar buttons");
        Check(ws.Output.Y >= ws.StartButton.Y + ws.StartButton.Height &&
              ws.Output.Y >= ws.SaveButton.Y + ws.SaveButton.Height,
            "the output field sits under the save/start bar");
        Check(ws.Output.Lines.Count == 0, "the output field starts empty");

        // The map panel: same width as the output, right under it, empty until the
        // host loads the handler the input file's matrix produced.
        Check(ws.MapPanel.Width == ws.Output.Width, "the map panel is as wide as the output field");
        Check(ws.MapPanel.Y == ws.Output.Y + ws.Output.Height + Theme.MapTopGap,
            "the map panel sits under the output field");
        Check(ws.MapPanel.Map == null, "the map panel starts empty");
        ws.MapPanel.SetMap(new MapHandler(new char[,]
        {
            { '.', '@', '#', '>' },
            { '<', '^', 'v', '.' },
        }));
        MapHandler? loadedMap = ws.MapPanel.Map;
        Check(loadedMap != null && loadedMap.Rows == 2 && loadedMap.Cols == 4,
            "the parsed map loads into the panel");
        Check(loadedMap != null && loadedMap.ElementAt(0, 2) == MapHandler.MapElement.Wall,
            "the panel holds the parsed wall cell");
        Direction? facing = null;
        Vector2Int? robot = loadedMap != null ? loadedMap.IdentifyRobot(out facing) : null;
        Check(robot is { } position && position.X == 3 && position.Y == 0 &&
              facing == Direction.Right,
            "the panel finds the robot where the handler parsed it");

        var toolbox = new Toolbox(() => 0, () => 0, 0, 100, BlockLibrary.CreateCategories());
        toolbox.Update(0, 0);

        // Popup paths: the dropdown (with a selected check mark) and the name prompt.
        ws.HandleClick(set, set.Slots[0]);
        ws.Prompt.Open(8, 8);
        ws.Prompt.InputChar('x');

        // One line in the output field, so this pass draws its text path too and
        // not just the empty-state hint.
        ws.Output.Lines.Add("print 1");

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Raylib.WHITE);
        toolbox.Draw(0, 0);
        ws.Draw(0, 0);
        Raylib.EndDrawing();

        Check(true, "workspace and toolbox drew without throwing");
    }

    /// <summary>Links a run of blocks head-first; refuses silently would hide a gap.</summary>
    /// <summary>Links a run of blocks head-first; refusing silently would hide a gap.</summary>
    static void Chain(Block head, params Block[] rest)
    {
        Block tail = head;
        foreach (Block b in rest)
        {
            Check(tail.Append(b), $"chained {b.Label} after {tail.Label}");
            tail = b;
        }
    }

    static bool Same(Body? a, Body? b)
    {
        if (a == null || b == null) return a == null && b == null;
        if (a.Instructions.Length != b.Instructions.Length) return false;
        for (int i = 0; i < a.Instructions.Length; i++)
            if (!SameInstruction(a.Instructions[i], b.Instructions[i])) return false;
        return true;
    }

    /// <summary>Structural equality including `Line`: the converted AST must carry the
    /// same line numbers the Backend parser produces for the generated source.</summary>
    static bool SameInstruction(Instruction a, Instruction b) => (a, b) switch
    {
        (StepInstruction x, StepInstruction y) =>
            x.Line == y.Line && SameValue(x.Value, y.Value),
        (TurnInstruction x, TurnInstruction y) =>
            x.Line == y.Line && SameValue(x.Value, y.Value),
        (MineInstruction x, MineInstruction y) => x.Line == y.Line,
        (PrintInstruction x, PrintInstruction y) =>
            x.Line == y.Line && SameValue(x.Value, y.Value),
        (SetInstruction x, SetInstruction y) =>
            x.Line == y.Line && x.variableName == y.variableName && SameValue(x.Value, y.Value),
        (IfInstruction x, IfInstruction y) =>
            x.Line == y.Line && SameValue(x.Expression, y.Expression) &&
            Same(x.body, y.body) && Same(x.elseBody, y.elseBody),
        (WhileInstruction x, WhileInstruction y) =>
            x.Line == y.Line && SameValue(x.Expression, y.Expression) && Same(x.body, y.body),
        _ => false,
    };

    static bool SameValue(Value? a, Value? b) => (a, b) switch
    {
        (Literal x, Literal y) => x.value == y.value,
        (LiteralBool x, LiteralBool y) => x.value == y.value,
        (Variable x, Variable y) => x.variable == y.variable,
        (BinOp x, BinOp y) => x.operation == y.operation &&
                              SameValue(x.left, y.left) && SameValue(x.right, y.right),
        (Not x, Not y) => SameValue(x.Value, y.Value),
        (IsThere x, IsThere y) => SameValue(x.Arg, y.Arg),
        (null, null) => true,
        _ => false,
    };

    // ------------------------------------------------------ validation badges --

    static bool HasError(AstResult result, Block block, string fragment)
        => result.Errors.Any(e => ReferenceEquals(e.Block, block) && e.Message.Contains(fragment));

    static void TestValidation()
    {
        Workspace ws = NewWorkspace();

        HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
        ws.AddRootBlock(hat, new Vector2(60, 60));

        // 1. print <> with nothing in the field
        StackBlock printEmpty = (StackBlock)ByKind(BlockKind.Print);
        Check(hat.Append(printEmpty), "empty-field print chained");

        // 2. set empty to 1 - nobody created empty
        StackBlock setBuiltin = (StackBlock)ByKind(BlockKind.Set);
        setBuiltin.Slots[0].SelectedName = "empty";
        Check(setBuiltin.AttachToSlot(setBuiltin.Slots[1], Num(1)), "set took its value");
        Check(printEmpty.Append(setBuiltin), "second unknown set chained");

        // 3. set ghost to 1 - nobody created ghost
        StackBlock setUnknown = (StackBlock)ByKind(BlockKind.Set);
        setUnknown.Slots[0].SelectedName = "ghost";
        Check(setUnknown.AttachToSlot(setUnknown.Slots[1], Num(1)), "set took its value");
        Check(setBuiltin.Append(setUnknown), "unknown-variable set chained");

        // 4. a set whose name field was never used
        StackBlock setNoName = (StackBlock)ByKind(BlockKind.Set);
        Check(setNoName.AttachToSlot(setNoName.Slots[1], Num(1)), "set took its value");
        Check(setUnknown.Append(setNoName), "nameless set chained");

        // 5. print <ghost> - an unknown variable reporter
        StackBlock printGhost = (StackBlock)ByKind(BlockKind.Print);
        VariableBlock ghostVar = Var("ghost");
        Check(printGhost.AttachToSlot(printGhost.Slots[0], ghostVar), "print took its variable");
        Check(setNoName.Append(printGhost), "unknown reporter chained");

        // 6. print <isThere <1 + 2>> - refused on the way in, so the field stays empty
        StackBlock printNest = (StackBlock)ByKind(BlockKind.Print);
        ReporterBlock nested = (ReporterBlock)ByKind(BlockKind.IsThere);
        ReporterBlock add = (ReporterBlock)ByKind(BlockKind.Add);
        Check(add.AttachToSlot(add.Slots[0], Num(1)), "add took its left operand");
        Check(add.AttachToSlot(add.Slots[1], Num(2)), "add took its right operand");
        Check(!nested.AttachToSlot(nested.Slots[0], add), "isThere refuses an expression");
        Check(!printNest.AttachToSlot(printNest.Slots[0], nested), "print refuses isThere");
        Check(printGhost.Append(printNest), "empty print chained");

        // 7. a block that is not a statement at all
        StackBlock notAStatement = new("helper", Cat(), Pos.Start, Pos.Start);
        Check(printNest.Append(notAStatement), "non-statement chained");

        // 8. a root stack with no hat
        StackBlock hatless = new("orphan", Cat(), Pos.Start, Pos.Start);
        ws.AddRootBlock(hatless, new Vector2(640, 120));

        // 9. a second hat
        HatBlock secondHat = (HatBlock)ByKind(BlockKind.Start);
        ws.AddRootBlock(secondHat, new Vector2(140, 560));

        ws.Update(0, 0);
        Healthy(ws, "validation workspace");
        Check(Roots(ws).Count == 3, "three root stacks: the script, an orphan, a second hat");

        AstResult result = ws.RunStart();
        string problems = string.Join(" | ", result.Errors.Select(e => $"[{e.Block.Label}] {e.Message}"));

        Check(result.Errors.Count == 9, $"exactly nine problems (got {result.Errors.Count}): {problems}");
        Check(result.Scripts.Length == 1, "only the hat's stack becomes a script");

        Check(HasError(result, printEmpty, "empty field"), "an empty field is badged");
        Check(HasError(result, setBuiltin, "unknown variable empty"), "set empty to ... is badged");
        Check(HasError(result, setUnknown, "unknown variable ghost"), "an unknown set target is badged");
        Check(HasError(result, setNoName, "no variable chosen"), "a nameless set is badged");
        Check(HasError(result, ghostVar, "unknown variable ghost"), "an unknown reporter is badged");
        Check(HasError(result, printNest, "empty field"), "the refused print stays empty and badged");
        Check(HasError(result, notAStatement, "can't be a step"), "a non-statement block is badged");
        Check(HasError(result, hatless, "no Start block"), "a hatless root is badged");
        Check(HasError(result, secondHat, "only one Start block"), "a second hat is badged");

        Check(ws.Validation.Errors.Count == 9,
            "the live badges agree with the Start-button result");
    }

    /// <summary>
    /// Start runs the script on the Backend VM through the real Main/Runner wiring:
    /// OnInstruction maps the robot onto the map panel and moves the active-line
    /// highlight after every instruction, the run clears the highlight at the end,
    /// and a failure comes back through MachineSpirit's log without an exception.
    /// </summary>
    static void TestVmRun()
    {
        Workspace ws = NewWorkspace();

        // '>' robot at (0,0) facing right, open floor to (2,0) where the gold waits.
        ws.LoadMap(new MapHandler(new[,]
        {
            { '>', '.', '@', '.' },
            { '#', '.', '.', '.' },
            { '.', '.', '.', '.' },
        }));

        HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
        StackBlock step1 = (StackBlock)ByKind(BlockKind.Step);
        Check(step1.AttachToSlot(step1.Slots[0], Num(1)), "step took its number");
        StackBlock step2 = (StackBlock)ByKind(BlockKind.Step);
        Check(step2.AttachToSlot(step2.Slots[0], Num(1)), "step took its number");
        StackBlock mine = (StackBlock)ByKind(BlockKind.Mine);
        Check(hat.Append(step1) && step1.Append(step2) && step2.Append(mine),
            "step/step/mine chained under the Start block");
        ws.LoadRoot(hat, 40, 40); // parents every block, the way the file loader does

        ws.Update(0, 0);
        AstResult? captured = null;
        ws.RunProgram = r => captured = r;
        AstResult result = ws.RunStart();
        Check(captured != null && result.Errors.Count == 0 && result.Scripts.Length == 1,
            "Start hands the converted script to the host runner");
        Check(step1.RunLine == 1 && step2.RunLine == 2 && mine.RunLine == 3,
            "every statement owns its generated source line");

        ws.HighlightLine(2);
        Check(ws.ActiveLine == 2, "the running line is exposed to the block highlight");
        ws.HighlightLine(null);

        // The real wiring, minus the frame presenter: each record is what the
        // OnInstruction handler left behind when its instruction finished.
        MapHandler map = ws.MapPanel.Map!;
        var seen = new List<(int? Line, Vector2Int? Robot, bool Mined)>();
        Runner.Run(ws, result, () =>
            seen.Add((ws.ActiveLine, map.IdentifyRobot(out _), map.IsMined(0, 2))));

        Check(seen.Count == 3, $"three instructions were reported, got {seen.Count}");
        if (seen.Count == 3)
        {
            Check(seen[0].Line == 1 && seen[0].Robot is { X: 1, Y: 0 },
                "step one: line 1 highlighted, robot moved to (1,0)");
            Check(seen[1].Line == 2 && seen[1].Robot is { X: 2, Y: 0 },
                "step two: line 2 highlighted, robot moved to (2,0)");
            Check(seen[2].Line == 3 && seen[2].Mined,
                "mine: line 3 highlighted, the gold under the robot is mined");
        }
        Check(ws.ActiveLine == null, "the highlight clears when the run ends");
        Check(map.GoldMined == 1 && map.IsMined(0, 2), "the mined gold shows on the handler");
        Check(map.IdentifyRobot(out _) is { X: 2, Y: 0 }, "the marker followed the spirit to the end");
        Check(ws.Output.GoldMined == 1 && ws.Output.TotalGold == 1,
            "the gold row followed the run");

        // A failing run (mine with no gold underfoot) reports through the log and
        // still leaves the workspace clean - no highlight, no mined gold, no throw.
        Workspace bare = NewWorkspace();
        bare.LoadMap(new MapHandler(new[,] { { '>', '.' } }));
        HatBlock bareHat = (HatBlock)ByKind(BlockKind.Start);
        StackBlock bareMine = (StackBlock)ByKind(BlockKind.Mine);
        Check(bareHat.Append(bareMine), "the doomed script chained");
        bare.LoadRoot(bareHat, 20, 20);
        bare.Update(0, 0);
        AstResult doomed = bare.RunStart(); // no runner wired: converts like before
        Check(doomed.Errors.Count == 0, "the doomed script converts cleanly");

        Runner.Run(bare, doomed);
        Check(bare.ActiveLine == 1, "a failed run keeps the failing line highlighted");
        Check(bare.MapPanel.Map!.GoldMined == 0, "a failed mine mines nothing");
        string log = File.ReadAllText(Runner.LogPath);
        Check(log.Contains("E_NO_GOLD") && log.Contains("EOR"),
            "the spirit logged the failure and the end of the run");

        if (File.Exists(Runner.LogPath)) File.Delete(Runner.LogPath);
    }

    /// <summary>
    /// print shows its resolved value in the output field: the API wrapper keeps the
    /// VM's log parameter and the OnInstruction handler consumes it exactly when the
    /// executed statement is a print - set and the other statements never write.
    /// </summary>
    static void TestPrintOutput()
    {
        Workspace ws = NewWorkspace();
        ws.LoadMap(new MapHandler(new[,] { { '>', '.' } }));
        Check(ws.Registry.TryCreate("n", out _), "the test variable exists");

        HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
        StackBlock set = (StackBlock)ByKind(BlockKind.Set);
        set.Slots[0].SelectedName = "n"; // the name dropdown picks the variable
        Check(set.AttachToSlot(set.Slots[1], Num(5)), "set took its value");
        StackBlock printVar = (StackBlock)ByKind(BlockKind.Print);
        Check(printVar.AttachToSlot(printVar.Slots[0], Var("n")), "print took its variable");
        StackBlock printNum = (StackBlock)ByKind(BlockKind.Print);
        Check(printNum.AttachToSlot(printNum.Slots[0], Num(7)), "print took its number");
        Check(hat.Append(set) && set.Append(printVar) && printVar.Append(printNum),
            "set/print/print chained");
        ws.LoadRoot(hat, 40, 40);
        ws.Update(0, 0);

        AstResult result = ws.RunStart();
        string problems = string.Join(" | ", result.Errors.Select(e => e.Message));
        Check(result.Errors.Count == 0, $"set/print converts cleanly, got: {problems}");

        // One snapshot per reported instruction: the append happens in the handler
        // before the presenter runs, so the snapshot shows exactly what was visible.
        var snapshots = new List<string[]>();
        Runner.Run(ws, result, () => snapshots.Add(ws.Output.Lines.ToArray()));

        Check(snapshots.Count == 3, $"three instructions were reported, got {snapshots.Count}");
        if (snapshots.Count == 3)
        {
            Check(snapshots[0].Length == 0, "set prints nothing");
            Check(snapshots[1].Length == 1 && snapshots[1][0] == "5",
                "print n put its value in the field");
            Check(snapshots[2].Length == 2 && snapshots[2][0] == "5" && snapshots[2][1] == "7",
                "print 7 appended its bare value after it");
        }
        Check(ws.Output.Lines.Count == 2 && ws.Output.Lines[0] == "5" && ws.Output.Lines[1] == "7",
            $"the field ends holding both printed values, got [{string.Join(",", ws.Output.Lines)}]");
        Check(ws.ActiveLine == null, "the highlight cleared after the print run");

        // The same story through the file path the app uses: AstLoader rebuilds the
        // blocks from parsed source, then Start runs them.
        Workspace loaded = NewWorkspace();
        loaded.LoadMap(new MapHandler(new[,] { { '>' } }));
        TextWriter savedOut = Console.Out;
        Body fromFile;
        try
        {
            Console.SetOut(TextWriter.Null);
            fromFile = MineCode.ParseCode("set n to 5\nprint 7\nprint n");
        }
        finally
        {
            Console.SetOut(savedOut);
        }
        List<string> loadProblems = AstLoader.Load(loaded, [fromFile]);
        Check(loadProblems.Count == 0,
            "the file-style script loaded, got: " + string.Join(" | ", loadProblems));
        loaded.Update(0, 0);
        loaded.RunProgram = r => Runner.Run(loaded, r);
        AstResult loadedResult = loaded.RunStart();
        Check(loadedResult.Errors.Count == 0, "the file-style script converts");
        Check(loaded.Output.Lines.Count == 2 &&
              loaded.Output.Lines[0] == "7" && loaded.Output.Lines[1] == "5",
            $"the loaded script prints 7 then 5, got [{string.Join(",", loaded.Output.Lines)}]");
        loaded.StopRun();
        Check(loaded.Output.Lines.Count == 0, "stopping clears the loaded run's output too");

        if (File.Exists(Runner.LogPath)) File.Delete(Runner.LogPath);
    }

    /// <summary>
    /// Start opens a run session: the button becomes the red Stop, the program runs
    /// and leaves its result on the map, and only a Stop click closes the session -
    /// putting the map back to its pre-start state and making the next start legal.
    /// Wired exactly like the app: Start hands the script to the real Runner.
    /// </summary>
    static void TestStopButton()
    {
        Workspace ws = NewWorkspace();

        // '>' at (0,0) facing right, floor at (1,0), gold at (2,0): the script walks
        // there and mines, so both position and mined marks drift from the start.
        ws.LoadMap(new MapHandler(new[,] { { '>', '.', '@' } }));

        HatBlock hat = (HatBlock)ByKind(BlockKind.Start);
        StackBlock step1 = (StackBlock)ByKind(BlockKind.Step);
        Check(step1.AttachToSlot(step1.Slots[0], Num(1)), "step took its number");
        StackBlock step2 = (StackBlock)ByKind(BlockKind.Step);
        Check(step2.AttachToSlot(step2.Slots[0], Num(1)), "step took its number");
        StackBlock mine = (StackBlock)ByKind(BlockKind.Mine);
        StackBlock print = (StackBlock)ByKind(BlockKind.Print);
        Check(print.AttachToSlot(print.Slots[0], Num(7)), "print took its number");
        Check(hat.Append(step1) && step1.Append(step2) && step2.Append(mine) && mine.Append(print),
            "step/step/mine/print chained");
        ws.LoadRoot(hat, 40, 40);
        ws.Update(0, 0);

        MapHandler map = ws.MapPanel.Map!;
        Check(!ws.Running && !ws.StartButton.IsStop, "idle: Start is Start");

        ws.RunProgram = r => Runner.Run(ws, r); // the app's wiring, minus the presenter
        AstResult result = ws.RunStart();
        Check(result.Errors.Count == 0, "the script converts cleanly");
        Check(ws.Running, "the session is open even though the program already finished");
        ws.Update(0, 0);
        Check(ws.StartButton.IsStop, "the button became Stop");
        Check(map.IdentifyRobot(out _) is { X: 2, Y: 0 } && map.GoldMined == 1,
            "the program ran: the robot walked and mined");
        Check(ws.ActiveLine == null, "the finished run leaves no highlight");
        Check(ws.Output.Lines.Count == 1 && ws.Output.Lines[0] == "7",
            "the run's print is in the output");

        // Robot control greys Start out, but the Stop face must stay reachable.
        ws.RobotToggle.Click();
        ws.Update(0, 0);
        Check(ws.StartButton.Disabled && ws.StartButton.IsStop,
            "control greys the button while the Stop face stays");
        ws.RobotToggle.Click();

        // Stop closes the session and puts the map back to what Start was given.
        ws.StopRun();
        Check(!ws.Running, "Stop closes the session");
        Check(ws.Output.Lines.Count == 0, "Stop clears the output window");
        ws.Update(0, 0);
        Check(!ws.StartButton.IsStop, "the button is Start again");
        Check(map.IdentifyRobot(out Direction? home) is { X: 0, Y: 0 } && home == Direction.Right,
            "the robot is back where Start was clicked, facing the original way");
        Check(map.ElementAt(0, 2) == MapHandler.MapElement.Gold && !map.IsMined(0, 2) &&
              map.GoldMined == 0, "the mined gold came back");

        // With the session closed, the program can start again from the restored state.
        AstResult again = ws.RunStart();
        Check(again.Errors.Count == 0 && ws.Running, "after Stop the program can start again");
        Check(map.IdentifyRobot(out _) is { X: 2, Y: 0 } && map.GoldMined == 1,
            "the second run walked and mined from the restored start");
        Check(ws.Output.Lines.Count == 1 && ws.Output.Lines[0] == "7",
            "the second run prints into the output again");
        ws.StopRun();
        Check(!ws.Running && map.GoldMined == 0 && map.IdentifyRobot(out _) is { X: 0, Y: 0 } &&
              ws.Output.Lines.Count == 0,
            "the second Stop restores the map and empties the output too");

        if (File.Exists(Runner.LogPath)) File.Delete(Runner.LogPath);
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
            else if (action < 84)
            {
                // Park a reporter in an empty value field: the slot path the
                // graph fuzz never exercised (the cycle-via-slot hazard).
                ParkIntoSlot(ws, all, rng);
            }
            else if (action < 92)
            {
                // ...and pull one back out again.
                PullOutOfSlot(ws, all, rng);
            }
            else if (action < 97)
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

    /// <summary>Grabs a loose reporter and drops it lined up with an empty value field.</summary>
    static void ParkIntoSlot(Workspace ws, List<Block> all, Random rng)
    {
        var empties = new List<Slot>();
        foreach (Block b in all)
            foreach (Slot s in b.Slots)
                if (s.Kind == SlotKind.Value && s.Content == null)
                    empties.Add(s);

        List<ReporterBlock> loose = all.OfType<ReporterBlock>().Where(r => r.Slot == null).ToList();
        if (empties.Count == 0 || loose.Count == 0) return;

        ReporterBlock reporter = loose[rng.Next(loose.Count)];
        Slot slot = empties[rng.Next(empties.Count)];

        _context = $"park {reporter.Label} into {slot.Host?.Label}";
        Block? grabbed = ws.HandlePress(new Vector2(reporter.X + 3, reporter.Y + 3), out _);
        if (grabbed == null) return;

        Healthy(ws, "fuzz/after-park-grab", grabbed);
        Check(grabbed.Slot == null && grabbed.Previous == null && grabbed.Container == null,
            "the grabbed reporter came loose");

        Vector2 center = new(slot.Bounds.X + slot.Bounds.width / 2f,
                             slot.Bounds.Y + slot.Bounds.height / 2f);
        grabbed.Layout((int)center.X, (int)center.Y);

        SnapTarget? snap = ws.FindSnap(grabbed);
        _context = $"park {grabbed.Label} into {slot.Host?.Label} -> " +
                   (snap == null ? "no snap" : $"{snap.Kind} {snap.Target.Label}");
        ws.Drop(grabbed, snap);
        Healthy(ws, "fuzz/after-park");
    }

    /// <summary>Detaches a parked reporter and re-homes it: another field, or free.</summary>
    static void PullOutOfSlot(Workspace ws, List<Block> all, Random rng)
    {
        List<Block> parked = all.Where(b => b.Slot != null).ToList();
        if (parked.Count == 0) return;

        Block content = parked[rng.Next(parked.Count)];
        Slot? oldSlot = content.Slot;
        _context = $"pull {content.Label} out of {oldSlot?.Host?.Label}";

        content.Detach();
        Check(content.Slot == null, "the reporter left its field");
        Check(oldSlot == null || oldSlot.Content == null, "the field it left is empty again");
        Healthy(ws, "fuzz/after-pull", content);

        content.Layout(rng.Next(0, 900), rng.Next(0, 700));
        SnapTarget? snap = ws.FindSnap(content);
        _context = $"re-home {content.Label} -> " +
                   (snap == null ? "free floating" : $"{snap.Kind} {snap.Target.Label}");
        ws.Drop(content, snap);
        Healthy(ws, "fuzz/after-rehome");
    }
}
