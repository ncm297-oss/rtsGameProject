# Builds one static building model from the Ultimate Fantasy RTS pack (docs/04 "Building models, as built", M6-V26).
# Run by tools/import-assets.ps1 inside Blender (background mode); never edit the outputs by hand, re-run instead.
#
#   blender -b --factory-startup -P tools/art/build_building.py -- --manifest tools/asset-manifest.json
#       --source-root <asset-sources dir> --repo <repo root> --work <temp dir> --building <id> [--preview <png>]
#
# A building: the source glTF's one mesh, its vertices merged, the faces no camera above the ground can see dropped
# (hidden_faces: a building is only ever seen from above), the manifest's materials decimated to their triangle counts,
# moved so its origin is the centre of its ground plan at its lowest point, turned by the yaw fix, flat shaded; every
# source material becomes one flat-colour tile of one atlas (the manifest's palette colour, or the team grey), the mesh's
# one material is "team" and the atlas alpha marks the team texels, exactly as a unit model's since M6-V24, so
# team_colour.gdshader draws it with no change. The atlas layout rides in the file as glTF extras on the mesh node.
# Prints one "RESULT {json}" line the PowerShell script reads; exits 1 with a "FAIL <why>" line on any problem.
#
# M6-V31: an entry may also (or instead of a `source`) carry a `kit`: a list of simple parts (boxes, ridge prisms,
# pyramids, cones, cylinders, poles, flat awnings) the builder makes here, each with a size, a position and a role. A role
# is a material: one of the entry's `team` (cloth, drawn in the player colour) or `colours` (frames, poles, props). The
# parts then go through the same hidden-face pass, atlas and export as a pack source's faces. A `keep` box crops a source
# to the faces inside it (one market stall of the pack's four). An entry with neither runs no new line.
import bmesh
import bpy
import json
import math
import os
import sys

import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

# The unit builder's atlas alpha codes (build_unit.py ALPHA_*; team_colour.gdshader reads them back). build_unit.py runs
# its main() when imported, so the few helpers shared with it are repeated here.
ALPHA_PLAIN, ALPHA_TEAM = 1.0, 0.5
# M6-V30: the material the `teamFaces` rules move their faces to; a team part, so it lands on the team tile.
TEAM_FACES = "team_faces"


def fail(why):
    print(f"FAIL {why}")
    sys.stdout.flush()
    os._exit(1)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_gltf(path):
    if not os.path.isfile(path):
        fail(f"source not found: {path}")
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def triangles(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def hex_rgb(text):
    text = text.lstrip("#")
    return [int(text[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]


def palette_blocks(count):
    """The side of a square grid of `count` tiles: a power of two, so each tile's edges fall on the atlas's mip
    boundaries (build_unit.py's rule for the props' colours)."""
    side = 1
    while side * side < count:
        side *= 2
    return side


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = {"preview": None}
    for i in range(0, len(argv) - 1, 2):
        out[argv[i][2:].replace("-", "_")] = argv[i + 1]
    for need in ("manifest", "source_root", "repo", "work", "building"):
        if need not in out:
            fail(f"missing --{need.replace('_', '-')}")
    return out


def view_directions(min_elevation):
    """Unit vectors toward the sky a camera can look from: every 22.5 degrees of azimuth at elevations from
    `min_elevation` to 75 degrees in 15-degree steps, and straight down. Fixed, so every run drops the same faces."""
    dirs = []
    elev = float(min_elevation)
    while elev < 89.0:
        e = math.radians(elev)
        for k in range(16):
            a = math.radians(k * 22.5)
            dirs.append(Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e))))
        elev += 15.0
    dirs.append(Vector((0.0, 0.0, 1.0)))
    return dirs


def hidden_faces(bm, min_elevation):
    """The faces of `bm` no ray toward the sky reaches: from the face's centre and from three points near its corners,
    nudged off the face, every direction of view_directions on the face's front side is blocked by the model itself.
    The game's camera looks down at 35-55 degrees from 20-60 m and buildings never turn, so those faces (undersides,
    the backs of blocks set against a wall, faces inside other parts) are never drawn on screen."""
    bvh = BVHTree.FromBMesh(bm)
    dirs = view_directions(min_elevation)
    lo = Vector((min(v.co[i] for v in bm.verts) for i in range(3)))
    hi = Vector((max(v.co[i] for v in bm.verts) for i in range(3)))
    size = (hi - lo).length or 1.0
    nudge = 1e-4 * size
    dead = []
    for f in bm.faces:
        n = f.normal
        centre = f.calc_center_median()
        points = [centre] + [centre + (v.co - centre) * 0.85 for v in f.verts]
        seen = False
        for p in points:
            for d in dirs:
                if n.dot(d) <= 0.0:
                    continue
                hit, _, _, _ = bvh.ray_cast(p + n * nudge + d * nudge, d)
                if hit is None:
                    seen = True
                    break
            if seen:
                break
        if not seen:
            dead.append(f)
    return dead


def mark_team_faces(b, bm, me, names):
    """M6-V30: the faces each `teamFaces` rule picks move to one new material, TEAM_FACES, drawn on the team tile. A rule
    picks the faces of its `materials` that face the sky (the normal's vertical part at least `minUp`) and stand high
    (the face's centre at least `above` of the model's height over its lowest point): a timber roof, when the pack drew the
    roof in the same material as the walls (the First Age buildings, whose only own team material is a few banner
    triangles). Returns the faces moved. A building without `teamFaces` never comes here, so its file is unchanged."""
    slot = len(me.materials)
    me.materials.append(bpy.data.materials.new(TEAM_FACES))
    z0 = min(v.co.z for v in bm.verts)
    z1 = max(v.co.z for v in bm.verts)
    moved = 0
    for rule in b["teamFaces"]:
        wanted = {i for i, n in enumerate(names) if n in rule["materials"]}
        up, floor = float(rule["minUp"]), z0 + float(rule["above"]) * (z1 - z0)
        for f in bm.faces:
            if f.material_index in wanted and f.normal.z >= up and f.calc_center_median().z >= floor:
                f.material_index = slot
                moved += 1
    return moved


# ---- M6-V31: the kit ----

KIT_SHAPES = ("box", "ridge", "pyramid", "cone", "cylinder", "pole", "awning")
# The gap the view keeps between a fitted model and each footprint edge (game/scripts/BuildingModels.cs FitMargin). A kit
# with no source is drawn in meters at that fit, so its parts must stay inside it.
FIT_MARGIN = 0.25
CELL_SIZE = 2.0  # meters a map cell (Rts.Sim MapConstants.CellSize)
# The thickest an awning may be: it is a flat sheet; a thicker one is a box.
AWNING_MAX = 0.3


def godot_to_blender(v):
    """A point in Godot's axes (x east, y up, z toward the camera: a building's front) in Blender's (x, -z, y): the glTF
    exporter turns Blender's z-up back into Godot's y-up. The map is a proper rotation, so a face's winding is kept."""
    return Vector((v[0], -v[2], v[1]))


def blender_to_godot(v):
    return Vector((v[0], v[2], -v[1]))


def ring(n, rx, rz, y):
    """`n` points round an ellipse of radii rx (x) and rz (z) at height y, the first on +x, counter-clockwise from above."""
    return [Vector((rx * math.cos(2.0 * math.pi * k / n), y, -rz * math.sin(2.0 * math.pi * k / n))) for k in range(n)]


def shape_geometry(part):
    """A part's corners and faces in its own frame (Godot's axes, meters or source units): every shape fills the box
    x in [-w/2, w/2], y in [0, h], z in [-d/2, d/2] of its `size` [w, h, d], its base centre at the origin. Faces are lists
    of corner indices; their winding is set outward by the caller (every shape is convex)."""
    w, h, d = (float(s) for s in part["size"])
    x, z = w / 2.0, d / 2.0
    shape = part["shape"]
    if shape in ("box", "pole", "awning"):
        v = [Vector((sx * x, sy * h, sz * z)) for sy in (0, 1) for sz in (-1, 1) for sx in (-1, 1)]
        f = [(0, 1, 3, 2), (4, 5, 7, 6), (0, 1, 5, 4), (2, 3, 7, 6), (0, 2, 6, 4), (1, 3, 7, 5)]
        return v, f
    if shape == "ridge":
        # A tent's prism: the ridge line at the top runs along `axis` ("z": front to back, the gable faces the camera; "x").
        base = [Vector((-x, 0, -z)), Vector((x, 0, -z)), Vector((x, 0, z)), Vector((-x, 0, z))]
        if part.get("axis", "z") == "z":
            v = base + [Vector((0, h, -z)), Vector((0, h, z))]
            f = [(0, 1, 2, 3), (0, 3, 5, 4), (1, 2, 5, 4), (0, 1, 4), (3, 2, 5)]
        else:
            v = base + [Vector((-x, h, 0)), Vector((x, h, 0))]
            f = [(0, 1, 2, 3), (0, 1, 5, 4), (3, 2, 5, 4), (0, 3, 4), (1, 2, 5)]
        return v, f
    if shape == "pyramid":
        v = [Vector((-x, 0, -z)), Vector((x, 0, -z)), Vector((x, 0, z)), Vector((-x, 0, z)), Vector((0, h, 0))]
        return v, [(0, 1, 2, 3), (0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)]
    n = int(part.get("sides", 8))
    if n < 3:
        fail(f"a {shape} part needs at least 3 sides, has {n}")
    if shape == "cone":
        # `top` > 0: a cut cone (a frustum) whose top ring is that share of the base's.
        top = float(part.get("top", 0.0))
        v = ring(n, x, z, 0.0)
        f = [tuple(range(n))]
        if top <= 0.0:
            v.append(Vector((0, h, 0)))
            f += [(k, (k + 1) % n, n) for k in range(n)]
        else:
            v += ring(n, x * top, z * top, h)
            f += [tuple(range(n, 2 * n))] + [(k, (k + 1) % n, n + (k + 1) % n, n + k) for k in range(n)]
        return v, f
    # cylinder: its axis "y" (standing), "x" or "z" (lying, a log); the round section fills the other two sizes.
    axis = part.get("axis", "y")
    if axis == "y":
        v = ring(n, x, z, 0.0) + ring(n, x, z, h)
    else:
        v = []
        for end in (-1, 1):
            for k in range(n):
                a, b = math.cos(2.0 * math.pi * k / n), math.sin(2.0 * math.pi * k / n)
                v.append(Vector((end * x, h / 2.0 + a * h / 2.0, b * z)) if axis == "x" else Vector((a * x, h / 2.0 + b * h / 2.0, end * z)))
    f = [tuple(range(n)), tuple(range(n, 2 * n))] + [(k, (k + 1) % n, n + (k + 1) % n, n + k) for k in range(n)]
    return v, f


def kit_part_matrix(part):
    """The part's frame to its parent's (Godot's axes): turned by `rotate` [x, y, z] degrees (x first) about its base
    centre, then moved to `at`."""
    rx, ry, rz = (math.radians(float(a)) for a in part.get("rotate", [0, 0, 0]))
    rot = Matrix.Rotation(rz, 4, 'Z') @ Matrix.Rotation(ry, 4, 'Y') @ Matrix.Rotation(rx, 4, 'X')
    return Matrix.Translation(Vector([float(a) for a in part["at"]])) @ rot


def check_kit(b, footprint):
    """Fails with a clear line on a bad kit: no parts, an unknown shape, a size not three positive numbers, an awning
    thicker than AWNING_MAX, a role with no colour, a part below the ground or (a kit with no source, drawn in meters)
    outside its footprint less FIT_MARGIN."""
    parts = b["kit"]
    if not isinstance(parts, list) or not parts:
        fail(f"{b['id']}: kit has no parts")
    roles = set(b.get("colours", {})) | set(b.get("team", []))
    for i, p in enumerate(parts):
        label = f"{b['id']}: kit part {i} ({p.get('shape', '?')}, role {p.get('role', '?')})"
        if p.get("shape") not in KIT_SHAPES:
            fail(f"{label}: shape not one of {', '.join(KIT_SHAPES)}")
        size, at = p.get("size"), p.get("at")
        if not isinstance(size, list) or len(size) != 3 or any(not isinstance(s, (int, float)) or s <= 0 for s in size):
            fail(f"{label}: size {size} is not three sizes over 0")
        if not isinstance(at, list) or len(at) != 3 or any(not isinstance(a, (int, float)) for a in at):
            fail(f"{label}: at {at} is not three numbers")
        if p["shape"] == "awning" and size[1] > AWNING_MAX:
            fail(f"{label}: an awning {size[1]} thick, over {AWNING_MAX}: a box, not an awning")
        if p.get("role") not in roles:
            fail(f"{label}: role has no colour and is not a team part")
        m = kit_part_matrix(p)
        corners = [m @ c for c in shape_geometry(p)[0]]
        if min(c.y for c in corners) < -1e-6:
            fail(f"{label}: reaches {min(c.y for c in corners):.3f} under the ground")
        if footprint is not None:
            hx, hz = footprint[0] / 2.0 - FIT_MARGIN, footprint[1] / 2.0 - FIT_MARGIN
            worst = max(max(abs(c.x) - hx, abs(c.z) - hz) for c in corners)
            if worst > 1e-6:
                fail(f"{label}: {worst:.3f} m outside its {footprint[0]:g} x {footprint[1]:g} m footprint less {FIT_MARGIN} m")


def footprint_of(b, repo):
    """The entry's building footprint in meters, from the data (game/data/factions/<faction>/buildings.json; the faction is
    the destination's folder). A kit with no source is checked against it."""
    faction = b["destination"].replace("\\", "/").split("/")[-3]
    path = os.path.join(repo, "game", "data", "factions", faction, "buildings.json")
    if not os.path.isfile(path):
        fail(f"{b['id']}: no {path} to read its footprint from")
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    row = next((r for r in data.get("buildings", []) if r.get("id") == b["id"]), None)
    if row is None:
        fail(f"{b['id']}: not a building of {path}")
    return (row["footprint"]["width"] * CELL_SIZE, row["footprint"]["height"] * CELL_SIZE)


def material_slot(me, name):
    """The index of material `name` on mesh `me`, added when it has none (a kit role the source lacks)."""
    for i, m in enumerate(me.materials):
        if m.name.split(".")[0] == name:
            return i
    me.materials.append(bpy.data.materials.new(name))
    return len(me.materials) - 1


def add_kit(b, bm, me, origin):
    """Adds the kit's parts to `bm` (Blender's axes), each part's faces on its role's material, `origin` (Blender's axes)
    the point the kit's positions count from. Faces wound outward (every shape is convex), then triangulated. Returns the
    triangles added."""
    slots = {p["role"]: material_slot(me, p["role"]) for p in b["kit"]}
    added = []
    for p in b["kit"]:
        m = kit_part_matrix(p)
        local, faces = shape_geometry(p)
        size = [float(s) for s in p["size"]]
        centre = godot_to_blender(m @ Vector((0.0, size[1] / 2.0, 0.0))) + origin
        verts = [bm.verts.new(godot_to_blender(m @ c) + origin) for c in local]
        for idx in faces:
            pts = [verts[i].co for i in idx]
            normal = (pts[1] - pts[0]).cross(pts[2] - pts[0])
            mid = sum(pts, Vector()) / len(pts)
            if normal.dot(mid - centre) < 0:
                idx = tuple(reversed(idx))
            face = bm.faces.new([verts[i] for i in idx])
            face.material_index = slots[p["role"]]
            added.append(face)
    result = bmesh.ops.triangulate(bm, faces=added)
    return len(result["faces"])


def crop(b, bm):
    """`keep`: only the source's faces whose centre lies inside the box (`min` / `max`, Godot's axes, source units) stay."""
    lo, hi = (Vector([float(a) for a in b["keep"][k]]) for k in ("min", "max"))
    out = []
    for f in bm.faces:
        c = blender_to_godot(f.calc_center_median())
        if not all(lo[i] <= c[i] <= hi[i] for i in range(3)):
            out.append(f)
    if len(out) == len(bm.faces):
        fail(f"{b['id']}: keep box {list(lo)} - {list(hi)} holds no face of {b['source']}")
    bmesh.ops.delete(bm, geom=out, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')


def decimate_material(obj, material_name, target):
    """Decimates (collapse) the faces of one source material to `target` triangles: separated, decimated, joined back."""
    me = obj.data
    index = next((i for i, m in enumerate(me.materials) if m and m.name.split(".")[0] == material_name), None)
    if index is None:
        fail(f"decimate: no material {material_name}")
    bpy.context.view_layer.objects.active = obj
    for o in bpy.context.scene.objects:
        o.select_set(o is obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='DESELECT')
    obj.active_material_index = index
    bpy.ops.object.material_slot_select()
    bpy.ops.mesh.separate(type='SELECTED')
    bpy.ops.object.mode_set(mode='OBJECT')
    part = next(o for o in bpy.context.selected_objects if o is not obj)
    have = triangles(part)
    if have > target:
        mod = part.modifiers.new("decimate", 'DECIMATE')
        mod.ratio = target / have
        mod.use_collapse_triangulate = True
        with bpy.context.temp_override(object=part, active_object=part):
            bpy.ops.object.modifier_apply(modifier=mod.name)
    with bpy.context.temp_override(active_object=obj, selected_editable_objects=[obj, part], object=obj):
        bpy.ops.object.join()
    return have, triangles(obj)


def build_building(manifest, b, src_root, repo, work):
    reset()
    kit = b.get("kit")
    if not b.get("source") and kit is None:
        fail(f"{b['id']}: neither a source nor a kit")
    if b.get("keep") and not b.get("source"):
        fail(f"{b['id']}: a keep box crops a source, and there is none")
    if kit is not None:
        # M6-V31: a kit with no source is drawn in meters, so it is checked against the building's footprint.
        check_kit(b, None if b.get("source") else footprint_of(b, repo))
    if b.get("source"):
        objs = import_gltf(os.path.join(src_root, b["source"]))
        meshes = [o for o in objs if o.type == 'MESH']
        if len(meshes) != 1:
            fail(f"{b['id']}: {len(meshes)} meshes in {b['source']}, want one")
        obj = meshes[0]
        remove_others = [o for o in objs if o is not obj]
        for o in remove_others:
            bpy.data.objects.remove(o, do_unlink=True)
        obj.parent = None
        obj.data.transform(obj.matrix_world)
        obj.matrix_world = Matrix.Identity(4)
    else:
        # M6-V31: a kit alone starts from an empty mesh.
        obj = bpy.data.objects.new(b["id"], bpy.data.meshes.new(b["id"]))
        bpy.context.scene.collection.objects.link(obj)
    me = obj.data
    source_triangles = triangles(obj)
    if kit is not None:
        for p in kit:
            material_slot(me, p["role"])

    colours = b["colours"]
    team = set(b["team"])
    names = [m.name.split(".")[0] for m in me.materials]
    for name in names:
        if name not in colours and name not in team:
            fail(f"{b['id']}: source material {name} has no colour and is not a team part")
    for name in list(colours) + list(team) + [n for rule in b.get("teamFaces", []) for n in rule["materials"]]:
        if name not in names:
            fail(f"{b['id']}: manifest names material {name}, not in the source")
    if b.get("teamFaces"):
        team.add(TEAM_FACES)

    # Merge the flat-shaded source's split corners (so a decimation can collapse edges), triangulate, drop what no camera
    # sees.
    bm = bmesh.new()
    bm.from_mesh(me)
    if b.get("keep"):
        crop(b, bm)
    # M6-V31: a mixed entry's kit counts from the (cropped) source's ground-plan centre at its lowest point, in its units.
    origin = Vector((0.0, 0.0, 0.0))
    if kit is not None and bm.verts:
        xs, ys, zs = ([v.co[i] for v in bm.verts] for i in range(3))
        origin = Vector(((min(xs) + max(xs)) / 2.0, (min(ys) + max(ys)) / 2.0, min(zs)))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    kit_triangles = add_kit(b, bm, me, origin) if kit is not None else 0
    if not b.get("source"):
        source_triangles = kit_triangles
    bm.normal_update()
    dead = hidden_faces(bm, b.get("viewElevationDegrees", 15))
    bmesh.ops.delete(bm, geom=dead, context='FACES_ONLY')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    team_face_count = mark_team_faces(b, bm, me, names) if b.get("teamFaces") else 0
    bm.to_mesh(me)
    bm.free()
    after_hidden = triangles(obj)

    decimated = {}
    for name, target in sorted(b.get("decimate", {}).items()):
        before, _ = decimate_material(obj, name, int(target))
        decimated[name] = [before, None]
    me = obj.data
    # The count per material after every step.
    per_material = {}
    me.calc_loop_triangles()
    for t in me.loop_triangles:
        name = me.materials[me.polygons[t.polygon_index].material_index].name.split(".")[0]
        per_material[name] = per_material.get(name, 0) + 1
    for name in decimated:
        decimated[name][1] = per_material.get(name, 0)

    # Origin at the centre of the ground plan, at its lowest point; then the yaw fix (about the vertical axis).
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lo, hi = co.min(axis=0), co.max(axis=0)
    shift = Vector((-(lo[0] + hi[0]) / 2.0, -(lo[1] + hi[1]) / 2.0, -lo[2]))
    yaw = math.radians(float(b.get("yawDegrees", 0)))
    me.transform(Matrix.Rotation(yaw, 4, 'Z') @ Matrix.Translation(shift))

    # Flat shading: the pack's low-poly look, and no custom normals left from the source to disagree with the decimation.
    if me.has_custom_normals:
        with bpy.context.temp_override(object=obj, active_object=obj):
            bpy.ops.mesh.customdata_custom_splitnormals_clear()
    me.polygons.foreach_set("use_smooth", np.zeros(len(me.polygons), dtype=bool))

    atlas, layout = build_atlas(b, obj, team, work)
    for img in list(bpy.data.images):
        if img is not atlas:
            bpy.data.images.remove(img)

    for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.materials, bpy.data.scenes, bpy.data.images):
        for idb in coll:
            for key in list(idb.keys()):
                del idb[key]
    obj.name = b["id"]
    me.name = b["id"]
    # The atlas layout as glTF extras on the mesh node (UnitModels reads the first node that has extras).
    obj["atlas"] = layout

    tris = triangles(obj)
    if tris > int(b["maxTriangles"]):
        fail(f"{b['id']}: {tris} triangles, over the {b['maxTriangles']} cap (from {source_triangles}; {after_hidden} after the hidden faces)")
    if [m.name for m in me.materials] != ["team"]:
        fail(f"{b['id']}: materials {[m.name for m in me.materials]}, want the one team material")
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lo, hi = co.min(axis=0), co.max(axis=0)
    dest = os.path.join(repo, b["destination"])
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=dest, export_format='GLB', export_animations=False, export_skins=False,
                              export_morph=False, export_yup=True, export_apply=False, export_extras=True,
                              export_tangents=False, export_cameras=False, export_lights=False)
    # Blender's x / y / z are glTF's x / -z / y: width (x) and depth (y here) of the ground plan, the height.
    result = {"id": b["id"], "destination": b["destination"], "triangles": tris, "sourceTriangles": source_triangles,
              "afterHiddenFaces": after_hidden, "decimated": decimated, "perMaterial": per_material,
              "teamFaces": team_face_count,
              "size": [round(float(hi[0] - lo[0]), 4), round(float(hi[1] - lo[1]), 4), round(float(hi[2] - lo[2]), 4)],
              "feet": round(float(lo[2]), 4), "materials": sorted(m.name for m in me.materials),
              "textures": {atlas.name: list(atlas.size)}, "atlas": layout}
    if kit is not None:
        # M6-V31: the kit's parts and the triangles they added before the hidden-face pass.
        result["kitParts"] = len(kit)
        result["kitTriangles"] = kit_triangles
    return result


def build_atlas(b, obj, team, work):
    """One flat-colour tile per source material (the team parts share one, "team"), on a square grid of power-of-two
    side (build_unit.palette_blocks), `tileSize` texels each; every face's three UVs sit on its tile's centre, so the
    face reads one colour at every mip level and team_colour.gdshader's tile clamp keeps the sample there. Team tiles
    hold the team grey (`teamShade`, as a unit's team texture's mean) at alpha 0.5; the others the palette colour at
    alpha 1. Returns the atlas image and the layout written into the file's extras."""
    me = obj.data
    size = int(b["tileSize"])
    names = ["team"] + [n for n in b["colours"]]  # manifest order: the same layout every run
    side = palette_blocks(len(names))
    tiles = {name: (i % side, i // side) for i, name in enumerate(names)}
    width = side * size
    px = np.zeros((width, width, 4), dtype=np.float32)  # Blender's rows run from the bottom
    px[:, :, 3] = ALPHA_PLAIN  # an unused tile too: no texel is transparent
    shade = float(b.get("teamShade", 0.8))
    for name, (c, r) in tiles.items():
        x0, y0 = c * size, (side - 1 - r) * size
        if name == "team":
            px[y0:y0 + size, x0:x0 + size, :3] = shade
            px[y0:y0 + size, x0:x0 + size, 3] = ALPHA_TEAM
        else:
            px[y0:y0 + size, x0:x0 + size, :3] = hex_rgb(b["colours"][name])

    def centre(name):
        c, r = tiles[name]
        return ((c + 0.5) / side, (side - 1 - r + 0.5) / side)  # Blender UV: v up

    tile_of_material = []
    for m in me.materials:
        name = m.name.split(".")[0]
        tile_of_material.append("team" if name in team else name)
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    while len(me.uv_layers) > 1:
        me.uv_layers.remove(me.uv_layers[1])
    me.uv_layers[0].name = "UVMap"
    n_polys = len(me.polygons)
    mat_index = np.empty(n_polys, dtype=np.int32)
    totals = np.empty(n_polys, dtype=np.int32)
    me.polygons.foreach_get("material_index", mat_index)
    me.polygons.foreach_get("loop_total", totals)
    loop_mat = np.repeat(mat_index, totals)
    uv = np.empty((len(me.loops), 2), dtype=np.float32)
    for k, name in enumerate(tile_of_material):
        uv[loop_mat == k] = centre(name)
    me.uv_layers[0].data.foreach_set("uv", uv.ravel())

    old = list(me.materials)
    me.materials.clear()
    for mat in old:
        bpy.data.materials.remove(mat)
    atlas = bpy.data.images.new("atlas", width, width, alpha=True)
    atlas.pixels.foreach_set(px.ravel())
    # Saved as atlas.png, so Godot extracts it beside the model as <id>_atlas.png (the units' name).
    work = os.path.join(work, b["id"])
    os.makedirs(work, exist_ok=True)
    atlas.filepath_raw = os.path.join(work, "atlas.png")
    atlas.file_format = 'PNG'
    atlas.save()
    mat = bpy.data.materials.new("team")
    if mat.node_tree is None:
        mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.9
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = atlas
    tex.interpolation = 'Closest'
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    me.materials.append(mat)
    me.polygons.foreach_set("material_index", np.zeros(n_polys, dtype=np.int32))
    layout = {"grid": [side, side], "tile": size, "tiles": {name: list(tiles[name]) for name in sorted(tiles)}}
    return atlas, layout


def render_preview(path, team_rgb=(0.75, 0.15, 0.12)):
    """Workbench renders of what was just built from the match camera's pitch (55 degrees) on four sides, the team
    texels drawn in a red (the developer's look, not an output)."""
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.show_shadows = True
    scene.render.resolution_x, scene.render.resolution_y = 640, 640
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    obj = next(o for o in scene.objects if o.type == 'MESH')
    co = [obj.matrix_world @ v.co for v in obj.data.vertices]
    span = max(max(c[i] for c in co) - min(c[i] for c in co) for i in range(3))
    cam.data.ortho_scale = span * 1.5
    scene.camera = cam
    centre = Vector((0.0, 0.0, max(c[2] for c in co) / 2.0))
    pitch = math.radians(55.0)
    for label, az in (("south", -90.0), ("east", 0.0), ("north", 90.0), ("west", 180.0)):
        a = math.radians(az)
        d = Vector((math.cos(pitch) * math.cos(a), math.cos(pitch) * math.sin(a), math.sin(pitch)))
        cam.location = centre + d * span * 4.0
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = path.replace(".png", f"-{label}.png")
        bpy.ops.render.render(write_still=True)


def main():
    args = parse_args()
    with open(args["manifest"], encoding="utf-8") as f:
        manifest = json.load(f)
    os.makedirs(args["work"], exist_ok=True)
    b = next((m for m in manifest.get("buildings", []) if m["id"] == args["building"]), None)
    if b is None:
        fail(f"no building {args['building']} in the manifest")
    result = build_building(manifest, b, args["source_root"], args["repo"], args["work"])
    if args.get("preview"):
        render_preview(args["preview"])
    print("RESULT " + json.dumps(result, sort_keys=True))
    sys.stdout.flush()


try:
    main()
except SystemExit:
    raise
except Exception as ex:  # a Blender API error is a FAIL line, not a traceback the script can't read
    import traceback
    traceback.print_exc()
    fail(f"{type(ex).__name__}: {ex}")
