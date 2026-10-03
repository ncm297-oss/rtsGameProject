# BUG-0007: Data loader turns huge numbers into float Infinity or a negative int instead of rejecting them

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-03-1151, task M1-2 |
| System | data loader |
| Fixed by | |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.DataLoaderQaTests.HugeFloat_IsRejected_NotStoredAsInfinity` and
   `HugePopCap_IsRejected_NotOverflowedToNegative`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HugeFloat|FullyQualifiedName~HugePopCap"`

By hand: in a copy of `game/data/`, set `malazan_crossbowman.speed` to `1e300` (or `sight`,
`attack.range`, `attack.splash`, `attack.bonusVs.heavy`), or set `rules.json` `popCap` to
`2000000000`, then `DataLoader.LoadAll(copy)`.

## Expected
QA focus for M1-2: every out-of-range number gives a `DataError` with file and field; nothing
non-finite or wrapped reaches `GameData` (BUG-0006 already established that non-finite values must
never enter sim state).

## Actual
`LoadAll` returns `Ok` with no errors, and:
- `UnitDef.SpeedPerTick == +Infinity` (`malazan_crossbowman SpeedPerTick ∞`), likewise `Sight`,
  `Attack.Range`, `Attack.Splash`, `Attack.BonusVs[heavy]`. The checks run on the `double`
  (`> 0` passes for 1e300), then the value is narrowed with `(float)`, which overflows to Infinity.
  The same applies to damage-table `multipliers`, `gatherRate.*` and `nodeSearchRadius`.
- `popCap: 2000000000` passes `Int(min 1)`, then `HalfPopCap = 2 * popCap` wraps to `-294967296`.

## Notes
Related message quality (S4, same root cause, no wrong data gets through because an error is
still reported): `cooldown`/`windup`/`trainTime` of `1e10` report
`10000000000 s rounds to -2147483648 ticks` (the double -> int cast overflowed), and `pop: 1e10`
reports `pop 10000000000 is not a multiple of 0.5` (overflow in `ToHalfPop`). Suggested fix: an
upper bound per field in `DataLimits` (or at least "result must be finite and fit in int") checked
before narrowing.
