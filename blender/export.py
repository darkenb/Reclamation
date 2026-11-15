# Writes a collection as a .tsm model file the mod reads (src/Model.cs):
#   "TSM2", int vertexCount, staticCount, frameCount, indexCount, floorIndexCount, paletteCount
#   palette: paletteCount x 8 floats (albedo rgb, metallic, smoothness, emission rgb)
#   uv: vertexCount x 2 floats (the baked atlas when the parts have a BakeUV layer, else the palette swatch),
#   indices: indexCount ints (Unity winding); the last floorIndexCount of them are the floor (second sub-mesh), which
#   the mod draws with the game's foundation pattern: its uv is the ground position in pattern repeats (FLOOR_TILE).
#   static vertices once: staticCount x 9 floats (position, normal, tangent: the normal bake's, from the BakeUV
#   layer, else made up from the normal), then per frame the animated ones:
#   frameCount x (vertexCount - staticCount) x 9 floats.
# Space: Blender Z-up to Unity Y-up by swapping y and z, which mirrors, so each triangle's winding is reversed.
import os, struct
exec(open(os.path.join(KIT_DIR, 'kit.py'), encoding='utf-8').read())

NAMES = list(PALETTE)
FLOOR = 'BaseTrim'      # parts whose name starts with this are the floor
FLOOR_TILE = 2 * math.pi  # metres one repeat of the foundation pattern covers (5 plates of a 1.26 m grid cell)
GRID = 4  # swatches per row in the palette texture

def animated(o):
    while o is not None:
        if o.animation_data and o.animation_data.action: return True
        o = o.parent
    return False

def unity(v): return (v[0], v[2], v[1])

def tangent(n):
    t = n.cross(Vector((0, 0, 1)))
    if t.length < 1e-4: t = n.cross(Vector((1, 0, 0)))
    return t.normalized()

def build(o, depsgraph, offset, floor=False):
    """Vertices of one object: list of (key loop index list), plus per-vertex material swatch and triangles."""
    ev = o.evaluated_get(depsgraph)
    me = ev.to_mesh()
    me.calc_loop_triangles()
    m = ev.matrix_world
    normals = me.corner_normals
    baked = me.uv_layers.get('BakeUV')
    keys, index, tris, uvs = [], {}, [], []
    for lt in me.loop_triangles:
        mat = o.material_slots[lt.material_index].material.name if o.material_slots else 'Frame'
        s = NAMES.index(mat.split('.')[0])
        tri = []
        for li in lt.loops:
            n = normals[li].vector
            if floor:
                p = m @ me.vertices[me.loops[li].vertex_index].co - offset
                uv = (p.x / FLOOR_TILE, p.y / FLOOR_TILE)
            elif baked: uv = tuple(baked.data[li].uv)
            else: uv = (((s % GRID) + 0.5) / GRID, ((s // GRID) + 0.5) / GRID)
            k = (me.loops[li].vertex_index, round(n.x, 3), round(n.y, 3), round(n.z, 3), round(uv[0], 5), round(uv[1], 5))
            if k not in index:
                index[k] = len(keys); keys.append(li); uvs.append(uv)
            tri.append(index[k])
        tris.append(tri)
    ev.to_mesh_clear()
    return keys, tris, uvs

def sample(o, depsgraph, keys, offset):
    ev = o.evaluated_get(depsgraph)
    me = ev.to_mesh()
    baked = 'BakeUV' in me.uv_layers
    if baked: me.calc_tangents(uvmap='BakeUV')
    normals = me.corner_normals
    m = ev.matrix_world
    rot = m.to_3x3().normalized()
    out = []
    for li in keys:
        p = m @ me.vertices[me.loops[li].vertex_index].co - offset
        n = (rot @ normals[li].vector).normalized()
        t = (rot @ me.loops[li].tangent).normalized() if baked else tangent(n)
        out += [*unity(p), *unity(n), *unity(t)]
    ev.to_mesh_clear()
    return out

def export(col_name, path, offset=(0, 0, 0)):
    scene = bpy.context.scene
    offset = Vector(offset)
    col = bpy.data.collections[col_name]
    meshes = [o for o in col.objects if o.type == 'MESH']
    meshes.sort(key=lambda o: (animated(o), o.name))  # static ones first
    scene.frame_set(0)
    dg = bpy.context.evaluated_depsgraph_get()
    parts, uv, idx, floor_idx, base = [], [], [], [], 0
    for o in meshes:
        floor = o.name.startswith(FLOOR)
        keys, tris, uvs = build(o, dg, offset, floor)
        parts.append((o, keys))
        for u, v in uvs: uv += [u, v]
        for a, b, c in tris: (floor_idx if floor else idx).extend([base + a, base + c, base + b])
        base += len(keys)
    assert floor_idx, 'no floor part'
    static = [p for p in parts if not animated(p[0])]
    moving = [p for p in parts if animated(p[0])]
    stat = []
    for o, keys in static: stat += sample(o, dg, keys, offset)
    frames = []
    for f in range(FRAMES):
        scene.frame_set(f)
        dg = bpy.context.evaluated_depsgraph_get()
        for o, keys in moving: frames += sample(o, dg, keys, offset)
    scene.frame_set(0)
    V = base
    S = len(stat) // 9
    with open(path, 'wb') as w:
        w.write(b'TSM2')
        w.write(struct.pack('<6i', V, S, FRAMES, len(idx) + len(floor_idx), len(floor_idx), len(NAMES)))
        for name in NAMES:
            alb, met, smo, emi = PALETTE[name]
            w.write(struct.pack('<8f', *alb, met, smo, *emi))
        w.write(struct.pack(f'<{len(uv)}f', *uv))
        w.write(struct.pack(f'<{len(idx) + len(floor_idx)}i', *idx, *floor_idx))
        w.write(struct.pack(f'<{len(stat)}f', *stat))
        w.write(struct.pack(f'<{len(frames)}f', *frames))
    return {'file': os.path.basename(path), 'vertices': V, 'static': S, 'animated': V - S, 'triangles': len(idx) // 3, 'floor_triangles': len(floor_idx) // 3,
            'bytes': os.path.getsize(path)}
