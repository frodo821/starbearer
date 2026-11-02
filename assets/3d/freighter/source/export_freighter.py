"""Export a meter-scale geometry preview without the authoring bevel modifiers."""
import bpy
import json
import struct
from pathlib import Path
from mathutils import Vector

OUT=Path(__file__).resolve().parent.parent
body=bpy.data.objects['Body']
scene=bpy.context.scene
preview_frame=scene.frame_current
scene.frame_set(1)
collection=bpy.data.collections.get('Freighter Game Preview')
if collection:
    assert all(o.name.startswith('Freighter_K40') for o in collection.objects)
    for o in list(collection.objects):bpy.data.objects.remove(o,do_unlink=True)
else:
    collection=bpy.data.collections.new('Freighter Game Preview')
    scene.collection.children.link(collection)
ramp_col=bpy.data.collections.get('08 Folding cargo ramp')
ramp=list(ramp_col.all_objects) if ramp_col else []
sources=[body]+[o for o in bpy.data.collections['Freighter Details'].all_objects if o not in ramp]
deps=bpy.context.evaluated_depsgraph_get()
copies=[]
for src in sources:
    if src==body:
        mesh=bpy.data.meshes.new_from_object(src.evaluated_get(deps))
    elif src.type=='MESH':
        mesh=src.data.copy()
    elif src.type=='FONT':
        mesh=bpy.data.meshes.new_from_object(src.evaluated_get(deps))
    else:continue
    mesh.transform(src.matrix_world)
    ob=bpy.data.objects.new('Export / '+src.name,mesh)
    collection.objects.link(ob)
    copies.append(ob)
bpy.ops.object.select_all(action='DESELECT')
for ob in copies:ob.select_set(True)
bpy.context.view_layer.objects.active=copies[0]
bpy.ops.object.join()
game=bpy.context.object
game.name='Freighter_K40'
game.data.name='Freighter_K40'
game['meters_per_unit']=1
game['class']='KESTREL K-40'
game['bow_axis']='-Z in glTF / Godot'
game['normal_maps_baked']=False
game.data.calc_loop_triangles()
count=len(game.data.loop_triangles)
points=[game.matrix_world@Vector(corner) for corner in game.bound_box]
dims=[max(p[k] for p in points)-min(p[k] for p in points) for k in range(3)]
assert all(abs(s-1)<1e-5 for s in game.scale)
assert 40<dims[1]<43 and 25<dims[0]<27
for ob in ramp:ob.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(OUT/'freighter.glb'),export_format='GLB',
    use_selection=True,export_apply=True,export_normals=True,
    export_animations=bool(ramp),export_animation_mode='SCENE',
    export_frame_range=True,export_force_sampling=True,
    export_anim_scene_split_object=False,export_cameras=False,export_lights=False,
    export_extras=True,export_yup=True)
game.hide_set(True)
game.hide_render=True
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
bpy.context.view_layer.objects.active=body
scene.frame_set(preview_frame)
binary=(OUT/'freighter.glb').read_bytes()
json_size=struct.unpack_from('<I',binary,12)[0]
gltf=json.loads(binary[20:20+json_size])
total_triangles=sum(gltf['accessors'][p['indices']]['count']//3
                    for mesh in gltf['meshes'] for p in mesh['primitives'])
report={'meters_per_unit':1,'base_hull_dimensions_m':[25,40,15],
        'full_dimensions_blender_xyz_m':[round(s,3) for s in dims],
        'triangles':total_triangles,'static_triangles':count,'materials':len(gltf['materials']),
        'bow_axis_godot':'-Z','scale':[1,1,1],'normal_maps_baked':False,
        'body_opening_and_bevel_exported':True,'purpose':'geometry preview for iteration',
        'ramp_animation_exported':bool(ramp),'ramp_closed_frame':1,'ramp_open_frame':48}
(OUT/'source'/'export_report.json').write_text(json.dumps(report,indent=2))
result=report
