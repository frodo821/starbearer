"""Add an editable 1:100 capital-ship detail study to the existing Body mesh.

Run in Blender's Python environment. Body geometry and transforms are preserved.
The generator intentionally refuses to overwrite an existing detail collection.
"""
import bpy
import bmesh
import math
import random
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
body = bpy.data.objects['Body']
assert body.type == 'MESH' and bpy.context.mode == 'OBJECT'
assert 'Ship Details' not in bpy.data.collections, 'Details already exist'
original_vertices = [tuple(v.co) for v in body.data.vertices]
rng = random.Random(112003)

details = bpy.data.collections.new('Ship Details')
bpy.context.scene.collection.children.link(details)
groups = {}
for name in ['01 Armor', '02 Superstructure', '03 Machinery', '04 Armament',
             '05 Engines', '06 Hangar', '07 Lights']:
    c = bpy.data.collections.new(name)
    details.children.link(c)
    groups[name] = c

def material(name, color, metal=.25, rough=.6, emission=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metal
    p.inputs['Roughness'].default_value = rough
    if emission:
        p.inputs['Emission Color'].default_value = (*color, 1)
        p.inputs['Emission Strength'].default_value = emission
    return m

armor = [material('SB / Armor '+str(i+1), (v, v*1.015, v*1.035))
         for i,v in enumerate([.36,.40,.44,.48,.52])]
dark = material('SB / Graphite machinery', (.065,.078,.09), .55)
seam = material('SB / Recess shadow', (.022,.029,.038), .25)
trim = material('SB / Edge metal', (.22,.25,.28), .65, .4)
window = material('SB / Warm service lights', (.9,.67,.37), .1, .35, 2)
blue = material('SB / Ion core', (.12,.5,1), .1, .35, 5)
red = material('SB / Navigation red', (.65,.02,.008), .1, .4, 3)

class MeshBatch:
    def __init__(self, name, group, mats):
        self.name, self.group, self.mats = name, group, mats
        self.v, self.f, self.mi = [], [], []

    def polyhedron(self, verts, faces, mat=0):
        base = len(self.v)
        self.v.extend([tuple(v) for v in verts])
        self.f.extend([tuple(base+i for i in f) for f in faces])
        self.mi.extend([mat]*len(faces))

    def prism(self, corners, normal, depth, mat=0):
        corners = [Vector(v) for v in corners]
        n = Vector(normal)*depth
        self.polyhedron(corners+[v+n for v in corners],
            [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mat)

    def box(self, center, size, mat=0, taper=1):
        x,y,z = center
        a,b,c = [s/2 for s in size]
        pts = [(x-a,y-b,z-c),(x+a,y-b,z-c),(x+a,y+b,z-c),(x-a,y+b,z-c),
               (x-a*taper,y-b*taper,z+c),(x+a*taper,y-b*taper,z+c),
               (x+a*taper,y+b*taper,z+c),(x-a*taper,y+b*taper,z+c)]
        self.polyhedron(pts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mat)

    def cylinder(self, start, end, r1, r2=None, mat=0, sides=16, caps=True):
        a,b = Vector(start),Vector(end)
        n = (b-a).normalized()
        u = n.cross(Vector((0,0,1)))
        if u.length < .01:
            u = n.cross(Vector((0,1,0)))
        u.normalize()
        v = n.cross(u)
        if r2 is None: r2=r1
        verts = [p + r*(u*math.cos(i*math.tau/sides)+v*math.sin(i*math.tau/sides))
                 for p,r in [(a,r1),(b,r2)] for i in range(sides)]
        faces = [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
        if caps: faces += [tuple(reversed(range(sides))),tuple(range(sides,sides*2))]
        self.polyhedron(verts,faces,mat)

    def finish(self, bevel=.0):
        if not self.v: return None
        me = bpy.data.meshes.new(self.name)
        me.from_pydata(self.v,[],self.f)
        me.update()
        for m in self.mats: me.materials.append(m)
        for p,i in zip(me.polygons,self.mi): p.material_index=i
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new(self.name,me)
        groups[self.group].objects.link(ob)
        ob.parent=body
        if bevel:
            mod=ob.modifiers.new('Small manufactured edge radii','BEVEL')
            mod.width=bevel
            mod.segments=2
        return ob

panels = MeshBatch('Conformal armor plates', '01 Armor', armor)
backing = MeshBatch('Dark panel seam backing', '01 Armor', [seam])
greeble = MeshBatch('Distributed service machinery', '03 Machinery', [dark,trim]+armor)
lights = MeshBatch('Portholes and service illumination', '07 Lights', [window,red,blue])
trenches = MeshBatch('Equatorial trench framework', '03 Machinery', [dark,trim])

# Surface tiles follow the actual quad topology rather than an assumed hull profile.
tile_count=0
for face in body.data.polygons:
    if len(face.vertices)!=4 or abs(face.normal.z)<.4: continue
    a,b,c,d=[body.data.vertices[i].co.copy() for i in face.vertices]
    n=face.normal.copy()
    def pos(u,v): return a.lerp(b,u).lerp(d.lerp(c,u),v)
    nu=max(1,round(((a-b).length+(d-c).length)/2/.43))
    nv=max(1,round(((a-d).length+(b-c).length)/2/.43))
    backing.prism([p+n*.003 for p in [a,b,c,d]],n,.002)
    for i in range(nu):
        for j in range(nv):
            gu=min(.09,.009/max((a-b).length/nu,.03))
            gv=min(.09,.009/max((a-d).length/nv,.03))
            uv=[((i+gu)/nu,(j+gv)/nv),((i+1-gu)/nu,(j+gv)/nv),
                ((i+1-gu)/nu,(j+1-gv)/nv),((i+gu)/nu,(j+1-gv)/nv)]
            corners=[pos(u,v)+n*.009 for u,v in uv]
            panels.prism(corners,n,rng.uniform(.009,.026),rng.randrange(5))
            tile_count+=1
            if rng.random()<.16:
                # Subpanels retain the local orientation of the hull surface.
                ctr=sum(corners,Vector())/4+n*.025
                small=[ctr+(p-sum(corners,Vector())/4)*.52 for p in corners]
                greeble.prism(small,n,.012,rng.choice([0,1,2,3]))

# Ray casting only against the original hull makes fittings sit on its surface.
def surface_z(x,y,upper=True):
    hit,loc,normal,index=body.ray_cast(Vector((x,y,10 if upper else -10)),
                                     Vector((0,0,-1 if upper else 1)))
    assert hit, ('Missing hull surface',x,y)
    return loc.z

# A band of dark ribbed technical detail runs along the broad outer hull edges.
for sign in [-1,1]:
    for i in range(94):
        x=-5.72+i*.122
        hit,p,n,idx=body.ray_cast(Vector((x,sign*8,0)),Vector((0,-sign,0)))
        if not hit: continue
        y=p.y+sign*.014
        trenches.box((x,y,0),(.115,.045,.085),0)
        trenches.box((x,y+sign*.027,0),(.018,.026,.115),1)
        if i%4==0:
            greeble.box((x,y+sign*.025,.01),(.075,.07,.035),0)
        if i%3==0:
            lights.box((x-.025,y+sign*.04,.018),(.024,.012,.009),0)

def add_command_foundation():
    """Close the hull/deck gap with a flared, hull-conforming solid plinth."""
    foundation=MeshBatch('Hull-conforming command foundation',
                         '02 Superstructure',[armor[2]])
    nx,ny=48,8
    verts=[]
    bottoms=[]
    for layer in range(2):
        for i in range(nx+1):
            u=i/nx
            for j in range(ny+1):
                v=j/ny
                if layer==0:
                    x=-1.60+6.70*u
                    y=-.90+1.80*v
                    z=surface_z(x,y)-.06
                    bottoms.append((x,y,z))
                else:
                    x=-1.30+6.10*u
                    y=-.74+1.48*v
                    z=1.025
                verts.append((x,y,z))
    stride=ny+1
    offset=(nx+1)*stride
    faces=[]
    for i in range(nx):
        for j in range(ny):
            a=i*stride+j
            faces.append((a,a+1,a+stride+1,a+stride))
            faces.append((offset+a,offset+a+stride,
                          offset+a+stride+1,offset+a+1))
    perimeter=([i*stride for i in range(nx+1)]
               +[nx*stride+j for j in range(1,ny+1)]
               +[i*stride+ny for i in range(nx-1,-1,-1)]
               +[j for j in range(ny-1,0,-1)])
    for a,b in zip(perimeter,perimeter[1:]+perimeter[:1]):
        faces.append((a,b,b+offset,a+offset))
    foundation.polyhedron(verts,faces)
    ob=foundation.finish(.003)
    ob['purpose']='Continuous hull-to-command-deck attachment; 0.06m embedded in Body'
    assert all(z<surface_z(x,y)-.05 for x,y,z in bottoms)
    return ob

add_command_foundation()

# Stepped upper decks and the stern command tower.
decks = MeshBatch('Terraced command decks', '02 Superstructure', armor+[trim])
for x,z,length,width,height in [
    (1.75,1.045,6.6,1.58,.30),(2.05,1.31,5.3,1.30,.29),
    (2.65,1.57,3.9,1.05,.25),(3.75,1.78,1.16,.78,.28),
    (3.85,2.04,.78,.58,.48),(3.75,2.32,.70,1.80,.24),
    (3.80,2.47,.58,1.52,.09)]:
    decks.box((x,0,z),(length,width,height),2,.87)
    # Thin courses emphasize scale and individual deck levels.
    decks.box((x,0,z-height*.30),(length*1.007,width*1.015,.018),5)
    for sign in [-1,1]:
        for k in range(max(3,int(length/.075))):
            xx=x-length*.44+k*.075
            if xx>x+length*.44:break
            if k%7!=0:
                lights.box((xx,sign*(width*.485),z),(.025,.012,.012),0)

# A narrow dark forward bridge glazing band, divided into many small panes.
bridge = MeshBatch('Bridge glazing and antenna supports', '02 Superstructure', [seam,trim])
bridge.box((3.389,0,2.335),(.024,1.52,.065),0)
for j in range(29):
    lights.box((3.373,-.70+j*.05,2.335),(.006,.023,.018),0)
for y in [-.57,.57]:
    bridge.cylinder((3.8,y,2.49),(3.8,y,2.67),.05,.035,1)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2,radius=.145,location=(3.8,y,2.76))
    ob=bpy.context.object
    ob.name='Faceted sensor dome '+('port' if y<0 else 'starboard')
    for col in list(ob.users_collection):col.objects.unlink(ob)
    groups['02 Superstructure'].objects.link(ob)
    ob.parent=body
    ob.data.materials.append(armor[3])
bridge.cylinder((4.03,0,2.50),(4.03,0,2.98),.018,.008,1)
bridge.box((4.03,0,2.85),(.018,.32,.018),1)

# Repeated but deliberately varied technical blocks, vents and heat exchangers.
vents = MeshBatch('Radiator louvres', '03 Machinery', [seam,trim])
for sign in [-1,1]:
    for x in [-3.8,-2.7,-1.6,-.5,.65,1.8,2.9,4.0,4.85]:
        y=sign*(.78+(x+4)*.115)
        z=surface_z(x,y)
        greeble.box((x,y,z+.055),(.42,.24,.11),1,.9)
        vents.box((x,y,z+.116),(.34,.17,.018),0)
        for k in range(7):
            vents.box((x-.145+k*.048,y,z+.132),(.016,.16,.016),1)
    for i in range(150):
        x=rng.uniform(-5.3,5.35)
        y=sign*rng.uniform(.9,3.05)
        hit,p,n,idx=body.ray_cast(Vector((x,y,8)),Vector((0,0,-1)))
        if not hit or p.z<.24: continue
        if  -.9<x<5.2 and abs(y)<.92: continue
        sx,sy=rng.uniform(.055,.19),rng.uniform(.04,.12)
        h=rng.uniform(.018,.07)
        greeble.box((x,y,p.z+h/2+.035),(sx,sy,h),rng.randrange(2,7))
        if i%4==0:
            greeble.cylinder((x-sx/2,y,p.z+.07),(x+sx/2,y,p.z+.07),.012,mat=1,sides=8)

# Eight paired heavy batteries and small point-defense mounts.
guns = MeshBatch('Eight twin heavy batteries', '04 Armament', [dark,trim,armor[2]])
for sign in [-1,1]:
    for x in [-.6,.9,2.4,3.9]:
        y=sign*(1.23+max(0,x)*.09)
        z=surface_z(x,y)+.03
        guns.cylinder((x,y,z),(x,y,z+.07),.205,.18,mat=1)
        guns.box((x,y,z+.145),(.32,.28,.17),2,.72)
        for yy in [-.07,.07]:
            guns.cylinder((x-.05,y+yy,z+.20),(x-.49,y+yy,z+.24),.025,.016,1,12)
            guns.cylinder((x-.43,y+yy,z+.235),(x-.51,y+yy,z+.243),.028,.023,0,12)
    for x in [-4,-2.3,-.5,1.2,3,4.6]:
        y=sign*(.54+(x+5)*.20)
        z=surface_z(x,y)+.04
        guns.cylinder((x,y,z),(x,y,z+.065),.075,.055,2,12)
        guns.cylinder((x,y,z+.075),(x-.14,y,z+.095),.012,mat=1,sides=8)

# A ventral docking bay is an attached framed module; no hull boolean is required.
hangar = MeshBatch('Ventral hangar frame', '06 Hangar', [dark,armor[2],trim])
hangar.box((.7,0,-.89),(2.9,1.3,.37),1,.96)
hangar.box((.7,0,-1.09),(2.75,1.22,.16),0)
hangar.box((.7,0,-1.185),(2.45,.94,.035),0)
for sign in [-1,1]:
    hangar.box((.7,sign*.58,-1.20),(2.78,.13,.19),1)
    hangar.box((.7,sign*.49,-1.255),(2.48,.028,.018),2)
    for k in range(18):
        lights.box((-.42+k*.13,sign*.475,-1.255),(.055,.014,.012),0)
for x in [-.67,2.07]:
    hangar.box((x,0,-1.20),(.14,1.24,.19),1)
for x in [-.4,0,.4,.8,1.2,1.6]:
    hangar.box((x,0,-1.225),(.016,.84,.014),2)
hangar.box((2.18,0,-1.06),(.25,.68,.13),1,.7)

# Three main thrusters with open, double-walled nozzles and four auxiliary engines.
engines = MeshBatch('Engine bells and cooling rings', '05 Engines', [dark,trim,armor[1],blue])
for y,z,r in [(0,0,.46),(-.99,0,.35),(.99,0,.35),
              (-1.85,.13,.18),(-1.85,-.18,.16),(1.85,.13,.18),(1.85,-.18,.16)]:
    x0,x1=5.86,6.62 if r>.3 else 6.32
    engines.cylinder((x0,y,z),(x1,y,z),r*.65,r,0,48,False)
    engines.cylinder((x0+.10,y,z),(x1,y,z),r*.49,r*.84,1,48,False)
    # Narrow radial lip connects outer and inner walls, leaving a real opening.
    for xx,rr in [(x1-.025,r),(x0+.22,r*.79),(x0+.12,r*.74)]:
        engines.cylinder((xx,y,z),(xx+.028,y,z),rr*1.04,rr*1.04,1,48,False)
    engines.cylinder((x0+.32,y,z),(x0+.335,y,z),r*.61,mat=3,sides=48)
    for k in range(12):
        angle=k*math.tau/12
        yy,zz=y+math.cos(angle)*r*.83,z+math.sin(angle)*r*.83
        engines.box((x1-.10,yy,zz),(.13,.025,.025),2)
    # Annular visible rim.
    pts=[Vector((x1,y+rr*math.cos(i*math.tau/48),z+rr*math.sin(i*math.tau/48)))
         for rr in [r,r*.84] for i in range(48)]
    engines.polyhedron(pts,[(i,(i+1)%48,(i+1)%48+48,i+48) for i in range(48)],1)

def detail_deck_roofs():
    roofs=MeshBatch('Deck roof armor and service housings','02 Superstructure',armor+[dark,trim,seam])
    # Thin overlapping plates on each exposed terrace; raised fittings stay clear
    # of the tower and leave an accessible service corridor down the center.
    for x,z,length,width,height in [
        (1.75,1.045,6.6,1.58,.30),(2.05,1.31,5.3,1.30,.29),
        (2.65,1.57,3.9,1.05,.25)]:
        top=z+height/2+.012
        for sign in [-1,1]:
            for k in range(int(length/.29)):
                xx=x-length*.435+.14+k*.29
                if xx>x+length*.435-.1:continue
                roofs.box((xx,sign*width*.405,top),(.27,width*.075,.015),2)
        for k in range(5):
            roofs.box((x-length*.435+.035+k*.052,0,top),(.035,width*.7,.016),1)
    # Recessed rooftop heat exchangers, cable trunks and access covers.
    for xx in [1.15,1.85,2.55]:
        for sign in [-1,1]:
            yy=sign*.275
            roofs.box((xx,yy,1.745),(.46,.24,.09),5,.88)
            roofs.box((xx,yy,1.795),(.38,.18,.018),7)
            for j in range(9):
                roofs.box((xx-.17+j*.042,yy,1.81),(.016,.17,.016),6)
    for sign in [-1,1]:
        roofs.cylinder((1.0,sign*.10,1.735),(3.1,sign*.10,1.735),.025,mat=6,sides=12)
        for xx in [1.25,1.85,2.45,3.05]:
            roofs.box((xx,sign*.10,1.73),(.065,.11,.065),1)
    # Engine-room service spine at the stern.
    for sign in [-1,1]:
        roofs.box((5.22,sign*.44,.96),(.72,.34,.17),5,.84)
        for j in range(10):
            roofs.box((4.92+j*.064,sign*.44,1.06),(.025,.29,.045),6)
    return roofs.finish(.003)

detail_deck_roofs()

for batch,width in [(panels,.003),(backing,0),(greeble,.004),(trenches,.002),
                    (decks,.014),(bridge,.004),(vents,.002),(guns,.005),
                    (hangar,.006),(engines,.003),(lights,0)]:
    batch.finish(width)

body['design_scale']='1 Blender meter = 100 real meters'
body['base_hull_length_m']=1200
details['design_notes']='Imperial-inspired 1200m capital ship; bow -X, stern +X, dorsal +Z. Base Body geometry preserved.'

# A separate studio rig preserves the user's existing Camera and Light.
studio=bpy.data.collections.new('Preview Studio')
bpy.context.scene.collection.children.link(studio)
def studio_object(name,data,loc):
    ob=bpy.data.objects.new(name,data)
    studio.objects.link(ob)
    ob.location=loc
    return ob

def aim(ob,target):
    ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()

cam=studio_object('Ship Preview Camera',bpy.data.cameras.new('Ship Preview Camera'),(-12,-16,12))
cam.data.type='ORTHO'
cam.data.ortho_scale=16.5
aim(cam,(.1,0,.55))
for name,loc,power,size,color in [
    ('Key',(-5,-7,12),2300,8,(.89,.94,1)),
    ('Fill',(1,7,7),1800,7,(.60,.74,1)),
    ('Rim',(7,3,8),2600,6,(1,.86,.67)),
    ('Ventral fill',(0,-3,-8),1200,7,(.70,.80,1))]:
    data=bpy.data.lights.new('Ship '+name,'AREA')
    data.energy=power
    data.shape='DISK'
    data.size=size
    data.color=color
    ob=studio_object('Ship '+name,data,loc)
    aim(ob,(0,0,0))

scene=bpy.context.scene
scene.camera=cam
world=bpy.data.worlds.new('Ship Preview World')
world.use_nodes=True
world.node_tree.nodes['Background'].inputs['Color'].default_value=(.025,.035,.055,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=.45
scene.world=world
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=1600
scene.render.resolution_y=1100
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.film_transparent=False
scene.view_settings.view_transform='AgX'

# Set a clean modeling view. Materials also work in solid mode without compiling shaders.
for ob in bpy.context.selected_objects:ob.select_set(False)
body.select_set(True)
bpy.context.view_layer.objects.active=body
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type!='VIEW_3D':continue
        s=area.spaces.active
        s.shading.type='SOLID'
        s.shading.light='STUDIO'
        s.shading.color_type='MATERIAL'
        s.shading.show_cavity=True
        s.shading.cavity_type='BOTH'
        s.overlay.show_extras=False
        s.overlay.show_floor=False
        s.overlay.show_axis_x=False
        s.overlay.show_axis_y=False
        s.show_region_ui=False
        s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
        s.region_3d.view_distance=13.8
        s.region_3d.view_location=(.1,0,.55)
        s.region_3d.view_perspective='ORTHO'

bpy.context.view_layer.update()
assert original_vertices==[tuple(v.co) for v in body.data.vertices]
ROOT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'body_detail_study.blend'))
result={'armor_tiles':tile_count,'added_objects':len(details.all_objects),
        'base_hull_preserved':True,'blend_file':str(ROOT/'body_detail_study.blend')}
