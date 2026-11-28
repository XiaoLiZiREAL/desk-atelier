"""Lossless per-cel residuals. Every decoded BGRA frame equals the authored PNG cel."""
from pathlib import Path
import struct,zlib
import numpy as np
from PIL import Image

W,H,N=192,208,252
assets=Path(__file__).resolve().parents[2]/'src'/'assets'
def zipped(data):
    pipe=zlib.compressobj(6,zlib.DEFLATED,-15)
    return pipe.compress(data)+pipe.flush()
def load_cells(path):
    with Image.open(path) as im:
        assert im.size==(1152,8736) and im.mode=='RGBA', path
        for i in range(N):
            x,y=i%6*W,i//6*H
            rgba=np.asarray(im.crop((x,y,x+W,y+H)))
            yield np.ascontiguousarray(rgba[:,:,[2,1,0,3]]).reshape(-1)
for path in sorted(assets.glob('motion-steve-focuscave-*.png')):
    cels=list(load_cells(path))
    first=cels[0]
    blocks=[zipped(first.tobytes())]
    blocks += [zipped(np.bitwise_xor(first,x).tobytes()) for x in cels]
    cursor=20+len(blocks)*8
    entries=[]
    for part in blocks:
        entries.append(struct.pack('<II',cursor,len(part)))
        cursor+=len(part)
    blob=b'DACAVE01'+struct.pack('<III',W,H,N)+b''.join(entries)+b''.join(blocks)
    for k,x in enumerate(cels,1):
        start,length=struct.unpack_from('<II',blob,20+8*k)
        delta=np.frombuffer(zlib.decompress(blob[start:start+length],-15),dtype=np.uint8)
        assert np.array_equal(np.bitwise_xor(first,delta),x)
    path.with_suffix('.dca').write_bytes(blob)
print('Pixel-for-pixel verified',len(list(assets.glob('motion-steve-focuscave-*.dca')))*N,'complete cels')
