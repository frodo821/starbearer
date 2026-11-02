"""Reinterpret the blockout silhouette as a swept, chamfered utility freighter.

Prerequisite: build_freighter.py and its surface/ramp revision. World dimensions
remain in meters. The existing articulated ramp and cargo opening are retained.
"""
import bpy
import bmesh
import math
import ast
import json
from pathlib import Path
from mathutils import Vector

OUT=Path(__file__).resolve().parent.parent
body=bpy.data.objects['Body']
assert not body.get('silhouette_revision',False), 'Silhouette revision already applied'
groups={c.name:c for c in bpy.data.collections['Freighter Details'].children}
tree=ast.parse((OUT/'source'/'build_freighter.py').read_text())
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.ClassDef) and n.name=='Batch'],
                       type_ignores=[]),str(OUT/'source'/'build_freighter.py'),'exec'))

def mat(n):return bpy.data.materials['Freighter / '+n]
ivory=mat('Warm ceramic hull')
dark=mat('Recesses and structural frame')
metal=mat('Exposed brushed steel')
slate=mat('Blue-grey cargo doors')
orange=mat('Safety ochre')
glass=mat('Smoked cockpit glazing')

# y, half width, upper Z, lower Z: shared station data also repositions fittings.
stations=[(-20.20,5.20,3.80,-5.30),(-17.50,5.40,5.05,-5.60),
          (-7.00,5.20,5.05,-5.60),(-4.00,5.05,4.80,-5.70),
          (8.80,4.65,4.45,-5.50),(13.80,4.50,4.25,-4.90),
          (17.70,4.25,3.75,-4.10),(20.30,3.30,1.20,-2.50)]

def main_profile(y):
    for a,b in zip(stations,stations[1:]):
        if y<=b[0]:
            t=max(0,min(1,(y-a[0])/(b[0]-a[0])))
            return tuple(a[i]+t*(b[i]-a[i]) for i in [1,2,3])
    return stations[-1][1:]

vertices=[]
faces=[]
indices=[]
def loft(sections,chamfer=.65,top_mat=0,side_mat=0,bottom_mat=1):
    """Eight-sided cross sections; each shell is closed before Boolean cutting."""
    base=len(vertices)
    for y,cx,w,top,bottom in sections:
        c=min(chamfer,w*.35,(top-bottom)*.2)
        vertices.extend([(cx-w+c,y,top),(cx+w-c,y,top),(cx+w,y,top-c),
                         (cx+w,y,bottom+c),(cx+w-c,y,bottom),
                         (cx-w+c,y,bottom),(cx-w,y,bottom+c),(cx-w,y,top-c)])
    faces.append(tuple(base+i for i in reversed(range(8))))
    indices.append(side_mat)
    for k in range(len(sections)-1):
        for i in range(8):
            faces.append((base+k*8+i,base+k*8+(i+1)%8,
                          base+(k+1)*8+(i+1)%8,base+(k+1)*8+i))
            indices.append(top_mat if i==0 else bottom_mat if i==4 else side_mat)
    faces.append(tuple(base+(len(sections)-1)*8+i for i in range(8)))
    indices.append(side_mat)

loft([(y,0,w,t,b) for y,w,t,b in stations],.68)
loft([(-6.50,0,7.85,2.50,-5.40),(-4,0,7.80,2.50,-5.25),
      (15.30,0,7.80,2.50,-5.25),(18.50,0,5.00,.55,-3.25)],.50,
     top_mat=1,side_mat=0,bottom_mat=1)
loft([(-20.25,0,5.0,-4.55,-9.3),(-17.50,0,5.0,-4.55,-10.0),
      (9.70,0,5.0,-4.60,-10.0),(13.0,0,4.60,-4.70,-9.4),
      (15.5,0,3.0,-4.0,-7.0)],.70)
for sign in [-1,1]:
    loft([(-20.20,sign*8.8,2.7,1.90,-4.30),(-18.20,sign*8.8,3.7,2.70,-5.20),
          (-9.0,sign*8.8,3.7,2.70,-5.20),(-5.5,sign*8.7,2.4,1.30,-4.80),
          (-3.8,sign*8.4,1.4,.60,-3.80)],.85,top_mat=1)

mesh=bpy.data.meshes.new('Freighter / swept hull stations')
mesh.from_pydata(vertices,[],faces)
mesh.update()
for m in [ivory,dark]:mesh.materials.append(m)
for p,i in zip(mesh.polygons,indices):p.material_index=i
bm=bmesh.new()
bm.from_mesh(mesh)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
assert all(e.is_manifold for e in bm.edges)
bm.to_mesh(mesh)
bm.free()
mesh.transform(body.matrix_world.inverted())
body.data=mesh
for mod in body.modifiers:
    if mod.type=='BOOLEAN':mod.use_self=True
    if mod.type=='BEVEL':
        mod.width=.035
        mod.segments=3
body['silhouette_revision']=True
body['design']='Swept wedge bow, sloping dorsal spine, chamfered twin engine nacelles'
body['base_hull_dimensions_m']=[25.,40.55,15.05]

# Reattach existing fittings in world meters, then convert back to their own
# coordinates. Cargo doors still have six real bay-sized faces, without a grid.
for ob in list(bpy.data.collections['Freighter Details'].all_objects):
    if ob.type!='MESH' or any(c.name=='08 Folding cargo ramp' for c in ob.users_collection):continue
    if ob.name in ['Cockpit glazing and coaming','Safety paint and roof walkways']:continue
    inv=ob.matrix_world.inverted()
    for v in ob.data.vertices:
        p=ob.matrix_world@v.co
        ax=abs(p.x)
        sign=1 if p.x>=0 else -1
        if p.z>4.8 and ax<5.05 and -19.7<p.y<17.4:
            w,top,bottom=main_profile(p.y)
            p.x*=(w-.68)/5.0
            p.z+=top-5.0
        elif p.z>2.50 and ax>5.5 and p.y<-8.0:
            p.z+=.12
        if ob.name in ['Cargo rack structural rails','Six smooth cargo bay covers']:
            p.x+=sign*.16
        if ob.name=='Crew airlocks and rear cargo portal' and p.y>8 and ax>4:
            p.x-=sign*.22
        if ob.name=='Navigation and working lights':
            if p.y>20:
                p.x*=.75
                p.y+=.22
            elif p.z<-5 and p.y>9 and 4<ax<5.5:
                p.x-=sign*.22
            elif -4<p.y<15 and ax>7.5:
                p.x+=sign*.16
            elif p.y>15 and ax>7.5:
                p.x+=sign*.13
        if ob.name=='Radiators piping and maneuvering jets' and p.y>19.7:
            p.x*=.75
            p.y+=.20
        v.co=inv@p
    ob.data.update()

# The windshield follows the newly lowered wedge rather than the old blockout.
cockpit=Batch('Swept cockpit glazing','03 Cockpit and access',[glass,metal,dark,ivory])
normal=Vector((0,2.55/2.6,1)).normalized()
roof=lambda y:main_profile(y)[1]
for sign in [-1,1]:
    inner=.15*sign
    top_outer=3.15*sign
    bottom_outer=2.65*sign
    pts=[Vector((inner,17.95,roof(17.95))),Vector((top_outer,17.95,roof(17.95))),
         Vector((bottom_outer,19.40,roof(19.40))),Vector((inner,19.40,roof(19.40)))]
    cockpit.prism([p+normal*.065 for p in pts],normal,.035,0)
    for a,b in zip(pts,pts[1:]+pts[:1]):cockpit.beam(a+normal*.105,b+normal*.105,.11,1)
    pts=[Vector((sign*(main_profile(y)[0]+.035),y,z))
         for y,z in [(14.7,2.35),(16.6,2.35),(16.6,3.00),(14.7,3.00)]]
    cockpit.prism(pts,(sign,0,0),.04,0)
    for a,b in zip(pts,pts[1:]+pts[:1]):cockpit.beam(a+Vector((sign*.06,0,0)),b+Vector((sign*.06,0,0)),.08,1)
new=cockpit.finish(.018)
old=bpy.data.objects.get('Cockpit glazing and coaming')
if old:bpy.data.objects.remove(old,do_unlink=True)

# Paint follows the dorsal slope as a continuous strip, without repeating panels.
paint=Batch('Dorsal safety line and work-light mounts','07 Markings',[orange,dark,ivory])
for sign in [-1,1]:
    for y in range(-17,12):
        pts=[]
        for yy,dx in [(y,-.045),(y,.045),(y+1,.045),(y+1,-.045)]:
            w,top,bottom=main_profile(yy)
            pts.append((sign*(w-.9)+dx,yy,top+.012))
        paint.prism(pts,(0,0,1),.006,0)
    for y in [-.95,5.35,11.65]:
        paint.box((sign*7.95,y,1.79),(.25,.72,.23),1)
    paint.box((sign*7.69,15.5,.65),(.48,.6,.38),1)
paint.finish(.006)
old=bpy.data.objects.get('Safety paint and roof walkways')
if old:bpy.data.objects.remove(old,do_unlink=True)

for ob in bpy.data.collections['07 Markings'].objects:
    if ob.type!='FONT':continue
    if ob.name.startswith('Cargo bay stencil'):
        ob.location.x+=.16 if ob.location.x>0 else -.16
    elif ob.name.startswith('Engine shoulder registry'):
        ob.location.z+=.12
    else:
        y=ob.location.y
        ob.location.z=roof(y)+.018
        slope=(roof(y+.01)-roof(y-.01))/.02
        ob.rotation_euler=(math.atan(-slope),0,math.pi)

bpy.context.view_layer.update()
report={'meters_per_unit':1,'reference':'Original T silhouette, approximately 40m long / 25m wide',
        'changes':['tapered wedge bow','lowered swept cockpit','sloping dorsal spine',
                   'chamfered engine nacelles','tapered cargo fairings','sculpted lower keel'],
        'continuous_hull':True,'rear_folding_ramp_retained':True,
        'construction_shells_manifold':True,'source_body_replaced':True,
        'hull_dimensions_m':[round(v,3) for v in body.dimensions]}
(OUT/'source'/'silhouette_report.json').write_text(json.dumps(report,indent=2))
result=report
