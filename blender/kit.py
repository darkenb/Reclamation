# Shared helpers for the Trash Station building models. Run inside Blender: exec(open(path).read()).
import bpy, bmesh, math
from mathutils import Vector, Matrix, Euler

# Palette: one solid swatch per material. The exporter writes these values into the model file, the mod builds the
# textures from them. (albedo rgb, metallic, smoothness, emission rgb)
PALETTE = {
    'Frame':      ((0.64, 0.65, 0.68), 0.6, 0.45, (0, 0, 0)),
    'Body':       ((0.80, 0.81, 0.83), 0.5, 0.50, (0, 0, 0)),
    'Accent':     ((0.80, 0.40, 0.09), 0.4, 0.50, (0, 0, 0)),
    'Dark':       ((0.055, 0.060, 0.085), 0.4, 0.40, (0, 0, 0)),
    'Pipe':       ((0.30, 0.31, 0.34), 0.8, 0.60, (0, 0, 0)),
    'GlowPurple': ((0.35, 0.10, 0.55), 0.0, 0.80, (0.55, 0.12, 0.95)),
    'GlowCore':   ((0.60, 0.15, 0.40), 0.0, 0.90, (1.00, 0.25, 0.65)),
    'GlowFire':   ((0.90, 0.35, 0.05), 0.0, 0.70, (1.00, 0.42, 0.06)),
    'GlowEmber':  ((0.50, 0.12, 0.02), 0.0, 0.50, (0.70, 0.16, 0.02)),
}

def material(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
    alb, met, smo, emi = PALETTE[name]
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*alb, 1)
    b.inputs['Metallic'].default_value = met
    b.inputs['Roughness'].default_value = 1 - smo
    b.inputs['Emission Color'].default_value = (*emi, 1)
    b.inputs['Emission Strength'].default_value = 4.0 if any(emi) else 0.0
    m.diffuse_color = (*alb, 1) if not any(emi) else (*emi, 1)
    return m

def collection(name, clear=True):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    elif clear:
        for o in list(c.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    return c

def _finish(bm, name, col, mat, loc=(0, 0, 0), rot=(0, 0, 0), smooth=False, bevel=0.0, parent=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = smooth
    me.materials.append(material(mat))
    o = bpy.data.objects.new(name, me)
    col.objects.link(o)
    o.location = loc; o.rotation_euler = rot
    if parent: o.parent = parent
    if bevel > 0:
        md = o.modifiers.new('Bevel', 'BEVEL'); md.width = bevel; md.segments = 1; md.limit_method = 'ANGLE'
    return o

def box(name, col, mat, size, loc=(0, 0, 0), rot=(0, 0, 0), bevel=0.0, parent=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    return _finish(bm, name, col, mat, loc, rot, bevel=bevel, parent=parent)

def cyl(name, col, mat, r1, r2, h, seg=24, loc=(0, 0, 0), rot=(0, 0, 0), smooth=True, cap=True, parent=None):
    # Base at z=0 of its own space, top at z=h.
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=cap, cap_tris=False, segments=seg, radius1=r1, radius2=r2, depth=h)
    bmesh.ops.translate(bm, vec=Vector((0, 0, h / 2)), verts=bm.verts)
    o = _finish(bm, name, col, mat, loc, rot, smooth=smooth, parent=parent)
    if smooth:  # caps flat, sides smooth
        for p in o.data.polygons: p.use_smooth = abs(p.normal.z) < 0.7
    return o

def strut(name, col, mat, a, b, r, seg=8):
    a, b = Vector(a), Vector(b)
    d = b - a
    o = cyl(name, col, mat, r, r, d.length, seg=seg, loc=a)
    o.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
    return o

def torus(name, col, mat, R, r, seg=32, rseg=8, loc=(0, 0, 0), rot=(0, 0, 0), parent=None):
    bm = bmesh.new()
    verts = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        ring = []
        for j in range(rseg):
            b = 2 * math.pi * j / rseg
            d = R + r * math.cos(b)
            ring.append(bm.verts.new((d * math.cos(a), d * math.sin(a), r * math.sin(b))))
        verts.append(ring)
    for i in range(seg):
        for j in range(rseg):
            a, b = verts[i][j], verts[(i + 1) % seg][j]
            c, d = verts[(i + 1) % seg][(j + 1) % rseg], verts[i][(j + 1) % rseg]
            bm.faces.new((a, b, c, d))
    return _finish(bm, name, col, mat, loc, rot, smooth=True, parent=parent)

def ico(name, col, mat, r, sub=2, loc=(0, 0, 0), parent=None, smooth=False):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=r)
    return _finish(bm, name, col, mat, loc, smooth=smooth, parent=parent)

def empty(name, col, loc=(0, 0, 0), rot=(0, 0, 0), parent=None):
    o = bpy.data.objects.new(name, None)
    col.objects.link(o); o.location = loc; o.rotation_euler = rot
    if parent: o.parent = parent
    return o

# Loop animation: FRAMES frames over the loop, frame FRAMES-1 equals frame 0 (the game's verta files work the same way).
LOOP_SECONDS = 4
FRAMES = 30 * LOOP_SECONDS + 1

def key_linear(o, path, values):
    # values: list of (frame, value) pairs for a property path like 'rotation_euler'
    for f, v in values:
        setattr(o, path, v)
        o.keyframe_insert(path, frame=f)
    if o.animation_data and o.animation_data.action:
        act = o.animation_data.action
        curves = []
        try:
            curves = list(act.fcurves)
        except AttributeError:  # layered actions (Blender 4.4+)
            for layer in act.layers:
                for strip in layer.strips:
                    for bag in strip.channelbags:
                        curves += list(bag.fcurves)
        for fc in curves:
            for k in fc.keyframe_points: k.interpolation = 'LINEAR'
