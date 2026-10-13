# BUG-0650: The AI's site resume path never gives up on a site no worker can reach

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-13-0527, task M5-H1 |
| System | AI economy (`EconomyManager.Sites`, BUG-0640's resume path) |
| Fixed by | |

## Repro
1. Un-skip `QA/SimHardeningM5H1QaTests.AnUnreachableOwnSite_ResumeChurnIsBounded_TheEconomyKeepsGoing` and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AnUnreachableOwnSite" --logger "console;verbosity=detailed"`.
   Scene: the flat AI scene (Malazan, Normal, shipped order), an own Billet site spawned at cell (20, 14) straight into the
   store and ringed two cells deep by trees; the twin has the same ring and no site. 6,000 ticks.

## Expected
A site the AI can't get a worker to is left alone (or retried with a back-off). Its economy should not pay much for it.

## Actual
At 1dfdef3: **99 joins in 300 thinks**, so one every `StalledThinks` (3) thinks for the whole run. Each one pulls a gatherer
off, it walks to the ring and stops, and the idle pass sends it back to gather (176 Gathers, the twin sends 59). Gold + wood
gathered: **1,160 against the twin's 1,730 (67%)**. Pop is 22 against 29.

## Notes
- Not reachable through normal play as far as QA can tell: the never-seal rule keeps a placed site reachable, so the site
  has to be spawned past the placement rules. S4 for that reason. It would matter if a site could ever be cut off later, for
  example by a hand-made map, or by a future wall or gate mechanic.
- Related measurement (not a bug): an own Billet site in reach of an enemy Lookout Tower, with combat on, got 5 joins and
  cost 4 workers over 6,000 ticks, and the site was finished
  (`AnOwnSiteUnderAnEnemyTower_WorkersSentToIt_Measured`). The resume path keeps sending workers into fire, one per 3
  thinks. That may be acceptable; it is a design call.
- `EconomyManager.StalledThinks` = 3 is a C# constant. The brief's constraint is "no stat in C# (cooldowns from ai.json)".
  docs/03 calls it a code guard, the same as `MilitaryManager.MaxTries` (Producer ruling, docs/01 2026-10-12 (c)). It is
  listed here so the Producer can confirm that ruling covers it too.
- Possible fix: give up on a site after N joins that never got a worker into reach. A per-site join count on the Blackboard,
  next to `SiteStill`, would do it.

## Producer triage (2026-10-13-0527 ACCEPT)
S4 confirmed (not reachable through play: the never-seal rule keeps a placed site reachable). **Ruling on `EconomyManager.StalledThinks` (3):** a code guard like `MilitaryManager.MaxTries` (docs/01 2026-10-12 (c)): it debounces a Build that just applied and tunes no balance number; it stays in C#. Direction for the next sim hardening: a per-site join count on the Blackboard beside `SiteStill`; give the site up (or back off) after N joins that never got a worker into reach; un-skip the QA row. The tower measurement (5 joins, 4 workers lost, site finished) is left as is until M5-4b's defense logic exists.
