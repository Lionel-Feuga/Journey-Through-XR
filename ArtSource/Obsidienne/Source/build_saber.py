"""Blender 5.x — reconstruct the Obsidienne prop and its Unity export.
Run: blender --background --factory-startup --python build_saber.py
"""
import bpy, math, os, json
from mathutils import Vector
from math import sin, cos, pi

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEL = os.path.join(BASE, 'Unity', 'Obsidienne', 'Models')
os.makedirs(MODEL, exist_ok=True)
os.makedirs(os.path.join(BASE, 'Previews'), exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for d in list(bpy.data.materials): bpy.data.materials.remove(d)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1

def mat(name, color, metallic, rough, emission=0):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Metallic'].default_value=metallic
    p.inputs['Roughness'].default_value=rough
    if emission:
        p.inputs['Emission Color'].default_value=(*color,1)
        p.inputs['Emission Strength'].default_value=emission
    return m
metal=mat('Obsidian_Matte',(0.012,0.015,0.019),.7,.44)
rubber=mat('Grip_Carbon',(0.009,0.012,0.016),.05,.63)
edge=mat('Machined_Graphite',(0.036,0.042,0.050),.8,.32)
dark=mat('Recess_Black',(0.004,0.006,0.008),.45,.42)
cyan=mat('Status_Cyan',(0.015,.65,.85),.2,.24,3)
blade_mat=mat('Blade_Cyan',(.16,.85,1),.0,.24,6)

def empty(name, parent=None, loc=(0,0,0)):
    o=bpy.data.objects.new(name,None); scene.collection.objects.link(o)
    o.parent=parent; o.location=loc; o.empty_display_size=.035
    return o
root=empty('Obsidienne')
root['units']='meters'; root['guard_diameter_m']=.50
rotor=empty('Rotor',root)
axis=empty('Axis_Normal',root,(0,0,.1))
lock=empty('Axis_Locked',root,(0,.1,0))
grab=empty('Grab_Anchor',root,(0,0,.025))
parts={k:[] for k in ['Guard','Grip','Emitter_A','Emitter_B']}

def finish(o, name, material, group, bevel=0):
    o.name=name; o.data.materials.append(material)
    bpy.context.view_layer.objects.active=o
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        b=o.modifiers.new('Machined edge','BEVEL'); b.width=bevel; b.segments=2
        bpy.ops.object.modifier_apply(modifier=b.name)
    for p in o.data.polygons: p.use_smooth=True
    n=o.modifiers.new('Weighted normals','WEIGHTED_NORMAL'); n.keep_sharp=True; n.weight=40
    bpy.ops.object.modifier_apply(modifier=n.name)
    if group: parts[group].append(o)
    return o

def box(name,loc,dim,material,group,bevel=.001,rz=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    o=bpy.context.object; o.dimensions=dim; o.rotation_euler.z=rz
    return finish(o,name,material,group,bevel)

def cyl(name,loc,r,depth,material,group,axis='Y',vertices=48,bevel=.0006):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=r,depth=depth,location=loc)
    o=bpy.context.object
    if axis=='Y': o.rotation_euler.x=pi/2
    elif axis=='X': o.rotation_euler.y=pi/2
    return finish(o,name,material,group,bevel)

def annulus(name,inner,outer,z,depth,material,group,n=128,bevel=.0005):
    verts=[]
    for zz,rr in [(z-depth/2,inner),(z-depth/2,outer),(z+depth/2,inner),(z+depth/2,outer)]:
        verts.extend([(rr*cos(2*pi*i/n),rr*sin(2*pi*i/n),zz) for i in range(n)])
    faces=[]
    for i in range(n):
        j=(i+1)%n
        faces.extend([(i,j,n+j,n+i),(2*n+i,3*n+i,3*n+j,2*n+j),
                      (i,2*n+i,2*n+j,j),(n+i,n+j,3*n+j,3*n+i)])
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
    o=bpy.data.objects.new(name,mesh); scene.collection.objects.link(o)
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
    return finish(o,name,material,group,bevel)

# Fixed rail: the carriage travels on the front; fasteners are behind it.
annulus('Structural annulus',.208,.25,0,.018,metal,'Guard')
annulus('Outer polished bevel',.247,.250,.010,.002,edge,'Guard')
annulus('Inner polished bevel',.208,.211,.010,.002,edge,'Guard')
annulus('Recessed race',.220,.239,.0097,.001,dark,'Guard')
annulus('Inner guide',.218,.221,.012,.004,edge,'Guard')
annulus('Outer guide',.239,.242,.012,.004,edge,'Guard')
for a in [0,pi/2,pi,3*pi/2]:
    x,y=.233*cos(a),.233*sin(a)
    box('Rear rail splice',(x,y,-.014),(.034,.042,.012),metal,'Guard',.0015,a)
    for r in [.213,.247]:
        # Flush fasteners do not protrude into the carriage path.
        cyl('Rail fastener',(r*cos(a),r*sin(a),.0105),.0032,.001,edge,'Guard','Z',12,0)
        box('Fastener slot',(r*cos(a),r*sin(a),.0111),(.003,.0007,.0003),dark,'Guard',0,a)
for s in [-1,1]:
    box('Rear handle mount',(0,s*.202,-.015),(.037,.035,.016),metal,'Guard')
    box('Handle support',(0,s*.186,.005),(.025,.016,.045),metal,'Grip')

# Stationary handle, long ridged black grip and twin longitudinal exoskeleton rails.
cyl('Handle chassis',(0,0,.025),.023,.384,metal,'Grip')
cyl('Rubber sleeve',(0,0,.025),.024,.290,rubber,'Grip')
for i in range(21):
    y=-.14+i*.014
    cyl('Grip rib',(0,y,.025),.0255,.009,rubber,'Grip',vertices=48)
for y in [-.179,-.160,.160,.179]:
    cyl('Grip end band',(0,y,.025),.0265,.008,metal,'Grip')
for x in [-.018,.018]:
    box('Longitudinal armor',(x,0,.044),(.009,.34,.012),metal,'Grip',.002)
    box('Armor inset',(x,.025,.0502),(.004,.19,.001),dark,'Grip',.0004)
    box('Status strip',(x,-.045,.051),(.003,.036,.0015),cyan,'Grip',.001)
    for y in [-.146,.146]:
        cyl('Armor screw',(x,y,.051),.0035,.0015,edge,'Grip','Z',12,.0002)
        box('Screw recess',(x,y,.052),(.003,.0008,.0003),dark,'Grip',0)
cyl('Button bezel',(0,-.103,.050),.010,.006,edge,'Grip','Z',48)
cyl('Button light',(0,-.103,.0535),.008,.0015,cyan,'Grip','Z',48,.0003)
cyl('Button cap',(0,-.103,.0545),.0065,.0018,metal,'Grip','Z',48,.0004)
for i in range(4):
    box('Cooling slot',(0,.120+i*.009,.048),(.019,.003,.002),dark,'Grip',.0005)

def radial_tube(name,profile,sign,material,group):
    # Cross-section follows outer wall then inward over lip and down inner bore.
    n=48; verts=[]
    for y,r in profile:
        verts.extend([(r*cos(2*pi*i/n),sign*y,.025+r*sin(2*pi*i/n)) for i in range(n)])
    faces=[]
    for k in range(len(profile)):
        nk=(k+1)%len(profile)
        for i in range(n):
            j=(i+1)%n
            face=(k*n+i,k*n+j,nk*n+j,nk*n+i)
            faces.append(face if sign>0 else tuple(reversed(face)))
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
    o=bpy.data.objects.new(name,mesh); scene.collection.objects.link(o)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True)
    return finish(o,name,material,group,.0004)

for s,label in [(1,'A'),(-1,'B')]:
    g='Emitter_'+label
    # Open-sided race saddle is distinct from the stationary grip.
    box('Carriage saddle',(0,s*.230,.018),(.064,.026,.010),metal,g,.0015)
    for x in [-.026,.026]:
        box('Carriage cheek',(x,s*.231,.027),(.012,.034,.015),metal,g)
        cyl('Carriage bolt',(x,s*.231,.035),.003,.002,edge,g,'Z',12,.0002)
    radial_tube('Emitter body',[(.211,.024),(.225,.026),(.241,.026),(.250,.031),(.288,.031),(.303,.027),(.327,.022),(.335,.021),(.335,.015),(.305,.015),(.305,.010),(.211,.010)],s,metal,g)
    for y,r,w in [(.218,.026,.005),(.246,.032,.006),(.257,.033,.006),(.269,.034,.006),(.281,.034,.006),(.296,.030,.006)]:
        radial_tube('Emitter cooling fin',[(y-w/2,r-.004),(y-w/2,r),(y+w/2,r),(y+w/2,r-.004)],s,metal,g)
    radial_tube('Muzzle edge',[(.329,.0215),(.335,.0215),(.335,.015),(.329,.015)],s,edge,g)
    cyl('Deep emitter lens',(0,s*.312,.025),.012,.002,dark,g,vertices=32)
    cyl('Emitter lens core',(0,s*.314,.025),.007,.001,cyan,g,vertices=32)
    for a in [0,pi/2,pi,3*pi/2]:
        x,z=.022*cos(a),.025+.022*sin(a)
        o=box('Muzzle prong',(x,s*.326,z),(.004,.035,.004),metal,g,.0006)
    socket=empty('Socket_'+label,rotor,(0,s*.335,.025))
    tip=empty('Tip_'+label,socket,(0,s*.85,0))
    blade=cyl('Blade_'+label,(0,s*(.335+.425),.025),.012,.85,blade_mat,None,vertices=24,bevel=.006)
    # Put origin at blade base: independent ignition by scaling local Y.
    bpy.context.scene.cursor.location=(0,s*.335,.025)
    bpy.context.view_layer.objects.active=blade
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    # Bake primitive rotation so the blade axis remains model Y.
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    mw=blade.matrix_world.copy(); blade.parent=socket; blade.matrix_world=mw

# Consolidate meshes by moving assembly, preserving material slots and clean pivots.
for name,objs in parts.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:o.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]; bpy.ops.object.join()
    o=bpy.context.object; o.name=name
    bpy.context.scene.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    o.parent=rotor if name.startswith('Emitter') else root
    # Recalculate consistent outside normals and provide editable UVs.
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.015)
    bpy.ops.object.mode_set(mode='OBJECT')
scene.cursor.location=(0,0,0)
asset_objects=[root]+list(root.children_recursive)
for o in asset_objects:
    if o.type=='MESH':
        o.data.calc_loop_triangles()
        assert all(math.isfinite(v) for p in o.data.vertices for v in p.co)

# Export only the clean asset. No baked motion: Unity owns the rotor.
bpy.ops.object.select_all(action='DESELECT')
for o in asset_objects:o.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=os.path.join(MODEL,'Obsidienne.fbx'),use_selection=True,
    object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_UNITS',bake_anim=False,add_leaf_bones=False,
    use_custom_props=True,use_triangles=True)
bpy.ops.export_scene.gltf(filepath=os.path.join(BASE,'Obsidienne.glb'),export_format='GLB',
    use_selection=True,export_animations=False,export_extras=True)
stats={'blender':bpy.app.version_string,'meters':True,'meshes':{},'hierarchy':{o.name:o.parent.name if o.parent else None for o in asset_objects}}
for o in asset_objects:
    if o.type=='MESH': stats['meshes'][o.name]={'triangles':len(o.data.loop_triangles),'vertices':len(o.data.vertices),'materials':len(o.data.materials)}
stats['total_triangles']=sum(x['triangles'] for x in stats['meshes'].values())
with open(os.path.join(BASE,'model_report.json'),'w') as f:json.dump(stats,f,indent=2)

# Secondary export for closer-to-mobile budgets; preserve the same hierarchy.
original_meshes={}
low_counts={}
for o in asset_objects:
    if o.type!='MESH':continue
    original_meshes[o.name]=o.data
    o.data=o.data.copy()
    bpy.context.view_layer.objects.active=o
    if not o.name.startswith('Blade_'):
        mod=o.modifiers.new('VR reduction','DECIMATE');mod.ratio=.34
        mod.use_collapse_triangulate=True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    o.data.calc_loop_triangles();low_counts[o.name]=len(o.data.loop_triangles)
bpy.ops.export_scene.fbx(filepath=os.path.join(MODEL,'Obsidienne_Low.fbx'),use_selection=True,
    object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_UNITS',bake_anim=False,add_leaf_bones=False,
    use_custom_props=True,use_triangles=True)
for o in asset_objects:
    if o.name in original_meshes:o.data=original_meshes[o.name]
stats['low_triangles']=low_counts
stats['low_total_triangles']=sum(low_counts.values())
with open(os.path.join(BASE,'model_report.json'),'w') as f:json.dump(stats,f,indent=2)

# Studio collection stays out of FBX/GLB.
studio=bpy.data.collections.new('STUDIO_preview_only');scene.collection.children.link(studio)
def to_studio(o):
    for c in list(o.users_collection):c.objects.unlink(o)
    studio.objects.link(o)
floor=box('Studio floor',(0,0,-.064),(200,200,.02),mat('Studio',(0.027,.034,.045),.1,.52),None,.0);to_studio(floor)
world=bpy.data.worlds.new('Studio World');scene.world=world;world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.10,.13,.18,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.4
def area(name,loc,power,color,size):
    bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.name=name
    o.data.energy=power;o.data.color=color;o.data.shape='DISK';o.data.size=size
    o.rotation_euler=(Vector((0,0,0))-o.location).to_track_quat('-Z','Y').to_euler();to_studio(o)
area('Key softbox',(.35,-.35,1.1),12,(.80,.90,1),.85)
area('White rim',(-.6,.35,.60),16,(1,.93,.80),.70)
area('Blue fill',(.5,.45,.30),4,(.28,.64,1),.50)
bpy.ops.object.camera_add(location=(.62,-.88,1.16));cam=bpy.context.object;to_studio(cam)
cam.rotation_euler=(Vector((0,0,.01))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO';cam.data.ortho_scale=.77;scene.camera=cam
scene.render.engine='CYCLES';scene.cycles.samples=48
scene.cycles.use_denoising=True
scene.render.resolution_x=1500;scene.render.resolution_y=1500;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
scene.view_settings.exposure=-.7
for o in asset_objects:
    if o.name.startswith('Blade_'):o.hide_render=True;o.hide_set(True)
bpy.ops.object.select_all(action='DESELECT');root.select_set(True);bpy.context.view_layer.objects.active=root
for screen in bpy.data.screens:
    for ar in screen.areas:
        if ar.type=='VIEW_3D':
            ar.spaces.active.region_3d.view_distance=1.05
            ar.spaces.active.region_3d.view_location=(0,0,0)
            ar.spaces.active.clip_end=100
            ar.spaces.active.shading.type='MATERIAL'
scene.render.filepath=os.path.join(BASE,'Previews','01_Obsidienne_locked.png')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BASE,'Obsidienne.blend'))
bpy.ops.render.render(write_still=True)
# A second view explicitly demonstrates independently rotating emitters.
rotor.rotation_euler.z=math.radians(62)
for o in asset_objects:
    if o.name.startswith('Blade_'):o.hide_render=False;o.hide_set(False)
cam.location=(.8,-1.1,2.8);cam.rotation_euler=(Vector((0,0,0))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.ortho_scale=2.6
scene.render.resolution_x=1500;scene.render.resolution_y=1500
scene.render.filepath=os.path.join(BASE,'Previews','02_Obsidienne_rotor.png')
bpy.ops.render.render(write_still=True)
print('OB SIDIENNE SUCCESS',json.dumps(stats))
