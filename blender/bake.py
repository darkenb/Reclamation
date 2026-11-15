# Textures for a building in the game's style: one shared UV atlas over all its parts, clean palette colours, panel
# grooves carried by a normal map (no dirt), and Cycles bakes of albedo x ambient occlusion, normal, metallic/smoothness
# and emission. Writes <name>_a.png, _n.png, _s.png, _e.png beside the model file (src/Model.cs reads them).
# Run after converter.py / incinerator.py: prepare(), then bake() in separate calls (each bake takes a while).
import os
import numpy as np
exec(open(os.path.join(KIT_DIR, 'kit.py'), encoding='utf-8').read())

UV = 'BakeUV'

# Panel grooves, only on the parts that are built from plates: part -> (plate size along x, y, z in metres, 0 = no
# grooves across that axis; where a groove runs, in model coordinates, None = the part's own lower edge). Grooves follow
# the part's geometry (world position and face direction), not the UV atlas. Port slots sit in the middle of a plate.
SEAMS = {
    'Housing':      ((0.8, 0.8, 0), (None, None, None)),
    'Body':         ((2.2, 1.0, 1.1), (0.475, 0.5, None)),   # front: grooves at the fire mouth's sides
}
OFFSET = {'TS_Converter': 0.0, 'TS_Incinerator': 8.0}  # where each model stands in the Blender scene (x)
GROOVE = 0.014  # half width of a groove, metres

def mark_seams(col_name):
    for o in parts(col_name):
        for k in ('seams', 'seam_origin'):
            if k in o: del o[k]
        if o.name not in SEAMS: continue
        size, at = SEAMS[o.name]
        low = [min((o.matrix_world @ Vector(c))[i] for c in o.bound_box) for i in range(3)]
        o['seams'] = size
        o['seam_origin'] = [low[i] if at[i] is None else at[i] + (OFFSET[col_name] if i == 0 else 0) for i in range(3)]

def parts(col_name):
    return [o for o in bpy.data.collections[col_name].objects if o.type == 'MESH']

def prepare(col_name):
    """Apply modifiers and unwrap every part of the collection into one shared atlas."""
    scene = bpy.context.scene
    scene.frame_set(0)
    objs = parts(col_name)
    dg = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        if o.modifiers:
            me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
            o.modifiers.clear()
            old = o.data
            o.data = me
            if old.users == 0: bpy.data.meshes.remove(old)
        if UV not in o.data.uv_layers: o.data.uv_layers.new(name=UV)
        o.data.uv_layers.active = o.data.uv_layers[UV]
        o.data.uv_layers[UV].active_render = True
        bm = bmesh.new(); bm.from_mesh(o.data)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.to_mesh(o.data); bm.free()
    bpy.ops.object.mode_set(mode='OBJECT') if bpy.context.object and bpy.context.object.mode != 'OBJECT' else None
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    area = next(a for a in bpy.context.screen.areas if a.type == 'VIEW_3D')
    region = next(r for r in area.regions if r.type == 'WINDOW')
    with bpy.context.temp_override(area=area, region=region):
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004, scale_to_bounds=False)
        bpy.ops.uv.pack_islands(margin=0.004, rotate=True)
        bpy.ops.object.mode_set(mode='OBJECT')
    return {'parts': len(objs)}

def bake_material(name):
    """Turns a palette material into a procedural one for baking (the viewport colours stay the palette's)."""
    alb, met, smo, emi = PALETTE[name]
    m = material(name)
    nt = m.node_tree
    for n in list(nt.nodes):
        if n.type not in ('BSDF_PRINCIPLED', 'OUTPUT_MATERIAL'): nt.nodes.remove(n)
    b = nt.nodes['Principled BSDF']
    for i in ('Base Color', 'Roughness', 'Metallic', 'Normal'):
        for l in list(b.inputs[i].links): nt.links.remove(l)
    N = nt.nodes.new; L = nt.links.new
    tc = N('ShaderNodeTexCoord')
    # Barely-there tone variation between parts; the game's buildings are clean.
    tone = N('ShaderNodeTexNoise'); tone.inputs['Scale'].default_value = 1.5; tone.inputs['Detail'].default_value = 1
    L(tc.outputs['Object'], tone.inputs['Vector'])
    shade = N('ShaderNodeMapRange'); shade.inputs['To Min'].default_value = 0.97; shade.inputs['To Max'].default_value = 1.02
    L(tone.outputs['Fac'], shade.inputs['Value'])
    if not any(emi):
        # Panel grooves from the part's 'seams' / 'seam_origin' (mark_seams): planes across each axis every plate size,
        # drawn only on faces they cut (a face looking along an axis gets no grooves across that axis). Pressed in
        # through the normal map, with only a light line in the colour.
        def math(op, a, b=None):
            n = N('ShaderNodeMath'); n.operation = op
            for i, v in enumerate((a, b)):
                if v is None: continue
                if isinstance(v, (int, float)): n.inputs[i].default_value = v
                else: L(v, n.inputs[i])
            return n.outputs[0]
        geo = N('ShaderNodeNewGeometry')
        size = N('ShaderNodeAttribute'); size.attribute_type = 'OBJECT'; size.attribute_name = 'seams'
        origin = N('ShaderNodeAttribute'); origin.attribute_type = 'OBJECT'; origin.attribute_name = 'seam_origin'
        rel = N('ShaderNodeVectorMath'); rel.operation = 'SUBTRACT'
        L(geo.outputs['Position'], rel.inputs[0]); L(origin.outputs['Vector'], rel.inputs[1])
        q = N('ShaderNodeVectorMath'); q.operation = 'DIVIDE'  # by 0 gives 0
        L(rel.outputs[0], q.inputs[0]); L(size.outputs['Vector'], q.inputs[1])
        sq, ss, sn = N('ShaderNodeSeparateXYZ'), N('ShaderNodeSeparateXYZ'), N('ShaderNodeSeparateXYZ')
        L(q.outputs[0], sq.inputs[0]); L(size.outputs['Vector'], ss.inputs[0]); L(geo.outputs['Normal'], sn.inputs[0])
        groove = None
        for i in range(3):
            # distance to the nearest groove plane, metres
            d = math('MULTIPLY', math('ABSOLUTE', math('SUBTRACT', math('FRACT', math('ADD', sq.outputs[i], 0.5)), 0.5)), ss.outputs[i])
            g = N('ShaderNodeMapRange'); g.interpolation_type = 'SMOOTHSTEP'
            g.inputs['From Min'].default_value = 0.0; g.inputs['From Max'].default_value = GROOVE
            g.inputs['To Min'].default_value = 1.0; g.inputs['To Max'].default_value = 0.0
            L(d, g.inputs['Value'])
            cuts = math('LESS_THAN', math('ABSOLUTE', sn.outputs[i]), 0.5)
            on = math('MULTIPLY', math('GREATER_THAN', ss.outputs[i], 0.0), cuts)
            one = math('MULTIPLY', g.outputs['Result'], on)
            groove = one if groove is None else math('MAXIMUM', groove, one)
        height = N('ShaderNodeMath'); height.operation = 'SUBTRACT'; height.inputs[0].default_value = 1.0
        L(groove, height.inputs[1])
        bump = N('ShaderNodeBump'); bump.inputs['Strength'].default_value = 1.0; bump.inputs['Distance'].default_value = 0.02
        L(height.outputs[0], bump.inputs['Height'])
        L(bump.outputs['Normal'], b.inputs['Normal'])
        line = N('ShaderNodeMapRange'); line.inputs['To Min'].default_value = 0.78; line.inputs['To Max'].default_value = 1.0
        L(height.outputs[0], line.inputs['Value'])
        seam = N('ShaderNodeMath'); seam.operation = 'MULTIPLY'
        L(shade.outputs[0], seam.inputs[0]); L(line.outputs['Result'], seam.inputs[1])
        shade = seam
    col = N('ShaderNodeMix'); col.data_type = 'RGBA'; col.blend_type = 'MULTIPLY'
    col.inputs['Factor'].default_value = 1.0
    col.inputs[6].default_value = (*alb, 1)
    sv = N('ShaderNodeCombineColor')
    for i in range(3): L(shade.outputs[0], sv.inputs[i])
    L(sv.outputs[0], col.inputs[7])
    L(col.outputs[2], b.inputs['Base Color'])
    rough = N('ShaderNodeMapRange'); rough.inputs['From Min'].default_value = 0.78; rough.inputs['From Max'].default_value = 1.02
    rough.inputs['To Min'].default_value = min(1, 1 - smo + 0.15); rough.inputs['To Max'].default_value = 1 - smo
    L(shade.outputs[0], rough.inputs['Value'])
    L(rough.outputs['Result'], b.inputs['Roughness'])
    b.inputs['Metallic'].default_value = met
    b.inputs['Emission Strength'].default_value = 1.0 if any(emi) else 0.0
    return m

def target(m, image):
    nt = m.node_tree
    node = nt.nodes.get('BakeTarget') or nt.nodes.new('ShaderNodeTexImage')
    node.name = 'BakeTarget'
    node.image = image
    for n in nt.nodes: n.select = False
    node.select = True
    nt.nodes.active = node

def new_image(name, size, data):
    img = bpy.data.images.get(name)
    if img: bpy.data.images.remove(img)
    img = bpy.data.images.new(name, size, size, alpha=False, float_buffer=False)
    img.colorspace_settings.name = 'Non-Color' if data else 'sRGB'
    return img

def bake(col_name, kind, size=2048, samples=32):
    """kind: 'albedo', 'ao', 'normal', 'rough', 'metal', 'emit'. Each writes an image named <collection>_<kind>."""
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = samples
    scene.cycles.device = 'CPU'
    scene.render.bake.margin = 6
    scene.frame_set(0)
    objs = parts(col_name)
    mark_seams(col_name)
    names = sorted({s.material.name for o in objs for s in o.material_slots if s.material})
    mats = [bake_material(n) for n in names]
    img = new_image(f'{col_name}_{kind}', size, kind in ('ao', 'normal', 'rough', 'metal'))
    saved = {}
    for m in mats:
        target(m, img)
        b = m.node_tree.nodes['Principled BSDF']
        # The diffuse colour pass is base colour x (1 - metallic): bake the colour itself (bake_material sets it back).
        if kind == 'albedo': b.inputs['Metallic'].default_value = 0.0
        if kind == 'metal':  # no metallic pass: show metallic as emission for the bake
            saved[m.name] = (tuple(b.inputs['Emission Color'].default_value), b.inputs['Emission Strength'].default_value)
            v = b.inputs['Metallic'].default_value
            b.inputs['Emission Color'].default_value = (v, v, v, 1); b.inputs['Emission Strength'].default_value = 1.0
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for o in objs:
        o.select_set(True)
        o.data.uv_layers.active = o.data.uv_layers[UV]
    bpy.context.view_layer.objects.active = objs[0]
    if kind == 'albedo': bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, use_clear=True, margin=6)
    elif kind == 'ao': bpy.ops.object.bake(type='AO', use_clear=True, margin=6)
    elif kind == 'normal': bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', use_clear=True, margin=6)
    elif kind == 'rough': bpy.ops.object.bake(type='ROUGHNESS', use_clear=True, margin=6)
    elif kind in ('metal', 'emit'): bpy.ops.object.bake(type='EMIT', use_clear=True, margin=6)
    for m in mats:
        if m.name in saved:
            b = m.node_tree.nodes['Principled BSDF']
            b.inputs['Emission Color'].default_value, b.inputs['Emission Strength'].default_value = saved[m.name]
    return {'image': img.name}

def pixels(name):
    img = bpy.data.images[name]
    a = np.empty(len(img.pixels), dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)

def write(col_name, out_dir, file_name, ao_strength=0.5):
    """Packs the bakes into the three files the mod reads (rows bottom-up, as Unity reads PNGs)."""
    alb, ao = pixels(f'{col_name}_albedo'), pixels(f'{col_name}_ao')
    rough, metal, emit = pixels(f'{col_name}_rough'), pixels(f'{col_name}_metal'), pixels(f'{col_name}_emit')
    nor = pixels(f'{col_name}_normal')
    a = alb.copy()
    occl = 1 - ao_strength * (1 - ao[..., :1])
    # albedo is stored sRGB: darken in linear light, then back to sRGB
    lin = np.where(a[..., :3] <= 0.04045, a[..., :3] / 12.92, ((a[..., :3] + 0.055) / 1.055) ** 2.4) * occl
    a[..., :3] = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(np.clip(lin, 0, None), 1 / 2.4) - 0.055)
    a[..., 3] = 0
    s = np.zeros_like(alb)
    s[..., 0] = metal[..., 0]; s[..., 1] = 1; s[..., 3] = 1 - rough[..., 0]
    e = emit.copy(); e[..., 3] = 0
    # The game's normal maps hold x in A and y in G. The exporter's swap of y and z mirrors the model, which turns the
    # bitangent around, so y is flipped here.
    n = np.ones_like(nor)
    n[..., 1] = n[..., 2] = 1 - nor[..., 1]; n[..., 3] = nor[..., 0]
    saved = []
    for suffix, data, colorspace in (('a', a, 'sRGB'), ('n', n, 'Non-Color'), ('s', s, 'Non-Color'), ('e', e, 'sRGB')):
        h, w = data.shape[:2]
        old = bpy.data.images.get(f'out_{suffix}')
        if old: bpy.data.images.remove(old)
        img = bpy.data.images.new(f'out_{suffix}', w, h, alpha=True)
        img.colorspace_settings.name = colorspace
        img.pixels.foreach_set(np.clip(data, 0, 1).ravel())
        path = os.path.join(out_dir, f'{file_name}_{suffix}.png')
        img.filepath_raw = path; img.file_format = 'PNG'
        img.save()
        saved.append(os.path.basename(path))
    return {'saved': saved}
