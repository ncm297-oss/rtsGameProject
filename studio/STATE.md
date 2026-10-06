# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" (further down) is non-blocking: what the studio built or decided
on its own, explained in terms of what you'd see in the game.

_Last updated: 2026-10-06 (session 2026-10-06-1255, ACCEPT both tracks)._

## Waiting on you

- Nothing blocking. The studio runs on autopilot (chain sessions, self sign-off) until the end
  of the roadmap or a cap/incident stops it.

## Now

| Field | Value |
| --- | --- |
| Sim: milestone | M3 — Economy & buildings (started 2026-10-06-1255); 1 / 8 criteria |
| Sim: next task | **M3-2 worker gather / return loop** (Gather command, worker state machine, automatic drop-off; a minimal drop-off building store; settles BUG-0073 / BUG-0075 in the brief) · feature · QA full |
| Sim: gate | **GO** |
| View: milestone | M2 — Presentation (started 2026-10-05); 7 / 10 criteria |
| View: next task | **Trees and gold mines as MultiMesh** from `World.Resources` (M2 criterion 3, unblocked by M3-1; `Match` asks for forests / mines) · feature · QA standard; then M2-6 audio, M2-7 60 FPS check |
| View: gate | **GO** |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | sim branch 0 warnings; view branch 1 warning (CS8602 in a test scene, BUG-0084); conductor re-checks `main` after the two merges |
| Tests | sim branch 2088 / 13 skipped / 0 failed non-Perf, Perf rows alone green (500 moving with forests 0.82 ms; tight blob 4.33 ms = base); view branch 1929 / 10 / 0 (QA full incl. Perf 1998 / 13 / 0); smoke PASS on both; `DebugOverlayTest` + `QaM25Test` PASS; CLI twin with forests identical; Producer re-ran all |
| Open bugs | 22 (S1: 0, S2: 0, S3: 14, S4: 8) — none block; 6 new (sim 0073 / 0074 / 0075 S3, 0076 S4; view 0083 S3, 0084 S4) |
| Sessions today | 3 / 8 on 2026-10-06; feature sessions since last hardening: sim 1 / 4, view 1 / 4 |
| Last session | 2026-10-06-1255 · sim M3-1 resource entities (0 fix rounds) · view M2-5 debug overlay (0 fix rounds) · both ACCEPT |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 (sim track) | 8 / 8 | **Done** 2026-10-06 (Producer sign-off after the M1-9 hardening; retro in docs/05) |
| M2 (view track) | 7 / 10 (SimRunner interpolation, camera, placeholder unit views, selection complete, orders complete, minimap, debug overlay); left: terrain mesh trees/rocks (unblocked now), audio (M2-6), 60 FPS playable check (M2-7) | In progress; trees / mines next, then M2-6, M2-7, M2 hardening + sign-off |
| M3 (sim track) | 1 / 8 (resource entities + depletion updates the nav grid) | In progress; M3-2 gather loop next |
| M4-M9 | — | Planned |

## For your review

Non-blocking. Each entry says what was built or decided, what you'd notice in the game, and how
to change it. To change anything, write it in `studio/inbox.md`, for example "use formations
instead of clusters" or "make giving up take 2 seconds".

### Gold mines and trees exist in the rules: the first piece of the economy (sim track, M3-1, 2026-10-06)

- **What was built:** the game now knows about resource nodes. A **tree** holds 100 wood and
  covers one 2 m square; a **gold mine** holds 2,500 gold and covers a 2 x 2 block. Both are solid:
  units walk round them. When a tree is cut down to nothing (nothing cuts yet; that's the next sim
  task, workers), its square opens up and every pathfinding arrow map is recomputed, so armies walk
  straight through the gap. A map can be generated with forests (connected clumps of 12-40 trees)
  and mines (at least 24 m apart), and the generator makes sure no patch of ground is ever walled
  in by them. All of this is part of the replay fingerprint, so a replay where a tree falls one tick
  later is a different replay. The replay file format grew (version 3; old files are refused with a
  clear code), and the checked-in golden replay was regenerated once with the proof that every
  unit's path stayed byte-identical.
- **What you'll see:** nothing in the window yet: the game window still builds its map without
  resources, and no tree or mine is drawn. The next view task draws them (placeholder cone trees
  and dark blocks) and turns forests on in the game's map, so in one or two sessions you'll see
  forests and mines on the map and the army routing round them.
- **Try it now (command line):** `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200 --ticks 600 --forests 12 --mines 8`
  twice: the header ends with `forests 12 trees N mines 8` and the fingerprint lines are identical
  both times. Press F12 in the game window later (see the overlay entry) to see resource cells red
  once the view turns forests on.
- **Producer decisions, revisit any time:**
  - *A mine is 2 x 2 cells (4 x 4 m), a tree 1 x 1.* The docs didn't say; 2 x 2 gives eight sides for
    workers to stand on. Alternative: 3 x 3 for a grander mine.
  - *A worked-out mine leaves open ground, like a felled tree* (no rubble blocking the spot).
  - *Maps have no forests or mines unless asked* (the generator's defaults are 0). This kept every
    M1 test and the golden replay's trajectories identical. The game window will ask for them in
    the next view task; the numbers (how many forests, how many mines) are a design call I'll make
    then (something like 12 forests and 8 mines on the 128 x 128 map). Tell me if you have a
    preference, or a preference for where mines go (near start positions comes with M3-3 / M6).
  - *Every mine holds the "start mine" amount (2,500)* until start locations exist and expansion
    mines (2,000) can be told apart.
  - *Room for 4,096 nodes per map* (a setting, not a limit of the design).
  - *Size: about 2,200 lines against a 1,000-1,300 budget*, mostly tests. Accepted: the new part is
    how resources interact with pathfinding and the fingerprint, and the tests are what prove it.
- **Rough edges (S3, none visible before workers exist):**
  - BUG-0073: when trees fall *continuously* (one per tick, QA's stress), every arrow map is
    invalidated every tick and most groups stand still until the felling stops. In play, with workers
    chopping, a tree falls every few seconds, which would mean short stalls. I've made this a design
    question for the next sim task (felled trees only *open* ground, so an old arrow map is still safe
    to follow; the fix is to keep using it and refresh it in the background).
  - BUG-0075: cutting a tree in the *middle* of a forest first leaves an open square nobody can
    reach, and a click on it makes the unit give up. Workers will only ever cut trees they can stand
    next to, so this can't happen in play once that rule is written down; the next sim task does that.
  - BUG-0074: the forest generator assumes trees are 1 x 1; if someone edits the data to make trees
    2 x 2, forests can wall ground in. Next sim clean-up session.
  - BUG-0076 (S4): small notes (a setup-time number in the docs, fixed; a redundant check that costs
    2 s on a huge 1024 x 1024 map; an empty resources file loads without complaint).
- **To change it:** `game/data/common/resources.json` (node types, footprints, names);
  `game/data/common/rules.json` (`treeWood`, `startMines.gold`); forest sizes and mine spacing are
  generator settings (`MapGenParams`), by inbox note.

### Press F12: the developer overlay shows the pathfinding under the hood (view track, M2-5, 2026-10-06)

- **What was built:** a toggle (F12) that draws the game's inner workings on the map: every ground
  cell as a faint square (red where units can't go, dark red on cliff edges, orange on ramps); yellow
  arrows on the ground showing which way the selected units' pathfinding map points, in a 40 x 40-cell
  window round the camera, with a cyan disc on the destination; a bar graph of the last 120 ticks'
  cost with the 4 ms budget line (bars go red over budget); and a second line in the top-left label
  with the live unit count, how many are moving, how many arrow maps are cached, and the average and
  worst tick time. Off by default; `-- --debug-overlay` starts with it on (so `--screenshot` can capture
  it). While off it costs nothing and nothing is built.
- **Try it:** `& $env:GODOT --path game`. Box-select the blue army, right-click a spot on the far
  plateau, press **F12**: the arrows converge on the ramp (orange) and then run to the cyan disc. Pan the
  camera: the arrow window follows. Press F12 again to hide it. `& $env:GODOT --path game -- --units 1000 --zoom 60 --debug-overlay`
  shows the graph with 2,000 units ticking (bars well under the line).
- **Producer decisions, revisit any time:**
  - *F12*, rebindable (`debug_overlay` in `game/project.godot`); the docs' key list left it free.
  - *Arrows for one destination only*: the lowest-numbered selected unit's. Arrows for every selected
    group would clutter; the window and a Tab subgroup pick the one you mean.
  - *A fixed 40 x 40-cell window round the camera*, not the exact visible area: at the farthest zoom you
    see more ground than the window covers. The visible-trapezoid version is deferred.
  - *The numbers (average, worst) are on the label, not drawn inside the graph.* The dev's call;
    accepted.
  - *Per-unit state labels and the dev console are deferred* (they are in the docs' wish list, not in
    the M2 criterion); labels may come with the M2-7 polish if cheap.
  - *Size: about 2,100 lines against an 800-1,100 budget*, mostly tests (an independent checker that
    recomputes the arrows after every refresh and compares them to what's drawn).
- **Rough edges (view hardening session, after M2-6 / M2-7):** BUG-0083 (S3): with the overlay on,
  the label's text line allocates a little memory every frame while the docs claim zero (the overlay's
  own drawing is zero; the fix is a line in the docs or a cached string). BUG-0084 (S4): cliff edges
  read olive, not dark red, on the green plateau; arrow tips dip a few cm into steep ramps; one compiler
  warning in a test scene; one dev test measures the wrong case. Also seen: with more than 128
  destinations active at once under the 2-maps-per-tick build limit, a new order's arrows can take a
  while to appear (BUG-0025, sim, known).
- **To change it:** key binding in `game/project.godot`; colours are constants in
  `sim/Rts.Sim/ViewApi/NavOverlayBuilder.cs` and `game/scripts/FlowArrowsView.cs` (dev-only, not
  player-facing); window size `FlowArrowLayout.DefaultWindow`; the rest by inbox note.

### M1 is done: the whole game simulation works without graphics, and I signed it off (sim track, M1-9 + sign-off, 2026-10-06)

- **What M1 is:** everything that makes the game *run*, with no picture yet: generated terraced maps
  with cliffs and ramps; armies of hundreds of units that walk, funnel through chokes, pack up at the
  click point, give up when truly stuck, and make way for each other; Stop / Hold / Attack-move and
  Shift-queues; replays that prove the game plays out identically every time; the 500-unit speed
  target (0.62 ms per tick against a 4 ms budget); and a command-line tool that runs a match with no
  window. Fourteen sessions over four days, three of them clean-up sessions.
- **This session's clean-up (nine items), in plain words:**
  1. A unit told to **hold position now blocks your own soldiers too** (nobody squeezes past a holder
     in a one-cell gap; before, 10 of 30 did). It already blocked the enemy.
  2. **Bad orders are refused at the door** instead of being accepted and corrupting the replay file
     later (only hand-built orders could do this; the game's own keys never produced one).
  3. **Enemy walls of 5+ units now hold** (a line of five foot soldiers across a 4 m choke used to be
     leaked through by your own units shoving each other). Walls of up to 32 units count.
  4. The two-army speed regression is paid back: 2,500 units of two armies fighting over one spot
     cost 10.6 ms per tick, now 6.8; one tight 2,500 blob 4.5 → 4.3 ms, and that number is now a
     test that fails if it regresses.
  5. Found while measuring: a unit that gave up while being pushed off a crowded goal could stop a
     metre away and still "own" the spot, so later arrivals packed around a stray. Fixed (BUG-0058).
  6. Command-line tool: a bad `--record` path is reported before the run, not after a long run; a
     replay too short to check says so; two cosmetic messages fixed.
  7. The docs now list every known limit of the movement code in one place (docs/03 "Known limits
     (M1)").
- **Try it (two minutes):** `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 500 --ticks 1500`
  twice: the 15 fingerprint lines are identical and the timing line is under 1 ms per tick. Then
  `& $env:GODOT --path game`: box-select part of the blue army and press **H**; select the rest and
  right-click through the holders: nobody gets past them. Send the whole army across the map and
  watch it stream through a ramp.
- **Producer decisions, revisit any time:**
  - *Signed M1 off myself* (your autopilot setting `stop_at_milestone_end: no`). Conditions held: all
    eight criteria verified, QA coverage complete for every M1 system, no serious open bugs, clean-up
    session done. If you'd rather playtest before a milestone closes, flip that setting to `yes`.
  - *Two limits accepted instead of fixed:* (a) a unit's exact path can depend on the order its
    neighbours were created (BUG-0046); replays are exact anyway (same game, same result), it only
    matters for a hypothetical "re-create the army in a different order" case. The fix was built and
    measured but it reshuffles every crowd's outcome and broke three test bounds; not worth it now.
    (b) 4.7% of units give up when 500 units go to 500 random spots (my target was 3%); every rule
    that got lower also made jams never end. Both revisit with the "crowd cost" pathfinding work after
    M4. Alternative: spend a session re-fitting the test bounds and landing (a) now.
  - *The 64-goal stress test allows one bad map to give up 76 of 128* (it's the map that once
    livelocked; typical maps give up 28-30, and that typical number is now guarded too).
- **Rough edges (all small, next sim clean-up session in four sessions):** BUG-0071 (S3): in one
  constructed geometry the "is this a wall?" shortcut can answer differently depending on which unit
  asks first; deterministic, never seen in random play, no replay risk. BUG-0072 (S4): a `--record`
  file name with illegal characters is still caught only after the run.
- **For you, one line in your file:** `CLAUDE.md` says "Current milestone: M1". Suggested:
  "Current milestones: M3 (sim track), M2 (view track)", plus the CLI line
  `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200   # headless hashes + timings`.
- **What's next:** M3, the economy: gold mines and trees, workers gathering, building placement and
  construction, production queues, Age II, and the full Malazan and Whirlwind rosters in data.
  **Update 1255: gold mines and trees landed (see the M3-1 entry at the top).**

### View clean-up: the minimap right-click cancels A, lone dots are visible, five bugs closed (view track, M2-H1, 2026-10-06)

- **What was built (clean-up, no new features):**
  1. Right-clicking the **minimap** while A is armed now cancels A and orders nothing, the same as a
     right-click on the 3D map (BUG-0068 fixed).
  2. A double-tap on a group digit at exactly the 300 ms limit always counts the same way; if every
     selected unit dies while A is armed, the next click selects normally instead of being swallowed
     (BUG-0067 fixed).
  3. A height lookup that could crash for a unit billions of metres off the map can't any more
     (BUG-0052 fixed; unreachable in play, but gone).
  4. **Minimap dots have an outline**: each unit's dot is its cell in the player colour inside a
     one-cell dark rim (or a light rim around the dark Malazan colour), so a lone scout no longer
     hides against a ramp tick or a cliff lip (BUG-0064 fixed). Costs 0.13 ms per refresh at 2,000
     units (was 0.06; budget 0.25).
  5. Test gaps closed: the facing test now catches a mirrored turn; the mesh test now catches
     missing cliff walls (BUG-0053 fixed).
- **Try it:** `& $env:GODOT --path game`. Select a few units, press A, right-click the minimap: the
  `A` disappears from the top-left label and nobody moves. Zoom out (`-- --zoom 60`) and look at the
  minimap: every unit is a small outlined square.
- **Producer decisions, revisit any time:**
  - *The rim colour switches by brightness:* dark rim around light colours (orange Whirlwind), light
    rim around dark ones (slate-blue Malazan). The dev's call; I accepted it, but see the rough edge.
  - *The refresh-cost target is the 0.25 ms limit, not the old 0.06 ms.* Doubling a tenth of a
    millisecond five times a second is nothing.
  - *No fix for maps wider than 220 cells* (none exist): a note in the docs says what to do when they do.
- **Rough edge, a taste call for you (BUG-0069, S3):** at the minimap's size a lone dot is a 5 x 5
  pixel square with only 1-4 pixels of player colour in the middle, so **a lone Whirlwind unit reads
  as a black dot and a lone Malazan unit as a near-white one**, close to the white camera outline.
  Two light-coloured factions' scouts would look alike, and gaps inside an army show as thin rim
  lines through it. QA's screenshots are in the conductor's scratch folder for this session
  (`qa100-minimap.png`, `qa990-minimap.png`, 4x crops; not in the repo). Default plan: a 2 x 2-cell
  coloured centre inside the rim (more colour, same visibility) in the next view clean-up session
  (after M2-5, M2-6, M2-7). Alternatives: draw dots in screen pixels (a fixed 3-4 px colour square
  with a 1 px outline, also future-proof for big maps), or rim only around the outside of a crowd.
  Say which in the inbox if you have a preference. BUG-0070 (S4): four doc/test nits, same session.
- **To change it:** rim colours are `MinimapRaster.DarkRim` / `LightRim` (engine constants); the
  double-tap window is `ControlGroups.DoubleTapSeconds`; the rest by inbox note.

### The order keys are in: A, S, H, Shift-queue, double-click, control groups and Tab (view track, M2-3, 2026-10-06)

- **What was built:** the keyboard half of giving orders. **A** then click: attack-move (for now
  it just walks there; fighting on the way comes in M4). **S**: stop. **H**: hold position (your
  own walkers route round holders instead of nudging them). **Shift + right-click** (or Shift + S /
  H): queue the order behind the current ones, up to 8 per unit. **Double-click** a unit, or **Ctrl +
  click** it: select every unit of that type on screen (Shift adds them). **Ctrl + 1-9** saves the
  selection as a group, **Shift + 1-9** adds to it, **1-9** recalls it, and tapping the digit twice
  quickly jumps the camera to the group. **Tab** steps through the unit types in a mixed selection
  (shown in the top-left label as `sub <type> 1/3`; the command card will use it in M3). Esc
  cancels an armed A. Every key is its own rebindable action in `game/project.godot`.
- **Try it:** `& $env:GODOT --path game`. Box-select the blue army, press H, then select a few
  others and right-click through them: the holders don't budge. Select a group, press A, click far
  away: they walk (the label shows an `A` while you're aiming). Shift + right-click four spots:
  they visit them in order. Ctrl + 1, click empty ground, press 1: they're back; press 1 again at
  once: the camera jumps to them. Double-click one cavalry unit: all cavalry on screen are selected.
- **Producer decisions, revisit any time:** A with nothing selected does nothing; while A is armed,
  a click off the map does nothing and keeps aiming; Ctrl + digit with nothing selected keeps the
  old group (no accidental wipes); recalling an empty group changes nothing; Ctrl + Shift + digit
  counts as assign; the double-tap window is 0.3 s; Tab order is by unit type id; a unit told to
  hold shows no "holding" marker because any queued order ends the hold (so a marker would lie).
- **Rough edges (view hardening session, next view session):** BUG-0068 (S3): right-clicking the
  *minimap* while A is armed sends a plain move and leaves A armed, where a right-click on the
  3D map cancels A as intended. BUG-0067 (S4): a double-tap at exactly 300 ms is a coin flip; if
  every selected unit dies while A is armed, the next click is swallowed (can't happen before M4).
  Also visible now: send a tight group through Shift-queued points and some units give up a leg
  when they bump into their own packed comrades (BUG-0028, the crowd-cost follow-up after M4).
  **Update 0905: BUG-0068 and BUG-0067 fixed (see the view clean-up entry above).**
- **To change it:** key bindings are in `game/project.godot` (one action per key, e.g.
  `order_hold`); the double-tap window is `ControlGroups.DoubleTapSeconds`; the rest by inbox note.

### The game can be run from the command line with no window, and the last M1 criterion is met (sim track, M1-8, 2026-10-06)

- **What was built:** a small tool, `tools/Rts.Cli`, that runs a match headless: it spawns an army
  (every unit type, `--units` of them), marches it across the map from one edge to the far side,
  and prints a fingerprint of the game state every 5 seconds of game time plus how long each tick
  took (average, 99th percentile, worst). It can record the run as a replay file and play a replay
  back, checking every fingerprint; the exit code says what happened (0 fine, 1 bad arguments or
  files, 2 a replay that doesn't match). This is the "runs a scenario headless and prints hashes
  and timings" criterion, the last of M1's eight. The same task gave the view a read-only window
  into the pathfinding arrow maps, so the M2-5 debug overlay can draw them.
- **Try it:** `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200 --ticks 1500`. Run
  it twice: the 15 fingerprint lines are identical (that's determinism you can see). Add
  `--record x.replay` then `dotnet run --project tools/Rts.Cli -- play x.replay`: "ok: 15
  checkpoints matched". `--units 2500` shows the tick cost at scale (about 4.6 ms on this PC).
  Update 1255: `--forests 12 --mines 8` adds resources to the map (see the M3-1 entry).
- **Producer decisions, revisit any time:** the march goes to the walkable cell farthest (by
  walking distance) from the middle of the player's own map edge, so armies cross the whole map
  (with `--players 2` the two armies cross each other); `--units` is the total across players and
  is capped at 100,000 (the start blocks hold about 14,600 on the default map).
- **Rough edges (M1 hardening session):** BUG-0057 (S4): an unwritable `--record` path is only
  reported after the run finishes; a replay recorded with fewer ticks than the checkpoint interval
  "passes" with nothing compared; a cosmetic `: :` in one error message. (The docs mismatch about
  timing with `--record` is already fixed.) **Update 0905: BUG-0057 fixed; one leftover (illegal
  characters in the file name) is BUG-0072, S4.**
- **For you, one line in your file:** `CLAUDE.md`'s Commands block has no CLI line yet (agents
  don't edit it). Suggested: `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200   # headless hashes + timings`.
- **What's next for M1:** one clean-up session (nine small bugs, listed in the sim debt backlog),
  then I sign M1 off and write its retro; M3 (economy and buildings) starts after. **Update 0905: done,
  see the M1 entry at the top.**

### Stop, hold position, attack-move and Shift-queued orders exist in the rules; the M1 speed target is proven (sim track, M1-7, 2026-10-05)

- **What was built:** three new orders a unit understands besides "walk there": **Stop** (drop
  everything and stand), **Hold position** (stand and refuse to be pushed aside by your own
  walkers; it will fight from there in M4), and **Attack-move** (walk there; from M4 it will stop
  to fight anything met on the way). Plus **Shift-queuing**: up to 8 orders lined up per unit, run
  one after the other; a queued Stop or Hold ends the line; a unit that gives up on one leg still
  tries the next. Replays now record the queue flag (format 2; the old format is refused with a
  clear code; the checked-in golden replay was regenerated and every fingerprint stayed identical,
  which proves no movement rule changed). Also the M1 roadmap criterion "500 moving units under
  4 ms per tick" now has a named, enforced test.
- **What you'll see:** nothing yet; there are no keys for it. The next view task (M2-3) wires A /
  S / H / Shift. Then: select a group, press H, and walkers from your own army route round them
  instead of nudging them off their spot; Shift-right-click four points and the group walks them
  in order. Update M2-3: landed; see the entry above.
- **Numbers:** 500 units marching cost 0.67 ms per tick on this PC (budget 4 ms, so 6x headroom);
  2,500 units each with a full queue of 8: 3.0 ms.
- **Producer decisions, revisit any time:**
  - *A held unit still lets its own army squeeze past it in a one-cell gap* (BUG-0055, S3): 10 of 30
    friendly walkers got past a big unit holding a 2 m corridor; enemies never do. This is the
    "your own standing units are soft" rule from M1-5, applied to holders as the brief said. I
    think a unit told to hold a choke should block its own side too (that's what H means), and I've
    scheduled that change for the M1 clean-up session, as long as it doesn't hurt the crowd numbers.
    Alternative: keep holders soft and document it. **Update 0905: done, holders block everyone; the
    crowd numbers didn't move.**
  - *Hold, then Shift-queue a move = walk off at once.* A queued order given to a holding unit
    starts immediately (it's standing idle), and starting an order releases the hold. This is how
    most RTSs behave; the HUD must not show "holding" once an order is queued after it (noted for M2-3).
  - *Queue of 8 per unit; a 9th is dropped silently.* The view can warn if it wants to.
  - *One tick over budget on size* (about 1,510 changed lines vs 1,500). Accepted.
- **Rough edges (M1 clean-up session):** BUG-0055 above; BUG-0054 (S3): a hand-built order with a
  flag bit the rules don't know is accepted and then makes the replay file unwritable (no factory
  produces one, and the game doesn't save replays until M6); BUG-0056 (S4): three nits. **Update
  0905: all three fixed.**
- **To change it:** the queue length is `OrderConstants.QueueCapacity` (an engine constant, not
  data); the hold / stop behaviour is a rules decision: write it in the inbox.

### The minimap is in: see the whole map, jump the camera, send troops with a right-click (view track, M2-4, 2026-10-05)

- **What was built:** a 220-pixel map in the bottom-left corner: the three height levels in their
  tints, ramps as small orange ticks, cliff lips dark, the unreachable border darkened; every unit a
  dot in its side's colour (refreshed 5 times a second); and a white outline showing exactly what
  the camera sees (wider at the top, since the camera looks across the ground at an angle). It is
  the first piece of the HUD (`Hud` layer), so later panels have a home.
- **Try it:** `& $env:GODOT --path game`. Left-click anywhere on the minimap (or hold and drag) and
  the camera jumps there. Box-select your army, then right-click a spot on the minimap: they walk
  there, same as right-clicking the ground. Hovering the minimap never scrolls the camera. `-- --no-hud`
  starts without it (clean screenshots). `-- --units 1000 --zoom 60` still runs at 120 fps.
- **Producer decisions, revisit any time:**
  - *Bottom-left, 220 px, square; a non-square map gets letterboxed* (dark bars) instead of
    stretched, so distances read the same both ways. Alternative: stretch to fill.
  - *Same placeholder colours as the 3D terrain,* so what you see on the minimap matches the ground.
    Real biome colours come with the art pass (M6).
  - *Dots are one map cell (2 m) and show the current tick, not the smooth in-between position*;
    at 5 refreshes a second nobody can tell. **Update 0905: dots are now the cell plus a one-cell rim
    (see the view clean-up entry).**
  - *The mouse wheel does nothing over the minimap* (minimap zoom may come later).
- **Rough edges (view hardening session, after M2-3):** BUG-0064 (S4): a lone Whirlwind dot is
  nearly the same orange as a ramp tick, and a lone Malazan dot on a cliff lip is nearly the cliff
  colour, so single scouts can hide in plain sight; likely fix is an outline or a 2x2 dot. On maps
  wider than 220 cells (none exist in the game yet) about a quarter of cells would never get a
  pixel. No fog, resource markers, pings or Alt-click yet (M3/M4). Update M2-3: a Shift + right-click
  on the minimap now queues the move, like on the 3D map. **Update 0905: BUG-0064 fixed (outline);
  the follow-up on the outline's colour is BUG-0069.**
- **To change it:** size and corner are in `game/scenes/Match.tscn` (the `Minimap` node's
  offsets); dot colours follow each faction's `primaryColor` in `game/data/factions/<faction>/`.

### You can now select your soldiers and send them somewhere (view track, M2-2, 2026-10-05)

- **What was built:** when the game opens, 200 placeholder units stand on the map: your army in
  Malazan slate-blue to the west of the centre, the Whirlwind army in orange to the east. Each unit
  is a capsule sized by its body radius from the data (foot soldiers small, cavalry bigger, siege
  biggest). Left-click a unit, or drag a box over several; hold Shift to add or remove; click empty
  ground to clear. Selected units get a green ring. Right-click anywhere on the map and they walk
  there, smoothly at the frame rate (the game ticks 20 times a second; the picture blends between
  ticks). Enemy units can't be selected. The top-left label now ends with `sel N`.
- **Try it:** `& $env:GODOT --path game`. Drag a box over the blue army, then right-click the ramp
  or the plateau behind it: the crowd funnels through the ramp (the M1-5 behaviour, now visible).
  `-- --units 1000` starts 2,000 units (120 fps on this PC, vsync-capped); `-- --zoom 60` starts
  zoomed out.
- **Producer decisions, revisit any time:**
  - *Team colour = faction colour.* Player 0 plays Malazan, player 1 Whirlwind, until M6's match
    setup gives each player a colour of their own. Alternative: a per-player palette now; skipped
    because there is no setup screen to pick it in yet.
  - *Clicks have a 12-pixel minimum radius* so far-away units stay clickable; *Shift + click on
    empty ground keeps the selection* (as in most RTSs); *a click on a cliff face sends units to the
    plateau above it* (the wall belongs to the higher ground).
  - *Start positions are a debug layout*: two half-disc blocks either side of the map's centre, so
    both armies fit the opening camera. Real start locations come with the maps in M6.
  - *Over the size budget* (2,085 lines vs 1,500; about 900 of them game code, the rest tests).
    Accepted because the code is plain and the tests are what make the picking exact (the click
    point lands within 1 mm of the drawn ground).
- **Rough edges:** units turn to face where they walk but don't animate (capsules). With 2,000
  units selected, three right-clicks inside one tick overflow the order queue and the third is
  dropped with a warning (by design; the queue holds 4,096 orders). Two nits for the view hardening
  session: BUG-0052 (S3, a height lookup would crash for a unit billions of metres off the map,
  which can't happen) and BUG-0053 (S4, a test that can't catch a facing sign flip, a stale remark,
  one doc number). Update M2-4: the minimap's right-click uses the same order path. **Update 0905:
  BUG-0052 and BUG-0053 fixed.**
- **To change it:** unit sizes are the `radius` values in `game/data/factions/<faction>/units.json`;
  team colours are each faction's `primaryColor` in `game/data/factions/<faction>/`. Both are data
  edits (plus a golden-replay regen, which the studio does).

### Crowds route round each other, parked units make way, enemy walls hold better (sim track, M1-4d-3 hardening, 2026-10-05)

- **What was built** (the M1 hardening session: five debt items in one batch):
  1. Walkers steer round other groups' standing clusters and enemy clumps instead of pressing in
     and giving up (the shorter way round; ties go right).
  2. Waiting isn't being stuck: a unit behind traffic that is still moving (anyone's, within 2 m),
     or behind a unit waiting for its path, keeps its patience (behind a path-wait it loses patience
     four times slower rather than never, so a jam always ends).
  3. A unit pushed off its cluster walks back to its spot once the pushing stops, once per order,
     so a corridor can't bounce it back and forth forever.
  4. A pair parked in a one-cell corridor is pushed along by a walker stuck behind it (parked lines
     of up to 3 yield together; a cluster's spot still holds).
  5. Smaller fixes: an enemy standing on your click point is a wall, not a friend (BUG-0037); a
     walker beside an enemy no longer slides into its own standing comrade (BUG-0038); a short
     re-click inside the same 2 m square moves an arrived unit (BUG-0030); a hand-edited replay can't
     declare more than 24 hours (BUG-0040 part 1); the movement tests now run on many maps instead
     of one (BUG-0034/0039).
- **What you'll see:** send one group through another's parked cluster and most now get through:
  111 of 200 walkers cross a mixed cluster (was 2); two groups swapping rooms through a one-cell
  corridor all arrive; the parked pair steps along. Groups sent to four points 6 m apart: 51% arrive
  (was 36%; on the fair comparison 241 → 256 of 500).
- **Producer decisions, revisit any time:**
  - *Accepted below my targets again* (four points 51% / 34% vs 60% / 50%; 64 small groups 19% give
    up vs 10%; walkers crossing their own army's settled cluster 22 of 200 vs 150). Every row beats
    the old code on the same setup, and the cause is the same structural one: the shared path map
    doesn't know where units stand, so four clusters merge into one 12 m mass that walkers from
    the far side can't get round before their 1-second patience runs out. The real fix is a "crowd
    cost" in the path maps; I'm not scheduling it inside M1 and will propose it once the M4 combat
    sandbox shows whether it matters in play.
  - *Found and fixed in-session* (QA): one map in 160 where 94 units walked forever (S1); the
    walk-back pushing the walker it had just let through back off its goal (S2); a re-click
    mid-route making a walker give up (S2).
  - *BUG-0045 lowered from S2 to S3, my call, you may object.* QA found that a line of **five or
    more** standing enemies plugging a passage (for example 5 foot soldiers across a 4 m choke) can
    still be leaked through: your own units shove a given-up comrade into the enemies, and it later
    walks out the far side. This was already true before this session; the M1-5 entry below said
    "nobody gets through", which held for the 3-unit plugs tested then. Plugs of up to 4 units now
    hold, including diagonal ones and ones that form and dissolve mid-push. I didn't block the merge
    on it: nothing you can see depends on it before M4 (units pressed into enemies will be fighting,
    not standing), and it predates this work. It's scheduled for the M1 end-of-milestone hardening
    session. Alternative: reject the whole session over it, losing nine verified fixes. **Update
    0905: fixed; plugs of up to 32 hold.**
  - *Perf:* 500 moving units cost 0.17 ms per tick (the M1 budget is 4 ms). At 2,500 units, one tight
    cluster is at 4.5-4.6 ms vs my 4.5 ms slice target (3-5% over the old code the same day), and
    two-player clusters got 2x slower (BUG-0044, S3: the new plug check runs for every touching
    enemy). All beyond M1's supported scale; hardening session. **Update 0905: paid back (4.3 and
    6.8 ms).**
  - *The old crowd tests measured the wrong thing:* a slot-numbering slip sent both players to
    every point, so they measured enemies contesting a spot, not crowds. Re-set to one player per
    point, with the old code re-measured on the same setup for a fair comparison.
  - *Every map's trajectories changed once* (golden replay regenerated, reason in the commit).
- **Rough edges (all S3/S4, M1 end-of-milestone hardening):** BUG-0044 two-player perf at 2,500;
  BUG-0045 plugs of 5+; BUG-0046 a walker's exact path can depend on the order its neighbours were
  spawned in (replays are unaffected: same seed + same orders still match tick for tick); BUG-0049
  one 64-goal map gives up 75 of 128 (test bound 48); BUG-0050 random-goal give-ups 4.7% vs the 3%
  target (the price of the livelock fix); BUG-0047 test-guard nits. BUG-0028/0032 (crowd targets)
  stay open as the "crowd cost" follow-up. **Update 0905: BUG-0044 / 0045 / 0047 / 0049 fixed;
  BUG-0046 and BUG-0050 accepted as known limits (see the M1 entry at the top).**
- **To watch it:** `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CrowdRoutingTests" --logger "console;verbosity=detailed"`.

### The game window shows a 3D map you can fly over (view track, M2-1, 2026-10-05)

- **What was built:** the first picture. `& $env:GODOT --path game` now opens a window with a
  generated map in 3D: flat plateaus at three heights (tan lowland, green upland, pale stone top),
  dark vertical cliff walls, orange sloped ramps with walls at their sides, a sun with shadows and a
  sky. The simulation ticks underneath at 20 ticks per second; a small white label in the top-left
  shows the tick number, game speed, how long the last tick took, and the frame rate. No units are
  drawn yet (next view task). Update M2-2: units are drawn now, see the entry above.
- **Try it:** arrow keys pan; push the mouse against a window edge to pan; hold the middle mouse
  button and drag to grab the ground; mouse wheel zooms between 20 m and 60 m above the ground.
  The camera can't leave the map. `& $env:GODOT --path game -- --seed 6` shows a map with all three
  heights in view at the start; `-- --speed 4` runs the sim at 4x.
- **Screenshot tool (for the studio and for you):** `& $env:GODOT --path game -- --screenshot
  C:\temp\shot.png --screenshot-after 2` saves a picture after 2 seconds and quits. Every later
  visual task is checked with it, so the studio no longer needs the optional Godot MCP install.
- **Producer decisions, revisit any time:**
  - *Placeholder colours* (three level tints, ramp orange, cliff brown) are hard-coded in the mesh
    builder for now; biome colours and real materials come with the art pass (M6). Change them by
    an inbox note until then.
  - *Camera numbers from the design doc as written:* 55° pitch, no rotation, 20-60 m zoom, 4 m per
    wheel notch, pan speed equal to the zoom height per second. Alternative: a rotating camera; the
    doc rules it out for readability.
  - *The pure camera/clock/mesh logic lives in `sim/Rts.Sim/ViewApi/`* so it is unit-tested
    without Godot. It reads the sim and never writes it (QA proved this: 100 mesh builds between
    ticks leave the game state hash untouched).
- **Rough edges:** ramps look a little steeper at their two ends than in the middle (about 31° vs
  22°), because heights are stored at cell centres; cosmetic, no fix planned unless it bothers you.
  Three tiny input nits (BUG-0041: a `--seed` with no value swallows the next flag; the start-up log
  prints the largest possible seed as -1; a clock corner case no code path reaches) go to the view
  hardening session. Update M2-2: BUG-0041 fixed. The test scene `game/tests/CameraClampTest.tscn`
  compiles into the game for now; it is excluded when the release build exists (M6).

### Replays and a determinism proof that outlives the session (sim track, M1-6, 2026-10-05)

- **What was built:** a replay file format and player. A replay stores the map seed and settings,
  every command the game accepted (with the tick it was given), and a fingerprint of the game state
  every 5 seconds. Playing it back re-runs the match from the seed and checks every fingerprint;
  the first mismatch stops it and names the tick. One "golden" replay is checked in: 200 units of
  every kind marching across the seed-1 map, 75 seconds of game time, 15 fingerprints, a 15 KB text
  file you can read in a diff.
- **What it means for you:** from now on, any change to movement, tick order, random numbers or
  unit stats that changes how a match plays out fails the test suite until a developer regenerates
  the golden on purpose and explains why in the commit. This is the guard that keeps future replays
  and saved games exact, and it's the backbone of the M6 replay viewer. Update M1-4d-3: the guard
  did its job; the golden was regenerated once, with the reason in the commit. Update M1-7: the
  file format grew a column (format 2) and the fingerprints stayed byte-identical. Update M1-8: you
  can record and play replays yourself from the command line (see the M1-8 entry). Update M1-9: the
  golden stayed byte-identical through the whole clean-up batch. Update 1255: format 3 (resources);
  the fingerprints changed because the fingerprint now covers trees and mines, but every unit's path
  was proven identical before and after.
- **Also fixed (BUG-0014):** two particular seeds (0 and the largest possible number) used to give
  the same map. The seed is now scrambled before use, so every seed is its own map. Side effect:
  **every map changed once.** Seed 1 today is not the seed 1 of yesterday. Nothing you have seen is
  lost (nothing was on screen yet).
- **Producer decisions, revisit any time:**
  - *A replay is tied to the exact game data it was recorded with*, including display text. Change
    one number in `game/data/` and old replays refuse to play (with a clear code, never a silent
    desync). Alternative: hash only balance-relevant fields; rejected for now because a wrong guess
    there means a desync that looks like a bug.
  - *Fingerprints every 100 ticks (5 s)* by default; file size grows ~17 bytes per checkpoint, so a
    30-minute match is under 1 MB even with thousands of orders.
  - *Six movement tests keep running on their old maps* through a helper that undoes the seed
    scramble, because their pass/fail bounds were tuned to one map. QA showed those bounds fail on
    most other maps (BUG-0039, S3, pre-existing): the next sim session, a hardening one, re-measures
    them on many maps. Update M1-4d-3: done; the helper is gone from those tests and the bounds hold
    on every swept map (one 64-goal map still breaks a bound, BUG-0049). Update M1-9: BUG-0049
    re-bounded on 80 maps.
- **Rough edges:** a hand-edited replay can declare a tick count of two billion and make playback
  spin for hours (BUG-0040, S4; a limit is a few lines, hardening session). Update M1-4d-3: the
  24-hour limit is in. When the AI arrives (M5) the recording point inside the tick needs a decision
  (also BUG-0040, still open for that part).

### Two tracks started: the view track begins M2 while the sim track finishes M1 (2026-10-05, session 1446 plan)

- **What changed for you:** from this session the game window stops being an empty scene (see the
  M2-1 entry above: it landed). Units, selection and right-click orders are the next view task (M2-2).
  Update 1609: M2-2 landed too. Update 2330: the minimap (M2-4) landed. Update 0655: the order keys
  (M2-3) landed; M1's criteria are all met. Update 0905: M1 signed off; sim starts M3. Update 1255:
  resources (M3-1) and the F12 overlay (M2-5) landed.
- **Producer decisions, revisit any time:**
  - *Where the view's tests live.* Dev tests in `sim/Rts.Sim.Tests/ViewApi/`, QA tests in
    `sim/Rts.Sim.Tests/QA/ViewApi/`, Godot-side test scenes in `game/tests/`; `ViewApi/` may hold
    pure helpers with no sim reference (fixed-step clock, terrain geometry, camera limits) so that
    logic is unit-tested without Godot. Alternative: put those helpers in `game/scripts/`, where
    nothing can test them. Now also written into the ownership table in docs/07.
  - *The sim track keeps its public setup API additive* (no new required fields on `SimConfig`, no
    signature changes to `Simulation` or the data loader), so the view compiles after both merge.
    Held this session and the next. Update 2330: held again (M1-7 kept the 3-argument `Move`).
    Update 0655: held (M1-8 added one read-only method and nothing else to the sim). Update 0905:
    held (one unused public constant renamed; `Enqueue` now throws on malformed commands, which the
    view never produces). Update 1255: held (`SimConfig.ResourceCapacity` optional; a new required
    data file `resources.json` that the game already ships).
  - *First view slice order:* scene + terrain + camera + screenshot flag before unit views, because
    the screenshot flag is how the studio (and you) verify every later visual task.
  - *Bug numbering with two QA inspectors:* both filed a BUG-0039 this session. From now on the
    brief gives each track its own starting number (view = sim + 10). The view's bug is BUG-0041.
- **Docs drift for you to fix (one line, your file):** `CLAUDE.md` still says "Current milestone:
  M1" and "don't write gameplay code ahead of the roadmap". With two tracks that line is per
  track (sim M3 now, view M2). The agents don't edit `CLAUDE.md`; suggested text: "Current
  milestones: M3 (sim track), M2 (view track)". Update 0655: plus the CLI command line (M1-8 entry).
- **Cap note:** `autopilot.md` says 8 sessions per day; the old dashboard said 10. The studio
  follows autopilot (8).

### The M1 headline works: an army crosses the map and climbs a ramp (M1-5, 2026-10-05)

- **What was built:** the proof test for M1's main promise. 200 soldiers of every kind (foot,
  cavalry, siege) start near the west edge of a generated map and are sent to the far side of a
  plateau, so they must funnel through a 6 m ramp. On every map tried (58 maps, plus 20 with a
  climb of two ramps, plus armies of 500 and 1,000) everyone arrives, nobody gives up, nobody ends
  up in a cliff, and running the same march twice gives the identical result tick for tick. The
  march takes 72-127 seconds of game time, under half the allowed limit.
- **What you'll see:** select your whole army and right-click across the map: the group streams
  through the ramp as a crowd, the ones pressed against the ramp's walls wait their turn instead
  of quitting, and the whole army arrives and packs up around the click point. Before this
  session, a few units (1-9 per march) got pinned at the corners of ramps and gaps between
  plateaus, stopped, and stayed behind. Update M2-2: you can do exactly this in the game window now.
- **Producer decisions, revisit any time:**
  - *Waiting in a moving queue doesn't count as stuck.* A unit only starts its 1-second give-up
    timer when nobody just ahead of it is making headway either. Trade-off: a jammed crowd (for
    example behind an enemy blocking a pass) takes a little longer to give up, about 10-14 seconds
    for 60 units instead of 9-10.
  - *Enemy units holding their ground are solid.* QA found that a walker squeezed between two
    standing enemies could slip through a 0.2 m slit (an old bug, made worse by the new patience).
    Now a line of enemies plugging a ramp holds: nobody gets through, everyone behind gives up.
    Your *own* standing units stay "soft" (a walker can still slip between two of them, and can
    shove idle ones aside), because making them solid too cut crowd arrivals in the tests.
    **Correction (1609):** "nobody gets through" held for the 3-unit plugs tested then. Plugs of 5
    or more enemies can still be leaked through by your own units shoving each other (BUG-0045,
    S3, pre-existing); plugs of up to 4 hold since M1-4d-3. See that entry. **Update 0905: plugs of
    up to 32 hold; your own *holding* units are solid too.**
- **Rough edges (S3, next hardening session):** if an enemy is standing exactly on the spot you
  clicked, your unit treats it as a friend and stops touching it, a hand's width inside it
  (BUG-0037); and a unit squeezed between an enemy and its own standing comrade can press into the
  comrade by up to 14 cm for a tick (BUG-0038). Neither lets anyone through a plug. Also, with
  500-1,000 units marching, a few pairs end up closer than they should (a known limit of the
  back-off rule). Update M1-4d-3: BUG-0037 and BUG-0038 fixed.
- **To watch it:** `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ScenarioTests" --logger "console;verbosity=detailed"` prints each seed's path length, ticks taken and the limit. Update M1-6: the same march is now the golden replay (`sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`). Update M1-8: `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200` runs the same kind of march from the command line.

### Idle units step aside for walkers (M1-4d-2, 2026-10-05)

- **What you'll see:** soldiers walking through your own idle troops push them out of the way
  instead of stopping. A clustered group bends to let walkers by but doesn't split apart. A lone
  unit parked in a narrow pass steps aside once a friendly walker has been stuck behind it for
  0.5 s. Enemy units and units that are already walking are never pushed. A unit squeezed between
  a standing unit and a cliff can walk away again (BUG-0031 fixed). Update M1-7: a unit told to
  hold position is never pushed either.
- **Producer calls, revisit any time:**
  - Accepted although my own crowd targets were missed: groups sent to 4 nearby points now arrive
    35-44% of the time (was about 16%), against the 60-80% I asked for. The causes are structural
    (pathfinding ignores units; units waiting for a path and enemies act as walls) and were
    outside this task. QA filed them as S2 (BUG-0032/0033); I set them to S3 and scheduled a
    follow-up, M1-4d-3 crowd routing. Update 2026-10-05-1234: that follow-up now waits for the M1
    hardening session (your rule: S3 work goes there), and the cross-map scenario test (M1-5) went
    first because one army walking to one spot doesn't need it (confirmed: it passed without it).
    Update 1446: the hardening session is the next sim session. Update 1609: it landed (see the
    M1-4d-3 entry above); BUG-0033 fixed, BUG-0032 stays open for the crowd-cost follow-up.
  - Units standing on their own click point hold it. So a parked *group* in a 1-cell corridor still
    blocks its own army until you move it (BUG-0033 remainder). Update 1609: a parked pair now
    yields; a cluster's spot still holds.
- **Rough edge:** walkers crossing a cluster that mixes both players' units mostly give up (2 of
  200 in a test, 28 before shoving). M1-4d-3 targets this. Update 1609: 111 of 200 now.
- **To watch it:** `dotnet test sim/Rts.Sim.Tests --filter "PastAFriendlyUnitParkedThereByAMove|WalkersCrossingASettledBlob" --logger "console;verbosity=detailed"`.

### Two studio sessions overlapped (incident note, 2026-10-05)

- At about 12:14 a second session (id 2026-10-05-1214) overwrote `studio/.session.lock` in your
  checkout while session 1013 was still running; your three studio-skill commits (724234a,
  432fafe, c089405) went to main meanwhile. The studio worktree was untouched and session 1013
  finished normally. If that second session is still chaining, two conductors may be running.
  Your "Downloads stay with the owner" inbox note was processed at the 1234 PLAN (reflected in
  the M6 queue line). Session 1234 ran on the studio-session skill as loaded at its start; your
  later two-track (sim + view) change to the skill applies from the next session. Update 1446:
  this session is the first on the two-track skill; no second conductor has shown up since.
  Update 1609: a power outage at about 16:40 interrupted both builders; both were resumed from
  their transcripts with work intact, no second conductor. Update 2330: a quiet session. Update
  0655: quiet again. Update 0905: one near miss, no harm: the view builder's file I/O used a
  relative path and briefly rewrote two files in *your* checkout (`game/scripts/UnitViews.cs`,
  `game/tests/UnitViewsTest.cs`); restored byte-identical, your `git status` shows only your own
  `.claude/settings.json` change. Builders are reminded to use absolute paths. Update 1255: quiet
  (Remote Control was refused as "unavailable in unattended sessions"; no effect on the work).

### Units cluster around the click point, and give up when stuck (M1-4d-1, 2026-10-05)

- **What you'll see:** select 30 soldiers and right-click a spot. They walk there and pack into a
  tight cluster centered on the click, shoulder to shoulder, rather than lining up in a formation.
  Two units meeting in a narrow pass both step to their right and slip past each other.
  Spam-clicking the same spot doesn't make them restart or stutter.
- **Giving up:** a unit that makes no headway for 1 second (wedged behind a crowd, or blocked by
  standing units) stops and stands idle instead of jittering in place forever. Update M1-5: waiting
  behind a groupmate who is still moving no longer counts as "no headway". Update M1-4d-3: nor does
  waiting behind anyone's moving traffic within 2 m.
- **Producer decisions:**
  - Clusters instead of formations for now: StarCraft-style clumping, where Age of Empires uses
    line or box formations. Formations can come back in M2 with group commands.
  - 1 second before giving up. Shorter feels snappier but units stop short more often; longer
    means more pushing and shoving before they quit.
  - Clicking the spot a unit is already heading to counts as the same order.
- **Rough edges until the next session (shoving):** units that are standing still never step
  aside yet. So two groups sent to spots close together bump into each other's clusters and many
  stop short: in a stress test, 84% of 500 units sent to 4 nearby points gave up 8-16 m early
  (BUG-0028). A unit squeezed between a standing unit and a cliff can't walk away (BUG-0031).
  Both should go away once idle units make room for moving ones. (Update: shoving landed in
  M1-4d-2; BUG-0031 fixed, BUG-0028 partly, see the entry above.)
- **Also open:** a short nudge order (under about 2.8 m, inside the same 2 m map cell) to a unit
  that has already arrived is ignored (BUG-0030). It's in the debt backlog. Update M1-4d-3: fixed.

### Pathfinding: shared arrow maps with a speed limit (M1-4b and M1-4c, 2026-10-04)

- **What it is:** when you send units somewhere, the game computes one map of arrows for that
  destination: every 2 m square points along the shortest route there. Every unit headed to that
  spot follows the same map, so moving 200 units costs about the same as moving one. The game
  remembers recent destinations (32 to 128, depending on how many units the game allows), so
  re-ordering to a recent spot is instant.
- **Speed limit (Producer decision):** at most 2 new arrow maps per tick, which is 40 per second,
  oldest orders first. Each takes about 0.7 ms to build, and the limit keeps every tick inside its
  time budget so the game never hitches.
- **What you'll see:** nothing in normal play. Only in a big burst: order 32 separate groups to 32
  different spots at the same instant and the last group starts walking about 0.8 s after the first.
  Update M1-8: the M2-5 debug overlay will be able to draw these arrows on the ground. Update 1255:
  it does; press F12 (see the overlay entry at the top).
- **Why it's saved with the game (Producer decision):** which arrow maps are remembered decides
  which units wait a tick, so that memory is saved and checked like the rest of the game state.
  That keeps replays and saved games exact.
- **Rough edges (debt backlog):** with more separate destinations active at once than the game
  remembers (only in very large games with many small groups), some older groups can stall while
  newer ones walk (BUG-0025). In same-instant order bursts, one side of the map gets served
  0.1-0.2 s sooner on average (BUG-0026, cosmetic). Update M1-7: maps larger than 256 x 256 are
  now documented as unsupported until arrow maps can be built in slices (BUG-0023); the design
  map is 128 x 128.

### Terraced maps: plateaus, cliffs and ramps (M1-3 and M1-4a, 2026-10-03)

- **What you'll see:** maps have up to three height levels (ground, 4 m and 8 m up), each a flat
  plateau. Plateau edges are cliffs no unit can climb. The only way up is a ramp, 6 m wide and 8 m
  long with a gentle slope, entered only at its top or bottom because its sides are walled. Ramps
  are natural chokepoints, wide enough for about 7 foot soldiers or 4 horsemen side by side, so a
  few defenders can hold one. In M4 the high-ground rule raises the stakes: units below can't see
  up onto a plateau. Update M2-1: you can now see these in the game window.
- **Cliff edges (Producer decision):** the outer 2 m strip of each plateau counts as cliff, so
  units on high ground stand about one step back from the visible edge. The alternative (walls
  between squares) would let them stand right at the lip but makes pathfinding more complex.
- **Safety nets:** the map's outer ring is impassable, and any patch of ground no ramp reaches is
  sealed off, so a unit can never be stranded. The generator re-rolls a layout that comes out too
  blocked (under half walkable) or missing a height level, up to 8 times (Producer decision), then
  takes the best one, so map generation can't hang. A normal map takes about 4 ms to make.
- Also added: a fast "who's near me" lookup grid that movement and later combat use. Nothing to
  review there.

### First real game data (M1-2, 2026-10-03)

- **What it is:** the Malazan and Whirlwind rosters (7 units each, stats from the faction pages),
  the damage-type table and the economy rules now live as JSON in `game/data/`. The game checks
  every file when it starts and lists every mistake at once.
- **Values the docs didn't specify (Producer decision, first pass):**
  - **Body size:** 0.4 m radius for foot soldiers, 0.7 m for cavalry, 0.9 m for siege. This is the
    one you'll notice most: it decides how tightly crowds pack and how many units fit through a
    ramp at once. Update M2-2: you can see the three sizes on screen now.
  - Per-unit attack wind-up times, faction color palettes, and a placeholder for "requires Age II".
- **To change them:** edit the numbers in `game/data/factions/<faction>/units.json`, or ask the
  studio. No code changes are needed. Update M1-6: a data edit also means regenerating the golden
  replay (the studio does this; it's one command). Update 1255: `common/resources.json` joins the
  data (tree and gold-mine types).

### Optional: Godot MCP server (M0)

- A tool that lets Claude launch the game and take screenshots more easily. Not needed: since
  M2-1 the game has its own `--screenshot` flag, which covers the studio's need.

## Requests for the sim track

What the view track needs from the sim and can't add itself (the Producer plans these for the sim
track right after S1/S2 bugs).

1. ~~`Stop`, `HoldPosition`, `AttackMove` command kinds and shift-queued orders~~ → **done in
   M1-7** (session 2330).
2. ~~Public read access to a cached flow field's directions, for the M2-5 debug overlay~~ → **done
   in M1-8** (session 0655): `FlowFieldCache.PeekCached(targetCell)`; read `DirectionAt` / `CostAt`,
   don't keep it across ticks. M2-5 used it (session 1255).
3. ~~`DataError.ToString()` shouldn't print `: :` for empty fields~~ → **done in M1-9** (session 0905).
4. Low priority: a previous-tick facing (`PrevFacing`) so unit views can blend turns; today the
   view snaps yaw per tick (M2-2 note, M2-7 polish). Fold into any M3 slice touching `UnitStore`.
5. Noted, not requested: Shift-queued legs through a packed friendly group give up (BUG-0028);
   the M2-3 test scene works round it with spread-out units. Crowd-cost follow-up after M4.
6. Noted (M2-5, session 1255): with 64+ live goals under the 2-builds-per-tick cap about 90 % of
   selected goals had no cached field in QA's churn; that is BUG-0025 (sim debt). No depletion
   events for resource nodes: the view polls `World.Resources.Alive` / `Generation` (fine for M2).

## Feature queue: sim track (feature sessions, in order)

1. **Next: M3-2** worker gather / return loop: `Gather` command, worker state machine in tick phase 4
   (walk to node, gather `workerCarry` at `gatherRate`, return to the nearest drop-off by walking
   distance, repeat; `nodeSearchRadius` for the next node when one depletes), per-player resource
   totals in the hash. Needs a drop-off: a minimal building store with pre-placed Town Halls
   (recommended; it becomes M3-3's store) or M3-3 first. The brief settles BUG-0073 (open-only grid
   changes keep fields usable) and BUG-0075 (gather only from an adjacent reachable cell). QA full.
2. M3-3 building placement (ghost validity rule in the sim, construction with multiple builders,
   repair); M3-4 production queues (5 slots), rally points, population and cap, refunds; M3-5 Age II
   research and Forge upgrades; M3-6 full Malazan and Whirlwind data (units, buildings, techs).
   **Requests for the sim track** above outrank M3 work.
3. M6 (far ahead): agents can't download. The Producer lists under "Waiting on you", when M5
   starts, the exact links for the Godot 4.7.2 .NET export templates and the Kenney/KayKit/Quaternius
   packs (docs/04) with the `asset-sources/` folder for each (owner note 2026-10-05).

## Feature queue: view track (feature sessions, in order)

1. **Next: trees and gold mines as MultiMesh** from `World.Resources` (M2 criterion 3; rocks are
   decorative and optional): `Match` builds its map with forests and mines (launch flags `--forests`
   / `--mines`, defaults a Producer call at the PLAN), placeholder meshes sized from the footprint,
   instances hidden when a node's `Alive` goes false, 0 bytes per frame, hash twin. QA standard.
2. M2-6: placeholder audio for select and command (generated tones or CC0 already in repo; no
   downloads by agents).
3. M2-7: playable check, 100 placeholder units at 60 FPS → M2 end-of-milestone hardening (BUG-0069,
   BUG-0070, BUG-0083, BUG-0084, export hygiene notes) → M2 sign-off.
4. M3 view side after that: HUD resource bar, selection panel, command card (uses Tab subgroups),
   build ghosts, worker / gather feedback.

## Debt backlog: sim track (hardening sessions only; next one after 3 more feature sessions)

- **BUG-0073 (S3, design, M3-2 brief)** every tree fall invalidates every cached field; with the
  2-per-tick build cap, continuous felling leaves most groups standing. Open-only changes should keep
  fields usable and rebuild lazily; closing changes (buildings) still invalidate at once.
- **BUG-0075 (S3, rule, M3-2 brief)** felling an interior tree first leaves an unreachable hollow;
  gather only from a node cell adjacent to the passable cell the worker stands on; fix the two docs/03
  sentences; un-skip the QA row.
- **BUG-0074 (S3)** forest placer assumes 1 x 1 trees; validate (tree type must be 1 x 1) or
  generalize `TryForest` to footprints; un-skip the QA row.
- **BUG-0076 (S4)** M3-1 nits: full flood fill after the ring test can't fail (keep as a debug
  assertion), empty resources list loads clean (require one type per kind), -0 mine spacing. Item 1
  (docs timing) fixed by the Producer.
- **BUG-0071 (S3)** shove-pass plug cache: the cached answer can depend on which cluster member is
  asked first when a member stopped this tick within a hair of the link distance (stale hash);
  deterministic. Fix: widen `SearchPlug`'s query by `MaxUnitSpeed` in the shove pass (as
  `SettleBackedOff` does) and keep the exact gap test; un-skip the QA row.
- **BUG-0072 (S4)** CLI `--record` pre-check misses invalid file names / access-denied folders:
  open the file before ticking (or check `GetInvalidFileNameChars()`); docs/03 wording.
- **BUG-0046 (S3, known limit)** wall clips in slot order; a slot-free sort exists in the M1-9
  report (hard walls first, farthest first, ties by position) and re-rolls three fitted bounds.
  Revisit with the crowd-cost work after M4. 4 QA rows skipped.
- **BUG-0050 (S3, known limit)** random-goal give-ups 4.7% (target 3%); alternatives in docs/03.
- **BUG-0028 / BUG-0032 (S3)** crowd targets (4 points 51% / 34%, 64 goals median 22-24% give up,
  same-owner crossing 22 / 200): needs a crowd cost in the flow fields; after the M4 sandbox.
- BUG-0040 (S4) part 2: in-tick AI enqueue vs the phase-14 checkpoint is a design note for M5.
- BUG-0025 (S3) evict the live field with the newest order (the M2-5 churn confirms it at 64 goals)
  + BUG-0026 (S4) rotate same-tick tie-break.
- BUG-0005 (S3) per-player command buckets (O(n^2) insertion sort under a flood); before M5.
- BUG-0023 (S3) single field build > tick budget on maps > 256: documented as unsupported.
- Loader: BUG-0008 (S3) duplicate JSON keys, BUG-0010 (S4) faction slots; fold into M3-6 data.
- BUG-0002 (S4) `.sln` Release config maps RtsGame to Debug; with `tools/export.ps1` (M6).
- Perf (Debug, this machine, alone): 500 moving 0.62 ms, with 12 forests + 8 mines 0.82 ms; full
  4,096-slot resource hash 20 µs; 2,500 one-player tight blob 4.33 ms (enforced <= 4.5, ~3%
  headroom); two-player contested blob 6.84 ms (guard < 10.5); 2,500 to 4 points 3.08 ms (guard
  < 3.7); 1,000 walkers crossing a 1,500 blob 14.2 ms (report); CLI 2,500 march 4.6 ms. Setup: 128
  map with 12 forests + 8 mines +5 ms; 1024 map at the resource caps +2.2 s.
- Known limits: docs/03 "Known limits (M1), as of M1-9" is the list (flow fields ignore units;
  4.7% random-goal give-ups; the BUG-0048 map gives up 76 of 128; pack rule not guaranteed; partial
  spawn-order independence; clusters over 32 are not plugs; holders are walls only; maps > 256
  unsupported; perf measured in Debug). Plus: enqueue stamps `TickNumber + 1`; `SimInfo.Version`
  recorded but not checked; no depletion events (views poll); .NET 8 support ends 2026-11-10, move
  to the next LTS at M6.

## Debt backlog: view track (hardening sessions only; next one is the M2 end-of-milestone session)

- **BUG-0083 (S3)**: the overlay-on label line allocates ~600 B per frame; docs/03 "Debug tooling"
  claims 0 bytes on and off. Fix the doc claim (the layers are 0 B) or cache the string.
- **BUG-0084 (S4)**: cliff tint reads olive on green terrain (raise alpha or pick a bluer dark red);
  arrow ends dip up to 9 cm into steep ramp cells (lift by the cell's slope); CS8602 in
  `DebugOverlayTest.cs:173`; the dev allocation probe should relist with a live field.
- **BUG-0069 (S3)**: a lone minimap dot reads as its rim colour (1-4 px of owner colour in a 5 x 5 px
  square at 220 px / 128 cells); two light factions' lone units would look alike; empty cells in a
  formation leave rim lines. Default: 2 x 2-cell owner centre in a one-cell rim; alternatives:
  screen-space dots (fixed 3-4 px square + 1 px outline; also fixes the >220-cell note) or rim only
  round a crowd's outside. Owner may pick (For your review).
- **BUG-0070 (S4)**: docs/01 minimap decision row says "one map cell"; docs/03 "221 to 440" → "over
  220"; `DoubleTapSeconds` / `DoubleTapMs` twin literals (derive one); dev wall test blind to the last
  row / column (QA kills those mutants; add a hand map with a step on the far edges).
- Edge-pan hover suppression (`RtsCamera.EdgePanBlocker`) can't fire today: the minimap's 8 px margin
  keeps it out of the 8 px edge band. Harmless; revisit if the HUD layout changes.
- Export hygiene (M6): exclude `game/tests/` from the release build (now 14 scenes compile in); load
  `game/data/` in a way that works from a `.pck` instead of `ProjectSettings.GlobalizePath("res://data")`.
- `MinimapDotsShot --units 1000` silently drops its second lone unit (store full; documented).
- Cosmetic: ramp ends ~31° vs 22° mid-ramp; no wall skirts on the map border; 1024² mesh 904 MiB
  transient (outside supported sizes); `StartLayout` at radius 1.0 puts bodies exactly touching.
- Overlay deferred items (docs/03): per-unit state labels, arrows for more than one goal, a window
  that follows the visible trapezoid, the dev console.
- Note: headless scene runs print Godot warning stack traces for the expected "order dropped"
  warnings; not errors. Builders: absolute paths for all file I/O.
- Optional M0 item: Godot MCP server (owner install; not needed since `--screenshot`).

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-06 | [2026-10-06-1255](sessions/2026-10-06-1255.md) | sim M3-1 resource entities (`ResourceStore`, placer, depletion → nav grid, hash, replay format 3, CLI flags); view M2-5 debug overlay (F12: nav grid, flow arrows, tick graph, counts) | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 3 S3 + 1 S4; view 1 S3 + 1 S4). M3 1 / 8, M2 7 / 10 |
| 2026-10-06 | [2026-10-06-0905](sessions/2026-10-06-0905.md) | sim M1-9 M1 end-of-milestone hardening (9 items, 8 bugs fixed + BUG-0058); view M2-H1 view hardening (5 bugs fixed, rimmed minimap dots) | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 1 S3 + 1 S4; view 1 S3 + 1 S4). **M1 signed off by the Producer** |
| 2026-10-06 | [2026-10-06-0655](sessions/2026-10-06-0655.md) | sim M1-8 headless CLI (`tools/Rts.Cli`) + `FlowFieldCache.PeekCached`; view M2-3 A / S / H / Shift-queue, type select, control groups, Tab subgroups | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 1 S4; view 1 S3 + 1 S4). All 8 M1 criteria met |
| 2026-10-05 | [2026-10-05-2330](sessions/2026-10-05-2330.md) | sim M1-7 perf criterion + Stop / HoldPosition / AttackMove + shift-queue (replay format 2); view M2-4 minimap + `Hud` + `--no-hud` | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 2 S3 + 1 S4, view 1 S4) |
| 2026-10-05 | [2026-10-05-1609](sessions/2026-10-05-1609.md) | sim M1-4d-3 hardening batch (crowd routing + 6 debt bugs); view M2-2 unit views + selection + right-click move + BUG-0041 | both ACCEPT; sim 2 fix rounds (QA FAIL x3: S1 + 2 S2 fixed in-session, BUG-0045 S2 → S3 by the Producer, 4 S3 + 1 S4 filed); view 0 fix rounds (PASS_WITH_ISSUES: 1 S3 + 1 S4) |
| 2026-10-05 | [2026-10-05-1446](sessions/2026-10-05-1446.md) | sim M1-6 replays + golden + BUG-0014; view M2-1 match scene, terrain mesh, camera, screenshot flag | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: 1 S3 + 2 S4 filed, BUG-0014 fixed); first two-track session |
| 2026-10-05 | [2026-10-05-1234](sessions/2026-10-05-1234.md) | M1-5 cross-map scenario test + queued-walker give-up rule + BUG-0035 enemies as hard walls | ACCEPT after 1 fix round (QA FAIL then PASS_WITH_ISSUES; S2 + S4 fixed in-session, 2 S3 filed) |
| 2026-10-05 | [2026-10-05-1013](sessions/2026-10-05-1013.md) | M1-4d-2 shoving of idle units + BUG-0031 fix + re-tightened assertions | ACCEPT after 2 fix rounds (QA FAIL on crowd targets; S2 x2 re-triaged S3, 1 S3 filed, 1 S3 fixed) |
| 2026-10-05 | [2026-10-05-0742](sessions/2026-10-05-0742.md) | M1-4d-1 separation, crowded arrival, give-up, adjacent-cell steering | ACCEPT after 1 fix round (QA: S2 + S3 fixed in-session, 3 S3 open) |
| 2026-10-04 | [2026-10-04-2056](sessions/2026-10-04-2056.md) | M1-4c build-cap determinism + fairness; suite de-flaked | ACCEPT, 0 fix rounds (hardening) |
| 2026-10-04 | [2026-10-04-0120](sessions/2026-10-04-0120.md) | M1-4b GameData in sim + flow fields + LRU cache + `Move` | ACCEPT after 1 fix round |
| 2026-10-03 | [2026-10-03-2220](sessions/2026-10-03-2220.md) | M1-4a ramp walls + param safety + spatial hash | ACCEPT after 1 fix round |
| 2026-10-03 | [2026-10-03-1235](sessions/2026-10-03-1235.md) | M1-3 heightmap + nav grid + 2 bug fixes | ACCEPT |
| 2026-10-03 | [2026-10-03-1151](sessions/2026-10-03-1151.md) | M1-2 data loader + 3 bug fixes | ACCEPT |
| 2026-10-03 | [2026-10-03-0907](sessions/2026-10-03-0907.md) | M1-1 sim core | ACCEPT |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
