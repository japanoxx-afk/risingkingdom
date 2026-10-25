"""Read original and patched PAK resources when investigating engine compatibility."""
import argparse
import struct
from pathlib import Path

def read_pak(path):
    b=path.read_bytes();count,size=struct.unpack_from('<II',b,32);at=40;previous=b'';result={}
    for _ in range(count):
        length,prefix=struct.unpack_from('<BB',b,at);at+=2
        name=previous[:prefix]+b[at:at+length-prefix];at+=length-prefix
        offset,length=struct.unpack_from('<II',b,at);at+=8
        result[name.decode('ascii')]=b[offset:offset+length];previous=name
    if at!=40+size:raise ValueError('Directory length mismatch')
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('game',type=Path);args=p.parse_args()
    original=read_pak(args.game/'.risingkingdom-ko/original/Packs/data.pak')
    patched=read_pak(args.game/'Packs/data.pak')
    for name,content in original.items():
        if name.lower().endswith('.ini') and (b'FrameTestY' in content or b'[Races]' in content):
            print(name);print(content.decode('utf8',errors='replace')[:12000])
        if name.lower() in ['data/subai/buildingupgrade.vs','data/subai/human_worker_behavior_repair.vs','data/subai/darkling_townhall_repair.vs']:
            print(name,'ORIGINAL');print(content.decode('utf8',errors='replace'))
            print(name,'PATCHED');print(patched[name].decode('utf8',errors='replace'))
