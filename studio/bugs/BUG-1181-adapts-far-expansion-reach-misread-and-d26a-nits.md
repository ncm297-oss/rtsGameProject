# BUG-1181: "How the AI adapts" says a free start location's mine qualifies whatever its distance; the 384 m reach applies to every mine (plus D26a nits)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (D-R35, 2026-11-09-2226; the Producer's docs/02 half at the 2026-11-09-1336 ACCEPT) |
| Found | 2026-11-09-1336, task D26a |
| System | Docs: faction pages' "How the AI adapts" (and docs/02 "AI opponent", which has the same wording) |
| Fixed by | D-R35 780d73c: both pages' Far expansion bullet ("The reach holds for every mine, a start location's included: a free start farther than that from the keep is not walked to, and a mine beyond it is never taken"); pinned both ways by `Content/AiAdaptsContentTests` + QA's `DR35ProsePinQaTests.TheFarExpansionBullet_SaysTheReachHoldsForStarts` (Producer: whirlwind page 385 red, `rules.json` 380 red on both pages). Nits: 1 ("Three rules ... and two notes", `TheIntro_CountsTheBullets`), 2 (position-based number guard: "within 2 minutes" / "0.5 of the match" / "384 m apart" red), 4 (`'none'` printed) fixed in the same commit; 3 left as is (the bracket already says the Raider Camp case is a Malazan measurement; Producer ruling); 5 skipped (the wording lives only in a report, not on a page) |

## Repro
1. Read `docs/factions/malazan.md` (and `whirlwind.md`) "How the AI adapts", the Far expansion bullet: "Once it has seen a
   free mine there, or anywhere within `ai.farExpandReach` 384 m of its Garrison Keep, with no enemy drop-off by it, it
   builds a Quartermaster's Depot". docs/02-game-design.md line 448 says the same ("a free mine there (or anywhere within
   ... 384 m)").
2. Read `sim/Rts.Sim/Ai/FarExpansion.cs` `Candidate` (line 138): `if (d > reach || d >= best) continue;` is applied to every
   seen gold node, start-location mines included. docs/03 "Implementation (M5-9b)" (line 9489) also says "within
   `farExpandReach` of the hall's centre" with no exception for start locations.

## Expected
The page states the rule the sim plays: the drop-off goes only by a free mine within 384 m of the hall, wherever it was
seen. (Corrected at the 2026-11-09-2226 ACCEPT: since M5-H6 / BUG-1159 the leg itself walks only to free starts within
the reach (`FarExpansion.NextStart` skips a farther one), so a start beyond 384 m is neither walked to nor its mine taken;
the filed text's "walks ... whatever their distance" was M5-9b's behaviour.)

## Actual
"there, or anywhere within 384 m" reads as: a start-location mine always qualifies, and other mines only within 384 m. On
the 128 map (256 m side, 362 m diagonal) the two readings agree, but on the 192 / 256 maps (`--map-size`, 384 / 512 m
sides) a free start in another corner can be more than 384 m from the hall: the leg walks there, sees the mine, and the
AI builds nothing by it.

## Notes
Inherited from docs/02 line 448 (sim / docs owner), so the fix touches both places. Grouped S4 nits from the same task:
1. Both sub-sections open "Three rules sit on top of every opening above" and then list five bullets ("What answers what"
   and "Where the shift doesn't show" are not rules); a reader counts five.
2. The page-to-data number guard accepts any number equal to a pinned value (0, 0.5, 1, 1.5, 2, 60, 75, 180, 384), so
   "One at a time, within 2 minutes." or "at 0.5 of the match" passes the pin (verified on a scratch clone). A tick or a
   digest-sized number is caught (2400 reads red).
3. The Whirlwind BUG-1156 bullet states the Raider Camp case from a Malazan measurement (it says so in brackets); fine as
   long as nobody reads it as measured.
4. The pin's failure message for a "none" row prints `page ''` instead of `page 'none'` (e.g. "whirlwind enemy Unique:
   page '' vs data (ai.counters) 'Raider'").
5. The developer's BUG-1156 candidate (B) reason reads "Line counter" as "a unit that counters an enemy Line" (Cadre Mage /
   Catapult, Age II). The brief's "(if the roster has one beyond Heavy Infantry)" means another unit that answers Shock,
   like Heavy Infantry. The conclusion is the same: in the Malazan roster only Heavy Infantry answers Shock
   (`ai.counters` `shock: ["line"]`), so (B) is not buildable either way; the table's stated reason should say so.
