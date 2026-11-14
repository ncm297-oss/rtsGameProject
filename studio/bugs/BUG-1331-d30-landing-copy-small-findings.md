# BUG-1331: D30 landing copy, small findings (building scale "drawn" claim, the ability tooltip silent on the aftermath, the write helper reformats whole files)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (items 2 and 3 fixed in D-H8; item 1 is the view's) |
| Found | 2026-11-13-1118, task D30 |
| System | `docs/factions/teblor.md`, `Content/TeblorLanding`, `Content/TeblorLandingWriteTests` |
| Fixed by | items 2-3: D-H8 (2026-11-13-2306). Item 2: `TeblorLanding.FrenzyDescription` states 40% faster, 20% more damage, Slowed 20% for 4 seconds, pinned both ways (`TeblorLandingPinTests.TheFrenzyTooltip_*`); the landing page's Owner review table gains the `statuses.json` `teblor_frenzied` row (`TeblorRosterPinTests.TheLandingsStatusRow_IsHeldBothWays`). Item 3: the write helper edits the files' text (`TeblorLanding.EditText`, `DocText`; `TeblorLandingTextEditTests`): ten one-field edits are ten added lines, line endings kept, idempotent. Item 1 open (view) |

## Repro
1. Read `docs/factions/teblor.md` "Buildings" prose (line 123-124) and `grep -rn "\.Scale" game/scripts` (only `UnitDef.Scale`
   is read: `UnitViews`, `CombatViews`, `ProjectileViews`).
2. In a scratch clone outside `.claude/worktrees`, run `D30_WRITE_LANDING=1 dotnet test sim/Rts.Sim.Tests --filter
   FullyQualifiedName~TeblorLandingWriteTests`, then `git diff --stat`.

## Expected / Actual
1. **Building scale "drawn":** the page says "Models are 1.25× scale visually ... (`buildings.json` `scale`, the view's
   model size; the rule exists (M7-4); ...)". The data field exists, but no view code reads `BuildingDef.Scale` (STATE:
   the building's drawn `scale` moved to a later view session; BUG-1281 already notes docs/03 says the view reads it).
   After D31 the copy's text drops the note and reads "the view's model size: the Scale column", which stays untrue until
   the view draws it. Suggest "(drawn once the view reads it, M7-V4 or later)" or landing D31 with the view's change.
2. **Ability tooltip:** the copy proposes the status tooltip with the aftermath, but `blood_oil_frenzy`'s own description
   (what the player reads on the Shaman's button) stays "own units within 8 m attack 40% faster for 10 seconds", silent
   on the +20% damage and on the Slowed 20% for 4 s drawback that the page calls "the trade-off". An owner call at D31;
   the Owner review table has no row for the new status's description either (the developer noted no pin holds it).
3. **Write helper formatting:** `TeblorLandingWriteTests` writes the JSON re-indented and the two docs with
   `File.WriteAllLines` (CRLF on Windows, warnings from git): `buildings.json` shows 162 changed lines for ten one-field
   edits. Fine for a scratch check; if D31 uses it to produce the shipped files the diff will hide the real change.

## Notes
None affects D30's criteria; all three are inputs for D31.
