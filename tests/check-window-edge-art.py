"""Verify native atlases, contacts, no clipping and genuine PNG padding."""
from pathlib import Path
from PIL import Image
from hashlib import sha256
import numpy as np
from scipy.ndimage import label
assets=Path(__file__).resolve().parents[1]/'src/assets'
registered=0
rem_centers=[]
for animation in ('window-edge','window-scene'):
 for role in ('hitori','rem','kurisu','mutsumi','mutsumi-stage','steve'):
  n=(16 if animation=='window-edge' else 85) if role=='steve' else 6
  a=Image.open(assets/f'{animation}-{role}.png').convert('RGBA')
  assert a.size==(192*7,208*((n+6)//7))
  distinct=set()
  for i in range(7*((n+6)//7)):
   x,y=i%7*192,i//7*208
   c=a.crop((x,y,x+192,y+208)); alpha=c.getchannel('A')
   if i>=n:
    assert alpha.getbbox() is None
    continue
   distinct.add(sha256(c.tobytes()).digest())
   mask=alpha.point(lambda x:255 if x>128 else 0)
   p=mask.getbbox()
   assert p and min(p[0],p[1],192-p[2],208-p[3])>=2,(animation,role,i,p)
   if animation=='window-edge':
    assert mask.crop((79,128,113,138)).getbbox() and mask.crop((65,146,127,176)).getbbox()
    if role=='rem':
     for y in range(153,172):
      row=mask.crop((0,y,192,y+1)).getbbox()
      assert row
      rem_centers.append((row[0]+row[2]-1)/2)
   else:
    assert 169<=p[3]<=196,(role,i,p)
  assert len(distinct)>=(65 if animation=='window-scene' and role=='steve' else n)
  registered+=n
assert max(rem_centers)-min(rem_centers)<=2,rem_centers
print(registered,'native window-edge and vignette cels verified')
for role in ('hitori','rem','kurisu','mutsumi','mutsumi-stage','steve'):
 a=Image.open(assets/f'window-rest-{role}.png').convert('RGBA')
 assert a.size==(1344,208)
 seen=set()
 for i in range(6):
    c=a.crop((i*192,0,(i+1)*192,208));mask=c.getchannel('A').point(lambda x:255 if x>127 else 0)
    p=mask.getbbox()
    assert p and min(p[0],p[1],192-p[2],208-p[3])>=2
    assert mask.crop((75,128,117,139)).getbbox()
    assert mask.crop((64,146,128,183)).getbbox()
    seen.add(sha256(c.tobytes()).digest())
 assert len(seen)==6,role
print('36 verified quiet window cels on stable seat anchors')
for role in ('hitori','rem','kurisu','mutsumi','mutsumi-stage'):
 for kind,n in (('lounge',28),('swing',4)):
    a=Image.open(assets/f'window-{kind}-{role}.png').convert('RGBA')
    assert a.size==(1344,208*((n+6)//7))
    seen=set()
    still=[]
    shoes=[]
    for i in range(n):
      x,y=i%7*192,i//7*208
      c=a.crop((x,y,x+192,y+208))
      mask=c.getchannel('A').point(lambda v:255 if v>127 else 0)
      b=mask.getbbox()
      assert b and min(b[0],b[1],192-b[2],208-b[3])>=2,(role,kind,i,b)
      assert mask.crop((44,121,148,136)).getbbox(),(role,kind,i,'missing contact')
      if kind=='lounge' and (i in (1,2,5,6,7,25,26) or (i==24 and role!='mutsumi') or 16<=i<=21):assert 128<=b[3]<=139,(role,i,b)
      if kind=='swing':
        assert b[3]>155
        t=np.asarray(mask)>0
        still.append(t[:127])
        shoes.append(t[150:])
        if i in (0,1,2,3):
          pieces,_=label(t[155:])
          assert 1<=sum(area>15 for area in np.bincount(pieces.ravel())[1:])<=2,(role,i,'detached or additional shoe shapes')
      if kind=='lounge' and role=='mutsumi-stage' and i in (18,19,20,21):
        # Low forearms, chest, pelvis and thighs all stay in contact even
        # with the feet up. No return to the raised-hip silhouette.
        for x0 in (20,55,80,104,128):
          assert mask.crop((x0,125,x0+12,135)).getbbox(),(role,i,x0,'body lifted off the sill')
      if kind=='lounge' and (i in (19,20,21) if role!='mutsumi-stage' else i==20):
        rest_cel=18 if role=='mutsumi-stage' else 17
        a0=a.crop((rest_cel%7*192,rest_cel//7*208,(rest_cel%7+1)*192,(rest_cel//7+1)*208)).getchannel('A')
        footband=(112,55,192,118)
        assert np.asarray(mask.crop(footband)).sum()-float(np.asarray(a0.crop(footband)).sum())>255*90,(role,i,'feet not lifted')
      seen.add(sha256(c.tobytes()).digest())
    assert len(seen)==n,(role,kind,len(seen))
    if kind=='swing':
      original=Image.open(assets/f'window-rest-{role}.png').convert('RGBA').crop((0,0,192,208))
      neutral=np.asarray(original.getchannel('A'))>128
      for i,top in enumerate(still):
        assert (top&neutral[:127]).sum()/(top|neutral[:127]).sum()>=.98,(role,i,'torso drift crossing from actual neutral')
      for j in range(2):
        region=slice(0,96) if j==0 else slice(96,192)
        other=slice(96,192) if j==0 else slice(0,96)
        ease,peak=[a^neutral[150:] for a in shoes[j*2:j*2+2]]
        assert ease[:,region].sum()>=85 and peak[:,region].sum()>ease[:,region].sum()+50,(role,j,'no graduated toe swing')
        assert ease[:,other].sum()<=45 and peak[:,other].sum()<=45,(role,j,'the other shoe moved')
assert not (assets/'window-lounge-steve.png').exists() and not (assets/'window-swing-steve.png').exists()
print('140 lying/prone and transition cels, 20 individually drawn seated easing cels verified')
