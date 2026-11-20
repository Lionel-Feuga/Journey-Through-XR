"""Génère Sabre_Griffes.blend selon le cahier des charges des modèles fournis (version 2, qualité d'abord).
Usage : python build_sabre_griffes.py <sortie.blend> [--render <image.png>]
Repère : lame vers +Z, commandes (bouton) vers +Y, mètres. Cotes du croquis en cm depuis la base du pommeau."""
import bpy, bmesh, math, sys
from mathutils import Vector, Matrix, Quaternion

OUT = sys.argv[1]
RENDER = sys.argv[sys.argv.index("--render") + 1] if "--render" in sys.argv else None
Z0 = 13.95                       # origine : 4,5 cm au-dessus du centre de la zone tenue (6,6 à 12,3)
def Z(z): return (z - Z0) / 100.0
def R(r): return r / 100.0
SEG = 96
UP = math.pi / 2                 # +Y : côté commandes
TAU = 2 * math.pi

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# --- Matériaux (7, Principled BSDF sans texture, couleurs linéaires) ---
def mat(name, color, metallic, rough, emit=0.0):
    m = bpy.data.materials.new(name)
    if hasattr(m, "use_nodes"): m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (*color, 1)
        b.inputs["Emission Strength"].default_value = emit
    return m
M = {
    "Acier_Vieilli": mat("Acier_Vieilli", (0.40, 0.40, 0.41), 1.0, 0.22),
    "Chrome_Poli":   mat("Chrome_Poli", (0.86, 0.86, 0.88), 1.0, 0.06),
    "Noir_Brillant": mat("Noir_Brillant", (0.02, 0.02, 0.022), 0.9, 0.16),
    "Noir_Mat":      mat("Noir_Mat", (0.008, 0.008, 0.008), 0.2, 0.5),
    "Cuir_Noir":     mat("Cuir_Noir", (0.012, 0.011, 0.011), 0.0, 0.55),
    "Couture_Rouge": mat("Couture_Rouge", (0.5, 0.008, 0.012), 0.0, 0.5),
    "Rouge_Emissif": mat("Rouge_Emissif", (1.0, 0.012, 0.006), 0.0, 0.4, emit=1.8),
}

root = bpy.data.objects.new("Saber_Root", None); scene.collection.objects.link(root)
root.empty_display_size = 0.05
pieces = []

def add(name, bm, material, bevel=0.0006, segs=3):
    for f in bm.faces: f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(M[material])
    ob = bpy.data.objects.new(name, me); scene.collection.objects.link(ob); ob.parent = root
    if bevel:
        bv = ob.modifiers.new("Bevel", 'BEVEL'); bv.width = bevel; bv.segments = segs
        bv.limit_method = 'ANGLE'; bv.angle_limit = math.radians(30); bv.harden_normals = True
    wn = ob.modifiers.new("WeightedNormal", 'WEIGHTED_NORMAL'); wn.keep_sharp = True
    pieces.append(ob); return ob

def polar(r, ang, z):
    return Vector((R(r) * math.cos(ang), R(r) * math.sin(ang), Z(z)))

def lathe(name, prof, material, seg=SEG, close_bottom=False, close_top=False, **kw):
    bm = bmesh.new()
    rings = [[bm.verts.new(polar(r, TAU * i / seg, z)) for i in range(seg)] for r, z in prof]
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg; bm.faces.new((a[i], a[j], b[j], b[i]))
    if close_bottom: bm.faces.new(list(reversed(rings[0])))
    if close_top: bm.faces.new(rings[-1])
    return add(name, bm, material, **kw)

def ring(name, r_in, r_out, z0, z1, material, lip=0.04, **kw):
    """Bague pleine, arêtes adoucies par le chanfrein."""
    return lathe(name, [(r_in, z0), (r_out, z0 + lip), (r_out, z1 - lip), (r_in, z1)], material, **kw)

def cyl(name, r, z0, z1, material, **kw):
    return lathe(name, [(r, z0), (r, z1)], material, close_bottom=True, close_top=True, **kw)

def loft(name, sections, material, cap=True, **kw):
    bm = bmesh.new()
    rings = [[bm.verts.new(p) for p in s] for s in sections]
    n = len(sections[0])
    for a, b in zip(rings, rings[1:]):
        for i in range(n):
            j = (i + 1) % n; bm.faces.new((a[i], a[j], b[j], b[i]))
    if cap:
        bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    return add(name, bm, material, **kw)

def curved_panel(name, r_in, r_out, a0, a1, z0, z1, material, n=10, **kw):
    """Plaque qui épouse le cylindre (fenêtre, nervure, cadre)."""
    secs = []
    for z in (z0, z1):
        inner = [polar(r_in, a0 + (a1 - a0) * i / n, z) for i in range(n + 1)]
        outer = [polar(r_out, a0 + (a1 - a0) * i / n, z) for i in reversed(range(n + 1))]
        secs.append(inner + outer)
    return loft(name, secs, material, **kw)

def framed_window(name, r_surf, ang, half, z0, z1, frame=0.12):
    """Fenêtre rouge en retrait, entourée d'un cadre en acier."""
    da = R(frame) / R(r_surf)
    curved_panel(name + "_Glow", r_surf - 0.02, r_surf + 0.025, ang - half, ang + half, z0, z1, "Rouge_Emissif", bevel=0.0003, segs=2)
    curved_panel(name + "_FrameL", r_surf - 0.05, r_surf + 0.09, ang - half - da, ang - half, z0 - frame, z1 + frame, "Acier_Vieilli", n=2)
    curved_panel(name + "_FrameR", r_surf - 0.05, r_surf + 0.09, ang + half, ang + half + da, z0 - frame, z1 + frame, "Acier_Vieilli", n=2)
    curved_panel(name + "_FrameB", r_surf - 0.05, r_surf + 0.09, ang - half, ang + half, z0 - frame, z0, "Acier_Vieilli", n=8)
    curved_panel(name + "_FrameT", r_surf - 0.05, r_surf + 0.09, ang - half, ang + half, z1, z1 + frame, "Acier_Vieilli", n=8)

def helix(name, z0, z1, pitch, phase, profile, material, sign=1, steps_per_turn=96, **kw):
    """Ruban en spirale. profile : liste (rayon cm, décalage z cm) formant une section fermée."""
    turns = (z1 - z0) / pitch
    n = max(12, int(turns * steps_per_turn))
    secs = []
    for k in range(n + 1):
        t = k / n
        ang = phase + sign * TAU * turns * t
        zc = z0 + (z1 - z0) * t
        secs.append([polar(r, ang, zc + dz) for r, dz in profile])
    return loft(name, secs, material, **kw)

def facets(name, r_base, r_top, z0, z1, count, material, phase=0.0, alternate=True, **kw):
    """Triangles en relief autour d'une bague (évoque les plaques gravées)."""
    half = math.pi / count
    for k in range(count):
        a = phase + k * TAU / count
        up = (k % 2 == 0) or not alternate
        zb, zt = (z0, z1) if up else (z1, z0)
        base = [polar(r_base, a - half * 0.92, zb), polar(r_base, a, zt), polar(r_base, a + half * 0.92, zb)]
        cz = (2 * zb + zt) / 3
        top = [polar(r_top, a - half * 0.62, zb + (cz - zb) * 0.35), polar(r_top, a, zt + (cz - zt) * 0.35), polar(r_top, a + half * 0.62, zb + (cz - zb) * 0.35)]
        if not up:
            base.reverse(); top.reverse()
        loft(f"{name}_{k}", [base, top], material, bevel=0.0004, segs=2)

def screw(name, ang, z, r_surf):
    """Vis bombée posée sur la surface."""
    bm = bmesh.new()
    prof = [(0.0, 0.0), (0.17, 0.0), (0.17, 0.05), (0.13, 0.10), (0.0, 0.12)]
    seg = 20
    rings = [[bm.verts.new((R(r) * math.cos(TAU * i / seg), R(r) * math.sin(TAU * i / seg), R(h))) for i in range(seg)] for r, h in prof]
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg; bm.faces.new((a[i], a[j], b[j], b[i]))
    rot = Matrix.Rotation(ang, 4, 'Z') @ Matrix.Rotation(math.pi / 2, 4, 'Y')
    bmesh.ops.transform(bm, matrix=Matrix.Translation(polar(r_surf - 0.02, ang, z)) @ rot, verts=bm.verts)
    return add(name, bm, "Chrome_Poli", bevel=0)

# ======================= Pommeau (0 à 5,4) =======================
lathe("Pommel_Core", [(0.0, 0.35), (0.7, 0.35), (0.7, 0.2), (1.25, 0.2), (1.5, 0.0), (1.66, 0.25), (1.66, 5.0)], "Noir_Brillant", close_top=True)
lathe("Pommel_Red_Ring", [(0.72, 0.24), (1.18, 0.24), (1.18, 0.30), (0.72, 0.30)], "Rouge_Emissif", bevel=0)
cyl("Pommel_Cap", 0.62, 0.12, 0.34, "Noir_Mat")
ring("Pommel_Cap_Ring", 0.6, 0.72, 0.08, 0.36, "Chrome_Poli")
for k in range(4):
    a = UP + math.pi / 4 + k * math.pi / 2
    framed_window(f"Pommel_Win_{k}", 1.66, a, 0.25, 1.1, 4.2)
    # griffe facettée à arête centrale
    c = a + math.pi / 4
    tv = Vector((-math.sin(c), math.cos(c), 0))
    secs = []
    for z, ri, ro, w in ((-0.6, 1.35, 1.52, 0.12), (0.1, 1.5, 2.05, 0.55), (0.9, 1.55, 2.3, 0.9), (2.5, 1.58, 2.22, 0.95), (4.3, 1.58, 2.08, 0.9), (5.15, 1.58, 1.82, 0.6)):
        hw = R(w / 2)
        i0, o0 = polar(ri, c, z), polar(ro, c, z)
        sh = polar(ri + (ro - ri) * 0.55, c, z)
        secs.append([i0 - tv * hw, sh - tv * hw * 0.8, o0, sh + tv * hw * 0.8, i0 + tv * hw])
    loft(f"Pommel_Claw_{k}", secs, "Acier_Vieilli", bevel=0.0007)
ring("Pommel_Collar", 1.6, 1.92, 4.95, 5.4, "Acier_Vieilli")
ring("Pommel_Collar_Lip", 1.88, 1.98, 5.05, 5.22, "Chrome_Poli", lip=0.02)

# ======================= Bagues (5,4 à 6,6) =======================
cyl("Rings_Core", 1.6, 5.4, 6.6, "Noir_Brillant")
for i, (z0, z1, rr, m) in enumerate(((5.45, 5.72, 1.8, "Chrome_Poli"), (5.8, 5.98, 1.72, "Acier_Vieilli"), (6.06, 6.33, 1.8, "Chrome_Poli"), (6.38, 6.56, 1.7, "Acier_Vieilli"))):
    ring(f"Rings_{i}", 1.58, rr, z0, z1, m, lip=0.03)

# ======================= Poignée cuir (6,6 à 12,3) =======================
cyl("Grip_Core", 1.5, 6.55, 12.35, "Noir_Mat", bevel=0)
leather = [(1.50, -0.44), (1.62, -0.42), (1.69, -0.33), (1.71, -0.15), (1.71, 0.15), (1.69, 0.33), (1.62, 0.42), (1.50, 0.44)]
helix("Grip_Leather", 6.95, 11.95, 0.95, 0.0, leather, "Cuir_Noir", bevel=0)
stitch = [(1.69, -0.035), (1.745, -0.03), (1.76, 0.0), (1.745, 0.03), (1.69, 0.035)]
helix("Grip_Stitch_A", 7.0, 11.9, 2.45, 0.4, stitch, "Couture_Rouge", bevel=0, steps_per_turn=120)
helix("Grip_Stitch_B", 7.0, 11.9, 2.45, 0.4, stitch, "Couture_Rouge", sign=-1, bevel=0, steps_per_turn=120)
ring("Grip_End_Low", 1.48, 1.74, 6.6, 6.95, "Acier_Vieilli")
ring("Grip_End_High", 1.48, 1.74, 11.95, 12.3, "Acier_Vieilli")

# ======================= Bague biseautée (12,3 à 14,5) =======================
lathe("Band1", [(1.6, 12.25), (1.9, 12.6), (1.93, 14.15), (1.6, 14.55)], "Acier_Vieilli", bevel=0.0008)
facets("Band1_Facet", 1.91, 2.02, 12.7, 14.05, 8, "Acier_Vieilli", phase=UP)
ring("Band1_Lip_Low", 1.85, 2.0, 12.55, 12.68, "Chrome_Poli", lip=0.02)
ring("Band1_Lip_High", 1.85, 2.0, 14.07, 14.2, "Chrome_Poli", lip=0.02)

# ======================= Corps (14,5 à 17) =======================
cyl("Mid_Core", 1.66, 14.4, 17.1, "Noir_Brillant")
for k in range(4):
    framed_window(f"Mid_Win_{k}", 1.66, UP + math.pi / 4 + k * math.pi / 2, 0.22, 14.95, 16.55)
ring("Mid_Ring", 1.6, 1.78, 15.65, 15.85, "Chrome_Poli", lip=0.02)

# ======================= Bague en Z (17 à 20,7) =======================
cyl("Band2_Core", 1.7, 17.0, 20.7, "Noir_Brillant")
ring("Band2_Low", 1.65, 1.97, 16.95, 17.85, "Acier_Vieilli")
ring("Band2_High", 1.65, 1.97, 19.85, 20.75, "Acier_Vieilli")
facets("Band2_Low_Facet", 1.95, 2.04, 17.1, 17.7, 10, "Acier_Vieilli", phase=UP, alternate=False)
facets("Band2_High_Facet", 1.95, 2.04, 20.0, 20.6, 10, "Acier_Vieilli", phase=UP + math.pi / 10, alternate=False)
band = [(1.66, -0.45), (1.92, -0.45), (1.97, -0.38), (1.98, 0.0), (1.97, 0.38), (1.92, 0.45), (1.66, 0.45)]
helix("Band2_Diagonal", 17.85, 19.85, 4.0, UP, band, "Acier_Vieilli", bevel=0.0005)
edge = [(1.95, -0.06), (2.01, -0.05), (2.02, 0.0), (2.01, 0.05), (1.95, 0.06)]
helix("Band2_Diag_Edge_A", 17.85, 19.85, 4.0, UP, [(r, dz - 0.42) for r, dz in edge], "Chrome_Poli", bevel=0)
helix("Band2_Diag_Edge_B", 17.85, 19.85, 4.0, UP, [(r, dz + 0.42) for r, dz in edge], "Chrome_Poli", bevel=0)
for k in range(3):
    framed_window(f"Band2_Win_{k}", 1.7, UP + math.pi * 0.75 + k * 0.45, 0.15, 18.25, 19.45, frame=0.08)

# ======================= Tête (20,7 à 26) =======================
cyl("Head_Core", 1.72, 20.7, 26.0, "Noir_Brillant")
ring("Head_Ring_Low", 1.7, 1.88, 20.75, 21.2, "Chrome_Poli")
ring("Head_Ring_High", 1.7, 1.88, 25.5, 25.98, "Chrome_Poli")
for k in range(6):
    a = UP + math.pi / 6 + k * math.pi / 3
    framed_window(f"Head_Win_{k}", 1.72, a, 0.28, 22.5, 25.0)
    curved_panel(f"Head_Rib_{k}", 1.7, 1.86, a + math.pi / 6 - 0.09, a + math.pi / 6 + 0.09, 21.25, 25.45, "Noir_Brillant", n=4)
    screw(f"Head_Screw_{k}", a + math.pi / 6, 21.75, 1.86) if k != 2 else None
# bouton (côté +Y)
def button():
    bm = bmesh.new(); seg = 32
    prof = [(0.0, 0.0), (0.42, 0.0), (0.42, 0.10), (0.33, 0.12), (0.30, 0.25), (0.22, 0.33), (0.0, 0.35)]
    rings = [[bm.verts.new((R(r) * math.cos(TAU * i / seg), R(r) * math.sin(TAU * i / seg), R(h))) for i in range(seg)] for r, h in prof]
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg; bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.transform(bm, matrix=Matrix.Translation(polar(1.84, UP, 21.75)) @ Matrix.Rotation(-math.pi / 2, 4, 'X'), verts=bm.verts)
    return add("Button", bm, "Chrome_Poli", bevel=0)
button()

# ======================= Émetteur (26 à 28) =======================
lathe("Emitter_Body", [(1.7, 25.98), (1.74, 26.0), (1.74, 26.55), (1.62, 26.62), (1.62, 26.85), (1.74, 26.92), (1.74, 27.82), (1.62, 27.98), (1.34, 27.98), (1.34, 27.6)], "Acier_Vieilli")
for k in range(12):
    a = k * TAU / 12
    curved_panel(f"Emitter_Tooth_{k}", 1.7, 1.84, a - 0.13, a + 0.13, 27.0, 27.65, "Noir_Brillant", n=3)
    curved_panel(f"Emitter_Crown_{k}", 1.4, 1.62, a - 0.16, a + 0.16, 27.95, 28.18, "Acier_Vieilli", n=3)
lathe("Emitter_Red", [(0.98, 27.78), (1.33, 27.78), (1.33, 27.86), (0.98, 27.86)], "Rouge_Emissif", bevel=0)
cyl("Emitter_Bore", 0.98, 27.4, 27.92, "Noir_Mat")

# ======================= 3 crochets à 120° =======================
def bez(p0, p1, p2, t):
    return tuple((1 - t) ** 2 * a + 2 * t * (1 - t) * b + t ** 2 * c for a, b, c in zip(p0, p1, p2))
for k in range(3):
    ang = UP + k * TAU / 3
    tv = Vector((-math.sin(ang), math.cos(ang), 0))
    secs = []
    N = 64
    for i in range(N + 1):
        t = 0.985 * i / N
        ro, zo = bez((1.6, 21.7), (5.6, 23.6), (3.6, 29.5), t)
        ri, zi = bez((1.6, 24.7), (3.4, 25.0), (3.6, 29.5), t)
        w = R(0.75 * (1 - t) ** 0.8 + 0.06 * t)
        a0, b0 = polar(ri, ang, zi), polar(ro, ang, zo)
        def lerp(u): return a0 + (b0 - a0) * u
        secs.append([a0, lerp(0.18) - tv * w * 0.35, lerp(0.42) - tv * w * 0.5, lerp(0.75) - tv * w * 0.3, b0,
                     lerp(0.75) + tv * w * 0.3, lerp(0.42) + tv * w * 0.5, lerp(0.18) + tv * w * 0.35])
    loft(f"Hook_{k}", secs, "Acier_Vieilli", bevel=0.0004, segs=2)
    # selle de fixation sur la tête
    curved_panel(f"Hook_Saddle_{k}", 1.7, 1.98, ang - 0.32, ang + 0.32, 21.3, 25.2, "Acier_Vieilli", n=12, bevel=0.0007)
    screw(f"Hook_Screw_{k}", ang, 21.7, 1.98)

# --- Socket de lame ---
sock = bpy.data.objects.new("Blade_Socket", None); scene.collection.objects.link(sock)
sock.parent = root; sock.location = (0, 0, Z(27.9)); sock.empty_display_size = 0.03

base = 0
for o in pieces:
    o.data.calc_loop_triangles(); base += len(o.data.loop_triangles)
dg = bpy.context.evaluated_depsgraph_get()
ev = 0
for o in pieces:
    me = o.evaluated_get(dg).to_mesh(); me.calc_loop_triangles(); ev += len(me.loop_triangles); o.evaluated_get(dg).to_mesh_clear()
print("RESULT pieces:", len(pieces), "base tris:", base, "evaluated tris:", ev, "materials:", len(M))
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=OUT)

if RENDER:
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 160; scene.cycles.device = 'CPU'
    scene.render.resolution_x, scene.render.resolution_y = 1800, 1000
    w = bpy.data.worlds.new("W"); scene.world = w
    if hasattr(w, "use_nodes"): w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.03, 0.03, 0.033, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    scene.view_settings.view_transform = 'AgX'
    try: scene.view_settings.look = 'AgX - Medium High Contrast'
    except Exception: pass
    def look(obj, target, roll=0.0):
        q = (Vector(target) - obj.location).to_track_quat('-Z', 'Y'); obj.rotation_euler = (q @ Quaternion((0, 0, 1), roll)).to_euler()
    # boîtes à lumière de studio : invisibles à la caméra, mais reflétées par le chrome
    def softbox(loc, sx, sy, strength):
        bpy.ops.mesh.primitive_plane_add(size=1, location=loc); p = bpy.context.object
        p.scale = (sx, sy, 1); look(p, (0, 0, 0))
        m = bpy.data.materials.new("SB"); 
        if hasattr(m, "use_nodes"): m.use_nodes = True
        nt = m.node_tree; nt.nodes.clear()
        em = nt.nodes.new("ShaderNodeEmission"); em.inputs[1].default_value = strength
        out = nt.nodes.new("ShaderNodeOutputMaterial"); nt.links.new(em.outputs[0], out.inputs[0])
        p.data.materials.append(m); p.visible_camera = False
    softbox((0.0, -0.9, 0.55), 1.6, 0.18, 9)      # bandeau haut
    softbox((0.9, -0.2, 0.1), 0.25, 1.4, 6)       # bandeau latéral droit
    softbox((-0.8, -0.5, -0.2), 0.3, 1.2, 3)      # bandeau gauche
    softbox((0.0, 0.8, 0.4), 1.4, 0.6, 2.5)       # contre-jour
    softbox((0.0, -0.3, -0.9), 1.6, 1.6, 0.6)     # rebond bas
    # fond dégradé
    bpy.ops.mesh.primitive_plane_add(size=14, location=(0.6, 0.9, 0)); bg = bpy.context.object
    look(bg, (0, 0, 0)); bgm = bpy.data.materials.new("BG")
    if hasattr(bgm, "use_nodes"): bgm.use_nodes = True
    bgm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.06, 0.06, 0.065, 1)
    bgm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    bg.data.materials.append(bgm); bg.visible_glossy = False
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C")); scene.collection.objects.link(cam); scene.camera = cam
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 0.40
    cam.location = (0.55, -0.66, 0.30); look(cam, (0, 0, 0.012), math.radians(-52))
    scene.render.filepath = RENDER
    bpy.ops.render.render(write_still=True)
