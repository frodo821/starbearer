extends SceneTree

func _initialize() -> void:
    call_deferred("_verify")

func _verify() -> void:
    var doc = GLTFDocument.new()
    var state = GLTFState.new()
    var error = doc.append_from_file("res://assets/3d/freighter/freighter.glb", state)
    assert(error == OK)
    var scene = doc.generate_scene(state)
    assert(scene != null)
    root.add_child(scene)
    var meshes: Array[MeshInstance3D] = []
    collect_meshes(scene, meshes)
    assert(meshes.size() == 7)
    var bounds = aggregate_bounds(meshes)
    assert(bounds.size.x > 25 and bounds.size.x < 27)
    assert(bounds.size.z > 40 and bounds.size.z < 43)
    var triangles = 0
    var surfaces = 0
    for instance in meshes:
        var mesh = instance.mesh
        surfaces += mesh.get_surface_count()
        for i in mesh.get_surface_count():
            var arrays = mesh.surface_get_arrays(i)
            triangles += arrays[Mesh.ARRAY_INDEX].size() / 3
            assert(mesh.surface_get_material(i) != null)
    var players = scene.find_children("*", "AnimationPlayer", true, false)
    assert(players.size() == 1)
    var player = players[0] as AnimationPlayer
    assert(player.has_animation("Scene"))
    var duration = player.get_animation("Scene").length
    player.play("Scene")
    player.seek(duration, true)
    player.advance(0)
    var open_bounds = aggregate_bounds(meshes)
    assert(open_bounds.size.z > bounds.size.z + 6.0)
    var report = {"godot_version": Engine.get_version_info().string,
                  "mesh_instances": meshes.size(), "surfaces": surfaces,
                  "triangles": triangles, "dimensions_godot_xyz_m": str(bounds.size),
                  "open_dimensions_godot_xyz_m": str(open_bounds.size),
                  "ramp_animation": "Scene", "animation_duration_seconds": duration,
                  "ramp_animation_verified": true,
                  "meters_per_unit": 1, "import_passed": true}
    var file = FileAccess.open("res://assets/3d/freighter/source/import_report.json", FileAccess.WRITE)
    file.store_string(JSON.stringify(report, "  "))
    file.close()
    scene.free()
    print("FREIGHTER_VERIFIED ", JSON.stringify(report))
    quit()

func collect_meshes(node: Node, result: Array[MeshInstance3D]) -> void:
    if node is MeshInstance3D: result.append(node)
    for child in node.get_children(): collect_meshes(child, result)

func aggregate_bounds(meshes: Array[MeshInstance3D]) -> AABB:
    var bounds = meshes[0].global_transform * meshes[0].get_aabb()
    for i in range(1, meshes.size()):
        bounds = bounds.merge(meshes[i].global_transform * meshes[i].get_aabb())
    return bounds
