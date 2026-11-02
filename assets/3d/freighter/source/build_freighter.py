"""Editable 1:1, forty-meter freighter developed from the Body silhouette.

Run inside Blender with the freighter blockout open. Surface/ramp and silhouette
revisions run after the initial assembly. Detail coordinates are world meters.
"""
import bpy
import bmesh
import math
import random
import json
from pathlib import Path
from mathutils import Vector, Matrix

OUT=Path(__file__).resolve().parent.parent
OUT.mkdir(parents=True,exist_ok=True)
body=bpy.data.objects['Body']
assert bpy.context.mode=='OBJECT'
assert 'Freighter Details' not in bpy.data.collections
original_vertices=[tuple(v.co) for v in body.data.vertices]
original_matrix=body.matrix_world.copy()
rng=random.Random(40125)
scene=bpy.context.scene
scene.unit_settings.system='METRIC'
scene.unit_settings.scale_length=1
details=bpy.data.collections.new('Freighter Details')
scene.collection.children.link(details)
groups={}
for name in ['01 Hull plating','02 Cargo bays','03 Cockpit and access',
             '04 Propulsion','05 Services','06 Landing gear','07 Markings']:
    c=bpy.data.collections.new(name)
    details.children.link(c)
    groups[name]=c

def material(name,color,metal=.25,rough=.6,emission=0):
    m=bpy.data.materials.new('Freighter / '+name)
    m.diffuse_color=(*color,1)
    m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Metallic'].default_value=metal
    p.inputs['Roughness'].default_value=rough
    if emission:
        p.inputs['Emission Color'].default_value=(*color,1)
        p.inputs['Emission Strength'].default_value=emission
    return m

ivory=material('Warm ceramic hull',(.59,.57,.50),.28,.63)
ivory2=material('Replacement hull plates',(.69,.66,.57),.22,.66)
slate=material('Blue-grey cargo doors',(.105,.20,.23),.35,.55)
dark=material('Recesses and structural frame',(.025,.038,.044),.45,.65)
metal=material('Exposed brushed steel',(.22,.26,.28),.75,.36)
orange=material('Safety ochre',(.80,.32,.065),.22,.6)
glass=material('Smoked cockpit glazing',(.018,.077,.10),.65,.16)
warm=material('Work lights',(.94,.78,.51),.1,.32,3)
ion=material('Engine discharge',(.13,.48,.70),.1,.4,3)
red=material('Port navigation',(.75,.015,.009),.1,.4,2)
green=material('Starboard navigation',(.01,.48,.12),.1,.4,2)
body.data.materials.clear()
body.data.materials.append(dark)
bev=body.modifiers.new('Hull edge radii / 0.10m','BEVEL')
bev.width=.02
bev.segments=2

class Batch:
    def __init__(self,name,group,mats):
        self.name,self.group,self.mats=name,group,mats
        self.v,self.f,self.mi=[],[],[]
    def poly(self,verts,faces,mi=0):
        base=len(self.v)
        self.v.extend(tuple(v) for v in verts)
        self.f.extend(tuple(base+i for i in f) for f in faces)
        self.mi.extend([mi]*len(faces))
    def prism(self,corners,n,depth,mi=0):
        c=[Vector(v) for v in corners]
        delta=Vector(n)*depth
        self.poly(c+[v+delta for v in c],[(0,3,2,1),(4,5,6,7),
                  (0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mi)
    def box(self,center,size,mi=0,taper=1):
        x,y,z=center
        a,b,c=[s/2 for s in size]
        self.poly([(x-a,y-b,z-c),(x+a,y-b,z-c),(x+a,y+b,z-c),(x-a,y+b,z-c),
                   (x-a*taper,y-b*taper,z+c),(x+a*taper,y-b*taper,z+c),
                   (x+a*taper,y+b*taper,z+c),(x-a*taper,y+b*taper,z+c)],
                  [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mi)
    def cylinder(self,start,end,r1,r2=None,mi=0,sides=16,caps=True):
        a,b=Vector(start),Vector(end)
        n=(b-a).normalized()
        u=n.cross(Vector((0,0,1)))
        if u.length<.01:u=n.cross(Vector((0,1,0)))
        u.normalize()
        v=n.cross(u)
        r2=r1 if r2 is None else r2
        pts=[p+r*(u*math.cos(i*math.tau/sides)+v*math.sin(i*math.tau/sides))
             for p,r in [(a,r1),(b,r2)] for i in range(sides)]
        faces=[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
        if caps:faces += [tuple(reversed(range(sides))),tuple(range(sides,sides*2))]
        self.poly(pts,faces,mi)
    def beam(self,a,b,width,mi=0):
        a,b=Vector(a),Vector(b)
        n=(b-a).normalized()
        u=n.cross(Vector((0,0,1)))
        if u.length<.01:u=n.cross(Vector((0,1,0)))
        u.normalize()
        u*=width/2
        v=n.cross(u)
        pts=[p+su*u+sv*v for p in [a,b] for su,sv in [(-1,-1),(1,-1),(1,1),(-1,1)]]
        self.poly(pts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mi)
    def finish(self,bevel=0):
        if not self.v:return None
        me=bpy.data.meshes.new(self.name)
        me.from_pydata(self.v,[],self.f)
        me.update()
        for m in self.mats:me.materials.append(m)
        for p,i in zip(me.polygons,self.mi):p.material_index=i
        bm=bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(me)
        bm.free()
        ob=bpy.data.objects.new(self.name,me)
        groups[self.group].objects.link(ob)
        ob.parent=body
        ob.matrix_parent_inverse=body.matrix_world.inverted()
        if bevel:
            m=ob.modifiers.new('Manufactured edge radii','BEVEL')
            m.width=bevel
            m.segments=2
        return ob

plates=Batch('Meter-scale hull plating','01 Hull plating',[ivory,ivory2,slate])
frame=Batch('Cargo rack structural rails','02 Cargo bays',[dark,metal,orange])
cargo=Batch('Six ribbed cargo hatches','02 Cargo bays',[slate,ivory,metal,orange,dark])
cockpit=Batch('Cockpit glazing and coaming','03 Cockpit and access',[glass,metal,dark,ivory])
access=Batch('Crew airlocks and rear loading ramp','03 Cockpit and access',[slate,ivory,dark,orange,metal])
engines=Batch('Twin inset propulsion units','04 Propulsion',[dark,metal,ivory,ion,orange])
services=Batch('Radiators piping and maneuvering jets','05 Services',[dark,metal,ivory,orange])
gear=Batch('Three-point articulated landing gear','06 Landing gear',[metal,dark,ivory,orange])
light=Batch('Navigation and working lights','05 Services',[warm,red,green])

# Match the blockout's actual faces; no synthetic hull profile is assumed.
normal_matrix=body.matrix_world.to_3x3().inverted().transposed()
plate_count=0
for face in body.data.polygons:
    if len(face.vertices)==3:
        n=(normal_matrix@face.normal).normalized()
        pts=[body.matrix_world@body.data.vertices[i].co+n*.02 for i in face.vertices]
        plates.poly(pts+[v+n*.045 for v in pts],[(0,2,1),(3,4,5),
                    (0,1,4,3),(1,2,5,4),(2,0,3,5)])
        plate_count+=1
        continue
    if len(face.vertices)!=4:continue
    a,b,c,d=[body.matrix_world@body.data.vertices[i].co for i in face.vertices]
    n=(normal_matrix@face.normal).normalized()
    def pos(u,v):return a.lerp(b,u).lerp(d.lerp(c,u),v)
    nu=max(1,round(((a-b).length+(d-c).length)/2/3.2))
    nv=max(1,round(((a-d).length+(b-c).length)/2/3.2))
    for i in range(nu):
        for j in range(nv):
            gu=min(.12,.035/max((a-b).length/nu,.1))
            gv=min(.12,.035/max((a-d).length/nv,.1))
            uv=[((i+gu)/nu,(j+gv)/nv),((i+1-gu)/nu,(j+gv)/nv),
                ((i+1-gu)/nu,(j+1-gv)/nv),((i+gu)/nu,(j+1-gv)/nv)]
            pts=[pos(u,v)+n*.015 for u,v in uv]
            plates.prism(pts,n,.045,1 if rng.random()<.17 else 0)
            plate_count+=1

# Six 6m loading bays on the exposed flanks, with two 2.5m hatch leaves each.
for sign in [-1,1]:
    x=sign*7.57
    for index,y in enumerate([-.95,5.35,11.65]):
        cargo.box((x,y,-1.25),(.20,6.05,5.65),4)
        for z in [-2.65,.05]:
            cargo.box((x+sign*.12,y,z),(.20,5.80,2.53),0)
            for k in range(12):
                cargo.box((x+sign*.235,y-2.63+k*.478,z),(.07,.10,2.30),0)
            for edge in [-1,1]:
                cargo.box((x+sign*.28,y+edge*2.74,z),(.12,.13,2.48),2)
                for zz in [-.88,.88]:
                    cargo.box((x+sign*.36,y+edge*2.75,z+zz),(.08,.24,.18),3)
        frame.box((x+sign*.05,y,1.63),(.32,6.18,.18),1)
        frame.box((x+sign*.05,y,-4.18),(.32,6.18,.24),1)
        for yy in [y-3.03,y+3.03]:
            frame.box((x+sign*.05,yy,-1.25),(.35,.20,5.7),1)
        # Work lights are decimeter fixtures, not capital-ship window rows.
        light.box((x+sign*.25,y,1.87),(.14,.55,.12),0)
    # Protective longitudinal sill and a few hazard strips.
    frame.box((sign*7.77,5.3,-4.55),(.32,20.2,.36),0)
    for y in [-3.3,-2.7,14,14.6]:
        frame.box((sign*7.96,y,-4.54),(.04,.25,.28),2)

# Front bevel: y+z=22.5m. Two large cockpit panes remain physically attached.
n=Vector((0,1,1)).normalized()
for left,right in [(-4.30,-.18),(.18,4.30)]:
    corners=[Vector((left,17.95,4.55)),Vector((right,17.95,4.55)),
             Vector((right,19.32,3.18)),Vector((left,19.32,3.18))]
    cockpit.prism([p+n*.10 for p in corners],n,.065,0)
    for a,b in zip(corners,corners[1:]+corners[:1]):
        cockpit.beam(a+n*.17,b+n*.17,.14,1)
for sign in [-1,1]:
    cockpit.box((sign*5.10,15.95,3.80),(.10,2.10,.85),0)
    cockpit.box((sign*5.17,15.95,4.29),(.20,2.35,.10),1)
    services.box((sign*3.65,20.08,-.85),(.75,.30,.50),0)
    light.box((sign*3.65,20.26,-.85),(.54,.045,.26),0)
    light.box((sign*7.85,15.50,.65),(.15,.18,.16),1 if sign<0 else 2)

# Crew doors are 0.95m wide and 2.05m high on the lower central keel.
for sign in [-1,1]:
    x=sign*5.08
    access.box((x,10.8,-6.5),(.16,1.23,2.35),2)
    access.box((x+sign*.10,10.8,-6.5),(.12,.95,2.05),0)
    access.box((x+sign*.18,10.8,-5.91),(.08,.48,.24),4)
    access.box((x+sign*.18,10.38,-6.68),(.10,.08,.33),3)
    access.box((x+sign*.12,10.8,-7.76),(.55,1.5,.18),4)
    for z in [-8.2,-8.65,-9.1]:
        access.box((sign*4.8,10.8,z),(.52,.9,.09),4)
    light.box((x+sign*.15,10.8,-5.19),(.16,.42,.12),0)
access.box((0,-20.09,-6.75),(6.15,.26,3.8),2)
access.box((0,-20.26,-6.75),(5.70,.14,3.30),0)
for x in [-2.92,2.92]:
    access.box((x,-20.38,-6.75),(.18,.18,3.55),4)
for z in [-8.49,-5.01]:
    access.box((0,-20.38,z),(6.0,.20,.16),4)
for x in [-2.55,-1.8,-1.05,-.3,.45,1.2,1.95,2.7]:
    access.box((x,-20.50,-8.51),(.38,.03,.13),3)
for z in [-7.85,-7.1,-6.35,-5.6]:
    access.box((0,-20.35,z),(5.6,.04,.035),4)

# Twin 3.3m diameter nozzles; short, recessed mounting housings meet the stern.
for sign in [-1,1]:
    x,z=sign*9.0,-1.10
    # Chamfered service cassettes break up the flat stern behind each engine.
    outline=[(-2.55,-2.85),(-2.15,-3.20),(2.15,-3.20),(2.55,-2.85),
             (2.55,2.85),(2.15,3.20),(-2.15,3.20),(-2.55,2.85)]
    pts=[(x+xx,y,z+zz) for y in [-19.86,-20.22] for xx,zz in outline]
    engines.poly(pts,[tuple(reversed(range(8))),tuple(range(8,16))]
                 +[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)],0)
    for zz in [-3.88,1.55]:
        engines.box((x,-20.28,zz),(3.95,.14,.60),1)
        for k in range(10):
            engines.box((x-1.70+k*.375,-20.38,zz),(.15,.12,.43),0)
    for xx in [-2.28,2.28]:
        engines.box((x+xx,-20.31,-1.10),(.22,.20,3.0),2)
        engines.box((x+xx,-20.44,-2.32),(.20,.05,.33),4)
    rings=[(-19.75,2.18),(-20.28,1.94),(-21.12,1.65),(-21.12,1.42),(-20.45,.90)]
    count=32
    pts=[(x+r*math.cos(k*math.tau/count),y,z+r*math.sin(k*math.tau/count))
         for y,r in rings for k in range(count)]
    for row,mi in [(0,2),(1,0),(2,1),(3,1)]:
        engines.poly(pts,[(row*count+k,row*count+(k+1)%count,
                      (row+1)*count+(k+1)%count,(row+1)*count+k) for k in range(count)],mi)
    engines.cylinder((x,-20.43,z),(x,-20.46,z),.91,mi=3,sides=32)
    for k in range(12):
        angle=k*math.tau/12
        xx,zz=x+1.96*math.cos(angle),z+1.96*math.sin(angle)
        engines.box((xx,-20.08,zz),(.16,.54,.15),1)
    engines.cylinder((sign*11.70,-19.85,.65),(sign*11.70,-20.35,.65),.38,.48,0,16,False)
    engines.cylinder((sign*11.70,-20.31,.65),(sign*11.70,-20.33,.65),.35,mi=3,sides=16)
    for zz in [-3.65,1.6]:
        services.cylinder((sign*6.8,-20.13,zz),(sign*10.8,-20.13,zz),.085,mi=1,sides=12)
        for xx in [6.9,8.0,10.7]:
            services.box((sign*xx,-20.13,zz),(.16,.28,.24),2)
    # Shoulder radiators and protected fuel / coolant runs.
    services.box((sign*9.0,-13.3,2.66),(4.6,8.7,.24),0)
    for k in range(24):
        services.box((sign*9.0,-17.2+k*.34,2.87),(4.15,.12,.20),1)
    services.box((sign*9.0,-8.55,2.67),(4.4,.28,.38),3)
    for xx in [6.55,11.45]:
        services.cylinder((sign*xx,-18.5,2.72),(sign*xx,-8.7,2.72),.13,mi=1,sides=12)
        for yy in [-18,-15,-12,-9]:
            services.box((sign*xx,yy,2.72),(.38,.20,.36),2)
    for y in [-17.4,15.7]:
        xx=12.55 if y<0 else 7.55
        services.box((sign*xx,y,.8),(.25,1.05,.8),0)
        for yy in [-.25,.25]:
            services.cylinder((sign*(xx+.08),y+yy,.8),(sign*(xx+.42),y+yy,.8),.13,.20,1,12,False)
            services.cylinder((sign*(xx+.35),y+yy,.8),(sign*(xx+.38),y+yy,.8),.15,mi=0,sides=12)

# Dorsal maintenance fittings remain low rather than forming a bridge tower.
services.box((0,-12.6,5.18),(6.8,7.8,.26),0)
for sign in [-1,1]:
    for k in range(13):
        services.box((sign*1.8,-15.8+k*.5,5.4),(2.6,.13,.20),1)
    services.box((sign*3.65,-12.6,5.38),(.22,7.2,.30),2)
    services.cylinder((sign*3.1,-6.9,5.15),(sign*3.1,-4.0,5.15),.14,mi=1,sides=12)
    services.box((sign*3.1,-5.45,5.15),(.46,1.15,.42),2)
services.box((0,12.7,5.13),(1.65,1.65,.20),2)
services.box((0,12.7,5.26),(1.25,1.25,.08),0)
services.cylinder((3.5,9.5,5.1),(3.5,9.5,6.40),.055,.025,1,12)
services.box((3.5,9.5,6.12),(.08,.56,.08),1)

# Rear legs splay from the keel; the front leg completes a stable three-point footprint.
for sign in [-1,1]:
    a=(sign*4.5,-13.8,-8.75)
    b=(sign*6.2,-13.8,-10.95)
    gear.cylinder(a,b,.28,.22,mi=0,sides=16)
    gear.beam((sign*3.8,-12.0,-9.15),(sign*6.2,-13.8,-10.95),.24,1)
    gear.cylinder((sign*4.2,-13.8,-9.1),(sign*5.9,-13.8,-10.8),.12,mi=0,sides=12)
    gear.box((sign*4.5,-13.8,-8.8),(.85,1.45,.60),2)
    gear.box((sign*6.2,-13.8,-11.15),(1.75,2.85,.38),1,.9)
    gear.box((sign*6.2,-13.8,-10.99),(1.5,2.5,.12),0)
    gear.box((sign*6.2,-14.9,-10.9),(1.5,.16,.10),3)
gear.cylinder((0,10,-9.8),(0,10,-10.98),.24,.19,mi=0,sides=16)
gear.beam((0,8.5,-9.8),(0,10,-10.95),.20,1)
gear.box((0,10,-11.15),(1.65,2.60,.38),1,.9)
gear.box((0,10,-10.99),(1.40,2.25,.12),0)

for batch,bevel in [(plates,.025),(frame,.045),(cargo,.035),(cockpit,.025),
                     (access,.025),(engines,.025),(services,.025),(gear,.035),(light,.015)]:
    batch.finish(bevel)

def text(name,content,loc,size,rotation,mat,align='CENTER'):
    data=bpy.data.curves.new(name,'FONT')
    data.body=content
    data.size=size
    data.align_x=align
    data.extrude=.001
    ob=bpy.data.objects.new(name,data)
    groups['07 Markings'].objects.link(ob)
    ob.location=loc
    ob.rotation_euler=rotation
    ob.parent=body
    ob.matrix_parent_inverse=body.matrix_world.inverted()
    ob.data.materials.append(mat)
    return ob

text('Dorsal registration','K E S T R E L',(0,4.7,5.11),.72,(0,0,math.pi),slate)
text('Dorsal class stencil','FREIGHTER  /  K-40',(0,3.25,5.11),.31,(0,0,math.pi),dark)
for sign in [-1,1]:
    for index,y in enumerate([-.95,5.35,11.65]):
        text('Cargo bay stencil '+str(sign)+' '+str(index),f'{index+1:02}  /  CARGO',
             (sign*7.87,y,1.15),.22,(math.pi/2,0,sign*math.pi/2),ivory2)
    text('Engine shoulder registry '+str(sign),'K-40',
         (sign*9,-10.0,3.02),.6,(0,0,math.pi),ivory2)

# Safety chevrons emphasize working edges, using centimeters rather than meters.
stripes=Batch('Safety paint and roof walkways','07 Markings',[orange,dark,ivory2])
for sign in [-1,1]:
    stripes.box((sign*4.65,-1.8,5.105),(.17,25.4,.018),0)
    for y in [-18,-17.4,-16.8,-16.2]:
        stripes.box((sign*11.9,y,2.60),(.38,.30,.025),0)
for x in [-3.5,-2.5,-1.5,-.5,.5,1.5,2.5,3.5]:
    stripes.box((x,16.8,5.105),(.50,.22,.025),0)
stripes.finish(0)

body['class']='KESTREL K-40 utility freighter'
body['meters_per_unit']=1.0
body['base_hull_dimensions_m']=[25.,40.,15.]
body['bow_axis']='+Y (Blender), -Z (glTF / Godot)'
details['design']='Six cargo bays, twin main drives, two-seat cockpit, 2.05m crew airlocks, landing gear'
assert original_vertices==[tuple(v.co) for v in body.data.vertices]
assert body.matrix_world==original_matrix

studio=bpy.data.collections.new('Freighter Preview Studio')
scene.collection.children.link(studio)
def studio_object(name,data,loc,target=(0,0,-1)):
    ob=bpy.data.objects.new(name,data)
    studio.objects.link(ob)
    ob.location=loc
    ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()
    return ob
cam=studio_object('Freighter Preview Camera',bpy.data.cameras.new('Freighter Preview Camera'),(43,53,36))
cam.data.type='ORTHO'
cam.data.ortho_scale=58
scene.camera=cam
for name,loc,power,size,color in [
    ('Key',(10,16,40),32000,26,(1,.92,.81)),
    ('Fill',(-26,12,18),24000,24,(.63,.78,1)),
    ('Rim',(12,-32,28),40000,22,(1,.80,.55)),
    ('Belly',(0,0,-24),6000,18,(.62,.76,1))]:
    data=bpy.data.lights.new('Freighter '+name,'AREA')
    data.energy=power
    data.shape='DISK'
    data.size=size
    data.color=color
    studio_object('Freighter '+name,data,loc)
world=bpy.data.worlds.new('Freighter Preview World')
world.use_nodes=True
world.node_tree.nodes['Background'].inputs['Color'].default_value=(.023,.032,.044,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=.4
scene.world=world
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=1600
scene.render.resolution_y=1200
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.view_settings.view_transform='AgX'
scene.render.filepath=str(OUT/'source'/'freighter_bow.png')
for ob in bpy.context.selected_objects:ob.select_set(False)
body.select_set(True)
bpy.context.view_layer.objects.active=body
for area in bpy.context.screen.areas:
    if area.type!='VIEW_3D':continue
    s=area.spaces.active
    s.shading.type='SOLID'
    s.shading.color_type='MATERIAL'
    s.shading.show_cavity=True
    s.shading.cavity_type='BOTH'
    s.overlay.show_extras=False
    s.overlay.show_floor=False
    s.overlay.show_axis_x=False
    s.overlay.show_axis_y=False
    s.show_region_ui=False
    s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
    s.region_3d.view_location=(0,0,-1)
    s.region_3d.view_distance=48
    s.region_3d.view_perspective='ORTHO'
bpy.context.view_layer.update()
revision=OUT/'source'/'refine_freighter.py'
if revision.exists():
    revision_ns={'__file__':str(revision)}
    exec(compile(revision.read_text(),str(revision),'exec'),revision_ns)
    plate_count=0
silhouette=OUT/'source'/'reshape_freighter.py'
if silhouette.exists():
    silhouette_ns={'__file__':str(silhouette)}
    exec(compile(silhouette.read_text(),str(silhouette),'exec'),silhouette_ns)
industrial=OUT/'source'/'add_industrial_detail.py'
if industrial.exists():
    industrial_ns={'__file__':str(industrial)}
    exec(compile(industrial.read_text(),str(industrial),'exec'),industrial_ns)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'source'/'freighter.blend'))
report={'class':'KESTREL K-40','meters_per_unit':1,'base_hull_dimensions_m':[25,40,15],
        'base_hull_preserved':not body.get('silhouette_revision',False),
        'hull_dimensions_m':[round(v,3) for v in body.dimensions],
        'hull_plates':plate_count,'cargo_bays':6,
        'main_engines':2,'crew_airlock_height_m':2.05,
        'blender_bow_axis':'+Y','godot_bow_axis':'-Z'}
(OUT/'source'/'design_report.json').write_text(json.dumps(report,indent=2))
result=report
