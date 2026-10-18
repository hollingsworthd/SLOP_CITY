"""Independently inspect the native scene and freshly imported game exports.

Run after build_scene.py finishes, using Blender rather than system Python::

    Blender --background --factory-startup --python-exit-code 1 \
        --python scripts/validate_assets.py

The assets are never saved or modified. The sole output is
assets/validation-report.json. A failed check raises an exception so
--python-exit-code makes the command fail in automation. No Unity test is run.
"""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import re
import sys
import traceback

import bpy
from mathutils import Vector


# This contract is deliberately independent of build_scene.py and its manifest.
# Coordinates here are Blender Z-up metres. Blender's FBX/glTF importers undo
# the exporters' axis conversion before these world-space checks are applied.
MARKERS = {
    "Spawn_Daniel_Seated": (0.0, 0.94, 1.18),
    "Spawn_Daniel_Stand": (0.0, -0.20, 0.08),
    "LookAt_Daniel_Seated": (2.30, -2.45, 1.18),
    "Interact_TV": (2.30, -2.60, 1.10),
    "Interact_Radio": (-2.55, 0.48, 0.75),
    "Light_TableLamp": (-2.55, 0.70, 1.68),
}
FLOOR_CENTER = (0.0, -0.25, -0.16)
FLOOR_DIMENSIONS = (8.60, 6.50, 0.26)
POSITION_TOLERANCE = 0.002
FLOOR_TOLERANCE = 0.015
LANDMARKS = (
    "Broadcast_TV_Screen", "HoodRich_Radio", "Laptop_Screen",
    "Blue_Book_Folder", "Car_Key_Fob",
)


def vector_list(value):
    return [round(float(component), 6) for component in value]


def close_vector(actual, expected, tolerance):
    return all(abs(float(a) - float(b)) <= tolerance
               for a, b in zip(actual, expected))


def record_check(result, name, passed, **details):
    result["checks"].append({"name": name, "passed": bool(passed), **details})


def aliases(objects, exact_name):
    """Catch Blender's automatic .001 suffixes, including nested suffixes."""
    pattern = re.compile(re.escape(exact_name) + r"(?:\.\d+)*$")
    return [obj for obj in objects if pattern.fullmatch(obj.name)]


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    lower = Vector(tuple(min(point[axis] for point in corners) for axis in range(3)))
    upper = Vector(tuple(max(point[axis] for point in corners) for axis in range(3)))
    return lower, upper


def has_ancestor(obj, ancestor):
    parent = obj.parent
    while parent is not None:
        if parent == ancestor:
            return True
        parent = parent.parent
    return False


def packed(block):
    return bool(getattr(block, "packed_file", None)
                or getattr(block, "packed_files", None))


def inspect_resources(result):
    libraries = [library.filepath for library in bpy.data.libraries]
    resources = []
    unresolved = []
    external = []
    # Render Result and other generated buffers have no external dependencies.
    for collection_name in ("fonts", "images", "sounds", "movieclips", "volumes"):
        for block in getattr(bpy.data, collection_name, ()):
            if block.users == 0:
                continue
            filepath = getattr(block, "filepath", "")
            if collection_name == "fonts" and filepath in ("", "<builtin>"):
                continue
            if collection_name == "images" and block.source in {"GENERATED", "VIEWER"}:
                continue
            embedded = packed(block)
            resolved = str(Path(bpy.path.abspath(filepath)).resolve()) if filepath else None
            exists = bool(resolved and Path(resolved).is_file())
            item = {"kind": collection_name, "name": block.name,
                    "packed": embedded, "path": filepath,
                    "external_file_exists": exists}
            resources.append(item)
            if not embedded:
                external.append(item)
                if not exists:
                    unresolved.append(item)
    result["resources"] = resources
    record_check(result, "no_linked_blend_libraries", not libraries, libraries=libraries)
    record_check(result, "no_missing_file_resources", not unresolved, missing=unresolved)
    record_check(result, "self_contained_file_resources", not external,
                 unpacked_dependencies=external)


def inspect_scene(label, expected_mesh_count=None):
    result = {"checks": [], "markers": {}}
    objects = list(bpy.context.scene.objects)
    all_objects = list(bpy.data.objects)
    meshes = [obj for obj in objects if obj.type == "MESH"]
    portable = [obj for obj in objects if obj.type in {"MESH", "CURVE", "FONT"}]
    counts = {}
    for obj in objects:
        counts[obj.type] = counts.get(obj.type, 0) + 1
    result["object_counts"] = counts
    result["mesh_vertices"] = sum(len(obj.data.vertices) for obj in meshes)
    result["mesh_polygons"] = sum(len(obj.data.polygons) for obj in meshes)
    result["expected_export_mesh_objects"] = len(portable)
    record_check(result, "plausible_mesh_object_count", 300 <= len(meshes) <= 3000,
                 actual=len(meshes), accepted_range=[300, 3000])
    if expected_mesh_count is not None:
        record_check(result, "export_mesh_object_count_matches_native",
                     len(meshes) == expected_mesh_count,
                     expected=expected_mesh_count, actual=len(meshes))
        record_check(result, "export_has_only_meshes_and_markers",
                     all(obj.type in {"MESH", "EMPTY"} for obj in objects),
                     object_types=sorted(counts))
    empty_geometry = [obj.name for obj in meshes
                      if not obj.data.vertices or not obj.data.polygons]
    record_check(result, "mesh_objects_have_geometry", not empty_geometry,
                 empty_meshes=empty_geometry)
    invalid_transforms = [obj.name for obj in objects
                          if any(not math.isfinite(float(value))
                                 for row in obj.matrix_world for value in row)]
    record_check(result, "finite_world_transforms", not invalid_transforms,
                 invalid_objects=invalid_transforms)

    for name, expected_position in MARKERS.items():
        matches = aliases(all_objects, name)
        exact = [obj for obj in matches if obj.name == name and obj in objects]
        unique = len(matches) == 1 and len(exact) == 1
        record_check(result, "unique_marker:" + name, unique,
                     found=[obj.name for obj in matches])
        if len(exact) != 1:
            continue
        obj = exact[0]
        position = obj.matrix_world.translation
        result["markers"][name] = vector_list(position)
        record_check(result, "marker_is_empty:" + name, obj.type == "EMPTY",
                     actual_type=obj.type)
        record_check(result, "marker_position:" + name,
                     close_vector(position, expected_position, POSITION_TOLERANCE),
                     expected=list(expected_position), actual=vector_list(position),
                     tolerance_metres=POSITION_TOLERANCE)

    floor_matches = aliases(all_objects, "Floor_Main")
    floor = next((obj for obj in floor_matches
                  if obj.name == "Floor_Main" and obj in objects), None)
    record_check(result, "unique_floor_mesh",
                 len(floor_matches) == 1 and floor is not None and floor.type == "MESH",
                 found=[obj.name for obj in floor_matches])
    if floor is not None and floor.type == "MESH":
        lower, upper = world_bounds(floor)
        dimensions = upper - lower
        center = (upper + lower) * 0.5
        result["floor"] = {"minimum": vector_list(lower), "maximum": vector_list(upper),
                           "dimensions_metres": vector_list(dimensions),
                           "center_metres": vector_list(center)}
        record_check(result, "floor_has_metre_scale",
                     close_vector(dimensions, FLOOR_DIMENSIONS, FLOOR_TOLERANCE),
                     expected=list(FLOOR_DIMENSIONS), actual=vector_list(dimensions),
                     tolerance_metres=FLOOR_TOLERANCE)
        record_check(result, "floor_world_position",
                     close_vector(center, FLOOR_CENTER, FLOOR_TOLERANCE),
                     expected=list(FLOOR_CENTER), actual=vector_list(center))
        stand = result["markers"].get("Spawn_Daniel_Stand")
        seated = result["markers"].get("Spawn_Daniel_Seated")
        if stand:
            # This is a scale/placement check, not a Unity capsule collision test.
            inside = all(lower[axis] + 0.22 < stand[axis] < upper[axis] - 0.22
                         for axis in (0, 1))
            elevation = stand[2] - upper.z
            record_check(result, "standing_marker_is_near_floor_and_inside_room",
                         inside and 0 <= elevation <= 0.20,
                         height_above_base_floor_metres=round(elevation, 6))
        if seated:
            elevation = seated[2] - upper.z
            record_check(result, "seated_marker_has_plausible_eye_height",
                         0.9 <= elevation <= 1.5,
                         height_above_base_floor_metres=round(elevation, 6))

    missing_landmarks = [name for name in LANDMARKS
                         if not any(obj.name == name and obj.type == "MESH" for obj in objects)]
    record_check(result, "core_prop_meshes_present", not missing_landmarks,
                 missing=missing_landmarks)
    couch_meshes = [obj for obj in meshes if obj.name.startswith("SC_Couch")]
    record_check(result, "couch_geometry_present", len(couch_meshes) >= 10,
                 mesh_objects=len(couch_meshes))
    headset_matches = aliases(all_objects, "Headset_Daniel_Rig")
    headset = next((obj for obj in headset_matches
                    if obj.name == "Headset_Daniel_Rig" and obj in objects), None)
    record_check(result, "unique_headset_rig",
                 len(headset_matches) == 1 and headset is not None and headset.type == "EMPTY",
                 found=[obj.name for obj in headset_matches])
    if headset is not None:
        record_check(result, "headset_at_seated_eye_position",
                     close_vector(headset.matrix_world.translation,
                                  MARKERS["Spawn_Daniel_Seated"], POSITION_TOLERANCE),
                     actual=vector_list(headset.matrix_world.translation))
        headset_meshes = [obj for obj in meshes if has_ancestor(obj, headset)]
        record_check(result, "headset_has_parented_geometry", len(headset_meshes) >= 10,
                     mesh_objects=len(headset_meshes))
    if label == "native":
        units = bpy.context.scene.unit_settings
        record_check(result, "native_units_are_metres",
                     units.system == "METRIC" and abs(units.scale_length - 1) < 1e-6,
                     system=units.system, scale_length=units.scale_length)
        record_check(result, "native_editable_text_preserved", counts.get("FONT", 0) >= 10,
                     text_objects=counts.get("FONT", 0))
        camera = bpy.context.scene.camera
        record_check(result, "native_opens_with_seated_camera",
                     camera is not None and camera.name == "CAM_03_Daniel_Seated",
                     actual=camera.name if camera else None)
        if camera is not None:
            record_check(result, "seated_camera_attached_to_headset_at_local_origin",
                         headset is not None and camera.parent == headset
                         and close_vector(camera.location, (0, 0, 0), POSITION_TOLERANCE)
                         and close_vector(camera.rotation_euler, (0, 0, 0), 1e-5),
                         parent=camera.parent.name if camera.parent else None,
                         local_position=vector_list(camera.location),
                         local_rotation=vector_list(camera.rotation_euler))
            record_check(result, "camera_world_position_matches_seated_spawn",
                         close_vector(camera.matrix_world.translation,
                                      MARKERS["Spawn_Daniel_Seated"], POSITION_TOLERANCE),
                         actual=vector_list(camera.matrix_world.translation))
        required_visible = [bpy.data.objects.get(name)
                            for name in ("COL_Wall_Front", "COL_Wall_Right")]
        record_check(result, "native_interior_walls_visible",
                     all(obj is not None and obj.visible_get() and not obj.hide_render
                         and all(not collection.hide_render for collection in obj.users_collection)
                         for obj in required_visible))
    inspect_resources(result)
    result["passed"] = all(check["passed"] for check in result["checks"])
    return result


def file_info(path):
    result = {"path": str(path), "exists": path.is_file()}
    if path.is_file():
        digest = hashlib.sha256()
        with path.open("rb") as handle:
            for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                digest.update(chunk)
        result.update(bytes=path.stat().st_size, sha256=digest.hexdigest())
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    root = args.root.resolve()
    paths = {"native": root / "SlopCity_CouchSpawn.blend",
             "fbx": root / "Unity/Assets/Art/SlopCity_Lounge.fbx",
             "glb": root / "assets/SlopCity_Lounge.glb"}
    report = {
        "schema_version": 1,
        "validated_at_utc": datetime.now(timezone.utc).isoformat(),
        "blender_version": bpy.app.version_string,
        "validation": "Native inspection and clean-scene Blender FBX/GLB reimport",
        "coordinate_system": "Blender Z-up, one coordinate unit = one metre",
        "testedUnity": False,
        "limitations": [
            "Blender reimport does not verify Unity import, compilation, or Play Mode.",
            "This validation does not test character collisions, lighting, or material appearance.",
        ],
        "assets": {},
    }
    expected_mesh_count = None
    for label, path in paths.items():
        item = file_info(path)
        try:
            if not path.is_file():
                raise FileNotFoundError(str(path))
            if label == "native":
                bpy.ops.wm.open_mainfile(filepath=str(path), load_ui=False, use_scripts=False)
            else:
                # Reset datablocks too: stale native objects must not hide a bad export.
                bpy.ops.wm.read_factory_settings(use_empty=True)
                if label == "fbx":
                    bpy.ops.import_scene.fbx(filepath=str(path), use_custom_normals=True)
                else:
                    bpy.ops.import_scene.gltf(filepath=str(path))
            bpy.context.view_layer.update()
            item.update(inspect_scene(label, expected_mesh_count))
            if label == "native":
                expected_mesh_count = item["expected_export_mesh_objects"]
            elif expected_mesh_count is None:
                record_check(item, "native_count_baseline_available", False)
                item["passed"] = False
        except Exception as exc:
            item.update(passed=False, error=str(exc), traceback=traceback.format_exc())
        report["assets"][label] = item
        print("SLOP_CITY_ASSET_CHECK " + label + " " + ("PASS" if item["passed"] else "FAIL"))
    report["passed"] = all(item["passed"] for item in report["assets"].values())
    report["failed_checks"] = [label + ": " + check["name"]
                               for label, item in report["assets"].items()
                               for check in item.get("checks", []) if not check["passed"]]
    output = root / "assets/validation-report.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("SLOP_CITY_VALIDATION_REPORT " + str(output))
    if not report["passed"]:
        raise RuntimeError("Asset validation failed; inspect " + str(output))


if __name__ == "__main__":
    main()
