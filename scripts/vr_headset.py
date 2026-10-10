"""A wearable, camera-safe VR headset for Daniel's first-person viewpoint.

The root uses local -Z forward and +Y up. The optical opening is a single broad
aperture: there is deliberately no opaque faceplate or bridge across the view.
"""

import math

import bpy
from mathutils import Vector


def create_headset(materials, collection, eye_location, look_target):
    """Create the headset around an eye position and return its rig and parts.

    Recommended first-person camera: lens 22 mm, horizontal sensor 36 mm,
    sensor_fit='HORIZONTAL', clip_start=0.01 m, 1600x1100. This gives a 79-degree
    horizontal field of view inside an approximately 80-by-59-degree aperture,
    with a subtle soft rim at its outer corners.
    Parent the camera to ``root`` with local location/rotation both zero and an
    identity parent inverse. The camera then shares the headset's -Z/+Y frame.
    Do not place a camera at the front face of the visor.
    """
    materials = materials or {}

    def make_material(name, color, roughness=0.5, metallic=0.0):
        existing = bpy.data.materials.get(name)
        if existing:
            return existing
        material = bpy.data.materials.new(name)
        material.use_nodes = True
        material.diffuse_color = (*color, 1)
        shader = material.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Base Color"].default_value = (*color, 1)
        shader.inputs["Roughness"].default_value = roughness
        shader.inputs["Metallic"].default_value = metallic
        return material

    shell = materials.get("headset_shell") or make_material("Headset.MatteGraphite", (0.018, 0.024, 0.026), 0.43)
    cushion = materials.get("headset_cushion") or make_material("Headset.SoftCharcoal", (0.012, 0.018, 0.019), 0.91)
    trim = materials.get("headset_trim") or make_material("Headset.TealFabric", (0.034, 0.116, 0.112), 0.84)
    hardware = materials.get("headset_hardware") or make_material("Headset.SatinHardware", (0.115, 0.148, 0.150), 0.29, 0.55)
    optical = materials.get("headset_optical") or make_material("Headset.TrackingOptics", (0.022, 0.043, 0.055), 0.12, 0.30)
    led = make_material("Headset.StatusLED", (0.16, 0.72, 0.51), 0.25)
    led_shader = led.node_tree.nodes.get("Principled BSDF")
    led_shader.inputs["Emission Color"].default_value = (0.16, 0.72, 0.51, 1)
    led_shader.inputs["Emission Strength"].default_value = 0.65

    root = bpy.data.objects.new("Headset_Daniel_Rig", None)
    collection.objects.link(root)
    root.location = eye_location
    direction = Vector(look_target) - Vector(eye_location)
    if direction.length_squared < 1e-8:
        direction = Vector((0, -1, 0))
    root.rotation_euler = direction.normalized().to_track_quat("-Z", "Y").to_euler()
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.08
    root["wearer"] = "Daniel"
    root["camera_position"] = "Parent at local origin; -Z forward, +Y up"
    root["recommended_lens_mm"] = 22.0
    root["aperture"] = "Wide unobstructed first-person opening; no central divider"
    objects = [root]

    def object_link(name, data, location=(0, 0, 0), rotation=(0, 0, 0)):
        obj = bpy.data.objects.new("Headset_" + name, data)
        collection.objects.link(obj)
        obj.parent = root
        obj.location = location
        obj.rotation_euler = rotation
        objects.append(obj)
        return obj

    def mesh_object(name, vertices, faces, mesh_materials, face_materials=None, bevel=0):
        mesh = bpy.data.meshes.new("Headset_" + name + ".Mesh")
        mesh.from_pydata(vertices, [], faces)
        for material in mesh_materials:
            mesh.materials.append(material)
        mesh.update()
        obj = object_link(name, mesh)
        for index, polygon in enumerate(mesh.polygons):
            polygon.use_smooth = True
            if face_materials:
                polygon.material_index = face_materials[index]
        if bevel:
            modifier = obj.modifiers.new("Soft manufactured edges", "BEVEL")
            modifier.width = bevel
            modifier.segments = 3
        return obj

    def rounded_rectangle(hx, hy, radius, z, samples=16):
        points = []
        # Positive winding as seen from local +Z. Each corner includes its end
        # point through the following segment, avoiding doubled vertices.
        for cx, cy, start in ((hx-radius, hy-radius, 0), (-hx+radius, hy-radius, 90),
                              (-hx+radius, -hy+radius, 180), (hx-radius, -hy+radius, 270)):
            for step in range(samples):
                angle = math.radians(start + step * 90 / samples)
                points.append((cx + radius * math.cos(angle), cy + radius * math.sin(angle), z))
        return points

    # Four concentric outlines form a complete shallow hollow shell. The
    # opening widens toward the front to follow the first-person viewing cone.
    sections = [
        rounded_rectangle(0.119, 0.076, 0.027, -0.104),  # outer front
        rounded_rectangle(0.113, 0.070, 0.025, -0.037),  # outer facial edge
        rounded_rectangle(0.082, 0.052, 0.021, -0.037),  # inner facial opening
        rounded_rectangle(0.088, 0.059, 0.025, -0.104),  # inner front opening
    ]
    segment_count = len(sections[0])
    vertices = [point for section in sections for point in section]
    faces, face_materials = [], []
    for section in range(4):
        following_section = (section + 1) % 4
        for point in range(segment_count):
            following = (point + 1) % segment_count
            faces.append((section*segment_count + point,
                          section*segment_count + following,
                          following_section*segment_count + following,
                          following_section*segment_count + point))
            face_materials.append((0, 1, 1, 0)[section])
    frame = mesh_object("HollowVisorShell", vertices, faces, [shell, cushion], face_materials, bevel=0.0022)
    frame["design_note"] = "An open ring, never a solid box across the player's view"

    def tube(name, points, material, radius=0.0015, cyclic=False):
        data = bpy.data.curves.new("Headset_" + name + ".Curve", "CURVE")
        data.dimensions = "3D"
        data.bevel_depth = radius
        data.bevel_resolution = 3
        data.resolution_u = 2
        spline = data.splines.new("POLY")
        spline.points.add(len(points) - 1)
        for target, point in zip(spline.points, points):
            target.co = (*point, 1)
        spline.use_cyclic_u = cyclic
        data.materials.append(material)
        return object_link(name, data)

    tube("FacialCushionWelt", rounded_rectangle(0.087, 0.057, 0.024, -0.034), trim, 0.0042, True)
    tube("FrontShellReveal", rounded_rectangle(0.114, 0.071, 0.026, -0.105), hardware, 0.00065, True)

    # Two soft side arcs can barely enter the extreme perimeter of a 22 mm
    # first-person lens. They never cross the middle or create a binocular mask.
    for side, label in ((-1, "Left"), (1, "Right")):
        arc = []
        for step in range(35):
            y = -0.048 + step * 0.096 / 34
            edge_x = 0.088 - 0.006 * (abs(y) / 0.048) ** 3
            arc.append((side * edge_x, y, -0.104))
        tube("PeripheralSoftRim." + label, arc, cushion, 0.0017)

    def rounded_block(name, center, dimensions, material, bevel=0.004):
        cx, cy, cz = center
        hx, hy, hz = (dimension * 0.5 for dimension in dimensions)
        vertices = [(cx+x*hx, cy+y*hy, cz+z*hz) for z in (-1, 1) for y in (-1, 1) for x in (-1, 1)]
        faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4),
                 (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
        return mesh_object(name, vertices, faces, [material], bevel=bevel)

    def side_disc(name, x, y, z, radius, thickness, material):
        segments = 48
        vertices = []
        for offset in (-thickness/2, thickness/2):
            for index in range(segments):
                angle = math.tau * index / segments
                vertices.append((x + offset, y + radius * math.cos(angle), z + radius * math.sin(angle)))
        faces = [tuple(reversed(range(segments))), tuple(range(segments, 2*segments))]
        for index in range(segments):
            following = (index+1) % segments
            faces.append((index, following, segments+following, segments+index))
        return mesh_object(name, vertices, faces, [material], bevel=0.0008)

    for side, label in ((-1, "Left"), (1, "Right")):
        rounded_block("StrapAnchor." + label, (side*0.115, 0.002, -0.034), (0.020, 0.035, 0.056), shell, 0.006)
        side_disc("AdjustmentPivot." + label, side*0.125, 0.002, -0.041, 0.014, 0.005, hardware)
        side_disc("PeripheralTrackingLens." + label, side*0.124, 0.011, -0.080, 0.006, 0.003, optical)
        # Vents are on the outside of the visor; invisible to the central eye.
        for vent in range(4):
            rounded_block("SideVent.%s.%02d" % (label, vent),
                          (side*0.120, -0.022, -0.060-vent*0.007),
                          (0.0025, 0.014, 0.0025), cushion, 0.0008)

    rounded_block("StatusLED", (0.054, 0.068, -0.106), (0.008, 0.0028, 0.0015), led, 0.0007)
    rounded_block("PowerButton", (0.085, 0.075, -0.064), (0.019, 0.0035, 0.012), hardware, 0.002)

    # Comfortable fabric halo around the back of the head; local +Z is behind
    # the eye, so no part of this assembly can obstruct the rendered view.
    strap_vertices, strap_faces = [], []
    strap_segments = 64
    for index in range(strap_segments + 1):
        angle = math.pi * index / strap_segments
        center = Vector((0.111*math.cos(angle), 0.004, 0.139*math.sin(angle)-0.008))
        normal = Vector((math.cos(angle), 0, math.sin(angle))).normalized()
        for height_side, radial_side in ((-1,-1), (-1,1), (1,1), (1,-1)):
            point = center + Vector((0, height_side*0.014, 0)) + normal*(radial_side*0.0025)
            strap_vertices.append(tuple(point))
    for index in range(strap_segments):
        for side in range(4):
            strap_faces.append((index*4+side, index*4+(side+1)%4,
                                (index+1)*4+(side+1)%4, (index+1)*4+side))
    strap_faces.extend([(3,2,1,0), tuple(strap_segments*4+side for side in range(4))])
    mesh_object("RearFabricHeadband", strap_vertices, strap_faces, [trim], bevel=0.0018)
    rounded_block("RearComfortPad", (0, 0.004, 0.133), (0.074, 0.044, 0.012), cushion, 0.006)

    top_vertices, top_faces = [], []
    top_segments = 36
    front = Vector((0, 0.069, -0.051))
    crown = Vector((0, 0.153, 0.048))
    rear = Vector((0, 0.029, 0.132))
    for index in range(top_segments+1):
        t = index/top_segments
        center = (1-t)**2*front + 2*(1-t)*t*crown + t*t*rear
        tangent = (2*(1-t)*(crown-front)+2*t*(rear-crown)).normalized()
        normal = Vector((0, -tangent.z, tangent.y)).normalized()
        for width_side, normal_side in ((-1,-1), (1,-1), (1,1), (-1,1)):
            point = center + Vector((width_side*0.012,0,0)) + normal*(normal_side*0.002)
            top_vertices.append(tuple(point))
    for index in range(top_segments):
        for side in range(4):
            top_faces.append((index*4+side, index*4+(side+1)%4,
                              (index+1)*4+(side+1)%4, (index+1)*4+side))
    top_faces.extend([(3,2,1,0), tuple(top_segments*4+side for side in range(4))])
    mesh_object("TopComfortStrap", top_vertices, top_faces, [trim], bevel=0.0012)

    return {
        "root": root,
        "headset_root": root,
        "objects": objects,
        "eye_location": tuple(eye_location),
        "look_target": tuple(look_target),
        "camera_settings": {
            "lens": 22.0,
            "sensor_width": 36.0,
            "sensor_fit": "HORIZONTAL",
            "clip_start": 0.01,
            "resolution_x": 1600,
            "resolution_y": 1100,
            "local_location": (0.0, 0.0, 0.0),
            "local_rotation": (0.0, 0.0, 0.0),
        },
    }
