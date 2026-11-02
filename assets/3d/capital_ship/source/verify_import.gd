extends SceneTree

func _initialize() -> void:
    var packed = load("res://assets/3d/capital_ship/capital_ship.glb") as PackedScene
    assert(packed != null, "GLB failed to import")
    var instance = packed.instantiate()
    root.add_child(instance)
    var meshes: Array[MeshInstance3D] = []
    collect_meshes(instance, meshes)
    assert(meshes.size() == 1, "Expected one game mesh")
    var mesh = meshes[0].mesh
    assert(mesh.get_surface_count() == 1, "Expected one material surface")
    var arrays = mesh.surface_get_arrays(0)
    var vertices = arrays[Mesh.ARRAY_VERTEX].size()
    var indices = arrays[Mesh.ARRAY_INDEX].size()
    assert(arrays[Mesh.ARRAY_NORMAL].size() == vertices)
    assert(arrays[Mesh.ARRAY_TANGENT].size() == vertices * 4)
    assert(arrays[Mesh.ARRAY_TEX_UV].size() == vertices)
    var material = mesh.surface_get_material(0) as StandardMaterial3D
    assert(material != null)
    assert(material.normal_enabled and material.normal_texture != null)
    assert(material.albedo_texture != null)
    assert(material.emission_enabled and material.emission_texture != null)
    var report = {
        "godot_version": Engine.get_version_info().string,
        "mesh_instances": meshes.size(),
        "surfaces": mesh.get_surface_count(),
        "triangles": indices / 3,
        "exported_vertices": vertices,
        "normal_enabled": material.normal_enabled,
        "normal_texture_size": str(material.normal_texture.get_size()),
        "emission_enabled": material.emission_enabled,
        "tangents_present": true,
        "bounds": str(mesh.get_aabb()),
    }
    var file = FileAccess.open("res://assets/3d/capital_ship/source/import_report.json", FileAccess.WRITE)
    file.store_string(JSON.stringify(report, "  "))
    file.close()
    print("SHIP_IMPORT_VERIFIED ", JSON.stringify(report))
    quit(0)

func collect_meshes(node: Node, result: Array[MeshInstance3D]) -> void:
    if node is MeshInstance3D:
        result.append(node)
    for child in node.get_children():
        collect_meshes(child, result)
