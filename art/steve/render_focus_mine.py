"""Redraw each mining cel from Java 1.8 ModelBiped samples and pinned texels."""
import argparse
import math
import random
from hashlib import sha1,sha256
from functools import lru_cache
import json
from pathlib import Path
from zipfile import ZipFile
from PIL import Image
import numpy as np
from classic_scenes import Frame,input_field
from render_steve import (Renderer,SIZES,PARTS,CENTRES,pose_skin,tr,rx,ry,rz,
                          held_item,cube_texture,texture,FACES,item_rows)

HERE=Path(__file__).resolve().parent
CELS=48
LEAD=6
HIT_FRAMES=(9,15,21)
BREAK=22
DROPS={"stone":"cobblestone","coal_ore":"coal","iron_ore":"iron_ore","gold_ore":"gold_ore",
       "redstone_ore":"redstone_dust","lapis_ore":"dye_powder_blue","diamond_ore":"diamond","emerald_ore":"emerald"}
CUBES={"cobblestone","iron_ore","gold_ore"}
NAMES={ore:ore.replace('_ore','') for ore in DROPS}
PAIRS=tuple([('stone',ore) for ore in DROPS]+[(ore,'stone') for ore in DROPS if ore!='stone'])
def pair_suffix(ore,next_ore):
    return "" if ore==next_ore=="stone" else "-"+NAMES[ore]+"-to-"+NAMES[next_ore]

def work_frame(i,ore):
    f=Frame(float(i)*1.65+2,x=4.4)
    f.item="iron_pickaxe"
    f.pitch=14.+min(i,5)*3.2
    f.yaw=-7.-min(i,5)*2.1
    f.turn=-min(i,5)*5.8
    if 4<=i<22:
        strike=(i-4)//6
        phase=(i-4)%6
        # Swing progress, not blended 2D limbs. These inputs are sampled
        # directly by the client ModelBiped using the original item transform.
        f.swing=(.02,.12,.24,.39,.57,.82)[phase]
        f.pitch=(27.,24.,27.,35.,39.,35.)[phase]+strike*2
        f.yaw=(-16.,-20.,-23.,-25.,-24.,-19.)[phase]
        f.turn=(-29.,-30.,-32.,-35.,-37.,-39.)[phase]
    elif 22<=i<=31:
        phase=i-22
        f.swing=(.95,.0,.0,.0,.0,.0,.0,.0,.0,.0)[phase]
        f.pitch=(39.,41.,44.,47.,49.,53.,56.,56.,48.,37.)[phase]
        f.yaw=(-22.,-21.,-20.,-18.,-18.,-18.,-22.,-20.,-16.,-14.)[phase]
        f.turn=(-38.,-36.,-34.,-32.,-30.,-28.,-26.,-24.,-22.,-20.)[phase]
        f.sneak=27<=i<=30
        if i>=30:f.item=DROPS[ore]
    elif i>=32:
        f.item=DROPS[ore] if i<34 else "iron_pickaxe"
        f.pitch=(30.,23.,17.,12.)[i-32]
        f.yaw=(-12.,-10.,-9.,-7.)[i-32]
        f.turn=(-17.,-21.,-16.,0.)[i-32]
        f.stride=(.95,2.1,3.55,4.5)[i-32]
        f.amount=(.10,.28,.20,0.)[i-32]
    return f

def frame(i,ore):
    phase=i-LEAD
    if 0<=phase<30:return work_frame(phase,ore)
    if phase<0:
        f=Frame(2.+i*1.65,item='iron_pickaxe')
        f.x=(12.7,11.2,9.8,8.1,6.4,4.4)[i]
        f.yaw=(-12.,-15.,-17.,-13.,-10.,-7.)[i]
        f.pitch=(8.,9.,11.,12.,13.,14.)[i]
        f.turn=(-62.,-62.,-62.,-55.,-26.,0.)[i]
        f.stride=(.45,1.65,2.7,3.9,5.0,6.0)[i]
        f.amount=(.05,.65,.80,.73,.48,.08)[i]
        return f
    w=phase-30
    f=work_frame(min(phase,35),ore)
    f.age=float(i)*1.65+2
    f.x=4.4+(0.,.17,.41,.65,.77,.58,.24,-.27,-.91,-1.6,-2.4,-3.25)[w]
    f.z=(0.,0.,0.,-.75,-2.,-3.5,-5.25,-7.2,-9.25,-11.4,-13.65,-16.0)[w]
    if w>=2:
        f.item="iron_pickaxe"
        f.sneak=False
        f.yaw=(-8.,-13.,-17.,-20.,-22.,-22.,-22.,-22.,-22.,-22.)[w-2]
        f.pitch=max(9.,25.-1.6*w)
        f.turn=(-14.,-49.,-104.,-136.,-146.,-149.,-151.,-152.,-153.,-153.)[w-2]
        f.stride=(w-2)*.98
        f.amount=(.12,.34,.64,.77,.88,.88,.82,.75,.65,.45)[w-2]
    return f

# Both deposits are world props, with zero shared translation through a shot.
NEAR=(-7.,8.,7.)
FAR=(-7.,8.,-9.)
def world_blocks(i,ore,next_ore):
    return [(next_ore,FAR)]+([(ore,NEAR)] if i-LEAD<BREAK else [])

def input_text():
    return ''.join('focus.%d %s\n'%(i,' '.join(map(input_field,frame(i,'stone').inputs()))) for i in range(CELS))

def sample_poses():
    rows=(HERE/'focus-mine-poses-1.8.txt').read_text('ascii').splitlines()
    assert rows[0]=='# Java 1.8; inputs sha256 '+sha256(input_text().encode('ascii')).hexdigest()
    result={}
    for row in rows[1:]:
        key,*numbers=row.split()
        result[int(key.split('.')[1])]=np.asarray(numbers,dtype=float).reshape(6,6)
    assert set(result)==set(range(CELS))
    return result

@lru_cache(None)
def damage(name,stage):
    block=Image.fromarray(texture(name))
    block.alpha_composite(Image.fromarray(texture('destroy_stage_'+str(stage))))
    return {face:np.asarray(block) for face in FACES}

def body_matrices(i,f,samples):
    root=tr(f.x,24+f.lift-(3 if f.sneak else 0),getattr(f,"z",0.))@ry(f.turn)
    mats=[]
    for x,y,z,pitch,yaw,roll in samples[i]:
        a,b,c=np.degrees((pitch,-yaw,-roll))
        mats.append(root@tr(x,-y,-z)@rz(c)@ry(b)@rx(a))
    return mats

def debris(renderer,i,ore):
    step=i-BREAK
    if not -1<=step<=8:return
    rng=random.Random(117)
    for n in range(10):
        vx=rng.uniform(-1.9,2.6);vz=rng.uniform(-.8,2.2)
        vy=rng.uniform(1.0,4);x0=rng.uniform(-8.,-4.);y0=rng.uniform(6,14)
        size=rng.uniform(.55,1.45);z0=rng.uniform(4.,9.)
        dt=step if step>=0 else (i-21)
        if step<0 and n>=4:continue
        height=y0+vy*dt-.63*dt*dt
        if height<.6:continue
        renderer.box(tr(x0+vx*dt,height,z0+vz*dt)@ry(18*n+21*dt),
                     (size,size,size),cube_texture(ore))

def draw(i,desktop,ore='stone',next_ore='stone',samples=None):
    f=frame(i,ore)
    pose=body_matrices(i,f,sample_poses() if samples is None else samples)
    renderer=Renderer(4.10,184) if desktop else Renderer(3.55,132)
    for part,pivot,center,size in zip(PARTS,pose,CENTRES,SIZES):
        renderer.box(pivot@tr(*center),size,pose_skin(part))
    phase=i-LEAD
    for n,(material,where) in enumerate(world_blocks(i,ore,next_ore)):
        hits=sum(phase>=hit for hit in HIT_FRAMES)
        stage=(None,2,5,8)[hits] if n==1 else None
        block=damage(material,stage) if stage is not None else cube_texture(material)
        renderer.box(tr(*where),(16,16,16),block)
    if phase in HIT_FRAMES or BREAK<=phase<=30:
        debris(renderer,phase if phase>=21 else 21,ore)
    if BREAK<=phase<30:
        d=phase-BREAK
        dy=(5.,6.8,5.5,3.2,2.1,2.65,2.45,2.2)[d]
        drop=tr(-6+d*.62,dy,6.)@ry(d*24+15)
        item=DROPS[ore]
        if item not in CUBES:
            for x,y,n,color in item_rows(item):
                renderer.box(drop@tr(x*.23,y*.23,0),(n*.23,.23,.28),color)
        else:renderer.box(drop,(3.2,3.2,3.2),cube_texture(item))
    if f.item in ("iron_ore","gold_ore"):
        # Ore blocks are third-person block models, just like cobblestone.
        from render_steve import item_displays
        spec=item_displays()[f.item]
        pitch,yaw,roll=spec['rotation']
        tx,ty,tz=spec['translation']
        mat=pose[2]@tr(-1,-7,-1)@rx(180)@tr(2*tx,2*ty,2*tz)@ry(yaw)@rx(pitch)@rz(roll)
        renderer.box(mat,tuple(16*x for x in spec['scale']),cube_texture(f.item))
    else:held_item(renderer,f,pose[2])
    picture=renderer.finish()
    # A cut between mine sections occurs after the walking motion, not by
    # scrolling blocks into a stationary player. Fade the entire camera shot.
    opacity=(.18,.42,.67,.86,1.,1.)[i] if i<LEAD else (1.,.9,.72,.48,.25,.0)[i-42] if i>=42 else 1.
    if opacity!=1.0:picture.putalpha(picture.getchannel("A").point(lambda x:round(opacity*x)))
    return picture

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--inputs',type=Path)
    parser.add_argument('--cache',type=Path)
    parser.add_argument('--client',type=Path)
    args=parser.parse_args()
    if args.inputs:
        args.inputs.write_text(input_text(),'ascii')
    elif args.cache:
        rows=args.cache.read_text().splitlines()
        (HERE/'focus-mine-poses-1.8.txt').write_text(
            '# Java 1.8; inputs sha256 '+sha256(input_text().encode('ascii')).hexdigest()+'\n'+'\n'.join(rows)+'\n','ascii')
        if args.client:
            assert sha1(args.client.read_bytes()).hexdigest()=='d722504db9de2b47f46cc592b8528446272ae648'
            with ZipFile(args.client) as z:
                items=HERE/'items'
                for n in (*DROPS,'destroy_stage_5','destroy_stage_8'):
                    (items/(n+'.png')).write_bytes(z.read('assets/minecraft/textures/blocks/'+n+'.png'))
                models={'redstone_dust':'redstone','dye_powder_blue':'dye_blue'}
                p=items/'display.json'
                views=json.loads(p.read_text())
                for n in DROPS.values():
                    data=json.loads(z.read('assets/minecraft/models/item/'+models.get(n,n)+'.json'))
                    if n not in CUBES:
                        (items/(n+'.png')).write_bytes(z.read('assets/minecraft/textures/items/'+n+'.png'))
                    views[n]=data.get('display',{}).get('thirdperson',views['cobblestone'])
                p.write_text(json.dumps(views,indent=2)+chr(10))
    else:
        assets=HERE.parents[1]/'src'/'assets'
        poses=sample_poses()
        for desktop in (False,True):
            for ore,next_ore in PAIRS:
                sheet=Image.new('RGBA',(192*6,208*(CELS//6)))
                for i in range(CELS):
                    pose=draw(i,desktop,ore,next_ore,poses)
                    bounds=pose.getchannel('A').point(lambda v:255 if v>32 else 0).getbbox()
                    assert (i==CELS-1 and bounds is None) or (bounds and bounds[0]>=2 and bounds[1]>=2 and bounds[2]<=190 and bounds[3]<=206),(desktop,i,bounds)
                    sheet.paste(pose,(i%6*192,i//6*208))
                sheet.save(assets/('motion-steve-focusmate'+('-desk' if desktop else '')+pair_suffix(ore,next_ore)+'.png'),optimize=True)
