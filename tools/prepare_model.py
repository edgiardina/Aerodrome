"""
Turn a downloaded aircraft model into something the game can fly.

Run through headless Blender. See tools/prepare-model.ps1 for the wrapper.

Downloaded models arrive in whatever orientation, scale and topology the artist
left them in. The game needs all of the following to be true, and none of them
usually are:

  * nose along +X, up along +Y, wings spanning Z
  * origin at the centre of gravity, not at the artist's world origin
  * length in real meters
  * the propeller as its own object, so it can spin
  * a triangle count the renderer can afford

Inspect first, convert second:

    prepare-model.ps1 -Inspect raw.glb
    prepare-model.ps1 raw.glb -Name camel -RotateX -90 -NoseAxis -Y
"""

import argparse
import math
import sys

import bpy
import bmesh
from mathutils import Vector

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


def world_bounds(objects):
    lo = Vector((1e18, 1e18, 1e18))
    hi = Vector((-1e18, -1e18, -1e18))
    for obj in objects:
        for corner in obj.bound_box:
            p = obj.matrix_world @ Vector(corner)
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

    print("\nobjects:")
    for obj in objs:
        hint = "  <-- looks like the propeller" if any(
            h in obj.name.lower() for h in PROP_HINTS) else ""
        print(f"  {obj.name:<40} {len(obj.data.polygons):>7} faces{hint}")
    print("=== end ===\n")


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
    """Rotate the model so the nose runs +X and up runs +Y."""
    for obj in objects:
        obj.rotation_euler = (math.radians(rotate_x),
                              math.radians(rotate_y),
                              math.radians(rotate_z))
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    # Then swing whichever axis the nose ended up on round to +X.
    nose = AXES[nose_axis]
    target = Vector((1, 0, 0))
    if nose.dot(target) < 0.999:
        axis = nose.cross(target)
        angle = nose.angle(target)
        if axis.length < 1e-6:            # exactly backwards
            axis = AXES[up_axis]
            angle = math.pi
        for obj in objects:
            obj.rotation_mode = "AXIS_ANGLE"
            obj.rotation_axis_angle = (angle, axis.x, axis.y, axis.z)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)


def scale_and_centre(objects, target_length):
    lo, hi = world_bounds(objects)
    length = hi.x - lo.x
    if length < 1e-6:
        sys.exit("Model has no length along X after orientation. Check the flags.")

    factor = target_length / length
    for obj in objects:
        obj.scale = (factor, factor, factor)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Origin at the centre of gravity, which for these purposes is a third back
    # from the nose: that is roughly where a biplane balanced, and it is what the
    # flight model assumes when it rotates the airframe.
    lo, hi = world_bounds(objects)
    cg = Vector((hi.x - (hi.x - lo.x) * 0.36,
                 (lo.y + hi.y) * 0.5,
                 (lo.z + hi.z) * 0.5))
    for obj in objects:
        obj.location -= cg
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)


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
    ap.add_argument("--up-axis", default="+Y", choices=list(AXES))
    ap.add_argument("--prop-cut", type=float, default=0.93)
    args = ap.parse_args(argv)

    if args.inspect:
        inspect(args.source)
        return

    clear_scene()
    import_any(args.source)

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

    if prop is None:
        prop = split_propeller_by_geometry(body, args.prop_cut)
        print("propeller split off by geometry" if prop else
              "WARNING: no propeller found, it will not spin")

    group = [o for o in (body, prop) if o]
    orient(group, args.rotate_x, args.rotate_y, args.rotate_z, args.nose_axis, args.up_axis)
    scale_and_centre(group, args.length)
    decimate(group, args.budget)

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
