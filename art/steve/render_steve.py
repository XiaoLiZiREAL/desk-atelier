"""Render the desktop Steve atlases from the unmodified Java 1.8 skin.

Requires Python 3, Pillow, NumPy. Paths are relative to this file. All props,
geometry, poses, rasterization and QA previews are produced offline.
"""
import argparse
import math
from functools import lru_cache
from pathlib import Path

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

@lru_cache(None)
def grain(kind):
    n=16;y,x=np.mgrid[:n,:n];rng=np.random.default_rng({'stone':58,'cobble':84,'oak':16,'paper':33}[kind])
    if kind=='oak':
        g=rng.integers(-7,8,(n,n))+rng.integers(-11,12,(n,1))
        base=np.array([161,116,59])
        g[y%5==0]-=16
    elif kind=='paper':
        g=rng.integers(-7,8,(n,n));base=np.array([229,208,151])
    elif kind=='stone':
        g=np.repeat(np.repeat(rng.integers(-11,12,(8,8)),2,0),2,1)+rng.integers(-6,7,(n,n))
        base=np.array([135,137,142])
    else:
        g=rng.integers(-9,9,(n,n));base=np.array([130,136,139])
        g[(y%5==0)|((x+((y//5)%2)*3)%7==0)]-=43
        g[(y%5==1)|((x+((y//5)%2)*3)%7==1)]+=13
    rgb=np.clip(base+g[...,None],0,255).astype('uint8')
    return np.dstack((rgb,np.full((n,n),255,dtype='uint8')))
def textured(kind):return {face:grain(kind) for face in FACES}

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
def pose_skin(part,section='whole',eyes='open',mouth='neutral'):
    u,v0,w,h,d=UV[part]
    regions={'right':(u,v0+d,d,h),'front':(u+d,v0+d,w,h),'left':(u+d+w,v0+d,d,h),
             'back':(u+d+w+d,v0+d,w,h),'top':(u+d,v0,w,d),'bottom':(u+d+w,v0,w,d)}
    maps={}
    for side,(a,b,c,e) in regions.items():
        tile=SKIN[b:b+e,a:a+c].copy()
        if section!='whole' and side not in ('top','bottom'):
            tile=tile[:e//2] if section=='upper' else tile[e//2:]
        maps[side]=tile
    if section=='upper':maps['bottom']=np.repeat(maps['front'][-1:,:,:],d,axis=0)
    if section=='lower':maps['top']=np.repeat(maps['front'][:1,:,:],d,axis=0)
    if part=='head' and (eyes!='open' or mouth!='neutral'):
        face=np.repeat(np.repeat(maps['front'],4,axis=0),4,axis=1)
        if eyes in ('blink','half'):
            for x in (4,20):
                face[16:(20 if eyes=='blink' else 18),x:x+8]=[167,121,96,255]
                face[18:20 if eyes=='blink' else 19,x:x+8]=[91,64,54,255]
        if mouth!='neutral':
            face[24:28,10:22]=[151,97,73,255]
            width,height={'closed':(7,1),'breath':(4,2),'ah':(9,5),'eh':(12,3),'oo':(5,4),'smile':(8,2)}[mouth]
            x0=16-width//2;y0=25-height//2
            face[y0:y0+height,x0:x0+width]=[76,45,43,255]
            if height>=3:face[y0+height-1:y0+height,x0+1:x0+width-1]=[185,103,92,255]
        maps['front']=face
    return maps

class Renderer:
    def __init__(self):
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
            px=(xyz@self.right*SCALE+96)*SS;py=(197-xyz@self.up*SCALE)*SS
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
            self.pixels[y0:y1,x0:x1][keep]=sample;target[keep]=depth[keep]
    def finish(self):
        return Image.fromarray(self.pixels).resize((W,H),Image.Resampling.LANCZOS)

def bone(render,start,end,part,section,across):
    origin=(start+end)/2;m=along_y(origin,start-end,across)
    render.box(m,(4,np.linalg.norm(end-start)+.025,4),pose_skin(part,section))

def reach(origin,target,hint):
    delta=target-origin;direction=unit(delta)
    length=np.clip(np.linalg.norm(delta),.8,11.97);tip=origin+direction*length
    perp=unit(hint-direction*np.dot(direction,hint))
    joint=origin+direction*(length/2)+perp*math.sqrt(max(0,36-(length/2)**2))
    return joint,tip

def neutral():
    return dict(yaw=-2,pitch=0,roll=0,head=(0,0,0),x=0,z=0,jump=0,rleg=(3,5,-2.1),lleg=(-2,3,2.1),
        rh=(-6.55,12.04,.5),lh=(6.5,12.04,.65),eyes='open',mouth='neutral',prop=None)

def walk(f,left):
    p=neutral();ph=2*math.pi*f/8;s=math.sin(ph);q=math.cos(ph)
    p.update(yaw=-68 if left else 44,pitch=7,roll=2*s,head=(-4,-4 if left else 4,0),
             rleg=(31*s,9+13*max(0,-q),-2),lleg=(-31*s,9+13*max(0,q),2),
             rh=(-6.25,13.25,-4.2*s+.2),lh=(6.25,13.25,4.2*s+.2))
    return p

def base_pose(row,i):
    if row in(1,2):return walk(i,row==2)
    p=neutral();t=i*math.pi/3;s=math.sin(t);c=math.cos(t)
    if row==0:p.update(pitch=.7*s,head=(1.3*s,2.0*s,.7*c),rh=(-6.55,12.05,.6+.35*s),lh=(6.5,12.06,.7-.28*s),eyes='blink' if i==4 else 'open')
    elif row==3:
        p.update(rh=[(-8,17,4),(-9,26.8,3),(-11,29,2),(-8,27.8,3)][i],head=(-2,-8,3),roll=[0,2,4,2][i],mouth='smile')
    elif row==4:
        p.update(rleg=[(26,44,-2),(1,14,-3),(38,62,-6),(3,15,-3),(30,49,-3)][i],lleg=[(28,40,2),(-9,11,4),(1,43,6),(0,17,3),(23,48,3)][i],jump=[0,1.1,2.35,1.25,0][i],
            rh=[(-6,14,4),(-8,19,3),(-11,23,2),(-9,19,3),(-6,15,3)][i],lh=[(6,14,4),(8,19,3),(11,23,2),(9,19,3),(6,15,3)][i],head=(4 if i in(0,4) else -4,0,0))
    elif row==5:
        a=[0,.35,.75,1,.96,.8,.4,.1][i];p.update(pitch=7*a,head=(21*a,-9*a,-5*a),rh=(-6+2*a,12.3+13.4*a,1+3.4*a),lh=(6.2,12.4,1+2*a),eyes='blink' if i==4 else 'half' if a>.6 else 'open')
    elif row==6:
        p.update(head=(1+3*s,-10+3*c,2),rh=(-.5,18.1+.3*s,4.2),lh=(2,21.5+.5*c,4),roll=1+1.5*s)
    elif row==7:return action_pose('focus',i)
    elif row==8:
        p.update(pitch=4+3*s,head=(11+6*s,-12+6*c,4),rh=(-1.4,23.5+.9*c,4.3),lh=(.4,17.3+.5*s,3.6))
    else:
        angle=(i+(8 if row==10 else 0))*math.pi/8
        p.update(head=(-18*math.cos(angle),29*math.sin(angle),-2.3*math.sin(angle)))
    return p

def action_pose(name,i):
    p=neutral();p['prop']=name;p['key']=i
    if name=='tea':
        p.update(rh=[(-6.6,13.2,2.0),(-5.4,17.4,4.5),(-4.7,21.5,5.4),(-3.15,24.1,5.45),(-4.8,20,4.8),(-6.4,13.6,2.3)][i],
                 lh=[(6.2,12.3,.7),(4,15.5,3),(3.5,18.4,3.8),(4.5,17,3.6),(4,15.4,3),(6.2,12.4,1)][i],
                 head=[(8,-13,-2),(6,-8,0),(2,-6,0),(-8,-4,2),(3,-7,0),(0,-3,0)][i],eyes='blink' if i==3 else 'open')
    elif name=='game':
        p.update(rh=[(-4.2,17.2,4.6),(-2.8,19.3,5),(-2,18.5,4.5),(-6.6,15,-3.4),(-6.5,14.2,-3.5),(-7.4,21.5,5.4)][i],
            lh=[(5.4,14,2),(2.7,18.6,4.4),(1.1,19,5),(6.9,17.1,4.4),(1.8,23.4,4.5),(13.6,17.9,4.9)][i],
            head=[(10,-12,0),(17,-16,-4),(8,3,0),(-2,16,1),(4,14,6),(-3,-9,0)][i],pitch=[1,9,4,0,5,-2][i])
    elif name=='song':
        p.update(rh=[(-6.2,12.3,1),(-3.4,18.5,3),(-1.8,21,3.5),(-10.3,21.2,5),(-4.1,19.7,4.4),(-6,13.7,2)][i],
            lh=[(6.2,12.4,.6),(6.4,13,2),(7,15.3,3.1),(7.8,17.8,4.9),(5.2,16.4,4),(6.2,12.5,1)][i],
            head=[(1,0,0),(-2,-4,-2),(-5,-9,-3),(-8,4,4),(-3,9,2),(2,3,-1)][i],roll=[0,0,-2,3,1,0][i],
            mouth=['closed','breath','ah','eh','oo','closed'][i],eyes='blink' if i==3 else 'open')
    elif name=='focus':
        p.update(rh=[(-5.5,14,2),(-3.9,18,4.8),(-3.9,19.2,5.5),(-1,23.5,4.5),(-1.8,19.2,5.6),(-5.4,14.3,2.8)][i],
            lh=[(5.7,14.4,2.3),(4.9,18,4.8),(4.9,18.5,5),(4.7,18,4.8),(4.6,18.7,5.1),(5.6,14.4,2.3)][i],
            head=[(10,10,0),(15,2,0),(19,-4,1),(-8,17,2),(15,6,-3),(1,-3,0)][i],pitch=[1,3,5,0,5,0][i])
    elif name=='rest':
        p.update(x=[-1.8,1.4,4.1,4.1,3.4,-1.1][i],z=[0,-1,-1.9,-1.9,-1.5,0][i],yaw=[11,31,48,48,32,7][i],
            rleg=[(1,0,-2),(44,75,-2),(66,69,-3),(66,69,-3),(55,81,-2),(8,13,-2)][i],
            lleg=[(2,0,2),(40,66,2),(64,66,3),(64,66,3),(52,78,2),(8,13,2)][i],
            pitch=[0,13,3,-1,21,4][i],head=[(8,20,0),(5,12,0),(1,-8,0),(6,-7,4),(-5,2,0),(0,-4,0)][i],
            rh=[(-6.3,12.3,1),(-6.4,12.7,2.7),(1.4,18.3,5),(1.9,18,4.7),(-5.5,13.2,4),(-6.2,12.4,1)][i],
            lh=[(7.1,13.9,-2.5),(6.7,13,-1.6),(-.8,17,5.7),(-.8,16.8,5.5),(5.8,13,-1.5),(7,13.3,-2)][i],eyes='blink' if i==3 else 'open')
    elif name=='mine':
        p.update(x=[.5,.5,0,-.3,-2.5,0][i],yaw=[-8,-15,-11,-16,-65,-2][i],
            rleg=[(10,17,-5),(8,14,-5),(25,44,-6),(39,59,-7),(74,113,-4),(7,11,-2)][i],
            lleg=[(4,9,4),(2,4,4),(13,25,4),(27,46,4),(83,118,4),(4,8,2)][i],
            roll=[8,3,12,17,-19,2][i],pitch=[8,-3,17,22,54,0][i],
            head=[(11,-19,-3),(-2,-16,1),(14,-17,0),(19,-20,0),(-13,23,1),(3,7,0)][i],
            rh=[(-6.8,15.3,3.2),(-8.9,27.8,1.8),(-8.6,19.7,4),(-6,13.4,3.1),(-7.3,14.9,-1),(-6.6,14.3,2.7)][i],
            lh=[(6.5,14.4,3.2),(5.6,18.9,3.4),(6.1,17.4,3.1),(6.5,16.8,2.7),(1.1,15.5,4.6),(5.6,17.4,4)][i])
    return p

def leg_mats(p):
    result={}
    for part,s,angles in [('rleg',-1,p['rleg']),('lleg',1,p['lleg'])]:
        thigh,knee,spread=angles
        m=tr(s*2.06,0,0)@rz(spread)@rx(-thigh)
        shin=m@tr(0,-6,0)@rx(knee)
        result[part]=(m@tr(0,-3,0),shin@tr(0,-3,0))
    return result

@lru_cache(None)
def map_art():
    m=np.array(Image.fromarray(grain('paper')).resize((24,20),Image.Resampling.NEAREST))
    img=Image.fromarray(m);d=ImageDraw.Draw(img)
    d.polygon([(2,4),(7,3),(11,6),(14,4),(18,5),(20,8),(17,11),(22,12),(21,17),(15,16),(11,13),(6,15),(3,11)],fill='#81984f')
    d.line([(4,15),(8,12),(9,9),(13,8),(17,5)],fill='#408b99',width=2)
    d.line([(15,10),(18,13)],fill='#ad4836',width=2);d.line([(18,10),(15,13)],fill='#ad4836',width=2)
    return np.asarray(img)
@lru_cache(None)
def rock_art(crack):
    maps=textured('stone')
    if crack:
        for face in ('top','right','front'):
            img=Image.fromarray(maps[face]);d=ImageDraw.Draw(img)
            for path in [[(8,6),(7,8),(9,10),(6,13),(7,15)],[(8,6),(10,5),(12,7),(15,6)],[(8,6),(6,4),(4,4),(3,1)],[(8,9),(11,10),(12,12)]]:
                d.line(path,fill='#4d5055',width=1)
            maps[face]=np.asarray(img)
    return maps

def draw_stool(r):
    wood=textured('oak');cx,cz=4.25,-2.1
    r.box(tr(cx,6.65,cz),(9.4,1.25,8.1),wood)
    for x in(-3.7,3.7):
        for z in(-3.1,3.1):r.box(tr(cx+x,3.05,cz+z),(1.36,6.1,1.36),wood)
    for x in(-3.7,3.7):r.box(tr(cx+x,2.25,cz),(1,1,6.4),wood)

def draw_bottle(r,hand,axis):
    mat=along_y(hand,axis)
    r.box(mat@tr(0,.05,0),(2.55,2.2,2.1),'3d87b0')
    r.box(mat@tr(0,.55,0),(3.17,1.8,2.3),'4b9bbf')
    r.box(mat@tr(0,1.5,0),(2.28,.72,1.8),'88c6cf')
    r.box(mat@tr(0,2.7,0),(.95,1.25,1.03),'addbdb')
    r.box(mat@tr(0,3.32,0),(1.4,.4,1.28),'c3e4da')
    r.box(mat@tr(-.75,.54,1.17),(.48,1.4,.14),'a6e1e6')

def draw_emerald(r,hand):
    m=tr(*hand)@tr(-.44,.88,1.88)@ry(-12)
    for y,w,h,col in [(-1.25,1.55,.7,'298b57'),(-.65,2.35,.75,'31ac69'),(.12,2.85,.95,'38c785'),(.86,2.3,.65,'67dc9c'),(1.38,1.45,.49,'91e8ad')]:
        r.box(m@tr(0,y,0),(w,h,1.35),col)
    r.box(m@tr(-.54,.37,.7),(.48,1.05,.13),'baedbd')

PICK_HEAD=[(-3,4),(-3,5),(-2,5),(-2,6),(-1,6),(0,6),(1,6),(2,6),(2,5),(3,5),(3,4)]
PICK_TIP=v(-3.45,3.55,.13)*1.14
def draw_pick(r,m):
    size=1.14
    r.box(m@tr(0,2.2*size,0),(1.02*size,7.1*size,1.05*size),'976833')
    r.box(m@tr(-.10,2.0*size,.54*size),(.32*size,6.5*size,.15*size),'bb894a')
    for x,y in PICK_HEAD:
        col='b8c9c7' if y>=6 else ('8eaaa9' if y==5 else '698784')
        r.box(m@tr(x*size,y*size,0),(size+.016,size+.016,1.42*size),col)

def render(p):
    r=Renderer();legs=leg_mats(p)
    floor=min((corners(4,6,4)@m[:3,:3].T+m[:3,3])[:,1].min() for pair in legs.values() for m in pair)
    pelvis=tr(p['x'],-floor+p['jump'],p['z'])@ry(p['yaw'])
    for part,pair in legs.items():
        for m,section in zip(pair,('upper','lower')):r.box(pelvis@m,(4,6.015,4),pose_skin(part,section))
    body=pelvis@rz(p['roll'])@rx(p['pitch'])@tr(0,-12,0)
    r.box(body@tr(0,18,0),(8,12,4),pose_skin('body'))
    pitch,yaw,roll=p['head'];head=body@tr(0,24,0)@ry(yaw)@rz(roll)@rx(pitch)
    r.box(head@tr(0,4,0),(8,8,8),pose_skin('head','whole',p['eyes'],p['mouth']))
    kind=p['prop'];k=p.get('key',0);hands={}
    mine_angle=[13,35,56,96,-46,15][k];tool_rot=rz(mine_angle)@ry(7)
    impact=v(-10.1,6.72,1.3)
    for s,part,code in [(-1,'rarm','rh'),(1,'larm','lh')]:
        shoulder=v(6*s,24,0);target=v(p[code])
        if kind=='mine' and k==3 and s==-1:
            grip=impact-tool_rot[:3,:3]@PICK_TIP;target=point(np.linalg.inv(body),grip)
        if kind=='mine' and k==4 and s==1:
            target=point(np.linalg.inv(body),v(-9.45,4.25,4.1))
        elbow,tip=reach(shoulder,target,v(s*.84,-1,-.22))
        bone(r,point(body,shoulder),point(body,elbow),part,'upper',body[:3,0])
        bone(r,point(body,elbow),point(body,tip),part,'lower',body[:3,0])
        joint=along_y(point(body,elbow),body[:3,:3]@unit(shoulder-tip),body[:3,0])
        r.box(joint,(3.85,2.2,3.85),pose_skin(part,'lower'))
        hands[code]=point(body,tip)
    rh,lh=hands['rh'],hands['lh']
    if kind=='tea':
        mouth=point(head,v(0,1.7,4.15))
        rh=rh+v(-.9,.14,1.85)
        axis=unit(mouth-rh) if k==3 else unit(v([.12,.25,.48,.76,.40,.08][k],1,[-.07,-.10,-.28,-.55,-.14,-.07][k]))
        if k==3:
            # Place the neck against the mouth with the palm surrounding the bottle body.
            rh=mouth-axis*3.49
        draw_bottle(r,rh,axis)
    elif kind=='game':draw_emerald(r,rh)
    elif kind=='focus':
        w=[2.2,6.1,8.2,7.6,7.8,2.05][k];center=lh+v(-w*.42,.47,.6)
        m=tr(*center)@ry(-7)@rx([-7,-12,-17,-15,-17,-8][k])
        mat=textured('paper');mat['front']=map_art()
        r.box(m,(w,5,.33),mat)
        for x in(-1,1):r.box(m@tr(x*w/2,0,0),(.5,5.2,.65),'c8a566')
    elif kind=='rest':draw_stool(r)
    elif kind=='mine':
        if k<=3:r.box(tr(-10.3,3.35,.55),(6.7,6.7,6.7),rock_art(k==3))
        if k==4:
            for x,y,z,w in [(-9.9,3.6,5.25,2.5),(-12.0,.65,2.5,1),(-8.25,.68,3.3,.9),(-11.8,.55,-.35,.75)]:
                r.box(tr(x,y,z)@ry(17),(w,w,w),textured('cobble'))
        if k==5:r.box(tr(*(lh+v(0,1.3,.8)))@ry(19),(2.8,2.8,2.8),textured('cobble'))
        draw_pick(r,tr(*rh)@tool_rot)
    return r.finish()

def pngframe(image,path):
    image.save(path,optimize=True)
def build(out,qa):
    out.mkdir(parents=True,exist_ok=True)
    atlas=Image.new('RGBA',(W*8,H*11));counts=[6,8,8,4,5,8,6,6,6,8,8]
    for row,count in enumerate(counts):
        for col in range(count):
            img=render(base_pose(row,col));atlas.alpha_composite(img,(col*W,row*H))
            if row==0 and col==0:pngframe(img.crop(img.getbbox()),out/'steve-still.png')
    pngframe(atlas,out/'steve.png')
    sheets={}
    for name in ('tea','game','song','focus','rest','mine'):
        strip=Image.new('RGBA',(W*6,H));frames=[]
        for i in range(6):
            img=render(action_pose(name,i));strip.alpha_composite(img,(i*W,0));frames.append(img)
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
        base=Image.new('RGB',atlas.size,'#e9eddf');base.paste(atlas,(0,0),atlas);base.thumbnail((930,1380));base.save(qa/'base.png')
    for path in sorted(out.glob('*steve*.png')):print(path.name,Image.open(path).size)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--out',type=Path,default=HERE.parent/'src'/'assets');parser.add_argument('--qa',type=Path)
    args=parser.parse_args();build(args.out,args.qa)
