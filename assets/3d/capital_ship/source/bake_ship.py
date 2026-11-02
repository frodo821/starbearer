"""Run with Blender --background <detail-study.blend> --python bake_ship.py.

Builds a separate game asset; never overwrites the authoring scene.
Units stay at 1 Blender unit = 100 design meters. Bow is -X in Blender.
"""
import bpy
import bmesh
import json
import math
import time
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent
TEX = OUT / 'textures'
TEX.mkdir(parents=True, exist_ok=True)
START = time.time()
STATUS = OUT / 'source' / 'bake_status.json'

def status(phase, **extra):
    data = {'phase': phase, 'elapsed_seconds': round(time.time()-START, 1), **extra}
    STATUS.write_text(json.dumps(data, indent=2))
    print('BAKE_STATUS', json.dumps(data), flush=True)

def activate(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

def triangles(ob, evaluated=False):
    mesh = ob.evaluated_get(bpy.context.evaluated_depsgraph_get()).data if evaluated else ob.data
    mesh.calc_loop_triangles()
    return len(mesh.loop_triangles)

scene = bpy.context.scene
assert bpy.context.mode == 'OBJECT'
high = [bpy.data.objects['Body']] + list(bpy.data.collections['Ship Details'].all_objects)
high = [o for o in high if o.type == 'MESH']
high_triangles = sum(triangles(o, True) for o in high)
status('building_low_mesh', source_triangles=high_triangles)

game_col = bpy.data.collections.new('GAME / Baked Capital Ship')
scene.collection.children.link(game_col)
keep = [
    'Body', 'Terraced command decks', 'Hull-conforming command foundation',
    'Bridge glazing and antenna supports', 'Faceted sensor dome port',
    'Faceted sensor dome starboard', 'Eight twin heavy batteries',
    'Engine bells and cooling rings', 'Recessed engine mounting wells',
    'Ventral hangar frame', 'Hangar attachment fairing',
]
parts = []
for name in keep:
    src = bpy.data.objects[name]
    low = bpy.data.objects.new('LOW / '+name, src.data.copy())
    game_col.objects.link(low)
    low.matrix_world = src.matrix_world.copy()
    activate(low)
    bm = bmesh.new()
    bm.from_mesh(low.data)
    # Remove disconnected nozzle cooling tabs; their shading is baked instead.
    if name == 'Engine bells and cooling rings':
        seen = set()
        remove = []
        for v in bm.verts:
            if v in seen: continue
            stack, component = [v], []
            seen.add(v)
            while stack:
                p = stack.pop()
                component.append(p)
                for e in p.link_edges:
                    other = e.other_vert(p)
                    if other not in seen:
                        seen.add(other)
                        stack.append(other)
            if len(component) < 24: remove.extend(component)
        bmesh.ops.delete(bm, geom=remove, context='VERTS')
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.00001)
    if name == 'Hull-conforming command foundation':
        bmesh.ops.dissolve_limit(bm, angle_limit=.02, verts=list(bm.verts),
                                 edges=list(bm.edges), use_dissolve_boundaries=False)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(low.data)
    bm.free()
    if name in ['Engine bells and cooling rings', 'Recessed engine mounting wells',
                'Eight twin heavy batteries']:
        mod = low.modifiers.new('Game silhouette reduction', 'DECIMATE')
        mod.ratio = .42 if name != 'Eight twin heavy batteries' else .58
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    # Match the author's hard-surface normals. Face boundaries also stabilize
    # tangent-space baking after glTF triangulation.
    for p in low.data.polygons: p.use_smooth = False
    low.data.materials.clear()
    parts.append(low)

bpy.ops.object.select_all(action='DESELECT')
for p in parts: p.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
low = bpy.context.object
low.name = 'CapitalShip_LOD0'
low.data.name = 'CapitalShip_LOD0'
tri = low.modifiers.new('Stable tangent triangulation', 'TRIANGULATE')
tri.quad_method = 'FIXED'
tri.ngon_method = 'CLIP'
bpy.ops.object.modifier_apply(modifier=tri.name)
activate(low)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.0035,
                         area_weight=.5, correct_aspect=True, scale_to_bounds=True)
bpy.ops.object.mode_set(mode='OBJECT')
low.data.uv_layers.active.name = 'BakeUV'
low_triangles = triangles(low)
status('uv_ready', low_triangles=low_triangles)

mat = bpy.data.materials.new('CapitalShip / Baked PBR')
mat.use_nodes = True
low.data.materials.clear()
low.data.materials.append(mat)
nodes = mat.node_tree.nodes
links = mat.node_tree.links
bsdf = nodes.get('Principled BSDF')
bsdf.inputs['Metallic'].default_value = .25
bsdf.inputs['Roughness'].default_value = .6
textures = {}
for name, size, color, space in [
    ('normal', 4096, (.5,.5,1,1), 'Non-Color'),
    ('base_color', 4096, (0,0,0,1), 'sRGB'),
    ('emission', 2048, (0,0,0,1), 'sRGB'),
]:
    img = bpy.data.images.new('capital_ship_'+name, width=size, height=size, alpha=False)
    img.generated_color = color
    img.colorspace_settings.name = space
    img.file_format = 'PNG'
    img.filepath_raw = str(TEX / ('capital_ship_'+name+'.png'))
    node = nodes.new('ShaderNodeTexImage')
    node.name = 'Baked '+name
    node.label = 'Baked '+name
    node.image = img
    textures[name] = (img,node)

# CPU is predictable on machines where the interactive Blender Metal device
# configuration is not available to a background process.
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 1
scene.render.bake.use_selected_to_active = True
scene.render.bake.use_clear = False
scene.render.bake.margin = 12
scene.render.bake.margin_type = 'EXTEND'
scene.render.bake.cage_extrusion = .18
scene.render.bake.max_ray_distance = .36
scene.render.bake.use_cage = False
for name, bake_type in [('normal','NORMAL'),('base_color','DIFFUSE'),('emission','EMIT')]:
    status('baking_'+name, low_triangles=low_triangles)
    img,node = textures[name]
    for n in nodes: n.select = False
    node.select = True
    nodes.active = node
    activate(low)
    for src in high:
        src.hide_set(False)
        src.hide_render = False
        src.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    kwargs = dict(type=bake_type, use_selected_to_active=True, use_clear=False,
                  cage_extrusion=.18, max_ray_distance=.36, margin=12,
                  normal_space='TANGENT', normal_r='POS_X', normal_g='POS_Y', normal_b='POS_Z')
    if bake_type == 'DIFFUSE': kwargs['pass_filter'] = {'COLOR'}
    bpy.ops.object.bake(**kwargs)
    img.save()
    status('saved_'+name, texture=str(img.filepath_raw))

links.new(textures['base_color'][1].outputs['Color'], bsdf.inputs['Base Color'])
normal = nodes.new('ShaderNodeNormalMap')
normal.space = 'TANGENT'
normal.uv_map = 'BakeUV'
links.new(textures['normal'][1].outputs['Color'], normal.inputs['Color'])
links.new(normal.outputs['Normal'], bsdf.inputs['Normal'])
links.new(textures['emission'][1].outputs['Color'], bsdf.inputs['Emission Color'])
bsdf.inputs['Emission Strength'].default_value = 1
for i,(name,(_,node)) in enumerate(textures.items()): node.location = (-650,200-i*260)
normal.location = (-350,-60)

low['design_scale'] = '1 unit = 100 real meters; 12-unit base hull = 1200m'
low['blender_bow_axis'] = '-X'
low['baked_normal_convention'] = 'tangent-space OpenGL +Y, MikkTSpace'
low['source_triangles'] = high_triangles
low['game_triangles'] = low_triangles

# Only the selected game mesh is exported; high geometry, cameras and lights
# remain in the separate source blend for inspection and rebaking.
for src in high:
    src.hide_render = True
    src.hide_set(True)
activate(low)
scene.render.engine = 'BLENDER_EEVEE'
bpy.context.view_layer.update()
status('exporting_glb', low_triangles=low_triangles)
bpy.ops.export_scene.gltf(filepath=str(OUT/'capital_ship.glb'), export_format='GLB',
    use_selection=True, export_apply=True, export_tangents=True, export_normals=True,
    export_texcoords=True, export_animations=False, export_cameras=False,
    export_lights=False, export_extras=True, export_yup=True)

# A second camera angle makes changes in the normal texture easy to review.
scene.render.resolution_x = 1600
scene.render.resolution_y = 1100
scene.render.resolution_percentage = 100
scene.render.filepath = str(OUT/'source'/'baked_preview.png')
for img,_ in textures.values():
    img.pack()
    img.filepath_raw = bpy.path.relpath(img.filepath_raw, start=str(OUT/'source'))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'source'/'capital_ship_baked.blend'))
status('rendering_preview')
bpy.ops.render.render(write_still=True)
report = {
    'source_blend': 'art/ships/body_detail_study.blend',
    'game_mesh': 'capital_ship.glb',
    'normal': 'textures/capital_ship_normal.png',
    'base_color': 'textures/capital_ship_base_color.png',
    'emission': 'textures/capital_ship_emission.png',
    'normal_convention': 'OpenGL +Y tangent space, glTF tangents included',
    'source_triangles': high_triangles, 'game_triangles': low_triangles,
    'triangle_reduction_percent': round(100*(1-low_triangles/high_triangles),2),
    'materials': 1, 'atlas_resolution': [4096,4096], 'emission_resolution': [2048,2048],
    'kept_geometry': keep,
    'baked_details': ['armor plates','service machinery','radiator louvres',
                      'trench ribs','deck fittings','windows','engine cooling tabs',
                      'aft bulkhead machinery','small bevels'],
    'roughness': .6, 'metallic': .25,
    'design_meters_per_unit': 100, 'runtime_scale_applied': False,
    'collision_mesh': False,
}
(OUT/'source'/'bake_report.json').write_text(json.dumps(report,indent=2))
status('complete', **report)
