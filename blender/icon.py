# Item icons in the game's style: the building seen from the front right and above, on a transparent background.
# render(collection, path): renders at 4x and averages down to the game's 80x80 icon size (src/Icons.cs loads it).
# Run after bake.py's bake materials are set up (the icon shows the same panels and glow as the game).
import os
import numpy as np
exec(open(os.path.join(KIT_DIR, 'kit.py'), encoding='utf-8').read())

SIZE, ICON = 320, 80
LOOSE = ('Ember',)  # moving bits caught mid-flight in a still picture (rising embers); left out of the icon
ELEVATION, AZIMUTH = math.radians(32), math.radians(38)  # camera above the ground, and round from the front (-y)

def render(col_name, path):
    scene = bpy.context.scene
    scene.frame_set(0)
    keep = {}
    for c in bpy.data.collections:
        if c.name.startswith('TS_'):
            keep[c.name] = c.hide_render
            c.hide_render = c.name != col_name
    objs = [o for o in bpy.data.collections[col_name].objects if o.type == 'MESH']
    loose = {o.name: o.hide_render for o in objs if o.name.startswith(LOOSE)}
    for name in loose: bpy.data.objects[name].hide_render = True
    objs = [o for o in objs if o.name not in loose]
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    centre = sum(pts, Vector()) / len(pts)
    look = Vector((math.sin(AZIMUTH) * math.cos(ELEVATION), -math.cos(AZIMUTH) * math.cos(ELEVATION), math.sin(ELEVATION)))

    cam_data = bpy.data.cameras.get('TS_IconCam') or bpy.data.cameras.new('TS_IconCam')
    cam_data.type = 'ORTHO'
    cam = bpy.data.objects.get('TS_IconCam') or bpy.data.objects.new('TS_IconCam', cam_data)
    if cam.name not in scene.collection.objects: scene.collection.objects.link(cam)
    cam.rotation_euler = (-look).to_track_quat('-Z', 'Y').to_euler()
    # fit: the building's corners seen from the camera, with a small margin
    inv = cam.rotation_euler.to_matrix().inverted()
    local = [inv @ (p - centre) for p in pts]
    xs, ys = [p.x for p in local], [p.y for p in local]
    mid = Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, 0))
    cam_data.ortho_scale = max(max(xs) - min(xs), max(ys) - min(ys)) * 1.06
    cam.location = centre + cam.rotation_euler.to_matrix() @ mid + look * 30
    cam_data.clip_end = 100

    sun_data = bpy.data.lights.get('TS_IconSun') or bpy.data.lights.new('TS_IconSun', 'SUN')
    sun_data.energy = 4.0; sun_data.angle = math.radians(8)
    sun = bpy.data.objects.get('TS_IconSun') or bpy.data.objects.new('TS_IconSun', sun_data)
    if sun.name not in scene.collection.objects: scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), 0, math.radians(-20))  # from the upper left front

    world = scene.world or bpy.data.worlds.new('World')
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get('Background')
    bg.inputs['Color'].default_value = (0.55, 0.6, 0.68, 1); bg.inputs['Strength'].default_value = 0.45

    r = scene.render
    saved = (scene.camera, r.engine, r.resolution_x, r.resolution_y, r.film_transparent, r.filepath,
             scene.view_settings.view_transform, scene.view_settings.look)
    scene.camera = cam
    r.engine = 'BLENDER_EEVEE'
    r.resolution_x = r.resolution_y = SIZE; r.resolution_percentage = 100
    r.film_transparent = True
    r.image_settings.file_format = 'PNG'; r.image_settings.color_mode = 'RGBA'
    scene.view_settings.view_transform = 'Standard'; scene.view_settings.look = 'Medium High Contrast'
    r.filepath = path
    bpy.ops.render.render(write_still=True)
    # 4x4 average with premultiplied alpha, so edges fade without a dark fringe
    img = bpy.data.images.load(path, check_existing=False)
    a = np.empty(SIZE * SIZE * 4, dtype=np.float32); img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    a = a.reshape(SIZE, SIZE, 4); a[..., :3] *= a[..., 3:]
    k = SIZE // ICON
    a = a.reshape(ICON, k, ICON, k, 4).mean(axis=(1, 3))
    a[..., :3] /= np.maximum(a[..., 3:], 1e-6)
    out = bpy.data.images.new('TS_Icon', ICON, ICON, alpha=True)
    out.pixels.foreach_set(np.clip(a, 0, 1).ravel())
    out.filepath_raw = path; out.file_format = 'PNG'; out.save()
    bpy.data.images.remove(out)
    (scene.camera, r.engine, r.resolution_x, r.resolution_y, r.film_transparent, r.filepath,
     scene.view_settings.view_transform, scene.view_settings.look) = saved
    for name, hidden in keep.items(): bpy.data.collections[name].hide_render = hidden
    for name, hidden in loose.items(): bpy.data.objects[name].hide_render = hidden
    return {'file': os.path.basename(path)}
