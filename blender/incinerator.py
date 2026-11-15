# Incinerator: fits the thermal power plant's footprint (its collider: x -2.55..1.35, y -3.9..4.4, off centre) and its
# five sorter slots: three on the left wall (x -2.12, y -1, 0, 1), two on the right (x 0.87, y 0, 1), each with a port.
# Trash drops through the hopper into a furnace with a glowing grate; two chimneys at the back, a flywheel on the right.
# Loop: flywheel turns, two pistons pump.
import os
exec(open(os.path.join(KIT_DIR, 'kit.py'), encoding='utf-8').read())

col = collection('TS_Incinerator')
scene = bpy.context.scene
scene.frame_start, scene.frame_end = 0, FRAMES - 1
scene.render.fps = 30
OX = 8.0  # shown beside the converter in Blender; the exporter moves it back to the origin
def at(x, y, z): return (x + OX, y, z)

L, R = -2.2, 0.95          # furnace walls, where the slots are
CX = (L + R) / 2           # furnace centre line
box('Base', col, 'Frame', (3.9, 8.1, 0.25), at(-0.6, 0.25, 0.125), bevel=0.04)
box('BaseTrim', col, 'Dark', (3.6, 7.8, 0.1), at(-0.6, 0.25, 0.3))  # floor: the game's foundation pattern (export.py)

# Furnace body, roof and the glowing mouth with its grate.
box('Body', col, 'Body', (R - L, 4.2, 2.2), at(CX, -0.3, 1.4), bevel=0.06)
box('Roof', col, 'Frame', (R - L + 0.2, 4.4, 0.18), at(CX, -0.3, 2.55), bevel=0.03)
box('Mouth', col, 'Dark', (2.2, 0.08, 1.3), at(CX, -2.42, 1.05))
box('Fire', col, 'GlowFire', (1.9, 0.06, 1.0), at(CX, -2.45, 1.0))
for i in range(6):
    box(f'Grate{i}', col, 'Frame', (0.08, 0.1, 1.2), at(CX - 0.85 + 0.34 * i, -2.5, 1.05))
box('MouthHood', col, 'Accent', (2.5, 0.35, 0.18), at(CX, -2.5, 1.78), bevel=0.02)
# Flame tongues between the grate bars, darker than the fire behind them (animated: they rise and fall).
flames = [box(f'Flame{i}', col, 'GlowEmber', (0.24, 0.02, 0.7), at(CX - 0.68 + 0.34 * i, -2.485, 0.8)) for i in range(5)]

# The walls are plates between ribs, under an accent band like the converter's; louvres on the roof.
box('BodyBand', col, 'Accent', (R - L + 0.04, 4.24, 0.08), at(CX, -0.3, 2.3))
for k, (x, y, d) in enumerate([(L, -0.5, -1), (L, 0.5, -1), (L, 1.5, -1), (R, 0.5, 1), (R, 1.5, 1)]):
    box(f'Rib{k}', col, 'Frame', (0.1, 0.14, 1.9), at(x + 0.03 * d, y, 1.3))
box('VentBox', col, 'Frame', (1.1, 0.85, 0.1), at(CX, 0.55, 2.69), bevel=0.02)
for i in range(3):
    box(f'Vent{i}', col, 'Dark', (0.9, 0.1, 0.04), at(CX, 0.3 + 0.25 * i, 2.75))

# Sorter ports on both walls.
for k, (x, y, d) in enumerate([(L, -1, -1), (L, 0, -1), (L, 1, -1), (R, 0, 1), (R, 1, 1)]):
    box(f'PortFrame{k}', col, 'Frame', (0.08, 0.46, 0.46), at(x + 0.02 * d, y, 0.7))
    box(f'Port{k}', col, 'Dark', (0.03, 0.32, 0.32), at(x + 0.06 * d, y, 0.7))
    box(f'PortGlow{k}', col, 'GlowEmber', (0.03, 0.32, 0.04), at(x + 0.06 * d, y, 0.92))

# Cooling fins at the front corners, each with an ember strip.
for s, x in ((-1, L), (1, R)):
    for i, y in enumerate((-2.1, -1.7)):
        box(f'Fin{s}{i}', col, 'Frame', (0.24, 0.12, 1.7), at(x + 0.1 * s, y, 1.25))
        box(f'FinGlow{s}{i}', col, 'GlowEmber', (0.03, 0.06, 1.3), at(x + 0.23 * s, y, 1.25))

# Hopper on the roof.
cyl('HopperNeck', col, 'Pipe', 0.35, 0.35, 0.25, seg=16, loc=at(CX, -1.1, 2.6))
cyl('Hopper', col, 'Body', 0.4, 0.95, 0.75, seg=32, loc=at(CX, -1.1, 2.8))
cyl('HopperMouth', col, 'Dark', 0.85, 0.85, 0.01, seg=32, loc=at(CX, -1.1, 3.55), smooth=False)
torus('HopperRim', col, 'Accent', 0.95, 0.05, seg=32, rseg=6, loc=at(CX, -1.1, 3.55))

# Chimneys at the back, joined to the furnace by pipes.
embers = []
for s in (-1, 1):
    x = CX + 0.85 * s
    box(f'ChimneyFoot{s}', col, 'Frame', (1.1, 1.1, 0.4), at(x, 3.1, 0.5), bevel=0.03)
    cyl(f'Chimney{s}', col, 'Pipe', 0.42, 0.34, 3.6, seg=20, loc=at(x, 3.1, 0.7))
    torus(f'ChimneyBand{s}', col, 'Accent', 0.4, 0.05, seg=20, rseg=6, loc=at(x, 3.1, 2.2))
    torus(f'ChimneyTop{s}', col, 'GlowEmber', 0.35, 0.05, seg=20, rseg=6, loc=at(x, 3.1, 4.3))
    cyl(f'ChimneyHole{s}', col, 'Dark', 0.29, 0.29, 0.01, seg=20, loc=at(x, 3.1, 4.3), smooth=False)
    strut(f'Duct{s}', col, 'Pipe', at(x, 1.8, 1.9), at(x, 2.75, 1.9), 0.2, seg=12)
    torus(f'ChimneyBandHigh{s}', col, 'Accent', 0.37, 0.05, seg=20, rseg=6, loc=at(x, 3.1, 3.4))
    # Ember rings rise out of each chimney and fade by shrinking (animated).
    for j in range(2):
        embers.append((torus(f'Ember{s}{j}', col, 'GlowEmber', 0.26, 0.035, seg=16, rseg=6, loc=at(x, 3.1, 4.25)), j / 2 + (0.25 if s > 0 else 0)))

# Flywheel on the right wall, in front of the ports (animated) on a fixed hub.
cyl('Hub', col, 'Frame', 0.22, 0.22, 0.27, seg=16, loc=at(R, -1.2, 1.3), rot=(0, math.radians(90), 0))
wheel = empty('WheelPivot', col, at(R + 0.27, -1.2, 1.3), (0, math.radians(90), 0))
wheel.rotation_mode = 'ZYX'  # Z (the spin) first, then the 90 degree turn that stands the wheel up
torus('Wheel', col, 'Accent', 0.6, 0.07, seg=28, rseg=6, parent=wheel)
for k in range(6):
    a = 2 * math.pi * k / 6
    box(f'Spoke{k}', col, 'Frame', (0.58, 0.07, 0.07), (0.29 * math.cos(a), 0.29 * math.sin(a), 0), (0, 0, a), parent=wheel)
cyl('WheelHub', col, 'Pipe', 0.12, 0.12, 0.12, seg=12, loc=(0, 0, -0.06), parent=wheel)

# Pistons on the roof (animated rods in fixed sleeves).
rods = []
for k, s in enumerate((-1, 1)):
    x, y = CX + 0.9 * s, 0.9
    cyl(f'Sleeve{k}', col, 'Frame', 0.18, 0.18, 0.35, seg=12, loc=at(x, y, 2.64))
    rods.append(cyl(f'Rod{k}', col, 'Pipe', 0.1, 0.1, 0.6, seg=8, loc=at(x, y, 2.7)))
    box(f'RodCap{k}', col, 'Accent', (0.3, 0.3, 0.08), (0, 0, 0.6), parent=rods[-1])

last = FRAMES - 1
key_linear(wheel, 'rotation_euler', [(0, (0, math.radians(90), 0)), (last, (0, math.radians(90), 2 * 2 * math.pi))])
steps = 24
for k, rod in enumerate(rods):
    x, y, z = rod.location
    key_linear(rod, 'location', [(round(last * i / steps), (x, y, z + 0.18 * (0.5 - 0.5 * math.cos(2 * math.pi * (2 * i / steps + k / 2))))) for i in range(steps + 1)])
for i, f in enumerate(flames):
    x, y, z = f.location
    turns = 2 + i % 2  # whole cycles per loop
    key_linear(f, 'location', [(round(last * j / steps), (x, y, z + 0.14 * math.sin(2 * math.pi * (turns * j / steps + i * 0.37)))) for j in range(steps + 1)])
# Keyed every frame: the jump back down happens within one frame, while the ring is at its smallest.
for ring, phase in embers:
    x, y, z = ring.location
    t = [(fr / last + phase) % 1 for fr in range(last)] + [phase % 1]
    key_linear(ring, 'location', [(fr, (x, y, z + 1.3 * v)) for fr, v in enumerate(t)])
    key_linear(ring, 'scale', [(fr, (1 - 0.95 * v,) * 3) for fr, v in enumerate(t)])

result = {'objects': len(col.objects)}
