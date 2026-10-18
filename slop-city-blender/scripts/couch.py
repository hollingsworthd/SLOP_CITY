"""Soft, sewn three-seat sofa for the Daniel first-person room.

Blender 4.5 compatible. This module adds geometry to the supplied collection;
it does not touch the camera, renderer, scene lighting, or existing objects.
"""

import math

import bpy
from mathutils import Vector


TAU = math.tau


def create_couch(materials, collection):
    """Create a 3.5 m moss sofa, accessories, and seated/standing spawn empties.

    ``materials`` supplies fabric, fabric_dark, piping, orange, mustard, wood,
    blanket, and cream Blender materials. Couch geometry is parented to the
    returned empty at (0, 1.1, 0). Spawn empties use world coordinates and are
    deliberately left unparented so the room builder can set their orientation.
    """
    fabric = materials["fabric"]
    fabric_dark = materials["fabric_dark"]
    piping = materials["piping"]
    wood = materials["wood"]
    cream = materials["cream"]

    root = bpy.data.objects.new("SC_Couch.Root", None)
    collection.objects.link(root)
    root.location = (0.0, 1.1, 0.0)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.18
    root["description"] = "Deep moss upholstery, sewn cushions, walnut feet, and a woven throw"
    root["nominal_width_m"] = 3.5
    objects = [root]

    def link_object(name, data, location=(0, 0, 0), rotation=(0, 0, 0)):
        obj = bpy.data.objects.new("SC_Couch." + name, data)
        collection.objects.link(obj)
        obj.parent = root
        obj.location = location
        obj.rotation_euler = rotation
        objects.append(obj)
        return obj

    def mesh_object(name, vertices, faces, material, location=(0, 0, 0), rotation=(0, 0, 0), subdivision=0):
        mesh = bpy.data.meshes.new("SC_Couch." + name + ".Mesh")
        mesh.from_pydata(vertices, [], faces)
        mesh.materials.append(material)
        mesh.update()
        obj = link_object(name, mesh, location, rotation)
        for polygon in mesh.polygons:
            polygon.use_smooth = True
        if subdivision:
            modifier = obj.modifiers.new("Soft upholstery subdivision", "SUBSURF")
            modifier.subdivision_type = "CATMULL_CLARK"
            modifier.levels = subdivision
            modifier.render_levels = subdivision
        return obj

    def signed_power(value, exponent):
        return math.copysign(abs(value) ** exponent, value)

    def upholstered(name, dimensions, location, material=fabric, rotation=(0, 0, 0), depression=0.0, seed=0.0, fullness=0.48):
        """A closed, rounded superellipsoid, with broad gently yielding panels.

        This is a proper curved surface rather than a sharp cuboid. Its crown,
        small compression folds, and optional sitting depression are modeled
        before a light subdivision pass, so they also survive flat materials.
        """
        half_x, half_y, half_z = (value * 0.5 for value in dimensions)
        segments = 64
        rings = 24
        vertices = [(0, 0, -half_z)]
        for ring in range(1, rings):
            latitude = -math.pi * 0.5 + math.pi * ring / rings
            radius = max(0.0, math.cos(latitude)) ** fullness
            z = half_z * signed_power(math.sin(latitude), 0.65)
            for side in range(segments):
                angle = TAU * side / segments
                nx = signed_power(math.cos(angle), 0.48) * radius
                ny = signed_power(math.sin(angle), 0.48) * radius
                x, y = half_x * nx, half_y * ny
                panel = max(0.0, math.sin(latitude)) ** 5
                dent = depression * math.exp(-((nx - 0.05) ** 2 * 5.0 + (ny + 0.13) ** 2 * 3.5)) * panel
                # Subtle, repeatable soft compressions beside the stitched edge.
                rumple = 0.0028 * math.sin(angle * 7 + seed) * math.sin(latitude * 4) ** 2
                rumple += 0.0015 * math.cos(angle * 11 + seed * 0.7) * (1 - abs(math.sin(latitude)))
                vertices.append((x, y, z - dent + rumple))
        vertices.append((0, 0, half_z - depression * math.exp(-0.0632)))
        faces = []
        for side in range(segments):
            following = (side + 1) % segments
            faces.append((0, 1 + following, 1 + side))
        for ring in range(rings - 2):
            offset = 1 + ring * segments
            next_offset = offset + segments
            for side in range(segments):
                following = (side + 1) % segments
                faces.append((offset + side, offset + following, next_offset + following, next_offset + side))
        last = 1 + (rings - 2) * segments
        pole = len(vertices) - 1
        for side in range(segments):
            faces.append((last + side, last + (side + 1) % segments, pole))
        obj = mesh_object(name, vertices, faces, material, location, rotation, subdivision=1)
        obj["construction"] = "Sewn rounded upholstery with a soft modeled crown"
        return obj

    def curve(name, points, material, radius=0.0055, cyclic=False, location=(0, 0, 0), rotation=(0, 0, 0)):
        data = bpy.data.curves.new("SC_Couch." + name + ".Curve", "CURVE")
        data.dimensions = "3D"
        data.resolution_u = 2
        data.bevel_depth = radius
        data.bevel_resolution = 3
        spline = data.splines.new("POLY")
        spline.points.add(len(points) - 1)
        for destination, source in zip(spline.points, points):
            destination.co = (*source, 1)
        spline.use_cyclic_u = cyclic
        data.materials.append(material)
        return link_object(name, data, location, rotation)

    def outline(name, a, b, location, material=piping, plane="XY", radius=0.0055, rotation=(0, 0, 0), depth=0.0):
        points = []
        for i in range(112):
            angle = TAU * i / 112
            u = a * signed_power(math.cos(angle), 0.48)
            v = b * signed_power(math.sin(angle), 0.48)
            points.append((u, v, depth) if plane == "XY" else (u, depth, v))
        return curve(name, points, material, radius, True, location, rotation)

    def front_welt(name, dimensions, location, rotation=(0, 0, 0), material=piping, fullness=0.48, radius=0.0048):
        """Trace a seam on the curved front skin, rather than floating a flat
        rectangle in front of it. This is especially visible on the arm caps.
        """
        hx, hy, hz = (value * 0.5 for value in dimensions)
        points = []
        for i in range(112):
            angle = TAU * i / 112
            nx = 0.94 * signed_power(math.cos(angle), fullness)
            nz = 0.94 * signed_power(math.sin(angle), 0.65)
            ring_radius = max(0.001, 1 - abs(nz) ** (2 / 0.65)) ** (fullness / 2)
            radial_fraction = min(0.999, abs(nx) / ring_radius)
            ny = ring_radius * max(0.0, 1 - radial_fraction ** (2 / 0.48)) ** (0.48 / 2)
            points.append((hx * nx, -hy * ny - 0.0015, hz * nz))
        return curve(name, points, material, radius, True, location, rotation)

    def wood_block(name, dimensions, location, rotation=(0, 0, 0), bevel=0.025):
        x, y, z = (value * 0.5 for value in dimensions)
        vertices = [(-x, -y, -z), (x, -y, -z), (x, y, -z), (-x, y, -z),
                    (-x, -y, z), (x, -y, z), (x, y, z), (-x, y, z)]
        faces = [(0, 3, 2, 1), (0, 1, 5, 4), (1, 2, 6, 5),
                 (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 6, 7)]
        obj = mesh_object(name, vertices, faces, wood, location, rotation)
        modifier = obj.modifiers.new("Rounded walnut edges", "BEVEL")
        modifier.width = bevel
        modifier.segments = 4
        modifier.harden_normals = True
        normals = obj.modifiers.new("Broad polished face normals", "WEIGHTED_NORMAL")
        normals.keep_sharp = True
        return obj

    # Recessed warm wood base and four low, slightly splayed solid walnut feet.
    wood_block("WalnutPlinth", (3.23, 0.96, 0.105), (0, 0.015, 0.195), bevel=0.035)
    for side in (-1, 1):
        for front in (-1, 1):
            wood_block(
                "Foot.%s.%s" % ("Left" if side < 0 else "Right", "Front" if front < 0 else "Back"),
                (0.16, 0.17, 0.19),
                (side * 1.42, front * 0.40, 0.103),
                (front * 0.07, -side * 0.07, 0),
                bevel=0.028,
            )

    upholstered("LowerDeck", (3.40, 1.17, 0.30), (0, 0.005, 0.345), fabric_dark, seed=2)
    upholstered("FrontApron", (3.28, 0.19, 0.27), (0, -0.493, 0.35), fabric, seed=3)
    outline("DeckWelt", 1.647, 0.558, (0, 0.005, 0.358), radius=0.006)
    # The full-width back makes a supportive structure behind the loose pads.
    upholstered("PaddedBackFrame", (3.24, 0.34, 0.75), (0, 0.478, 0.795), fabric_dark, rotation=(-0.075, 0, 0), seed=5)

    for index, x in enumerate((-0.916, 0.0, 0.916)):
        angle = (0.006, -0.007, 0.009)[index]
        seat_z = (0.525, 0.518, 0.526)[index]
        seat_location = (x, -0.091 + (index - 1) * 0.006, seat_z)
        seat_rotation = (0.01, angle, (index - 1) * 0.004)
        upholstered("SeatCushion.%02d" % (index + 1), (0.916, 0.922, 0.226), seat_location,
                    rotation=seat_rotation, depression=(0.026, 0.037, 0.023)[index], seed=11 + index)
        outline("SeatPiping.%02d" % (index + 1), 0.456, 0.457, seat_location,
                rotation=seat_rotation, depth=0.012, radius=0.005)
        # Parallel small stitches visible in close seated and oblique renders.
        for stitch in range(30):
            sx = -0.397 + stitch * 0.027
            sy = -0.457 * max(0, 1 - (abs(sx) / 0.456) ** (2 / 0.48)) ** (0.48 / 2)
            ex = sx + 0.010
            ey = -0.457 * max(0, 1 - (abs(ex) / 0.456) ** (2 / 0.48)) ** (0.48 / 2)
            curve("SeatStitch.%02d.%02d" % (index + 1, stitch),
                  [(sx, sy, 0.022), (ex, ey, 0.022)],
                  piping, 0.0009, False, seat_location, seat_rotation)
        back_location = (x, 0.299 + (index - 1) * 0.003, 0.837)
        back_rotation = (-0.145, (index - 1) * 0.014, (index - 1) * -0.008)
        upholstered("BackCushion.%02d" % (index + 1), (0.932, 0.27, 0.588), back_location,
                    rotation=back_rotation, seed=23 + index, fullness=0.57)
        front_welt("BackPiping.%02d" % (index + 1), (0.932, 0.27, 0.588), back_location,
                   rotation=back_rotation, fullness=0.57)

    for side, label in ((-1, "Left"), (1, "Right")):
        arm_location = (side * 1.548, -0.001, 0.647)
        upholstered("Arm." + label, (0.427, 1.207, 0.588), arm_location,
                    seed=35 + side, fullness=0.56)
        # Soft oval seam follows the leading padded arm end.
        front_welt("ArmEndPiping." + label, (0.427, 1.207, 0.588), arm_location,
                   fullness=0.56, radius=0.0046)

    def throw_pillow(name, location, rotation, material, seed):
        obj = upholstered(name, (0.496, 0.235, 0.493), location, material,
                          rotation=rotation, seed=seed, fullness=0.64)
        front_welt(name + ".EdgeWelt", (0.496, 0.235, 0.493), location,
                   rotation=rotation, material=material, fullness=0.64, radius=0.006)
        # A short diagonal crease is embedded in the front, not a hard button.
        curve(name + ".HandSewnTack", [(-0.013, -0.118, -0.009), (0.013, -0.118, 0.009)],
              piping, 0.0017, False, location, rotation)
        return obj

    throw_pillow("ThrowPillow.BurntOrange", (1.025, 0.050, 0.791),
                 (-0.19, -0.14, -0.30), materials["orange"], 58)
    throw_pillow("ThrowPillow.Ochre", (-0.96, 0.078, 0.789),
                 (-0.12, 0.12, 0.23), materials["mustard"], 63)

    # Tailored woven blanket follows the arm's actual cross-section. Narrow
    # waves form folds over the crown, becoming looser toward the hanging hem.
    drape_profile = [(-1.945, 0.175), (-1.920, 0.378), (-1.855, 0.640),
                     (-1.789, 0.795), (-1.733, 0.871), (-1.644, 0.932),
                     (-1.526, 0.936), (-1.415, 0.886), (-1.351, 0.792),
                     (-1.289, 0.647), (-1.228, 0.615)]

    def catmull_profile(t):
        position = t * (len(drape_profile) - 1)
        index = min(int(position), len(drape_profile) - 2)
        u = position - index
        p0 = Vector(drape_profile[max(0, index - 1)])
        p1 = Vector(drape_profile[index])
        p2 = Vector(drape_profile[index + 1])
        p3 = Vector(drape_profile[min(len(drape_profile) - 1, index + 2)])
        return 0.5 * ((2 * p1) + (-p0 + p2) * u + (2*p0 - 5*p1 + 4*p2 - p3) * u*u + (-p0 + 3*p1 - 3*p2 + p3) * u*u*u)

    def upper_upholstery_surface(x, y, center, dimensions, fullness):
        """Exact upper envelope of the uncompressed superellipsoid above.

        Cushion compression only lowers this bound. A small clearance also
        covers modeled rumples, seat rotation, subdivision, and cloth thickness.
        """
        cx, cy, cz = center
        hx, hy, hz = (dimension * 0.5 for dimension in dimensions)
        power = 2.0 / 0.48
        radial = (abs((x - cx) / hx) ** power + abs((y - cy) / hy) ** power) ** (1.0 / power)
        if radial >= 1.0:
            return None
        return cz + hz * max(0.0, 1.0 - radial ** (2.0 / fullness)) ** (0.65 / 2.0)

    columns, rows = 86, 40
    vertices = []
    for column in range(columns + 1):
        u = column / columns
        px, pz = catmull_profile(u)
        for row in range(rows + 1):
            v = row / rows
            # Fold the inside corner forward onto the open seat. The blanket
            # deliberately stops before the ochre pillow and loose back pad.
            tuck = max(0.0, min(1.0, (u - 0.64) / 0.36))
            tuck = tuck * tuck * (3.0 - 2.0 * tuck)
            y = -0.492 + v * (0.680 - 0.360 * tuck)
            fold = 0.015 * math.sin(v * TAU * 5.0 + u * 3.2)
            fold += 0.006 * math.sin(v * TAU * 10.0 - u * 5.0)
            hem_wave = 0.007 * math.sin(u * 17.0 + v * 8.0)
            x = px + (0.014 * math.sin(v * 30.0) * (1.0 - u) ** 2)
            z = pz + fold * (0.7 + 0.3 * math.sin(math.pi * u)) + hem_wave
            y += 0.018 * math.sin(u * 11.0 + v * 4.0) * (abs(v - 0.5) * 2) ** 3
            # Expand the arm envelope 6 mm as well as lifting the skin. This
            # keeps subdivided triangles clear at the steep outer shoulder.
            arm_top = upper_upholstery_surface(x, y, (-1.548, -0.001, 0.647), (0.439, 1.219, 0.600), 0.56)
            seat_top = upper_upholstery_surface(x, y, (-0.916, -0.097, 0.525), (0.916, 0.922, 0.226), 0.48)
            support = max((surface for surface in (arm_top, seat_top) if surface is not None), default=None)
            if support is not None:
                clearance = 0.015 + max(0.0, fold + hem_wave) * 0.45
                minimum = support + clearance
                # Smooth max prevents a sharp crease where free fabric meets
                # the cushion, while always remaining above both surfaces.
                z = 0.5 * (z + minimum + math.sqrt((z - minimum) ** 2 + 0.010 ** 2))
            vertices.append((x, y, z))
    faces = []
    for column in range(columns):
        for row in range(rows):
            a = column * (rows + 1) + row
            faces.append((a, a + rows + 1, a + rows + 2, a + 1))
    blanket = mesh_object("ThrowBlanket.WovenDrape", vertices, faces, materials["blanket"], subdivision=1)
    blanket.data.materials.append(cream)
    for polygon in blanket.data.polygons:
        row = polygon.index % rows
        polygon.material_index = 1 if row in (3, 4, 7, 32, 35, 36) else 0
    thickness = blanket.modifiers.new("Woven textile thickness", "SOLIDIFY")
    thickness.thickness = 0.0045
    thickness.offset = 0
    for row, name in ((0, "Front"), (rows, "Back")):
        edge = [vertices[column * (rows + 1) + row] for column in range(columns + 1)]
        curve("ThrowBlanket." + name + "BoundHem", edge, cream, 0.0031)
    curve("ThrowBlanket.HangingHem", vertices[:rows + 1], cream, 0.0031)
    for fringe in range(27):
        row = round(fringe * rows / 26)
        x, y, z = vertices[row]
        curve("ThrowBlanket.Fringe.%02d" % fringe,
              [(x, y, z), (x - 0.005, y + 0.003, z - 0.020),
               (x + 0.004 * math.sin(fringe), y + 0.005, z - 0.041)],
              cream, 0.0017)

    # Small woven maker's label; restrained enough to reward a close look.
    text = bpy.data.curves.new("SC_Couch.MakersLabel.Text", "FONT")
    text.body = "1984 / DRH"
    text.size = 0.018
    text.align_x = "CENTER"
    text.align_y = "CENTER"
    text.extrude = 0.00035
    text.materials.append(cream)
    label = link_object("MakersLabel", text, (1.252, -0.592, 0.336), (math.pi * 0.5, 0, 0))
    label["detail"] = "A small embroidered autobiographical maker's mark"

    seated_location = (0.0, 0.94, 1.18)
    standing_location = (0.0, -0.20, 0.08)
    spawn_objects = {}
    for name, location in (("Spawn_Daniel_Seated", seated_location), ("Spawn_Daniel_Stand", standing_location)):
        empty = bpy.data.objects.new(name, None)
        collection.objects.link(empty)
        empty.location = location
        empty.empty_display_type = "ARROWS"
        empty.empty_display_size = 0.18
        empty.hide_render = True
        empty["purpose"] = "First-person seated eye" if name.endswith("Seated") else "First-person standing feet"
        spawn_objects[name] = empty

    return {
        "couch_root": root,
        "seat_spawn": seated_location,
        "stand_spawn": standing_location,
        "seat_spawn_object": spawn_objects["Spawn_Daniel_Seated"],
        "stand_spawn_object": spawn_objects["Spawn_Daniel_Stand"],
        "objects": objects,
    }
