"""Check authored whole-cel strips and actual body-part click coverage."""
from pathlib import Path
from statistics import median
from PIL import Image

assets = Path(__file__).resolve().parents[1] / "src/assets"
W, H, MIN_ALPHA = 192, 208, 32

def opened(name):
    with Image.open(assets / name) as im:
        return im.convert("RGBA")

def strip(name, columns, count=6):
    im = opened(name)
    rows=(count+columns-1)//columns
    assert im.size == (W * columns, H*rows), (name, im.size)
    def cell_at(i):
        x,y=(i%columns)*W,(i//columns)*H
        return im.crop((x,y,x+W,y+H))
    cells = [cell_at(i) for i in range(count)]
    for i, cell in enumerate(cells):
        box = cell.getbbox()
        assert box and 0 < box[0] < box[2] < W and 0 < box[1] < box[3] < H, (name, i, box)
    for i in range(count,columns*rows):
        assert cell_at(i).getbbox() is None,(name,i)
    return cells

def independent(a,b):
    assert (assets/a).read_bytes() != (assets/b).read_bytes(), (a,b)

for name in ("seat-note","seat-return-note","manuscript-entry","manuscript-offer","manuscript-book","manuscript-exit"):
    strip("kuroneko-interaction-"+name+".png",7)
for target in ("stand-cuff","stand-hair","stand-collar","stand-hand","seat-leftshoe","seat-rightshoe"):
    count=12 if target.startswith("stand-") else 8
    frames=strip("kuroneko-interaction-"+target+".png",7,count)
    assert len({c.tobytes() for c in frames})==count,target
independent("kuroneko-interaction-seat-note.png","kuroneko-interaction-seat-return-note.png")
independent("motion-kuroneko-handoff.png","motion-kuroneko-returnnote.png")
independent("motion-kuroneko-song.png","motion-kuroneko-listenbeat.png")
for name in ("song","rest","handoff","returnnote"):
    strip("motion-kuroneko-"+name+".png",6)
assert len({c.tobytes() for c in strip("motion-kuroneko-listenbeat.png",6,8)})==8
seated = strip("window-rest-kuroneko.png",7)
offered = strip("kuroneko-interaction-manuscript-offer.png",7)
look = opened("kuroneko.png")
assert look.size == (8*W,11*H)
gaze=[]
for i in range(16):
    x=(i%8)*W;y=(9+i//8)*H
    cel=look.crop((x,y,x+W,y+H))
    assert cel.getbbox() and cel.crop((0,178,W,197)).getbbox(), i
    gaze.append(cel.tobytes())
assert len(set(gaze))==16
swings=strip("window-swing-kuroneko.png",7,8)
torso=[c.getchannel("A").point(lambda p:255 if p>=96 else 0).crop((0,82,W,132)).getbbox() for c in swings]
assert max(b[0] for b in torso)-min(b[0] for b in torso)<=1
assert max(b[2] for b in torso)-min(b[2] for b in torso)<=1
for name,count,rows in (("window-lounge-kuroneko.png",12,2),("window-scene-kuroneko.png",12,2)):
    sheet=opened(name)
    assert sheet.size==(7*W,rows*H)
    for i in range(rows*7):
        x,y=(i%7)*W,(i//7)*H
        assert bool(sheet.crop((x,y,x+W,y+H)).getbbox())==(i<count),(name,i)
def heel_anchor(cell):
    a=cell.getchannel("A").point(lambda p:255 if p>=96 else 0)
    bottom=a.getbbox()[3]
    xs=[x for y in range(bottom-14,bottom) for x in range(W) if a.getpixel((x,y))]
    return median(xs),bottom

base=heel_anchor(seated[0])
lounges=opened("window-lounge-kuroneko.png")
for index in (0,5,6,11):
    x,y=(index%7)*W,(index//7)*H
    anchor=heel_anchor(lounges.crop((x,y,x+W,y+H)))
    assert abs(anchor[0]-base[0])<=2 and abs(anchor[1]-base[1])<=2,(index,base,anchor)
maps = {}
hands=[]
for line in (assets/"kuroneko-zones.txt").read_text().splitlines():
    layout, target, *bounds = line.split()
    x,y,w,h = map(int,bounds)
    box = (x,y,x+w,y+h)
    assert 0 <= x < box[2] <= W and 0 <= y < box[3] <= H
    maps.setdefault(layout, {})[target] = box
    if layout=="standing" and target=="Hand":hands.append(box)

def coverage(cell, box):
    return sum(1 for p in cell.getchannel("A").crop(box).getdata() if p>=MIN_ALPHA)

for i,cell in enumerate(seated):
    for target in ("LeftShoe","RightShoe"):
        assert coverage(cell,maps[f"seated-{i}"][target]) >= 170, (i,target)
for i,cell in enumerate(offered):
    assert coverage(cell,maps["offer-book"]["Manuscript"]) >= 900, i
for i,cell in enumerate(swings):
    for target in ("LeftShoe","RightShoe"):
        assert coverage(cell,maps[f"swing-{i}"][target])>=200,(i,target)
assert len(hands)==2 and hands[0][2]<hands[1][0]
for i in list(range(6))+list(range(72,88)):
    x,y=(i%8)*W,(i//8)*H
    cell=look.crop((x,y,x+W,y+H))
    for box in hands:
        def premul(v,a):return v*a//255
        skin=sum(1 for r,g,b,a in cell.crop(box).getdata()
                 if a>=112 and premul(r-b,a)>=16 and premul(g-b,a)>=4 and premul(r,a)>=92)
        assert skin>=25,(i,box,skin)
print("Kuroneko 80 authored gesture cels, gaze, steady swing torso and 78 hit-region checks passed.")
