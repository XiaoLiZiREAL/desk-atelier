"""A short visual check of quiet cels and the exact last/first frames of a cave cut."""
from pathlib import Path
import sys
from PIL import Image,ImageDraw,ImageFont

assets=Path(__file__).resolve().parents[2]/'src/assets'
out=Path(sys.argv[1])
font='/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'
def ft(n): return ImageFont.truetype(font,n,index=2)
packs={key:Image.open(assets/('motion-steve-focuscave-'+key+'.png')).convert('RGBA')
       for key in ('torch-coal','fork-check','stairs-iron')}
stills=[];timings=[]
def segment(key,cels,caption,ms):
    bg=Image.new('RGB',(500,322),'#192127');d=ImageDraw.Draw(bg)
    d.text((18,10),'史蒂夫  /  休整与换洞',font=ft(23),fill='#eedfc4')
    d.text((19,45),caption,font=ft(15),fill='#dcbf81')
    d.rounded_rectangle((17,74,482,295),10,fill='#242f34',outline='#465149')
    d.text((19,303),'动作摘录 · 换洞处保留前后原始画帧',font=ft(11),fill='#b6bba7')
    for i in cels:
        x,y=(i%6)*192,(i//6)*208
        cut=packs[key].crop((x,y+22,x+192,y+198)).resize((230,211),Image.Resampling.NEAREST)
        im=bg.copy();im.paste(cut,(136,78),cut);stills.append(im);timings.append(ms)
segment('torch-coal',range(180,196),'休整一：抬头看洞顶',75)
segment('torch-coal',range(196,212),'休整二：回头确认来路',75)
segment('torch-coal',range(212,228),'休整三：低头看看镐',75)
segment('torch-coal',range(228,252),'换洞：走进石柱后面',50)
segment('fork-check',range(0,35),'下一段：走到木架岔路',50)
segment('fork-check',range(228,252),'绕过洞口，接着往里走',50)
segment('stairs-iron',range(8,53),'下一段：沿台阶下到水洞',50)
sample=Image.new('RGB',(250,161*9))
for i in range(9):
    sample.paste(stills[i*(len(stills)-1)//8].resize((250,161)),(0,i*161))
pal=sample.quantize(colors=256)
images=[f.quantize(palette=pal,dither=Image.Dither.NONE) for f in stills]
images[0].save(out,save_all=True,append_images=images[1:],duration=timings,loop=0,optimize=True)
with Image.open(out) as g:
    assert g.n_frames>=150
    print(g.n_frames,out.stat().st_size)
