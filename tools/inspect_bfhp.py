"""Read-only inspection of small HPFS files; never rewrites campaign archives."""
import argparse
import json
import re
import struct
from pathlib import Path

def inspect(path, catalog=None, payloads=None):
    data=path.read_bytes()
    if data[:4]!=b'HPFS' or struct.unpack_from('<I',data,4)[0]!=512:raise ValueError('Unknown BFHP format')
    result=[]
    seen=set()
    def read_node(node):
        if node<=0 or (node+1)*512>len(data):raise ValueError('Node out of bounds')
        size,level=struct.unpack_from('<II',data,node*512)
        if level>3:raise ValueError('Unsupported pointer depth')
        count=(size+511)//512
        def pointers(at,depth,needed,capacity):
            number=(needed+128**depth-1)//(128**depth)
            if number>capacity:raise ValueError('Pointer count exceeds capacity')
            result=[]
            for block in struct.unpack_from(f'<{number}I',data,at):
                if block==0 or block*512>=len(data):raise ValueError(f'Pointer out of bounds: node={node} size={size} level={level} pointer={block} at={at}')
                take=min(needed,128**depth)
                result.extend(pointers(block*512,depth-1,take,128) if depth else [block])
                needed-=take
            return result
        blocks=pointers(node*512+8,level,count,126)
        content=b''.join(data[block*512:(block+1)*512] for block in blocks)[:size]
        if len(content)!=size:raise ValueError('Truncated file contents')
        return size,content
    def walk(node,prefix=''):
        if node in seen:raise ValueError('Directory cycle')
        seen.add(node)
        size,content=read_node(node)
        if content is None:raise ValueError('Large directory unsupported')
        offset=0
        while offset<len(content):
            child,flags,length=struct.unpack_from('<IHH',content,offset)
            offset+=8
            name=content[offset:offset+length].decode('cp1252');offset+=length
            if len(name)!=length or not name:raise ValueError('Invalid name')
            rel=prefix+name
            child_size,child_data=read_node(child)
            if flags==1:walk(child,rel+'/')
            else:
                if payloads is not None:payloads[rel]=child_data
                markers=[] if child_data is None else re.findall(rb'\[\[([^\[\]]+)\]\]([\s\S]*?)\[\[\]\]',child_data)
                if catalog is not None and child_data is not None and rel.lower().endswith(('.xml','.vs','.txt','.ini')):
                    for id,source in markers:
                        id=id.decode('ascii')
                        try:source=source.decode('utf8')
                        except UnicodeDecodeError:source=''.join(bytes([c]).decode('cp1252') if c not in (0x81,0x8d,0x8f,0x90,0x9d) else chr(c) for c in source)
                        archive='adventures/'+path.name
                        catalog.append({'Archive':archive,'Path':rel,'Id':id,'Key':archive+'|'+rel+'|'+id,'Source':source,'Korean':''})
                result.append({'Path':rel,'Node':child,'Size':child_size,'Readable':child_data is not None,'Markers':len(markers)})
    walk(1)
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('path',type=Path);p.add_argument('--output',type=Path);p.add_argument('--catalog',type=Path);args=p.parse_args()
    catalog=[]
    result=inspect(args.path,catalog)
    if args.output:args.output.write_text(json.dumps(result,indent=2),encoding='utf8')
    if args.catalog:args.catalog.write_text(json.dumps(catalog,ensure_ascii=False),encoding='utf8')
    print('Files',len(result),'Markers',sum(x['Markers'] for x in result))
    print('Large text files',[x for x in result if not x['Readable'] and x['Path'].lower().endswith(('.xml','.vs','.txt','.ini'))])
    print('Marked files',[x for x in result if x['Markers']][:12])
