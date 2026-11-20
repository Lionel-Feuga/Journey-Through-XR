"""Génère LaserSaber_Regent.blend (version 2, qualité) d'après le concept « Le Régent ».
Usage : python build_sabre_griffes.py <sortie.blend> [--render <image.png>]
Repère : lame vers +Z, commandes (bouton) vers +Y, mètres. Cotes du croquis en cm depuis la base du pommeau."""
import bpy, bmesh, math, sys
from mathutils import Vector, Matrix, Quaternion

OUT = sys.argv[1]
RENDER = sys.argv[sys.argv.index("--render") + 1] if "--render" in sys.argv else None
Z0 = 1.8                         # cotes du Régent d'origine ; origine 4,5 cm au-dessus du centre de la poignée (-9,4 à 4)
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
    "Gunmetal":      mat("Gunmetal", (0.24, 0.24, 0.25), 1.0, 0.30),
    "Bronze":        mat("Bronze", (0.55, 0.32, 0.17), 1.0, 0.28),
    "Grip_Noir":     mat("Grip_Noir", (0.012, 0.012, 0.013), 0.35, 0.42),
    "Noir_Panneau":  mat("Noir_Panneau", (0.010, 0.010, 0.011), 0.6, 0.25),
    "Acier_Poli":    mat("Acier_Poli", (0.55, 0.55, 0.56), 1.0, 0.14),
    "Rouge_Emissif": mat("Rouge_Emissif", (1.0, 0.012, 0.006), 0.0, 0.4, emit=1.8),
    "Noir_Mat":      mat("Noir_Mat", (0.006, 0.006, 0.006), 0.2, 0.6),
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


# ---------- Colonne vertébrale courbe (dans le plan YZ) ----------
K = 0.01875
def spine_y(z): return -K * (2.0 - z) ** 2 if z < 2.0 else 0.0
def spine_dy(z): return 2 * K * (2.0 - z) if z < 2.0 else 0.0
def frame(z):
    """Centre et axe « haut » de la section (perpendiculaire à la colonne)."""
    dy = spine_dy(z); n = math.sqrt(1 + dy * dy)
    return Vector((0, R(spine_y(z)), Z(z))), Vector((0, 1 / n, -dy / n))
def pt(z, u, v):
    c, up = frame(z)
    return c + Vector((R(u), 0, 0)) + up * R(v)

def octo(a, b, c):
    """Rectangle chanfreiné (demi-largeur a, demi-hauteur b, chanfrein c), sens trigonométrique."""
    return [(a, -b + c), (a, b - c), (a - c, b), (-a + c, b), (-a, b - c), (-a, -b + c), (-a + c, -b), (a - c, -b)]

def faceted(name, stations, material, slant0=0.0, slant1=0.0, **kw):
    """stations : (z, a, b, c[, dv]) ; slant : décalage z par cm de hauteur (coupes en biais) aux deux bouts."""
    secs = []
    n = len(stations)
    for i, st in enumerate(stations):
        z, a, b, c = st[:4]; dv = st[4] if len(st) > 4 else 0.0
        s = slant0 if i == 0 else (slant1 if i == n - 1 else 0.0)
        secs.append([pt(z + s * (v + dv), u, v + dv) for u, v in octo(a, b, c)])
    return loft(name, secs, material, **kw)

def band(name, z, a, b, c, width, slant, material, grow=0.05, **kw):
    """Liseré en biais (bronze) autour d'une section."""
    return faceted(name, [(z - width / 2, a + grow, b + grow, c), (z + width / 2, a + grow, b + grow, c)], material, slant0=slant, slant1=slant, **kw)

# ======================= Pommeau (-14,65 à -9,6) =======================
faceted("Pommel", [(-14.65, 1.15, 0.45, 0.3, -0.25), (-13.6, 1.45, 1.2, 0.45, -0.1), (-12.2, 1.72, 1.65, 0.55), (-10.6, 1.82, 1.82, 0.55), (-9.7, 1.82, 1.82, 0.55)], "Gunmetal", slant1=0.35, bevel=0.0008)
band("Pommel_Bronze", -9.75, 1.82, 1.82, 0.55, 0.22, 0.35, "Bronze")
# liseré bronze sur l'arête du dessous du pommeau
faceted("Pommel_Keel", [(-14.4, 0.12, 0.08, 0.03, -0.62), (-11.0, 0.12, 0.08, 0.03, -1.86)], "Bronze", bevel=0.0003)

# ======================= Poignée cannelée (-9,6 à 4) =======================
def fluted_grip():
    bm = bmesh.new(); SEGU = 192; FL = 26
    zs = [-10.5 + 15.6 * i / 78 for i in range(79)]
    rings = []
    for z in zs:
        t = (z - (-2.8)) / 6.8
        r0 = min(1.56 + 0.22 * t * t, 1.68)
        ring = []
        for i in range(SEGU):
            th = TAU * i / SEGU
            groove = abs(math.sin(FL * th / 2)) ** 0.6
            r = r0 - 0.075 * (1 - groove)
            ring.append(bm.verts.new(pt(z, r * math.cos(th), r * math.sin(th))))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for i in range(SEGU):
            j = (i + 1) % SEGU; bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    return add("Grip_Fluted", bm, "Grip_Noir", bevel=0)
fluted_grip()

# ======================= Collier et corps (4 à 11,8) =======================
band("Collar_Bronze", 4.15, 1.72, 1.92, 0.5, 0.22, 0.5, "Bronze")
faceted("Body", [(4.0, 1.72, 1.92, 0.5), (11.8, 1.75, 1.95, 0.5)], "Gunmetal", slant0=0.5, slant1=-0.3, bevel=0.0009)
faceted("Collar_Step", [(4.3, 1.80, 2.0, 0.52), (5.0, 1.80, 2.0, 0.52)], "Gunmetal", slant0=0.5, slant1=0.5, bevel=0.0008)
# panneau du bouton (côté +Y) : cadre noir, interrupteur bronze, repères « II » rouges
def topbox(name, z0, z1, hw, y0, y1, material, **kw):
    secs = [[pt(z, -hw, y0), pt(z, hw, y0), pt(z, hw, y1), pt(z, -hw, y1)] for z in (z0, z1)]
    return loft(name, secs, material, **kw)
topbox("Panel_Frame", 6.6, 10.9, 1.05, 1.80, 2.02, "Noir_Panneau", bevel=0.0006)
topbox("Switch_Base", 7.1, 9.3, 0.62, 1.95, 2.10, "Noir_Mat", bevel=0.0004)
topbox("Switch_A", 7.2, 8.15, 0.52, 2.0, 2.26, "Bronze", bevel=0.0006)
topbox("Switch_B", 8.25, 9.2, 0.52, 2.0, 2.20, "Bronze", bevel=0.0006)
for k, x in enumerate((-0.18, 0.18)):
    secs = [[pt(z, x - 0.06, 1.98), pt(z, x + 0.06, 1.98), pt(z, x + 0.06, 2.05), pt(z, x - 0.06, 2.05)] for z in (9.9, 10.6)]
    loft(f"Indicator_{k}", secs, "Rouge_Emissif", bevel=0)
# évents (côté +X, moitié basse), 3 fentes en biais
for k in range(3):
    zc = 9.0 + k * 0.55
    secs = []
    for dz in (-0.12, 0.12):
        secs.append([pt(zc + dz - 0.25, -1.80, -1.55), pt(zc + dz + 0.25, -1.80, -0.55), pt(zc + dz + 0.25, -1.70, -0.55), pt(zc + dz - 0.25, -1.70, -1.55)])
    loft(f"Vent_{k}", secs, "Noir_Mat", bevel=0.0003)
# arête bronze longitudinale sur le flanc
for sgn in (1, -1):
    secs = [[pt(z, sgn * 1.76, -0.9), pt(z, sgn * 1.80, -0.9), pt(z, sgn * 1.80, -0.78), pt(z, sgn * 1.76, -0.78)] for z in (5.4, 8.4)]
    loft(f"Side_Line_{sgn}", secs, "Bronze", bevel=0)

# ======================= Tête (11,8 à 15) + cornes =======================
band("Head_Bronze", 11.85, 1.75, 1.95, 0.5, 0.2, -0.3, "Bronze")
faceted("Head", [(11.8, 1.75, 1.95, 0.5), (13.0, 1.86, 2.25, 0.7, 0.15), (14.6, 1.86, 2.30, 0.75, 0.15), (15.0, 1.6, 2.0, 0.7, 0.1)], "Gunmetal", slant0=-0.3, bevel=0.0009)
# corne supérieure, effilée vers l'avant
faceted("Horn_Top", [(12.6, 1.55, 0.32, 0.12, 2.1), (15.0, 1.30, 0.28, 0.1, 2.35), (17.2, 0.85, 0.22, 0.08, 2.62), (18.6, 0.40, 0.12, 0.04, 2.82)], "Gunmetal", bevel=0.0006)
faceted("Horn_Top_Edge", [(13.2, 1.3, 0.06, 0.02, 2.47), (17.6, 0.6, 0.05, 0.02, 2.75)], "Bronze", bevel=0)
# lèvre inférieure
faceted("Lip_Bottom", [(14.3, 0.75, 0.28, 0.1, -2.1), (16.3, 0.62, 0.24, 0.08, -2.3), (17.2, 0.45, 0.18, 0.05, -2.38)], "Gunmetal", bevel=0.0006)
# émetteur : bague ronde polie et alésage
lathe("Emitter_Ring", [(1.0, 14.4), (1.38, 14.4), (1.38, 15.85), (1.25, 15.95), (1.0, 15.95)], "Acier_Poli")
lathe("Emitter_Inner", [(0.75, 14.6), (1.0, 14.6), (1.0, 15.7), (0.75, 15.7)], "Bronze")
cyl("Emitter_Bore", 0.76, 14.0, 15.6, "Noir_Mat")
# --- Socket de lame ---
sock = bpy.data.objects.new("Blade_Socket", None); scene.collection.objects.link(sock)
sock.parent = root; sock.location = (0, 0, Z(15.8)); sock.empty_display_size = 0.03

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
    softbox((-0.7, 0.7, 0.1), 1.6, 0.2, 7)        # bandeau haut côté caméra
    softbox((-0.6, -0.6, 0.0), 1.6, 0.25, 3)      # bandeau bas côté caméra
    # fond dégradé
    bpy.ops.mesh.primitive_plane_add(size=14, location=(1.2, -0.3, 0)); bg = bpy.context.object
    look(bg, (0, 0, 0)); bgm = bpy.data.materials.new("BG")
    if hasattr(bgm, "use_nodes"): bgm.use_nodes = True
    bgm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.06, 0.06, 0.065, 1)
    bgm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    bg.data.materials.append(bgm); bg.visible_glossy = False
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C")); scene.collection.objects.link(cam); scene.camera = cam
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 0.40
    cam.location = (-0.75, 0.22, 0.012)
    f = (Vector((0, -0.01, 0.012)) - cam.location).normalized(); zc = -f
    yc = (Vector((0, 1, 0)) - zc * zc.dot(Vector((0, 1, 0)))).normalized(); xc = yc.cross(zc)
    cam.rotation_euler = Matrix((xc, yc, zc)).transposed().to_euler()
    scene.render.filepath = RENDER
    bpy.ops.render.render(write_still=True)
