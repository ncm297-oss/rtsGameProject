# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-10-1015** (third full session of 2026-10-10, cap 8; all three tracks
accepted with 0 fix rounds). Base for the next session = `origin/main` after this integration (sim M4-4b-3a → view M4-V6c →
data D-H2). The next PLAN writes the briefs; this file says where we are and what to pick.

## Where we are

- M0-M3 Done. **M4: 6 / 10.** Criterion 6 (abilities, statuses, zones) owes summons / `targetUnit` / `autocast` (sim M4-4b-3b)
  and the Frenzied marker + the `selfAura` card (view M4-V6d). Criterion 8 (the four signature abilities) **works in the
  rules since M4-4b-3a** but is not ticked: BUG-0421 (hp techs never raise hit points, so Dryjhna's Prophecy's +20 HP is not
  real and the Zealot frenzies at 41 / 63) is the first item of M4-4b-3b; tick 8 with that fix. Then 7 stealth (M4-5), 10 the
  fog-on sandbox Playable (view M4-V7, can start now), the end-of-M4 hardening per track, sign-off.
- **No open S1 / S2.** S3 15 / S4 22 open by the merged index. New this session: BUG-0420 (S3, sim, exact-line float rounding
  for unshipped fractions), BUG-0421 (S3, sim, pre-existing: hp techs), BUG-0422 (S4, sim nits; item 1 fixed), BUG-0430 (S3,
  view: a zone disc across a cliff is hidden or floats), BUG-0431 (S4, view: own rim vs Whirlwind gold), BUG-0440 (S4, data:
  four unshipped wordings; item 5 fixed). BUG-0410 closed (D-H2).
- Counters after this session: **sim 1 / 4, view 1 / 4, data 0 / 4** (`hardening_every` 4).
- Golden `data-hash` is **D22C849A175F9ED5** (M4-4b-3a; in `GatherWedgeQaTests.SameGameDataHashes`). **D11 moves it next**
  (the Frenzied description, the `blinded` text): rule 3 applies (add the new hash to `SameGameDataHashes` in the same commit;
  the seed-21 match has no Zealot and no Frenzied). The sim's M4-4b-3b must then stay off `common/statuses.json` and every
  `game/data/factions/**` file, or the two tracks collide on the golden: if 3b needs a shipped `spawn` entry, it waits for the
  session after D11 (test-only abilities are the pattern this session set).
- **Bug ids next session:** sim from **BUG-0450**, view from **BUG-0460**, data from **BUG-0470** (this session's QAs ignored
  the per-track ranges and collided; the Producer renumbered on the view and data branches; say the ranges in every QA brief).
- Memory bound 231,920,000 (72 KB margin; itemised through M4-4b-3a). `EmptyTick(2500)` paired medians 481.0 / 483.5 µs of
  500: a summon store or an autocast scan must add no per-tick work in the no-user case (the `BelowHpUnits` counter pattern).
- Standing rules unchanged: (1) a sim task that changes a placement, vision, combat or data-loading rule runs the full suite
  incl. Perf once alone and the merged scene loop before it reports; (2) a sim task that adds a `UnitState` ships the
  `ui.json` `states.*` line itself; (3) whoever moves `data-hash` adds the new hash to `SameGameDataHashes` in the same commit
  when the seed-21 match is unaffected (BUG-0275 item 1 still waits for a re-record); (4) when the view needs a `ui.json` key
  the brief says so and the sim stays off the file; (5) when a sim brief changes a behaviour a view QA scene asserts on, the
  same session's view brief carries a named item for that scene; (6) **new:** a sim task that adds a `Command` kind adds it
  to `CommandDoorFuzzStressTests` and says so in the report (autocast's toggle, M4-4b-3b).

## Sim track

**Next: M4-4b-3b** · feature · QA full · ~800-1,000 lines (a new store, so the slice stays modest).
1. **BUG-0421 first** (S3): `UnitStore.TrySpawn` sets `Hp = def.Hp + Points(TechBonus(owner, type, TechStat.Hp))`; when an hp
   tech finishes (`TechSystem` / the research completion path), every live unit of the affected types gains the delta to its
   current hp at once (Producer default, owner may revisit: max and current move together; docs/01 row). `PassiveSystem.MaxHp`
   stays. Tests: a Zealot at 63 / 63 researches Prophecy → 83 / 83; one at 41 / 63 → 61 / 83 and not Frenzied; the
   `PassiveQaTests.ProphecyResearchedMidFight_*` row re-stated; `StateHashTests` (hp is hashed already). Ticks criterion 8.
2. Summons: `spawn` effect (`{ "kind": "spawn", "unit": <own-faction id>, "count", "lifetime": <s> }`), temporary units with
   a lifetime in ticks (a per-slot `ExpiresAtTick` or `LifetimeTicks`, hashed; no cost, no pop; freed on expiry through the
   death path with a `DeathEvent` the view already draws, or a distinct flag: pick, document), spawned at free cells round the
   resolve point (`FreeCellSearch`), slot order. Test-only ability (shadow.md Summon Wraiths 20 s is the design reference; no
   shipped entry: the golden and D11).
3. `targetUnit` kind (a unit handle as the target; range to the target's centre; walk then cast; cancelled if the target dies
   or hides; effects on the target only or within `radius` of it: docs/02 "Ability system"). `Command.UseAbility` needs a
   target handle (a new command kind or a field: check replay format 4; a format bump re-records the goldens per rule 3).
4. `autocast`: `abilities.json` `autocast: true` already loads (check), a toggle command (`ToggleAutocast`, a new kind → the
   `CommandDoorFuzz` list), per (unit, ability) hashed flag, the cast fires when a valid target is in range with the cooldown
   ready, through the same `UseAbility` path; the AI's hook noted for M5.
5. Fold BUG-0420 (an integer compare at the hp line, per mille at load) if `PassiveSystem` is touched.
Out of scope: stealth (M4-5), any `game/data/**` content change (D11 owns the files this session), `AbilityViews`.
After: M4-5 stealth / detection; the end-of-M4 sim hardening (BUG-0401, 0420 if left, 0302 b / c, 0275, 0271 / 0272, 0144,
0142, 0113 item 2, 0094, 0391, 0422).

## View track

**Next: M4-V6d + the start of M4-V7** · feature · QA standard.
1. M4-V6d: `AbilityViews` gets a `StatusKind.Frenzied` arm (a colour apart from orange / blue / violet on sand and both
   factions' bodies at zoom 30; windowed shot, looked at); the `selfAura` card (`AbilityDef.Kind == SelfAura`: the button
   casts at once, sending the caster's position; the radius ring drawn around the caster while hovering; no targeting state);
   a headless row with a test-only `selfAura` ability (in-memory data, the `SelfAuraTests.AddTo` pattern; Requests 30).
2. M4-V7 (criterion 10, the fog-on sandbox Playable): `M4PlayableTest.tscn` after `M3PlayableTest`'s pattern: both bases
   with armies incl. a Priest and Zealots at Age II, fog on, a scripted fight through the real HUD (attack-move, a Sandstorm
   cast, a Telas Fire, a Cusser on a building, a Zealot frenzy observed through the marker), `Check`s on kills / losses /
   fog state, ends with a PASS line; windowed shots for the owner. Size it to the budget; a second slice is fine.
3. Watch BUG-0430 in the sandbox shots: if a storm across a cliff looks broken in the playtest, pull it forward.
Out of scope: stealth visuals (M4-5), the minimap zone dots, BUG-0430 / 0431 unless pulled forward.
After: stealth visuals, the end-of-M4 view hardening (BUG-0430, 0431, 0400, 0250 item 2, 0148 item 1, 0126 items 5 / 6, export hygiene).

## Data track

**Next: D11** · feature · QA light · inbox tweaks first if any.
Files: `game/data/common/statuses.json` (`frenzied`, `blinded` descriptions), `docs/factions/whirlwind.md` (lines 55-57, the
Zealot passives; the Frenzied row if any), `docs/02-game-design.md` "Status effects" Frenzied Effect cell, `Content/**`,
`QA/Content/**`, `GatherWedgeQaTests.SameGameDataHashes` (the named exception), the golden replay.
1. Pin `UnitDef.Passives` both ways to whirlwind.md (Frenzy of the Apocalypse 50 % / +50 %; Martyrdom 6 m / 5 s / +50 %: the
   page must state the +50 % too, add it) and the Frenzied row (docs/02 Effect cell reworded to the exact kind: "attacks
   faster: the attack cooldown is divided by 1 + the strength; the wind-up is unchanged; only the strongest counts").
2. `Frenzied` out of `PendingStatuses`; its `description` may state "+50 %" only if the pin covers it (`DescriptionProblems`
   reads "%" as magnitude x 100: fine).
3. `blinded.description` "can't target anything more than 3 m away" → "can't attack anything more than 3 m away" (the Producer
   proposal under For your review since D10c; the owner has not vetoed).
4. One golden regen: every `k` line equal, the new hash in `SameGameDataHashes`, said in the commit.
Out of scope: any number change; `abilities.json`; `units.json` beyond text (none needed).
After: the owner's tweaks; the full balance pass once the sandbox gives numbers; `ai.json` build orders (M5); M7-M9 data.

## Watch-outs (all tracks)

- **Integration order sim → view → data.** Expected conflicts: `studio/bugs/README.md` (union by id), `studio/qa/coverage.md`
  (three sides), docs/03 (different sections), docs/01 (sim + data rows: keep both).
- **Golden:** D11 moves `data-hash`; the sim's 3b must not (test-only abilities, no `game/data/**` edit). If both must move it
  in one session, the data track STOPs that session instead.
- **Perf:** three suites at once fail wall-clock rows on base and head alike; a failure counts only alone.
- **Bug ids** per track as above; the QA briefs must say them.
- The view's `selfAura` row needs no shipped ability: use in-memory data. The view must not touch `game/data/**`.
- QA scratch directories carry the agent's name; never `rm -rf` a shared scratchpad name. Stale scratch worktrees are the
  owner's housekeeping (STATE "Waiting on you").
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
