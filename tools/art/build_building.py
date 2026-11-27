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
    me = obj.data
    source_triangles = triangles(obj)

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
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.triangulate(bm, faces=bm.faces)
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
    return {"id": b["id"], "destination": b["destination"], "triangles": tris, "sourceTriangles": source_triangles,
            "afterHiddenFaces": after_hidden, "decimated": decimated, "perMaterial": per_material,
            "teamFaces": team_face_count,
            "size": [round(float(hi[0] - lo[0]), 4), round(float(hi[1] - lo[1]), 4), round(float(hi[2] - lo[2]), 4)],
            "feet": round(float(lo[2]), 4), "materials": sorted(m.name for m in me.materials),
            "textures": {atlas.name: list(atlas.size)}, "atlas": layout}


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
