"""Manufacturing joints and rounded, serviceable pipework in actual meters.

Functional deck plates replace a uniform grid. Tubes have swept circular elbows,
flanged unions, gate valves and clamps instead of disconnected straight rods.
"""
import bpy
import bmesh
import ast
import math
import json
from pathlib import Path
from mathutils import Vector, Quaternion

OUT=Path(__file__).resolve().parent.parent
body=bpy.data.objects['Body']
assert body.get('industrial_detail_revision')
assert not body.get('fabrication_revision',False)
groups={c.name:c for c in bpy.data.collections['Freighter Details'].children}
group=bpy.data.collections.new('10 Fabrication joints and routed pipework')
bpy.data.collections['Freighter Details'].children.link(group)
groups[group.name]=group
tree=ast.parse((OUT/'source'/'build_freighter.py').read_text())
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.ClassDef) and n.name=='Batch'],
                       type_ignores=[]),'batch','exec'))
tree=ast.parse((OUT/'source'/'reshape_freighter.py').read_text())
for n in tree.body:
    if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='stations' for t in n.targets):
        stations=ast.literal_eval(n.value)
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef)
                             and n.name=='main_profile'],type_ignores=[]),'profile','exec'))

def mat(n):return bpy.data.materials['Freighter / '+n]
dark=mat('Recesses and structural frame')
steel=mat('Exposed brushed steel')
copper=mat('Coolant copper')
yellow=mat('Industrial ochre armor')
slate=mat('Blue-grey cargo doors')
white=mat('Warm ceramic hull')
graphite=mat('Machinery casing')
panel=bpy.data.materials.new('Freighter / Replacement rolled plate')
panel.use_nodes=True
panel.diffuse_color=(.32,.355,.335,1)
p=panel.node_tree.nodes.get('Principled BSDF')
p.inputs['Base Color'].default_value=panel.diffuse_color
p.inputs['Metallic'].default_value=.45
p.inputs['Roughness'].default_value=.73
mats=[dark,steel,copper,yellow,slate,white,graphite,panel]
joints=Batch('Gaskets welds and structural flanges',group.name,mats)
panels=Batch('Shaped service deck plates and cargo doublers',group.name,mats)
bolts=Batch('Captive fasteners hinges and mounting feet',group.name,mats)
tubes=Batch('Continuous pipework with swept elbows',group.name,mats)
fittings=Batch('Flanged unions valves and pipe clamps',group.name,mats)
cut=Batch('Shallow structural seam cutters',group.name,[dark])
route_count=0
flange_count=0

def extrude(batch,corners,n,depth,mi=0):
    pts=[Vector(p) for p in corners]
    count=len(pts)
    delta=Vector(n)*depth
    faces=[tuple(reversed(range(count))),tuple(range(count,count*2))]
    faces += [(i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count)]
    batch.poly(pts+[p+delta for p in pts],faces,mi)

def basis(axis):
    n=Vector(axis).normalized()
    u=n.cross(Vector((0,0,1)))
    if u.length<.05:u=n.cross(Vector((0,1,0)))
    u.normalize()
    return n,u,n.cross(u)

def torus(batch,center,axis,radius,tube,mi=1,segments=20):
    center=Vector(center)
    n,u,v=basis(axis)
    pts=[]
    for i in range(segments):
        radial=u*math.cos(i*math.tau/segments)+v*math.sin(i*math.tau/segments)
        for j in range(6):
            a=j*math.tau/6
            pts.append(center+radial*(radius+tube*math.cos(a))+n*tube*math.sin(a))
    faces=[(i*6+j,((i+1)%segments)*6+j,((i+1)%segments)*6+(j+1)%6,i*6+(j+1)%6)
           for i in range(segments) for j in range(6)]
    batch.poly(pts,faces,mi)

def bolt(center,n,r=.032):
    center,n=Vector(center),Vector(n).normalized()
    bolts.cylinder(center-n*.006,center+n*.008,r*1.45,mi=0,sides=12)
    bolts.cylinder(center+n*.008,center+n*.031,r,mi=1,sides=6)
    # A dark center recess reads as a socket rather than another bright dot.
    bolts.cylinder(center+n*.031,center+n*.034,r*.42,mi=0,sides=6)

def rounded_path(points,radius):
    pts=[Vector(p) for p in points]
    result=[pts[0]]
    for i in range(1,len(pts)-1):
        p=pts[i]
        a,b=(p-pts[i-1]).normalized(),(pts[i+1]-p).normalized()
        turn=math.acos(max(-1,min(1,a.dot(b))))
        axis=a.cross(b)
        if turn<.04 or axis.length<.01:
            result.append(p)
            continue
        trim=min(radius*math.tan(turn/2),(p-pts[i-1]).length*.34,(pts[i+1]-p).length*.34)
        actual=trim/math.tan(turn/2)
        start=p-a*trim
        center=p+(-a+b).normalized()*(actual/math.cos(turn/2))
        vec=start-center
        axis.normalize()
        samples=max(3,math.ceil(turn/math.radians(12)))
        for k in range(samples+1):
            result.append(center+Quaternion(axis,turn*k/samples)@vec)
    result.append(pts[-1])
    return [p for i,p in enumerate(result) if i==0 or (p-result[i-1]).length>.00001]

def pipe(points,radius,mi=1,bend=.24):
    global route_count
    centers=rounded_path(points,bend)
    sides=12
    verts=[]
    previous=None
    for i,p in enumerate(centers):
        t=(centers[min(i+1,len(centers)-1)]-centers[max(i-1,0)]).normalized()
        if previous is None:
            _,u,v=basis(t)
        else:
            t0,u0=previous
            u=t0.rotation_difference(t)@u0
            u=(u-t*u.dot(t)).normalized()
            v=t.cross(u)
        previous=(t,u)
        for j in range(sides):
            a=j*math.tau/sides
            verts.append(p+radius*(u*math.cos(a)+v*math.sin(a)))
    faces=[(i*sides+j,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,(i+1)*sides+j)
           for i in range(len(centers)-1) for j in range(sides)]
    faces += [tuple(reversed(range(sides))),tuple(range((len(centers)-1)*sides,len(centers)*sides))]
    tubes.poly(verts,faces,mi)
    route_count+=1

def flange(center,axis,r,mi=1):
    global flange_count
    c=Vector(center)
    n,u,v=basis(axis)
    # Two bolted flange halves compress a visible gasket, not a single ring.
    for d in [-.048,.048]:
        fittings.cylinder(c+n*(d-.027),c+n*(d+.027),r*1.85,mi=mi,sides=16)
    fittings.cylinder(c-n*.019,c+n*.019,r*1.74,mi=0,sides=16)
    for k in range(6):
        a=k*math.tau/6
        bolt(c+n*.078+(u*math.cos(a)+v*math.sin(a))*r*1.47,n,r*.24)
    flange_count+=1

def support(center,tangent,foot,r):
    center,foot=Vector(center),Vector(foot)
    torus(fittings,center,tangent,r+.018,.018,1,16)
    fittings.beam(center,foot,.065,1)
    normal=(center-foot).normalized()
    fittings.cylinder(foot-normal*.015,foot+normal*.015,.10,mi=6,sides=8)

def valve(center,tangent,normal,r=.08):
    c,n=Vector(center),Vector(normal).normalized()
    t,u,v=basis(tangent)
    fittings.cylinder(c-t*.17,c+t*.17,r*1.4,mi=6,sides=12)
    for d in [-.21,.21]:flange(c+t*d,t,r)
    fittings.cylinder(c+n*.06,c+n*.30,.035,mi=1,sides=10)
    wheel=c+n*.32
    torus(fittings,wheel,n,.15,.022,3,20)
    for k in range(4):
        a=k*math.pi/2
        radial=basis(n)[1]*math.cos(a)+basis(n)[2]*math.sin(a)
        fittings.beam(wheel,wheel+radial*.14,.025,1)

def roof(x,y,offset=0):return Vector((x,y,main_profile(y)[1]+offset))

def plate(name,points,normal,mi=7):
    pts=[Vector(p) for p in points]
    n=Vector(normal).normalized()
    extrude(panels,pts,n,.018,mi)
    for a,b in zip(pts,pts[1:]+pts[:1]):
        # Recessed gasket and a narrow weld toe give each edge a real joint.
        joints.beam(a+n*.004,b+n*.004,.028,0)
        length=(b-a).length
        count=max(1,int(length/.85))
        for i in range(count+1):
            p=a.lerp(b,(i+.12)/(count+.24))
            direction=(sum(pts,Vector())/len(pts)-p).normalized()
            bolt(p+direction*.075+n*.023,n)

# A few bulkhead seams wrap the actual octagonal hull, rather than making tiles.
for y in [-17.35,-6.85,8.72,13.83]:
    w,top,bottom=main_profile(y)
    c=.68
    ring=[Vector(p) for p in [(-w+c,y,top),(w-c,y,top),(w,y,top-c),
          (w,y,bottom+c),(w-c,y,bottom),(-w+c,y,bottom),(-w,y,bottom+c),(-w,y,top-c)]]
    # Only the dorsal shell and upper side walls are visible above the cargo body.
    for i in [0,1,7]:
        a,b=ring[i],ring[(i+1)%8]
        cut.beam(a,b,.042,0)
        joints.beam(a-Vector((0,0,.009)),b-Vector((0,0,.009)),.023,0)
    for sign in [-1,1]:
        a=Vector((sign*w,y,top-c))
        b=Vector((sign*w,y,2.55))
        if a.z>b.z:
            joints.beam(a,b,.028,0)
            for z in [2.8,top-c-.15]:bolt((sign*(w+.015),y+.085,z),(sign,0,0))

# One large removable machinery bed, two shaped canopy access panels and a
# left-side switchgear bed provide unequal, purpose-sized plate boundaries.
plate('compressor bed',[roof(-3.62,-16.40,.004),roof(3.62,-16.40,.004),
      roof(3.82,-16.0,.004),roof(3.82,-9.0,.004),roof(3.40,-8.60,.004),
      roof(-3.60,-8.60,.004),roof(-3.82,-9.10,.004),roof(-3.82,-15.95,.004)],(0,0,1))
for sign in [-1,1]:
    pts=[roof(sign*x,y,.005) for x,y in [(1.78,11.0),(3.52,11.0),(3.45,14.1),
         (2.90,15.6),(1.65,15.6),(1.65,12.3)]]
    plate('canopy service access',pts,(0,0,1),3)
plate('switchgear bed',[roof(-3.80,-3.5,.004),roof(-1.82,-3.5,.004),
      roof(-1.82,4.60,.004),roof(-3.30,4.60,.004),roof(-3.80,3.8,.004)],(0,0,1))

# Pressed corner-cut cargo doublers retain the large bay-sized surfaces.
for sign in [-1,1]:
    for idx,y in enumerate([-.95,5.35,11.65]):
        pts=[Vector((sign*7.972,y+yy,z)) for yy,z in
             [(-2.43,-3.40),(1.96,-3.40),(2.40,-2.98),(2.40,-.28),
              (1.40,.17),(-2.43,.17)]]
        plate('cargo doubler',pts,(sign,0,0),4)
        # A lap-weld on one repaired corner reads differently from a bolted lid.
        if idx==1 and sign<0:
            p=[Vector((sign*8.001,y+yy,z)) for yy,z in [(.65,-2.85),(1.65,-2.85),(1.85,-2.65),(1.85,-1.85),(.65,-1.85)]]
            extrude(panels,p,(sign,0,0),.015,7)
            for a,b in zip(p,p[1:]+p[:1]):
                length=(b-a).length
                count=max(2,int(length/.045))
                for j in range(count):
                    t=(j+.5)/count
                    center=a.lerp(b,t)+Vector((sign*.022,0,0))
                    joints.cylinder(center-Vector((0,.020,0)),center+Vector((0,.020,0)),.010,mi=1,sides=6)

# Flange plates and bolts at crane and nacelle clamp attachment points.
for sign in [-1,1]:
    for y in [-18.15,-9.0]:
        for x in [sign*5.55,sign*12.06]:
            for yy in [-.18,.18]:bolt((x,y+yy,2.85),(0,0,1),.037)
    for y in [-3.0,-.65]:
        for x in [2.45,3.45]:bolt(roof(x,y,.065),(0,0,1),.037)

cutter=cut.finish()
group.objects.unlink(cutter)
bpy.data.collections['Freighter Construction'].objects.link(cutter)
cutter.hide_render=True
cutter.hide_set(True)
cutter.display_type='WIRE'
mod=body.modifiers.new('Fabrication seam rebates / reversible','BOOLEAN')
mod.operation='DIFFERENCE'
mod.solver='EXACT'
mod.use_self=True
mod.object=cutter

# Replace the former straight segments, keeping compressor collars and bolts.
old=bpy.data.objects.get('Exposed coolant and power distribution')
if old:bpy.data.objects.remove(old,do_unlink=True)
old=bpy.data.objects.get('Additional pipes assemblies')
if old:
    bm=bmesh.new();bm.from_mesh(old.data)
    seen=set();remove=[]
    for v in bm.verts:
        if v in seen:continue
        stack=[v];comp=[];seen.add(v)
        while stack:
            p=stack.pop();comp.append(p)
            for e in p.link_edges:
                q=e.other_vert(p)
                if q not in seen:seen.add(q);stack.append(q)
        ps=[old.matrix_world@p.co for p in comp]
        preserve=(all(p.y<-9.0 and p.z>5.1 for p in ps)
                  and (len(comp)==48 or len(comp)==16))
        if not preserve:remove.extend(comp)
    bmesh.ops.delete(bm,geom=remove,context='VERTS')
    bm.to_mesh(old.data);bm.free()
old=bpy.data.objects['Radiators piping and maneuvering jets']
bm=bmesh.new();bm.from_mesh(old.data);seen=set();remove=[]
for v in bm.verts:
    if v in seen:continue
    stack=[v];comp=[];seen.add(v)
    while stack:
        p=stack.pop();comp.append(p)
        for e in p.link_edges:
            q=e.other_vert(p)
            if q not in seen:seen.add(q);stack.append(q)
    ps=[old.matrix_world@p.co for p in comp]
    if (all(6<abs(p.x)<12 and 2.5<p.z<3.2 for p in ps)
        and max(p.y for p in ps)-min(p.y for p in ps)>6
        and max(p.x for p in ps)-min(p.x for p in ps)<.5):remove.extend(comp)
bmesh.ops.delete(bm,geom=remove,context='VERTS')
bm.to_mesh(old.data);bm.free()

# Dorsal trunk detours around each pump, with detachable flanges and valves.
for sign in [-1,1]:
    xy=([(.98,-5.15),(.98,-3.0),(1.18,-2.4),(1.18,-.8),(.90,-.3),
         (.90,1.7),(1.16,2.2),(1.16,4.3),(.98,4.8),(.98,7.3)] if sign>0 else
        [(.96,-5.15),(.96,-3.6),(1.15,-3.2),(1.15,-1.3),(.92,-.8),
         (.92,3.1),(1.17,3.7),(1.17,5.6),(.97,6.1),(.97,7.3)])
    pts=[roof(sign*x,y,-.25) for x,y in xy]
    pipe(pts,.085,2 if sign>0 else 1,.27)
    for p in [pts[0]+Vector((0,.22,0)),pts[-1]-Vector((0,.22,0))]:flange(p,(0,1,0),.085)
    valve(roof(sign*.92,1.00,-.25),(0,1,0),(0,0,1),.085)
    for x,y in [xy[0],xy[3],xy[-3],xy[-1]]:
        support(roof(sign*x,y,-.25),(0,1,0),roof(sign*x,y,-.50),.085)

for sign in [-1,1]:
    # Nacelle service face: rise above the exchanger, then dip below its neighbor.
    points=[(sign*12.32,-16.60,-2.45),(sign*12.32,-14.65,-2.45),
            (sign*12.44,-14.65,-1.85),(sign*12.44,-14.65,.37),
            (sign*12.32,-13.9,.37),(sign*12.32,-11.22,.37)]
    pipe(points,.067,2,.30)
    points2=[(sign*12.28,-16.6,-2.70),(sign*12.28,-13.2,-2.70),
             (sign*12.41,-12.65,-2.70),(sign*12.41,-12.1,-2.25),
             (sign*12.28,-11.20,-2.25)]
    pipe(points2,.085,1,.26)
    valve((sign*12.32,-13.15,.37),(0,1,0),(sign,0,0),.067)
    for y,z in [(-16.3,-2.45),(-15.25,-2.45),(-11.4,.37)]:
        support((sign*12.32,y,z),(0,1,0),(sign*11.97,y,z),.067)
    for p,t in [(points[0],(0,1,0)),(points[-1],(0,1,0)),(points2[-1],(0,1,0))]:flange(p,t,.075)

    # Roof supply pipes dog-leg around the exchanger brackets.
    for x,dx in [(6.55,-.30),(11.45,.30)]:
        ps=[(sign*x,-18.25,2.96),(sign*x,-16.2,2.96),
            (sign*(x+dx),-15.7,2.96),(sign*(x+dx),-13.1,2.96),
            (sign*x,-12.6,2.96),(sign*x,-9.20,2.96)]
        pipe(ps,.11,1,.28)
        for y in [-17.8,-10.0]:flange((sign*x,y,2.96),(0,1,0),.11)
        for y in [-17.4,-11.3]:support((sign*x,y,2.96),(0,1,0),(sign*x,y,2.72),.11)
    # Compressor line rises, bends around its collar and terminates in a union.
    x=sign*2.98
    ps=[(sign*2.70,-14.95,5.60),(x,-14.95,5.60),(x,-12.8,5.60),
        (x,-12.25,5.88),(x,-12.25,6.28),(x,-10.45,6.28),(sign*2.55,-10.45,6.28)]
    pipe(ps,.072,2,.22)
    flange(ps[0],(sign,0,0),.072)
    flange(ps[-1],(sign,0,0),.072)

    # Rear hydraulic feed includes a visible flexible loop, not square elbows.
    ps=[(sign*1.3,-20.55,-1.70),(sign*1.3,-20.55,-2.2),
        (sign*1.00,-20.70,-2.45),(sign*1.00,-20.70,-3.25),
        (sign*1.5,-20.68,-3.55),(sign*2.75,-20.53,-3.55)]
    pipe(ps,.072,2,.32)
    for p,t in [(ps[0],(0,0,1)),(ps[-1],(sign,0,0))]:flange(p,t,.072)
    for k in range(9):torus(fittings,(sign*1.00,-20.70,-2.52-k*.065),(0,0,1),.081,.014,0,12)

for batch,bevel in [(joints,.003),(panels,.008),(bolts,.003),(fittings,.005)]:batch.finish(bevel)
ob=tubes.finish()
for p in ob.data.polygons:p.use_smooth=len(p.vertices)==4
if hasattr(ob.data,'set_sharp_from_angle'):ob.data.set_sharp_from_angle(angle=math.radians(45))
body['fabrication_revision']=True
bpy.context.view_layer.update()
report={'meters_per_unit':1,'uniform_grid':False,
        'surface_details':['four structural hull joints','unequal removable deck plates',
                           'pressed cargo doublers','gaskets and recessed rebates',
                           'captured hex fasteners','one lap-weld repair'],
        'pipe_routes':route_count,'bolted_flange_unions':flange_count,
        'pipe_details':['circular swept elbows','obstacle clearance dog-legs',
                        'gate valves','straps and mounting feet','flexible hose sections'],
        'ramp_animation_retained':True}
(OUT/'source'/'fabrication_report.json').write_text(json.dumps(report,indent=2))
result=report
