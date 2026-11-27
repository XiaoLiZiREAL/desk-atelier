"""Desktop scenes expressed as inputs to Minecraft Java 1.8's third-person biped."""
from dataclasses import dataclass
from math import cos, pi, sin

COUNTS = (6, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8)
ACTIONS = ("tea","game","song","focus","rest","mine","fish","fishcatch","pillar","handoff","returnnote","bowdraw","blockguard")

@dataclass
class Frame:
    age: float
    stride: float = 0
    amount: float = 0
    yaw: float = 0
    pitch: float = 0
    swing: float = 0
    sneak: bool = False
    turn: float = 0
    lift: float = 0
    x: float = 0
    item: str = ""
    scene: str = ""
    step: int = 0
    block: bool = False
    aim: bool = False

    def inputs(self):
        return (self.stride,self.amount,self.age,self.yaw,self.pitch,3 if self.block else int(bool(self.item)),
                str(self.sneak).lower(),self.swing,str(self.aim).lower())

def action(name,i):
    f=Frame(i*7+1,scene=name,step=i)
    if name in ("fish","fishcatch"):
        f.x=3
        f.item=("fishing_rod_uncast","fishing_rod_cast","fishing_rod_cast",
                "fishing_rod_cast","fishing_rod_uncast","fish_cod_raw" if name=="fishcatch" else "leather_boots")[i]
        f.swing=(0,.30,0,0,.53,0)[i]
        f.pitch=(9,-8,14,23,4,19)[i]
        f.yaw=(-8,-18,-28,-33,-20,-12)[i]
    elif name=="pillar":
        f.item=("dirt","dirt","dirt","iron_pickaxe","iron_pickaxe","dirt")[i]
        f.lift=(0,17,32,32,16,0)[i]
        f.pitch=(30,57,56,66,54,17)[i]
        f.sneak=i in (2,3,4)
        f.swing=(0,.40,.06,.49,.36,0)[i]
        f.yaw=(9,0,-9,-13,0,6)[i]
    elif name=="mine":
        f.x=3
        f.item="cobblestone" if i==5 else "iron_pickaxe"
        f.pitch=(16,26,38,46,52,20)[i]
        f.swing=(0,.16,.34,.62,.85,0)[i]
        f.yaw=(-10,-19,-24,-26,-18,-8)[i]
        f.sneak=i in (3,4)
    elif name in ("handoff","returnnote"):
        # Vanilla third-person inventory selection, look and use. The original note
        # contents are presented by the desktop UI rather than drawn on the item.
        f.item=(("","paper","map_filled","map_filled","paper","") if name=="handoff"
                else ("","paper","map_filled","map_filled","paper","paper"))[i]
        f.pitch=((6,20,33,27,13,1) if name=="handoff" else (20,26,18,3,-5,3))[i]
        f.yaw=(-15,-10,-5,-2,4,0)[i]
        f.swing=((0,.27,0,0,0,0) if name=="handoff" else (0,0,0,.21,.37,0))[i]
    elif name=="tea":
        f.item=("" if i in (0,5) else "potion_bottle_drinkable")
        f.pitch=(11,16,6,-7,2,5)[i]
        f.yaw=(4,0,-7,-8,-3,2)[i]
    elif name=="game":
        f.item=("" if i in (0,3) else "emerald")
        f.yaw=(-4,-17,-23,11,6,-9)[i]
        f.pitch=(0,12,17,21,8,-2)[i]
        f.sneak=i==3
    elif name=="focus":
        f.item="map_filled" if 0<i<5 else "map_empty"
        f.pitch=(6,15,27,34,22,10)[i]
        f.yaw=(-7,-12,-4,5,11,2)[i]
    elif name=="rest":
        f.sneak=i in (2,3,4)
        f.yaw=(0,11,17,6,-13,-3)[i]
        f.pitch=(3,9,15,18,13,2)[i]
    elif name=="bowdraw":
        f.item=("bow_standby","bow_pulling_0","bow_pulling_1","bow_pulling_2","bow_pulling_1","bow_standby")[i]
        f.aim=i in (1,2,3,4)
        f.yaw=(-7,-15,-18,-17,-12,-6)[i]
        f.pitch=(12,2,-4,-5,0,8)[i]
    elif name=="blockguard":
        f.item="iron_sword"
        f.block=i in (1,2,3,4)
        f.yaw=(-8,-12,-18,-5,4,-3)[i]
        f.pitch=(7,2,0,-4,1,5)[i]
    elif name=="song":
        f.age=i*10
        f.yaw=(0,5,8,3,-5,-7)[i]
        f.pitch=(0,2,3,1,-1,0)[i]
    return f

def base(row,i):
    f=Frame(i*8+.5)
    phase=i*2*pi/COUNTS[row]
    if row in (1,2):
        f.stride=phase/.6662
        f.amount=.86
        f.turn=67 if row==1 else -67
    elif row==3:
        f.swing=(.07,.24,.49,.80)[i]
    elif row==4:
        f.lift=(0,1.8,2.85,1.9,0)[i]
        f.stride=(.8,1.5,2.1,2.8,3.4)[i]
        f.amount=(.10,.25,.36,.24,.09)[i]
        f.pitch=(6,2,-4,1,5)[i]
    elif row==5:
        f.sneak=i in (2,3,4,5)
        f.pitch=13+11*(1-cos(phase))
        f.yaw=9*sin(phase)
    elif row==6:
        f.yaw=(-4,-11,-3,9,13,4)[i]
    elif row==7:
        return action("focus",i)
    elif row==8:
        f.pitch=18+7*sin(phase)
        f.yaw=18*cos(phase)
    elif row>=9:
        angle=(i+8*(row-9))*pi/8
        f.pitch=-23*cos(angle)
        f.yaw=34*sin(angle)
    return f

def frames():
    for row,count in enumerate(COUNTS):
        for i in range(count):
            yield f"base.{row}.{i}",base(row,i)
    for name in ACTIONS:
        for i in range(6):
            yield f"{name}.{i}",action(name,i)

if __name__=="__main__":
    import sys
    from pathlib import Path
    Path(sys.argv[1]).write_text("".join(key+" "+" ".join(map(str,f.inputs()))+"\n"
                                          for key,f in frames()),encoding="ascii")
