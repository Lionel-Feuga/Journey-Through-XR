"""Convertit Aureole_Detail.blend en modèle de jeu conforme aux autres sabres : Saber_Hilt en un seul maillage,
Saber_Blade, Pommel_Ring (pièce mobile, laissée à part). La forme n'est pas modifiée.
  light  : version retenue (55 000 triangles). Mêmes pièces, mêmes chanfreins, mais chanfreins à 1 segment au lieu
           de 3 et cylindres à 2 fois moins de côtés. Écart de silhouette inférieur à 0,03 mm.
  detail : géométrie d'origine (environ 210 000 triangles), jugée identique au casque, non retenue.
Le sabre est tourné d'un demi-tour autour de la lame (ROLL) : le boîtier de commande se retrouve côté doigts,
à l'opposé de la paume, au lieu de suivre la convention « commandes vers -Z ».
Usage : python convert_aureole.py <Aureole_Detail.blend> <dossier de sortie> <detail|light> [--render]"""
import bpy, bmesh, math, sys
from mathutils import Matrix
from collections import Counter

args = [a for a in sys.argv if a != "--render"]
SRC, OUT, LEVEL = args[-3], args[-2], args[-1]
RENDER = "--render" in sys.argv
ROLL = math.pi                  # rotation du modèle autour de l'axe de la lame
NAME = "Aureole" if LEVEL == "detail" else "Aureole_Light"

bpy.ops.wm.open_mainfile(filepath=SRC, use_scripts=False)
scene = bpy.context.scene
KEEP_APART = {"Saber_Blade", "Pommel_Ring"}
STUDIO = {"Plane"}
meshes = [o for o in bpy.data.objects if o.type == 'MESH' and o.name not in STUDIO]


def halve_segments(obj):
    """Pièce de révolution autour de l'axe du sabre : retire un méridien sur deux."""
    if abs(obj.location.x) > 1e-6 or abs(obj.location.y) > 1e-6:
        return False
    me = obj.data
    rings = Counter((round(math.hypot(v.co.x, v.co.y), 5), round(v.co.z, 5)) for v in me.vertices if math.hypot(v.co.x, v.co.y) > 1e-6)
    if not rings:
        return False
    n = Counter(rings.values()).most_common(1)[0][0]
    if n < 48 or n % 2 or any(c != n for c in rings.values()):
        return False
    step = 2 * math.pi / n
    first = next(v for v in me.vertices if math.hypot(v.co.x, v.co.y) > 1e-6)
    a0 = math.atan2(first.co.y, first.co.x)
    index = {}
    for v in me.vertices:
        if math.hypot(v.co.x, v.co.y) <= 1e-6:
            index[v.index] = None          # sommet sur l'axe (centre d'un bouchon)
            continue
        k = (math.atan2(v.co.y, v.co.x) - a0) / step
        if abs(k - round(k)) > 0.02:
            return False
        index[v.index] = round(k) % n
    bm = bmesh.new(); bm.from_mesh(me)
    doomed = []
    for e in bm.edges:
        a, b = index[e.verts[0].index], index[e.verts[1].index]
        if a is None and b is None:
            continue
        odd = a if a is not None else b
        if odd % 2 == 1 and (a is None or b is None or a == b):
            doomed.append(e)
    bmesh.ops.dissolve_edges(bm, edges=doomed, use_verts=True)
    bm.to_mesh(me); bm.free(); me.update()
    return True


if LEVEL == "light":
    done = 0
    for o in meshes:
        if o.name in KEEP_APART:
            continue
        for m in o.modifiers:
            if m.type == 'BEVEL':
                m.segments = 1
        done += halve_segments(o)
    print("cylindres allégés :", done)

# Demi-tour autour de la lame, appliqué aux objets avant de les figer.
roll = Matrix.Rotation(ROLL, 4, 'Z')
for o in meshes:
    o.matrix_world = roll @ o.matrix_world
bpy.context.view_layer.update()

# Chaque pièce figée en maillage (modificateurs appliqués, normales pondérées conservées).
depsgraph = bpy.context.evaluated_depsgraph_get()
frozen = {}
for o in meshes:
    ev = o.evaluated_get(depsgraph)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=depsgraph)
    ob = bpy.data.objects.new("tmp_" + o.name, me)
    ob.matrix_world = ev.matrix_world.copy()
    frozen[o.name] = ob
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True) if o.name not in {f.name for f in frozen.values()} else None
for ob in frozen.values():
    scene.collection.objects.link(ob)

blade, ring = frozen.pop("Saber_Blade"), frozen.pop("Pommel_Ring")
parts = list(frozen.values())
for ob in parts:
    ob.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
hilt = bpy.context.view_layer.objects.active
for ob in (hilt, blade, ring):
    # La rotation passe dans le maillage (normales comprises) ; l'objet ne garde que sa position.
    loc = ob.matrix_world.translation.copy()
    keep = Matrix.Identity(4) if ob is hilt else Matrix.Translation(loc)
    ob.data.transform(keep.inverted() @ ob.matrix_world)
    ob.matrix_world = keep
bad = 0
hilt.data.calc_loop_triangles()
for p in hilt.data.polygons:
    for li in p.loop_indices:
        if hilt.data.corner_normals[li].vector.dot(p.normal) < 0.3: bad += 1
print("normales incohérentes :", bad, "/", len(hilt.data.loops))
for ob, name in ((hilt, "Saber_Hilt"), (blade, "Saber_Blade"), (ring, "Pommel_Ring")):
    ob.name = name; ob.data.name = name

def tris(ob):
    ob.data.calc_loop_triangles(); return len(ob.data.loop_triangles)
vs = [v.co for v in hilt.data.vertices]
print("RESULT", NAME, "hilt:", tris(hilt), "ring:", tris(ring), "blade:", tris(blade),
      "materials:", [m.name for m in hilt.data.materials],
      "min", [round(min(v[i] for v in vs), 3) for i in range(3)], "max", [round(max(v[i] for v in vs), 3) for i in range(3)],
      "blade base:", [round(x, 4) for x in blade.location], "ring pivot:", [round(x, 4) for x in ring.location])

for o in bpy.data.objects:
    o.hide_set(False); o.hide_render = False; o.select_set(True)
bpy.ops.export_scene.fbx(filepath=f"{OUT}/{NAME}.fbx", use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True, object_types={'MESH'},
                         mesh_smooth_type='OFF', use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False)

if RENDER:
    from mathutils import Vector, Quaternion
    blade.hide_render = True
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 64; scene.cycles.device = 'CPU'
    scene.render.resolution_x, scene.render.resolution_y = 1400, 700
    scene.render.resolution_percentage = 100
    scene.world = scene.world or bpy.data.worlds.new("W")
    def look(obj, target, roll=0.0):
        q = (Vector(target) - obj.location).to_track_quat('-Z', 'Y')
        obj.rotation_euler = (q @ Quaternion((0, 0, 1), roll)).to_euler()
    for loc, e in (((-0.6, 0.6, 0.5), 25), ((-0.5, -0.5, -0.2), 10), ((0.6, 0.3, 0.2), 8)):
        ld = bpy.data.lights.new("L", 'AREA'); ld.energy = e; ld.size = 0.6
        lo = bpy.data.objects.new("L", ld); lo.location = loc
        scene.collection.objects.link(lo); look(lo, (0, 0, -0.07))
    cd = bpy.data.cameras.new("C"); cd.lens = 85
    cam = bpy.data.objects.new("C", cd); scene.collection.objects.link(cam); scene.camera = cam
    cam.location = (-0.30, 0.50, 0.10)
    look(cam, (0, 0, -0.075), math.radians(90))
    scene.render.filepath = f"{OUT}/{NAME}_preview.png"
    bpy.ops.render.render(write_still=True)
