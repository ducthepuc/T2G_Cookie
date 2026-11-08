# Fix: `not` and `isThere` in `if` and `while`, and `isThere` in `not`

## The problem

These block drops were refused in the GUI:

1. `not` and `isThere` could not go into an `if` or `while` field.
2. `isThere` could not go into a `not` field.

The cause was one rule in `Block.AcceptsValue`: a field only took blocks with no fields of
their own, unless the host was `set`. `not` and `isThere` both have a field, so they were
refused everywhere else.

That rule followed the competition spec on purpose, so this change is a deliberate
extension of the language (see [Spec warning](#spec-warning)).

## What works now

```
if isThere 1
if not x
if not isThere 1
while isThere 0
while not x
while not isThere 1
set a to not isThere 1
```

These work both as blocks and as text, and they survive text -> blocks -> text.

## What is still refused

| Form | Why |
|---|---|
| `not not x` | Not requested, and rejected by the parser |
| `not a + b`, `if a + 1`, `while a + 1` | Not requested |
| `print isThere 1`, `step forward not x`, `turn by not x` | Not requested |

`not not x` and `not a + b` are refused everywhere. The others are refused only as blocks:
as text they already parsed before this fix, because `if`, `while`, `print` and `step` all
take an `expression` in the grammar.

## Changes

| File | Change |
|---|---|
| `Gui/Blocks.cs` | `AcceptsValue` also allows `not` and `isThere` in `if` and `while`, and `isThere` in `not`. |
| `Gui/Ast.cs` | The block converter accepts `isThere` as the operand of `not`. Other nested expressions still get the error badge. |
| `lang/MineCodeParser.g4` | `unaryExpression` is now `NOT (atom \| functionExpression)`. |
| `Backend/Parsing/lang/MineCodeParser.cs`, `.interp` | Regenerated from the grammar. |
| `Backend/Parsing/MineCodeVisitor.cs` | Builds `Not` from either an atom or an `isThere` call. |
| `Shared/Ast/Values.cs` | `Not` now holds a `Value` instead of a `SimpleValue`. |
| `Gui/SelfTest/SelfTest.cs` | New checks for the allowed forms, their limits, and the round trip. |

The bytecode emitter and the VM needed no change. The emitter already handled `Not`
recursively, and the VM already pops the stacked result.

To regenerate the parser, run this from `lang/` (ANTLR 4.13.2):

```
antlr4 -Dlanguage=CSharp -visitor -lib ../Backend/Parsing/lang -o ../Backend/Parsing/lang MineCodeParser.g4
```

Before the grammar edit, this command reproduced the committed parser files exactly.

## Verification

| Check | Result |
|---|---|
| GUI self-tests (`Gui/SelfTest`) | All pass: 691 checks, up from 678 before the change |
| `error_tests` (run on a copy) | 41 of 41 pass |
| `regression_tests` (run on a copy) | 13 of 13 pass |
| Full solution build | 0 warnings, 0 errors |

New programs run through the parser, bytecode and VM:

| Program | Situation | Result |
|---|---|---|
| `if not isThere 1` with `else` | Wall ahead | Takes the `else` branch |
| `if not isThere 1` with `else` | Open cell ahead | Takes the `if` branch |
| `set x to not isThere 1`, `print x` | Wall ahead / open cell ahead | Prints `false` / `true` |
| `if not b` | `b` is `false` | Runs the body |
| `if not isThere 3` | Bad argument | `E_BAD_ARG` |
| `if not y` | `y` is a number | `E_BAD_OPERAND` |
| `if not nope` | Variable never set | `E_UNINITIALIZED` |
| `if isThere 1` with `else` | Wall ahead / open cell ahead | Takes the `if` / `else` branch |
| `while not isThere 1`, `step forward 1` | Two open cells, then a wall | Steps twice, then leaves the loop |
| `while isThere 0`, `step forward 1` | Two open cells, then a wall | Steps twice, then leaves the loop |
| `while not a` | `a` is `true` | Skips the body |
| `while isThere 3` | Bad argument | `E_BAD_ARG` |

Not tested: dragging the blocks by hand in the running app. The self-tests go through
`AttachToSlot`, which uses the same `AcceptsValue` check as drag and drop.

## Spec warning

`Robotszimulátor.pdf` does not allow these forms:

- Page 3: `if = "if" simpleval block`, `while = "while" simpleval block` and
  `unop = unoperator simpleval`.
- Page 4: no nested expressions.
- Page 9: a value block is always part of a `set` block and cannot be nested in another
  value block's field.

Valid contest programs behave the same as before. The risk is a judge or a test that expects
these forms to be rejected, for example in the block editor. To go back to the spec, undo
the changes listed above.
