# BUG-0954: M6-V12 nits: a base raid can read "Your units are under attack", SfxTest renames a tracked clip in the working tree, `AttackAlerts.New` order past the ring

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-17-2344, task M6-V12 |
| System | view: `AttackAlerts` / `AlertHud`, `game/tests/SfxTest.cs` |
| Fixed by | |

## Repro / items
1. **Kind is fixed by the first hit.** A raid that hits a worker standing by the Town Hall one frame before it hits the
   hall raises a `Units` alert; the hall's hits (within 30 m, inside 10 s) join it, so the player reads "Your units are
   under attack" while the hall burns. Buildings-first only applies within one frame (docs/03 says so). Upgrading an
   alert's `Kind` to `Base` when a building hit joins it (and re-showing the text if it was off screen) would fix it.
2. **SfxTest moves a tracked file.** `FallbackWhenMissing` renames `game/assets/audio/sfx/ui/alert_1.wav` and its
   `.import` to `*.aside` in the working tree and back in `finally`. A run killed in between (scene-loop's timeout kill,
   a closed console) leaves the clip "deleted" in `git status` and the next runs falling back. Copying the folder to
   `user://` or a check-and-restore at the scene's start would make it safe.
3. **`AttackAlerts.New(k)` past the ring.** With more new alerts in one `Update` than `capacity`, `New` holds the first
   `capacity` slot indices; by the end of the frame those slots hold the newest `capacity` alerts in rotated order, so
   "the k-th alert raised by the last Update" is not what `New(k)` returns (the set is right: QA's
   `MoreAlertsInOneFrameThanTheRingHolds_...` pins distinct positions and the newest last). Default capacity is 32, so only
   a comment.

## Notes
None blocks; item 1 is the one a player would notice.

## Re-verify (2026-10-17-2344, fix round 1, QA)
- Item 1 (Kind fixed by the first hit): **still open**, not addressed in round 1.
- Item 2 (SfxTest moves a tracked file): **fixed**. `FallbackWhenMissing` builds its pointer under
  `user://sfx_nolfs_probe` and removes it; after the full loop and three extra `QaM6V12Test` runs `git status` shows no
  change under `game/assets/`.
- Item 3 (`New(k)` order past the ring): **documented** in `AttackAlerts.New`'s remarks (BUG-0954, capacity 32 makes it
  rare); QA's ring test still pins the set and the newest last. Acceptable as a comment.
- Item 4 (new, round 1): docs/03 says the 10 s suppression is "200 ticks"; with `SetNow(tick x 0.05f)` the float
  difference of two tick times sometimes rounds just under 10 s, so a raid hit every tick re-alerts after 201 ticks
  (2 of 19 gaps from tick 0 in QA's `TickClock_HitEveryTick_ReAlertsEvery200Ticks_Or201FromFloatRounding`). Harmless
  and deterministic; an integer tick clock (`RaisedAtTick`, compare `tick - raised < 200`) or the docs saying "200-201"
  would make the claim exact.
