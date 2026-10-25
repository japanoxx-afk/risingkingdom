"""Read-only engine inspection for localization compatibility. No game writes."""
import argparse
import struct
import sys
import re
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'build' / 'analysis-deps'))
import capstone
import pefile

parser = argparse.ArgumentParser()
parser.add_argument('exe', type=Path)
parser.add_argument('--address', type=lambda v: int(v, 0))
parser.add_argument('--size', type=lambda v: int(v, 0), default=400)
args = parser.parse_args()
data = args.exe.read_bytes()
pe = pefile.PE(data=data)
base = pe.OPTIONAL_HEADER.ImageBase
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)

def dump(address, size):
    offset = pe.get_offset_from_rva(address-base)
    for i in md.disasm(data[offset:offset+size], address):
        annotations=[]
        for match in re.finditer(r'0x([0-9a-f]+)',i.op_str):
            value=int(match[1],16)
            if base <= value < base+pe.OPTIONAL_HEADER.SizeOfImage:
                try:
                    target=pe.get_offset_from_rva(value-base)
                    raw=data[target:target+120].split(b'\0')[0]
                    if raw and all(32<=c<127 for c in raw): annotations.append(repr(raw.decode('ascii')))
                except pefile.PEFormatError: pass
        print(f'{i.address:08x}: {i.mnemonic:8s} {i.op_str}' + (' ; '+', '.join(annotations) if annotations else ''))

if args.address:
    dump(args.address, args.size)
else:
    for needle in [b'game.loc.xml', b'translationtableentry', b'local/fonts/', b'ABCF', b'COffFont', b'CBMPFont']:
        start = 0
        while True:
            offset = data.find(needle, start)
            if offset < 0:
                break
            address = pe.get_rva_from_offset(offset)+base
            print(f'\n{needle!r} at {address:08x}')
            pattern = struct.pack('<I', address)
            pos = 0
            while True:
                pos = data.find(pattern, pos)
                if pos < 0:
                    break
                xref = pe.get_rva_from_offset(pos)+base
                print(f'  reference {xref:08x}')
                if pe.get_section_by_rva(xref-base).Characteristics & 0x20000000:
                    dump(xref-12, 65)
                pos += 4
            start = offset+len(needle)
