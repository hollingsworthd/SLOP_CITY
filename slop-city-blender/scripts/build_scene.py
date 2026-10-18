"""Build SLOP CITY's editable Blender couch spawn, render it, and export game assets."""
import bpy, math, random, sys, json, os
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'scripts'))
from couch import create_couch
from vr_headset import create_headset
random.seed(1984)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
for c in list(bpy.data.collections):
    if c.name!='Collection': bpy.data.collections.remove(c)
scene=bpy.context.scene
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
scene.render.engine='CYCLES';scene.cycles.samples=64;scene.cycles.use_denoising=True
scene.cycles.max_bounces=8;scene.cycles.diffuse_bounces=4;scene.cycles.transmission_bounces=4
scene.render.resolution_x=1600;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast';scene.view_settings.exposure=.7
world=bpy.data.worlds.new('Night over Static Heights');scene.world=world;world.use_nodes=True
world.node_tree.nodes['Background'].inputs['Color'].default_value=(.055,.085,.13,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=.35

def collection(name):
    c=bpy.data.collections.new(name);scene.collection.children.link(c);return c
ROOM=collection('01 • THE LOFT');COUCH=collection('02 • DANIEL’S COUCH');PROPS=collection('03 • PERSONAL EFFECTS');CITY=collection('04 • STATIC HEIGHTS');LIGHTS=collection('05 • LIGHT & CAMERAS');MARKERS=collection('06 • UNITY SPAWN MARKERS');CUTAWAY=collection('07 • INTERIOR WALLS — hide for cutaway');HEADSET=collection('08 • DANIEL’S VR HEADSET')
ACTIVE=ROOM

def move(obj,col=None):
    for c in list(obj.users_collection):c.objects.unlink(obj)
    (col or ACTIVE).objects.link(obj);return obj

def colhex(h):
    vals=[int(h[i:i+2],16)/255 for i in (1,3,5)]
    return tuple(((v+.055)/1.055)**2.4 if v>.04045 else v/12.92 for v in vals)+(1,)

def material(name,h,rough=.6,metal=0,emission=0):
    m=bpy.data.materials.new(name);m.diffuse_color=colhex(h);m.use_nodes=True;n=m.node_tree.nodes.get('Principled BSDF');n.inputs['Base Color'].default_value=colhex(h);n.inputs['Roughness'].default_value=rough;n.inputs['Metallic'].default_value=metal
    if emission:n.inputs['Emission Color'].default_value=colhex(h);n.inputs['Emission Strength'].default_value=emission
    return m

def texture(m,scale=100,strength=.15,dist=.015):
    nt=m.node_tree;noise=nt.nodes.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=scale;noise.inputs['Detail'].default_value=3
    bump=nt.nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=strength;bump.inputs['Distance'].default_value=dist;nt.links.new(noise.outputs['Fac'],bump.inputs['Height']);nt.links.new(bump.outputs['Normal'],nt.nodes.get('Principled BSDF').inputs['Normal'])
    return m
M={
'fabric':texture(material('Upholstery | midnight teal','#346766',.92),155,.22,.008),
'fabric_dark':texture(material('Upholstery | shadow teal','#204B4B',.96),145,.25,.008),
'piping':material('Couch | sage piping','#78A59B',.82),
'orange':texture(material('Fabric | burnt tangerine','#C4673E',.95)),
'mustard':texture(material('Fabric | warm ochre','#DEB75B',.95)),
'wood':texture(material('Wood | walnut','#594135',.4),6,.18,.009),
'blanket':texture(material('Throw | oatmeal boucle','#D8CEB7',.98),185,.3,.01),
'cream':material('Ceramic | bone','#EDE4CD',.33),
'wall':texture(material('Plaster | blue green','#244A4D',.94),5,.2,.04),
'black':material('Equipment | charcoal','#1A2326',.42),
'brass':material('Metal | worn brass','#B3A37A',.28,.65),
'metal':material('Metal | gunmetal','#4B5D61',.32,.75),
'lime':material('Signal | phosphor','#DCF99C',.45,0,2.7),
'orange_glow':material('Sign | warm orange','#FFAC66',.4,0,3),
'cyan':material('Sign | cool cyan','#79B9B3',.5,0,1.4),
'rug':texture(material('Rug | undyed wool','#CABD9C',.99),155,.35,.008),
'rug_border':texture(material('Rug | indigo edge','#425F64',.95)),
'paper':material('Paper | aged ivory','#DED6BE',.85),
'vinyl':material('Vinyl | black lacquer','#091319',.2),
'window':material('City | amber windows','#D6AF69',.5,0,1.8),
'window_cyan':material('City | teal windows','#609C9A',.5,0,1.25),
'screen':material('Screen | dark phosphor','#142E2C',.36,0,.1),
}

def box(name,loc,dim,mat,bevel=0.02,col=None):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=move(bpy.context.object,col);o.name=name;o.dimensions=dim;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if mat:o.data.materials.append(mat)
    if bevel:
        mod=o.modifiers.new('Soft manufactured edges','BEVEL');mod.width=bevel;mod.segments=3
        mod=o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
    return o

def cylinder(name,loc,radius,depth,mat,vertices=40,col=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=depth,location=loc);o=move(bpy.context.object,col);o.name=name;o.data.materials.append(mat)
    for p in o.data.polygons:p.use_smooth=True
    mod=o.modifiers.new('Edge rounding','BEVEL');mod.width=min(.007,depth*.18);mod.segments=2;o.modifiers.new('Weighted normals','WEIGHTED_NORMAL');return o

def uvball(name,loc,scale,mat,col=None):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,location=loc);o=move(bpy.context.object,col);o.name=name;o.scale=scale;o.data.materials.append(mat)
    for p in o.data.polygons:p.use_smooth=True
    return o

def curve(name,pts,radius,mat,col=None):
    data=bpy.data.curves.new(name,'CURVE');data.dimensions='3D';data.bevel_depth=radius;data.bevel_resolution=3;sp=data.splines.new('POLY');sp.points.add(len(pts)-1)
    for p,v in zip(sp.points,pts):p.co=(*v,1)
    ob=bpy.data.objects.new(name,data);(col or ACTIVE).objects.link(ob);data.materials.append(mat);return ob

fontpath='/System/Library/Fonts/Supplemental/Arial Bold.ttf'
font=bpy.data.fonts.load(fontpath) if Path(fontpath).exists() else None

def text(name,body,loc,size,mat,rotation=(math.pi/2,0,0),align='LEFT',col=None):
    data=bpy.data.curves.new(name,'FONT');data.body=body;data.size=size;data.align_x=align;data.extrude=.0015;data.bevel_depth=.0006
    if font:data.font=font
    ob=bpy.data.objects.new(name,data);(col or ACTIVE).objects.link(ob);ob.location=loc;ob.rotation_euler=rotation;data.materials.append(mat);return ob

def empty(name,loc):
    ob=bpy.data.objects.get(name) or bpy.data.objects.new(name,None)
    for c in list(ob.users_collection):c.objects.unlink(ob)
    MARKERS.objects.link(ob);ob.location=loc;ob.empty_display_type='ARROWS';ob.empty_display_size=.18;return ob

def area(name,loc,color,power,size,target):
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.color=color;d.shape='DISK';d.size=size;o=bpy.data.objects.new(name,d);LIGHTS.objects.link(o);o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();return o

def point(name,loc,color,power,size=.15):
    d=bpy.data.lights.new(name,'POINT');d.energy=power;d.color=color;d.shadow_soft_size=size;o=bpy.data.objects.new(name,d);LIGHTS.objects.link(o);o.location=loc;return o

def camera(name,loc,target,lens=48,ortho=None):
    d=bpy.data.cameras.new(name);o=bpy.data.objects.new(name,d);LIGHTS.objects.link(o);o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();d.lens=lens
    if ortho:d.type='ORTHO';d.ortho_scale=ortho
    return o

# Loft floor, an open front/right cutaway, and a glazed city-facing rear wall.
box('Floor_Main',(0,-.25,-.16),(8.6,6.5,.26),M['wood'],.06)
for i in range(25):
    x=-4.12+i*.342
    for j in range(5):
        y=-3.1+j*1.3
        mm=material('Floor board %02d'%((i+j)%9),['#695548','#6F5B4C','#604C3E','#79604C','#665044','#856850','#705342','#624C40','#735A45'][(i+j)%9],.58)
        box('Floorboard_%02d_%02d'%(i,j),(x,y,-.007),(.333,1.288,.044),mm,.006)
box('COL_Wall_Left',(-4.25,-.25,1.56),(.16,6.5,3.2),M['wall'],.025)
box('COL_Window_Sill',(0,2.93,.42),(8.6,.16,.9),M['wall'],.025)
box('COL_Window_Header',(0,2.93,3.18),(8.6,.18,.34),M['wall'],.03)
for x in [-4.22,-1.42,1.42,4.22]:box('Window_Frame_Post',(x,2.88,2),(.065,.15,2.55),M['black'],.005)
for z in [.93,2.14,3.05]:box('Window_Frame_Rail',(0,2.86,z),(8.5,.16,.055),M['black'],.008)
box('Window_Deep_Sill',(0,2.76,.925),(8.5,.43,.075),M['wood'],.017)
box('Skirting_Left',(-4.14,-.25,.07),(.045,6.5,.16),M['wood'],.004)
# Window glass purposely omitted: the modular opening is a clear portal to exterior art.
for x in [-4.28,4.28]:box('COL_Cutaway_Side_Edge',(x,-.25,.02),(.06,6.5,.08),M['black'],.01)
# Woven rug, bordered bands and individual fringe.
box('Wool_Rug',(0,-.1,.038),(4.75,3.9,.055),M['rug'],.11)
for x in [-2.16,2.16]:box('Rug_Indigo_Border',(x,-.1,.07),(.085,3.5,.008),M['rug_border'],.004)
for y in [-1.78,1.58]:box('Rug_End_Border',(0,y,.072),(4.4,.072,.008),M['rug_border'],.004)
for i in range(69):
    x=-2.26+i*.067
    for y,sign in [(-2.08,-1),(1.88,1)]:curve('Rug_Fringe',[(x,y,.041),(x+.012,y+sign*.11,.038),(x-.008,y+sign*.17,.034)],.007,M['blanket'])
# Hand-built upholstered hero asset.
couch_result=create_couch(M,COUCH)
# Spawn markers stay human-readable in Blender and survive FBX/GLB export.
for obj in list(MARKERS.objects):bpy.data.objects.remove(obj,do_unlink=True)
for name,loc in [('Spawn_Daniel_Seated',(0,.94,1.18)),('Spawn_Daniel_Stand',(0,-.2,.08)),('LookAt_Daniel_Seated',(2.3,-2.45,1.18)),('Interact_TV',(2.3,-2.6,1.1)),('Interact_Radio',(-2.55,.0,.85)),('Light_TableLamp',(-2.55,.7,1.68))]:empty(name,loc)
# Floating plaque / warm sign above the couch.
ACTIVE=PROPS
box('Couch_Nameplate',(-.25,2.695,1.88),(2.28,.035,.48),M['black'],.015)
text('1984 DRH name','1984 DRH',(-.25,2.672,1.90),.29,M['orange_glow'],align='CENTER')
text('Alias subtitle','DANIEL  /  FISHTICKLESWORTH  /  BEEFCAKE',(-.25,2.67,1.755),.058,M['paper'],align='CENTER')
# Coffee table with rounded wooden top and angled legs.
box('Coffee_Table_Top',(0,-1.14,.46),(1.65,.83,.12),M['wood'],.15)
for x in [-.62,.62]:
    for y in [-1.42,-.87]:
        ob=box('Coffee_Table_Leg',(x,y,.23),(.075,.075,.43),M['brass'],.015);ob.rotation_euler[1]=(-.1 if x<0 else .1)
# Mug with a real open rim, hollow dark coffee surface, and handle.
mx,my=.42,-1.02
cylinder('Mug_Ceramic',(mx,my,.63),.075,.18,M['cream'])
cylinder('Coffee_Surface',(mx,my,.727),.062,.006,M['wood'])
bpy.ops.mesh.primitive_torus_add(major_radius=.067,minor_radius=.009,major_segments=36,minor_segments=10,location=(mx,my,.727));o=move(bpy.context.object);o.name='Mug_Rim';o.data.materials.append(M['cream'])
bpy.ops.mesh.primitive_torus_add(major_radius=.048,minor_radius=.012,major_segments=24,minor_segments=10,location=(mx+.077,my,.64),rotation=(math.pi/2,0,0));o=move(bpy.context.object);o.name='Mug_Handle';o.data.materials.append(M['cream'])
# Laptop with clearly authored little Roblox-baseplate screen.
box('Laptop_Base',(-.39,-1.13,.535),(.57,.39,.025),M['metal'],.015)
for row in range(4):
    for k in range(11):box('Laptop_Key',(-.63+k*.045,-1.24+row*.048,.551),(.034,.034,.006),M['black'],.003)
box('Laptop_Trackpad',(-.39,-1.29,.552),(.13,.057,.004),M['black'],.006)
box('Laptop_Lid',(-.39,-.95,.74),(.57,.032,.37),M['black'],.02)
box('Laptop_Screen',(-.39,-.969,.742),(.526,.006,.315),M['screen'],.006)
text('Laptop_Title','VIBE LAB',(-.63,-.976,.845),.047,M['lime'])
text('Laptop_Code','baseplate.Anchored = true\n-- HUMAN INPUT REQUIRED\nprint("hello, 2026")',(-.63,-.977,.792),.028,M['cyan'])
# Face the laptop toward its owner on the couch.
pivot=Vector((-.39,-1.13,.535));turn=Matrix.Rotation(math.pi,4,'Z')
for ob in PROPS.objects:
    if ob.name.startswith('Laptop_'):
        ob.location=pivot+turn.to_3x3()@(ob.location-pivot)
        ob.rotation_euler=(turn.to_3x3()@ob.rotation_euler.to_matrix()).to_euler()
# Sauce packets: colorful foil-like shapes, collectible motif.
for i,(x,y,mm,title) in enumerate([(.07,-1.40,M['mustard'],'MILD'),(.24,-1.43,M['orange'],'HOT'),(.42,-1.40,M['orange'],'FIRE')]):
    packet=box('Sauce_Packet_'+title,(x,y,.554),(.125,.19,.023),mm,.02);packet.rotation_euler[2]=(i-1)*.18
    text('Sauce_'+title,title,(x,y,.57),.027,M['cream'],rotation=(0,0,(i-1)*.18),align='CENTER')
# Blue Book folder underneath a set of car keys.
folder=box('Blue_Book_Folder',(.45,-1.4,.532),(.57,.30,.014),M['paper'],.004);folder.rotation_euler[2]=-.12
text('Folder_Label','PROJECT BLUE BOOK',(.48,-1.44,.542),.027,M['black'],rotation=(0,0,-.12),align='CENTER')
text('Folder_2026','2026 / FICTIONAL FIELD NOTES',(.5,-1.5,.543),.016,M['black'],rotation=(0,0,-.12),align='CENTER')
box('Car_Key_Fob',(.62,-1.04,.552),(.058,.095,.025),M['black'],.022)
box('Car_Key_Blade',(.64,-1.12,.545),(.018,.085,.008),M['metal'],.003)
# Side table and warm shade lamp.
box('Side_Table',(-2.58,.72,.47),(.75,.67,.12),M['wood'],.045)
for x in [-2.86,-2.30]:
    for y in [.49,.95]:box('Side_Table_Leg',(x,y,.23),(.045,.045,.43),M['black'],.008)
cylinder('Lamp_Base',(-2.55,.75,.55),.16,.045,M['brass'])
cylinder('Lamp_Stem',(-2.55,.75,1.05),.017,1.0,M['brass'])
shade=material('Lamp shade | linen','#EFCE93',.82,0,.28);texture(shade,90,.2,.006)
bpy.ops.mesh.primitive_cone_add(vertices=64,radius1=.35,radius2=.21,depth=.45,location=(-2.55,.75,1.58));ob=move(bpy.context.object);ob.name='Linen_Lamp_Shade';ob.data.materials.append(shade)
cylinder('Lamp_Shade_Bottom',(-2.55,.75,1.36),.335,.008,M['cream'])
point('Warmth at the couch',(-2.55,.68,1.34),(1,.63,.31),55,.19)
# Record cabinet, turntable and spines.
box('Record_Cabinet',(-3.47,.1,.52),(.83,1.54,1.04),M['wood'],.04)
box('Record_Cabinet_Inset',(-3.045,.1,.59),(.015,1.30,.58),M['black'],.015)
for i in range(18):
    mm=[M['orange'],M['paper'],M['fabric_dark'],M['mustard'],M['metal']][i%5]
    box('Record_Spine_%02d'%i,(-3.025,-.48+i*.068,.59),(.026,.049,.51),mm,.002)
box('Turntable',(-3.44,.14,1.09),(.73,.61,.09),M['black'],.022)
cylinder('Turntable_Vinyl',(-3.47,.13,1.148),.235,.012,M['vinyl'])
for radius in [.10,.135,.175,.207]:
    bpy.ops.mesh.primitive_torus_add(major_radius=radius,minor_radius=.0015,major_segments=64,minor_segments=6,location=(-3.47,.13,1.156));o=move(bpy.context.object);o.name='Record_Groove';o.data.materials.append(M['metal'])
cylinder('Turntable_Label',(-3.47,.13,1.159),.063,.004,M['orange'])
curve('Turntable_Tonearm',[(-3.14,.35,1.17),(-3.16,.28,1.2),(-3.36,.05,1.18)],.008,M['brass'])
# Pocket radio at the front of the sideboard.
box('HoodRich_Radio',(-2.56,.06,.80),(.57,.22,.37),M['black'],.05)
for i in range(10):box('Radio_Speaker_Grille',(-2.78+i*.027,-.056,.80),(.009,.006,.23),M['brass'],.003)
box('Radio_Frequency_Display',(-2.44,-.058,.86),(.19,.009,.08),M['screen'],.007)
text('Radio_Frequency','98.4 HRR',(-2.52,-.065,.85),.029,M['lime'])
knob=cylinder('Radio_Tuner',(-2.44,-.075,.73),.037,.027,M['brass']);knob.rotation_euler[0]=math.pi/2
curve('Radio_Antenna',[(-2.36,.06,1.0),(-2.29,.11,1.48)],.006,M['metal'])
# Place the radio firmly on the front of the lamp table.
for o in PROPS.objects:
    if o.name.startswith(('HoodRich_Radio','Radio_')):
        o.location.y += .48; o.location.z -= .09
bpy.data.objects['Interact_Radio'].location = (-2.55,.48,.75)
# Album sleeve and poster as editable 3D typography.
box('Record_Sleeve_HRR',(-3.86,.56,1.55),(.07,.54,.65),M['orange'],.004)
text('Album_HRR','HRR',(-3.817,.33,1.69),.22,M['black'],rotation=(math.pi/2,0,math.pi/2))
# Left-wall poster faces +X.
box('HRR_Poster_Frame',(-4.135,.3,2.29),(.035,1.15,1.10),M['black'],.012)
box('HRR_Poster',(-4.112,.3,2.29),(.012,1.07,1.02),M['orange'],.0)
text('Poster_Title','HOOD\nRICH',(-4.100,-.17,2.47),.27,M['black'],rotation=(math.pi/2,0,math.pi/2))
text('Poster_Subtitle','ALL SLOP. ALL THE TIME.',(-4.097,-.17,1.97),.043,M['black'],rotation=(math.pi/2,0,math.pi/2))
# TV console in front of the couch, low enough for clear seated view.
box('TV_Console',(2.58,-2.51,.36),(1.62,.57,.65),M['wood'],.035)
for x in [2.03,3.13]:box('TV_Console_Door',(x,-2.805,.41),(.50,.028,.42),M['fabric_dark'],.015)
for x in [1.99,3.18]:box('TV_Foot',(x,-2.46,.79),(.08,.35,.08),M['black'],.018)
# Screen faces toward couch (+Y), text rotated to match.
box('Broadcast_TV_Frame',(2.58,-2.53,1.18),(1.6,.10,.88),M['black'],.045)
box('Broadcast_TV_Screen',(2.58,-2.47,1.18),(1.48,.015,.76),M['screen'],.025)
text('TV_Broadcaster','MACHINE\nPUBLISHERS',(3.19,-2.455,1.36),.13,M['lime'],rotation=(math.pi/2,0,math.pi))
text('TV_Tagline','FROM THEIR MOUTH. TO YOURS.',(3.19,-2.451,1.03),.037,M['lime'],rotation=(math.pi/2,0,math.pi))
text('TV_Transmission','CITY TRANSMISSION / 2026',(3.19,-2.451,.93),.033,M['cyan'],rotation=(math.pi/2,0,math.pi))
point('Phosphor TV spill',(2.52,-2.1,1.33),(.57,1,.52),18,.35)
# Books and headphones beside the couch.
for i in range(3):box('Coffee_Table_Book',(-.47,-1.15,.12+i*.033),(.54,.33,.03),[M['orange'],M['paper'],M['fabric_dark']][i],.004)
pts=[(-1.63+.17*math.cos(t),.74,.96+.17*math.sin(t)) for t in [i*math.pi/20 for i in range(21)]]
curve('Headphones_Headband',pts,.018,M['black'])
for x in [-1.80,-1.46]:uvball('Headphone_Earcup',(x,.74,.94),(.043,.075,.095),M['black'])
# Large plant, hand-shaped leaves and terracotta vessel.
plantpot=material('Ceramic | terracotta','#B15E43',.88);leafmat=material('Leaves | bottle green','#356B53',.74)
cylinder('Plant_Pot',(3.50,1.60,.36),.30,.63,plantpot)
cylinder('Plant_Soil',(3.50,1.60,.68),.27,.02,M['wood'])
for i in range(11):
    angle=i*2.399;radius=.30+random.random()*.28;top=1.13+random.random()*.85
    tip=(3.5+math.cos(angle)*radius,1.6+math.sin(angle)*radius,top)
    curve('Plant_Stem',[(3.5,1.6,.69),(3.5+math.cos(angle)*.16,1.6+math.sin(angle)*.16,top-.25),tip],.013,leafmat)
    leaf=uvball('Plant_Leaf',tip,(.12,.035,.32),leafmat);leaf.rotation_euler=(.2+random.random()*.7,angle*.15,angle)
# Wall outlet and coiled cables keep it lived in.
box('Outlet_Plate',(-4.147,-1.70,.32),(.035,.15,.20),M['cream'],.017)
curve('Lamp_Power_Cable',[(-2.57,.75,.10),(-2.8,.90,.055),(-3.8,.92,.045),(-4.08,.8,.09)],.01,M['black'])
# Complete playable interior. Hide these two walls only for exterior cutaway renders.
ACTIVE=CUTAWAY
box('COL_Wall_Front',(0,-3.45,1.56),(8.6,.16,3.2),M['wall'],.025)
box('COL_Wall_Right',(4.25,-.25,1.56),(.16,6.5,3.2),M['wall'],.025)
box('COL_Ceiling',(0,-.25,3.25),(8.6,6.5,.14),M['cream'],.025)
move(area('Interior warm ceiling bounce',(0,-1.2,2.92),(1,.80,.58),300,3.4,(0,-1.2,0)),CUTAWAY)
box('Skirting_Front',(0,-3.35,.07),(8.5,.045,.16),M['wood'],.004)
box('Skirting_Right',(4.14,-.25,.07),(.045,6.5,.16),M['wood'],.004)
# A record sleeve gallery across the TV wall, facing into the room.
for i,(title,subtitle,mm) in enumerate([('1984 DRH','IN-Q-TEL',M['orange']),('2026','HUMAN INPUT REQUIRED',M['fabric_dark']),('HRR','ALL SLOP. ALL THE TIME.',M['mustard'])]):
    x=-2.6+i*1.35
    box('Wall_Record_Frame',(x,-3.348,2.12),(1.02,.035,1.10),M['black'],.014)
    box('Wall_Record_Sleeve',(x,-3.324,2.12),(.94,.012,1.02),mm,.003)
    text('Wall_Record_Title',title,(x,-3.312,2.19),.15,M['cream'],rotation=(math.pi/2,0,math.pi),align='CENTER')
    text('Wall_Record_Subtitle',subtitle,(x,-3.308,1.83),.035,M['cream'],rotation=(math.pi/2,0,math.pi),align='CENTER')
# Exterior towers: coherent silhouettes, lit windows and large broadcaster sign.
ACTIVE=CITY
city_mats=[material('Tower | %d'%i,c,.9) for i,c in enumerate(['#182E33','#263C3F','#213C43','#354747','#1A303A'])]
box('City_Platform',(0,9,-.8),(35,18,1.3),M['black'],.05)
for idx,(x,y,w,d,h) in enumerate([(-9,7,3.0,2.1,6.3),(-5.7,8.2,2.5,2.8,8.5),(-2.9,6.5,2.2,2.4,5.8),(.2,8.1,2.6,2.4,8.2),(3.3,7.2,2.3,2.5,5.8),(6.2,9.2,3.3,3.2,10),(9.2,6.8,2.3,2.6,6.7),(-11.8,11,2.8,3.2,11)]):
    box('City_Tower_%02d'%idx,(x,y,h/2-.4),(w,d,h),city_mats[idx%5],.045)
    box('City_Tower_Coping',(x,y,h-.35),(w+.08,d+.08,.11),M['metal'],.02)
    for row in range(int(h/.60)-1):
        for col in range(int(w/.44)-1):
            if random.random()<.45:continue
            mm=M['window'] if random.random()<.62 else M['window_cyan']
            box('City_Lit_Window',(x-w/2+.37+col*.44,y-d/2-.012,.1+row*.60),(.19,.018,.30),mm,0)
    for roof in range(2):box('Roof_Unit',(x+(roof-.5)*.8,y,h-.13),(.55,.6,.35),M['metal'],.02)
# Freestanding rooftop transmission mast.
for z in range(7):
    box('Transmission_Mast',(2.8,7.8,5.7+z*.27),(.042,.042,.27),M['metal'],.001)
curve('Antenna_Beam',[(2.45,7.8,7.0),(3.15,7.8,7.0)],.019,M['metal'])
uvball('Antenna_Beacon',(2.8,7.8,7.67),(.06,.06,.06),M['orange_glow'])
# Floating exterior sign visible from the loft.
box('City_Billboard',(3.25,5.72,3.65),(2.85,.20,1.28),M['black'],.035)
text('City_Billboard_Machine','MACHINE\nPUBLISHERS',(2.03,5.607,3.97),.28,M['lime'])
text('City_Billboard_Source','YOUR LIFE IS THE CONTENT',(2.03,5.604,3.09),.075,M['lime'])
# Neighbor's warm HRR sign creates continuity with the earlier browser world.
text('City_HRR_Neon','HRR',(-4.2,5.19,3.25),.50,M['orange_glow'])
# Lighting and cameras. Editorial diorama shot, couch portrait, and literal seated spawn.
ACTIVE=LIGHTS
area('Warm large softbox',(1,-3.5,6),(1,.80,.60),850,5,(0,.6,.7))
area('Moonlight through glass',(0,5,5),(.35,.64,1),950,5,(0,.6,1))
area('Amber left bounce',(-4,-.5,3),(1,.44,.19),230,2,(0,1,.8))
area('Window cool fill',(4,1.8,4),(.37,.82,.87),300,3,(0,1,.9))
hero=camera('CAM_01_CouchSpawn_Hero',(8.2,-11.5,8.1),(-.20,.7,1.08),ortho=11.5)
close=camera('CAM_02_Comfort',(5.2,-6.5,4.4),(-.15,.85,.80),lens=52)
seated=camera('CAM_03_Daniel_Seated',(0,.94,1.18),(2.3,-2.45,1.18),lens=22)
seated.data.clip_start=.01
headset_result=create_headset(M,HEADSET,(0,.94,1.18),(2.3,-2.45,1.18))
seated.parent=headset_result['root'];seated.location=(0,0,0);seated.rotation_euler=(0,0,0)
scene.camera=seated
HEADSET.hide_render=True;HEADSET.hide_viewport=True
CUTAWAY.hide_render=False
CUTAWAY.hide_viewport=False
# A clean object hierarchy and embedded readme make the native file self-contained.
readme=bpy.data.texts.new('START HERE — SLOP CITY')
readme.write('SLOP CITY / COUCH SPAWN\n\n1984 DRH = Fishticklesworth = Beefcake = Daniel.\nThe comfortable couch is the canonical player spawn.\nScene units: meters, Z-up.\n\nCAM_03 (default): Daniel seated, clear first-person view. Headset equipment is hidden from the local camera.\nCAM_01: room cutaway\nCAM_02: couch detail\nHide Collections 07 and 08 for exterior cutaway cameras.\nThe VR headset is a modeled visual rig, not a stereo XR runtime.\n\nCollections separate room, upholstery, props, skyline, light rigs and Unity markers.\nAll labels are editable Blender text. Fabric uses procedural bump; no missing linked assets.\n\nUnity marker names: Spawn_Daniel_Seated (eye), Spawn_Daniel_Stand (feet), LookAt_Daniel_Seated, Interact_TV, Interact_Radio.\nUnity-ready FBX lives in Unity/Assets/Art. GLB interchange asset lives in assets.\n\nMusic source original: https://www.tiktok.com/@machine_publishers/video/7573075941463756062\nDuet: https://www.tiktok.com/@beef_talko/video/7689137720760864030\nThis is an authored environment and spawn kit, not a networked Unity MMO.\n')
for screen in bpy.data.screens:
    for a in screen.areas:
        if a.type=='VIEW_3D':
            a.spaces.active.region_3d.view_perspective='CAMERA';a.spaces.active.overlay.show_extras=False;a.spaces.active.shading.type='MATERIAL'
scene['project']='SLOP CITY';scene['spawn_owner']='1984 DRH / Fishticklesworth / Beefcake / Daniel';scene['source_feed']='https://www.tiktok.com/@beef_talko';scene['unity_units']='meters'
# Save native source before evaluating modifiers and converting labels for game export.
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active=seated
bpy.ops.file.pack_all()
blendpath=ROOT/'SlopCity_CouchSpawn.blend';bpy.ops.wm.save_as_mainfile(filepath=str(blendpath))
for cam,out in [(hero,'01-couch-spawn-hero.png'),(close,'02-couch-comfort.png'),(seated,'03-seated-spawn.png')]:
    if '--player-only' in sys.argv and cam != seated: continue
    scene.camera=cam;CUTAWAY.hide_render=(cam!=seated);HEADSET.hide_render=True;scene.render.filepath=str(ROOT/'renders'/out)
    bpy.ops.render.render(write_still=True)
scene.camera=seated
HEADSET.hide_render=False;HEADSET.hide_viewport=False
# Export with editable objects retained in the .blend; texts become portable geometry here.
bpy.ops.object.select_all(action='DESELECT')
CUTAWAY.hide_viewport=False;CUTAWAY.hide_render=False
export_cols=[ROOM,COUCH,PROPS,CITY,MARKERS,CUTAWAY,HEADSET]
for c in export_cols:
    for o in c.objects:
        if o.type in {'MESH','CURVE','FONT','EMPTY'}:o.select_set(True)
for obj in list(bpy.context.selected_objects):
    if obj.type in {'FONT','CURVE'}:
        bpy.context.view_layer.objects.active=obj
        for o in bpy.context.selected_objects:o.select_set(False)
        obj.select_set(True);bpy.ops.object.convert(target='MESH')
bpy.ops.object.select_all(action='DESELECT')
for c in export_cols:
    for o in c.objects:
        if o.type in {'MESH','EMPTY'}:o.select_set(True)
# Custom metadata and markers are exported; rendering-only cameras/lights stay native.
bpy.ops.export_scene.fbx(filepath=str(ROOT/'Unity'/'Assets'/'Art'/'SlopCity_Lounge.fbx'),use_selection=True,object_types={'MESH','EMPTY'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',bake_space_transform=False,use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
bpy.ops.export_scene.gltf(filepath=str(ROOT/'assets'/'SlopCity_Lounge.glb'),export_format='GLB',use_selection=True,export_apply=True,export_extras=True,export_cameras=False,export_lights=False)
metrics={'native':str(blendpath),'meshes':len([o for o in scene.objects if o.type=='MESH']),'markers':{o.name:list(o.location) for o in MARKERS.objects},'renders':[str(ROOT/'renders'/s) for s in ['01-couch-spawn-hero.png','02-couch-comfort.png','03-seated-spawn.png']],'source':'authored procedural geometry; no external models','units':'meters'}
(ROOT/'assets'/'scene-manifest.json').write_text(json.dumps(metrics,indent=2))
print('SLOP_CITY_COMPLETE '+json.dumps(metrics))
