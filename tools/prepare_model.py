"""
Turn a downloaded aircraft model into something the game can fly.

Run through headless Blender. See tools/prepare-model.ps1 for the wrapper.

Downloaded models arrive in whatever orientation, scale and topology the artist
left them in. This script normalises all of it.

EVERYTHING HERE IS IN BLENDER SPACE, WHICH IS Z-UP. The target layout is:

  * nose along +X
  * up along +Z
  * wings spanning Y

Do NOT rotate the model to be Y-up to match Godot. Blender's glTF exporter
already converts Z-up to Y-up on the way out, so a model arranged Z-up here
arrives in Godot exactly right. Rotating it first gets it wrong twice.

Also true of the finished model:

  * origin at the centre of gravity, not at the artist's world origin
  * length in real meters
  * the propeller as its own object, so it can spin
  * a triangle count the renderer can afford

Inspect first, convert second:

    prepare-model.ps1 -Inspect raw.glb
    prepare-model.ps1 raw.glb -Name camel -NoseAxis -X
"""

import argparse
import math
import sys

import bpy
import bmesh
from mathutils import Matrix, Vector

AXES = {
    "+X": Vector((1, 0, 0)), "-X": Vector((-1, 0, 0)),
    "+Y": Vector((0, 1, 0)), "-Y": Vector((0, -1, 0)),
    "+Z": Vector((0, 0, 1)), "-Z": Vector((0, 0, -1)),
}

PROP_HINTS = ("prop", "blade", "airscrew", "screw", "spinner", "vrtule")


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_any(path):
    lower = path.lower()
    if lower.endswith((".glb", ".gltf")):
        bpy.ops.import_scene.gltf(filepath=path)
    elif lower.endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=path)
    elif lower.endswith(".obj"):
        bpy.ops.wm.obj_import(filepath=path)
    elif lower.endswith(".dae"):
        bpy.ops.wm.collada_import(filepath=path)
    else:
        sys.exit(f"Do not know how to import {path}")


def meshes():
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def flatten():
    """
    Unparent everything and bake its world transform into the mesh.

    glTF importers hang the meshes off a tree of empties, and one of those
    empties usually carries the Y-up to Z-up conversion. Rotating a child and
    applying the transform then does nothing useful, because the parent still
    contributes its own rotation on top: the model comes out exactly as it went
    in, which is precisely what happened the first time.

    Flattening first means local and world are the same thing, so every later
    step can just set a rotation and apply it.
    """
    bpy.ops.object.select_all(action="SELECT")
    if bpy.context.scene.objects:
        bpy.context.view_layer.objects.active = bpy.context.scene.objects[0]
        bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")

    for obj in list(bpy.context.scene.objects):
        if obj.type != "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)

    objs = meshes()
    if not objs:
        return

    bpy.ops.object.select_all(action="DESELECT")
    for obj in objs:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def transform_meshes(objects, matrix):
    """
    Apply a matrix straight to the vertices.

    Deliberately not obj.rotation_euler followed by transform_apply. That path
    depends on object mode, selection state, the object's rotation_mode and its
    parenting, and when any of those is not what you assumed it fails SILENTLY:
    the script reports success and writes a model that never turned. A Fokker
    came out with its wingspan scaled down to the fuselage length that way.

    Editing mesh data has none of those preconditions.
    """
    seen = set()
    for obj in objects:
        if obj is None or obj.data.name in seen:
            continue          # joined objects can share mesh data
        seen.add(obj.data.name)
        obj.data.transform(matrix)
        obj.data.update()


def world_bounds(objects):
    """
    Exact bounds, measured from the vertices.

    NOT obj.bound_box, which is cached from the evaluated mesh and does not
    refresh when the mesh data is edited underneath it. Using it meant the
    scaling step measured the model as it was BEFORE the rotation, and quietly
    scaled a Fokker's wingspan down to its fuselage length.
    """
    lo = Vector((1e18, 1e18, 1e18))
    hi = Vector((-1e18, -1e18, -1e18))

    for obj in objects:
        if obj is None:
            continue
        matrix = obj.matrix_world
        for vertex in obj.data.vertices:
            p = matrix @ vertex.co
            lo = Vector((min(lo[i], p[i]) for i in range(3)))
            hi = Vector((max(hi[i], p[i]) for i in range(3)))

    return lo, hi


def triangle_count(objects):
    total = 0
    for obj in objects:
        mesh = obj.data
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        total += len(bm.faces)
        bm.free()
    return total


def inspect(path):
    """Print everything needed to work out the right conversion flags."""
    clear_scene()
    import_any(path)
    flatten()
    objs = meshes()

    lo, hi = world_bounds(objs)
    size = hi - lo

    print("\n=== model inspection ===")
    print(f"file        {path}")
    print(f"mesh objects {len(objs)}")
    print(f"triangles    {triangle_count(objs)}")
    print(f"bounds min   ({lo.x:.3f}, {lo.y:.3f}, {lo.z:.3f})")
    print(f"bounds max   ({hi.x:.3f}, {hi.y:.3f}, {hi.z:.3f})")
    print(f"size         ({size.x:.3f}, {size.y:.3f}, {size.z:.3f})")

    longest = max(range(3), key=lambda i: size[i])
    widest = sorted(range(3), key=lambda i: -size[i])[1]
    print(f"longest axis {'XYZ'[longest]}  (probably nose to tail)")
    print(f"second axis  {'XYZ'[widest]}  (probably the wingspan)")

    print("\nobjects (index, faces, size, materials):")
    for i, obj in enumerate(objs):
        olo, ohi = world_bounds([obj])
        osize = ohi - olo
        mats = ",".join(sorted({s.material.name for s in obj.material_slots if s.material})) or "-"

        hints = []
        if any(h in obj.name.lower() for h in PROP_HINTS):
            hints.append("name says propeller")
        if len(obj.data.polygons) <= 4:
            hints.append("tiny, probably a ground plane or a billboard")
        if "ground" in mats.lower() or "floor" in mats.lower():
            hints.append("ground material")

        note = ("  <-- " + "; ".join(hints)) if hints else ""
        print(f"  [{i}] {obj.name:<28} {len(obj.data.polygons):>7} faces  "
              f"({osize.x:7.1f},{osize.y:7.1f},{osize.z:7.1f})  {mats}{note}")

    print("\nDrop anything that is not the aeroplane with --drop, by index or name.")
    print("=== end ===\n")


def drop_objects(specs):
    """
    Delete objects the aeroplane does not need.

    Scanned scenes routinely ship a ground plane, a backdrop or a display stand,
    and every one of them wrecks the bounding box the scaling depends on.
    """
    if not specs:
        return

    objs = meshes()
    doomed = set()
    for spec in specs:
        spec = spec.strip()
        if spec.isdigit():
            index = int(spec)
            if 0 <= index < len(objs):
                doomed.add(objs[index])
        else:
            needle = spec.lower()
            for obj in objs:
                # Match the material too. Imported scenes often name every object
                # "defaultMaterial", and the material is the only thing that says
                # which part is which.
                materials = " ".join(s.material.name.lower()
                                     for s in obj.material_slots if s.material)
                if needle in obj.name.lower() or needle in materials:
                    doomed.add(obj)

    for obj in doomed:
        print(f"dropping {obj.name} ({len(obj.data.polygons)} faces)")
        bpy.data.objects.remove(obj, do_unlink=True)


def join_all(name):
    objs = meshes()
    if not objs:
        sys.exit("No meshes were imported.")
    for obj in bpy.context.scene.objects:
        obj.select_set(obj in objs)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = name
    return body


def find_propeller():
    """Match the propeller by name before touching geometry."""
    for obj in meshes():
        if any(hint in obj.name.lower() for hint in PROP_HINTS):
            return obj
    return None


def split_propeller_by_geometry(body, nose_fraction):
    """
    Fall back to cutting off everything ahead of a plane near the nose.

    Crude, and it is meant to be: the alternative is hand-editing every model.
    The inspection listing usually finds a named propeller first, and this only
    runs when it does not.
    """
    mesh = body.data
    lo, hi = world_bounds([body])
    cut = lo.x + (hi.x - lo.x) * nose_fraction

    bm = bmesh.new()
    bm.from_mesh(mesh)
    ahead = [f for f in bm.faces if f.calc_center_median().x > cut]
    if not ahead:
        bm.free()
        return None

    for f in bm.faces:
        f.select = f in ahead
    bm.to_mesh(mesh)
    bm.free()

    bpy.context.view_layer.objects.active = body
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")

    parts = [o for o in bpy.context.selected_objects if o is not body]
    if not parts:
        return None
    prop = parts[0]
    prop.name = "Propeller"
    return prop


def orient(objects, rotate_x, rotate_y, rotate_z, nose_axis, up_axis):
    """Rotate the model so the nose runs +X, in Blender's Z-up space."""
    rotation = (Matrix.Rotation(math.radians(rotate_z), 4, "Z")
                @ Matrix.Rotation(math.radians(rotate_y), 4, "Y")
                @ Matrix.Rotation(math.radians(rotate_x), 4, "X"))
    transform_meshes(objects, rotation)

    # Then swing whichever axis the nose ended up on round to +X.
    nose = AXES[nose_axis]
    target = Vector((1, 0, 0))
    if nose.dot(target) >= 0.999:
        return

    axis = nose.cross(target)
    angle = nose.angle(target)
    if axis.length < 1e-6:
        # Nose is exactly backwards, so the cross product gives no axis to turn
        # about and we have to pick one. It must be the model's VERTICAL, which
        # in Blender is Z. Using Y spins the aircraft about its own long axis on
        # the way round and lands it upside down.
        axis = AXES[up_axis]
        angle = math.pi

    transform_meshes(objects, Matrix.Rotation(angle, 4, axis.normalized()))


def pitch(objects, degrees):
    """
    Tilt the aircraft after it has been turned to face +X.

    Almost every aircraft model is built sitting on its undercarriage, and a
    taildragger parks nose-high by twelve to fifteen degrees. Imported as-is it
    flies permanently nose-up, looking like it is climbing while the telemetry
    says one degree of alpha. This levels it onto its flight attitude.

    Positive is nose up. A rotation about +Y tips the nose toward -Z, which is
    down, hence the sign flip.
    """
    if abs(degrees) < 1e-6:
        return

    transform_meshes(objects, Matrix.Rotation(math.radians(-degrees), 4, "Y"))


def scale_and_centre(objects, target_length):
    lo, hi = world_bounds(objects)
    length = hi.x - lo.x
    if length < 1e-6:
        sys.exit("Model has no length along X after orientation. Check the flags.")

    factor = target_length / length
    transform_meshes(objects, Matrix.Scale(factor, 4))

    # Origin at the centre of gravity, which for these purposes is a third back
    # from the nose: that is roughly where a biplane balanced, and it is what the
    # flight model assumes when it rotates the airframe.
    lo, hi = world_bounds(objects)
    cg = Vector((hi.x - (hi.x - lo.x) * 0.36,
                 (lo.y + hi.y) * 0.5,
                 (lo.z + hi.z) * 0.5))
    transform_meshes(objects, Matrix.Translation(-cg))


def shrink_textures(max_size):
    """
    Downsize every texture in the file.

    Sketchfab ships 2K and 4K PBR sets, which came to 30 MB in a single .glb for
    an aircraft that is never more than about 150 pixels tall on screen. That is
    a repository and a load time spent on detail nobody can see.
    """
    if max_size <= 0:
        return

    for image in bpy.data.images:
        if image.size[0] <= max_size and image.size[1] <= max_size:
            continue

        width, height = image.size
        scale = max_size / max(width, height)
        new_size = (max(1, int(width * scale)), max(1, int(height * scale)))
        print(f"texture {image.name}: {width}x{height} -> {new_size[0]}x{new_size[1]}")
        image.scale(*new_size)


def decimate(objects, budget):
    total = triangle_count(objects)
    if total <= budget:
        print(f"triangles {total}, already inside the {budget} budget")
        return

    ratio = budget / total
    print(f"triangles {total} -> decimating to {ratio:.3f} for a {budget} budget")
    for obj in objects:
        mod = obj.modifiers.new("Decimate", "DECIMATE")
        mod.ratio = ratio
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
    print(f"triangles now {triangle_count(objects)}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("source")
    ap.add_argument("--inspect", action="store_true")
    ap.add_argument("--out", default="")
    ap.add_argument("--name", default="aircraft")
    ap.add_argument("--length", type=float, default=5.71)
    ap.add_argument("--budget", type=int, default=8000)
    ap.add_argument("--rotate-x", type=float, default=0.0)
    ap.add_argument("--rotate-y", type=float, default=0.0)
    ap.add_argument("--rotate-z", type=float, default=0.0)
    ap.add_argument("--nose-axis", default="+X", choices=list(AXES))
    ap.add_argument("--up-axis", default="+Z", choices=list(AXES))
    ap.add_argument("--prop-cut", type=float, default=0.93)
    ap.add_argument("--pitch", type=float, default=0.0,
                    help="Degrees nose up, applied after the nose is turned to +X. "
                         "Use a negative value to level a parked taildragger.")
    ap.add_argument("--texture-size", type=int, default=1024)
    ap.add_argument("--drop", default="",
                    help="Comma separated object indices or name fragments to delete.")
    args = ap.parse_args(argv)

    if args.inspect:
        inspect(args.source)
        return

    clear_scene()
    import_any(args.source)
    flatten()
    drop_objects([s for s in args.drop.split(",") if s.strip()])

    prop = find_propeller()
    if prop:
        print(f"propeller found by name: {prop.name}")
        prop.name = "Propeller"

    body = join_all(args.name) if not prop else None
    if body is None:
        others = [o for o in meshes() if o is not prop]
        for obj in bpy.context.scene.objects:
            obj.select_set(obj in others)
        bpy.context.view_layer.objects.active = others[0]
        if len(others) > 1:
            bpy.ops.object.join()
        body = bpy.context.view_layer.objects.active
        body.name = args.name

    # ORIENT BEFORE SPLITTING THE PROPELLER.
    #
    # The geometry split takes everything ahead of a plane near +X, and "ahead"
    # only means the nose once the model has been turned round. Splitting first
    # on a model whose nose pointed -X sawed the TAIL off and made it spin.
    group = [o for o in (body, prop) if o]
    orient(group, args.rotate_x, args.rotate_y, args.rotate_z, args.nose_axis, args.up_axis)
    pitch(group, args.pitch)
    scale_and_centre(group, args.length)

    if prop is None:
        prop = split_propeller_by_geometry(body, args.prop_cut)
        print("propeller split off by geometry, from the nose" if prop else
              "WARNING: no propeller found, it will not spin")
        if prop:
            group.append(prop)

    decimate(group, args.budget)
    shrink_textures(args.texture_size)

    # The propeller needs its own origin at the hub or it will orbit the aircraft
    # instead of spinning on its shaft.
    if prop:
        lo, hi = world_bounds([prop])
        hub = Vector(((lo.x + hi.x) * 0.5, (lo.y + hi.y) * 0.5, (lo.z + hi.z) * 0.5))
        bpy.context.scene.cursor.location = hub
        bpy.ops.object.select_all(action="DESELECT")
        prop.select_set(True)
        bpy.context.view_layer.objects.active = prop
        bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

    out = args.out or f"{args.name}.glb"
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True)
    print(f"\nwrote {out}")


if __name__ == "__main__":
    main()
