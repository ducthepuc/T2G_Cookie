# Design: Input slots, variables, and block → AST conversion

Date: 2026-11-07
Status: approved (section-by-section) — awaiting spec review

## Goal

Turn the block editor's `<>` placeholders into real drop-in fields, add the
block vocabulary needed to author the target script, and convert block trees
into the existing `T2G_Cookie.Shared.Ast` records.

Target script this must be able to express:

```
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
```

Out of scope: bytecode emission, VM execution, map/world rendering. The
Backend pipeline already exists; this feature stops at producing `Body[]`.

## Approved decisions

- Architecture path: **architectural** (new subsystem + Gui→Backend boundary).
- Scope: **editor + AST only** (no execution). A Start button converts and
  reports; it does not run.
- Slot architecture: **A — explicit `Slot` objects** owned by blocks.
- Variables: **Scratch-like** — "Make a variable…" prompt + clickable
  dropdowns on name fields.
- Defaults: read-only built-ins **`empty=0`, `wall=1`, `gold=2`**.
- Start block: green-flag-style hat, **exactly one allowed** in the
  workspace; it marks what a Start button would run.
- AST output: **live validation badges** on offending blocks, plus a
  Start/AST dump for inspection; verified by tests.

## 1. Slot model & block shape

```csharp
enum SlotKind { Name, Value }   // Name = dropdown only, Value = accepts reporters

sealed class Slot
{
    public Block Host { get; }           // owning block
    public string TextBefore { get; }    // e.g. "set "
    public string TextAfter { get; }     // e.g. " to"
    public SlotKind Kind { get; }
    public Block? Content { get; set; }  // parked reporter, or null
    public Rectangle Bounds { get; set; } // set during Layout, workspace-local
}
```

- **Labels become segment lists.** Blocks are built from segments
  (`text | slot | text`). `Label` remains the concatenated string for
  measurement/fallback, but rendering and measuring iterate segments.
  Literal `<>` text disappears from rendering — the slot is the hole.
- **Geometry:** width = Σ segment widths + slot minimums
  (`Theme.MinSlotWidth` when empty). Height unchanged (`HeaderHeight`), so
  chain layout is untouched. `LayoutChildren()` additionally positions
  `slot.Content` reporters, which recursively lay out their own slots.
- **Graph safety (mandatory):** `CollectBlocks()`, `Flatten()`,
  `UnregisterTree()` and `FindAt()` all traverse slot contents. Without
  this, dropping a stack that contains a reporter into a slot of that same
  stack's host bypasses `CanJoin` and re-creates the cycle bug. `Detach()`
  on a reporter clears its `Slot.Content`; `CanJoin` therefore sees slot
  contents as part of the structure.
- **Drawing:** slots render as an inset rounded hole (lighter fill + inner
  line); filled slots draw the reporter on top; empty slots show a faint
  placeholder. Existing `SnapTarget` preview gains `IntoSlot`.
- **Hit-testing** inside a block: slot contents first (topmost slot last,
  matching draw order), then host body. Pressing a parked reporter drags
  that reporter; pressing elsewhere drags the stack. Pressing an empty
  `Value` slot starts no drag. Pressing a `Name` slot opens its dropdown
  (a click, never a drag).
- **Click vs drag:** a press that travels less than `Theme.DragThreshold`
  never moves the block — it re-homes exactly where it was on release and
  runs the block's click action (caret / toggle / dropdown). Only past the
  threshold does the block start following the pointer, so ordinary hand
  jitter can no longer turn a click into a tiny drag.
- **Literal category:** `number`, `true` and `false` share a `Value`
  category (the grammar's `NUMBER` / `TRUE` / `FALSE` atoms); `is there`
  stays in `Sensing` because it is a function call, not a value.

## 2. Interaction & block vocabulary

### IntoSlot snapping

- `FindSnap` gains `SnapKind.IntoSlot`: a **reporter** (no prev/next
  connections) near an empty `Value` slot gets a hole-shaped preview.
  `Drop` calls `slot.Host.AttachToSlot(slot, drag)`, guarded by `CanJoin`
  (slot contents are in `CollectBlocks`, so self-snap is impossible) and
  returning `bool`.
- **Occupied slots do not replace content** on drop — pull the old
  reporter out first. No preview is offered over a filled slot.
- `Name` slots accept no blocks (dropdown only); no preview.
- A reporter dragged over the toolbox deletes it (existing
  `UnregisterTree` path).

### New / changed blocks

| Block | Category | Behavior |
|---|---|---|
| `Start clicked` (hat) | Control | Singleton: toolbox item greyed while one exists; factory refuses a second. |
| `<number>` | Value | Inset input box inside the pill: click = caret, digits only, Enter/click-away commits. The block is the gate for keyboard content: `Text` keeps `[0-9]+` only, drops leading zeros, caps at 9 digits and never becomes empty. |
| `true` / `false` | Value | Click toggles the value. |
| `<variable>` | Variables | Reporter showing chosen name; click opens the variable dropdown. |
| `set <name> to <value>` | Variables | Slot 1 = `Name` dropdown, slot 2 = `Value`. |

Existing blocks gain slots: `print <>`, `step forward <>`, `turn by <>`,
`if <> then`, `if <> then <> else`, `while <> do`, all operator reporters
(`<> + <>`, …, `not <>`), `is there <>`.

**Scope decision — no free-text block.** MineCode's atoms are only
`NUMBER | TRUE | FALSE | IDENTIFIER` (`lang/MineCodeParser.g4`), so the single
keyboard input block accepts numbers and nothing else; `NumberBlock.Text`
enforces that rather than the keyboard path. Typing text would first require a
`STRING` token in the grammar (plus `./gen_lang` regeneration, the visitor, the
AST and the VM), which is out of scope until that lands.

### Dropdowns & variable creation

One shared raylib-drawn popup (closes on outside click), used by:

- `set`'s Name slot and the variable reporter: list of registry names,
  read-only built-ins shown greyed with a lock glyph.
- "Make a variable…": one-line inline prompt, `Enter` confirms, validates
  identifier rules (`[a-zA-Z][a-zA-Z0-9]*`), joins the registry.

### Start button

Green-flag button in the workspace toolbar. Clicking it runs
`AstConverter.Convert`; on success prints the AST (stand-in for future
execution), on failure flashes validation badges.

## 3. Validation badges & AST conversion

### VariableRegistry

Workspace-level, seeded with read-only `empty=0`, `wall=1`, `gold=2`.
API: `Names`, `IsBuiltin(name)`, `TryCreate(name)`, `GetValue(name)`.
Blocks never own variables; dropdowns and converter read this one source.

### AstConverter

Pure static: `AstResult Convert(Workspace)` → `Body[]` (one per script) +
`IReadOnlyList<(Block, string)> Errors`.

Walk rules:

- Root stacks: the stack containing the hat is *the* script; any other
  root stack errors on its first block ("script has no Start block").
  A second hat anywhere errors on that hat ("only one Start block").
- Statements map 1:1 to existing AST records: `step forward` →
  `StepInstruction`, `turn by` → `TurnInstruction`, `mine here` →
  `MineInstruction`, `print` → `PrintInstruction`, `set` →
  `SetInstruction(name, value)`, `if` → `IfInstruction(cond, body,
  elseBody?)`, `while` → `WhileInstruction`.
- Slots → `Value`: number → `Literal`, boolean → `LiteralBool`, variable
  reporter → `Variable`, built-in cell kind → `Literal(0|1|2)` (so
  `isThere wall` is exactly `isThere 1`), `is there <>` →
  `IsThere(SimpleValue)`, `not <>` → `Not(SimpleValue)`, operators →
  `BinOp` with the matching `BinOp.Operation`.
- Error cases (each = badge): empty required slot, malformed number
  field, unknown variable, missing hat, second hat, `set` targeting a
  read-only built-in, empty `if`/`while`/`else` body (the grammar's `body`
  needs at least one instruction).

### Badges

Validation re-runs whenever the forest changes (same walk cost class as
`Layout`; no debounce). Bad blocks draw a warning dot at the top-right
corner; hover shows the message. `Workspace.Draw` renders badges after
blocks. Start button and AST dump consume the same `AstResult`.

## Testing (extend `Gui/SelfTest`)

1. **Graph invariants** — fuzz harness gains slot operations (insert into
   slot, pull out, drop a stack whose forest includes slot contents) and
   keeps validating full forest invariants after every op. Regression
   guard for the cycle-via-slot hazard.
2. **AST golden test** — build the target script block-by-block through
   real APIs, `Convert`, deep-equal against a hand-written expected AST.
3. **Badge tests** — missing hat, second hat, empty slot, unknown
   variable, `set empty to …` each produce the expected error on the
   expected block.

## Files touched (expected)

- `Gui/Blocks.cs` — `Slot`, segment labels, slot layout/draw/hit-test,
  `AttachToSlot`, traversal updates.
- `Gui/Workspace.cs` — `IntoSlot` snap kind, badge rendering, registry
  ownership, hat singleton bookkeeping.
- `Gui/BlockCategory.cs` — new/changed block factories, greyed hat item.
- `Gui/Toolbox.cs` — disabled palette item support.
- `Gui/Theme.cs` — slot metrics.
- New `Gui/Variables.cs` — `VariableRegistry`, dropdown/prompt widget.
- New `Gui/Ast.cs` — `AstConverter`, `AstResult`, badge model.
- `Main/Program.cs` — Start button, AST dump entry.
- `Gui/SelfTest/SelfTest.cs` — new test groups.

## Risks

- Slot traversal missing from one of the four graph walks would silently
  reintroduce the cycle bug — mitigated by fuzz coverage and by making
  `Slot` the only way a reporter attaches (no other path exists).
- Inline text editing in raylib (caret, commit on click-away) is new
  territory for this codebase; keep it minimal (digits only, no selection).
