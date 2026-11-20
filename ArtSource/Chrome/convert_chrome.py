"""Convertit Sabre_Chrome.blend en modèle de jeu : un seul maillage, orienté et dimensionné comme les autres sabres,
avec la lame et le halo standard. Usage : python convert_chrome.py <source.blend> <dossier de sortie> <high|low>"""
import bpy, bmesh, math, sys
from mathutils import Matrix, Vector

SRC, OUT, LEVEL = sys.argv[-3], sys.argv[-2], sys.argv[-1]
SCALE = 0.77                    # 4,4 cm de diamètre -> 3,4 cm ; poignée d'environ 29 cm
GRIP_CENTER_Y = -0.105          # centre de la poignée noire dans le fichier d'origine
GRIP_TARGET = -0.045            # même position de la main que sur le premier sabre
EMITTER_Y = 0.176               # optique de l'émetteur : départ de la lame

bpy.ops.wm.open_mainfile(filepath=SRC, use_scripts=False)
scene = bpy.context.scene
root = bpy.data.objects["Sabre_Chrome"]
dg_objects = [o for o in bpy.data.objects if o.parent == root and o.type in ('MESH', 'CURVE')]

# 1) chanfreins retirés (ou remplacés par une simplification), puis chaque pièce figée en maillage
for o in dg_objects:
    if o.type != 'MESH':
        continue
    o.data.calc_loop_triangles()
    tris = len(o.data.loop_triangles)
    for m in list(o.modifiers):
        if m.type == 'BEVEL':
            if LEVEL == 'low' and tris > 300:
                index = list(o.modifiers).index(m)
                o.modifiers.remove(m)
                dec = o.modifiers.new("Decimate", 'DECIMATE')
                dec.ratio = 0.22 if tris > 1000 else 0.4
                with bpy.context.temp_override(object=o):
                    bpy.ops.object.modifier_move_to_index(modifier=dec.name, index=index)
            else:
                o.modifiers.remove(m)

depsgraph = bpy.context.evaluated_depsgraph_get()
parts = []
for o in dg_objects:
    ev = o.evaluated_get(depsgraph)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=depsgraph)
    ob = bpy.data.objects.new("part_" + o.name, me)
    ob.matrix_world = ev.matrix_world.copy()
    if not me.materials and o.material_slots:
        for s in o.material_slots:
            me.materials.append(s.material)
    scene.collection.objects.link(ob)
    parts.append(ob)

# 2) fusion en un seul objet
for o in bpy.data.objects:
    o.select_set(False)
for ob in parts:
    ob.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
hilt = bpy.context.view_layer.objects.active
hilt.name = "Saber_Hilt"
hilt.data.name = "Saber_Hilt"

# 3) orientation (lame vers +Z, commandes côté pouce), échelle, position de la prise
ROLL = math.pi                  # demi-tour autour de l'axe de la lame : commandes à l'opposé de la paume (0 : orientation d'origine)
turn = Matrix.Rotation(ROLL, 4, 'Z') @ Matrix.Rotation(math.pi / 2, 4, 'Z') @ Matrix.Rotation(math.pi / 2, 4, 'X')
shift = GRIP_TARGET - GRIP_CENTER_Y * SCALE
place = Matrix.Translation((0, 0, shift)) @ Matrix.Scale(SCALE, 4) @ turn
hilt.data.transform(place @ hilt.matrix_world)
hilt.matrix_world = Matrix.Identity(4)
hilt.data.update()

# 4) nettoyage : on ne garde que le sabre
for o in list(bpy.data.objects):
    if o != hilt:
        bpy.data.objects.remove(o, do_unlink=True)

# 5) lame et halo standard (mêmes maillages que les autres sabres)
def make_mat(name, color):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    return m

def finish(bm, name, mat):
    me = bpy.data.meshes.new(name)
    bm.normal_update(); bm.to_mesh(me); bm.free()
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    return ob

SEG, BL_R, BL_LEN = 32, 0.011, 0.90
BASE_Z = EMITTER_Y * SCALE + shift
bm = bmesh.new()
rings = [(BL_R, 0.0), (BL_R, BL_LEN - BL_R)]
for k in range(1, 6):
    a = (math.pi / 2) * k / 6
    rings.append((BL_R * math.cos(a), BL_LEN - BL_R + BL_R * math.sin(a)))
loops = [[bm.verts.new((r * math.cos(2 * math.pi * i / SEG), r * math.sin(2 * math.pi * i / SEG), z)) for i in range(SEG)] for r, z in rings]
tip = bm.verts.new((0, 0, BL_LEN))
for a, b in zip(loops, loops[1:]):
    for i in range(SEG):
        j = (i + 1) % SEG
        bm.faces.new((a[i], a[j], b[j], b[i])).smooth = True
for i in range(SEG):
    bm.faces.new((loops[-1][i], loops[-1][(i + 1) % SEG], tip)).smooth = True
bm.faces.new(list(reversed(loops[0])))
blade = finish(bm, "Saber_Blade", make_mat("Blade_Emissive", (0.35, 0.75, 1.0)))
blade.location = (0, 0, BASE_Z)

GL_R, G0, G1 = 0.040, 0.030, BL_LEN - 0.011
bm = bmesh.new()
rings = []
for k in range(1, 9):
    a = (math.pi / 2) * k / 8
    rings.append((GL_R * math.sin(a), G0 - GL_R * math.cos(a)))
for k in range(0, 8):
    a = (math.pi / 2) * k / 8
    rings.append((GL_R * math.cos(a), G1 + GL_R * math.sin(a)))
loops = [[bm.verts.new((r * math.cos(2 * math.pi * i / SEG), r * math.sin(2 * math.pi * i / SEG), z)) for i in range(SEG)] for r, z in rings]
bot = bm.verts.new((0, 0, G0 - GL_R)); top = bm.verts.new((0, 0, G1 + GL_R))
for a, b in zip(loops, loops[1:]):
    for i in range(SEG):
        j = (i + 1) % SEG
        bm.faces.new((a[i], a[j], b[j], b[i])).smooth = True
for i in range(SEG):
    j = (i + 1) % SEG
    bm.faces.new((loops[-1][i], loops[-1][j], top)).smooth = True
    bm.faces.new((loops[0][j], loops[0][i], bot)).smooth = True
glow = finish(bm, "Saber_Glow", make_mat("Saber_Glow", (0.1, 0.55, 1.0)))
glow.location = (0, 0, BASE_Z)

# 6) bilan et export
hilt.data.calc_loop_triangles()
vs = [v.co for v in hilt.data.vertices]
print("RESULT", LEVEL, "tris:", len(hilt.data.loop_triangles), "materials:", [m.name for m in hilt.data.materials],
      "min", [round(min(v[i] for v in vs), 3) for i in range(3)], "max", [round(max(v[i] for v in vs), 3) for i in range(3)],
      "blade base z:", round(BASE_Z, 4))

name = "Sabre_Chrome_Low" if LEVEL == 'low' else "Sabre_Chrome"
for o in bpy.data.objects:
    o.select_set(True)
bpy.ops.export_scene.fbx(filepath=f"{OUT}/{name}.fbx", use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True, object_types={'MESH'},
                         mesh_smooth_type='OFF', use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False)

if "--render" in sys.argv[:-3]:
    from mathutils import Quaternion
    blade.hide_render = True; glow.hide_render = True
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 48; scene.cycles.device = 'CPU'
    scene.render.resolution_x, scene.render.resolution_y = 1400, 640
    scene.render.resolution_percentage = 100
    world = scene.world or bpy.data.worlds.new("W"); scene.world = world
    def look(obj, target, roll=0.0):
        q = (Vector(target) - obj.location).to_track_quat('-Z', 'Y')
        obj.rotation_euler = (q @ Quaternion((0, 0, 1), roll)).to_euler()
    for loc, e in (((-0.6, 0.6, 0.5), 25), ((-0.5, -0.5, -0.2), 10), ((0.6, 0.3, 0.2), 8)):
        ld = bpy.data.lights.new("L", 'AREA'); ld.energy = e; ld.size = 0.6
        lo = bpy.data.objects.new("L", ld); lo.location = loc
        scene.collection.objects.link(lo); look(lo, (0, 0, 0))
    cd = bpy.data.cameras.new("C"); cd.lens = 60
    cam = bpy.data.objects.new("C", cd); scene.collection.objects.link(cam); scene.camera = cam
    cam.location = (-0.42, 0.50, 0.22)
    look(cam, (0, 0, 0.02), math.radians(90))
    scene.render.filepath = f"{OUT}/{name}_preview.png"
    bpy.ops.render.render(write_still=True)
