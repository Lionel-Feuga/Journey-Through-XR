"""Convertit un modèle de sabre conforme au cahier des charges (Saber_Root, Blade_Socket, modificateurs Bevel
non appliqués) en modèle de jeu pour Unity : poignée fusionnée en un seul maillage, lame et halo standard,
FBX + tableau des matériaux.

Usage (Blender 5.2, ou module bpy) :
    python -I convert_model.py <source.blend> <dossier de sortie> <NomDuModele> [--preview] [--bake <taille>] [--full]
Sorties : <Nom>.fbx, <Nom>.materials.json, et <Nom>_preview.png avec --preview.
Avec --bake 2048 : UV de la poignée et cuisson des matériaux (usure procédurale comprise) en trois textures
<Nom>_BaseColor.png, <Nom>_MetallicSmoothness.png (R métal, A lissé), <Nom>_Normal.png, citées dans le JSON.
"""
import bpy, bmesh, json, math, sys
from mathutils import Matrix, Vector, Quaternion

argv = list(sys.argv)
BAKE = 0
if "--bake" in argv:
    i = argv.index("--bake"); BAKE = int(argv[i + 1]); del argv[i:i + 2]
args = [a for a in argv if not a.startswith("--")]
SRC, OUT, NAME = args[-3], args[-2], args[-1]
PREVIEW = "--preview" in sys.argv
BUDGET = 10**9 if "--full" in sys.argv else 16000          # au-delà, les grosses pièces rondes sont allégées

bpy.ops.wm.open_mainfile(filepath=SRC, use_scripts=False)
scene = bpy.context.scene
root = bpy.data.objects["Saber_Root"]
sockets = [bpy.data.objects[n] for n in ("Blade_Socket", "Blade_Socket_2") if n in bpy.data.objects]
if not sockets:
    raise SystemExit("Blade_Socket introuvable")
socket_poses = [s.matrix_world.copy() for s in sockets]
pieces = [o for o in bpy.data.objects if o.parent == root and o.type == 'MESH']


def base_tris(o):
    o.data.calc_loop_triangles()
    return len(o.data.loop_triangles)


# 1) chanfreins retirés ; si le total de base dépasse le budget, les pièces de plus de 300 triangles sont allégées
total = sum(base_tris(o) for o in pieces)
lighten = total > BUDGET
for o in pieces:
    tris = base_tris(o)
    for m in list(o.modifiers):
        if m.type in ('BEVEL', 'SUBSURF', 'MULTIRES'):
            index = list(o.modifiers).index(m)
            kind = m.type
            o.modifiers.remove(m)
            if lighten and tris > 300 and kind == 'BEVEL':
                dec = o.modifiers.new("Decimate", 'DECIMATE')
                dec.ratio = 0.22 if tris > 1000 else 0.4
                with bpy.context.temp_override(object=o):
                    bpy.ops.object.modifier_move_to_index(modifier=dec.name, index=index)

# 2) chaque pièce figée en maillage, puis fusion
depsgraph = bpy.context.evaluated_depsgraph_get()
parts = []
for o in pieces:
    ev = o.evaluated_get(depsgraph)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=depsgraph)
    ob = bpy.data.objects.new("part_" + o.name, me)
    ob.matrix_world = ev.matrix_world.copy()
    scene.collection.objects.link(ob)
    parts.append(ob)
for o in bpy.data.objects:
    o.select_set(False)
for ob in parts:
    ob.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
hilt = bpy.context.view_layer.objects.active
hilt.name = hilt.data.name = "Saber_Hilt"
hilt.data.transform(hilt.matrix_world)
hilt.matrix_world = Matrix.Identity(4)

# 3) tableau des matériaux, lu sur les Principled BSDF du fichier
table = []
for m in hilt.data.materials:
    bsdf = m.node_tree.nodes.get("Principled BSDF") if m and m.node_tree else None
    if bsdf is None:
        continue
    c = bsdf.inputs["Base Color"].default_value
    entry = {"name": m.name, "color": [round(c[0], 4), round(c[1], 4), round(c[2], 4)],
             "metallic": round(bsdf.inputs["Metallic"].default_value, 3),
             "roughness": round(bsdf.inputs["Roughness"].default_value, 3)}
    # émission (couleur linéaire x intensité), seulement si le matériau éclaire
    strength = bsdf.inputs["Emission Strength"].default_value
    e = bsdf.inputs["Emission Color"].default_value
    if strength > 0 and max(e[0], e[1], e[2]) > 0:
        entry["emission"] = [round(e[0] * strength, 4), round(e[1] * strength, 4), round(e[2] * strength, 4)]
    table.append(entry)


def bake_textures(obj, size):
    """UV automatiques puis cuisson : couleur et métal/rugosité par émission, relief en normal map tangente."""
    import numpy as np
    scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 16
    for o in bpy.data.objects:
        o.select_set(False)
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.003)
    bpy.ops.object.mode_set(mode='OBJECT')

    def image(name, non_color):
        img = bpy.data.images.new(name, size, size, alpha=False)
        img.colorspace_settings.name = 'Non-Color' if non_color else 'sRGB'
        return img

    def target(img):
        for m in obj.data.materials:
            nt = m.node_tree
            n = nt.nodes.get("BakeTarget") or nt.nodes.new("ShaderNodeTexImage")
            n.name = "BakeTarget"; n.image = img; nt.nodes.active = n

    def bake_input(img, input_name):
        """Branche l'entrée du Principled sur une émission le temps de la cuisson."""
        saved = []
        for m in obj.data.materials:
            nt = m.node_tree; b = nt.nodes.get("Principled BSDF")
            out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
            old = out.inputs["Surface"].links[0].from_socket if out.inputs["Surface"].links else None
            em = nt.nodes.new("ShaderNodeEmission"); em.inputs["Strength"].default_value = 1.0
            src = b.inputs[input_name]
            if src.links:
                nt.links.new(src.links[0].from_socket, em.inputs["Color"])
            else:
                v = src.default_value
                em.inputs["Color"].default_value = (v, v, v, 1) if isinstance(v, float) else tuple(v)
            nt.links.new(em.outputs[0], out.inputs["Surface"])
            saved.append((nt, out, old, em))
        target(img)
        bpy.ops.object.bake(type='EMIT', margin=8, use_clear=True)
        for nt, out, old, em in saved:
            if old is not None:
                nt.links.new(old, out.inputs["Surface"])
            nt.nodes.remove(em)

    normal = image(f"{NAME}_Normal", True); target(normal)
    bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', margin=8, use_clear=True)
    color = image(f"{NAME}_BaseColor", False); bake_input(color, "Base Color")
    metal = image("metal", True); bake_input(metal, "Metallic")
    rough = image("rough", True); bake_input(rough, "Roughness")

    m = np.array(metal.pixels[:]).reshape(-1, 4)[:, 0]
    r = np.array(rough.pixels[:]).reshape(-1, 4)[:, 0]
    ms = bpy.data.images.new(f"{NAME}_MetallicSmoothness", size, size, alpha=True)
    ms.colorspace_settings.name = 'Non-Color'
    px = np.zeros((size * size, 4), dtype=np.float32)
    px[:, 0] = px[:, 1] = px[:, 2] = m; px[:, 3] = 1.0 - r
    ms.pixels.foreach_set(px.ravel())
    files = {}
    for key, img in (("baseMap", color), ("maskMap", ms), ("normalMap", normal)):
        img.filepath_raw = f"{OUT}/{img.name}.png"; img.file_format = 'PNG'; img.save()
        files[key] = f"{img.name}.png"
    for mat_ in obj.data.materials:
        n = mat_.node_tree.nodes.get("BakeTarget")
        if n: mat_.node_tree.nodes.remove(n)
    return files


textures = bake_textures(hilt, BAKE) if BAKE else None
with open(f"{OUT}/{NAME}.materials.json", "w") as f:
    data = {"materials": table}
    if textures:
        data["textures"] = textures
    json.dump(data, f, indent=2)

# 4) on ne garde que la poignée
for o in list(bpy.data.objects):
    if o != hilt:
        bpy.data.objects.remove(o, do_unlink=True)


def finish(bm, name, color):
    me = bpy.data.meshes.new(name)
    bm.normal_update(); bm.to_mesh(me); bm.free()
    mat = bpy.data.materials.get(name + "_Mat") or bpy.data.materials.new(name + "_Mat")
    mat.diffuse_color = (*color, 1)
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    return ob


def blade_mesh():
    SEG, R, LEN = 32, 0.011, 0.90
    bm = bmesh.new()
    rings = [(R, 0.0), (R, LEN - R)] + [(R * math.cos(math.pi / 2 * k / 6), LEN - R + R * math.sin(math.pi / 2 * k / 6)) for k in range(1, 6)]
    loops = [[bm.verts.new((r * math.cos(2 * math.pi * i / SEG), r * math.sin(2 * math.pi * i / SEG), z)) for i in range(SEG)] for r, z in rings]
    tip = bm.verts.new((0, 0, LEN))
    for a, b in zip(loops, loops[1:]):
        for i in range(SEG):
            j = (i + 1) % SEG
            bm.faces.new((a[i], a[j], b[j], b[i])).smooth = True
    for i in range(SEG):
        bm.faces.new((loops[-1][i], loops[-1][(i + 1) % SEG], tip)).smooth = True
    bm.faces.new(list(reversed(loops[0])))
    return bm


def glow_mesh():
    SEG, R, G0, G1 = 32, 0.040, 0.030, 0.889
    bm = bmesh.new()
    rings = [(R * math.sin(math.pi / 2 * k / 8), G0 - R * math.cos(math.pi / 2 * k / 8)) for k in range(1, 9)]
    rings += [(R * math.cos(math.pi / 2 * k / 8), G1 + R * math.sin(math.pi / 2 * k / 8)) for k in range(0, 8)]
    loops = [[bm.verts.new((r * math.cos(2 * math.pi * i / SEG), r * math.sin(2 * math.pi * i / SEG), z)) for i in range(SEG)] for r, z in rings]
    bot = bm.verts.new((0, 0, G0 - R)); top = bm.verts.new((0, 0, G1 + R))
    for a, b in zip(loops, loops[1:]):
        for i in range(SEG):
            j = (i + 1) % SEG
            bm.faces.new((a[i], a[j], b[j], b[i])).smooth = True
    for i in range(SEG):
        j = (i + 1) % SEG
        bm.faces.new((loops[-1][i], loops[-1][j], top)).smooth = True
        bm.faces.new((loops[0][j], loops[0][i], bot)).smooth = True
    return bm


# 5) lame et halo standard à chaque Blade_Socket (axe +Z local du repère)
extras = []
for index, pose in enumerate(socket_poses):
    suffix = "" if index == 0 else "_2"
    for name, bm, color in (("Saber_Blade" + suffix, blade_mesh(), (0.35, 0.75, 1.0)), ("Saber_Glow" + suffix, glow_mesh(), (0.1, 0.55, 1.0))):
        ob = finish(bm, name, color)
        if index > 0:
            ob.data = extras[0 if name.startswith("Saber_Blade") else 1].data
        ob.matrix_world = pose
        extras.append(ob)

hilt.data.calc_loop_triangles()
vs = [v.co for v in hilt.data.vertices]
print("RESULT", NAME, "base:", total, "-> hilt tris:", len(hilt.data.loop_triangles), "lightened:", lighten,
      "materials:", [m["name"] for m in table],
      "min", [round(min(v[i] for v in vs), 3) for i in range(3)], "max", [round(max(v[i] for v in vs), 3) for i in range(3)],
      "sockets:", [[round(x, 4) for x in p.translation] for p in socket_poses])

for o in bpy.data.objects:
    o.select_set(True)
bpy.ops.export_scene.fbx(filepath=f"{OUT}/{NAME}.fbx", use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True, object_types={'MESH'},
                         mesh_smooth_type='OFF', use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False)

if PREVIEW:
    for ob in extras:
        ob.hide_render = True
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 40; scene.cycles.device = 'CPU'
    scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 1100, 520, 100
    scene.world = scene.world or bpy.data.worlds.new("W")

    def look(obj, target, roll=0.0):
        q = (Vector(target) - obj.location).to_track_quat('-Z', 'Y')
        obj.rotation_euler = (q @ Quaternion((0, 0, 1), roll)).to_euler()

    for loc, e in (((0.6, -0.6, 0.5), 25), ((0.5, 0.5, -0.2), 10), ((-0.6, -0.3, 0.2), 8)):
        ld = bpy.data.lights.new("L", 'AREA'); ld.energy = e; ld.size = 0.6
        lo = bpy.data.objects.new("L", ld); lo.location = loc
        scene.collection.objects.link(lo); look(lo, (0, 0, 0))
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C")); cam.data.lens = 60
    scene.collection.objects.link(cam); scene.camera = cam
    cam.location = (0.25, -0.62, 0.12)
    look(cam, (0.02, 0, -0.05), math.radians(-90))
    scene.render.filepath = f"{OUT}/{NAME}_preview.png"
    bpy.ops.render.render(write_still=True)
