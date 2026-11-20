import bpy, os, json, math
from mathutils import Vector, Quaternion
BASE=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
results={}
for file in ['Obsidienne.fbx','Obsidienne_Low.fbx']:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=os.path.join(BASE,'Unity','Obsidienne','Models',file))
    o=bpy.data.objects
    rotor=o['Rotor'];guard=o['Guard'];grip=o['Grip']
    assert o['Emitter_A'].parent==rotor and o['Emitter_B'].parent==rotor
    assert o['Socket_A'].parent==rotor and o['Socket_B'].parent==rotor
    assert o['Blade_A'].parent==o['Socket_A']
    assert abs(max(guard.dimensions)-.5)<.002,guard.dimensions
    fixed=[guard.matrix_world.copy(),grip.matrix_world.copy()]
    normal=(o['Axis_Normal'].matrix_world.translation-rotor.matrix_world.translation).normalized()
    localnormal=rotor.parent.matrix_world.to_3x3().inverted()@normal
    base=rotor.rotation_quaternion.copy() if rotor.rotation_mode=='QUATERNION' else rotor.rotation_euler.to_quaternion()
    rotor.rotation_mode='QUATERNION'
    for degrees in range(0,360,5):
        rotor.rotation_quaternion=Quaternion(localnormal,math.radians(degrees))@base
        bpy.context.view_layer.update()
        a=o['Socket_A'].matrix_world.translation-rotor.matrix_world.translation
        b=o['Socket_B'].matrix_world.translation-rotor.matrix_world.translation
        ar=a-normal*a.dot(normal);br=b-normal*b.dot(normal)
        assert (ar+br).length<.0001
        assert abs(ar.length-.335)<.0001
        assert guard.matrix_world==fixed[0] and grip.matrix_world==fixed[1]
        for tag in ['A','B']:
            tip=o['Tip_'+tag].matrix_world.translation
            socket=o['Socket_'+tag].matrix_world.translation
            assert abs((tip-socket).length-.85)<.0001
    meshes=[x for x in o if x.type=='MESH']
    triangles=0
    for obj in meshes:
        obj.data.calc_loop_triangles();triangles+=len(obj.data.loop_triangles)
        assert len(obj.data.uv_layers)>0
        assert all(math.isfinite(c) for v in obj.data.vertices for c in v.co)
    results[file]={'passed':True,'rotation_samples':72,'triangles':triangles,'mesh_count':len(meshes),'diameter_m':max(guard.dimensions)}
with open(os.path.join(BASE,'validation_model.json'),'w') as f:json.dump(results,f,indent=2)
print('VALIDATED',json.dumps(results))
