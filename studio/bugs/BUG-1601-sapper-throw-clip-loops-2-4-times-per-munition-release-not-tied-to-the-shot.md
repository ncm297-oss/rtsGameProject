# BUG-1601: The Sapper's throw loops 2.4 times per munition and its release is not tied to the shot leaving

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-17-0816, task M6-V25 (QA) |
| System | unit views: attack clip timing (`UnitViews.PlayClip`, M6-V20 rule) |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V25Test.tscn`, row (C): a Sapper attacks an enemy Heavy Infantry
   told to hold 7 m away. The match's own `SimRunner` runs at a fixed 1/60 s frame.
2. Output:
   ```
   (C) malazan_sapper attack clip 'attack_throw' 1.33 s; data windup 8 ticks, cooldown 60 ticks; 839 frames attacking, 0 not on 'attack', 12 clip loops, 5 shots
   (C) shot frames [25 205 385 565 745], gaps [180 180 180 180] frames; the throw's phase when each shot first exists [0.69 0.39 0.06 0.75 0.43] (spread 0.06-0.75); the release (throwing hand fastest) at phase 0.28; 2.40 throws per shot
   ```

## Expected
The brief's QA focus for M6-V25 is "the Sapper's throw against the moment the shot leaves". A unit that throws one munition
every 3 s should look like it throws once, with the munition leaving its hand near the release.

## Actual
While Attacking, the attack clip loops from the crowd phase (`slot * CrowdPhase % length`; the M6-V20 rule for every attack
clip). With the throw at 1.33 s and the Sapper's cooldown at 3.0 s, the figure throws about 2.4 times per munition. The shot
appears at an arbitrary point of the throw: phases 0.06 to 0.75 over 5 shots, against a release near 0.28. None of the five shots is within 0.1 of the release (the nearest is 0.11 away, the farthest 0.47); the phase is set by the
slot and the attack's start, not by the windup.

## Notes
- Not new code: the same rule drives the Crossbowman, the Desert Archer, the Javelin Thrower and the casters' attack (the
  Mage's and the Priest's 2.2 s bolt against a 0.5 s `attack_cast`: about 4.4 gestures per bolt). The Sapper shows it worst,
  because its munition is slow, rare and the type's whole identity, and because the brief asked for it.
- The cast side is fine: the Cusser's throw follows the cast (M6-V24's rule). Rows (A) and (B) of `QaM6V25Test` pass at 0.5x,
  1x and 2x and when paused.
- A fix is a view rule (the attack clip started from the windup, as the cast clip is since BUG-1530) or a Producer decision to
  accept looping attacks for now. Either way it is a separate task, not M6-V25's.
- The release phase is measured as the frame where the right hand moves fastest on the Sapper's skeleton (120 samples): a
  proxy, good to about +-0.05.
