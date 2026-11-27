"""Verify every released Steve sprite against the sampled 1.8 six-cuboid model."""
from hashlib import sha256
import json
from pathlib import Path
import sys

import numpy as np
from PIL import Image

repo=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(repo/"art/steve"))
import render_steve as rig
from classic_scenes import frames

def check():
    origin=json.loads((rig.HERE/"origin.json").read_text(encoding="utf-8"))
    assert sha256((rig.HERE/"skin.png").read_bytes()).hexdigest()==origin["skin_sha256"]
    assert rig.SIZES==((8,8,8),(8,12,4),(4,12,4),(4,12,4),(4,12,4),(4,12,4))
    assets=repo/"src/assets"
    sheets={}
    count=0
    for key,frame in frames():
        name,*fields=key.split(".")
        index=int(fields[-1])
        row=int(fields[0]) if name=="base" else 0
        path=assets/("steve.png" if name=="base" else "motion-steve-"+name+".png")
        if path not in sheets:
            with Image.open(path) as image:
                sheets[path]=image.convert("RGBA")
        pivots=rig.part_matrices(key,frame)
        assert len(pivots)==6, key
        for mat in pivots:
            basis=mat[:3,:3]
            assert np.allclose(basis.T@basis,np.eye(3)) and np.isclose(np.linalg.det(basis),1), key
        box=(index*192,row*208,(index+1)*192,(row+1)*208)
        frame_image=sheets[path].crop(box)
        expected=Image.new("RGBA",(192,208))
        expected.alpha_composite(rig.render(key,frame))
        assert frame_image.tobytes()==expected.tobytes(),key
        bounds=frame_image.getchannel("A").point(lambda v:255 if v>32 else 0).getbbox()
        assert bounds and bounds[0]>=2 and bounds[1]>=2 and bounds[2]<=190 and bounds[3]<=206,(key,bounds)
        count+=1
    print(f"{count} frames match the original model samples and fit within their cells")

if __name__=="__main__":
    check()
