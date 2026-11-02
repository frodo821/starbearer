"""Replace the tiled skin with continuous hull surfaces and a folding cargo ramp.

Works on the editable Freighter scene. Original Body coordinates remain intact;
the loading opening is a reversible Boolean modifier. Frame 1: closed; 48: open.
"""
import bpy
import bmesh
import math
import ast
import json
from pathlib import Path
from mathutils import Vector, Matrix

OUT=Path(__file__).resolve().parent.parent
body=bpy.data.objects['Body']
groups={c.name:c for c in bpy.data.collections['Freighter Details'].children}
tree=ast.parse((OUT/'source'/'build_freighter.py').read_text())
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.ClassDef) and n.name=='Batch'],
                       type_ignores=[]),str(OUT/'source'/'build_freighter.py'),'exec'))

def mat(name):return bpy.data.materials['Freighter / '+name]
ivory=mat('Warm ceramic hull')
slate=mat('Blue-grey cargo doors')
dark=mat('Recesses and structural frame')
metal=mat('Exposed brushed steel')
orange=mat('Safety ochre')
warm=mat('Work lights')
assert '08 Folding cargo ramp' not in groups, 'Ramp revision is already installed'
before=[tuple(v.co) for v in body.data.vertices]

# The source hull supplies the continuous outer skin; material breaks follow
# shoulders and the actual cargo recess rather than a surface grid.
body.data.materials.clear()
for m in [ivory,dark]:body.data.materials.append(m)
for p in body.data.polygons:
    shoulder=p.normal.z>.9 and abs(p.center.x)>1.01
    cargo_recess=abs(p.normal.x)>.95 and 1.45<abs(p.center.x)<1.6 and p.center.y>-.8
    p.material_index=1 if shoulder or cargo_recess else 0
for name in ['Meter-scale hull plating','Cockpit triangular cheek cladding']:
    ob=bpy.data.objects.get(name)
    if ob:bpy.data.objects.remove(ob,do_unlink=True)

seams=Batch('Bulkhead joints and service access','01 Hull plating',[dark,ivory,metal])
for y in [-7.3,9.3]:
    seams.box((0,y,5.006),(8.8,.025,.012),0)
for sign in [-1,1]:
    seams.box((sign*12.512,-15.5,-.35),(.025,1.25,1.85),0)
    seams.box((sign*12.535,-15.5,-.35),(.025,1.1,1.7),1)
    seams.box((sign*12.565,-15.85,-.35),(.05,.08,.28),2)
seams.finish(.005)

# Smooth, latched side covers replace the vertical shutter-like ribs.
old=bpy.data.objects.get('Six ribbed cargo hatches')
new=Batch('Six smooth cargo bay covers','02 Cargo bays',[slate,ivory,metal,orange,dark])
for sign in [-1,1]:
    x=sign*7.57
    for y in [-.95,5.35,11.65]:
        new.box((x,y,-1.25),(.20,6.05,5.65),4)
        new.box((x+sign*.13,y,-1.25),(.20,5.76,5.27),0)
        for yy in [y-2.78,y+2.78]:
            new.box((x+sign*.25,yy,-1.25),(.11,.11,5.25),2)
            for z in [-3.45,.95]:
                new.box((x+sign*.31,yy,z),(.10,.23,.25),3)
        for z in [-3.95,1.45]:
            new.box((x+sign*.25,y,z),(.11,5.70,.11),2)
        new.box((x+sign*.27,y-2.35,-1.25),(.11,.20,.65),2)
ob=new.finish(.035)
if old:bpy.data.objects.remove(old,do_unlink=True)

# Rebuild the access object without the rear shutter panel and its cross-ribs.
old=bpy.data.objects.get('Crew airlocks and rear loading ramp')
access=Batch('Crew airlocks and rear cargo portal','03 Cockpit and access',
             [slate,ivory,dark,orange,metal,warm])
for sign in [-1,1]:
    x=sign*5.08
    access.box((x,10.8,-6.5),(.16,1.23,2.35),2)
    access.box((x+sign*.10,10.8,-6.5),(.12,.95,2.05),0)
    access.box((x+sign*.18,10.8,-5.91),(.08,.48,.24),4)
    access.box((x+sign*.18,10.38,-6.68),(.10,.08,.33),3)
    access.box((x+sign*.12,10.8,-7.76),(.55,1.5,.18),4)
    for z in [-8.2,-8.65,-9.1]:access.box((sign*4.8,10.8,z),(.52,.9,.09),4)
for x in [-2.96,2.96]:
    access.box((x,-20.18,-6.75),(.23,.42,3.80),4)
for z in [-8.52,-4.98]:
    access.box((0,-20.18,z),(6.15,.42,.20),4)
# A real recessed throat, floor and rear wall sit behind the opening.
access.box((0,-17.18,-8.47),(5.58,6.15,.12),4)
access.box((0,-14.25,-6.75),(5.60,.15,3.4),2)
for x in [-2.63,2.63]:
    access.box((x,-17.2,-6.75),(.13,5.9,3.3),2)
    access.box((x,-18.1,-5.15),(.08,2.7,.09),5)
access.box((0,-19.75,-8.41),(5.4,.55,.035),4)
access.finish(.02)
if old:bpy.data.objects.remove(old,do_unlink=True)

construction=bpy.data.collections.new('Freighter Construction')
bpy.context.scene.collection.children.link(construction)
me=bpy.data.meshes.new('Reversible cargo opening cutter')
bm=bmesh.new()
bmesh.ops.create_cube(bm,size=1)
bm.to_mesh(me)
bm.free()
cutter=bpy.data.objects.new('Cargo portal cutter / do not export',me)
construction.objects.link(cutter)
cutter.location=(0,-18.05,-6.725)
cutter.scale=(5.60,8.90,3.35)
cutter.display_type='WIRE'
cutter.hide_render=True
cutter.hide_set(True)
boolean=body.modifiers.new('Rear cargo opening / reversible','BOOLEAN')
boolean.operation='DIFFERENCE'
boolean.solver='EXACT'
boolean.object=cutter
# Weld duplicate blockout vertices in the evaluated mesh before cutting. This
# avoids bevel/triangulation artifacts while preserving the original mesh data.
weld=body.modifiers.new('Resolve coincident blockout vertices','WELD')
weld.merge_threshold=.00001
# Cut before the small hull bevel so the cargo throat gets a manufactured edge.
bpy.context.view_layer.objects.active=body
bpy.ops.object.modifier_move_up(modifier=boolean.name)
bpy.ops.object.modifier_move_up(modifier=weld.name)
bpy.ops.object.modifier_move_up(modifier=weld.name)

# Three solid panels fold into a 3.34m-high rear door. The same panels unfold
# to an 8.84m vehicle ramp, keeping the slope below 19 degrees at ground level.
ramp_col=bpy.data.collections.new('08 Folding cargo ramp')
bpy.data.collections['Freighter Details'].children.link(ramp_col)
groups[ramp_col.name]=ramp_col
scene=bpy.context.scene
scene.frame_start=1
scene.frame_end=48
lengths=[3.34,2.90,2.60]
theta=math.asin(2.82/sum(lengths))
root=bpy.data.objects.new('CargoRamp / door hinge',None)
ramp_col.objects.link(root)
root.location=(0,-20.28,-8.45)
root.rotation_mode='XYZ'
root['function']='Frame 1 closed; frame 48 deployed. Solid door unfolds into ground ramp.'
root['slope_degrees']=math.degrees(theta)
root['extended_length_m']=sum(lengths)
for frame,angle in [(1,-math.pi/2),(24,theta),(48,theta)]:
    root.rotation_euler.x=angle
    root.keyframe_insert('rotation_euler',frame=frame,group='Ramp deployment')
pivots=[root]
for index in [1,2]:
    pivot=bpy.data.objects.new('CargoRamp / fold hinge '+str(index),None)
    ramp_col.objects.link(pivot)
    pivot.parent=pivots[-1]
    pivot.rotation_mode='XYZ'
    first,last=(18,36) if index==1 else (30,48)
    depth=-.28 if index==1 else .28
    for frame,angle,z in [(1,math.pi,depth),(first,math.pi,depth),(last,0,0),(48,0,0)]:
        pivot.location=(0,-lengths[index-1],z)
        pivot.rotation_euler.x=angle
        pivot.keyframe_insert('location',frame=frame,group='Ramp deployment')
        pivot.keyframe_insert('rotation_euler',frame=frame,group='Ramp deployment')
    pivots.append(pivot)

for index,(pivot,length) in enumerate(zip(pivots,lengths)):
    panel=Batch('CargoRamp / solid deck '+str(index+1),ramp_col.name,
                [slate,metal,orange,dark,warm])
    width=5.47-index*.08
    panel.box((0,-length/2,-.09),(width,length-.045,.18),0)
    # Longitudinal wheel tracks and raised curbs replace transverse shutter ribs.
    for sign in [-1,1]:
        panel.box((sign*1.6,-length/2,.012),(.82,length-.12,.025),1)
        panel.box((sign*(width/2-.09),-length/2,.08),(.13,length-.06,.16),1)
        panel.box((sign*(width/2-.17),-length/2,.012),(.07,length-.10,.025),2)
        panel.box((sign*(width/2-.19),-.35,.09),(.06,.32,.045),4)
    panel.cylinder((-width/2,0,-.03),(width/2,0,-.03),.09,mi=1,sides=12)
    ob=panel.finish(.018)
    ob.parent=pivot
    ob.matrix_parent_inverse=Matrix.Identity(4)
    ob.location=(0,0,0)
    # The exterior face is a clean door when the ramp is stowed.
    if index==0:
        latch=Batch('CargoRamp / exterior door braces',ramp_col.name,[slate,metal,orange])
        for sign in [-1,1]:
            latch.box((sign*2.40,-1.70,-.205),(.15,2.55,.10),1)
            latch.box((sign*2.40,-.55,-.275),(.22,.25,.06),2)
        brace=latch.finish(.02)
        brace.parent=pivot
        brace.matrix_parent_inverse=Matrix.Identity(4)
        brace.location=(0,0,0)

# Two telescopic actuators support the first panel; sampled transforms export
# cleanly without drivers, constraints or an armature.
for sign in [-1,1]:
    start=Vector((sign*2.99,-19.92,-5.32))
    batch=Batch('CargoRamp / actuator '+str(sign),ramp_col.name,[metal,dark])
    batch.cylinder((0,0,0),(0,0,1),.075,mi=0,sides=12)
    actuator=batch.finish(.008)
    actuator.parent=None
    actuator.matrix_parent_inverse=Matrix.Identity(4)
    actuator.rotation_mode='QUATERNION'
    for frame in range(1,49):
        scene.frame_set(frame)
        end=root.matrix_world@Vector((sign*2.90,-2.15,.12))
        delta=end-start
        actuator.location=start
        actuator.rotation_quaternion=delta.to_track_quat('Z','Y')
        actuator.scale=(1,1,delta.length)
        for path in ['location','rotation_quaternion','scale']:
            actuator.keyframe_insert(path,frame=frame,group='Ramp actuators')

scene.frame_set(48)
bpy.context.view_layer.update()
assert before==[tuple(v.co) for v in body.data.vertices]
report={'surface':'continuous hull, two structural roof joints; no tiled cladding',
        'side_cargo_covers':'six smooth fixed covers',
        'loading':'rear three-panel folding ramp and actual hull opening',
        'closed_frame':1,'deployed_frame':48,
        'ramp_length_m':sum(lengths),'ramp_width_m':5.47,
        'ramp_slope_degrees':round(math.degrees(theta),2),'meters_per_unit':1,
        'original_body_vertices_preserved':True}
(OUT/'source'/'surface_and_ramp_report.json').write_text(json.dumps(report,indent=2))
result=report
