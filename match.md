# Spec match: Robotszimulátor.pdf vs. the code

Reviewed against all 19 pages of the task PDF (Dusza 2026/27, round 1).

**How this was checked:** by reading the code only. Nothing was built or run, because a build would write to `bin/` and `obj/`. That includes the 41 cases in `error_tests/`, so their current pass/fail state is unknown. Every bug below was traced by hand through the source. I read all of Backend, Shared, Main and the Gui code, and only searched `Gui/SelfTest/SelfTest.cs`. `Gui/Frame.cs`, `Element.cs`, `Dim.cs` and `Pos.cs` got a grep only.

## Status

Items 1-31 below have since been fixed in the working tree, uncommitted. The rest of this file is the original review, kept as written so each fix can be traced to its finding. Still open for the team: the video, `results.zip`, and the marked parts of the `ai.md` draft.

## Bottom line

The language pipeline (parser, bytecode, VM, log format) is close, and the block editor has a solid base. Three things put the most points at risk:

1. **`-q` is not implemented and the output-file argument is ignored.** The contest tests (160 of 300 points) depend on both.
2. **Two logic bugs will fail hidden tests:** a mined cell becomes a wall, and any `if`/`while` inside an `else` branch loops forever.
3. **Four required UI features do not exist:** the text source view, step-by-step tracing with variable values, explored-area display, and the sensor display. The UI is worth 140 points and the video script walks through all four.

## Fix list, in priority order

### P0: contest tests (160 points)

| # | Problem | Spec | Where | Fix |
|---|---------|------|-------|-----|
| 1 | **`-q` still opens the GUI.** The flag only shifts the argument index. No headless run exists in `Main`, so `results.zip` cannot be produced. | p.1 | `Main/Program.cs:23,63,129` | When `-q` is given, run the pipeline and exit before `new App()`. `Test/Tester.cs:15` `RunFile` is already that pipeline. |
| 2 | **Output filename is ignored.** Logs always go to `output.log`, and the startup writes `Test line 1/2` into it. | p.1, p.11 | `Main/Runner.cs:21`, `Main/Program.cs:56-60` | Pass `args[1]` (or `args[2]` with `-q`) to the runner. Delete the test logger. |
| 3 | **A mined cell turns into a wall.** Afterwards `isThere 1` is true, `isThere 0` is false, and stepping back onto it gives `E_BEDROCK_HIT`. The spec only says the cell has no more gold, and a cell with no gold is empty (p.2, p.6). | p.6 | `Shared/MapHandler.cs:214` | Set `MapElement.Empty`. Then remove the `IsMined` workaround at `MapHandler.cs:106`. `SelfTest.cs:542-548` asserts the wrong behaviour and must change too. |
| 4 | **`if`/`while` inside an `else` re-runs forever.** `elseInstructions` starts with the `Nop` at index 0, but nested `resumePc` values are computed without it. The inner block returns to its own entry `Ret` and repeats until the 1000-line limit. Example below. | p.3, p.5, p.11 | `Backend/Parsing/BytecodeEmitter.cs:43,333-334` | Offset `resumePc` by 1 for else bodies, for example with a start-offset parameter on `ParseBody`. |
| 5 | **A program that ends exactly on line 1000 reports the limit.** The check runs before the injected `Ret`/`End`, so no further source line is needed for it to fire. The spec only stops if another line is about to be processed. Example below. | p.12 | `Backend/Runtime/VM.cs:113` | Skip injected instructions before testing the counter, and check only before a real line. |
| 6 | **Straight-line programs over 1000 lines get `EOR` instead of the limit message.** `programCounter<1000` ends the loop silently. It can also cut off shorter programs, since `set x to 1 + 2` uses two bytecodes. | p.12 | `Backend/Runtime/VM.cs:111` | Remove `programCounter<1000`. |
| 7 | **Empty blocks fail to parse.** The EBNF says `block = { command }` (zero or more), but the grammar requires at least one instruction, so such input throws. This is my reading of the EBNF, and the indentation rules do not settle it. It is cheap to support. | p.3 | `lang/MineCodeParser.g4:13` | Make `INDENT instructions DEDENT` optional in `body`, and regenerate. |

Example for #4. Expected log is `1`, `3`, `4`, `5 print 2`, `6 print 3`, `EOR`. Actual log repeats lines 4 and 5 until `INSTRUCTION_COUNT_LIMIT_EXCEEDED`.

```
if false
  print 1
else
  if true
    print 2
print 3
```

Example for #5. This processes exactly 1000 lines (2 + 3 × 332 + 1 + 1), so it should end `6 print 332`, `EOR`.

```
set i to 0
set go to true
while go
  set i to i + 1
  set go to i < 332
print i
```

### P1: required UI features (140 points)

| # | Problem | Spec | Where | Fix |
|---|---------|------|-------|-----|
| 8 | **No text source view.** `MineCodeWriter` output only reaches the console and Save. The video needs it to update live during editing. | p.7, p.18 step 3 | `Gui/Workspace.cs:852,953` | Add a read-only panel rendering `_validation.ToMineCode()` each frame, with line numbers. |
| 9 | **No step-by-step tracing.** `RunCode(bool pause)` ignores `pause` and there is no Step control. Runs finish in one blocking call. | p.7, p.18 step 6 | `Backend/Runtime/VM.cs:106`, `Main/Runner.cs:94` | Add a "Trace" mode where the run waits for a Next click or key inside the instruction callback. The block highlight already exists, and the spec accepts tracing in one view only. |
| 10 | **No variable values on screen.** Names are dropped in the emitter; the VM keeps only id-indexed slots. | p.7, p.18 step 6 | `BytecodeEmitter.cs:16`, `VM.cs:24` | Expose the name-to-id map in `BytecodeHeader` and draw a variables panel. |
| 11 | **Explored area is not shown.** The whole map is always visible. Visited cells and `isThere` targets must look different from unexplored ones. | p.12 | `Gui/MapField.cs:78-109` | Track an explored set, updated in `MachineSpirit.move`, `isThere`, and manual moves. Reset it on run start. |
| 12 | **Sensor is not shown.** It must show the cell directly ahead and refresh on every move or turn, without marking that cell explored. | p.13 | none exists | Draw it from `GetForward` for empty, wall or gold. Show wall at the map edge. |
| 13 | **Run start does not reset the map.** Start only checkpoints, so a robot moved by hand or mined gold carries into the run. | p.7 | `Gui/Workspace.cs:859` | Call `ResetMap()` before the run. |
| 14 | **Manual control cannot interrupt a run.** During a run only the Stop button is polled, so the checkbox and robot buttons are dead. The spec says manual control aborts the current run and stops logging. | p.13, p.18 step 7 | `Main/Runner.cs:86`, `Gui/Workspace.cs:477-488` | Poll the toggle in the run loop and end the run without further logging. With #9 this also enables step 7. |
| 15 | **Keywords are accepted as variable names.** `while`, `IF` and similar all pass. Because the GUI runs the AST directly, such a program even executes. The spec requires rejection in any letter case. | p.4, p.9, p.18 step 8.1 | `Gui/Variables.cs:68-88` | Reject the 18 keywords from p.4, case-insensitively. |
| 16 | **No nesting limit.** Control blocks nest without bound. The editor must refuse a fourth level. | p.10, p.18 step 8.2 | `Gui/Blocks.cs:400,667,687`, `Gui/Workspace.cs:713` | Compute depth in `FindSnap` and all attach paths, counting the dragged chain's own depth. |
| 17 | **"Insert before" breaks the chain.** Dropping above a block cuts that block from its parent and makes a new free-floating root. Above a mid-script block, the script splits from Start. Above the first block in a mouth, the block leaves the `if`/`while`. Video steps 3.7 and 3.9 are exactly this move. `SelfTest.cs:350-354` asserts the split. | p.8 | `Gui/Workspace.cs:821-831` | Insert into the target's old position: `Previous.AttachBelow`, or the matching mouth when `Container` is set. |
| 18 | **Operator blocks go into every value field.** `if a + 1`, `print isThere 2` and `step forward x * 2` validate cleanly, though those fields are `simpleval` only. Value blocks belong only in `set ... to`. Nesting inside an operator is accepted on drop and only badged later. | p.8, p.9 | `Gui/Blocks.cs:147-161`, `Gui/Ast.cs:177-183,209,217` | In `AttachToSlot`, accept only simple reporters unless the slot is `set`'s value field. |
| 19 | **Numbers above 100 are allowed.** The field takes up to 9 digits. | p.4 | `Gui/ValueBlocks.cs:15` | Clamp input to 0-100. |
| 20 | **`gold`, `wall` and `empty` are treated as reserved.** The spec allows them as variable names. A loaded `set gold to 1` gets a "read-only" error and cannot run, and `print gold` silently becomes `print 2`. | p.4, p.7 | `Gui/Variables.cs:15,75`, `Gui/Ast.cs:188,277`, `Gui/AstLoader.cs:173` | Keep the cell blocks but drop the name reservation. |
| 21 | **An empty program cannot be run from the GUI.** No Start hat is created when the file has no code, so Start stays disabled and no `EOR`-only log is written. | p.12 | `Main/Program.cs:108` | Always load a Start hat. |

### P2: smaller gaps and polish

| # | Item | Where |
|---|------|-------|
| 22 | The new-variable prompt has no Backspace. `NamePrompt.Backspace` is never called, so after a rejected `while` the user must Esc and start over. | `Gui/Workspace.cs:410-417` |
| 23 | Run speed is 60 ms per line, too fast to follow, and step 8.4 still takes about 60 s of the 10-minute video. A speed control solves both. | `Main/Runner.cs:25` |
| 24 | `step forward n` jumps n cells in one frame. Animating cell by cell would be easier to follow. | `Backend/MachineSpirit.cs:80`, `Main/Runner.cs:71` |
| 25 | A failing `print x` shows as `x = E_UNINITIALIZED`, because errors go through the print formatter. | `Main/Runner.cs:98`, `Gui/Workspace.cs:908` |
| 26 | The panel prints `n = 5`, while the spec says print outputs the value. | `Gui/Workspace.cs:911` |
| 27 | Labels differ from the language: `is there`, `if ... then`, `while ... do`. The spec's figures use `isThere`, `if`, `while`. | `Gui/BlockCategory.cs:58-60,89` |
| 28 | Two block forms produce the same text (cell block `wall` and number `1`), which weakens the one-to-one rule on p.7. | `Gui/BlockCategory.cs:93-95` |
| 29 | The grid is always 15×15, so the video's 5×10 map is tiny and sits among empty grid. Scale tiles to the map. | `Gui/MapField.cs:21,63` |
| 30 | The workspace has no scrolling, and the video program nearly fills the default 650 px window. | `Gui/Workspace.cs:579-581` |
| 31 | Closing the window during a run does nothing until the run ends. | `Main/Runner.cs:67-92` |

## Requirement checklist

Status: **OK** met, **PARTIAL** partly met, **MISSING** not there, **BUG** present but wrong. Numbers refer to the fix list.

### 1. Input (p.1-2)

| Requirement | Status | Note |
|-------------|--------|------|
| `robot.exe input output` | PARTIAL | Input read, output ignored (#2) |
| `-q` headless, auto-exit | MISSING | #1 |
| Map header, 1-15 dimensions | OK | `FileHandler.cs:42-57`, limit checked in `Program.cs:30` |
| Symbols `. @ # > < ^ v` | OK | `MapHandler.cs:168-194` |
| Program directly after the map, may be empty | OK | Parser handles it; GUI see #21 |

### 2. Language (p.3-6)

| Requirement | Status | Note |
|-------------|--------|------|
| Grammar for all commands and operators | OK | `.g4` accepts a superset, harmless for valid input |
| Empty block | BUG | #7 |
| Case-sensitive keywords and identifiers | OK | Lexer |
| Indentation, per-block width | OK | `MineCodeLexerSuperClass.cs` |
| Arithmetic, comparison, equality, logic types | OK | `VM.cs:223-332` |
| No short-circuit, left-to-right, `=` checks after both reads | OK | `VM.cs:280-291` |
| `set` first-assign type lock | OK | `VM.cs:399-421` |
| `step` partial move, leaves gold | OK | `MachineSpirit.cs:80-89` |
| `turn by` left quarter-turns | OK | `Direction.cs:10` |
| `mine here` | BUG | #3 |
| `isThere`, map edge counts as wall | OK | `MapHandler.cs:196-207` |
| `print` to console | OK | `VM.cs:380-397` |
| All 7 error codes, run ends on error | OK | `VM.cs:54-64` |
| Nested control flow | BUG | Inside `else` only (#4) |

### 3. IDE and block editor (p.7-10)

| Requirement | Status | Note |
|-------------|--------|------|
| Load input file | OK | `Program.cs:88-98` |
| Text source view | MISSING | #8 |
| Block view | OK | |
| Edit with blocks | OK | Drag and snap |
| Palette: commands, control, value blocks | OK | `BlockCategory.cs` |
| Insert after / into a body | OK | `Workspace.cs:805-819` |
| Insert before | BUG | #17 |
| Incomplete programs allowed while editing | OK | Live badges, `while x` before `set x` works |
| Reject keyword variable names | BUG | #15 |
| Reject invalid names such as `9lives` | OK | `Variables.cs:65` |
| Value blocks only in `set`, no nesting | BUG | #18 |
| Numbers 0-100 | BUG | #19 |
| Max 3 nesting levels | MISSING | #16 |
| Slowed run without tracing | PARTIAL | Works, but fast (#23) |
| Tracing with line and variable display | MISSING | Highlight exists; #9, #10 |
| Map and robot reset on run start | BUG | #13 |

### 4. Logging (p.10-12)

| Requirement | Status | Note |
|-------------|--------|------|
| File named by argument | BUG | #2 |
| Overwritten every run | OK | `FileMode.Create`, `FileHandler.cs:124` |
| Line numbers, `while` every check, `else` only when taken | OK | `BytecodeEmitter.cs:333,362` |
| API name, value, return, error order | OK | `Bytecode.cs:305-313`, `MachineSpirit.cs:41-42` |
| Uninitialized parameter omits the value | OK | `VM.cs:476-479` |
| `EOR` always | OK | `VM.cs:140,144` |
| 1000-line limit | BUG | Loops OK; boundary #5, straight-line #6 |

### 5. Simulator (p.12-13)

| Requirement | Status | Note |
|-------------|--------|------|
| Map, robot position and direction | OK | `MapField.cs` |
| Explored vs. unexplored | MISSING | #11 |
| Sensor | MISSING | #12 |
| Gold count | OK | `OutputField.cs:77-91` |
| Automatic run | OK | |
| Manual `turn by`, `step forward`, `mine here` | OK | Stops at walls with no error, not logged |
| Manual control interrupts a run | MISSING | #14 |

### 6. Deliverables (p.13-15)

| Requirement | Status | Note |
|-------------|--------|------|
| `build.md` in zip root | MISSING | Not in the repo |
| `ai.md` with tools, usage, verification, 5 cases | MISSING | Not in the repo |
| Runnable binary in `project.zip` | PARTIAL | Output is named `Main`, since no `AssemblyName` is set. `robot` would match the PDF. Needs a build the graders can run. |
| Batch run of 200+ inputs within 30 minutes | MISSING | Needs #1 and a small loop script |
| `input_video.txt` | MISSING | Needed for the video |
| `bemutato.mp4`, `results.zip` | MISSING | Produced at the end |

## Video script walkthrough (p.17-19)

| Step | Can it be shown today? |
|------|------------------------|
| 1. Start from the command line | Partly: the output name is ignored (#2) |
| 2. Map, robot, sensor | Map yes, sensor no (#12) |
| 3. Build the program from empty | Blocks yes, after adding a Start hat first. Text view no (#8). Steps 3.7 and 3.9 risk the chain split (#17). |
| 4. Run without tracing | Runs. No explored area or sensor, and the log is in the wrong file. |
| 5. Run the program from the file | Same as step 4 |
| 6. Trace step by step with variables | No (#9, #10) |
| 7. Manual control mid-trace | No (#9, #14) |
| 8.1 Reject `while`, `9lives` | `9lives` yes, `while` no (#15) |
| 8.2 Refuse a fourth level | No (#16) |
| 8.3 Error on `mine here` | Yes, shown in the output panel |
| 8.4 Limit message | Yes, after about 60 s (#23) |
| 9-10 | Team content |

## Test coverage gaps

- None of the 41 `error_tests` inputs contains an `else`, which is why #4 went unnoticed.
- None steps back onto a mined cell or runs `isThere` at one (#3).
- `expected_041` covers the limit only through an endless loop. Nothing covers the exact-1000 boundary (#5) or a long straight-line program (#6).
- `SelfTest.cs:350-354` and `SelfTest.cs:542-548` lock in bugs #17 and #3, and need updating with those fixes.
