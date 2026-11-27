"""Regenerate the cached transforms and item artwork from the verified Java 1.8 client."""
import argparse
from hashlib import sha1, sha256
import json
import os
from pathlib import Path
import subprocess
import tempfile
from zipfile import ZipFile
from classic_scenes import frames

HERE=Path(__file__).resolve().parent
ITEMS=("paper","map_empty","map_filled","potion_bottle_drinkable","potion_overlay",
       "emerald","iron_pickaxe","iron_sword","bow_standby","bow_pulling_0","bow_pulling_1",
       "bow_pulling_2","fishing_rod_cast","fishing_rod_uncast","fish_cod_raw",
       "leather_boots","leather_boots_overlay")

def input_text():
    return "".join(key+" "+" ".join(map(str,frame.inputs()))+"\n" for key,frame in frames())

def sample(client,jars,jdk):
    origin=json.loads((HERE/"origin.json").read_text(encoding="utf-8"))
    if sha1(client.read_bytes()).hexdigest()!=origin["client_sha1"]:
        raise ValueError("Use the official Java Edition 1.8 release client.")
    source=input_text()
    with tempfile.TemporaryDirectory() as temp:
        root=Path(temp)
        (root/"inputs.txt").write_bytes(source.encode("ascii"))
        subprocess.run([str(jdk/"javac"),"-proc:none","-d",temp,str(HERE/"SampleClassic.java")],check=True)
        path=os.pathsep.join((temp,str(client.resolve()),str(jars.resolve()/"*")))
        subprocess.run([str(jdk/"java"),"-cp",path,"atelier.art.SampleClassic",
                        str(root/"inputs.txt"),str(root/"poses.txt")],check=True)
        poses=["# Java 1.8; inputs sha256 "+sha256(source.encode("ascii")).hexdigest()]
        for line in (root/"poses.txt").read_text().splitlines():
            name,*numbers=line.split()
            if len(numbers)!=36:
                raise ValueError("Incomplete biped transform")
            poses.append(name+" "+" ".join(format(float(n),".8g") for n in numbers))
        (HERE/"poses-1.8.txt").write_text("\n".join(poses)+"\n",encoding="ascii")
    dest=HERE/"items"
    dest.mkdir(exist_ok=True)
    with ZipFile(client) as jar:
        for name in ITEMS:
            (dest/(name+".png")).write_bytes(jar.read("assets/minecraft/textures/items/"+name+".png"))
        for name in ("dirt","cobblestone","stone","destroy_stage_7"):
            (dest/(name+".png")).write_bytes(jar.read("assets/minecraft/textures/blocks/"+name+".png"))
        aliases={"bow_standby":"bow","map_empty":"map","map_filled":"filled_map","fish_cod_raw":"cod",
                 "fishing_rod_uncast":"fishing_rod","potion_bottle_drinkable":"bottle_drinkable"}
        displays={}
        for name in sorted({frame.item for _,frame in frames() if frame.item}):
            model=aliases.get(name,name)
            data=json.loads(jar.read("assets/minecraft/models/item/"+model+".json"))
            displays[name]=data["display"]["thirdperson"]
        (dest/"display.json").write_text(json.dumps(displays,indent=2)+"\n",encoding="ascii")

if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--client",type=Path,required=True)
    parser.add_argument("--libraries",type=Path,required=True)
    parser.add_argument("--jdk-bin",type=Path,required=True)
    args=parser.parse_args()
    sample(args.client,args.libraries,args.jdk_bin)
