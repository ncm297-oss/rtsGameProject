# Builds one unit model, or the shared animation library, from the raw Quaternius packs (docs/04 "Import process").
# Run by tools/import-assets.ps1 inside Blender (background mode); never edit the outputs by hand, re-run instead.
#
#   blender -b --factory-startup -P tools/art/build_unit.py -- --manifest tools/asset-manifest.json
#       --source-root <asset-sources dir> --repo <repo root> --work <temp dir> (--model <id> | --library) [--preview <png>]
#
# A model: the outfit parts and the base character's head on one 65-bone armature, each part decimated to its triangle
# share, joined into one mesh with one material per role (the team part's material is named "team"), base colour
# textures only, cut to the manifest's size and tinted toward the faction palette, the manifest's props built from boxes
# and bound to their bone, the yaw fix on the armature node (glTF faces +Z, docs/04 wants -Z), exported as one .glb with
# no animations. The library: the clips every model's clip map names, renamed to docs/04's names, on one armature with a
# one-triangle skin (so Godot imports the tracks as Skeleton3D bone tracks), exported as one .glb.
# Prints one "RESULT {json}" line the PowerShell script reads; exits 1 with a "FAIL <why>" line on any problem.
import bmesh
import bpy
import json
import math
import os
import sys

import numpy as np
from mathutils import Matrix, Vector

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
            px[:, :3] = np.clip(px[:, :3] * (1.0 - a) + lum[:, None] * rgb[None, :] * a, 0.0, 1.0)
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


def box(bm, centre, size, deform, group):
    """Adds an axis-aligned box (12 triangles) to `bm`, every vertex weighted 1 to `group`."""
    res = bmesh.ops.create_cube(bm, size=1.0)
    for v in res["verts"]:
        v.co = Vector((centre[0] + v.co.x * size[0], centre[1] + v.co.y * size[1], centre[2] + v.co.z * size[2]))
        v[deform][group] = 1.0


def build_prop(prop, arm, work_clip_pose):
    """A prop made of boxes (in armature space, -Y forward) placed so it sits in its bone's hand at the clip pose."""
    me = bpy.data.meshes.new(prop["name"])
    obj = bpy.data.objects.new(prop["name"], me)
    bpy.context.scene.collection.objects.link(obj)
    group = obj.vertex_groups.new(name=prop["bone"])
    bm = bmesh.new()
    deform = bm.verts.layers.deform.verify()
    for b in prop["boxes"]:
        box(bm, b["at"], b["size"], deform, group.index)
    # Placed where the bone is in the clip's pose, then carried back into the rest pose the mesh is bound in.
    pose = work_clip_pose
    rest = arm.data.bones[prop["bone"]].matrix_local
    grip = Matrix.Translation(pose.to_translation() + pose.to_3x3() @ Vector(prop["offset"]))
    to_rest = rest @ pose.inverted() @ grip
    for v in bm.verts:
        v.co = to_rest @ v.co
    bm.to_mesh(me)
    bm.free()
    mat = flat_material(prop["material"], colour=hex_rgb(prop["colour"]), roughness=0.8)
    me.materials.append(mat)
    obj.parent = arm
    mod = obj.modifiers.new("armature", 'ARMATURE')
    mod.object = arm
    return obj


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
    for prop in model.get("props", []):
        pose = clip_pose(os.path.join(src_root, manifest["animationSources"][prop["pose"]["source"]]),
                         prop["pose"]["clip"], prop["bone"], float(prop["pose"]["at"]))
        props.append(build_prop(prop, arm, pose))

    meshes = [obj for _, obj in parts] + props
    with bpy.context.temp_override(active_object=meshes[0], selected_editable_objects=meshes, object=meshes[0]):
        bpy.ops.object.join()
    body = meshes[0]
    body.name = model["id"]
    body.data.name = model["id"]
    # Unused images from the sources must not reach the file.
    for img in list(bpy.data.images):
        if img not in images.values():
            bpy.data.images.remove(img)

    # glTF faces +Z; docs/04 wants -Z: the yaw lives on the armature node, so bone tracks stay untouched.
    arm.rotation_mode = 'XYZ'
    arm.rotation_euler = (0.0, 0.0, math.radians(float(model.get("yawDegrees", 0))))

    tris = triangles(body)
    zs = [v.co.z for v in body.data.vertices]
    height = max(zs) - min(zs)
    if tris > int(model["maxTriangles"]):
        fail(f"{model['id']}: {tris} triangles, over the {model['maxTriangles']} cap")
    if "team" not in [m.name for m in body.data.materials]:
        fail(f"{model['id']}: no team material")
    dest = os.path.join(repo, model["destination"])
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=dest, export_format='GLB', export_animations=False, export_skins=True,
                              export_morph=False, export_yup=True, export_apply=False, export_extras=False,
                              export_tangents=False, export_cameras=False, export_lights=False)
    return {"id": model["id"], "destination": model["destination"], "triangles": tris,
            "height": round(height, 3), "feet": round(min(zs), 3),
            "materials": sorted(m.name for m in body.data.materials),
            "textures": {img.name: list(img.size) for img in images.values()}}


def library_clips(manifest):
    """docs/04 name -> (source alias, clip): the union of every model's clip map; one name must mean one clip."""
    clips = {}
    for model in manifest["models"]:
        for name, ref in model["clips"].items():
            alias, clip = ref.split("/", 1)
            if name in clips and clips[name] != (alias, clip):
                fail(f"clip {name} maps to {clips[name]} and {(alias, clip)}: one library name, one clip")
            clips[name] = (alias, clip)
    return clips


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
