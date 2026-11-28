"""Complete, fixed-camera Java 1.8 cels for multiple Desk Atelier cave routes."""
import argparse
from dataclasses import dataclass
from functools import lru_cache
import io
import math
from pathlib import Path
from zipfile import ZipFile
from concurrent.futures import ProcessPoolExecutor

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from classic_scenes import Frame
import cave_scout as native
from render_steve import PARTS,CENTRES,SIZES,pose_skin,tr,ry,held_item,cube_texture,item_rows,texture
from render_focus_mine import damage

CELL_W,CELL_H=192,208
FRAME_COUNT=252
IDLE_FIRST=180
QUIET_CELS=16
EXIT_FIRST=228
routes=("torch","fork","stairs")
actions=("pass","check","coal","iron","red","lapis","gold","diamond","emerald")
ore_names={"coal":"coal_ore","iron":"iron_ore","gold":"gold_ore","red":"redstone_ore","lapis":"lapis_ore",
           "diamond":"diamond_ore","emerald":"emerald_ore"}
drops={"coal":"coal","iron":"iron_ore","gold":"gold_ore","red":"redstone_dust","lapis":"dye_powder_blue","diamond":"diamond","emerald":"emerald"}
STONE_SIZE=(16,16,16)
CATALOG=(
    "torch-pass","torch-check","torch-coal","torch-red","torch-lapis","torch-diamond",
    "fork-pass","fork-check","fork-coal","fork-iron","fork-gold","fork-emerald",
    "stairs-pass","stairs-check","stairs-coal","stairs-iron","stairs-red","stairs-diamond",
)


@dataclass
class World:
    floor: tuple
    walls: tuple
    lanterns: tuple
    azimuth: float
    tilt: float
    scale: float
    name: str
    description: str

FORK_FLOOR=tuple((x,-8.,z) for x,zs in [(-32,(-32,-16,0,16,32)),(-16,(-32,-16,0,16,32)),
    (0,(-32,-16,0,16,32)),(16,(-32,-16,0,16)),(32,(-32,-16,0))] for z in zs)
FORK_WALL=tuple(
    [(x,y,z) for x,z,rows in [(-39,-31,3),(-39,-15,3),(-39,1,2),(-39,17,1),
                               (-11,-19,3),(-11,-35,3),(5,-40,3),(16,-32,3),(32,-40,3),(36,-24,2)]
     for y in (8.,24.,40.)[:rows] if (x,y,z)!=native.ORE] +
    [(-28,8,-46),(-28,24,-46)])
STAIR_LOW=tuple((x,-8.,z) for x in (0,16,32) for z in (-32,-16,0,16))
STAIR_WALL=tuple([(x,y,z) for x,z,ys in [(-31,-10,(8,24,40)),(-15,-20,(8,24,40)),
           (0,-32,(8,24,40)),(16,-32,(8,40)),(32,-40,(8,24,40)),(38,-23,(8,24)),(-31,27,(8,24))]
           for y in ys])
WORLDS={
    "torch":World(tuple(native.FLOOR),tuple(native.WALL),((-15.,19.,-15.1),(9.,17.,-23.2)),-15.,24.,2.00,
                  "火把之后","绕过石壁，循着火把找矿脉"),
    "fork":World(FORK_FLOOR,FORK_WALL,((-18.,21.,-10.),(22.,19.,-23.)), -9.,31.,1.82,
                 "岔路口","停下来辨路，先看左洞，再走右洞"),
    "stairs":World(STAIR_LOW,STAIR_WALL,((-24.,38.,-1.3),(10.,18.,-24.)), -21.,27.,1.92,
                   "阶梯洞","沿错层下去，到了低处才挥镐")
}

@dataclass
class Step:
    cel: native.Cel
    y: float=0.
    delay: int=50

def path(route,action):
    origin={"torch":(-30.,22.,0.),"fork":(-31.,28.,0.),"stairs":(-36.,13.,24.)}[route]
    x,z,y=origin
    turn={"torch":140.,"fork":142.,"stairs":91.}[route]
    distance=0.
    steps=[]
    def emit(gaze='walk',amount=0.,yaw=0.,pitch=4.,swing=0.,item='iron_pickaxe',crack=-1,broken=-1,dust=-1):
        f=Frame(len(steps),x=x,turn=turn,yaw=yaw,pitch=pitch,stride=distance*.57,amount=amount,swing=swing,item=item)
        steps.append(Step(native.Cel(f,z,crack,broken,dust,gaze),y))
    def swivel(dest,n,head_ang=0.,pitch=0.,broken=-1):
        nonlocal turn
        start=turn
        # Unwrap the shortest turn to avoid 360° spins on a path corner.
        delta=(dest-start+180.)%360.-180.
        for i in range(1,n+1):
            u=native.smooth(i/n);turn=start+delta*u
            emit("look",yaw=head_ang*math.sin(u*math.pi),pitch=pitch,broken=broken)
    def move(end,n,*,past=False,vertical=False):
        nonlocal turn,distance,x,z,y
        start=(x,z,y); a=turn
        target=native.angle(end[0]-x,end[1]-z)
        delta=(target-a+180.)%360.-180.
        for i in range(1,n+1):
            t=i/n; px,pz,py=x,z,y
            x=native.lerp(start[0],end[0],t);z=native.lerp(start[1],end[1],t)
            if vertical:
                # Approach every lip before descending; fall to the lower
                # tread over the final footfall, not a sliding diagonal ramp.
                weight=native.smooth(max(0.,min(1.,(t-.57)/.38)))
                y=native.lerp(start[2],end[2],weight)
            else:y=end[2]
            turn=a+delta*native.smooth(min(1.,i/7.))
            distance+=math.hypot(px-x,pz-z)
            amp=min(i/5.,(n-i)/6.,1.)*.65
            emit("descend" if vertical else "walk",amp,3.*math.sin(distance*.12),16. if vertical else 5.,
                 broken=250 if past else -1)
    if route=="torch":
        move((-17.,7.,0.),23);move((10.,6.,0.),27)
    elif route=="fork":
        move((-18.,11.,0.),23)
        swivel(-153.,12,20.,5.)
        swivel(85.,13,-22.,0.)
        move((10.,8.,0.),26)
    else:
        move((-25.,13.,24.),14)
        move((-19.,13.,16.),12,vertical=True)
        move((-11.,13.,8.),12,vertical=True)
        move((-3.,13.,0.),12,vertical=True)
        move((9.,10.,0.),16)
    if action in ("pass","check"):
        if action=="check":
            swivel(157.,15,15.,-10.)
            for i in range(24):emit("check",yaw=11*math.sin(i/12),pitch=-3.+5.*math.sin(i/10.))
        move((30.,7.,0.),24);move((36.,-13.,0.),19)
    else:
        swivel(156.,11,10.,3.)
        move((native.STANCE[0],native.STANCE[1],0.),13)
        swivel(158.,8,pitch=-2.7)
        # The native reference stance is identical across cave layouts.
        turn=158.
        for k in range(3):
            for tick,sp in enumerate((0.,1/6,2/6,3/6,4/6,5/6,0.)):
                hit=tick>=4
                emit("hit" if hit else "swing",pitch=-2.7,swing=sp,
                     crack=(2,5,8)[k] if hit else (-1,2,5)[k],
                     dust=tick-4 if hit else -1)
        for j in range(18):
            if j>=9:x+=.39;z-=.33;distance+=.51
            emit("pickup",amount=.27 if j>=9 else 0.,pitch=7.+math.sin(j/18.*math.pi)*24.,
                 broken=j,dust=j+3,item=drops[action] if 14<=j<17 else 'iron_pickaxe')
        move((29.,4.,0.),17,past=True);move((36.,-13.,0.),16,past=True)
    already=action not in ('pass','check')
    assert len(steps)<=IDLE_FIRST, (route,action,len(steps))
    # Hold as the character looks further into the next passage. The long
    # rhythms interleave this very small, quiet action with full excursions.
    state=500 if already else -1
    while len(steps)<IDLE_FIRST:
        emit("quiet",pitch=5.,broken=state)
    # Three complete quiet gestures use the same body and site anchor;
    # none raises a tool, manufactures a drop, or changes the vein.
    for name,gaze,pitch in (("look-up",8.,-18.),("look-back",-35.,1.),("check-pick",19.,26.)):
        for i in range(QUIET_CELS):
            u=i/(QUIET_CELS-1.)
            ease=(math.sin(u*math.pi)**1.55)
            emit(name,yaw=gaze*ease,pitch=5.+pitch*ease,broken=state)
    assert len(steps)==EXIT_FIRST
    # Steve physically walks through the lip of the next passage. A fixed
    # foreground buttress hides him before the next camera cut.
    start=(x,z)
    direction=native.angle(56.5-x,-12.-z)
    diff=(direction-turn+180.)%360.-180.
    facing_start=turn
    for i in range(1,FRAME_COUNT-EXIT_FIRST+1):
        u=i/(FRAME_COUNT-EXIT_FIRST)
        lastx,lastz=x,z
        x=native.lerp(start[0],56.5,native.smooth(u));z=native.lerp(start[1],-12.,u)
        distance+=math.hypot(lastx-x,lastz-z)
        turn=facing_start+diff*native.smooth(min(1.,i/8.))
        emit("exit",amount=min(1.,i/6.)*.66,pitch=4.,broken=state)
    return steps

def cave_camera(world):
    r=native.Renderer(world.scale,151.)
    a,b=map(math.radians,(world.azimuth,world.tilt))
    r.right=np.array((math.cos(a),0,-math.sin(a)))
    r.up=np.array((-math.sin(a)*math.sin(b),math.cos(b),-math.cos(a)*math.sin(b)))
    r.view=np.array((math.sin(a)*math.cos(b),math.sin(b),math.cos(a)*math.cos(b)))
    return r

def terraces(r):
    # Original stone stairs are a half block plus one half-height step.
    # The lower shapes are built out of one texture set and don't shift.
    for center,top in [(-32.,24.),(-22.,16.),(-14.,8.)]:
        width=16. if center==-32. else 8.
        for z in (0.,16.,32.):
            r.box(tr(center,top/2.-4.,z),(width,top+8.,16.),native.tint('stone',.66,1))

@lru_cache(None)
def water_material():
    out={}
    for face,src in cube_texture("water_still").items():
        pixels=src.copy()
        lum=src[:,:,:3].astype(float).mean(axis=2)/255.
        pixels[:,:,:3]=np.asarray([19.,43.,54.])+lum[:,:,None]*np.asarray([55.,100.,117.])
        pixels[:,:,3]=255
        out[face]=pixels
    return out

def geometry(r,world,route,step,action,torch,index):
    for x,y,z in world.floor:
        level=.57+.07*((x//16+z//16)%3)
        if z in (0,16):level+=.10
        r.box(tr(x,y,z),STONE_SIZE,native.tint('stone',level,int(route!="stairs")))
    if route=="stairs":terraces(r)
    r.box(tr(48.5,-8.,-12.),(20.,16.,16.),native.tint('stone',.55))
    # This is part of the fixed geometry, also there during the mining.
    for yy in (8.,24.,40.):
        r.box(tr(47.5,yy,-1.5),(16.,16.,11.),native.tint('stone',.54,int(route=="torch")))
    for x,y,z in world.walls:
        # Alternate the tint in depth. Branch has a darker unchosen left nook.
        tint=.48 if z<-25 else .57
        if route=="fork" and x<-12 and z<-22:tint-=.13
        if route=="stairs" and x>=16:tint+=.08
        r.box(tr(x,y,z),STONE_SIZE,native.tint('stone',tint,int(x<20)))
    if route=="fork":
        # Unchosen left passage is marked by a weathered mineshaft support,
        # not another identical rock corridor. It does not obstruct the path.
        wood=native.tint('planks_oak',.53,1)
        for xx in (-32.9,-22.2):r.box(tr(xx,13,-43.2),(2.,26.,2.),wood)
        r.box(tr(-27.55,26.8,-43.2),(14.,3.,3.),wood)
    if route=="stairs":
        # Water sits in a depression off to the side of the lower walk.
        # A cool visible contrast to torch stone, never animated scenery motion.
        r.box(tr(25.,.35,17.5),(13.,.65,10.5),water_material())
        for k in ((32.5,1.4,17.4),(22.4,1.,23.0)):
            r.box(tr(*k),(3.8,2.9,3.8),native.tint('stone',.76))
    c=step.cel
    if c.broken_for<0:
        material=ore_names.get(action,'stone')
        colored=native.tint(material,.64,1)
        if c.crack>=0:
            colored={k:(v*np.array([.65,.61,.56,1.])).astype('uint8') for k,v in damage(material,c.crack).items()}
        r.box(tr(*native.ORE),STONE_SIZE,colored)
    for i,p in enumerate(world.lanterns):native.make_torch(r,p,torch,index//3+i*7)

def draw(step,pose,route,action,torch,index,static,actor=True):
    world=WORLDS[route];r=cave_camera(world);c=step.cel
    key=c.crack if c.broken_for<0 else 99
    fallback=static.get(key)
    if fallback is None:
        geometry(r,world,route,step,action,torch,0)
        static[key]=(r.pixels.copy(),r.depth.copy())
    else:
        r.pixels[:]=fallback[0];r.depth[:]=fallback[1]
    for k,(px,py,pz) in enumerate(world.lanterns):
        t=(index+k*11)%18
        if t<13:r.box(tr(px-1.6+.48*math.sin(t*.79),py+3.9+.16*t,pz+.7),(.38,.38,.38),'db8a43')
    if not actor:return r.finish()
    mats=[tr(0.,step.y,0.)@m for m in native.model_mats(c,pose)]
    for part,p,ctr,dim in zip(PARTS,mats,CENTRES,SIZES):r.box(p@tr(*ctr),dim,pose_skin(part))
    if c.model.item in ('iron_ore','gold_ore'):
        # ore drops are blocks: never draw their texture on a flat hand card.
        from render_steve import item_displays,rx,rz
        view=item_displays()[c.model.item];pitch,yaw,roll=view['rotation']
        x,y,z=view['translation']
        p=mats[2]@tr(-1,-7,-1)@rx(180)@tr(2*x,2*y,2*z)@ry(yaw)@rx(pitch)@rz(roll)
        r.box(p,tuple(16*k for k in view['scale']),cube_texture(c.model.item))
    else:held_item(r,c.model,mats[2])
    if c.dust>=0 and action in drops:
        rng=np.random.default_rng(182)
        for j in range(9 if c.broken_for>=0 else 4):
            dx,dy,dz=rng.uniform([-4.5,0.,0.],[4.5,10.,2.])
            t=c.dust*.60;h=18.+dy+t*.9-t*t*.63
            if h<.5:continue
            r.box(tr(16.+dx*(1+t/8),h,-23.+dz+t*.65)@ry(t*20+j*19),(.65,.65,.65),native.tint(ore_names[action],.68,1))
    if 0<=c.broken_for<=13 and action in drops:
        s=c.broken_for/13.
        m=tr(16.+s*(c.model.x-16.),max(2.,12.-s*s*17.),-21.+s*12.)@ry(s*200)
        item=drops[action]
        if item in ('iron_ore','gold_ore'):r.box(m,(3.2,)*3,cube_texture(item))
        else:
            for px,py,n,col in item_rows(item):r.box(m@tr(px*.23,py*.23,0),(n*.23,.23,.24),col)
    return r.finish()

def checked_frames(route,action,jar,classpath,torch):
    steps=path(route,action)
    poses=native.sample([s.cel for s in steps],jar,classpath)
    if action in drops:
        prior=-1; touches=[]
        for k,(s,p) in enumerate(zip(steps,poses)):
            if s.cel.crack!=prior and s.cel.crack in (2,5,8):
                pts=native.pick_points(s.cel,p)
                lo=np.array(native.ORE)-8.;hi=np.array(native.ORE)+8.
                d=np.linalg.norm(np.maximum(np.maximum(lo-pts,0),pts-hi),axis=1).min()
                assert d<.25,(route,action,k,d)
                touches.append(k)
            prior=s.cel.crack
        assert len(touches)==3
    if route=="stairs":assert set(round(s.y) for s in steps).issuperset({0,8,16,24})
    static={}
    result=[draw(s,p,route,action,torch,k,static) for k,(s,p) in enumerate(zip(steps,poses))]
    # A real depth test, not a fade or an alpha mask over Steve.
    bare=draw(steps[-1],poses[-1],route,action,torch,len(steps)-1,static,actor=False)
    visible=np.any(np.asarray(result[-1])!=np.asarray(bare),axis=2).sum()
    assert visible<=8,(route,action,visible,"actor is still visible at the cut")
    return result

def work(task):
    spec,jar,cp,torch,target=task
    route,action=spec.split('-')
    frames=checked_frames(route,action,jar,cp,torch)
    image=Image.new('RGBA',(CELL_W*6,CELL_H*((FRAME_COUNT+5)//6)))
    for i,frame in enumerate(frames):
        image.paste(frame,(i%6*CELL_W,i//6*CELL_H))
    image.save(target/('motion-steve-focuscave-'+spec+'.png'),optimize=False,compress_level=5)
    # Share only the small first+middle stills with the calling process.
    return spec,len(frames)


def main():
    p=argparse.ArgumentParser()
    p.add_argument('--client',required=True,type=Path);p.add_argument('--cp',required=True)
    p.add_argument('--folder',required=True,type=Path)
    p.add_argument('--assets',type=Path)
    a=p.parse_args()
    with ZipFile(a.client) as z:
        torch=np.asarray(Image.open(io.BytesIO(z.read('assets/minecraft/textures/blocks/torch_on.png'))).convert('RGBA'))
    a.folder.mkdir(exist_ok=True,parents=True)
    if a.assets:
        with ProcessPoolExecutor(max_workers=5) as pool:
            for k,n in pool.map(work,[(spec,a.client,a.cp,torch,a.assets) for spec in CATALOG]):
                print(k,n,flush=True)
        return
    clips=[checked_frames(r,act,a.client,a.cp,torch) for r,act in zip(routes,('coal','check','iron'))]
    bg=Image.new('RGB',(870,295),'#192127')
    d=ImageDraw.Draw(bg)
    font='/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'
    def ft(n):return ImageFont.truetype(font,n,index=2)
    d.text((19,11),'史蒂夫  /  三条矿道，同一趟专注',font=ft(21),fill='#e6d7b9')
    for i,r in enumerate(routes):
        x=14+i*287
        d.rounded_rectangle((x,51,x+268,278),12,fill='#263035',outline='#464f4d')
        d.text((x+13,60),WORLDS[r].name,font=ft(15),fill='#e6d7b9')
        d.text((x+13,252),WORLDS[r].description,font=ft(12),fill='#b5bbac')
    frames=[]
    for j in range(IDLE_FIRST):
        color=bg.copy()
        for i in range(3):
            canvas=clips[i][j].crop((0,32,192,190)).resize((220,181),Image.Resampling.NEAREST)
            color.paste(canvas,(36+i*287,77),canvas)
        frames.append(color)
    palette=Image.new('RGB',(435,148*7))
    for i,k in enumerate((0,29,50,69,91,118,145)):palette.paste(frames[k].resize((435,148)),(0,148*i))
    palette=palette.quantize(colors=256)
    q=[f.quantize(palette=palette,dither=Image.Dither.NONE) for f in frames]
    clip=a.folder/'史蒂夫-三条矿道.gif'
    q[0].save(clip,save_all=True,append_images=q[1:],duration=[50]*(len(q)-1)+[450],loop=0,optimize=True)
    frames[67].save(a.folder/'三条矿道-对照.png')
    with Image.open(clip) as g:
        assert g.n_frames>=140
        print(clip.name,g.n_frames,clip.stat().st_size)
if __name__=='__main__':main()
