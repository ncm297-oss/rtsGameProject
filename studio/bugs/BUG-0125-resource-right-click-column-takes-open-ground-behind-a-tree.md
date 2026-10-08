# BUG-0125: The resource right-click pick treats a tree as a full 2 x 2 x 3.5 m column, so a click on open ground just north of a tree or mine targets the node

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-07-1715, task M3-V3b (QA) |
| System | right-click context target (view): `SelectionController.ContextTarget`, `Rts.Sim.ViewApi.ResourcePicker.PickRay` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~PickRayQaTests" --logger "console;verbosity=detailed"`
2. Read the lines printed by `ResourcePickRay_TreeColumnVersusDrawnTree_Measure` and
   `RightClickNodeIntent_ColumnPickVersusGroundPick_Measure` (seed 1, RTS-camera rays: 55 degree pitch, zoom 20-60 m,
   aimed near nodes).

## Expected
Brief M3-V3b (f), docs/03 "Right-click context": "a right click on a tree's canopy, whose ground point is behind the
tree, gathers that tree". A pixel that shows a node's drawn prop should mean that node. A pixel that shows open ground
should still mean the ground (a Move), as it did before.

## Actual
`ResourcePicker.PickRay` treats every node as a box over its whole footprint, from the ground up to the prop's height:
2 x 2 x 3.5 m for a tree. The tree `PropsView` draws is much smaller: a 0.15 m trunk up to 1 m, then a cone of radius
0.8 m that narrows to a point at 3.5 m. The cone is about 12 % of the column's volume. At the camera's 55 degree pitch,
the empty top of the column covers open ground up to about 2.5 m north of the tree's cell. A mine is a box up to
1.6 m with a half-size gold block on top, so the top 0.5 m of its column is mostly empty too.

```
tree picks 2180, of which the ray misses the drawn tree 1691 (77.6 %)
3000 rays, 1613 with a node involved: column pick wrong 1099 (68.1 %, of which the pixel shows bare ground 699),
ground pick wrong 351 (21.8 %); open ground taken for a node: column 339, ground pick 0;
a drawn node taken for open ground: column 0, ground pick 106
```

The M3-V3b pick removes the old error, where a pixel on a canopy over open ground meant a Move (106 cases, now 0).
It adds a bigger new one: 339 pixels that show open ground now target a node. With workers selected, that is a
Gather on the tree instead of a Move. With soldiers, it is a Move to the tree's centre (a blocked cell) instead of
the clicked point. With a building selected, it sets a rally on the tree. In a forest, it also often names the tree in
front when the visible canopy belongs to the tree behind (the rest of the 68 %). That matters less, because both are
trees.

The dev's own `QaV1Test.FelledNodes` hit this case. A right click on a felled tree's old cell now targets the tree
south of it, because that tree's column covers the cell. The dev changed the row to pick a node with open ground in
front instead.

## Notes
- The building pick has no such gap: the drawn box is exactly the picked box (`PickRayQaTests.BuildingPickRay_*`:
  0 wrong of about 10,000 rays over 4 seeds, about 230 of them hidden by terrain).
- Fix ideas: pick the drawn shape instead of the column. For a tree, test the ray against the cone
  (`r <= 0.8 * (3.5 - h) / 2.5` above 1 m) and the trunk, or at least against a box of the canopy's radius, not the
  whole cell. For a mine, test against the 1.6 m block plus the half-size gold block. The prop dimensions are view
  constants (`PropsView.CanopyFill`, `TrunkHeight`, `MineHeight`, `GoldFill`) the caller can pass in, as it already
  passes the heights. When fixed, turn the measurement rows into checks: "open ground taken for a node" = 0 and
  "column pick wrong" well under the ground pick's 21.8 %.
