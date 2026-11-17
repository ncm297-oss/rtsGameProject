# Builds one unit model, or the shared animation library, from the raw Quaternius packs (docs/04 "Import process").
# Run by tools/import-assets.ps1 inside Blender (background mode); never edit the outputs by hand, re-run instead.
#
#   blender -b --factory-startup -P tools/art/build_unit.py -- --manifest tools/asset-manifest.json
#       --source-root <asset-sources dir> --repo <repo root> --work <temp dir> (--model <id> | --library) [--preview <png>]
#
# A model: the outfit parts and the base character's head on one 65-bone armature, each part decimated to its triangle
# share, base colour textures only, cut to the manifest's size and tinted toward the faction palette, the manifest's props
# built from boxes and bound to their bone, joined into one mesh; M6-V24: every role texture (the team part's as its
# brightness) and the props' flat colours baked into one atlas, so the mesh has one surface and one material, "team",
# whose atlas alpha marks the team texels (build_atlas); the yaw fix on the armature node (glTF faces +Z, docs/04 wants
# -Z), the model's clip map and the atlas layout as glTF extras on the armature node, exported as one .glb with no animations.
# The library: the manifest's library.clips, renamed to their library names, on one armature with a
# one-triangle skin (so Godot imports the tracks as Skeleton3D bone tracks), the bones the manifest's "dropBonePrefixes"
# name (the 40 finger bones, BUG-1321) deleted with their curves so no clip carries their tracks, exported as one .glb.
# Prints one "RESULT {json}" line the PowerShell script reads; exits 1 with a "FAIL <why>" line on any problem.
import bmesh
import bpy
import json
import math
import os
import sys

import numpy as np
from mathutils import Euler, Matrix, Vector

FPS = 30  # the Universal Animation Library's rate: clips keep their keys when imported and exported at it


def fail(why):
    print(f"FAIL {why}")
    sys.stdout.flush()
    os._exit(1)


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = {"model": None, "library": False, "preview": None}
    i = 0
    while i < len(argv):
        key = argv[i][2:]
        if key == "library":
            out["library"] = True
            i += 1
        else:
            out[key] = argv[i + 1]
            i += 2
    for need in ("manifest", "source_root", "repo", "work"):
        if need.replace("_", "-") in out:
            out[need] = out.pop(need.replace("_", "-"))
        if need not in out:
            fail(f"missing --{need.replace('_', '-')}")
    return out


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS


def import_gltf(path):
    if not os.path.isfile(path):
        fail(f"source not found: {path}")
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def remove_objects(objs):
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)


def triangles(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def decimate(obj, target):
    have = triangles(obj)
    if have <= target:
        return
    mod = obj.modifiers.new("decimate", 'DECIMATE')
    mod.ratio = target / have
    mod.use_collapse_triangulate = True
    obj.modifiers.move(len(obj.modifiers) - 1, 0)  # before the armature modifier: decimate the rest pose
    with bpy.context.temp_override(object=obj, active_object=obj):
        bpy.ops.object.modifier_apply(modifier=mod.name)


def keep_bone_region(obj, bones, threshold):
    """Deletes the vertices whose weights to `bones` sum under `threshold` (the base character's head, docs/04 Readme)."""
    groups = {obj.vertex_groups[b].index for b in bones if b in obj.vertex_groups}
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    deform = bm.verts.layers.deform.active
    kill = [v for v in bm.verts if sum(w for g, w in v[deform].items() if g in groups) < threshold]
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(obj.data)
    bm.free()


def align_to_bone(obj, src_arm, dst_arm, bone, model_id):
    """M7-V5: moves a part's vertices by where `bone` sits in the model's armature less where it sits in the part's own
    (the pack's Hair_Long is rigged on an armature whose Head is 5 cm lower than the male base character's), so the part
    sits on the model's bone in the rest pose it is bound in."""
    if bone not in src_arm.data.bones or bone not in dst_arm.data.bones:
        fail(f"{model_id}: alignBone {bone} is not in both armatures")
    src = src_arm.matrix_world @ src_arm.data.bones[bone].head_local
    dst = dst_arm.matrix_world @ dst_arm.data.bones[bone].head_local
    delta = obj.matrix_world.inverted().to_3x3() @ (dst - src)
    for v in obj.data.vertices:
        v.co += delta


def base_colour_source(material):
    """The image and UV map name feeding a glTF-imported material's Base Color (None if it has no texture)."""
    if material is None or material.node_tree is None:
        return None, None
    nodes = material.node_tree.nodes
    bsdf = next((n for n in nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None or not bsdf.inputs["Base Color"].is_linked:
        return None, None
    node = bsdf.inputs["Base Color"].links[0].from_node
    while node.type != 'TEX_IMAGE' and node.inputs and any(i.is_linked for i in node.inputs):
        node = next(i for i in node.inputs if i.is_linked).links[0].from_node
    if node.type != 'TEX_IMAGE':
        return None, None
    uv = None
    vec = node.inputs["Vector"]
    if vec.is_linked and vec.links[0].from_node.type == 'UVMAP':
        uv = vec.links[0].from_node.uv_map
    return node.image, uv


def flat_material(name, image=None, colour=None, roughness=0.9):
    mat = bpy.data.materials.new(name)
    if mat.node_tree is None:
        mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = roughness
    if image is not None:
        tex = nodes.new('ShaderNodeTexImage')
        tex.image = image
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    elif colour is not None:
        bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    return mat


def hex_rgb(text):
    text = text.lstrip("#")
    return [int(text[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]


def srgb_to_linear(rgb):
    """The linear value of sRGB channels (the IEC 61966-2-1 curve), what a glTF base colour factor holds."""
    return [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb]


def prop_rgb(model, text):
    """A prop's flat colour as the material's linear base colour. M7-V5: a model whose "propColourSpace" is "srgb" names
    its prop colours as they show on screen (converted here); without it the hex is written as the linear value itself,
    as M6-V20 / V21 built (those colours show lighter than their hex: the crossbow's #4a3626 draws about #937f6c)."""
    rgb = hex_rgb(text)
    return srgb_to_linear(rgb) if model.get("propColourSpace", "linear") == "srgb" else rgb


LUMA = np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def processed_image(src, name, size, tint, work, team_shade=None):
    """A copy of `src` at `size` x `size`, saved as PNG under `work`: tinted toward `tint` ({colour, amount}), or, for
    the team material, its brightness alone scaled so the mean is `team_shade` (the team shader multiplies it by the
    player colour, so every model's team part is about as bright as the capsule in that colour)."""
    path = bpy.path.abspath(src.filepath) if src.filepath else ""
    img = bpy.data.images.load(path) if path and os.path.isfile(path) else src.copy()
    img.name = name
    if img.size[0] != size or img.size[1] != size:
        img.scale(size, size)
    if tint or team_shade:
        px = np.empty(size * size * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        px = px.reshape(-1, 4)
        lum = px[:, :3] @ LUMA
        if team_shade:
            shade = np.clip(lum * (float(team_shade) / max(float(lum.mean()), 1e-3)), 0.0, 1.0)
            px[:, :3] = shade[:, None]
        else:
            rgb = np.array(hex_rgb(tint["colour"]), dtype=np.float32)
            rgb = rgb / max(float(rgb @ LUMA), 1e-3)
            a = float(tint["amount"])
            # M7-V5: an optional "brightness" scales the result (the tint alone keeps each texel's brightness, so a light
            # hair texture stays light and a dark outfit dark: Teblor dark hair, a bone-white robe).
            b = float(tint.get("brightness", 1.0))
            px[:, :3] = np.clip((px[:, :3] * (1.0 - a) + lum[:, None] * rgb[None, :] * a) * b, 0.0, 1.0)
        img.pixels.foreach_set(px.ravel())
    os.makedirs(work, exist_ok=True)
    img.filepath_raw = os.path.join(work, name + ".png")
    img.file_format = 'PNG'
    img.save()
    return img


def single_uv(obj, uv_for_material):
    """Leaves one UV layer: each polygon gets the UVs its material reads (the glTF part may use two maps)."""
    me = obj.data
    if len(me.uv_layers) == 0:
        return
    first = me.uv_layers[0]
    for poly in me.polygons:
        want = uv_for_material.get(poly.material_index)
        if want and want != first.name and want in me.uv_layers:
            src = me.uv_layers[want].data
            for li in poly.loop_indices:
                first.data[li].uv = src[li].uv
    while len(me.uv_layers) > 1:
        me.uv_layers.remove(me.uv_layers[1])
    first.name = "UVMap"


def box(bm, centre, size, deform, group, rotation=None):
    """Adds a box (12 triangles) to `bm`, every vertex weighted 1 to `group`: axis-aligned, or turned about its own centre
    by `rotation` (Euler XYZ degrees, M6-V21: a curved blade or a bow's limbs are a few turned boxes)."""
    res = bmesh.ops.create_cube(bm, size=1.0)
    turn = Euler([math.radians(float(a)) for a in rotation], 'XYZ').to_matrix() if rotation else Matrix.Identity(3)
    for v in res["verts"]:
        local = turn @ Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        v.co = Vector(centre) + local
        v[deform][group] = 1.0


# M6-V24: the atlas's tiles, in this order (row by row from the top-left), then any other role, then the props' palette.
TILE_ORDER = ["team", "outfit", "skin", "head", "hair"]
PROPS_TILE = "props"
# The atlas alpha is the team mask (and the props' roughness flag); no texel is transparent, so an importer's alpha
# handling (Godot's fix_alpha_border, a premultiply) never touches the colours. team_colour.gdshader reads it back.
ALPHA_PLAIN, ALPHA_PROP, ALPHA_TEAM = 1.0, 0.75, 0.5


def linear_to_srgb(rgb):
    """The sRGB value of linear channels (the IEC 61966-2-1 curve), what an 8-bit sRGB texel holds."""
    return [c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1.0 / 2.4) - 0.055 for c in rgb]


def atlas_layout(roles, has_props):
    """M6-V24: the atlas grid (columns, rows) and each tile's (column, row) from the top-left, one tile per role plus one
    for the props' palette: the fewest columns that make a near-square grid (4 tiles: 2 x 2, 5 or 6: 3 x 2)."""
    names = [r for r in TILE_ORDER if r in roles] + sorted(r for r in roles if r not in TILE_ORDER)
    if has_props:
        names.append(PROPS_TILE)
    cols = math.ceil(math.sqrt(len(names)))
    rows = math.ceil(len(names) / cols)
    return cols, rows, {name: (i % cols, i // cols) for i, name in enumerate(names)}


def palette_blocks(count):
    """The side of the square grid of colour blocks in the props' tile: a power of two, so each block's edges fall on the
    tile's mip boundaries (one colour: the whole tile, the same colour at every mip level)."""
    side = 1
    while side * side < count:
        side *= 2
    return side


def build_atlas(model, body, images, materials, prop_colours, size, work):
    """M6-V24: bakes the model's role textures and its props' flat colours into one atlas (one tile each, `size` square),
    moves every face's UVs into its tile, and gives the mesh one material, `team`, drawing the atlas. The atlas alpha marks
    each texel's kind (team / prop / plain, see ALPHA_*); team_colour.gdshader tints the team texels with the player colour
    and keeps each sample inside its tile. Returns the layout written into the file's extras."""
    role_of = {mat.name: role for role, mat in materials.items() if role in images}
    prop_of = {mat.name: name for name, mat in materials.items() if name in prop_colours}
    cols, rows, tiles = atlas_layout(sorted(images), bool(prop_colours))
    width, height = cols * size, rows * size
    px = np.zeros((height, width, 4), dtype=np.float32)  # Blender's rows run from the bottom
    px[:, :, 3] = ALPHA_PLAIN  # an unused tile too: no texel is transparent

    def tile_origin(name):
        c, r = tiles[name]
        return c * size, (rows - 1 - r) * size

    for role, img in images.items():
        src = np.empty(size * size * 4, dtype=np.float32)
        img.pixels.foreach_get(src)
        x0, y0 = tile_origin(role)
        px[y0:y0 + size, x0:x0 + size, :3] = src.reshape(size, size, 4)[:, :, :3]
        px[y0:y0 + size, x0:x0 + size, 3] = ALPHA_TEAM if role == "team" else ALPHA_PLAIN
    names = list(prop_colours)  # manifest order
    side = palette_blocks(len(names))
    block = size // side
    prop_uv = {}
    if names:
        x0, y0 = tile_origin(PROPS_TILE)
        px[y0:y0 + size, x0:x0 + size, 3] = ALPHA_PROP
        for j, name in enumerate(names):
            bc, br = j % side, j // side  # from the tile's top-left
            bx, by = x0 + bc * block, y0 + size - (br + 1) * block
            px[by:by + block, bx:bx + block, :3] = linear_to_srgb(prop_rgb(model, prop_colours[name]))
            prop_uv[name] = ((bc + 0.5) / side, 1.0 - (br + 0.5) / side)  # the block's centre, tile UV (v up)

    me = body.data
    n_loops, n_polys = len(me.loops), len(me.polygons)
    uv = np.empty(n_loops * 2, dtype=np.float32)
    me.uv_layers[0].data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    starts = np.empty(n_polys, dtype=np.int32)
    totals = np.empty(n_polys, dtype=np.int32)
    mat_index = np.empty(n_polys, dtype=np.int32)
    me.polygons.foreach_get("loop_start", starts)
    me.polygons.foreach_get("loop_total", totals)
    me.polygons.foreach_get("material_index", mat_index)
    loop_poly = np.repeat(np.arange(n_polys), totals)
    loop_mat = mat_index[loop_poly]
    for k, mat in enumerate(me.materials):
        sel = loop_mat == k
        if not sel.any():
            continue
        if mat.name in prop_of:
            tile, local = PROPS_TILE, np.tile(np.array(prop_uv[prop_of[mat.name]], dtype=np.float32), (int(sel.sum()), 1))
        elif mat.name in role_of:
            tile, local = role_of[mat.name], uv[sel]
            lo, hi = local.min(axis=0), local.max(axis=0)
            # A UV outside its texture would wrap there; in the atlas it would read the next tile. Half a texel over (the
            # decimation's UV interpolation leaves the hair 0.15 texel under 0) is clamped to the edge texel's centre, so
            # no vertex sits on a tile's edge (where the shader's floor() would pick the neighbouring tile); the shader
            # keeps its samples that far inside the tile anyway.
            slack = 0.5 / size
            if lo.min() < -slack or hi.max() > 1.0 + slack:
                fail(f"{model['id']}: {tile} UVs span {lo.round(4).tolist()}-{hi.round(4).tolist()}, outside [0, 1]")
            local = np.clip(local, slack, 1.0 - slack)
        else:
            fail(f"{model['id']}: material {mat.name} is neither a role nor a prop")
        c, r = tiles[tile]
        uv[sel, 0] = (c + local[:, 0]) / cols
        uv[sel, 1] = (rows - 1 - r + local[:, 1]) / rows
    me.uv_layers[0].data.foreach_set("uv", uv.ravel())

    old = list(me.materials)
    me.materials.clear()
    for mat in old:
        bpy.data.materials.remove(mat)
    for img in list(images.values()):
        bpy.data.images.remove(img)
    atlas = bpy.data.images.new("atlas", width, height, alpha=True)
    atlas.pixels.foreach_set(px.ravel())
    os.makedirs(work, exist_ok=True)
    atlas.filepath_raw = os.path.join(work, "atlas.png")
    atlas.file_format = 'PNG'
    atlas.save()
    me.materials.append(flat_material("team", image=atlas))
    me.polygons.foreach_set("material_index", np.zeros(n_polys, dtype=np.int32))
    layout = {"grid": [cols, rows], "tile": size, "tiles": {name: list(tiles[name]) for name in sorted(tiles)}}
    return atlas, layout


def team_uv(image, shade, block=32):
    """M6-V21: the UV of the centre of the `block` x `block` texel square of the team texture that is the most even and
    nearest the mean brightness `shade`. A prop drawn in team colour (a shield) puts all its UVs there, so it joins the team
    surface (no extra material, no extra draw call) and the team shader draws it in the player colour at about the
    brightness of the rest of the team part, at every mip level."""
    w, h = image.size
    px = np.empty(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(px)
    lum = (px.reshape(h, w, 4)[:, :, :3] @ LUMA).reshape(h // block, block, w // block, block)
    mean = lum.mean(axis=(1, 3))
    score = lum.var(axis=(1, 3)) + (mean - float(shade)) ** 2
    by, bx = np.unravel_index(int(np.argmin(score)), score.shape)  # first minimum: the same square every run
    # Blender's pixel rows and UV v both run from the bottom.
    return ((bx + 0.5) * block / w, (by + 0.5) * block / h)


def build_prop(prop, arm, work_clip_pose, material, uv=None):
    """A prop made of boxes (in armature space, -Y forward) placed so it sits in its bone's hand at the clip pose, drawn
    with `material`; `uv` (a team prop) is the one texture point every corner samples."""
    me = bpy.data.meshes.new(prop["name"])
    obj = bpy.data.objects.new(prop["name"], me)
    bpy.context.scene.collection.objects.link(obj)
    group = obj.vertex_groups.new(name=prop["bone"])
    bm = bmesh.new()
    deform = bm.verts.layers.deform.verify()
    for b in prop["boxes"]:
        box(bm, b["at"], b["size"], deform, group.index, b.get("rotation"))
    # Placed where the bone is in the clip's pose, then carried back into the rest pose the mesh is bound in.
    pose = work_clip_pose
    rest = arm.data.bones[prop["bone"]].matrix_local
    grip = Matrix.Translation(pose.to_translation() + pose.to_3x3() @ Vector(prop["offset"]))
    to_rest = rest @ pose.inverted() @ grip
    for v in bm.verts:
        v.co = to_rest @ v.co
    if uv is not None:
        layer = bm.loops.layers.uv.new("UVMap")
        for f in bm.faces:
            for loop in f.loops:
                loop[layer].uv = uv
    bm.to_mesh(me)
    bm.free()
    me.materials.append(material)
    obj.parent = arm
    mod = obj.modifiers.new("armature", 'ARMATURE')
    mod.object = arm
    return obj


def clear_custom_properties():
    """Drops every custom property (the glTF importer keeps a source's extras as custom properties): with extras exported,
    only the ones this script sets may reach the file."""
    for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.materials, bpy.data.armatures, bpy.data.scenes,
                 bpy.data.images, bpy.data.actions):
        for idb in coll:
            for key in list(idb.keys()):
                del idb[key]
    for arm in bpy.data.armatures:
        for bone in arm.bones:
            for key in list(bone.keys()):
                del bone[key]
    for obj in bpy.data.objects:
        for pbone in (obj.pose.bones if obj.pose else []):
            for key in list(pbone.keys()):
                del pbone[key]


def clip_pose(source_path, action_name, bone, frame):
    """The armature-space matrix of `bone` at `frame` of a library clip (the prop's aim pose)."""
    objs = import_gltf(source_path)
    arm = next(o for o in objs if o.type == 'ARMATURE')
    action = bpy.data.actions.get(action_name)
    if action is None:
        fail(f"clip {action_name} not in {source_path}")
    arm.animation_data_create()
    arm.animation_data.action = action
    if hasattr(arm.animation_data, "action_slot") and action.slots:
        arm.animation_data.action_slot = action.slots[0]
    bpy.context.scene.frame_set(int(action.frame_range[0] + frame * (action.frame_range[1] - action.frame_range[0])))
    m = arm.pose.bones[bone].matrix.copy()
    remove_objects(objs)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    return m


def build_model(manifest, model, src_root, repo, work):
    check_clip_map(manifest, model)
    reset()
    sources = {}
    arm = None
    parts = []
    for part in model["parts"]:
        path = os.path.join(src_root, part["source"])
        if path not in sources:
            sources[path] = import_gltf(path)
        objs = sources[path]
        obj = next((o for o in objs if o.type == 'MESH' and o.name.split(".")[0] == part["mesh"]), None)
        if obj is None:
            fail(f"{model['id']}: no mesh {part['mesh']} in {part['source']}")
        part_arm = next(o for o in objs if o.type == 'ARMATURE')
        if arm is None:
            arm = part_arm
        if "alignBone" in part:
            align_to_bone(obj, part_arm, arm, part["alignBone"], model["id"])
        if obj.parent is not arm:
            obj.parent = arm
            for m in obj.modifiers:
                if m.type == 'ARMATURE':
                    m.object = arm
        parts.append((part, obj))
    used = {obj for _, obj in parts} | {arm}
    remove_objects([o for objs in sources.values() for o in objs if o not in used])
    arm.name = "Armature"
    arm.data.name = "Armature"

    size = int(model["textureSize"])
    tints = model.get("tints", {})
    images = {}
    materials = {}
    for part, obj in parts:
        if "keepBones" in part:
            keep_bone_region(obj, part["keepBones"], float(part.get("keepThreshold", 0.5)))
        uv_for_material = {}
        for i, slot in enumerate(obj.material_slots):
            old = slot.material
            image, uv = base_colour_source(old)
            uv_for_material[i] = uv
            role = model["materials"].get(old.name.split(".")[0] if old else "", "outfit")
            if part.get("team") and role == model.get("teamRole", "outfit"):
                role = "team"
            if role not in images:
                if image is None:
                    fail(f"{model['id']}: material {old.name if old else None} has no base colour texture")
                # Named by role: Godot extracts it beside the model as <id>_<role>.png.
                images[role] = processed_image(image, role, size, tints.get(role), os.path.join(work, model["id"]),
                                               team_shade=model.get("teamShade", 0.8) if role == "team" else None)
            if role not in materials:
                materials[role] = flat_material(role, image=images[role])
            obj.material_slots[i].material = materials[role]
        single_uv(obj, uv_for_material)
        decimate(obj, int(part["triangles"]))

    props = []
    prop_colours = {}
    for prop in model.get("props", []):
        pose = clip_pose(os.path.join(src_root, manifest["animationSources"][prop["pose"]["source"]]),
                         prop["pose"]["clip"], prop["bone"], float(prop["pose"]["at"]))
        name = prop["material"]
        uv = None
        if name == "team":
            # M6-V21: a team prop (the Heavy Infantry's shield) joins the team surface at one even point of its texture.
            uv = team_uv(images["team"], model.get("teamShade", 0.8))
        elif name in materials:
            # Props naming one material share it (one surface): they must name one colour.
            if prop_colours.get(name) != prop["colour"]:
                fail(f"{model['id']}: prop material {name} is also a role or has two colours")
        else:
            materials[name] = flat_material(name, colour=prop_rgb(model, prop["colour"]), roughness=0.8)
            prop_colours[name] = prop["colour"]
        props.append(build_prop(prop, arm, pose, materials[name], uv))

    meshes = [obj for _, obj in parts] + props
    with bpy.context.temp_override(active_object=meshes[0], selected_editable_objects=meshes, object=meshes[0]):
        bpy.ops.object.join()
    body = meshes[0]
    body.name = model["id"]
    body.data.name = model["id"]
    # M6-V24: one surface, one material, one texture: the roles and the props' colours baked into an atlas.
    atlas, layout = build_atlas(model, body, images, materials, prop_colours, size, os.path.join(work, model["id"]))
    # Unused images from the sources must not reach the file.
    for img in list(bpy.data.images):
        if img is not atlas:
            bpy.data.images.remove(img)

    # glTF faces +Z; docs/04 wants -Z: the yaw lives on the armature node, so bone tracks stay untouched.
    arm.rotation_mode = 'XYZ'
    arm.rotation_euler = (0.0, 0.0, math.radians(float(model.get("yawDegrees", 0))))
    # M6-V21: the model's clip map (docs/04 state name -> library clip) rides on the armature node as glTF extras; Godot
    # imports it as the node's "extras" metadata and UnitModels plays those clips (a sword swing for one model, a shot
    # for another). Nothing else may carry extras.
    clear_custom_properties()
    arm["clips"] = {k: model["clips"][k] for k in sorted(model["clips"])}
    # M6-V24: the atlas layout beside it (the team shader keeps each sample inside its tile; tests find the props' faces).
    arm["atlas"] = layout

    tris = triangles(body)
    zs = [v.co.z for v in body.data.vertices]
    height = max(zs) - min(zs)
    if tris > int(model["maxTriangles"]):
        fail(f"{model['id']}: {tris} triangles, over the {model['maxTriangles']} cap")
    if [m.name for m in body.data.materials] != ["team"]:
        fail(f"{model['id']}: materials {[m.name for m in body.data.materials]}, want the one team material")
    dest = os.path.join(repo, model["destination"])
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=dest, export_format='GLB', export_animations=False, export_skins=True,
                              export_morph=False, export_yup=True, export_apply=False, export_extras=True,
                              export_tangents=False, export_cameras=False, export_lights=False)
    return {"id": model["id"], "destination": model["destination"], "triangles": tris,
            "height": round(height, 3), "feet": round(min(zs), 3), "clips": arm["clips"].to_dict(),
            "materials": sorted(m.name for m in body.data.materials),
            "textures": {atlas.name: list(atlas.size)}, "atlas": layout}


def library_clips(manifest):
    """Library name -> (source alias, clip), from the manifest's library.clips (M6-V21; one name, one clip). Every clip a
    model's clip map names must be there, and every library clip must be named by some model (no dead weight)."""
    clips = {}
    for name, ref in manifest["library"]["clips"].items():
        alias, clip = ref.split("/", 1)
        if alias not in manifest["animationSources"]:
            fail(f"library clip {name}: no animation source {alias}")
        clips[name] = (alias, clip)
    used = set()
    for model in manifest["models"]:
        check_clip_map(manifest, model)
        used.update(model["clips"].values())
    unused = sorted(set(clips) - used)
    if unused:
        fail(f"library clips no model plays: {unused}")
    return clips


def check_clip_map(manifest, model):
    """A model's clip map: docs/04 state name -> a library clip name."""
    for state, name in model["clips"].items():
        if name not in manifest["library"]["clips"]:
            fail(f"{model['id']}: clip {state} -> {name}, not in library.clips")


def action_fcurve_bags(action):
    """Every fcurve collection of `action`: the slotted layers' channel bags (Blender 4.4+), or the legacy list."""
    bags = []
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            bags.extend(getattr(strip, "channelbags", []))
    if not bags and hasattr(action, "fcurves"):
        bags.append(action)
    return bags


def drop_bones(arm, actions, prefixes):
    """Deletes the armature's bones whose names start with one of `prefixes`, and their curves in `actions`.
    BUG-1321: the 40 finger bones were 40 of each clip's 65 bone tracks, the larger part of the crowd's animation cost;
    a figure 1.8 m tall seen from 20-60 m shows no finger. The models keep the bones (their skins are bound to them):
    with no track, Godot leaves them at their rest pose. Returns the dropped names, sorted."""
    names = sorted(b.name for b in arm.data.bones if any(b.name.startswith(p) for p in prefixes))
    if not names:
        return names
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    if arm.mode != 'EDIT':
        fail("library: the armature did not enter edit mode to drop bones")
    for name in names:
        arm.data.edit_bones.remove(arm.data.edit_bones[name])
    bpy.ops.object.mode_set(mode='OBJECT')
    gone = set(names)

    def bone_of(path):  # 'pose.bones["index_01_l"].rotation_quaternion' -> 'index_01_l'
        return path.split('"')[1] if path.startswith('pose.bones["') else None

    for action in actions:
        for bag in action_fcurve_bags(action):
            for fc in [fc for fc in bag.fcurves if bone_of(fc.data_path) in gone]:
                bag.fcurves.remove(fc)
    return names


def build_library(manifest, src_root, repo, work):
    reset()
    clips = library_clips(manifest)
    arm = None
    found = {}
    for alias, rel in manifest["animationSources"].items():
        if not any(a == alias for a, _ in clips.values()):
            continue
        before = set(bpy.data.actions)
        objs = import_gltf(os.path.join(src_root, rel))
        new_actions = {a.name: a for a in bpy.data.actions if a not in before}
        for name, (a, clip) in clips.items():
            if a == alias:
                if clip not in new_actions:
                    fail(f"clip {clip} not in {rel}")
                found[name] = new_actions[clip]
        keep = next(o for o in objs if o.type == 'ARMATURE') if arm is None else None
        if keep is not None:
            arm = keep
        remove_objects([o for o in objs if o is not arm])
    for a in list(bpy.data.actions):
        if a not in found.values():
            bpy.data.actions.remove(a)
    arm.name = "Armature"
    arm.data.name = "Armature"
    dropped = drop_bones(arm, list(found.values()), manifest["library"].get("dropBonePrefixes", []))
    want = manifest["library"].get("dropBoneCount")
    if want is not None and len(dropped) != int(want):
        fail(f"library: {len(dropped)} bones match dropBonePrefixes, the manifest expects {want}")
    # One triangle bound to the root: the skin that makes Godot import the bones as a Skeleton3D (bone tracks).
    me = bpy.data.meshes.new("skin_anchor")
    me.from_pydata([(0, 0, 0), (0.01, 0, 0), (0, 0, 0.01)], [], [(0, 1, 2)])
    anchor = bpy.data.objects.new("skin_anchor", me)
    bpy.context.scene.collection.objects.link(anchor)
    anchor.parent = arm
    group = anchor.vertex_groups.new(name="root")
    group.add([0, 1, 2], 1.0, 'REPLACE')
    anchor.modifiers.new("armature", 'ARMATURE').object = arm
    # One NLA track per clip, named by docs/04's name: the exporter writes one glTF animation per track. The "_loop"
    # suffix is Godot's import rule for a looping clip; Godot drops it from the name (docs/04 "Look test (M6-V10)").
    arm.animation_data_create()
    arm.animation_data.action = None
    # The importer stacks every source clip as an NLA track: only the library's own tracks may reach the file.
    for track in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(track)
    for name in sorted(found):
        action = found[name]
        action.name = name + "_loop"
        track = arm.animation_data.nla_tracks.new()
        track.name = name + "_loop"
        strip = track.strips.new(name + "_loop", int(action.frame_range[0]), action)
        if hasattr(strip, "action_slot") and action.slots:
            strip.action_slot = action.slots[0]
    dest = os.path.join(repo, manifest["library"]["destination"])
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=dest, export_format='GLB', export_animations=True,
                              export_animation_mode='NLA_TRACKS', export_skins=True, export_morph=False,
                              export_yup=True, export_materials='NONE', export_extras=False,
                              export_cameras=False, export_lights=False)
    return {"destination": manifest["library"]["destination"],
            "clips": {n: f"{clips[n][0]}/{clips[n][1]}" for n in sorted(found)},
            "bones": len(arm.data.bones), "dropped": len(dropped),
            "seconds": {n: round((found[n].frame_range[1] - found[n].frame_range[0]) / FPS, 2) for n in sorted(found)}}


def render_preview(path):
    """A front and side workbench render of what was just built (the developer's look, not an output)."""
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.light = 'STUDIO'
    scene.render.resolution_x, scene.render.resolution_y = 512, 512
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 2.2
    scene.camera = cam
    for label, loc, rot in (("front", (0, 4, 0.95), (math.pi / 2, 0, math.pi)), ("side", (4, 0, 0.95), (math.pi / 2, 0, math.pi / 2))):
        cam.location = loc
        cam.rotation_euler = rot
        scene.render.filepath = path.replace(".png", f"-{label}.png")
        bpy.ops.render.render(write_still=True)


def main():
    args = parse_args()
    with open(args["manifest"], encoding="utf-8") as f:
        manifest = json.load(f)
    os.makedirs(args["work"], exist_ok=True)
    if args["library"]:
        result = build_library(manifest, args["source_root"], args["repo"], args["work"])
    else:
        model = next((m for m in manifest["models"] if m["id"] == args["model"]), None)
        if model is None:
            fail(f"no model {args['model']} in the manifest")
        result = build_model(manifest, model, args["source_root"], args["repo"], args["work"])
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
