# BUG-0138: A site under construction with a builder is effectively invulnerable: every build tick resets its hit points from progress

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-07-2014, task M4-1 |
| System | construction (`BuildingStore.SetWork`) vs combat damage (`BuildingStore.Damage`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SiteBeingBuilt_DamageSticks_TheNextBuildTickDoesNotUndoIt"`
2. A Camp Follower builds a Tent (500 hp). At 201 hp the site takes a 100-point hit (the call every melee hit on a
   building makes): 101 hp. The next tick it reads 202 hp. Kept up through the build (100 damage whenever it is above
   150 hp), it takes 23,900 damage in total and completes at 500 / 500 hp.

## Expected
docs/03 "Economy implementation": "Construction sites are buildings in `UnderConstruction` state with progress; HP
grows with progress", i.e. building adds hit points; damage taken stays taken. Brief criterion 5: a building killed by
melee is freed (sites included: they are targets like any building).

## Actual
`SetWork` writes `hp = max(1, maxHp x work / needed)` every tick a builder works, and full hp at completion, so any
damage below the site's current hit points is erased one tick later. A single worker makes a site immune to melee
(and later to everything that does less than the site's hp in one tick).

## Notes
The developer noticed this ("sites under construction recompute hp from progress") and left it. Fix idea: add the
progress delta to hp (`hp += max x (work - oldWork) / needed`, capped at max) instead of recomputing it, and do not
reset to max at completion. Regression test: the QA row above (it asserts the hit sticks and a site that took more
than its full hp is not complete at full hp).
