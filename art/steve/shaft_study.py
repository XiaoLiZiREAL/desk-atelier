"""Two-site, fixed-camera rhythm study. Samples a verified Java 1.8 ModelBiped."""
import argparse
import math
from pathlib import Path
import random
from hashlib import sha1
import subprocess
import tempfile

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from classic_scenes import Frame,input_field
from render_steve import (Renderer,PARTS,CENTRES,SIZES,pose_skin,tr,rx,ry,rz,
                          held_item,cube_texture,item_rows,item_displays,point)
from render_focus_mine import damage

SITE=(-1.,31.)
STOP=(-25.25,6.75)
DURATION=70

def timeline():
    # Camera does not move at any point. Positions are model-space units.
    plans=[]
    def add(x, *,site=0, progress=0, pick=0., stride=0., amount=0.,
            turn=83., yaw=0., pitch=3., item='iron_pickaxe', dust=-1, state='walking', pause=1):
        f=Frame(len(plans)*1.4+2.,x=x,turn=turn,yaw=yaw,pitch=pitch, item=item,
                swing=pick,stride=stride,amount=amount)
        plans.append((f,site,progress,dust,state,pause))
    start=-37.75
    for site,target in enumerate(STOP):
        for k in range(16):
            u=k/15.
            x=start+(target-start)*u
            gait=min(u*5.,(1.-u)*5.,1.)*.63
            add(x,site=site,stride=u*(target-start)*.52,amount=gait,
                yaw=3.5*math.sin(u*(target-start)*.36),
                pitch=3.+2.5*math.sin(u*(target-start)*.5))
        add(target,site=site,yaw=0.,pitch=5.,state='ready',pause=3)
        add(target,site=site,yaw=-1.,pitch=9.,state='ready',pause=2)
        # Two slow, complete strikes. Each speed is sampled, not blended.
        phases=(.035,.105,.17,.235,.30,.37,.45,.54,.64,.74,.86,.97)
        for swing in range(2):
            for j,v in enumerate(phases):
                hit=j>=8
                progress=8 if swing==1 and hit else 2 if swing==1 or hit else 0
                add(target,site=site,progress=progress,pick=v,pitch=9.2,state='hit' if hit else 'ready',
                    dust=j-8 if hit else -1)
        for j in range(13):
            moved=max(0.,(j-5)/7.)
            add(target+6.*moved,site=site,progress=10,dust=j+4,
                pitch=(11+min(j,6)*5 if j<7 else 41-(j-6)*4),
                stride=moved*3.1,amount=.42*math.sin(moved*math.pi),
                # The drop slides into pickup range, inventory sound is omitted.
                item=('cobblestone' if site==0 else 'coal') if j in (10,11) else 'iron_pickaxe',
                state='collect')
        start=target+6.
    for j in range(1,15):
        u=j/14.
        add(start+(36.-start)*u,site=2,progress=10,stride=u*9.,amount=min(.62,u*3,(1-u)*3))
    return plans

def native_poses(plans,java,cp,client):
    assert sha1(client.read_bytes()).hexdigest()=='d722504db9de2b47f46cc592b8528446272ae648'
    with tempfile.TemporaryDirectory() as folder:
        ins=Path(folder)/'poses-input.txt'; out=Path(folder)/'poses-output.txt'
        ins.write_text(''.join(f'shaft.{i} '+' '.join(map(input_field,f.inputs()))+'\n'
                      for i,(f,*_) in enumerate(plans)),'ascii')
        subprocess.run([java,'-cp',cp,'atelier.art.SampleClassic',str(ins),str(out)],check=True)
        return [np.array([float(x) for x in line.split()[1:]]).reshape((6,6))
                for line in out.read_text('ascii').splitlines()]

def body(f, sample):
    root=tr(f.x,24.,1.4)@ry(f.turn)
    mats=[]
    for x,y,z,p,yaw,roll in sample:
        a,b,c=np.degrees((p,-yaw,-roll))
        mats.append(root@tr(x,-y,-z)@rz(c)@ry(b)@rx(a))
    return mats

def camera():
    r=Renderer(2.10,150)
    a,b=map(math.radians,(-5,12))
    r.right=np.array((math.cos(a),0,-math.sin(a)))
    r.up=np.array((-math.sin(a)*math.sin(b),math.cos(b),-math.cos(a)*math.sin(b)))
    r.view=np.array((math.sin(a)*math.cos(b),math.sin(b),math.cos(a)*math.cos(b)))
    return r

def render(plan, sample):
    f,site,progress,dust,state,_=plan
    r=camera()
    op=body(f,sample)
    for x in (-32.,-16.,0.,16.,32.):
        r.box(tr(x,-8.,3.0),(16,16,16),cube_texture('stone'))
    # Low cross-section with permanent dark cutaway back wall. The blocked path
    # is in front of it. Never move/replace a deposit that Steve hasn't broken.
    base=cube_texture('stone')
    shadow={}
    for side,tex in base.items():
        t=tex.copy();t[:,:,:3]=(t[:,:,:3]*np.array([.61,.64,.67])).astype('uint8');shadow[side]=t
    for col,x in enumerate((-33.,-17.,-1.,15.,31.)):
        for y in ((8.,) if col<2 else (8.,24.)):
            r.box(tr(x,y,-14.6),(16,16,16),shadow)
    for s,(x,ore) in enumerate(zip(SITE,('stone','coal_ore'))):
        if s<site or s==site and progress==10:continue
        tex=damage(ore,progress) if s==site and progress else cube_texture(ore)
        r.box(tr(x,23.8,1.4),(16,16,16),tex)
    for part,m,center,dim in zip(PARTS,op,CENTRES,SIZES):
        r.box(m@tr(*center),dim,pose_skin(part))
    held_item(r,f,op[2])
    if dust>=0 and site<2:
        ore=('stone','coal_ore')[site]
        rng=random.Random(543+site)
        for j in range(9 if progress==10 else 4):
            dx=rng.uniform(-2.9,.9);vz=rng.uniform(.8,2.3);dy=rng.uniform(0.,1.6)
            sx=SITE[site]-8.+rng.uniform(0.,3.);sy=rng.uniform(14.,27.);sz=1.4+rng.uniform(2.,7.)
            dt=min(dust,11)*.63
            y=sy+dy*dt-.65*dt*dt
            if y<.4:continue
            size=rng.uniform(.5,1.3)
            r.box(tr(sx+dx*dt,y,sz+vz*dt)@ry(j*31+17*dt),(size,size,size),cube_texture(ore))
        if 10==progress and dust<13:
            t=(dust-4)/9.
            x=SITE[site]-5.-t*8.
            y=max(1.8,9.-t*t*17.)
            at=tr(x,y,4.8)@ry(dust*22)
            if site==0:r.box(at,(3,3,3),cube_texture('cobblestone'))
            else:
                for xx,yy,n,col in item_rows('coal'):
                    r.box(at@tr(xx*.24,yy*.24,0),(n*.24,.24,.27),col)
    return r.finish()

def make(plans, poses, target):
    paper=Image.new('RGB',(570,325),'#e9e7df')
    d=ImageDraw.Draw(paper)
    font='/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'
    def ft(n):return ImageFont.truetype(font,n,index=2)
    d.rounded_rectangle((14,52,556,291),radius=14,fill='#323b43',outline='#43525c')
    d.text((18,12),'史蒂夫  /  侧视矿道重制',font=ft(23),fill='#25323c')
    d.text((18,303),'Java 1.8 原始模型与贴图  ·  同一镜头  ·  两段真实走位',font=ft(12),fill='#42505b')
    frames=[];duration=[]
    for p,s in zip(plans,poses):
        scene=render(p,s).crop((0,42,192,185)).resize((414,229),Image.Resampling.NEAREST)
        im=paper.copy()
        im.paste(scene,(78,57),scene)
        frames.append(im)
        duration.append(DURATION*p[-1])
    palette=Image.new('RGB',(285,163*5))
    for i,k in enumerate([9,27,43,59,101]):palette.paste(frames[k].resize((285,163)),(0,163*i))
    palette=palette.quantize(colors=256);output=[x.quantize(palette=palette,dither=Image.Dither.NONE) for x in frames]
    output[0].save(target,save_all=True,append_images=output[1:],duration=duration,loop=0,optimize=True)
    # The contact sheet is for inspection, not a substitute for motion.
    indices=(4,15,24,26,37,47,60,73,85,98,110,121)
    out=Image.new('RGB',(900,3*185),'#e9e7df')
    for n,i in enumerate(indices):
        im=render(plans[i],poses[i])
        x,y=n%4*225,n//4*185
        out.paste(im.crop((0,12,192,197)),(x+14,y),im.crop((0,12,192,197)))
        ImageDraw.Draw(out).text((x+4,y+4),str(i),fill='#27323d')
    out.save(target.with_name('shaft-review.png'))
    with Image.open(target) as g:
        assert g.n_frames>105
        print('full-model cels',g.n_frames,'duration',sum(duration),'size',target.stat().st_size)

if __name__=='__main__':
    p=argparse.ArgumentParser()
    p.add_argument('--java',default='java')
    p.add_argument('--client',type=Path,required=True)
    p.add_argument('--cp',required=True)
    p.add_argument('--output',type=Path,required=True)
    a=p.parse_args()
    steps=timeline()
    assert all(p[0].x<=q[0].x for p,q in zip(steps,steps[1:]))
    make(steps,native_poses(steps,a.java,a.cp,a.client),a.output)
