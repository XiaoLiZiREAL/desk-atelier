"""Fixed-camera Minecraft 1.8 cave scouting study, built from complete voxel frames."""
import argparse
from dataclasses import dataclass
from functools import lru_cache
import math
from pathlib import Path
import random
from zipfile import ZipFile
import io

import numpy as np
from PIL import Image,ImageDraw,ImageFont
from classic_scenes import Frame
from render_steve import (Renderer,PARTS,CENTRES,SIZES,pose_skin,tr,rx,ry,rz,point,
                          held_item,cube_texture,item_rows,item_displays)
from render_focus_mine import damage
from shaft_study import native_poses

FPS=20
ORE=(16.,24.,-32.)
STANCE=(13.4,-9.22)
@dataclass
class Cel:
    model: Frame
    z: float
    crack: int = -1
    broken_for: int = -1
    dust: int = -1
    gaze: str = 'walk'

def lerp(a,b,t):return a+(b-a)*t
def smooth(t):return t*t*(3-2*t)
def angle(dx,dz):return math.degrees(math.atan2(dx,dz))

def blocking():
    frames=[]
    total_distance=0.
    x,z=(-30.,22.)
    facing=angle(14,-17)
    def push(*,leg=0.,crack=-1,broken=-1,dust=-1,pitch=4.,look=0.,swing=0.,item='iron_pickaxe',gaze='walk'):
        f=Frame(len(frames),x=x,turn=facing,yaw=look,pitch=pitch,stride=total_distance*.57,
                amount=leg,swing=swing,item=item)
        frames.append(Cel(f,z,crack,broken,dust,gaze))
    def walk(target,count,post=False,first=False,last=False):
        nonlocal x,z,facing,total_distance
        ox,oz=x,z; then=facing
        new=angle(target[0]-x,target[1]-z)
        for i in range(1,count+1):
            u=i/count
            # Positions and sampled leg cycle use the same traveled distance.
            dx,dz=lerp(ox,target[0],u)-x,lerp(oz,target[1],u)-z
            total_distance+=math.hypot(dx,dz)
            x,z=x+dx,z+dz
            facing=lerp(then,new,smooth(min(1.,i/7.)))
            amp=min(1.,i/5. if first else 1.,(count-i)/6. if last else 1.)*.69
            push(leg=amp,look=3.*math.sin(total_distance*.18),pitch=4. if not post else 6.,
                 broken=200+i if post else -1)
    # Quietly pass the barren section, round a corner, then notice the seam.
    walk((-16.,5.),23,first=True)
    walk((10.,6.),28,last=True)
    start=facing
    for i in range(14):
        u=smooth((i+1)/14.)
        facing=lerp(start,174.,u)
        push(look=lerp(21.,0.,u),pitch=lerp(4.,-2.,u),gaze='notice')
    walk(STANCE,13,last=True,first=True)
    before=facing
    # Aim slightly to the right of the ore. The native right arm arcs back onto
    # the exposed north wall; contact is calibrated in model space below.
    for j in range(8):
        facing=lerp(before,158.,smooth((j+1)/8.))
        push(look=2.0,pitch=-2.7,gaze='aim')
    for k in range(3):
        for tick,sp in enumerate((0.,1/6,2/6,3/6,4/6,5/6,0.)):
            hit=tick>=4
            crack=(2,5,8)[k] if hit else (-1,2,5)[k]
            push(crack=crack,swing=sp,pitch=-2.7,gaze='hit' if hit else 'swing',
                 dust=tick-4 if hit else -1)
    for j in range(18):
        # Settle, look at the item drop and step into pickup range.
        if j>=9:
            x+=.39;z-=.33;total_distance+=.51
        push(leg=.28 if j>=9 else 0.,pitch=7.+math.sin(j/18.*math.pi)*24.,
             look=2.2,broken=j,dust=j+3,gaze='pickup',
             item='coal' if 14<=j<17 else 'iron_pickaxe')
    walk((30.,3.),17,post=True,first=True)
    walk((39.,-13.),19,post=True,last=True)
    return frames

def sample(cels,jar,cp):
    steps=[(c.model,0,0,0,c.gaze,1) for c in cels]
    return native_poses(steps,'java',cp,jar)

def model_mats(c,pose):
    f=c.model
    root=tr(f.x,24.,c.z)@ry(f.turn)
    return [root@tr(x,-y,-z)@rz(-math.degrees(roll))@ry(-math.degrees(yaw))@rx(math.degrees(p))
            for x,y,z,p,yaw,roll in pose]

def pick_points(c,pose):
    arm=model_mats(c,pose)[2]
    view=item_displays()['iron_pickaxe']
    pitch,yaw,roll=view['rotation']
    tx,ty,tz=view['translation']; sx,sy,_=view['scale']
    mat=arm@tr(-1,-7,-1)@rx(180)@tr(2*tx,2*ty,2*tz)@ry(yaw)@rx(pitch)@rz(roll)
    # The iron head is the grey part of the item, not the hand or handle.
    return np.asarray([point(mat,np.array([(x+u)*sx,y*sy,0.]))
             for x,y,n,rgb in item_rows('iron_pickaxe') if max(rgb)-min(rgb)<25 and y>=1
             for u in (-n/2,n/2)])

@lru_cache(None)
def tint(material,level,warm=0):
    levels=np.array((level+warm*.08,level+warm*.035,level-warm*.06))
    out={}
    for side,tex in cube_texture(material).items():
        t=tex.copy();t[:,:,:3]=np.clip(t[:,:,:3]*levels,0,255).astype('uint8');out[side]=t
    return out

def camera():
    r=Renderer(2.00,149)
    a,b=map(math.radians,(-15,24))
    r.right=np.array((math.cos(a),0,-math.sin(a)))
    r.up=np.array((-math.sin(a)*math.sin(b),math.cos(b),-math.cos(a)*math.sin(b)))
    r.view=np.array((math.sin(a)*math.cos(b),math.sin(b),math.cos(a)*math.cos(b)))
    return r

# Fixed voxels around an irregular, level walking path. The ore replaces a
# wall voxel at precisely the same depth as the surrounding stone.
WALL=[
    (-32,8,-17),(-32,24,-17),(-32,40,-17),
    (-16,8,-24),(-16,24,-24),
    (0,8,-32),(0,24,-32),(0,40,-32),
    (16,8,-32),(16,40,-32),
    (32,8,-40),(32,24,-40),(32,40,-40),
    (32,8,-24),(32,24,-24),
    (-32,8,-1),(-32,24,-1),(-32,40,-1)
]
FLOOR=[(x,-8,z) for x,zs in [(-32,(-16,0,16,32)),(-16,(-32,-16,0,16,32)),
    (0,(-32,-16,0,16)),(16,(-32,-16,0,16)),(32,(-32,-16,0,16))] for z in zs]

def make_torch(r,p,tex,index):
    at=tr(*p)@rz(23)@rx(-15)
    # The Minecraft texture covers a narrow cutout; use a single complete torch.
    for dy,src in enumerate(tex):
        for xx,rgba in enumerate(src):
            if rgba[3]<80:continue
            color=tuple(map(int,rgba[:3]))
            r.box(at@tr((xx-7.5)*.46,(7.5-dy)*.46,0),(.49,.48,.49),color)
    for j in range(3):
        seed=(index+j*5)%17
        col=('f8c768','f0a449','df8740')[j]
        r.box(tr(p[0]-1.8+math.sin(seed)*.64,p[1]+3.3+seed*.18,p[2]+.5),
              (.35,.36,.35),col)

def matte(c,pose,torch,index):
    r=camera()
    for x,y,z in FLOOR:
        level=.55+.06*((x//16+z//16)%3)
        if x in (-16,0,16) and z in (-16,0,16):level+=.13
        r.box(tr(x,y,z),(16,16,16),tint('stone',level,1))
    for x,y,z in WALL:
        warm=1 if x in (-16,0,16) else 0
        level=(.45 if z<-22 else .55)+(.09 if x==0 else 0.)
        r.box(tr(x,y,z),(16,16,16),tint('stone',level,warm))
    if c.broken_for<0:
        ore=tint('coal_ore',.60,1)
        if c.crack>=0:
            ore={k:(v*np.array([.64,.60,.56,1.])).astype('uint8') for k,v in damage('coal_ore',c.crack).items()}
        r.box(tr(*ORE),(16,16,16),ore)
    for p in [(-15.,19.,-15.1),(9.,17.,-23.2)]:
        make_torch(r,p,torch,index//3)
    b=model_mats(c,pose)
    for part,p,ctr,dim in zip(PARTS,b,CENTRES,SIZES):
        r.box(p@tr(*ctr),dim,pose_skin(part))
    held_item(r,c.model,b[2])
    if c.dust>=0:
        rng=random.Random(94)
        for j in range(10 if c.broken_for>=0 else 4):
            x,y,z=16+rng.uniform(-5,5),19+rng.uniform(0,9),-23.+rng.uniform(0,2)
            u=c.dust*.63;size=rng.uniform(.45,.85)
            h=y+rng.uniform(0,3)*u-.62*u*u
            if h<.4:continue
            r.box(tr(x+rng.uniform(-1.1,1.1)*u,h,z+u*.8)@ry(j*19+u*25),(size,size,size),tint('coal_ore',.72,1))
    if 0<=c.broken_for<=13:
        s=c.broken_for/13.
        m=tr(16.+s*(c.model.x-16.),max(2.,12.-s*s*17.),-21.+s*12.)@ry(s*200)
        for x,y,n,col in item_rows('coal'):
            r.box(m@tr(x*.23,y*.23,0),(n*.23,.23,.24),col)
    return r.finish()

def export(cels,poses,jar,out):
    with ZipFile(jar) as z:
        torch=np.asarray(Image.open(io.BytesIO(z.read('assets/minecraft/textures/blocks/torch_on.png'))).convert('RGBA'))
    bg=Image.new('RGB',(454,300),'#171e24')
    d=ImageDraw.Draw(bg)
    font='/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'
    def ft(n):return ImageFont.truetype(font,n,index=2)
    d.text((21,12),'史蒂夫  /  火把之后',font=ft(20),fill='#e8dcc0')
    d.text((21,274),'穿过石壁，发现煤脉，再沿矿道往里走。',font=ft(13),fill='#c2b698')
    d.rounded_rectangle((15,42,438,267),radius=10,fill='#202830',outline='#394049')
    rgb=[]
    for i,(c,p) in enumerate(zip(cels,poses)):
        art=matte(c,p,torch,i).crop((0,49,192,183)).resize((320,223),Image.Resampling.NEAREST)
        im=bg.copy();im.paste(art,(67,43),art);rgb.append(im)
    sample=Image.new('RGB',(227,150*8))
    for i,k in enumerate(np.linspace(0,len(rgb)-1,8).astype(int)):
        sample.paste(rgb[k].resize((227,150)),(0,150*i))
    palette=sample.quantize(colors=256)
    indexed=[f.quantize(palette=palette,dither=Image.Dither.NONE) for f in rgb]
    indexed[0].save(out,save_all=True,append_images=indexed[1:],duration=[50]*(len(indexed)-1)+[500],loop=0,optimize=True)
    indices=(12,43,58,84,100,116,129,len(rgb)-5)
    sheet=Image.new('RGB',(908,300),'#171e24')
    for n,i in enumerate(indices):
        im=rgb[i].resize((227,150));sheet.paste(im,(n%4*227,n//4*150))
    sheet.save(out.with_name('cave-review.png'))
    with Image.open(out) as g:
        assert g.n_frames>=len(rgb)*.8
        print('native cels',len(cels),'encoded cels',g.n_frames,'bytes',out.stat().st_size)

if __name__=='__main__':
    p=argparse.ArgumentParser()
    p.add_argument('--client',required=True,type=Path)
    p.add_argument('--cp',required=True)
    p.add_argument('--out',required=True,type=Path)
    p.add_argument('--contacts-only',action='store_true')
    a=p.parse_args()
    cels=blocking();poses=sample(cels,a.client,a.cp)
    lo=np.array(ORE)-8;hi=np.array(ORE)+8
    changes=[]
    prior=-1
    for i,(c,s) in enumerate(zip(cels,poses)):
        if c.crack!=prior and c.crack in (2,5,8):
            gray=pick_points(c,s)
            d=float(np.min(np.linalg.norm(np.maximum(np.maximum(lo-gray,0),gray-hi),axis=1)))
            assert d<.25,(i,c.crack,d)
            changes.append((i,c.crack,round(d,3)))
        prior=c.crack
    assert len(changes)==3 and all(cels[i].model.x==STANCE[0] and cels[i].z==STANCE[1] for i,_,_ in changes)
    assert sum(c.broken_for==0 for c in cels)==1
    print('native pick head contacts',changes,'; no camera or ore translation')
    if not a.contacts_only:export(cels,poses,a.client,a.out)
