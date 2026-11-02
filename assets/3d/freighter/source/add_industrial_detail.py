"""ORE-inspired industrial detailing, sized for the existing 1:1 K-40 hull.

References: official EVE image renders, types 32880 (Venture) and 42244 (Porpoise).
Adds functional machinery masses and real recesses, not a tiled hull treatment.
"""
import bpy
import bmesh
import ast
import math
import json
from pathlib import Path
from mathutils import Vector

OUT=Path(__file__).resolve().parent.parent
body=bpy.data.objects['Body']
assert body.get('silhouette_revision')
assert not body.get('industrial_detail_revision',False)
groups={c.name:c for c in bpy.data.collections['Freighter Details'].children}
group=bpy.data.collections.new('09 Industrial working equipment')
bpy.data.collections['Freighter Details'].children.link(group)
groups[group.name]=group
tree=ast.parse((OUT/'source'/'build_freighter.py').read_text())
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.ClassDef) and n.name=='Batch'],
                       type_ignores=[]),str(OUT/'source'/'build_freighter.py'),'exec'))
shape_tree=ast.parse((OUT/'source'/'reshape_freighter.py').read_text())
for n in shape_tree.body:
    if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='stations' for t in n.targets):
        stations=ast.literal_eval(n.value)
exec(compile(ast.Module(body=[n for n in shape_tree.body if isinstance(n,ast.FunctionDef)
                             and n.name=='main_profile'],type_ignores=[]),'profile','exec'))

def oldmat(n):return bpy.data.materials['Freighter / '+n]
def material(name,color,metal=.35,rough=.55,emission=0):
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

yellow=material('Industrial ochre armor',(.63,.34,.045),.35,.57)
edge=material('Dark ochre edge guards',(.26,.135,.025),.4,.6)
copper=material('Coolant copper',(.27,.13,.065),.75,.4)
graphite=material('Machinery casing',(.055,.067,.069),.65,.50)
white=oldmat('Warm ceramic hull')
dark=oldmat('Recesses and structural frame')
steel=oldmat('Exposed brushed steel')
slate=oldmat('Blue-grey cargo doors')
lamp=oldmat('Work lights')

mats=[yellow,dark,steel,graphite,copper,white,slate,edge,lamp]
armor=Batch('Industrial armor frames and pod clamps',group.name,mats)
machinery=Batch('Recessed pumps exchangers and switchgear',group.name,mats)
pipes=Batch('Exposed coolant and power distribution',group.name,mats)
crane=Batch('Folded cargo handling arm',group.name,mats)
small=Batch('Service locks working lights and docking hardware',group.name,mats)

def poly_extrude(batch,corners,normal,depth,mi=0):
    pts=[Vector(p) for p in corners]
    n=Vector(normal)*depth
    count=len(pts)
    faces=[tuple(reversed(range(count))),tuple(range(count,count*2))]
    faces += [(i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count)]
    batch.poly(pts+[p+n for p in pts],faces,mi)

def chamfered_xy(batch,x,y,z,width,length,height,mi=0):
    w,l=width/2,length/2
    c=min(.22,w*.22,l*.22)
    ring=[(x-w+c,y-l,z),(x+w-c,y-l,z),(x+w,y-l+c,z),(x+w,y+l-c,z),
          (x+w-c,y+l,z),(x-w+c,y+l,z),(x-w,y+l-c,z),(x-w,y-l+c,z)]
    poly_extrude(batch,ring,(0,0,1),height,mi)

def route(batch,points,radius,mi=2):
    for a,b in zip(points,points[1:]):batch.cylinder(a,b,radius,mi=mi,sides=12)
    for p in points[1:-1]:
        p=Vector(p)
        batch.box(p,(radius*2.5,)*3,mi)

# Large color fields are tied to the cockpit and structural armor, not a grid.
body.data.materials.append(yellow)
yi=len(body.data.materials)-1
for p in list(body.data.polygons)[:58]:
    center=body.matrix_world@p.center
    if center.y>8.8 and (p.normal.z>.25 or p.normal.y>.65):p.material_index=yi
bpy.data.objects['Swept cockpit glazing'].data.materials[1]=yellow
old=bpy.data.objects.get('Bulkhead joints and service access')
if old:bpy.data.objects.remove(old,do_unlink=True)

# One compound cutter opens a sloping dorsal well and both nacelle service bays.
cut=Batch('Industrial service recess cutters',group.name,[dark])
ys,ye=-5.5,7.7
corners=[(-1.46,ys,main_profile(ys)[1]-.53),(1.46,ys,main_profile(ys)[1]-.53),
         (1.46,ye,main_profile(ye)[1]-.53),(-1.46,ye,main_profile(ye)[1]-.53)]
poly_extrude(cut,corners,(0,0,1),1.5)
for sign in [-1,1]:cut.box((sign*12.45,-13.9,-1.15),(1.05,6.0,3.4))
cutter=cut.finish()
group.objects.unlink(cutter)
bpy.data.collections['Freighter Construction'].objects.link(cutter)
cutter.hide_render=True
cutter.hide_set(True)
cutter.display_type='WIRE'
mod=body.modifiers.new('Industrial machinery wells / reversible','BOOLEAN')
mod.operation='DIFFERENCE'
mod.solver='EXACT'
mod.use_self=True
mod.object=cutter
bpy.context.view_layer.objects.active=body
bpy.ops.object.modifier_move_up(modifier=mod.name)

# Dorsal trunk: exposed feed lines, differently sized pumps, junction boxes.
floor_pts=[(x,y,main_profile(y)[1]-.515) for x,y in
           [(-1.44,ys),(1.44,ys),(1.44,ye),(-1.44,ye)]]
poly_extrude(machinery,floor_pts,(0,0,1),.035,1)
for sign in [-1,1]:
    for a,b in [(ys,-1.2),(-1.2,3.5),(3.5,ye)]:
        armor.beam((sign*1.60,a,main_profile(a)[1]+.055),
                   (sign*1.60,b,main_profile(b)[1]+.055),.24,0)
    points=[(sign*.91,y,main_profile(y)[1]-.27) for y in [ys+.2,-1.2,3.2,ye-.2]]
    route(pipes,points,.105,4 if sign>0 else 2)
    for y in [-4.9,-1.0,3.2,7.1]:
        z=main_profile(y)[1]-.27
        pipes.cylinder((sign*.91,y-.09,z),(sign*.91,y+.09,z),.165,mi=2,sides=12)
for y,length,width in [(-3.9,1.8,1.1),(-.5,2.5,1.05),(3.1,1.35,1.2),(5.9,1.4,.95)]:
    z=main_profile(y)[1]-.46
    chamfered_xy(machinery,0,y,z,width,length,.32,3)
    chamfered_xy(machinery,0,y,z+.32,width*.82,length*.85,.055,2)
    for k in range(5):
        machinery.box((-.32+k*.16,y,z+.395),(.055,length*.64,.05),1)
    small.box((width/2-.10,y-length*.27,z+.40),(.065,.24,.06),8)
for y in [ys,ye]:
    armor.box((0,y,main_profile(y)[1]-.04),(3.4,.30,.30),0)

# Replace the flat central radiator deck with two raised compressor assemblies.
# The large radiators on the nacelles remain; repetition is confined to fins.
old_services=bpy.data.objects['Radiators piping and maneuvering jets']
bm=bmesh.new()
bm.from_mesh(old_services.data)
seen=set()
remove=[]
for vert in bm.verts:
    if vert in seen:continue
    stack,component=[vert],[]
    seen.add(vert)
    while stack:
        v=stack.pop()
        component.append(v)
        for e in v.link_edges:
            other=e.other_vert(v)
            if other not in seen:
                seen.add(other)
                stack.append(other)
    world=[old_services.matrix_world@v.co for v in component]
    if all(abs(p.x)<3.95 and -16.7<p.y<-8.4 and p.z>4.95 for p in world):
        remove.extend(component)
bmesh.ops.delete(bm,geom=remove,context='VERTS')
bm.to_mesh(old_services.data)
bm.free()
for sign in [-1,1]:
    x=sign*1.88
    for y in [-14.9,-10.8]:
        chamfered_xy(machinery,x,y,5.02,1.6,1.0,.45,3)
    machinery.cylinder((x,-15.50,6.03),(x,-9.95,6.03),.76,mi=3,sides=24)
    for y in [-15.55,-13.9,-10.0]:
        pipes.cylinder((x,y-.10,6.03),(x,y+.10,6.03),.87,mi=2,sides=24,caps=False)
    for y in [-15.66,-9.84]:
        machinery.cylinder((x,y+.025,6.03),(x,y-.025,6.03),.73,mi=1,sides=24)
        machinery.cylinder((x,y-.03,6.03),(x,y-.08,6.03),.28,mi=2,sides=16)
        for k in range(6):
            a=k*math.tau/6
            pipes.cylinder((x+.51*math.cos(a),y-.05,6.03+.51*math.sin(a)),
                           (x+.51*math.cos(a),y-.10,6.03+.51*math.sin(a)),.055,mi=2,sides=8)
    for y in [-14.5,-11.0]:
        # Broad chamfered yellow caps flank exposed dark cylindrical machinery.
        chamfered_xy(armor,x,y,6.69,1.84,1.65,.15,0)
        for side in [-1,1]:
            armor.beam((x+side*.90,y,6.65),(x+side*.94,y,5.45),.18,0)
    route(pipes,[(x+.90,-14.8,5.70),(x+1.0,-12.1,5.70),
                 (x+.80,-12.1,6.30),(x+.80,-10.6,6.30)],.075,4)

# The same rounded nacelle silhouette is wrapped in two heavy protective hoops.
for sign in [-1,1]:
    for y in [-18.15,-9.0]:
        cx=sign*8.8
        w,top,bottom,c=3.72,2.74,-5.23,.86
        ring=[(cx-w+c,y,top),(cx+w-c,y,top),(cx+w,y,top-c),
              (cx+w,y,bottom+c),(cx+w-c,y,bottom),(cx-w+c,y,bottom),
              (cx-w,y,bottom+c),(cx-w,y,top-c)]
        for a,b in zip(ring,ring[1:]+ring[:1]):armor.beam(a,b,.25,0)
        # Raised corner blocks and visible bolt pairs emphasize structural joints.
        for x in [cx-w+.45,cx+w-.45]:
            armor.box((x,y,top-.18),(.55,.58,.40),7)
            for yy in [-.15,.15]:small.cylinder((x,y+yy,top+.03),(x,y+yy,top+.09),.065,mi=2,sides=8)

    # Nacelle side well: pumps, heat exchanger, manifold and cable trunk.
    xx=sign*11.97
    machinery.box((xx,-13.9,-1.15),(.10,5.96,3.36),1)
    for z in [-2.91,.61]:
        armor.box((sign*12.55,-13.9,z),(.28,6.35,.30),0)
    for y in [-17.02,-10.78]:
        armor.box((sign*12.55,y,-1.15),(.28,.28,3.7),0)
    for y,z,length,height in [(-15.8,-.8,1.35,1.9),(-13.6,-1.65,1.25,1.25),(-11.7,-.65,.85,1.8)]:
        machinery.box((sign*12.15,y,z),(.28,length,height),3)
        machinery.box((sign*12.34,y,z),(.08,length*.80,height*.78),2)
        for k in range(4):
            machinery.box((sign*12.40,y-length*.3+k*length*.2,z),(.065,.05,height*.64),1)
    route(pipes,[(sign*12.25,-16.6,-2.45),(sign*12.25,-14.3,-2.45),
                 (sign*12.25,-14.3,-.15),(sign*12.25,-11.2,-.15)],.075,4)
    route(pipes,[(sign*12.20,-16.55,.19),(sign*12.20,-15.0,.19),
                 (sign*12.20,-15.0,-1.9),(sign*12.20,-11.25,-1.9)],.095,2)
    for y in [-16.4,-14.2,-11.3]:
        small.box((sign*12.48,y,.39),(.12,.35,.09),8)

    # The swept forward face carries a cast drive yoke and coupling, giving
    # the previously bare shoulder a recognizable mechanical mass.
    pts=[]
    for y,z in [(-8.3,-3.80),(-5.95,-3.60),(-5.95,.30),(-8.3,1.20)]:
        xx=sign*(12.5-.4*(y+9.0))
        pts.append((xx,y,z))
    n=Vector((sign,.4,0)).normalized()
    poly_extrude(armor,[Vector(p)+n*.025 for p in pts],n,.17,0)
    center=Vector((sign*(12.5-.4*1.85),-7.15,-1.25))
    machinery.cylinder(center+n*.18,center+n*.45,.72,mi=1,sides=24)
    machinery.cylinder(center+n*.45,center+n*.55,.47,mi=2,sides=24)
    machinery.cylinder(center+n*.55,center+n*.60,.26,mi=3,sides=16)
    for z in [-2.75,.25]:
        route(pipes,[(sign*11.95,-8.1,z),(sign*11.60,-7.2,z),
                     (sign*11.60,-7.2,z+.25)],.065,2)

# Cargo ribs are heavy, visible load paths between actual bays, with offset feet.
for sign in [-1,1]:
    for y in [-4.1,2.20,8.50,14.80]:
        a=(sign*7.94,y,2.12)
        b=(sign*8.22,y,-4.51)
        armor.beam(a,b,.26,0)
        for z in [1.72,-4.11]:
            armor.box((sign*8.13,y,z),(.40,.52,.48),7)
            small.cylinder((sign*8.33,y,z),(sign*8.40,y,z),.105,mi=2,sides=8)
    for index,y in enumerate([-.95,5.35,11.65]):
        # One shaped armor cap per cargo module, leaving the central skin plain.
        pts=[(sign*8.00,y-2.52,1.20),(sign*8.00,y+2.52,1.20),
             (sign*8.00,y+2.12,.44),(sign*8.00,y-1.91,.44)]
        poly_extrude(armor,pts,(sign,0,0),.11,0)
        for yy in [y-2.18,y+2.18]:
            small.box((sign*8.05,yy,-3.45),(.21,.44,.64),3)
            small.box((sign*8.19,yy,-3.45),(.08,.26,.30),2)
        # A diagonal corner brace supports the latch, rather than a repeated grid.
        armor.beam((sign*8.0,y-2.42,-3.82),(sign*8.0,y-1.18,-2.65),.13,2)
    # Compact docking collar and fuel / service umbilical next to the crew door.
    x=sign*5.02
    small.cylinder((x,8.9,-6.30),(x+sign*.28,8.9,-6.30),.38,mi=3,sides=16)
    small.cylinder((x+sign*.28,8.9,-6.30),(x+sign*.34,8.9,-6.30),.28,mi=2,sides=16)

# Folded five-meter load-handling arm: actual gaps between flanges and pistons.
base=Vector((2.95,-1.8,main_profile(-1.8)[1]-.015))
chamfered_xy(crane,base.x,base.y,base.z,1.40,1.55,.22,1)
crane.cylinder(base+Vector((0,0,.22)),base+Vector((0,0,.48)),.57,mi=2,sides=24)
pivot=base+Vector((0,0,.82))
for sign in [-1,1]:
    crane.beam(base+Vector((sign*.29,0,.40)),pivot+Vector((sign*.29,0,0)),.18,3)
tip=Vector((2.95,3.2,6.28))
fold=Vector((2.95,.10,5.80))
for sign in [-1,1]:
    offset=Vector((sign*.22,0,0))
    crane.beam(pivot+offset,tip+offset,.18,0)
    crane.beam(tip+offset,fold+offset,.16,0)
    crane.cylinder(pivot+offset+Vector((0,.25,-.10)),
                   tip+offset+Vector((0,-.65,-.14)),.075,mi=2,sides=12)
for a,b in [(pivot,tip),(tip,fold)]:
    for t in [.18,.78]:
        p=a.lerp(b,t)
        crane.beam(p+Vector((-.26,0,0)),p+Vector((.26,0,0)),.10,3)
for p in [pivot,tip,fold]:
    crane.cylinder(p+Vector((-.35,0,0)),p+Vector((.35,0,0)),.19,mi=3,sides=16)
    for sign in [-1,1]:crane.cylinder(p+Vector((sign*.35,0,0)),p+Vector((sign*.39,0,0)),.11,mi=2,sides=12)
chamfered_xy(crane,fold.x,fold.y,fold.z-.35,.74,.66,.28,3)
for sign in [-1,1]:crane.beam(fold+Vector((sign*.33,0,-.25)),fold+Vector((sign*.28,.45,-.65)),.14,2)

# Opposite side carries a power cabinet and sensor mast, balancing the crane.
for x,y,w,l in [(-2.80,-1.6,1.0,2.5),(-2.75,3.25,1.15,1.4)]:
    z=main_profile(y)[1]+.025
    chamfered_xy(machinery,x,y,z,w,l,.43,3)
    chamfered_xy(armor,x,y,z+.43,w*.93,l*.72,.13,0)
    for k in range(4):small.box((x,y-l*.28+k*l*.18,z+.59),(w*.55,.06,.055),2)
    route(pipes,[(x+.40,y,z+.18),(x+.65,y,z+.18),(x+.65,y+.6,z+.18)],.06,4)

# Rear bulkhead power assembly gives the large face above the ramp its own job.
machinery.box((0,-20.30,.12),(4.9,.30,3.0),1)
for x in [-2.4,2.4]:armor.box((x,-20.52,.12),(.32,.40,3.2),0)
for z in [-1.39,1.63]:armor.box((0,-20.52,z),(4.95,.40,.24),0)
for x,w,h in [(-1.3,1.25,2.1),(.32,1.50,2.25),(1.70,.70,1.7)]:
    machinery.box((x,-20.60,.12),(w,.35,h),3)
    machinery.box((x,-20.81,.12),(w*.78,.08,h*.75),2)
    for k in range(5):small.box((x,-20.88,-h*.26+k*h*.13),(w*.6,.065,.07),1)
route(pipes,[(-1.75,-20.82,-.92),(-1.75,-20.82,-1.1),
             (1.8,-20.82,-1.1),(1.8,-20.82,.88)],.065,4)
for sign in [-1,1]:
    armor.beam((sign*4.0,-20.37,2.8),(sign*3.20,-20.37,-3.7),.24,0)
    armor.box((sign*3.20,-20.42,-3.7),(.65,.28,.7),7)
    pipes.cylinder((sign*1.85,-20.46,-3.25),(sign*1.85,-20.46,-2.05),.22,mi=2,sides=16)
    route(pipes,[(sign*1.3,-20.53,-1.6),(sign*1.3,-20.53,-3.45),
                 (sign*2.8,-20.53,-3.45)],.075,4)

# Small localized fasteners and wear guards finish functional edges only.
for sign in [-1,1]:
    for y in [-5.4,7.5]:
        for x in [sign*1.60,sign*3.80]:
            z=main_profile(y)[1]+.16
            small.cylinder((x,y,z),(x,y,z+.045),.06,mi=2,sides=8)
    for y in [-3.5,-2.9,-2.3]:
        small.box((sign*7.99,y,-4.48),(.035,.22,.22),0)
for batch,bevel in [(armor,.035),(machinery,.023),(pipes,.01),(crane,.022),(small,.008)]:batch.finish(bevel)

# Move the original roof registration out of the open service well.
for name,y,size in [('Dorsal registration',10.3,.56),('Dorsal class stencil',9.2,.24)]:
    ob=bpy.data.objects[name]
    ob.location.y=y
    ob.location.z=main_profile(y)[1]+.022
    ob.data.size=size
    slope=(main_profile(y+.01)[1]-main_profile(y-.01)[1])/.02
    ob.rotation_euler=(math.atan(-slope),0,math.pi)

body['industrial_detail_revision']=True
white.diffuse_color=(.39,.42,.39,1)
white.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.39,.42,.39,1)
body['style_reference']='ORE industrial ships: protective ochre frames, charcoal machinery, exposed service systems'
bpy.context.view_layer.update()
report={'style_reference':'EVE Online ORE industrial vessels, interpreted as a 40m utility freighter',
        'reference_urls':['https://images.evetech.net/types/32880/render?size=1024',
                          'https://images.evetech.net/types/42244/render?size=1024'],
        'details':['open dorsal service well','two raised compressor assemblies',
                   'two recessed nacelle machinery bays',
                   'protective structural hoops','cargo bay load frames','folded handling arm',
                   'coolant and power conduits','rear bulkhead power assembly'],
        'uniform_surface_tiling':False,'meters_per_unit':1,
        'ramp_animation_retained':True,'hull_dimensions_m':[round(v,3) for v in body.dimensions]}
(OUT/'source'/'industrial_detail_report.json').write_text(json.dumps(report,indent=2))
result=report
