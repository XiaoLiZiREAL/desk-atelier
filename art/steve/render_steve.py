"""Render the desktop Steve atlases from the unmodified Java 1.8 skin.

Requires Python 3, Pillow, NumPy. Paths are relative to this file. All props,
geometry, poses, rasterization and QA previews are produced offline.
"""
import argparse
import json
import math
from functools import lru_cache
from pathlib import Path
from hashlib import sha256
from classic_scenes import ACTIONS, COUNTS, action, base, input_text

import numpy as np
from PIL import Image, ImageDraw

HERE=Path(__file__).resolve().parent
SKIN=np.asarray(Image.open(HERE/'skin.png').convert('RGBA'))
W,H=192,208
SS=2
SCALE=5.52
I=np.eye(4)

def v(a,b=None,c=None):
    return np.array(a if b is None else [a,b,c],dtype=float)
def tr(x=0,y=0,z=0):
    m=I.copy();m[:3,3]=(x,y,z);return m
def rot(axis,angle):
    s,c=math.sin(math.radians(angle)),math.cos(math.radians(angle))
    m=I.copy();a,b=((1,2),(2,0),(0,1))[axis]
    m[a,a]=m[b,b]=c;m[b,a]=s;m[a,b]=-s;return m
def rx(a):return rot(0,a)
def ry(a):return rot(1,a)
def rz(a):return rot(2,a)
def point(mat,p):return mat[:3,:3]@p+mat[:3,3]
def unit(p):return p/max(1e-8,np.linalg.norm(p))
def along_y(origin,direction,right=None):
    y=unit(direction);right=v(1,0,0) if right is None else right
    if abs(np.dot(y,right))>.965:right=v(0,0,1) if abs(y[2])<.9 else v(0,1,0)
    x=unit(right-y*np.dot(right,y));z=np.cross(x,y)
    f=tr(*origin);f[:3,:3]=np.column_stack((x,y,z));return f
def corners(w,h,d):
    return np.array([[a*w/2,b*h/2,c*d/2] for a in(-1,1)for b in(-1,1)for c in(-1,1)])

@lru_cache(None)
def pixel(color):
    if isinstance(color,str):color=tuple(bytes.fromhex(color.lstrip('#')))
    return np.array([[(*color,255)]],dtype='uint8')

# Model space is right-handed. +Y is up and +Z is the skin's front.
# For each face store the texture's top-left, top-right, bottom-left corners.
FACES={
    'front': ((-1,1,1),(1,1,1),(-1,-1,1),(0,0,1)),
    'back': ((1,1,-1),(-1,1,-1),(1,-1,-1),(0,0,-1)),
    'right': ((-1,1,-1),(-1,1,1),(-1,-1,-1),(-1,0,0)),
    'left': ((1,1,1),(1,1,-1),(1,-1,1),(1,0,0)),
    'top': ((-1,1,-1),(1,1,-1),(-1,1,1),(0,1,0)),
    'bottom': ((-1,-1,1),(1,-1,1),(-1,-1,-1),(0,-1,0))
}
UV={'head':(0,0,8,8,8),'body':(16,16,8,12,4),'rarm':(40,16,4,12,4),'larm':(32,48,4,12,4),
    'rleg':(0,16,4,12,4),'lleg':(16,48,4,12,4)}
@lru_cache(None)
def pose_skin(part):
    u,v0,w,h,d=UV[part]
    regions={'right':(u,v0+d,d,h),'front':(u+d,v0+d,w,h),'left':(u+d+w,v0+d,d,h),
             'back':(u+d+w+d,v0+d,w,h),'top':(u+d,v0,w,d),'bottom':(u+d+w,v0,w,d)}
    maps={}
    for side,(a,b,c,e) in regions.items():
        tile=SKIN[b:b+e,a:a+c].copy()
        maps[side]=tile
    return maps

class Renderer:
    def __init__(self,scale=SCALE,baseline=197):
        self.scale=scale
        self.baseline=baseline
        a,b=map(math.radians,(-23,10))
        self.right=v(math.cos(a),0,-math.sin(a))
        self.up=v(-math.sin(a)*math.sin(b),math.cos(b),-math.cos(a)*math.sin(b))
        self.view=v(math.sin(a)*math.cos(b),math.sin(b),math.cos(a)*math.cos(b))
        self.light=unit(v(-.35,.65,1))
        self.pixels=np.zeros((H*SS,W*SS,4),dtype='uint8')
        self.depth=np.full((H*SS,W*SS),-1e8)
    def box(self,mat,size,material):
        half=v(size)/2
        for side,(a,b,c,n) in FACES.items():
            normal=mat[:3,:3]@v(n)
            if np.dot(normal,self.view)<.001:continue
            xyz=np.array([point(mat,half*v(p)) for p in (a,b,c)])
            px=(xyz@self.right*self.scale+96)*SS;py=(self.baseline-xyz@self.up*self.scale)*SS
            xy=np.column_stack((px,py));ab,ac=xy[1]-xy[0],xy[2]-xy[0]
            p4=xy[0]+ab+ac
            x0=max(0,math.floor(min(px.min(),p4[0])));x1=min(W*SS,math.ceil(max(px.max(),p4[0])))
            y0=max(0,math.floor(min(py.min(),p4[1])));y1=min(H*SS,math.ceil(max(py.max(),p4[1])))
            if x1<=x0 or y1<=y0:continue
            d=ab[0]*ac[1]-ab[1]*ac[0]
            if abs(d)<1e-6:continue
            yy,xx=np.ogrid[y0:y1,x0:x1];dx=xx+.5-xy[0,0];dy=yy+.5-xy[0,1]
            s=(dx*ac[1]-dy*ac[0])/d;t=(ab[0]*dy-ab[1]*dx)/d
            z=xyz@self.view;depth=z[0]+s*(z[1]-z[0])+t*(z[2]-z[0])
            target=self.depth[y0:y1,x0:x1]
            keep=(s>=0)&(t>=0)&(s<=1)&(t<=1)&(depth>target+1e-5)
            if not np.any(keep):continue
            tex=material[side] if isinstance(material,dict) else pixel(material)
            h,w=tex.shape[:2]
            sample=tex[np.clip((t[keep]*h).astype(int),0,h-1),np.clip((s[keep]*w).astype(int),0,w-1)].copy()
            lit=.73+.32*max(0,np.dot(normal,self.light))
            sample[:,:3]=np.clip(sample[:,:3]*lit,0,255).astype('uint8')
            visible=sample[:,3]>0
            rows,cols=np.nonzero(keep)
            rows,cols=rows[visible],cols[visible]
            self.pixels[y0:y1,x0:x1][rows,cols]=sample[visible]
            target[rows,cols]=depth[rows,cols]
    def finish(self):
        return Image.fromarray(self.pixels).resize((W,H),Image.Resampling.LANCZOS)


@lru_cache(None)
def texture(name):
    with Image.open(HERE/'items'/(name+'.png')) as image:
        pic=image.convert('RGBA')
    if name=='leather_boots':
        pixels=np.array(pic)
        pixels[:,:,:3]=(pixels[:,:,:3].astype(float)*[.63,.40,.27]).astype('uint8')
        pic=Image.fromarray(pixels)
        with Image.open(HERE/'items/leather_boots_overlay.png') as overlay:
            pic.alpha_composite(overlay.convert('RGBA'))
    if name=='potion_bottle_drinkable':
        with Image.open(HERE/'items/potion_overlay.png') as overlay:
            fill=np.array(overlay.convert('RGBA'))
        fill[:,:,:3]=(fill[:,:,:3].astype(float)*[.22,.40,.83]).astype('uint8')
        liquid=Image.fromarray(fill)
        liquid.alpha_composite(pic)
        pic=liquid
    return np.array(pic)

def cube_texture(name):
    return {face:texture(name) for face in FACES}

@lru_cache(None)
def item_rows(name):
    tex=texture(name)
    runs=[]
    for y,row in enumerate(tex):
        x=0
        while x<len(row):
            end=x+1
            while end<len(row) and np.array_equal(row[x],row[end]):
                end+=1
            if row[x,3]:
                runs.append(((x+end)/2-8,7.5-y,end-x,tuple(map(int,row[x,:3]))))
            x=end
    return runs

@lru_cache(None)
def load_poses():
    rows=(HERE/'poses-1.8.txt').read_text(encoding='ascii').splitlines()
    if not rows[0].endswith(sha256(input_text().encode('ascii')).hexdigest()):
        raise ValueError('Scene inputs changed. Regenerate the Java 1.8 model samples.')
    poses={}
    for row in rows[1:]:
        key,*numbers=row.split()
        poses[key]=np.asarray(numbers,dtype=float).reshape(6,6)
    return poses

PARTS=('head','body','rarm','larm','rleg','lleg')
# The vanilla arm boxes start two texels above their shoulder pivots and sit
# one texel to the outside. Legs start exactly at the hip pivot.
CENTRES=((0,4,0),(0,-6,0),(-1,-4,0),(1,-4,0),(0,-6,0),(0,-6,0))
SIZES=((8,8,8),(8,12,4),(4,12,4),(4,12,4),(4,12,4),(4,12,4))

def part_matrices(key,frame):
    root=tr(frame.x,24+frame.lift-(3 if frame.sneak else 0),0)@ry(frame.turn)
    matrices=[]
    for x,y,z,pitch,yaw,roll in load_poses()[key]:
        a,b,c=np.degrees((pitch,-yaw,-roll))
        matrices.append(root@tr(x,-y,-z)@rz(c)@ry(b)@rx(a))
    return matrices

def segment(renderer,start,end,width,color):
    delta=end-start
    renderer.box(along_y((start+end)*.5,delta),(width,np.linalg.norm(delta),width),color)

def rod_line(renderer,tip,step):
    bob=v((-15,-14.5,-15)[step-1],(2.3,1.55,.65)[step-1],3.2)
    points=[]
    for i in range(9):
        t=i/8
        points.append(tip*(1-t)+bob*t-v(0,2.6*t*(1-t),0))
    for a,b in zip(points,points[1:]):
        segment(renderer,a,b,.20,'46423a')
    renderer.box(tr(*bob),(.95,1.05,.95),'ad3637')
    renderer.box(tr(*(bob+v(0,.64,0))),(.82,.44,.82),'eee3d1')

def held_item(renderer,frame,arm):
    name=frame.item
    if not name:
        return
    # LayerHeldItem offset in model texels, followed by RenderItem's Y-X-Z
    # display rotation. rx(180) maps vanilla's down/back coordinates to up/front.
    view=item_displays()[name]
    pitch,yaw,roll=view['rotation']
    tx,ty,tz=view['translation']
    item=arm@tr(-1,-7,-1)@rx(180)@tr(2*tx,2*ty,2*tz)@ry(yaw)@rx(pitch)@rz(roll)
    sx,sy,sz=view['scale']
    if name in ('dirt','cobblestone'):
        renderer.box(item,(16*sx,16*sy,16*sz),cube_texture(name))
        return
    for x,y,n,color in item_rows(name):
        renderer.box(item@tr(x*sx,y*sy,0),(n*sx,sy,sz),color)
    if name=='fishing_rod_cast':
        rod_line(renderer,point(item,v(sx*6.0,sy*6.0,0)),frame.step)

@lru_cache(None)
def item_displays():
    return json.loads((HERE/'items/display.json').read_text())

def environment(renderer,frame):
    step=frame.step
    if frame.scene in ('propbridge','toolbridge') and step>=4:
        name='iron_pickaxe' if frame.scene=='toolbridge' else 'potion_bottle_drinkable'
        # A dropped inventory item uses the same texels as its hand-held counterpart.
        # For one beat it travels down from the right hand before resting on the ground.
        mat=tr(-9 if step==4 else -12,6 if step==4 else 2.6,5)@ry(-13)@rx(-64)@rz(-28)
        for x,y,width,color in item_rows(name):
            renderer.box(mat@tr(x*.46,y*.46,0),(width*.46,.46,.36),color)
    if frame.scene.startswith("project"):
        soil=cube_texture('dirt')
        if frame.scene!='projectstart' or step>=2:
            soil['top']=texture('farmland_dry' if frame.scene=='projectstart' else 'farmland_wet')
        plot=tr(-10,0,3)
        renderer.box(plot,(13,3,13),soil)
        stages={'projectstart':(-1,-1,-1,-1,0,0),'projectwork':(0,0,0,3,3,3),
                'projectfinish':(7,7,-1,-1,0,0)}
        stage=stages[frame.scene][step]
        if stage>=0:
            leaves=cube_texture('wheat_stage_'+str(stage))
            for angle in (45,-45):
                renderer.box(plot@tr(0,8,0)@ry(angle),(13,13,.12),leaves)
    if frame.scene=='pillar':
        count=(0,1,2,2,1,0)[step]
        for layer in range(count):
            material=cube_texture('dirt')
            if step in (3,4) and layer==count-1:
                material=cracked('dirt')
            renderer.box(tr(0,8+layer*16,0),(16,16,16),material)
    if frame.scene=='mine':
        if step<4:
            renderer.box(tr(-11.5,8,4.5),(16,16,16),
                         cracked('stone') if step>=2 else cube_texture('stone'))
        elif step==4:
            renderer.box(tr(-7,1.75,4.5)@ry(25),(3,3,3),cube_texture('cobblestone'))

@lru_cache(None)
def cracked(name):
    base=Image.fromarray(texture(name))
    cracks=Image.fromarray(texture('destroy_stage_7'))
    base.alpha_composite(cracks)
    return {face:np.asarray(base) for face in FACES}

def render(key,frame):
    camera=(5.15,3.66,2.85,2.85,3.68,5.15)[frame.step] if frame.scene=='pillar' else SCALE
    renderer=Renderer(4.45,189) if frame.scene in ('mine','toolbridge') else Renderer(camera)
    if frame.scene.startswith("project"):
        renderer=Renderer(4.45,181)
    transforms=part_matrices(key,frame)
    for part,pivot,center,size in zip(PARTS,transforms,CENTRES,SIZES):
        renderer.box(pivot@tr(*center),size,pose_skin(part))
    environment(renderer,frame)
    held_item(renderer,frame,transforms[2])
    return renderer.finish()

def pngframe(image,path):
    image.save(path,optimize=True)
def build(out,qa):
    out.mkdir(parents=True,exist_ok=True)
    atlas=Image.new('RGBA',(W*8,H*11));counts=COUNTS
    for row,count in enumerate(counts):
        for col in range(count):
            img=render(f'base.{row}.{col}',base(row,col));atlas.alpha_composite(img,(col*W,row*H))
            if row==0 and col==0:pngframe(img.crop(img.getbbox()),out/'steve-still.png')
    pngframe(atlas,out/'steve.png')
    sheets={}
    for name in ACTIONS:
        strip=Image.new('RGBA',(W*6,H));frames=[]
        for i in range(6):
            img=render(f'{name}.{i}',action(name,i));strip.alpha_composite(img,(i*W,0));frames.append(img)
        sheets[name]=frames;pngframe(strip,out/('motion-steve-'+name+'.png'))
    if qa:
        qa.mkdir(parents=True,exist_ok=True)
        contact=Image.new('RGB',(W*6,(H+23)*len(sheets)),'#e9eddf')
        draw=ImageDraw.Draw(contact)
        for row,(name,frames) in enumerate(sheets.items()):
            y=row*(H+23);draw.rectangle((0,y,contact.width,y+22),fill='#314c43');draw.text((9,y+4),name.upper(),fill='#f7f7e9')
            anim=[]
            for j,img in enumerate(frames):
                contact.paste(img,(j*W,y+23),img)
                board=Image.new('RGB',(W,H),'#e9eddf');board.paste(img,(0,0),img);anim.append(board)
            anim[0].save(qa/(name+'.gif'),save_all=True,append_images=anim[1:],duration=[250,220,280,550,250,380],loop=0)
        contact.save(qa/'contact.png')
        overview=Image.new('RGB',atlas.size,'#e9eddf');overview.paste(atlas,(0,0),atlas);overview.thumbnail((930,1380));overview.save(qa/'base.png')
    for path in sorted(out.glob('*steve*.png')):print(path.name,Image.open(path).size)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--out',type=Path,default=HERE.parent.parent/'src'/'assets');parser.add_argument('--qa',type=Path)
    args=parser.parse_args();build(args.out,args.qa)
