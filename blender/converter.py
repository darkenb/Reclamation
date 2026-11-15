# Trash Converter: fits the assembler's 4 x 4 m footprint. A drum takes the trash in through four chutes; above it a
# Dark Fog core spins inside two tilted rings and the product leaves through the hopper on top. Loop: rings turn,
# the core bobs, four pistons pump in turn.
import os
exec(open(os.path.join(KIT_DIR, 'kit.py'), encoding='utf-8').read())

col = collection('TS_Converter')
scene = bpy.context.scene
scene.frame_start, scene.frame_end = 0, FRAMES - 1
scene.render.fps = 30

box('Base', col, 'Frame', (3.6, 3.6, 0.25), (0, 0, 0.125), bevel=0.04)
box('BaseTrim', col, 'Dark', (3.3, 3.3, 0.12), (0, 0, 0.31))  # floor: the game's foundation pattern (export.py)
for sx in (-1, 1):
    for sy in (-1, 1):
        box(f'Pillar{sx}{sy}', col, 'Frame', (0.36, 0.36, 2.1), (1.55 * sx, 1.55 * sy, 1.3), bevel=0.03)
        box(f'PillarGlow{sx}{sy}', col, 'GlowPurple', (0.08, 0.08, 1.4), (1.72 * sx, 1.72 * sy, 1.25))
        box(f'PillarCap{sx}{sy}', col, 'Accent', (0.48, 0.48, 0.16), (1.55 * sx, 1.55 * sy, 2.42), bevel=0.02)
for s in (-1, 1):
    box(f'BeamX{s}', col, 'Frame', (2.8, 0.24, 0.24), (0, 1.55 * s, 2.3), bevel=0.02)
    box(f'BeamY{s}', col, 'Frame', (0.24, 2.8, 0.24), (1.55 * s, 0, 2.3), bevel=0.02)
# The hopper hangs on four struts from the pillar tops, leaving the core in view.
for sx in (-1, 1):
    for sy in (-1, 1):
        strut(f'Strut{sx}{sy}', col, 'Frame', (1.45 * sx, 1.45 * sy, 2.36), (0.34 * sx, 0.34 * sy, 2.66), 0.07)

# Intake housing: its faces stand where the sorters reach in (the assembler's 12 slots: 1.1 m out, at -1, 0, 1 along each
# side), each slot with its own port.
box('Housing', col, 'Body', (2.4, 2.4, 0.8), (0, 0, 0.77), bevel=0.05)
box('HousingBand', col, 'Accent', (2.44, 2.44, 0.08), (0, 0, 1.08))
cyl('DrumTop', col, 'Frame', 1.1, 0.82, 0.2, seg=32, loc=(0, 0, 1.17))
for side in range(4):
    rz = math.pi / 2 * side
    c, s_ = math.cos(rz), math.sin(rz)
    for k, along in enumerate((-1.0, 0.0, 1.0)):
        def put(d, a):  # d: out from the centre, a: along the side
            return (d * c - a * s_, d * s_ + a * c)
        x, y = put(1.2, along)
        box(f'PortFrame{side}{k}', col, 'Frame', (0.08, 0.42, 0.42), (x, y, 0.66), (0, 0, rz))
        x, y = put(1.25, along)
        box(f'Port{side}{k}', col, 'Dark', (0.03, 0.3, 0.3), (x, y, 0.66), (0, 0, rz))
        box(f'PortGlow{side}{k}', col, 'GlowPurple', (0.03, 0.3, 0.04), (x, y, 0.86), (0, 0, rz))

# Core and rings (animated).
cyl('CoreSeat', col, 'Pipe', 0.32, 0.2, 0.22, seg=16, loc=(0, 0, 1.37))
core_pivot = empty('CorePivot', col, (0, 0, 1.78))
ico('Core', col, 'GlowCore', 0.38, sub=2, parent=core_pivot, smooth=True)
ring_a = empty('RingAPivot', col, (0, 0, 1.78))
torus('RingA', col, 'Accent', 0.78, 0.055, seg=24, rseg=6, rot=(math.radians(22), 0, 0), parent=ring_a)
ring_b = empty('RingBPivot', col, (0, 0, 1.78))
torus('RingB', col, 'GlowPurple', 0.98, 0.04, seg=24, rseg=6, rot=(0, math.radians(-28), 0), parent=ring_b)

# Three shards circle the core against the rings, at staggered heights.
shards = empty('ShardPivot', col, (0, 0, 1.78))
for k in range(3):
    a = 2 * math.pi * k / 3
    ico(f'Shard{k}', col, 'GlowPurple', 0.085, sub=1, loc=(0.56 * math.cos(a), 0.56 * math.sin(a), 0.14 * (k - 1)), parent=shards)

# Hopper on top.
cyl('HopperNeck', col, 'Pipe', 0.2, 0.36, 0.17, seg=32, loc=(0, 0, 2.29))
cyl('Hopper', col, 'Body', 0.36, 0.95, 0.5, seg=32, loc=(0, 0, 2.45))
cyl('HopperMouth', col, 'Dark', 0.86, 0.86, 0.01, seg=32, loc=(0, 0, 2.95), smooth=False)
torus('HopperRim', col, 'Accent', 0.95, 0.05, seg=32, rseg=6, loc=(0, 0, 2.95))

# Pistons between the pillars (animated rods in fixed sleeves).
pistons = []
for k in range(4):
    a = math.radians(45 + 90 * k)
    x, y = 0.95 * math.cos(a), 0.95 * math.sin(a)
    cyl(f'Sleeve{k}', col, 'Frame', 0.13, 0.13, 0.3, seg=10, loc=(x, y, 1.17))
    pistons.append(cyl(f'Rod{k}', col, 'Pipe', 0.07, 0.07, 0.55, seg=8, loc=(x, y, 1.3)))

# Animation keys: whole turns so the last frame matches the first.
last = FRAMES - 1
key_linear(ring_a, 'rotation_euler', [(0, (0, 0, 0)), (last, (0, 0, 2 * math.pi))])
key_linear(ring_b, 'rotation_euler', [(0, (0, 0, 0)), (last, (0, 0, -2 * math.pi))])
key_linear(shards, 'rotation_euler', [(0, (0, 0, 0)), (last, (0, 0, -2 * 2 * math.pi))])
steps = 24
key_linear(core_pivot, 'rotation_euler', [(0, (0, 0, 0)), (last, (0, 0, 2 * math.pi))])
key_linear(core_pivot, 'location', [(round(last * i / steps), (0, 0, 1.78 + 0.07 * math.sin(2 * math.pi * 2 * i / steps))) for i in range(steps + 1)])
for k, rod in enumerate(pistons):
    x, y, _ = rod.location
    key_linear(rod, 'location', [(round(last * i / steps), (x, y, 1.3 + 0.24 * max(0.0, math.sin(2 * math.pi * (i / steps - k / 4))))) for i in range(steps + 1)])

result = {'objects': len(col.objects)}
