"""Génère Sabre_Machoires.blend selon le cahier des charges des modèles fournis (version 2, qualité d'abord).
Usage : python build_sabre_griffes.py <sortie.blend> [--render <image.png>]
Repère : lame vers +Z, commandes (bouton) vers +Y, mètres. Cotes du croquis en cm depuis la base du pommeau."""
import bpy, bmesh, math, sys
from mathutils import Vector, Matrix, Quaternion

OUT = sys.argv[1]
WEAR = "--wear" in sys.argv
RENDER = sys.argv[sys.argv.index("--render") + 1] if "--render" in sys.argv else None
Z0 = 11.55                       # origine : 4,5 cm au-dessus du centre de la zone tenue (1,4 à 12,7)
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
    "Acier_Sombre":  mat("Acier_Sombre", (0.24, 0.23, 0.22), 1.0, 0.45),
    "Acier_Bord":    mat("Acier_Bord", (0.34, 0.33, 0.31), 1.0, 0.30),
    "Noir_Satine":   mat("Noir_Satine", (0.014, 0.014, 0.015), 0.6, 0.34),
    "Noir_Mat":      mat("Noir_Mat", (0.008, 0.008, 0.008), 0.2, 0.55),
    "Cuir_Noir":     mat("Cuir_Noir", (0.011, 0.010, 0.010), 0.0, 0.9),
    "Rouge_Emissif": mat("Rouge_Emissif", (1.0, 0.012, 0.006), 0.0, 0.4, emit=1.8),
    "Acier_Poli":    mat("Acier_Poli", (0.55, 0.55, 0.56), 1.0, 0.14),
}


# --- Usure procédurale (aperçu Blender ; à cuire en textures pour Unity) ---
def wear_metal(m, dark, mid, edge, rough=(0.32, 0.62), scratch=1.0):
    nt = m.node_tree; N = nt.nodes; L = nt.links
    b = N["Principled BSDF"]
    tc = N.new("ShaderNodeTexCoord")
    def noise(scale, detail=8.0, rough_=0.6, stretch=None, rot=None):
        mp = N.new("ShaderNodeMapping"); L.new(tc.outputs["Object"], mp.inputs["Vector"])
        if rot: mp.inputs["Rotation"].default_value = rot
        if stretch: mp.inputs["Scale"].default_value = stretch
        n = N.new("ShaderNodeTexNoise"); n.inputs["Scale"].default_value = scale
        n.inputs["Detail"].default_value = detail; n.inputs["Roughness"].default_value = rough_
        L.new(mp.outputs["Vector"], n.inputs["Vector"]); return n
    def ramp(inp, stops):
        r = N.new("ShaderNodeValToRGB"); L.new(inp, r.inputs["Fac"])
        e = r.color_ramp.elements
        e[0].position, e[0].color = stops[0][0], (*stops[0][1], 1)
        e[1].position, e[1].color = stops[-1][0], (*stops[-1][1], 1)
        for pos, col in stops[1:-1]:
            el = e.new(pos); el.color = (*col, 1)
        return r
    def mix(a, bb, fac):
        mx = N.new("ShaderNodeMix"); mx.data_type = 'RGBA'
        L.new(fac, mx.inputs["Factor"]); L.new(a, mx.inputs[6]); L.new(bb, mx.inputs[7]); return mx.outputs[2]
    # patine : grandes taches sombres / brunes
    patch = ramp(noise(9.0, 6.0, 0.55).outputs["Fac"], [(0.35, (0, 0, 0)), (0.65, (1, 1, 1))])
    base = mix(ramp(patch.outputs["Color"], [(0.0, dark), (1.0, mid)]).outputs["Color"], ramp(patch.outputs["Color"], [(0.0, dark), (1.0, mid)]).outputs["Color"], patch.outputs["Color"])
    # rayures fines dans le sens du manche, et rayures croisées
    layers = []
    for k, rot in enumerate(((0.4, 0.9, 0.2), (1.3, 0.2, 2.1), (2.2, 1.6, 0.7), (0.1, 2.5, 1.4))):
        n = noise(700.0 + 90 * k, 1.0, 0.4, stretch=(1.0, 1.0, 0.008), rot=rot)
        layers.append(ramp(n.outputs["Fac"], [(0.635, (0, 0, 0)), (0.65, (1, 1, 1))]).outputs["Color"])
    acc = layers[0]
    for lay in layers[1:]:
        mx = N.new("ShaderNodeMix"); mx.data_type = 'RGBA'; mx.blend_type = 'LIGHTEN'; mx.inputs["Factor"].default_value = 1.0
        L.new(acc, mx.inputs[6]); L.new(lay, mx.inputs[7]); acc = mx.outputs[2]
    mask = ramp(noise(14.0, 3.0, 0.5).outputs["Fac"], [(0.45, (0, 0, 0)), (0.6, (1, 1, 1))])
    sc = N.new("ShaderNodeMix"); sc.data_type = 'RGBA'; sc.blend_type = 'MULTIPLY'; sc.inputs["Factor"].default_value = scratch
    L.new(acc, sc.inputs[6]); L.new(mask.outputs["Color"], sc.inputs[7])
    # arêtes polies par l'usage (saillies), creux encrassés
    geo = N.new("ShaderNodeNewGeometry")
    edges = ramp(geo.outputs["Pointiness"], [(0.57, (0, 0, 0)), (0.64, (1, 1, 1))])
    ao = N.new("ShaderNodeAmbientOcclusion"); ao.inputs["Distance"].default_value = 0.004
    grime = ramp(ao.outputs["AO"], [(0.55, (1, 1, 1)), (0.95, (0, 0, 0))])
    edgecol = (edge[0], edge[1], edge[2], 1)
    c1 = N.new("ShaderNodeMix"); c1.data_type = 'RGBA'; L.new(edges.outputs["Color"], c1.inputs["Factor"]); L.new(base, c1.inputs[6]); c1.inputs[7].default_value = edgecol
    c2 = N.new("ShaderNodeMix"); c2.data_type = 'RGBA'; L.new(sc.outputs[2], c2.inputs["Factor"]); L.new(c1.outputs[2], c2.inputs[6]); c2.inputs[7].default_value = tuple((edge[i] + mid[i]) / 2 for i in range(3)) + (1,)
    c3 = N.new("ShaderNodeMix"); c3.data_type = 'RGBA'; L.new(grime.outputs["Color"], c3.inputs["Factor"]); L.new(c2.outputs[2], c3.inputs[6]); c3.inputs[7].default_value = (0.03, 0.025, 0.02, 1)
    L.new(c3.outputs[2], b.inputs["Base Color"])
    # rugosité : taches + rayures plus brillantes + crasse mate
    rr = ramp(noise(55.0, 8.0, 0.6).outputs["Fac"], [(0.3, (rough[0],) * 3), (0.7, (rough[1],) * 3)])
    r2 = N.new("ShaderNodeMix"); r2.data_type = 'RGBA'; L.new(sc.outputs[2], r2.inputs["Factor"]); L.new(rr.outputs["Color"], r2.inputs[6]); r2.inputs[7].default_value = (0.18, 0.18, 0.18, 1)
    r3 = N.new("ShaderNodeMix"); r3.data_type = 'RGBA'; L.new(grime.outputs["Color"], r3.inputs["Factor"]); L.new(r2.outputs[2], r3.inputs[6]); r3.inputs[7].default_value = (0.85, 0.85, 0.85, 1)
    L.new(r3.outputs[2], b.inputs["Roughness"])
    # micro-piqûres et rayures en relief
    pits = noise(900.0, 4.0, 0.7)
    bump = N.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.12; bump.inputs["Distance"].default_value = 0.0002
    bsum = N.new("ShaderNodeMath"); bsum.operation = 'SUBTRACT'
    L.new(pits.outputs["Fac"], bsum.inputs[0]); L.new(sc.outputs[2], bsum.inputs[1])
    L.new(bsum.outputs[0], bump.inputs["Height"]); L.new(bump.outputs["Normal"], b.inputs["Normal"])

def leather_grain(m):
    nt = m.node_tree; N = nt.nodes; L = nt.links; b = N["Principled BSDF"]
    tc = N.new("ShaderNodeTexCoord")
    v = N.new("ShaderNodeTexVoronoi"); v.inputs["Scale"].default_value = 700.0; v.feature = 'DISTANCE_TO_EDGE'
    L.new(tc.outputs["Object"], v.inputs["Vector"])
    bump = N.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.35; bump.inputs["Distance"].default_value = 0.0003
    L.new(v.outputs["Distance"], bump.inputs["Height"]); L.new(bump.outputs["Normal"], b.inputs["Normal"])
    n = N.new("ShaderNodeTexNoise"); n.inputs["Scale"].default_value = 30.0; L.new(tc.outputs["Object"], n.inputs["Vector"])
    r = N.new("ShaderNodeValToRGB"); L.new(n.outputs["Fac"], r.inputs["Fac"])
    r.color_ramp.elements[0].color = (0.85, 0.85, 0.85, 1); r.color_ramp.elements[1].color = (0.97, 0.97, 0.97, 1)
    L.new(r.outputs["Color"], b.inputs["Roughness"])

if WEAR:
    wear_metal(M["Acier_Sombre"], dark=(0.07, 0.065, 0.06), mid=(0.20, 0.185, 0.17), edge=(0.42, 0.40, 0.38))
    wear_metal(M["Acier_Bord"], dark=(0.10, 0.09, 0.085), mid=(0.26, 0.245, 0.23), edge=(0.48, 0.46, 0.44))
    wear_metal(M["Noir_Satine"], dark=(0.008, 0.008, 0.009), mid=(0.02, 0.02, 0.022), edge=(0.09, 0.085, 0.08), rough=(0.35, 0.6), scratch=0.6)
    leather_grain(M["Cuir_Noir"])

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
    curved_panel(name + "_FrameL", r_surf - 0.05, r_surf + 0.09, ang - half - da, ang - half, z0 - frame, z1 + frame, "Acier_Sombre", n=2)
    curved_panel(name + "_FrameR", r_surf - 0.05, r_surf + 0.09, ang + half, ang + half + da, z0 - frame, z1 + frame, "Acier_Sombre", n=2)
    curved_panel(name + "_FrameB", r_surf - 0.05, r_surf + 0.09, ang - half, ang + half, z0 - frame, z0, "Acier_Sombre", n=8)
    curved_panel(name + "_FrameT", r_surf - 0.05, r_surf + 0.09, ang - half, ang + half, z1, z1 + frame, "Acier_Sombre", n=8)

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
    return add(name, bm, "Acier_Poli", bevel=0)


def shell_loft(name, stations, center, r_in, material, n=16, **kw):
    """Coque qui épouse le cylindre ; stations : (z, demi-angle, rayon extérieur)."""
    secs = []
    for z, half, ro in stations:
        inner = [polar(r_in, center - half + 2 * half * i / n, z) for i in range(n + 1)]
        outer = [polar(ro, center - half + 2 * half * i / n, z) for i in reversed(range(n + 1))]
        secs.append(inner + outer)
    return loft(name, secs, material, **kw)

# ======================= Pommeau (0 à 1,4) + anneau =======================
lathe("Pommel_Shell", [(0.9, 0.02), (1.7, 0.0), (2.1, 0.32), (2.1, 1.02), (1.6, 1.02)], "Acier_Sombre", seg=8, close_bottom=True, close_top=True)
lathe("Pommel_End_Rim", [(1.0, -0.04), (1.38, -0.04), (1.38, 0.08), (1.0, 0.08)], "Acier_Bord", seg=8)
cyl("Pommel_End_Plate", 1.02, -0.02, 0.06, "Noir_Satine", seg=8)
for k in range(8):
    a = math.pi / 8 + k * math.pi / 4
    if k % 2 == 0:
        curved_panel(f"Pommel_Slit_{k}", 1.98, 2.115, a - 0.11, a + 0.11, 0.42, 0.88, "Rouge_Emissif", n=2, bevel=0.0003, segs=2)
    else:
        curved_panel(f"Pommel_Plate_{k}", 2.05, 2.16, a - 0.3, a + 0.3, 0.38, 0.92, "Acier_Bord", n=4)
ring("Pommel_Ring", 1.6, 2.16, 1.0, 1.42, "Acier_Bord")
# anneau d'attache (côté -Y, comme sur l'image)
def dring():
    bm = bmesh.new(); seg = 12; N = 28; rr = 0.16
    for half in (0,):
        pts = []
        for i in range(N + 1):
            t = math.pi * i / N
            pts.append(Vector((0, -R(1.0 + 0.75 * math.cos(t)), Z(0.05) - R(1.05 * math.sin(t)))))
        rings = []
        for i, p in enumerate(pts):
            d = (pts[min(i + 1, N)] - pts[max(i - 1, 0)]).normalized()
            u = Vector((1, 0, 0)); v = d.cross(u).normalized()
            rings.append([bm.verts.new(p + (u * math.cos(TAU * j / seg) + v * math.sin(TAU * j / seg)) * R(rr)) for j in range(seg)])
        for a, b in zip(rings, rings[1:]):
            for j in range(seg):
                k = (j + 1) % seg; bm.faces.new((a[j], a[k], b[k], b[j]))
        bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    return add("Pommel_DRing", bm, "Acier_Sombre", bevel=0)
dring()
for s in (0.25, 1.75):
    curved_panel(f"Pommel_Hinge_{s}", 0.0 + s - 0.25, s + 0.25, -UP - 0.0001, -UP + 0.0001, 0, 0, "Acier_Sombre") if False else None
    box = bmesh.new()
    bmesh.ops.create_cube(box, size=1.0)
    bmesh.ops.scale(box, vec=(R(0.5), R(0.35), R(0.3)), verts=box.verts)
    bmesh.ops.translate(box, vec=Vector((0, -R(s), Z(0.05))), verts=box.verts)
    add(f"Pommel_Hinge_{int(s*100)}", box, "Acier_Bord")

# ======================= Poignée cuir (1,4 à 12,7) =======================
cyl("Grip_Core", 1.62, 1.35, 12.75, "Noir_Mat", bevel=0)
leather = [(1.6, -0.6), (1.71, -0.57), (1.78, -0.45), (1.8, -0.2), (1.8, 0.2), (1.78, 0.45), (1.71, 0.57), (1.6, 0.6)]
helix("Grip_Wrap", 1.7, 12.4, 1.15, 0.0, leather, "Cuir_Noir", bevel=0)
cross = [(1.74, -0.48), (1.84, -0.45), (1.875, -0.3), (1.875, 0.3), (1.84, 0.45), (1.74, 0.48)]
helix("Grip_Cross_A", 1.9, 12.3, 5.2, 0.6, cross, "Cuir_Noir", sign=-1, bevel=0)
helix("Grip_Cross_B", 1.9, 12.3, 5.2, 0.6 + math.pi, cross, "Cuir_Noir", sign=-1, bevel=0)
ring("Grip_Collar", 1.6, 1.86, 12.55, 12.9, "Acier_Sombre")

# ======================= Corps (12,7 à 19,5) =======================
cyl("Body_Core", 1.8, 12.7, 19.55, "Noir_Satine")
for side, c in (("Top", UP), ("Bottom", -UP)):
    shell_loft(f"Shell_{side}", [(12.75, 0.35, 1.8), (13.4, 0.95, 1.97), (13.6, 1.1, 1.97), (15.4, 1.1, 1.97), (16.6, 0.55, 1.97), (19.0, 0.55, 1.97), (19.5, 0.5, 1.85)], c, 1.78, "Acier_Sombre", n=20, bevel=0.0008)
    shell_loft(f"Shell_{side}_Edge", [(13.62, 1.12, 2.0), (15.38, 1.12, 2.0)], c + 1.07, 1.95, "Acier_Bord", n=2)
# boîtier du bouton (côté +Y)
def housing():
    secs = []
    for z, hw, top in ((13.3, 0.55, 1.97), (14.2, 0.85, 2.42), (18.0, 0.85, 2.42), (18.6, 0.55, 1.97)):
        y0, y1 = R(1.9), R(top)
        w = R(hw)
        secs.append([Vector((-w, y0, Z(z))), Vector((-w * 0.8, y1, Z(z))), Vector((w * 0.8, y1, Z(z))), Vector((w, y0, Z(z)))])
    return loft("Button_Housing", secs, "Noir_Satine", bevel=0.0006)
housing()
def disc(name, x, y, z, rad, h, material, seg=32, axis='Y'):
    bm = bmesh.new()
    prof = [(0.0, 0.0), (rad, 0.0), (rad, h * 0.75), (rad * 0.85, h), (0.0, h)]
    rings = [[bm.verts.new((R(r) * math.cos(TAU * i / seg), R(r) * math.sin(TAU * i / seg), R(hh))) for i in range(seg)] for r, hh in prof]
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg; bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.transform(bm, matrix=Matrix.Translation((R(x), R(y), Z(z))) @ Matrix.Rotation(-math.pi / 2, 4, 'X'), verts=bm.verts)
    return add(name, bm, material, bevel=0.0004, segs=2)
disc("Button_Base", 0, 2.4, 16.2, 0.85, 0.12, "Acier_Sombre")
disc("Button", 0, 2.5, 16.2, 0.7, 0.3, "Acier_Poli")
disc("Button_Small", 0, 2.4, 14.7, 0.32, 0.1, "Acier_Bord", seg=20)
box = bmesh.new(); bmesh.ops.create_cube(box, size=1.0)
bmesh.ops.scale(box, vec=(R(0.9), R(0.1), R(0.32)), verts=box.verts)
bmesh.ops.translate(box, vec=Vector((0, R(2.43), Z(17.55))), verts=box.verts)
add("Button_Indicator", box, "Rouge_Emissif", bevel=0)

# ======================= Bague, col, bague à fentes (19,5 à 24,3) =======================
ring("Band_Steel", 1.75, 1.98, 19.5, 20.8, "Acier_Sombre", lip=0.06)
ring("Band_Steel_Lip", 1.75, 2.02, 19.85, 20.0, "Acier_Bord", lip=0.02)
cyl("Neck", 1.8, 20.75, 22.55, "Noir_Satine")
ring("Neck_Groove", 1.7, 1.86, 21.55, 21.75, "Acier_Bord", lip=0.02)
lathe("Slit_Ring", [(1.75, 22.5), (1.98, 22.55), (1.98, 23.65), (1.75, 23.7)], "Acier_Sombre", seg=12)
for k in range(6):
    a = k * TAU / 6
    curved_panel(f"Slit_Ring_Red_{k}", 1.88, 2.0, a - 0.09, a + 0.09, 22.7, 23.5, "Rouge_Emissif", n=2, bevel=0.0003, segs=2)
cyl("Spacer", 1.86, 23.7, 24.35, "Acier_Bord")

# ======================= Émetteur à 4 mâchoires (24,3 à 27) =======================
lathe("Emitter_Base", [(1.3, 26.3), (1.3, 24.3), (1.86, 24.3), (1.86, 26.3)], "Acier_Sombre", close_bottom=False)
for k in range(4):
    c = UP + k * math.pi / 2
    shell_loft(f"Jaw_{k}", [(24.3, 0.5, 2.3), (26.3, 0.5, 2.5), (26.75, 0.5, 2.45), (27.0, 0.45, 2.2)], c, 1.84, "Acier_Sombre", n=10, bevel=0.0008)
    shell_loft(f"Jaw_Lip_{k}", [(26.32, 0.5, 1.88), (26.98, 0.45, 1.88)], c, 1.58, "Acier_Sombre", n=10, bevel=0.0006)
    shell_loft(f"Jaw_Face_{k}", [(24.5, 0.18, 2.36), (26.1, 0.18, 2.56)], c, 2.25, "Acier_Bord", n=4, bevel=0.0004)
lathe("Emitter_Tube", [(1.0, 25.4), (1.32, 25.4), (1.32, 26.62), (1.0, 26.62)], "Acier_Bord")
ring("Emitter_Inner_Ring", 0.95, 1.12, 26.0, 26.2, "Acier_Poli", lip=0.02)
cyl("Emitter_Bore", 1.0, 25.0, 25.6, "Noir_Mat")

# --- Socket de lame ---
sock = bpy.data.objects.new("Blade_Socket", None); scene.collection.objects.link(sock)
sock.parent = root; sock.location = (0, 0, Z(26.6)); sock.empty_display_size = 0.03

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
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.12, 0.12, 0.13, 1)
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
    cam.location = (0.55, -0.66, 0.30); look(cam, (0, 0, 0.02), math.radians(-52))
    scene.render.filepath = RENDER
    bpy.ops.render.render(write_still=True)
