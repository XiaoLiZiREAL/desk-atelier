"""Check atlas completeness, transparency, distinct drawn cels and native Steve provenance."""
from pathlib import Path
import sys
import numpy as np
from PIL import Image
root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(root/"art/steve"))
from render_focus_mine import draw,CELS,sample_poses,PAIRS,pair_suffix,world_blocks,frame,LEAD,BREAK,FAR,NEAR
poses=sample_poses()
for a,b in PAIRS:
    for i in range(CELS):
        coords=world_blocks(i,a,b)
        assert coords[0]==(b,FAR)
        assert coords[1:]==([(a,NEAR)] if i<LEAD+BREAK else [])
    assert frame(0,a).x-frame(LEAD,a).x>=8
    assert frame(CELS-1,a).z-getattr(frame(LEAD+BREAK,a),"z",0.)<=-15

roles=('kurisu','rem','hitori','mutsumi','mutsumi-stage','steve')
count=0
for role in roles:
    suffixes=['','-desk','-entry']
    if role=='kurisu': suffixes+=['-result']
    settings=[(suffix,None,None) for suffix in suffixes]
    if role=='steve':
        settings=[(prefix+pair_suffix(ore,next_ore),ore,next_ore)
                  for prefix in ('','-desk') for ore,next_ore in PAIRS]
    for suffix,ore,next_ore in settings:
        path=root/'src'/'assets'/('motion-'+role+'-focusmate'+suffix+'.png')
        with Image.open(path) as strip:
            cells=CELS if role=='steve' else 6
            assert strip.mode=='RGBA' and strip.size==(1152,208*(cells//6)),path
            seen=set();centers=[];heels=[]
            for i in range(cells):
                x0,y0=i%6*192,i//6*208
                frame=strip.crop((x0,y0,x0+192,y0+208))
                a=np.asarray(frame.getchannel('A'))
                y,x=np.where(a>120)
                if role=='steve':
                    if i==CELS-1:
                        assert not np.any(a), (path,i,'camera cut must be fully clear')
                    else:
                        dy,dx=np.where(a>32)
                        assert len(dx)>100 and 2<=dx.min()<dx.max()<=189 and 2<=dy.min()<dy.max()<=205,(path,i)
                else:
                    assert 2<=x.min()<x.max()<=189 and 2<=y.min()<y.max()<=205,(path,i)
                    assert .13 < np.count_nonzero(a>120)/a.size < .61,(path,i,'expected figure and open alpha')
                bits=frame.tobytes()
                assert bits not in seen,(path,i,'duplicate cell')
                seen.add(bits)
                if role=='steve':
                    expect=draw(i,'-desk' in suffix,ore,next_ore,poses)
                    assert bits==expect.tobytes(),(path,i,'not an intact original-rig render')
                else:
                    feet=x[(y>y.max()-10) & (x>71) & (x<120)]
                    assert len(feet)>20,(path,i,'no feet near anchor')
                    centers.append(float(np.median(feet)));heels.append(int(y.max()))
                count+=1
            if centers:
                assert max(centers)-min(centers)<=6,(path,centers,'stance slides')
                assert max(heels)-min(heels)<=2,(path,heels,'foot level jumps')
print(count,'independent focus cels fit their frames; native Steve rerenders match.')
