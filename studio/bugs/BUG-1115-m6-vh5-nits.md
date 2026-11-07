# BUG-1115: M6-VH5 nits: one new 137-character docs/03 line; TooltipWrap's px estimate differs from docs/03; a space at a break leaves an empty tooltip line; the tooltip panel is see-through over the setup's labels

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-06-0926, task M6-VH5 (QA, standard) |
| System | docs/03; view: `game/scripts/TooltipWrap.cs`; Godot default tooltip style |
| Fixed by | |

## Repro
1. `awk 'NR==6820 {print length}' docs/03-technical-design.md` → 137. The task wrapped the `--map <id>` lines below it
   but the edited sentence above ("own cap (four slots, Raraku: two, Vathar Crossing: four again). `MatchArgs` adds ...")
   was added at 137 characters; every other line the task added is at most 127.
2. `TooltipWrap.Width`'s summary says "roughly 450 px at the default 16 px font"; docs/03 (M6-VH5) says "about 480 px".
   Measured on the windowed shot: Raraku's widest wrapped line is about 470 px.
3. `TooltipWrap.Wrap(new string('e', 60) + " ")` returns the 60 e's plus `"\n"`: a tooltip with an empty last line. A
   leading space before a word that passes the width gives an empty first line. No shipped description has a leading or
   trailing space (and the data loader doesn't reject one), so nothing shows today.
4. Windowed `TooltipWrapTest -- --shots <dir>`: the wrapped Raraku tooltip is drawn over the Colour / Difficulty headers
   and the Gold mines row, and Godot's default tooltip panel is translucent enough that "sparse palms" sits on top of
   "Colour" and "mesas flank" on top of "Difficulty", both hard to read. Pre-existing (the unwrapped tooltip overlapped too), now just
   easier to see.

## Expected
1. Brief item 6: doc lines wrapped. 2. One figure. 3. Text unchanged and no empty lines. 4. A readable tooltip.

## Actual
As above.

## Notes
- 3: trimming a trailing break, or the data checker rejecting edge spaces in `description`, would close it.
- 4: a `TooltipPanel` style with an opaque background in the shell theme would fix every tooltip at once (view, art pass).
- The dev also flagged: other tests' `Shot` helpers still wait for `FramePostDraw` without a bound (windowed `--shots` runs
  only; the headless loop never takes a shot).
