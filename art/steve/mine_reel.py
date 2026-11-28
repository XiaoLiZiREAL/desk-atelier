"""Visual excerpt reel, driven by emitted 30-minute C# route plan (not a hand-picked fake timeline)."""
import argparse
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import re
p=argparse.ArgumentParser()
p.add_argument('--plan',type=Path,required=True);p.add_argument('--out',type=Path,required=True)
a=p.parse_args()
root=Path(__file__).resolve().parents[2]/'src'/'assets'
plan=[line.split() for line in a.plan.read_text().splitlines() if re.match(r'^\d\d:\d\d ',line)]
assert len(plan)==72
names={'torch':'火把之后','fork':'旧井岔路','stairs':'阶梯水洞'}
actions={'check':'在岔路看一看','pass':'越过普通石壁','coal':'开采煤矿','iron':'发现铁矿','gold':'发现可采的金矿',
         'lapis':'挖到青金石','red':'跟到红石矿脉','diamond':'遇到钻石矿脉','emerald':'遇到绿宝石矿脉'}
font='/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'
def ft(n):return ImageFont.truetype(font,n,index=2)
shots=[];used=set()
for k in (0,7,15,23,31,39,47,55,63,70):
    i=next((i for i in range(k,min(k+4,72)) if plan[i][1] not in used),k)
    stamp,name=plan[i];used.add(name)
    route,act=name.split('-')
    pack=Image.open(root/f'motion-steve-focuscave-{name}.png').convert('RGBA')
    still=Image.new('RGB',(485,318),'#192127');d=ImageDraw.Draw(still)
    d.text((20,13),'史蒂夫  /  三十分钟矿道行',font=ft(24),fill='#e4d8be')
    d.text((21,49),stamp+'   '+names[route]+' · '+actions[act],font=ft(14),fill='#e9c98c')
    d.rounded_rectangle((17,74,467,289),12,fill='#222e35',outline='#46504b')
    d.text((22,297),'来自同一轮计时的时点摘录 · 每段加速显示',font=ft(12),fill='#acb4a6')
    excursion=[]
    for j in range(0,180,4):
        x,y=j%6*192,j//6*208
        cel=pack.crop((x,y+34,x+192,y+190)).resize((254,207),Image.Resampling.NEAREST)
        f=still.copy();f.paste(cel,(115,78),cel);excursion.append(f)
    shots.extend(excursion)
assert len(used)>=8
sample=Image.new('RGB',(242,159*10))
for k in range(10):sample.paste(shots[k*45+20].resize((242,159)),(0,159*k))
pal=sample.quantize(colors=256)
frames=[f.quantize(palette=pal,dither=Image.Dither.NONE) for f in shots]
frames[0].save(a.out,save_all=True,append_images=frames[1:],duration=55,loop=0,optimize=True)
with Image.open(a.out) as f: assert f.n_frames>=240
print(a.out.name,a.out.stat().st_size,len(used),'unique episode combinations shown')
