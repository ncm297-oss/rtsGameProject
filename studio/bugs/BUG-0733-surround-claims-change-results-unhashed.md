# BUG-0733: Surround claims change the run but are not in the state hash (breaks save/load later)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | wontfix |
| Found | 2026-10-15-2325, task M5-P1 |
| System | combat / determinism (`World.SurroundClaim`, `World.SurroundHolder`, `StateHasher`) |
| Fixed by | 620ac47 documents it (Producer decision 2026-10-16: not hashed; the M6 save stores both arrays whole and its round-trip test compares them directly) |

## Repro
QA scratch test (not committed; reproduce in a test): two identical sims, 40 Heavy Infantry ordered at a Whirlwind Tent,
hp topped up. At tick 61, assert equal hashes; in sim `y` clear `SurroundHolder` and fill `SurroundClaim` with -1; assert
equal hashes again (they are); tick on.
```
tick 61: claims cleared in y, hashes still equal
hashes split at tick 62 (0 ticks after the claims went)
```

## Expected
docs/03 (determinism, and the save/load plan): every piece of state that decides the next tick is in `StateHash`, so a
save/load mid-run equals an uninterrupted run and a desync check sees every divergence. Same class as BUG-0021 (flow-field
cache contents changed results but were not hashed; S3, fixed by hashing the cache).

## Actual
docs/03 "Implementation (pathing slice 1)" says the claims are "not hashed: they only steer where a chase walks, and the walk
is hashed". The walk is hashed, but the claims decide the next walk: two states with the same hash split one tick later.

## Notes
- Not a determinism break today (same seed + commands still give the same hashes: QA's 2,000-tick twins at 10 / 40
  attackers pass) and there is no save yet. Fix: hash each live unit's held spot (or -1), in slot order, or document the
  claims as save state that the future save must carry, and say so in docs/03.

## Verification (QA, 2026-10-16, round 1)
Documented, not fixed (Producer decision 2026-10-16): docs/03 "Implementation (pathing slice 1)" now says the claims are
state outside the hash, and "Save/load and replays" requires the M6 save to store `SurroundClaim` and `SurroundHolder`
whole and its round-trip test to compare them directly. `StateHasher` is untouched by 620ac47 (it changes only docs/03,
a `World.cs` doc comment, `CombatSystem.cs` and tests); `git diff 53e69d7 HEAD -- sim/Rts.Sim.Tests/Replays
sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` is empty (goldens byte-identical) and `ReplayGoldenTests` / `GatherWedgeQaTests`
are green without a regen. The M6 save task inherits the requirement.
